using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
namespace MicControl {
enum Flow { Capture, Render }
sealed class AudioDevice { internal string Id,Name; internal bool IsDefault; }
struct EndpointState { internal bool Present; internal string Id,Name; internal float Volume; internal bool Muted; }
// Audio engine over Core Audio: devices, the default endpoint of each flow (role console), volume, mute, volume locks, Listen, meters.
// Every member is called on the UI thread, the thread `post` delivers to. COM callbacks arrive on worker threads: they only set
// flags and post one coalesced Process, which does the COM work (re-registration, lock enforcement) on the UI thread.
// Reads never throw (State.Present=false, Peak 0, Listening null); a failed write throws and leaves the hub usable.
sealed class AudioHub : IDisposable {
    static readonly Guid Own=new Guid("6D1C5A0E-4B7F-4C1E-9A53-2F0B7E8D4C61"); // event context of every write the hub makes
    // "Listen to this device" (Listen tab of the microphone properties): pid 1 = enabled (VT_BOOL), pid 0 = target render id.
    internal static readonly PropertyKey ListenEnabled=new PropertyKey(new Guid("24DBB0FC-9311-4B3D-9CF0-18FF155639D4"),1);
    const int DevicesDirty=1,ForeignCapture=2,ForeignRender=4,Notify=8,Tick=16;
    const float Tolerance=0.0051f; // a lock restores when the level differs from percent/100 by more than 0.005 (plus float margin)
    sealed class Endpoint { internal string Id,Name; internal IMMDevice Device; internal IAudioEndpointVolume Volume; internal IAudioMeterInformation Meter; internal VolumeCallback Callback; internal bool Registered; }
    readonly Action<Action> post; readonly object gate=new object(); int pending; bool scheduled,disposed;
    IMMDeviceEnumerator enumerator; readonly DeviceClient client; bool clientRegistered; Timer timer;
    readonly Endpoint[] endpoints=new Endpoint[2]; Dictionary<string,int> locks=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    // The id of a default endpoint that failed to open or to read: retried on the 1 s tick or a device notification, never on a read.
    readonly string[] failed=new string[2]; readonly bool[] lockFailing=new bool[2];
    IAudioClient stream; bool metering;
    internal event Action Changed; internal event Action<Flow> LockRestored,LockFailed; internal event Action LocksChanged;
    // Test seam: a step name ("open capture", "read render", "role 1", "restore capture", "stream") for which the predicate
    // returns true fails as the Windows call would. Null in the product.
    internal Predicate<string> Fault; internal int StreamOpens;
    internal AudioHub(Action<Action> post) {
        if(post==null)throw new ArgumentNullException("post"); this.post=post; client=new DeviceClient(this);
        try { enumerator=(IMMDeviceEnumerator)new MMDeviceEnumerator(); clientRegistered=enumerator.RegisterEndpointNotificationCallback(client)==0; } catch { enumerator=null; }
        Revalidate(Flow.Capture,true); Revalidate(Flow.Render,true);
        timer=new Timer(delegate { Signal(Tick); },null,1000,1000);
    }
    public void Dispose() {
        lock(gate) { if(disposed)return; disposed=true; }
        timer.Dispose(); StopStream(); Close(Flow.Capture); Close(Flow.Render);
        if(enumerator!=null) { if(clientRegistered)try { enumerator.UnregisterEndpointNotificationCallback(client); } catch { } Com.Release(enumerator); enumerator=null; }
    }
    // ---- devices and state
    internal List<AudioDevice> Devices(Flow flow) {
        List<AudioDevice> list=new List<AudioDevice>(); if(disposed||enumerator==null)return list; string def=DefaultId(flow); IMMDeviceCollection c=null;
        try {
            if(enumerator.EnumAudioEndpoints(DataFlow(flow),Com.StateActive,out c)!=0||c==null)return list; int n; if(c.GetCount(out n)!=0)return list;
            for(int i=0;i<n;i++) { IMMDevice d=null; try { string id; if(c.Item(i,out d)!=0||d.GetId(out id)!=0)continue; list.Add(new AudioDevice { Id=id,Name=Name(d),IsDefault=Same(id,def) }); } catch { } finally { Com.Release(d); } }
        } catch { } finally { Com.Release(c); }
        return list;
    }
    internal EndpointState State(Flow flow) {
        EndpointState s=new EndpointState(); Endpoint e=Current(flow); if(e==null)return s;
        try { Inject(flow,"read"); float v; bool m; Com.Check(e.Volume.GetMasterVolumeLevelScalar(out v)); Com.Check(e.Volume.GetMute(out m)); s.Present=true; s.Id=e.Id; s.Name=e.Name; s.Volume=v; s.Muted=m; }
        catch { Drop(flow); s=new EndpointState(); }
        return s;
    }
    // The device becomes the default of all three roles (console, multimedia, communications): a voice app reads the
    // communications default, so a split would leave it on the old device. A failed role puts back the roles already changed.
    internal void SetDefault(Flow flow,string id) {
        if(string.IsNullOrEmpty(id))throw new ArgumentException("No device id.","id");
        bool known=false; foreach(AudioDevice d in Devices(flow))if(Same(d.Id,id))known=true;
        if(!known)throw new ArgumentException("The device is not an active "+(flow==Flow.Render?"playback":"recording")+" device.","id");
        string[] before=new string[3]; for(int role=0;role<3;role++)before[role]=DefaultId(flow,role);
        try { SetRoles(id,before,delegate(string device,int role) { if(Fault!=null&&Fault("role "+role))throw new COMException("Injected fault: role "+role,unchecked((int)0x80004005)); Policy.SetDefault(device,role); }); }
        finally { Revalidate(flow,true); }
    }
    // Writes id to roles 0..2; on a failure writes back every role already changed (best effort) and rethrows.
    internal static void SetRoles(string id,string[] before,Action<string,int> write) {
        int role=0;
        try { for(;role<3;role++)write(id,role); }
        catch { for(int r=role-1;r>=0;r--)if(before[r]!=null&&!Same(before[r],id))try { write(before[r],r); } catch { } throw; }
    }
    // ---- volume, mute, locks
    internal void SetVolume(Flow flow,float scalar) {
        if(float.IsNaN(scalar))throw new ArgumentOutOfRangeException("scalar"); scalar=Math.Max(0f,Math.Min(1f,scalar)); Endpoint e=Require(flow);
        try {
            Guid g=Own; Com.Check(e.Volume.SetMasterVolumeLevelScalar(scalar,ref g));
            int old; if(locks.TryGetValue(e.Id,out old)) { float v; Com.Check(e.Volume.GetMasterVolumeLevelScalar(out v)); int p=Percent(v); if(p!=old) { locks[e.Id]=p; Raise(LocksChanged); } }
        } catch { Drop(flow); throw; }
    }
    internal void SetMute(Flow flow,bool mute) { Endpoint e=Require(flow); try { Guid g=Own; Com.Check(e.Volume.SetMute(mute,ref g)); } catch { Drop(flow); throw; } }
    internal bool ToggleMute(Flow flow) {
        Endpoint e=Require(flow);
        try { bool m; Com.Check(e.Volume.GetMute(out m)); Guid g=Own; Com.Check(e.Volume.SetMute(!m,ref g)); Com.Check(e.Volume.GetMute(out m)); return m; } catch { Drop(flow); throw; }
    }
    internal bool IsLocked(Flow flow) { Endpoint e=Current(flow); return e!=null&&locks.ContainsKey(e.Id); }
    internal void SetLock(Flow flow,bool on) {
        if(on) {
            Endpoint e=Require(flow); float v; try { Com.Check(e.Volume.GetMasterVolumeLevelScalar(out v)); } catch { Drop(flow); throw; }
            int p=Percent(v),old; if(!locks.TryGetValue(e.Id,out old)||old!=p) { locks[e.Id]=p; Raise(LocksChanged); }
        } else { Endpoint e=Current(flow); if(e!=null&&locks.Remove(e.Id))Raise(LocksChanged); }
    }
    internal Dictionary<string,int> Locks {
        get { return new Dictionary<string,int>(locks,StringComparer.OrdinalIgnoreCase); }
        set {
            Dictionary<string,int> next=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            if(value!=null)foreach(KeyValuePair<string,int> pair in value)if(!string.IsNullOrEmpty(pair.Key)&&pair.Value>=0&&pair.Value<=100)next[pair.Key]=pair.Value;
            locks=next; Enforce(Flow.Capture); Enforce(Flow.Render);
        }
    }
    // ---- Listen to this device, written through the audio service (IPolicyConfig), so no elevation is needed
    internal bool? Listening {
        get {
            Endpoint e=Current(Flow.Capture); if(e==null)return null; IPropertyStore s=null; PropVariant v=new PropVariant();
            try { if(e.Device.OpenPropertyStore(0,out s)!=0||s==null)return null; PropertyKey k=ListenEnabled; if(s.GetValue(ref k,out v)!=0)return null; return v.Type==PropVariant.Empty?false:v.AsBool(); }
            catch { return null; } finally { Com.PropVariantClear(ref v); Com.Release(s); }
        }
    }
    internal void SetListening(bool on) {
        Endpoint e=Require(Flow.Capture); Policy.SetProperty(e.Id,ListenEnabled,PropVariant.FromBool(on));
        if(Listening!=on)throw new InvalidOperationException("Windows did not keep the Listen setting.");
    }
    // ---- meters
    internal void SetMetering(bool on) { metering=on; if(on) { if(stream==null)StartStream(); } else StopStream(); }
    internal bool MeterStreamOpen { get { return stream!=null; } }
    internal float Peak(Flow flow) {
        Endpoint e=disposed?null:endpoints[(int)flow]; if(e==null||e.Meter==null)return 0f; float p;
        try { if(e.Meter.GetPeakValue(out p)!=0)return 0f; } catch { return 0f; }
        return p>0f&&p<=1f?p:p>1f?1f:0f;
    }
    // A capture endpoint without a hardware meter reads 0 unless a capture stream runs: a shared-mode stream whose data is ignored.
    void StartStream() {
        Endpoint e=Current(Flow.Capture); if(e==null)return; IAudioClient c=null; IntPtr format=IntPtr.Zero;
        try { c=Com.Activate<IAudioClient>(e.Device); Com.Check(c.GetMixFormat(out format)); Com.Check(c.Initialize(0,0,10000000,0,format,IntPtr.Zero)); Com.Check(c.Start()); stream=c; c=null; StreamOpens++; }
        catch { } finally { if(format!=IntPtr.Zero)Marshal.FreeCoTaskMem(format); Com.Release(c); }
    }
    void StopStream() { IAudioClient c=stream; stream=null; if(c==null)return; try { c.Stop(); } catch { } Com.Release(c); }
    // A stream invalidated under the same default id (format change, exclusive use by another app) fails GetCurrentPadding.
    bool StreamAlive() { try { if(Fault!=null&&Fault("stream"))return false; uint frames; return stream.GetCurrentPadding(out frames)==0; } catch { return false; } }
    // ---- endpoint bookkeeping (UI thread)
    Endpoint Current(Flow flow) { Revalidate(flow,false); return endpoints[(int)flow]; }
    Endpoint Require(Flow flow) { Endpoint e=Current(flow); if(e==null)throw new InvalidOperationException("No default "+(flow==Flow.Render?"playback":"recording")+" device."); return e; }
    // Follows the default endpoint: reopens it when the default changed, then enforces its lock and moves the meter stream.
    // A default that failed before is opened again only with retry (tick, device notification, own default change), so a
    // device that cannot be opened costs one attempt a second. Changed is raised only when the open endpoint really changed.
    bool Revalidate(Flow flow,bool retry) {
        if(disposed||enumerator==null)return false; int i=(int)flow; string id=DefaultId(flow); Endpoint e=endpoints[i];
        if(e!=null&&Same(e.Id,id))return false;
        if(e==null&&(id==null||(!retry&&Same(failed[i],id)))) { if(id==null)failed[i]=null; return false; }
        string was=e!=null?e.Id:null; Close(flow); failed[i]=null;
        if(id!=null) { endpoints[i]=Open(flow); if(endpoints[i]==null)failed[i]=id; }
        Endpoint now=endpoints[i]; if(now==null&&was==null)return false;
        Enforce(flow); if(flow==Flow.Capture&&metering) { StopStream(); StartStream(); }
        if(now==null||!Same(was,now.Id))Signal(Notify); return true;
    }
    Endpoint Open(Flow flow) {
        Endpoint e=new Endpoint(); IMMDevice d;
        try {
            if(enumerator.GetDefaultAudioEndpoint(DataFlow(flow),0,out d)!=0||d==null)return null; e.Device=d; Com.Check(d.GetId(out e.Id)); e.Name=Name(d);
            Inject(flow,"open"); e.Volume=Com.Activate<IAudioEndpointVolume>(d); try { e.Meter=Com.Activate<IAudioMeterInformation>(d); } catch { e.Meter=null; }
            e.Callback=new VolumeCallback(this,flow); Com.Check(e.Volume.RegisterControlChangeNotify(e.Callback)); e.Registered=true; return e;
        } catch { Release(e); return null; }
    }
    void Close(Flow flow) { Endpoint e=endpoints[(int)flow]; endpoints[(int)flow]=null; Release(e); }
    // Closes an endpoint whose call failed and remembers its id, so reads do not reopen it before the next retry.
    void Drop(Flow flow) { Endpoint e=endpoints[(int)flow]; if(e!=null)failed[(int)flow]=e.Id; Close(flow); }
    void Inject(Flow flow,string step) { Predicate<string> f=Fault; string name=step+(flow==Flow.Render?" render":" capture"); if(f!=null&&f(name))throw new COMException("Injected fault: "+name,unchecked((int)0x80004005)); }
    static void Release(Endpoint e) {
        if(e==null)return; if(e.Registered)try { e.Volume.UnregisterControlChangeNotify(e.Callback); } catch { }
        Com.Release(e.Meter); Com.Release(e.Volume); Com.Release(e.Device);
    }
    // Restores a held level. A restore that fails raises LockFailed once; the next success or a level already in place ends the streak.
    bool Enforce(Flow flow) {
        int i=(int)flow; Endpoint e=endpoints[i]; int p; if(e==null||!locks.TryGetValue(e.Id,out p)) { lockFailing[i]=false; return false; }
        bool ok;
        try {
            float v,level=p/100f; if(e.Volume.GetMasterVolumeLevelScalar(out v)!=0)return false;
            if(Math.Abs(v-level)<=Tolerance) { lockFailing[i]=false; return false; }
            Inject(flow,"restore"); Guid g=Own; ok=e.Volume.SetMasterVolumeLevelScalar(level,ref g)==0;
        } catch { ok=false; }
        if(!ok) { if(!lockFailing[i]) { lockFailing[i]=true; Action<Flow> f=LockFailed; if(f!=null)post(delegate { if(!disposed)f(flow); }); } return false; }
        lockFailing[i]=false; Action<Flow> h=LockRestored; if(h!=null)post(delegate { if(!disposed)h(flow); }); return true;
    }
    // ---- notifications: callbacks set bits, one Process runs on the UI thread for any number of them
    void Signal(int bits) {
        bool go; lock(gate) { if(disposed)return; pending|=bits; go=!scheduled; scheduled=true; }
        if(go)try { post(Process); } catch { lock(gate)scheduled=false; }
    }
    void Process() {
        int f; lock(gate) { f=pending; pending=0; scheduled=false; } if(disposed)return;
        if((f&(DevicesDirty|Tick))!=0) { Revalidate(Flow.Capture,true); Revalidate(Flow.Render,true); }
        if((f&(ForeignCapture|Tick))!=0)Enforce(Flow.Capture);
        if((f&(ForeignRender|Tick))!=0)Enforce(Flow.Render);
        if((f&Tick)!=0&&metering) { if(stream!=null&&!StreamAlive())StopStream(); if(stream==null)StartStream(); }
        if((f&(Notify|DevicesDirty))!=0) { Action h=Changed; if(h!=null)h(); }
    }
    void Raise(Action a) { if(a!=null)post(delegate { if(!disposed)a(); }); }
    string DefaultId(Flow flow) { return DefaultId(flow,0); }
    string DefaultId(Flow flow,int role) {
        if(enumerator==null)return null; IMMDevice d=null;
        try { string id; if(enumerator.GetDefaultAudioEndpoint(DataFlow(flow),role,out d)!=0||d==null||d.GetId(out id)!=0)return null; return id; } catch { return null; } finally { Com.Release(d); }
    }
    static string Name(IMMDevice d) {
        IPropertyStore s=null; PropVariant v=new PropVariant();
        try { PropertyKey k=Com.FriendlyName; if(d.OpenPropertyStore(0,out s)==0&&s!=null&&s.GetValue(ref k,out v)==0) { string n=v.AsString(); if(!string.IsNullOrEmpty(n))return n; } } catch { }
        finally { Com.PropVariantClear(ref v); Com.Release(s); }
        return "Unknown device";
    }
    static int DataFlow(Flow flow) { return flow==Flow.Render?0:1; }
    static bool Same(string a,string b) { return a!=null&&b!=null&&string.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
    internal static int Percent(float v) { return (int)Math.Round(Math.Max(0f,Math.Min(1f,v))*100,MidpointRounding.AwayFromZero); }
    sealed class DeviceClient : IMMNotificationClient {
        readonly AudioHub hub; internal DeviceClient(AudioHub hub) { this.hub=hub; }
        public int OnDeviceStateChanged(string id,int state) { hub.Signal(DevicesDirty|Notify); return 0; }
        public int OnDeviceAdded(string id) { hub.Signal(DevicesDirty|Notify); return 0; }
        public int OnDeviceRemoved(string id) { hub.Signal(DevicesDirty|Notify); return 0; }
        public int OnDefaultDeviceChanged(int flow,int role,string id) { hub.Signal(DevicesDirty|Notify); return 0; }
        public int OnPropertyValueChanged(string id,PropertyKey key) { hub.Signal(Notify); return 0; }
    }
    sealed class VolumeCallback : IAudioEndpointVolumeCallback {
        readonly AudioHub hub; readonly Flow flow; internal VolumeCallback(AudioHub hub,Flow flow) { this.hub=hub; this.flow=flow; }
        // AUDIO_VOLUME_NOTIFICATION_DATA starts with the event context GUID of the write.
        public int OnNotify(IntPtr data) {
            try { Guid context=data==IntPtr.Zero?Guid.Empty:(Guid)Marshal.PtrToStructure(data,typeof(Guid)); hub.Signal(context==Own?Notify:Notify|(flow==Flow.Render?ForeignRender:ForeignCapture)); } catch { }
            return 0;
        }
    }
}
}
