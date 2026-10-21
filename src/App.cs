using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Xml;
namespace MicControl {
static class Program {
    internal static bool Test;
    [STAThread] static int Main(string[] args) {
        if(args.Length>0 && args[0]==UpdateApply.Flag) return UpdateApply.Run(args);
        Native.SetProcessDPIAware(); Test=args.Contains("--self-test"); bool fresh;
        using(Mutex mutex=new Mutex(true,"Local\\MicControl.Glow.App",out fresh)) {
            if(!fresh && !Test) { MessageBox.Show("The app is already running. Open Settings from the system tray.","Mic Control"); return 0; }
            Application app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            try {
                SettingsWindow window=new SettingsWindow(args.Contains("--startup")); app.MainWindow=window;
                if(Test) { window.Show(); TestSuite.Run(window); } else window.Start();
                app.Run();
            } catch(Exception ex) {
                if(Test) { Directory.CreateDirectory(AppPaths.SelfTest); File.WriteAllText(Path.Combine(AppPaths.SelfTest,"self-test.txt"),ex.ToString()); }
                else MessageBox.Show("The app could not start. Close other copies and try again.","Mic Control");
            }
        }
        return 0;
    }
}
class Preferences {
    internal uint Modifiers=3,Key=77; internal bool Overlay=true,AutoDownloadUpdates=true;
    internal int IndicatorScale=100,PaletteIndex=0;
    internal static string PathName { get { return AppPaths.Settings; } }
    // Portable file first; otherwise import the profile settings of 1.0.0 once (never when MIC_CONTROL_HOME redirects the data).
    internal static Preferences Load() {
        if(File.Exists(PathName)) return Load(PathName);
        if(!AppPaths.Redirected) foreach(string old in AppPaths.ProfileSettings) if(File.Exists(old)) { Preferences p=Load(old); try { p.Save(); } catch { } return p; }
        return new Preferences();
    }
    internal static Preferences Load(string path) {
        Preferences p=new Preferences();
        try { XmlDocument d=new XmlDocument(); d.Load(path); uint m=uint.Parse(d.DocumentElement.GetAttribute("modifiers")),k=uint.Parse(d.DocumentElement.GetAttribute("key"));
            if(((k>=8 && k<=254)||k==4109) && m<=15) { p.Modifiers=m; p.Key=k; p.Overlay=bool.Parse(d.DocumentElement.GetAttribute("overlay")); }
            int scale,palette;
            if(int.TryParse(d.DocumentElement.GetAttribute("indicatorScale"),out scale))p.IndicatorScale=Math.Max(50,Math.Min(200,scale));
            if(int.TryParse(d.DocumentElement.GetAttribute("palette"),out palette) && palette>=0 && palette<Palette.All.Length)p.PaletteIndex=palette;
            bool auto; if(bool.TryParse(d.DocumentElement.GetAttribute("autoDownloadUpdates"),out auto))p.AutoDownloadUpdates=auto;
        } catch { } return p;
    }
    internal void Save() { Save(PathName); }
    internal void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); XmlDocument d=new XmlDocument(); XmlElement e=d.CreateElement("MicControl"); d.AppendChild(e);
        e.SetAttribute("modifiers",Modifiers.ToString()); e.SetAttribute("key",Key.ToString()); e.SetAttribute("overlay",Overlay.ToString());
        e.SetAttribute("indicatorScale",IndicatorScale.ToString()); e.SetAttribute("palette",PaletteIndex.ToString()); e.SetAttribute("autoDownloadUpdates",AutoDownloadUpdates.ToString());
        string tmp=path+".tmp"; d.Save(tmp); if(File.Exists(path))File.Replace(tmp,path,null); else File.Move(tmp,path);
    }
    internal static string KeyName(uint mods,uint key) { return ((mods&2)!=0?"Ctrl + ":"")+((mods&1)!=0?"Alt + ":"")+((mods&4)!=0?"Shift + ":"")+((mods&8)!=0?"Win + ":"")+KeyCatalog.Name(key); }
}
}
