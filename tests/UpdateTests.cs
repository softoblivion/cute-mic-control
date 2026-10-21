using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
namespace MicControl {
// Console runner for the update logic (src\Updater.cs, src\Paths.cs). Built and run by build\build.ps1; exit code 0
// means every test passed. "--stand-in <ms>" makes the runner itself a stand-in "app" that sleeps and exits.
static class UpdateTests {
    static int failed,passed; static string root;
    static int Main(string[] args) {
        if(args.Length==2&&args[0]=="--stand-in") { Thread.Sleep(int.Parse(args[1])); return 0; }
        root=Path.Combine(Path.GetTempPath(),"MicControl.Tests-"+Guid.NewGuid().ToString("N"),"Mic (Control) test");
        Directory.CreateDirectory(root);
        try {
            Run("version parse and compare",VersionCompare);
            Run("SHA256SUMS parsing",SumsParsing);
            Run("SHA-256 of a file",FileHash);
            Run("check: newer release is available",CheckAvailable);
            Run("check: same or older release is up to date",CheckUpToDate);
            Run("check: offline, limit, no release, bad tag, missing asset",CheckErrors);
            Run("check: release document override",CheckOverride);
            Run("download: verified file becomes ready",DownloadReady);
            Run("download: checksum mismatch and missing checksum",DownloadErrors);
            Run("check: verified earlier download is ready at once",AlreadyDownloaded);
            Run("check: automatic download goes on to ready",AutoDownload);
            Run("apply: marker and helper arguments",ApplyStartsHelper);
            Run("apply: refused when not ready or the file changed",ApplyRefused);
            Run("startup: toast once, .old and stale downloads removed",StartupCleanup);
            Run("swap: running stand-in exe is renamed and replaced",SwapRunning);
            Run("swap: failed copy restores the previous exe",SwapRestore);
            Run("apply mode: waits for the stand-in, then swaps",ApplyModeRun);
            Run("data folder follows MIC_CONTROL_HOME",DataFolder);
        } finally { try { Directory.Delete(Path.GetDirectoryName(root),true); } catch { } }
        Console.WriteLine(passed+" passed, "+failed+" failed");
        return failed==0?0:1;
    }
    static void Run(string name,Action test) {
        try { test(); passed++; Console.WriteLine("PASS  "+name); }
        catch(Exception e) { failed++; Console.WriteLine("FAIL  "+name+": "+e.Message); }
    }
    static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    static void Equal(object expected,object actual,string what) { if(!object.Equals(expected,actual))throw new Exception(what+": expected <"+expected+">, got <"+actual+">"); }
    static string Folder(string name) { string path=Path.Combine(root,name); if(Directory.Exists(path))Directory.Delete(path,true); Directory.CreateDirectory(path); return path; }
    static string Sha(string text) { return Checksums.Hex(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text))); }

    class FakeNet : IUpdateNet {
        internal string Location; internal Dictionary<string,object> Texts=new Dictionary<string,object>(),Files=new Dictionary<string,object>(),Lengths=new Dictionary<string,object>();
        internal Exception RedirectError;
        static object Take(Dictionary<string,object> map,string url) { object value; if(!map.TryGetValue(url,out value))throw new NetException(NetFailure.NotFound,"HTTP 404"); Exception e=value as Exception; if(e!=null)throw e; return value; }
        public string RedirectLocation(string url) { if(RedirectError!=null)throw RedirectError; Equal("https://github.com/softoblivion/cute-mic-control/releases/latest",url,"latest URL"); return Location; }
        public long ContentLength(string url) { return (long)Take(Lengths,url); }
        public string GetText(string url) { return (string)Take(Texts,url); }
        public string Download(string url,string destination,Action<long,long> progress) {
            byte[] data=Encoding.UTF8.GetBytes((string)Take(Files,url)); File.WriteAllBytes(destination,data);
            if(progress!=null) { progress(data.Length/2,data.Length); progress(data.Length,data.Length); }
            return Checksums.Hex(System.Security.Cryptography.SHA256.Create().ComputeHash(data));
        }
    }
    const string Base="https://github.com/softoblivion/cute-mic-control/releases/download/v1.2.0/";
    static FakeNet Published(string content) {
        FakeNet net=new FakeNet { Location="https://github.com/softoblivion/cute-mic-control/releases/tag/v1.2.0" };
        net.Lengths[Base+"MicControl.exe"]=(long)Encoding.UTF8.GetByteCount(content);
        net.Texts[Base+"SHA256SUMS"]=Sha(content)+"  MicControl.exe\n";
        net.Files[Base+"MicControl.exe"]=content; return net;
    }
    static Updater Make(FakeNet net,string folder,string version) {
        UpdateConfig c=new UpdateConfig { Repo="softoblivion/cute-mic-control",AssetName="MicControl.exe",AppName="Mic Control",Version=version,
            UpdatesDir=Path.Combine(folder,"updates"),ExePath=Path.Combine(folder,"MicControl.exe"),ReleaseUrlVariable="MIC_CONTROL_TEST_RELEASE_URL" };
        return new Updater(c,net);
    }

    static void VersionCompare() {
        Equal("1.2.3",SemVer.Format(SemVer.Parse("v1.2.3")),"v1.2.3"); Equal("1.2.0",SemVer.Format(SemVer.Parse("1.2")),"1.2");
        Equal("1.1.0",SemVer.Format(SemVer.Parse("v1.1.0-beta.1")),"suffix ignored");
        Assert(SemVer.Parse("latest")==null&&SemVer.Parse("")==null&&SemVer.Parse(null)==null,"no digits -> null");
        Assert(SemVer.IsNewer("v1.10.0","1.9.9"),"numeric, not text compare"); Assert(SemVer.IsNewer("v2.0.0","1.99.99"),"major wins");
        Assert(SemVer.IsNewer("1.1.1","1.1.0"),"patch"); Assert(!SemVer.IsNewer("v1.1.0","1.1.0"),"equal is not newer");
        Assert(!SemVer.IsNewer("v1.0.9","1.1.0"),"never a downgrade"); Assert(!SemVer.IsNewer("garbage","1.0.0"),"bad tag is not newer");
    }
    static void SumsParsing() {
        string a=new string('a',64),b=new string('B',64);
        string text="# comment\r\n"+a+"  Other.exe\r\n"+b+" *MicControl.exe\r\n\r\n";
        Equal(new string('b',64),Checksums.Find(text,"MicControl.exe"),"binary mode entry, CRLF, lower case");
        Equal(a,Checksums.Find(text,"other.EXE"),"name compare ignores case");
        Assert(Checksums.Find(text,"Missing.exe")==null,"missing entry"); Assert(Checksums.Find("not a sums file","MicControl.exe")==null,"garbage");
        Assert(Checksums.Find(new string('a',63)+"  MicControl.exe","MicControl.exe")==null,"short hash rejected");
    }
    static void FileHash() {
        string path=Path.Combine(Folder("hash"),"abc.txt"); File.WriteAllText(path,"abc",new UTF8Encoding(false));
        Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",Checksums.Sha256File(path),"sha256(abc)");
    }
    static void CheckAvailable() {
        List<UpdateStage> seen=new List<UpdateStage>(); Updater u=Make(Published("new build"),Folder("available"),"1.1.0"); u.OnStatus=delegate(UpdateStatus s) { seen.Add(s.Stage); };
        u.Check(); UpdateStatus st=u.Status;
        Equal(UpdateStage.Available,st.Stage,"stage"); Equal("1.2.0",st.Latest,"latest"); Equal(9L,st.Size,"size"); Equal("Version 1.2.0 is available",st.Message,"message");
        Assert(seen.Count==2&&seen[0]==UpdateStage.Checking,"checking then available");
    }
    static void CheckUpToDate() {
        Updater u=Make(Published("x"),Folder("uptodate"),"1.2.0"); u.Check(); Equal(UpdateStage.UpToDate,u.Status.Stage,"equal version"); Equal("Mic Control is up to date",u.Status.Message,"message");
        u=Make(Published("x"),Folder("uptodate"),"1.3.0"); u.Check(); Equal(UpdateStage.UpToDate,u.Status.Stage,"newer local version"); Assert(u.Status.Latest==null,"no latest offered");
    }
    static void CheckErrors() {
        FakeNet net=Published("x"); net.RedirectError=new NetException(NetFailure.Offline,"ConnectFailure"); Updater u=Make(net,Folder("errors"),"1.1.0"); u.Check();
        Equal(UpdateStage.Error,u.Status.Stage,"offline stage"); Equal(Updater.Messages.Offline,u.Status.Message,"offline");
        net.RedirectError=new NetException(NetFailure.Limit,"HTTP 429"); u.Check(); Equal(Updater.Messages.Limit,u.Status.Message,"limit");
        net.RedirectError=null; net.Location=null; u.Check(); Equal(Updater.Messages.NoRelease,u.Status.Message,"no redirect");
        net.Location="https://github.com/softoblivion/cute-mic-control/releases"; u.Check(); Equal(Updater.Messages.NoRelease,u.Status.Message,"redirect without tag");
        net.Location="https://github.com/softoblivion/cute-mic-control/releases/tag/nightly"; u.Check(); Equal(Updater.Messages.BadVersion,u.Status.Message,"bad tag");
        net=Published("x"); net.Lengths.Clear(); u=Make(net,Folder("errors"),"1.1.0"); u.Check(); Equal("The release has no MicControl.exe",u.Status.Message,"missing asset");
        u.Download(); Equal(UpdateStage.Error,u.Status.Stage,"no download without a release");
    }
    static void CheckOverride() {
        string folder=Folder("override"); FakeNet net=new FakeNet(); string doc="http://127.0.0.1:8765/latest.json";
        net.Texts[doc]="{\"tag_name\":\"v1.1.1\",\"body\":\"notes\",\"assets\":[{\"name\":\"MicControl.exe\",\"size\":5,\"browser_download_url\":\"http://127.0.0.1:8765/MicControl.exe\"},{\"name\":\"SHA256SUMS\",\"size\":81,\"browser_download_url\":\"http://127.0.0.1:8765/SHA256SUMS\"}]}";
        net.Texts["http://127.0.0.1:8765/SHA256SUMS"]=Sha("hello")+"  MicControl.exe\n"; net.Files["http://127.0.0.1:8765/MicControl.exe"]="hello";
        net.RedirectError=new Exception("the redirect must not be used when the override is set");
        Environment.SetEnvironmentVariable("MIC_CONTROL_TEST_RELEASE_URL",doc);
        try {
            Updater u=Make(net,folder,"1.1.0"); u.Check(); Equal(UpdateStage.Available,u.Status.Stage,"override available"); Equal(5L,u.Status.Size,"size from document");
            u.Download(); Equal(UpdateStage.Ready,u.Status.Stage,"override download");
            net.Texts[doc]="not json"; u.Check(); Equal(Updater.Messages.NoRelease,u.Status.Message,"unreadable document");
            net.Texts.Remove(doc); u.Check(); Equal(Updater.Messages.NoRelease,u.Status.Message,"404 document");
            net.Texts[doc]=new NetException(NetFailure.Offline,"ConnectFailure"); u.Check(); Equal(Updater.Messages.Offline,u.Status.Message,"server down");
        } finally { Environment.SetEnvironmentVariable("MIC_CONTROL_TEST_RELEASE_URL",null); }
    }
    static void DownloadReady() {
        string folder=Folder("download"); Updater u=Make(Published("new build"),folder,"1.1.0"); List<double> progress=new List<double>();
        u.OnStatus=delegate(UpdateStatus s) { if(s.Stage==UpdateStage.Downloading)progress.Add(s.Progress); };
        u.Check(); u.Download(); UpdateStatus st=u.Status;
        Equal(UpdateStage.Ready,st.Stage,"stage"); Assert(st.CanApply,"can apply"); Equal("Version 1.2.0 is ready to install",st.Message,"message");
        string file=Path.Combine(folder,"updates","MicControl-1.2.0.exe"); Assert(File.Exists(file),"file saved as MicControl-<version>.exe"); Assert(!File.Exists(file+".part"),".part removed");
        Assert(progress.Count>=2&&progress[0]==0&&progress[progress.Count-1]==1,"progress 0..100%");
    }
    static void DownloadErrors() {
        string folder=Folder("download-bad"); FakeNet net=Published("new build"); net.Texts[Base+"SHA256SUMS"]=Sha("other build")+"  MicControl.exe\n";
        Updater u=Make(net,folder,"1.1.0"); u.Check(); u.Download();
        Equal(Updater.Messages.Damaged,u.Status.Message,"checksum mismatch"); Assert(Directory.GetFiles(Path.Combine(folder,"updates")).Length==0,"nothing left on disk");
        net.Texts[Base+"SHA256SUMS"]=Sha("new build")+"  Other.exe\n"; u.Download(); Equal(Updater.Messages.NoChecksum,u.Status.Message,"entry missing (retry from error)");
        net.Texts.Remove(Base+"SHA256SUMS"); u.Download(); Equal(Updater.Messages.NoChecksum,u.Status.Message,"file missing");
        net=Published("new build"); net.Lengths[Base+"MicControl.exe"]=999L; u=Make(net,folder,"1.1.0"); u.Check(); u.Download(); Equal(Updater.Messages.Damaged,u.Status.Message,"size mismatch");
    }
    static void AlreadyDownloaded() {
        string folder=Folder("again"); Updater u=Make(Published("new build"),folder,"1.1.0"); u.Check(); u.Download();
        u=Make(Published("new build"),folder,"1.1.0"); u.Check(); Equal(UpdateStage.Ready,u.Status.Stage,"ready after restart");
        File.WriteAllText(Path.Combine(folder,"updates","MicControl-1.2.0.exe"),"tampered"); u=Make(Published("new build"),folder,"1.1.0"); u.Check(); Equal(UpdateStage.Available,u.Status.Stage,"corrupt copy is not ready");
    }
    static void AutoDownload() {
        string folder=Folder("auto"); Updater u=Make(Published("new build"),folder,"1.1.0"); u.AutoDownload=delegate { return true; }; u.Check();
        Equal(UpdateStage.Ready,u.Status.Stage,"downloaded in the same check");
        u=Make(Published("new build"),Folder("auto off"),"1.1.0"); u.AutoDownload=delegate { return false; }; u.Check(); Equal(UpdateStage.Available,u.Status.Stage,"switch off waits for a click");
    }
    static void ApplyStartsHelper() {
        string folder=Folder("apply"); Updater u=Make(Published("new build"),folder,"1.1.0"); u.Check(); u.Download();
        string exe=null,arguments=null; u.Spawn=delegate(string e,string a) { exe=e; arguments=a; return true; };
        Assert(u.Apply(),"apply returns true");
        Equal(Path.Combine(folder,"updates","MicControl-1.2.0.exe"),exe,"helper is the downloaded exe");
        Equal("--apply-update "+Process.GetCurrentProcess().Id+" \""+Path.Combine(folder,"MicControl.exe")+"\"",arguments,"arguments");
        string marker=File.ReadAllText(Path.Combine(folder,"updates","applied.json")); Assert(marker.Contains("\"version\": \"1.2.0\""),"marker names the version");
        u=Make(Published("new build"),folder,"1.1.0"); u.Check(); u.Spawn=delegate { return false; };
        Assert(!u.Apply(),"spawn failure"); Equal(Updater.Messages.StartFailed,u.Status.Message,"message"); Assert(!File.Exists(Path.Combine(folder,"updates","applied.json")),"marker removed");
    }
    static void ApplyRefused() {
        string folder=Folder("refused"); Updater u=Make(Published("new build"),folder,"1.1.0"); u.Spawn=delegate { throw new Exception("must not start"); };
        Assert(!u.Apply(),"idle"); Equal(Updater.Messages.NotReady,u.Status.Message,"not ready");
        u.Check(); Assert(!u.Apply(),"available only"); u.Download();
        File.WriteAllText(Path.Combine(folder,"updates","MicControl-1.2.0.exe"),"changed"); Assert(!u.Apply(),"changed file"); Equal(Updater.Messages.Damaged,u.Status.Message,"damaged");
        Assert(!File.Exists(Path.Combine(folder,"updates","MicControl-1.2.0.exe")),"changed file deleted");
        u=Make(Published("new build"),folder,"1.1.0"); u.Check(); u.Download(); File.Delete(Path.Combine(folder,"updates","MicControl-1.2.0.exe")); Assert(!u.Apply(),"vanished"); Equal(Updater.Messages.Missing,u.Status.Message,"missing");
    }
    static void StartupCleanup() {
        string folder=Folder("startup"),updates=Path.Combine(folder,"updates"); Directory.CreateDirectory(updates);
        File.WriteAllText(Path.Combine(folder,"MicControl.exe.old"),"old"); File.WriteAllText(Path.Combine(updates,"applied.json"),"{\"version\": \"1.2.0\", \"at\": 1}");
        foreach(string n in new string[]{"MicControl-1.2.0.exe","MicControl-1.1.0.exe.part","MicControl-1.3.0.exe","Other-1.0.0.exe"})File.WriteAllText(Path.Combine(updates,n),"x");
        List<string> toasts=new List<string>(); Updater u=Make(new FakeNet(),folder,"1.2.0"); u.OnToast=delegate(string t) { toasts.Add(t); };
        Assert(u.Startup().Join(15000),"cleanup finished");
        Assert(toasts.Count==1&&toasts[0]=="Updated to v1.2.0","toast once"); Assert(!File.Exists(Path.Combine(updates,"applied.json")),"marker removed");
        Assert(!File.Exists(Path.Combine(folder,"MicControl.exe.old")),".old removed");
        Assert(!File.Exists(Path.Combine(updates,"MicControl-1.2.0.exe"))&&!File.Exists(Path.Combine(updates,"MicControl-1.1.0.exe.part")),"current and older downloads removed");
        Assert(File.Exists(Path.Combine(updates,"MicControl-1.3.0.exe"))&&File.Exists(Path.Combine(updates,"Other-1.0.0.exe")),"newer download and foreign files kept");
        u.Startup().Join(15000); Assert(toasts.Count==1,"no second toast");
        File.WriteAllText(Path.Combine(updates,"applied.json"),"{\"version\": \"9.9.9\", \"at\": 1}"); u.Startup().Join(15000); Assert(toasts.Count==1,"stale marker gives no toast"); Assert(!File.Exists(Path.Combine(updates,"applied.json")),"stale marker removed");
    }
    static Process StandIn(string exe,int milliseconds) {
        File.Copy(AppPaths.ExePath,exe,true);
        Process p=Process.Start(new ProcessStartInfo(exe,"--stand-in "+milliseconds) { UseShellExecute=false,CreateNoWindow=true });
        Thread.Sleep(300); return p;
    }
    static void SwapRunning() {
        string folder=Folder("swap running"),target=Path.Combine(folder,"MicControl.exe"),source=Path.Combine(folder,"new.bin");
        File.WriteAllText(source,"new version"); File.WriteAllText(target+".old","stale");
        using(Process app=StandIn(target,3000)) {
            Assert(!app.HasExited,"stand-in runs"); List<string> log=new List<string>();
            Assert(UpdateApply.Swap(source,target,delegate(string l) { log.Add(l); }),"swap while the old exe still runs: "+string.Join("; ",log.ToArray()));
            Equal("new version",File.ReadAllText(target),"target replaced"); Assert(File.Exists(target+".old")&&new FileInfo(target+".old").Length>100,"running exe kept as .old");
            app.WaitForExit(10000);
        }
        Assert(Updater.TryDelete(target+".old"),".old deletable after the stand-in exited");
    }
    static void SwapRestore() {
        string folder=Folder("swap restore"),target=Path.Combine(folder,"MicControl.exe"),source=Path.Combine(folder,"new.bin"); List<string> log=new List<string>();
        File.WriteAllText(target,"previous");
        Assert(!UpdateApply.Swap(source,target,delegate(string l) { log.Add(l); }),"missing source fails"); Equal("previous",File.ReadAllText(target),"target untouched");
        File.WriteAllText(source,"new version");
        using(FileStream locked=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None)) {
            Assert(!UpdateApply.Swap(source,target,delegate(string l) { log.Add(l); }),"unreadable source fails");
        }
        Equal("previous",File.ReadAllText(target),"previous exe restored"); Assert(!File.Exists(target+".old"),"no .old after a restore");
    }
    static void ApplyModeRun() {
        string folder=Folder("apply mode"),target=Path.Combine(folder,"MicControl.exe");
        using(Process app=StandIn(target,2000)) {
            Stopwatch clock=Stopwatch.StartNew();
            int code=UpdateApply.Run(new string[]{UpdateApply.Flag,app.Id.ToString(),target,UpdateApply.NoLaunchFlag});
            Equal(0,code,"exit code"); Assert(app.HasExited,"waited for the app to exit"); Assert(clock.ElapsedMilliseconds>=1000,"did not swap before the exit");
        }
        Equal(new FileInfo(AppPaths.ExePath).Length,new FileInfo(target).Length,"target is a copy of the apply-mode exe"); Assert(File.Exists(target+".old"),".old kept for the new instance");
        Assert(UpdateApply.WaitForExit(int.MaxValue-7,TimeSpan.FromSeconds(1)),"a pid that does not exist counts as exited");
        using(Process slow=StandIn(Path.Combine(folder,"Slow.exe"),3000)) { Assert(!UpdateApply.WaitForExit(slow.Id,TimeSpan.FromMilliseconds(300)),"timeout reported"); slow.WaitForExit(10000); }
        Equal(1,UpdateApply.Run(new string[]{UpdateApply.Flag,"nope",target}),"bad arguments");
    }
    static void DataFolder() {
        string previous=Environment.GetEnvironmentVariable(AppPaths.HomeVariable);
        try {
            Environment.SetEnvironmentVariable(AppPaths.HomeVariable,null);
            Equal(Path.Combine(AppPaths.ExeDirectory,"cute-mic-control_data"),AppPaths.Data,"beside the exe");
            Environment.SetEnvironmentVariable(AppPaths.HomeVariable,root); Equal(Path.Combine(root,"settings.xml"),AppPaths.Settings,"override"); Equal(Path.Combine(root,"updates"),AppPaths.Updates,"updates");
        } finally { Environment.SetEnvironmentVariable(AppPaths.HomeVariable,previous); }
    }
}
}
