using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
namespace MicControl {
// Self-update from GitHub Releases. Contract: docs\SELF_UPDATE.md. No WPF or WinForms here: the UI subscribes to
// OnStatus / OnToast and marshals to its own thread; the unit tests in tests\ run this file without a window.
enum UpdateStage { Idle, Checking, UpToDate, Available, Downloading, Ready, Error }
class UpdateStatus {
    internal UpdateStage Stage=UpdateStage.Idle; internal string Current,Latest,Message="Updates are not checked yet"; internal long Size=-1; internal double Progress; internal bool CanApply;
    internal UpdateStatus Copy() { return (UpdateStatus)MemberwiseClone(); }
}
class UpdateConfig {
    internal string Repo,AssetName,AppName,Version,UpdatesDir,ExePath,ReleaseUrlVariable,SumsName="SHA256SUMS";
    internal string Stem { get { return Path.GetFileNameWithoutExtension(AssetName); } }
    internal string Extension { get { return Path.GetExtension(AssetName); } }
}
class Release { internal string Tag,Version,AssetUrl,SumsUrl; internal long Size=-1; }
static class SemVer {
    // Numeric X.Y.Z prefix of a tag ("v1.2.3-beta.1" -> 1,2,3); missing parts are zero; null when no digits lead.
    internal static int[] Parse(string text) {
        if(text==null)return null; text=text.Trim(); if(text.StartsWith("v")||text.StartsWith("V"))text=text.Substring(1);
        Match m=Regex.Match(text,@"^(\d+)(?:\.(\d+))?(?:\.(\d+))?"); if(!m.Success)return null;
        int[] parts=new int[3];
        for(int i=0;i<3;i++) { if(!m.Groups[i+1].Success)continue; int value; if(!int.TryParse(m.Groups[i+1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out value))return null; parts[i]=value; }
        return parts;
    }
    internal static int Compare(int[] a,int[] b) { for(int i=0;i<3;i++)if(a[i]!=b[i])return a[i]<b[i]?-1:1; return 0; }
    internal static bool IsNewer(string candidate,string current) { int[] a=Parse(candidate),b=Parse(current); return a!=null&&b!=null&&Compare(a,b)>0; }
    internal static string Format(int[] v) { return v[0]+"."+v[1]+"."+v[2]; }
}
static class Checksums {
    // SHA256SUMS line: "<64 hex>  <name>" (a "*" before the name marks binary mode in sha256sum output).
    internal static string Find(string text,string asset) {
        if(text==null)return null;
        foreach(string raw in text.Split('\n')) {
            string line=raw.Trim(); if(line.Length==0||line.StartsWith("#"))continue;
            Match m=Regex.Match(line,@"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if(m.Success&&string.Equals(m.Groups[2].Value.Trim(),asset,StringComparison.OrdinalIgnoreCase))return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }
    internal static string Sha256File(string path) { using(SHA256 sha=SHA256.Create())using(FileStream f=File.OpenRead(path))return Hex(sha.ComputeHash(f)); }
    internal static string Hex(byte[] hash) { StringBuilder b=new StringBuilder(hash.Length*2); foreach(byte x in hash)b.Append(x.ToString("x2")); return b.ToString(); }
}
static class UpdateSchedule {
    // Wait before the next periodic check: 5 minutes normally; after failed checks it doubles (10, 20, 40) up to 60 minutes.
    internal static TimeSpan Wait(int failures) { int minutes=5; for(int i=0;i<failures&&minutes<60;i++)minutes*=2; return TimeSpan.FromMinutes(Math.Min(minutes,60)); }
    // False while Windows reports no network connection (a periodic check is then skipped, not counted as a failure).
    internal static bool HasConnection() { try { int flags; return InternetGetConnectedState(out flags,0); } catch { return true; } }
    [DllImport("wininet.dll")] static extern bool InternetGetConnectedState(out int flags,int reserved);
}
enum NetFailure { Offline, Limit, NotFound, Other }
class NetException : Exception { internal NetFailure Failure; internal NetException(NetFailure failure,string message) : base(message) { Failure=failure; } }
interface IUpdateNet {
    string RedirectLocation(string url);   // HEAD without following redirects; null when the answer is not a redirect
    long ContentLength(string url);        // HEAD following redirects; -1 when unknown
    string GetText(string url);
    string Download(string url,string destination,Action<long,long> progress); // streams to destination, returns SHA-256 hex
}
class WebNet : IUpdateNet {
    readonly string userAgent;
    static WebNet() { try { ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072|(SecurityProtocolType)12288; } catch(NotSupportedException) { ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072; } }
    internal WebNet(string userAgent) { this.userAgent=userAgent; }
    HttpWebRequest Create(string url,string method,bool follow) {
        HttpWebRequest r=(HttpWebRequest)WebRequest.Create(url); r.Method=method; r.AllowAutoRedirect=follow; r.UserAgent=userAgent; r.Timeout=20000; r.ReadWriteTimeout=30000; return r;
    }
    static NetException Map(WebException e) {
        HttpWebResponse response=e.Response as HttpWebResponse;
        if(response!=null) { int code=(int)response.StatusCode; response.Close(); return new NetException(code==404?NetFailure.NotFound:code==403||code==429?NetFailure.Limit:NetFailure.Other,"HTTP "+code); }
        return new NetException(e.Status==WebExceptionStatus.ProtocolError?NetFailure.Other:NetFailure.Offline,e.Status.ToString());
    }
    static T Retry<T>(Func<T> action) {
        for(int attempt=1;;attempt++) {
            try { return action(); }
            catch(WebException e) { NetException n=Map(e); if(attempt>=2||n.Failure==NetFailure.NotFound||n.Failure==NetFailure.Limit)throw n; }
            catch(IOException) { if(attempt>=2)throw new NetException(NetFailure.Offline,"connection lost"); }
            Thread.Sleep(1500);
        }
    }
    public string RedirectLocation(string url) {
        return Retry(delegate {
            using(HttpWebResponse r=(HttpWebResponse)Create(url,"HEAD",false).GetResponse()) {
                int code=(int)r.StatusCode; string location=r.Headers["Location"];
                if(code<300||code>=400||string.IsNullOrEmpty(location))return (string)null;
                return new Uri(new Uri(url),location).AbsoluteUri;
            }
        });
    }
    public long ContentLength(string url) { return Retry(delegate { using(HttpWebResponse r=(HttpWebResponse)Create(url,"HEAD",true).GetResponse())return r.ContentLength; }); }
    public string GetText(string url) {
        return Retry(delegate { using(HttpWebResponse r=(HttpWebResponse)Create(url,"GET",true).GetResponse())using(StreamReader s=new StreamReader(r.GetResponseStream(),Encoding.UTF8))return s.ReadToEnd(); });
    }
    public string Download(string url,string destination,Action<long,long> progress) {
        try {
            using(HttpWebResponse r=(HttpWebResponse)Create(url,"GET",true).GetResponse())
            using(Stream input=r.GetResponseStream())
            using(FileStream output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None))
            using(SHA256 sha=SHA256.Create()) {
                long total=r.ContentLength,done=0; byte[] buffer=new byte[65536]; int read;
                while((read=ReadSome(input,buffer))>0) { output.Write(buffer,0,read); sha.TransformBlock(buffer,0,read,null,0); done+=read; if(progress!=null)progress(done,total); }
                sha.TransformFinalBlock(buffer,0,0); return Checksums.Hex(sha.Hash);
            }
        } catch(WebException e) { throw Map(e); }
    }
    // Network read errors become NetException; disk write errors stay IOException for the caller.
    static int ReadSome(Stream input,byte[] buffer) { try { return input.Read(buffer,0,buffer.Length); } catch(IOException) { throw new NetException(NetFailure.Offline,"connection lost"); } catch(WebException e) { throw Map(e); } }
}
class UpdateError : Exception { internal UpdateError(string message) : base(message) { } }
class Updater {
    internal static class Messages {
        internal const string Checking="Checking for updates…",Offline="No internet connection",Limit="GitHub limited the request, try again later",
            NoChecksum="The release has no checksum file",Damaged="The downloaded file is damaged (checksum mismatch)",BadVersion="Could not read the release version",
            NoRelease="No release has been published yet",SaveFailed="Could not save the update file",StartFailed="Could not start the update helper",
            NotReady="The update is not ready to install",Missing="The downloaded update file is missing",Failed="The update check failed";
    }
    internal Action<UpdateStatus> OnStatus; internal Action<string> OnToast; internal Func<bool> AutoDownload; // read once per check that finds a newer release
    internal Func<string,string,bool> Spawn=DefaultSpawn; // (exe, arguments) -> started
    readonly UpdateConfig config; readonly IUpdateNet net; readonly object gate=new object();
    UpdateStatus status=new UpdateStatus(); Release release; string expectedSha; int busy;
    internal Updater(UpdateConfig config,IUpdateNet net) {
        this.config=config; this.net=net??new WebNet(config.Stem+"/"+config.Version); status.Current=config.Version;
    }
    internal UpdateStatus Status { get { lock(gate)return status.Copy(); } }
    internal string ReleasePage { get { return "https://github.com/"+config.Repo+"/releases/latest"; } }
    internal string FileFor(string version) { return Path.Combine(config.UpdatesDir,config.Stem+"-"+version+config.Extension); }
    string MarkerPath { get { return Path.Combine(config.UpdatesDir,"applied.json"); } }

    void Set(UpdateStage stage,string message,double progress) {
        UpdateStatus copy;
        lock(gate) {
            status.Stage=stage; status.Message=message; status.Progress=progress;
            status.Latest=release!=null?release.Version:null; status.Size=release!=null?release.Size:-1;
            status.CanApply=stage==UpdateStage.Ready&&!string.IsNullOrEmpty(config.ExePath); copy=status.Copy();
        }
        Action<UpdateStatus> handler=OnStatus; if(handler!=null) { try { handler(copy); } catch { } }
    }
    bool Enter() { return Interlocked.CompareExchange(ref busy,1,0)==0; }
    void Leave() { Interlocked.Exchange(ref busy,0); }
    internal bool Busy { get { return busy!=0; } }
    internal void CheckInBackground() { if(Enter())ThreadPool.QueueUserWorkItem(delegate { try { CheckCore(); } finally { Leave(); } }); }
    internal void DownloadInBackground() { if(Enter())ThreadPool.QueueUserWorkItem(delegate { try { DownloadCore(); } finally { Leave(); } }); }
    internal void Check() { if(!Enter())return; try { CheckCore(); } finally { Leave(); } }
    internal void Download() { if(!Enter())return; try { DownloadCore(); } finally { Leave(); } }

    static string MessageFor(NetException e) { return e.Failure==NetFailure.Limit?Messages.Limit:e.Failure==NetFailure.Offline?Messages.Offline:Messages.Failed; }

    void CheckCore() {
        lock(gate) { release=null; expectedSha=null; }
        Set(UpdateStage.Checking,Messages.Checking,0);
        try {
            Release found=Resolve();
            if(!SemVer.IsNewer(found.Version,config.Version)) { Set(UpdateStage.UpToDate,config.AppName+" is up to date",0); return; }
            if(found.Size<0&&found.AssetUrl!=null) {
                try { found.Size=net.ContentLength(found.AssetUrl); }
                catch(NetException e) { if(e.Failure==NetFailure.NotFound)throw new UpdateError("The release has no "+config.AssetName); throw; }
            }
            if(found.AssetUrl==null)throw new UpdateError("The release has no "+config.AssetName);
            lock(gate)release=found;
            if(AlreadyDownloaded(found)) { Set(UpdateStage.Ready,"Version "+found.Version+" is ready to install",1); return; }
            Set(UpdateStage.Available,"Version "+found.Version+" is available",0);
            Func<bool> auto=AutoDownload; if(auto!=null&&auto())DownloadCore();
        }
        catch(UpdateError e) { Set(UpdateStage.Error,e.Message,0); }
        catch(NetException e) { Set(UpdateStage.Error,MessageFor(e),0); }
        catch(Exception) { Set(UpdateStage.Error,Messages.Failed,0); }
    }
    Release Resolve() {
        string overrideUrl=string.IsNullOrEmpty(config.ReleaseUrlVariable)?null:Environment.GetEnvironmentVariable(config.ReleaseUrlVariable);
        Release r=string.IsNullOrEmpty(overrideUrl)?FromRedirect():FromDocument(overrideUrl);
        int[] v=SemVer.Parse(r.Tag); if(v==null)throw new UpdateError(Messages.BadVersion);
        r.Version=SemVer.Format(v); return r;
    }
    // Default source: HEAD /releases/latest redirects to /releases/tag/<tag>; no API quota is used.
    Release FromRedirect() {
        string location;
        try { location=net.RedirectLocation("https://github.com/"+config.Repo+"/releases/latest"); }
        catch(NetException e) { if(e.Failure==NetFailure.NotFound)throw new UpdateError(Messages.NoRelease); throw; }
        const string marker="/releases/tag/"; int at=location==null?-1:location.IndexOf(marker,StringComparison.Ordinal);
        if(at<0)throw new UpdateError(Messages.NoRelease);
        string tag=location.Substring(at+marker.Length); int cut=tag.IndexOfAny(new char[]{'?','#','/'}); if(cut>=0)tag=tag.Substring(0,cut);
        tag=Uri.UnescapeDataString(tag); if(tag.Length==0)throw new UpdateError(Messages.NoRelease);
        string baseUrl="https://github.com/"+config.Repo+"/releases/download/"+Uri.EscapeDataString(tag)+"/";
        Release r=new Release { Tag=tag,AssetUrl=baseUrl+config.AssetName,SumsUrl=baseUrl+config.SumsName }; return r;
    }
    // Test source (MIC_CONTROL_RELEASE_URL): a JSON document in the GitHub "latest release" format.
    Release FromDocument(string url) {
        string text;
        try { text=net.GetText(url); }
        catch(NetException e) { if(e.Failure==NetFailure.Offline)throw; throw new UpdateError(Messages.NoRelease); }
        Dictionary<string,object> doc;
        try { doc=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text); } catch { throw new UpdateError(Messages.NoRelease); }
        object tag; if(doc==null||!doc.TryGetValue("tag_name",out tag)||!(tag is string))throw new UpdateError(Messages.NoRelease);
        Release r=new Release { Tag=(string)tag }; object assets;
        if(doc.TryGetValue("assets",out assets)&&assets is IEnumerable) {
            foreach(object item in (IEnumerable)assets) {
                Dictionary<string,object> a=item as Dictionary<string,object>; if(a==null)continue;
                object name,link,size; if(!a.TryGetValue("name",out name)||!a.TryGetValue("browser_download_url",out link))continue;
                if(string.Equals(name as string,config.AssetName,StringComparison.OrdinalIgnoreCase)) { r.AssetUrl=link as string; if(a.TryGetValue("size",out size)&&size!=null)r.Size=Convert.ToInt64(size,CultureInfo.InvariantCulture); }
                else if(string.Equals(name as string,config.SumsName,StringComparison.OrdinalIgnoreCase))r.SumsUrl=link as string;
            }
        }
        return r;
    }
    string ExpectedSha(Release r) {
        if(r.SumsUrl==null)throw new UpdateError(Messages.NoChecksum);
        string text;
        try { text=net.GetText(r.SumsUrl); }
        catch(NetException e) { if(e.Failure==NetFailure.NotFound)throw new UpdateError(Messages.NoChecksum); throw; }
        string sha=Checksums.Find(text,config.AssetName); if(sha==null)throw new UpdateError(Messages.NoChecksum); return sha;
    }
    bool AlreadyDownloaded(Release r) {
        string file=FileFor(r.Version); if(!File.Exists(file))return false;
        if(r.Size>=0&&new FileInfo(file).Length!=r.Size)return false;
        string sha=ExpectedSha(r); if(Checksums.Sha256File(file)!=sha)return false;
        lock(gate)expectedSha=sha; return true;
    }
    void DownloadCore() {
        Release r; UpdateStage stage;
        lock(gate) { r=release; stage=status.Stage; }
        if(r==null||(stage!=UpdateStage.Available&&stage!=UpdateStage.Error))return;
        string label="Downloading "+config.AppName+" "+r.Version+"… ";
        Set(UpdateStage.Downloading,label+"0%",0);
        string file=FileFor(r.Version),part=file+".part";
        try {
            string sha=ExpectedSha(r);
            try { Directory.CreateDirectory(config.UpdatesDir); } catch(Exception) { throw new UpdateError(Messages.SaveFailed); }
            int lastPercent=0; string actual;
            try {
                actual=net.Download(r.AssetUrl,part,delegate(long done,long total) {
                    long size=r.Size>=0?r.Size:total; if(size<=0)return; int percent=(int)Math.Min(100,done*100/size);
                    if(percent>lastPercent) { lastPercent=percent; Set(UpdateStage.Downloading,label+percent+"%",percent/100.0); }
                });
            }
            catch(NetException) { TryDelete(part); throw; }
            catch(IOException) { TryDelete(part); throw new UpdateError(Messages.SaveFailed); }
            catch(UnauthorizedAccessException) { TryDelete(part); throw new UpdateError(Messages.SaveFailed); }
            if((r.Size>=0&&new FileInfo(part).Length!=r.Size)||actual!=sha) { TryDelete(part); throw new UpdateError(Messages.Damaged); }
            try { if(File.Exists(file))File.Delete(file); File.Move(part,file); } catch(Exception) { TryDelete(part); throw new UpdateError(Messages.SaveFailed); }
            lock(gate)expectedSha=sha;
            Set(UpdateStage.Ready,"Version "+r.Version+" is ready to install",1);
        }
        catch(UpdateError e) { Set(UpdateStage.Error,e.Message,0); }
        catch(NetException e) { Set(UpdateStage.Error,MessageFor(e),0); }
        catch(Exception) { TryDelete(part); Set(UpdateStage.Error,Messages.Failed,0); }
    }
    // Starts the downloaded exe in apply mode. True: the caller must now exit the way its Quit command does.
    internal bool Apply() {
        Release r; string sha; UpdateStage stage;
        lock(gate) { r=release; sha=expectedSha; stage=status.Stage; }
        if(stage!=UpdateStage.Ready||r==null||sha==null) { Set(UpdateStage.Error,Messages.NotReady,0); return false; }
        if(string.IsNullOrEmpty(config.ExePath)) { Set(UpdateStage.Error,Messages.NotReady,0); return false; }
        string file=FileFor(r.Version);
        if(!File.Exists(file)) { Set(UpdateStage.Error,Messages.Missing,0); return false; }
        try { if(Checksums.Sha256File(file)!=sha) { TryDelete(file); Set(UpdateStage.Error,Messages.Damaged,0); return false; } }
        catch(Exception) { Set(UpdateStage.Error,Messages.Missing,0); return false; }
        try { WriteMarker(r.Version); } catch(Exception) { Set(UpdateStage.Error,Messages.SaveFailed,0); return false; }
        string arguments=UpdateApply.Flag+" "+Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)+" "+UpdateApply.Quote(config.ExePath);
        bool started; try { started=Spawn(file,arguments); } catch { started=false; }
        if(!started) { TryDelete(MarkerPath); Set(UpdateStage.Error,Messages.StartFailed,0); return false; }
        return true;
    }
    void WriteMarker(string version) {
        Directory.CreateDirectory(config.UpdatesDir);
        long at=(long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
        string tmp=MarkerPath+".tmp"; File.WriteAllText(tmp,"{\"version\": \""+version+"\", \"at\": "+at+"}",new UTF8Encoding(false));
        if(File.Exists(MarkerPath))File.Delete(MarkerPath); File.Move(tmp,MarkerPath);
    }
    static bool DefaultSpawn(string exe,string arguments) {
        ProcessStartInfo info=new ProcessStartInfo(exe,arguments) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe) };
        using(Process p=Process.Start(info))return p!=null;
    }
    // Run by the new instance: toast once after an update, remove <exe>.old and stale downloads (with retries, the
    // apply-mode copy and the old exe may still be closing). Returns the background thread for tests.
    internal Thread Startup() {
        string toast=null;
        try {
            if(File.Exists(MarkerPath)) {
                Match m=Regex.Match(File.ReadAllText(MarkerPath),"\"version\"\\s*:\\s*\"([^\"]+)\"");
                if(m.Success&&m.Groups[1].Value==config.Version)toast="Updated to v"+config.Version;
                TryDelete(MarkerPath);
            }
        } catch { TryDelete(MarkerPath); }
        if(toast!=null) { Action<string> handler=OnToast; if(handler!=null) { try { handler(toast); } catch { } } }
        Thread worker=new Thread(Cleanup) { IsBackground=true,Name="update cleanup" }; worker.Start(); return worker;
    }
    void Cleanup() {
        for(int attempt=0;attempt<20;attempt++) {
            bool pending=false;
            if(!string.IsNullOrEmpty(config.ExePath)) { string old=config.ExePath+".old"; if(File.Exists(old)&&!TryDelete(old))pending=true; }
            try {
                if(Directory.Exists(config.UpdatesDir)) foreach(string path in Directory.GetFiles(config.UpdatesDir,config.Stem+"-*")) {
                    string name=Path.GetFileName(path); if(name.EndsWith(".part"))name=name.Substring(0,name.Length-5);
                    if(!name.EndsWith(config.Extension,StringComparison.OrdinalIgnoreCase))continue;
                    string version=name.Substring(config.Stem.Length+1,name.Length-config.Stem.Length-1-config.Extension.Length);
                    if(SemVer.IsNewer(version,config.Version))continue;
                    if(!TryDelete(path))pending=true;
                }
            } catch { pending=true; }
            if(!pending)return; Thread.Sleep(500);
        }
    }
    internal static bool TryDelete(string path) { try { if(File.Exists(path))File.Delete(path); return true; } catch { return false; } }
}
// Apply mode: "<downloaded exe> --apply-update <pid> <target> [--no-launch]". Runs before the single-instance mutex
// and before any window; waits for the old app to exit, swaps the target, starts it. Log: apply.log beside itself.
static class UpdateApply {
    internal const string Flag="--apply-update",NoLaunchFlag="--no-launch",RelaunchArguments="--startup"; // the new instance starts in the tray
    internal static string Quote(string path) { return "\""+path+"\""; }
    internal static int Run(string[] args) {
        string self=AppPaths.ExePath,log=Path.Combine(Path.GetDirectoryName(self),"apply.log");
        Action<string> write=delegate(string line) { try { File.AppendAllText(log,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"  "+line+Environment.NewLine); } catch { } };
        int pid; if(args.Length<3||!int.TryParse(args[1],out pid)) { write("bad arguments"); return 1; }
        string target=Path.GetFullPath(args[2]); bool launch=Array.IndexOf(args,NoLaunchFlag)<0;
        write("apply "+self+" -> "+target+", waiting for pid "+pid);
        if(!WaitForExit(pid,TimeSpan.FromSeconds(120))) { write("the app did not exit within 120 s"); return 1; }
        if(!Swap(self,target,write))return 1;
        if(launch) {
            try { using(Process p=Process.Start(new ProcessStartInfo(target,RelaunchArguments) { UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target) })) { } write("started "+target); }
            catch(Exception e) { write("could not start the new version: "+e.Message); return 1; }
        }
        return 0;
    }
    internal static bool WaitForExit(int pid,TimeSpan timeout) {
        Process p; try { p=Process.GetProcessById(pid); } catch(ArgumentException) { return true; }
        using(p) { try { return p.WaitForExit((int)timeout.TotalMilliseconds); } catch(InvalidOperationException) { return true; } catch(Win32Exception) { return false; } }
    }
    // Rename the target (allowed for a running or just-closed image) to .old, copy the source over it, compare sizes.
    // Any failure after the rename puts .old back. Never leaves the target missing when it existed before.
    internal static bool Swap(string source,string target,Action<string> log) {
        string old=target+".old";
        if(File.Exists(old)&&!Updater.TryDelete(old)) {
            string aside=old+"."+DateTime.Now.Ticks; try { File.Move(old,aside); log("stale .old moved to "+aside); } catch(Exception e) { log("stale .old is locked: "+e.Message); return false; }
        }
        if(!File.Exists(source)) { log("update file missing: "+source); return false; }
        bool renamed=false;
        if(File.Exists(target)) {
            for(int attempt=1;attempt<=40&&!renamed;attempt++) {
                try { File.Move(target,old); renamed=true; }
                catch(Exception e) { if(attempt==40) { log("could not rename the target: "+e.Message); return false; } Thread.Sleep(500); }
            }
        }
        try {
            File.Copy(source,target,false);
            if(new FileInfo(source).Length!=new FileInfo(target).Length)throw new IOException("size mismatch after copy");
            log("swapped"+(renamed?", previous version kept as "+old:"")); return true;
        } catch(Exception e) {
            log("copy failed: "+e.Message);
            Updater.TryDelete(target);
            if(renamed) { try { File.Move(old,target); log("restored the previous version"); } catch(Exception r) { log("restore failed: "+r.Message); } }
            return false;
        }
    }
}
}
