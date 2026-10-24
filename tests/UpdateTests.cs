using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SelfUpdate;
namespace MicControl {
// Console runner for Mic Control's use of the vendored self-update library (src\vendor\selfupdate-dotnet; the
// library carries its own full suite): the app's configuration (src\UpdateSetup.cs), its names on the release path
// and its data folder (src\Paths.cs). Built and run by build\build.ps1; exit code 0 means every test passed.
// "--stand-in <ms>" makes the runner itself a stand-in "app" that sleeps and exits.
static class UpdateTests {
    static int failed,passed; static string root;
    static int Main(string[] args) {
        if(args.Length==2&&args[0]=="--stand-in") { Thread.Sleep(int.Parse(args[1])); return 0; }
        root=Path.Combine(Path.GetTempPath(),"MicControl.Tests-"+Guid.NewGuid().ToString("N"),"Mic (Control) test");
        Directory.CreateDirectory(root);
        string home=Environment.GetEnvironmentVariable(AppPaths.HomeVariable);
        try {
            Run("configuration: repository, asset, names, data folder",Configuration);
            Run("check: API and redirect of the app's repository, app messages",CheckThroughApp);
            Run("check: MIC_CONTROL_RELEASE_URL replaces GitHub",ReleaseOverride);
            Run("download and apply: MicControl-<version>.exe in the data folder, helper arguments",DownloadAndApply);
            Run("presenter: the app's pill, sheet and Install over a published release",PresenterThroughApp);
            Run("toasts: tray balloon text of the presenter's toasts",BalloonText);
            Run("apply mode: routed by the flag, swaps a running stand-in",ApplyMode);
            Run("data folder follows MIC_CONTROL_HOME",DataFolder);
        } finally { Environment.SetEnvironmentVariable(AppPaths.HomeVariable,home); try { Directory.Delete(Path.GetDirectoryName(root),true); } catch { } }
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
        internal List<string> Asked=new List<string>();
        static object Take(Dictionary<string,object> map,string url) { object value; if(!map.TryGetValue(url,out value))throw new NetException(NetFailure.NotFound,"HTTP 404"); Exception e=value as Exception; if(e!=null)throw e; return value; }
        public string RedirectLocation(string url) { Asked.Add("HEAD "+url); return Location; }
        public long ContentLength(string url) { return (long)Take(Lengths,url); }
        public string GetText(string url) { Asked.Add("GET "+url); return (string)Take(Texts,url); }
        public string GetText(string url,IDictionary<string,string> headers) { return GetText(url); }
        public string Download(string url,string destination,Action<long,long> progress) {
            byte[] data=Encoding.UTF8.GetBytes((string)Take(Files,url)); File.WriteAllBytes(destination,data);
            return Checksums.Hex(System.Security.Cryptography.SHA256.Create().ComputeHash(data));
        }
    }
    const string Repo="https://github.com/softoblivion/cute-mic-control";
    const string Base=Repo+"/releases/download/v9.0.0/";
    // The app's own config with its data folder redirected to a scratch folder, as every manual run does.
    static Updater Make(FakeNet net,string folder) { Environment.SetEnvironmentVariable(AppPaths.HomeVariable,folder); return new Updater(AppUpdate.Config(),net); }
    static FakeNet Published(string content) {
        FakeNet net=new FakeNet { Location=Repo+"/releases/tag/v9.0.0" };
        net.Lengths[Base+"MicControl.exe"]=(long)Encoding.UTF8.GetByteCount(content);
        net.Texts[Base+"SHA256SUMS"]=Sha(content)+"  MicControl.exe\n"; net.Files[Base+"MicControl.exe"]=content; return net;
    }

    static void Configuration() {
        string folder=Folder("config"); Environment.SetEnvironmentVariable(AppPaths.HomeVariable,folder); UpdateConfig c=AppUpdate.Config();
        Equal("softoblivion/cute-mic-control",c.Repo,"repository"); Equal("MicControl.exe",c.AssetName,"release asset"); Equal("Mic Control",c.AppName,"app name");
        Equal(AppInfo.Version,c.Version,"version from Version.cs"); Equal(Path.Combine(folder,"updates"),c.UpdatesDir,"updates in the data folder");
        Equal(AppPaths.ExePath,c.ExePath,"running exe"); Equal("MIC_CONTROL_RELEASE_URL",c.ReleaseUrlVariable,"test override variable"); Equal("SHA256SUMS",c.SumsName,"checksum asset");
        Equal("--startup",AppUpdate.RelaunchArguments,"the new version starts in the tray");
    }
    static void CheckThroughApp() {
        FakeNet net=Published("x"); Updater u=Make(net,Folder("check"));
        Equal("https://api.github.com/repos/softoblivion/cute-mic-control/releases/latest",u.ApiUrl,"API URL"); Equal(Repo+"/releases/latest",u.ReleasePage,"release page");
        u.Check(); Assert(net.Asked.Contains("GET "+u.ApiUrl)&&net.Asked.Contains("HEAD "+Repo+"/releases/latest"),"API first, then the redirect");
        Equal(UpdateStage.Available,u.Status.Stage,"newer release"); Equal("Version 9.0.0 is available",u.Status.Message,"message");
        net.Location=Repo+"/releases/tag/v"+AppInfo.Version; u.Check(); Equal("Mic Control is up to date",u.Status.Message,"up to date names the app");
    }
    static void ReleaseOverride() {
        string doc="http://127.0.0.1:8765/latest.json"; FakeNet net=new FakeNet();
        net.Texts[doc]="{\"tag_name\":\"v9.0.1\",\"assets\":[{\"name\":\"MicControl.exe\",\"size\":1,\"browser_download_url\":\"http://127.0.0.1:8765/MicControl.exe\"}]}";
        Environment.SetEnvironmentVariable("MIC_CONTROL_RELEASE_URL",doc);
        try { Updater u=Make(net,Folder("override")); u.Check(); Equal("9.0.1",u.Status.Latest,"version from the document"); Assert(!net.Asked.Exists(delegate(string a) { return a.Contains("github.com"); }),"GitHub not asked"); }
        finally { Environment.SetEnvironmentVariable("MIC_CONTROL_RELEASE_URL",null); }
    }
    static void DownloadAndApply() {
        string folder=Folder("apply"); Updater u=Make(Published("new build"),folder); u.Check(); u.Download();
        Equal(UpdateStage.Ready,u.Status.Stage,"ready"); string file=Path.Combine(folder,"updates","MicControl-9.0.0.exe"); Assert(File.Exists(file),"MicControl-<version>.exe saved");
        string exe=null,arguments=null; u.Spawn=delegate(string e,string a) { exe=e; arguments=a; return true; };
        Assert(u.Apply(),"apply"); Equal(file,exe,"the downloaded exe runs the apply mode");
        Equal("--apply-update "+Process.GetCurrentProcess().Id+" \""+AppPaths.ExePath+"\"",arguments,"arguments name the running exe");
    }
    // The release as the API document carries it (notes and date), the exe and its checksum as Published() serves them.
    static FakeNet PublishedWithNotes(string content) {
        FakeNet net=Published(content);
        net.Texts["https://api.github.com/repos/softoblivion/cute-mic-control/releases/latest"]="{\"tag_name\":\"v9.0.0\",\"body\":\"## What is new\\n- A **bold** item\",\"published_at\":\"2026-10-21T10:00:00Z\",\"assets\":[{\"name\":\"MicControl.exe\",\"size\":"+Encoding.UTF8.GetByteCount(content)+",\"browser_download_url\":\""+Base+"MicControl.exe\"},{\"name\":\"SHA256SUMS\",\"size\":100,\"browser_download_url\":\""+Base+"SHA256SUMS\"}]}";
        return net;
    }
    static void PresenterThroughApp() {
        Updater u=Make(PublishedWithNotes("new build"),Folder("presenter")); UpdatePresenter p=new UpdatePresenter(u,null);
        bool applied=false,quit=false; p.StartDownload=delegate { u.Download(); }; p.ApplyUpdate=delegate { applied=true; return true; }; p.OnQuit=delegate { quit=true; };
        Equal("v"+AppInfo.Version,p.View.Pill.Text,"pill text is the display version"); Assert(!p.View.Sheet.Open,"no sheet before a check");
        u.Check();
        Assert(p.View.Sheet.Open,"the sheet opens by itself for a newer version"); Equal("Mic Control 9.0.0",p.View.Sheet.Title,"sheet title");
        Assert(p.View.Sheet.Subtitle.StartsWith("Installed v"+AppInfo.Version+" · ")&&p.View.Sheet.Subtitle.EndsWith("Oct 21, 2026"),"subtitle: installed, size, date ("+p.View.Sheet.Subtitle+")");
        Equal(PillState.Available,p.View.Pill.State,"pill dot"); Equal("Mic Control 9.0.0 is available",p.View.Pill.Tooltip,"pill tooltip");
        Equal(NoteBlockKind.Heading,p.View.Sheet.Notes[0].Kind,"notes rendered as blocks"); Equal("Install",p.View.Sheet.Action.Label,"main button");
        p.Run(UpdateCommand.Install);
        Assert(applied&&quit,"Install downloads, then restarts into the verified file"); Assert(File.Exists(Path.Combine(AppPaths.Updates,"MicControl-9.0.0.exe")),"MicControl-9.0.0.exe is saved");
        p.Later(); Assert(!p.View.Sheet.Open,"Later closes the sheet");
    }
    static void BalloonText() {
        string title,text; bool clicked=false;
        AppUpdate.Balloon(new UpdateToast { Level=ToastLevel.Success,Text="Mic Control 9.0.0 is ready",ActionLabel=UpdateText.Restart,Action=delegate { clicked=true; },Sticky=true },out title,out text);
        Equal("Mic Control 9.0.0 is ready",title,"ready balloon title"); Equal("Click to restart and update.",text,"ready balloon text"); Assert(!clicked,"building the text does not click");
        AppUpdate.Balloon(new UpdateToast { Level=ToastLevel.Success,Text="Mic Control 9.0.0 is ready",ActionLabel=UpdateText.View,Action=delegate { } },out title,out text);
        Equal("Click to view.",text,"copy that cannot apply asks for a view");
        AppUpdate.Balloon(new UpdateToast { Level=ToastLevel.Success,Text="Updated to v9.0.0" },out title,out text);
        Equal("Mic Control",title,"plain toast under the app name"); Equal("Updated to v9.0.0",text,"updated toast text");
    }
    static void ApplyMode() {
        Assert(UpdateApply.IsApplyMode(new string[]{"--apply-update","1","x"})&&!UpdateApply.IsApplyMode(new string[]{"--startup"})&&!UpdateApply.IsApplyMode(new string[0]),"routing flag");
        string folder=Folder("apply mode"),target=Path.Combine(folder,"MicControl.exe");
        File.Copy(AppPaths.ExePath,target,true);
        using(Process app=Process.Start(new ProcessStartInfo(target,"--stand-in 1500") { UseShellExecute=false,CreateNoWindow=true })) {
            Thread.Sleep(300);
            Equal(0,UpdateApply.Run(new string[]{UpdateApply.Flag,app.Id.ToString(),target,UpdateApply.NoLaunchFlag},AppUpdate.RelaunchArguments),"exit code");
            Assert(app.HasExited,"waited for the app to exit");
        }
        Equal(new FileInfo(AppPaths.ExePath).Length,new FileInfo(target).Length,"target replaced"); Assert(File.Exists(target+".old"),".old kept for the new instance");
    }
    static void DataFolder() {
        Environment.SetEnvironmentVariable(AppPaths.HomeVariable,null);
        Equal(Path.Combine(AppPaths.ExeDirectory,"cute-mic-control_data"),AppPaths.Data,"beside the exe");
        Environment.SetEnvironmentVariable(AppPaths.HomeVariable,root); Equal(Path.Combine(root,"settings.xml"),AppPaths.Settings,"override"); Equal(Path.Combine(root,"updates"),AppPaths.Updates,"updates");
    }
}
}
