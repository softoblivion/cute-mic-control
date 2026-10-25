using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SelfUpdate;
namespace MicControl {
static class TestSuite {
    static void Assert(bool value,string message) { if(!value)throw new Exception("FAIL: "+message); }
    internal static void Run(SettingsWindow window) {
        DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1.3) };
        timer.Tick+=delegate {
            timer.Stop(); string folder=AppPaths.SelfTest; Directory.CreateDirectory(folder);
            try {
                Assert(AppInfo.DisplayVersion=="v"+AppInfo.Version&&SemVer.Parse(AppInfo.Version)!=null,"version label matches Version.cs");
                Assert(AppInfo.AuthorTag=="softoblivion","author tag");
                Assert(AppInfo.ProductName=="Mic Control"&&AppInfo.AssetName=="MicControl.exe","product and release asset names");
                Assert(Preferences.PathName==Path.Combine(AppPaths.Data,"settings.xml")&&(AppPaths.Redirected||AppPaths.Data==Path.Combine(AppPaths.ExeDirectory,"cute-mic-control_data")),"settings live in the portable data folder");
                int count=0; KeyEngine e=new KeyEngine { Key=77,Modifiers=3,Fired=delegate { count++; } };
                e.Feed(162,false); e.Feed(164,false); e.Feed(77,false); e.Feed(77,false); e.Feed(77,true); e.Feed(164,true); e.Feed(162,true); Assert(count==1,"combo and auto-repeat");
                uint[] singles={27,32,123,160,162,164,91,96,179,44,186,4109};
                foreach(uint key in singles) { e=new KeyEngine { Key=key,Modifiers=0,Fired=delegate { count++; } }; int before=count; e.Feed(key,false); e.Feed(key,false); e.Feed(key,true); Assert(count==before+1,"single key "+key); }
                e=new KeyEngine { Key=162,Fired=delegate { throw new Exception("modifier chord fired solo"); },Modifiers=0 }; e.Feed(162,false); e.Feed(67,false); e.Feed(67,true); e.Feed(162,true);
                uint captured=0,mods=0; e=new KeyEngine(); e.Captured=delegate(uint m,uint k) { mods=m; captured=k; }; e.BeginCapture(); e.Feed(91,false); e.Feed(123,false); e.Feed(123,true); e.Feed(91,true); Assert(captured==123&&mods==8,"Win+F12 capture");
                e.BeginCapture(); e.Feed(27,false); e.Feed(27,true); Assert(captured==27&&mods==0,"Escape capture");
                e.BeginCapture(); e.Feed(163,false); e.Feed(163,true); Assert(captured==163&&mods==0,"solo modifier capture");
                e=new KeyEngine { Key=32,Modifiers=0,Suspended=true,Fired=delegate { throw new Exception("binding consumed search input"); } }; Assert(!e.Feed(32,false),"editor input"); e.Feed(32,true);
                Assert(KeyCatalog.All().Count==248,"key catalog");
                string prefTest=Path.Combine(Path.GetTempPath(),"miccontrol-prefs-"+Guid.NewGuid().ToString("N")+".xml");
                try {
                    File.WriteAllText(prefTest,"<MicMute modifiers=\"4\" key=\"123\" overlay=\"False\" />");
                    Preferences legacy=Preferences.Load(prefTest);
                    Assert(legacy.Key==123&&legacy.Modifiers==4&&!legacy.Overlay&&legacy.IndicatorScale==100&&legacy.PaletteIndex==0&&legacy.AutoDownloadUpdates&&legacy.Theme=="bloom","legacy preferences migrate without losing bindings");
                    Assert(legacy.UiScale==100,"older settings files open at 100 % scale");
                    legacy.IndicatorScale=175; legacy.PaletteIndex=4; legacy.AutoDownloadUpdates=false; legacy.Theme="frost"; legacy.UiScale=150; legacy.Save(prefTest);
                    Preferences restored=Preferences.Load(prefTest);
                    Assert(restored.IndicatorScale==175&&restored.PaletteIndex==4&&restored.Key==123&&!restored.AutoDownloadUpdates&&restored.Theme=="frost"&&restored.UiScale==150,"appearance, scale and update settings persist after restart");
                    Assert(File.ReadAllText(prefTest).Contains("ui_scale=\"150\""),"scale stored as ui_scale");
                    File.WriteAllText(prefTest,"<MicControl modifiers=\"3\" key=\"77\" overlay=\"True\" indicatorScale=\"999\" palette=\"99\" theme=\"sunset\" ui_scale=\"999\" />");
                    Preferences bounded=Preferences.Load(prefTest); Assert(bounded.IndicatorScale==200&&bounded.PaletteIndex==0&&bounded.Theme=="bloom"&&bounded.UiScale==200,"invalid appearance values are bounded");
                    Assert(UiScale.Normalize(137)==125&&UiScale.Normalize(-5)==100&&UiScale.Normalize(0)==100&&UiScale.Normalize(176)==175,"scale values snap to the nearest step");
                } finally { if(File.Exists(prefTest))File.Delete(prefTest); }
                int originalScale=window.Prefs.IndicatorScale,originalPalette=window.Prefs.PaletteIndex;
                window.sizeSlider.Value=50; Assert(window.overlay.Width==44 && window.overlay.Heart.Width==44,"slider resizes overlay to 50%");
                window.sizeSlider.Value=200; Assert(window.overlay.Width==176 && window.overlay.Heart.Width==176,"slider resizes overlay to 200%");
                window.paletteButtons[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(window.Prefs.PaletteIndex==4,"palette card changes selected colors");
                window.sizeSlider.Value=originalScale; window.paletteButtons[originalPalette].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string originalTheme=window.Prefs.Theme; window.themeButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(window.Prefs.Theme=="dusk"&&window.Look.Current.Name=="dusk","theme chip applies the theme");
                window.themeButtons[Themes.IndexOf(originalTheme)].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach(int w in new int[]{450,550,600,650})Assert(TypeRoles.Embedded(TypeRoles.TextFamily,"Sora",FontWeight.FromOpenTypeWeight(w)),"Sora "+w+" comes from the embedded resources: "+TypeRoles.Describe(TypeRoles.TextFamily,FontWeight.FromOpenTypeWeight(w)));
                Assert(TypeRoles.Embedded(TypeRoles.MonoFamily,"Geist Mono",FontWeight.FromOpenTypeWeight(450)),"Geist Mono comes from the embedded resources");
                using(KeyboardHook hook=new KeyboardHook()) { }
                bool audio=Audio.Read();
                window.UpdateLayout(); Capture(window,Path.Combine(folder,"settings-preview.png"),2);
                try { window.Toggle(); Assert(Audio.Read()!=audio,"settings button toggles microphone"); }
                finally { if(Audio.Read()!=audio)window.Toggle(); }
                Assert(Audio.Read()==audio,"microphone state restored");
                HeartView active=new HeartView(220); active.SetState(false); active.Measure(new Size(220,220)); active.Arrange(new Rect(0,0,220,220));
                HeartView broken=new HeartView(220); broken.SetState(true); broken.Measure(new Size(220,220)); broken.Arrange(new Rect(0,0,220,220));
                UniformGrid paletteSheet=new UniformGrid { Columns=5,Width=900,Height=365,Background=new SolidColorBrush(window.Look.Current.BgMid) };
                for(int i=0;i<Palette.All.Length;i++) {
                    StackPanel card=new StackPanel { Margin=new Thickness(5,15,5,5) }; TextBlock name=Theme.Label(Palette.All[i].Name,TypeRoles.Title,Theme.Text); name.HorizontalAlignment=HorizontalAlignment.Center; card.Children.Add(name);
                    foreach(bool off in new bool[]{false,true}) { HeartView sample=new HeartView(148); sample.SetState(off); sample.ApplyPalette(i); card.Children.Add(sample); }
                    paletteSheet.Children.Add(card);
                }
                paletteSheet.Measure(new Size(900,365)); paletteSheet.Arrange(new Rect(0,0,900,365));
                window.OpenSheet(); window.UpdateLayout();
                double[] sizes=TypeRoles.All.Select(r=>r.Size).ToArray(); int texts=0;
                foreach(TextBlock t in Descendants<TextBlock>(window)) { texts++; Assert((t.FontFamily==TypeRoles.TextFamily||t.FontFamily==TypeRoles.MonoFamily)&&sizes.Contains(t.FontSize),"text uses a type role: '"+t.Text+"' "+t.FontFamily+" "+t.FontSize); }
                Assert(texts>30,"type roles checked on every text ("+texts+")");
                Assert(window.TitleBar.Pill.Content!=null&&System.Windows.Automation.AutomationProperties.GetName(window.TitleBar.Pill)=="Version "+AppInfo.DisplayVersion,"title bar version pill");
                Assert(window.TitleBar.ActualHeight==52&&(string)window.TitleBar.Gear.Tag=="on","title bar height and the gear marks the open sheet");
                int originalUi=window.Prefs.UiScale;
                window.scaleControl.Options[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ScaleTransform zoom=window.root.LayoutTransform as ScaleTransform;
                Assert(window.Prefs.UiScale==150&&window.AppliedScale==150&&zoom!=null&&zoom.ScaleX==1.5&&window.Width==SettingsWindow.ContentWidth*1.5+2*SettingsWindow.Edge&&window.Height==SettingsWindow.ContentHeight*1.5+2*SettingsWindow.Edge,"scale 150 % scales the content and the window");
                window.scaleControl.Options[Array.IndexOf(UiScale.Steps,UiScale.Normalize(originalUi))].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Prefs.UiScale=originalUi; window.ApplyScale(false);
                Action<double,Action> after=delegate(double seconds,Action step) { DispatcherTimer t=new DispatcherTimer { Interval=TimeSpan.FromSeconds(seconds) }; t.Tick+=delegate { t.Stop(); try { step(); } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); window.Exit(); } }; t.Start(); };
                // The Scale thumb travels with the spring: sampled every 25 ms it passes intermediate positions, overshoots once and settles.
                List<double> track=new List<double>(); int home=window.scaleControl.Index; DispatcherTimer sampler=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(25) };
                sampler.Tick+=delegate { track.Add(window.scaleControl.ThumbX); };
                after(.8,delegate { window.scaleControl.Select(4,true); sampler.Start(); });
                after(1.8,delegate {
                    sampler.Stop(); double final=window.scaleControl.ThumbX,start=track.Count>0?track[0]:0;
                    Assert(track.Count(v=>v>start+1&&v<final-1)>=2&&track.Max()>final+.5&&Math.Abs(track[track.Count-1]-final)<.01,"Scale thumb moves with the spring ("+string.Join(", ",track.Select(v=>v.ToString("0.0")).ToArray())+")");
                    window.scaleControl.Select(home,false);
                    Capture(window,Path.Combine(folder,"settings-sheet-preview.png"),2);
                    window.CloseSheet(); Assert(!window.SheetOpen&&window.TitleBar.Gear.Tag==null,"sheet closes");
                    window.OpenEditor();
                    after(.7,delegate {
                        Capture(window,Path.Combine(folder,"binding-preview.png"),2); Capture(active,Path.Combine(folder,"heart-active.png")); Capture(broken,Path.Combine(folder,"heart-muted.png")); Capture(paletteSheet,Path.Combine(folder,"palettes-preview.png"));
                        File.WriteAllText(Path.Combine(folder,"self-test.txt"),"PASS: keyboard state machine, repeat suppression, solo modifiers, Win+F12, Esc, media, numpad/Num Enter, OEM, editor input; 248 selectable key codes.\r\nPASS: legacy preferences, appearance, theme and update settings save/load, invalid values, live size slider, palette and theme selection.\r\nPASS: low-level hook installation and cleanup.\r\nPASS: Core Audio read, settings toggle, original state restored; muted="+audio+"\r\nPASS: English settings, binding editor and all 5 palettes in both states.\r\nPASS: embedded Sora and Geist Mono, every text on a type role, title bar with version pill and gear, settings sheet, Scale 150 % on content and window, ui_scale save/load and bounds.\r\n");
                        window.Exit();
                    });
                });
            } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); window.Exit(); }
        }; timer.Start();
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { DependencyObject child=VisualTreeHelper.GetChild(parent,i); T match=child as T; if(match!=null)yield return match; foreach(T inner in Descendants<T>(child))yield return inner; }
    }
    internal static void Capture(FrameworkElement element,string path) { Capture(element,path,1); }
    internal static void Capture(FrameworkElement element,string path,double scale) {
        element.UpdateLayout(); RenderTargetBitmap b=new RenderTargetBitmap((int)Math.Round(element.ActualWidth*scale),(int)Math.Round(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32); b.Render(element);
        PngBitmapEncoder encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(b)); using(FileStream f=File.Create(path))encoder.Save(f);
    }
}
}
