using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace MicControl {
static class TestSuite {
    static void Assert(bool value,string message) { if(!value)throw new Exception("FAIL: "+message); }
    internal static void Run(SettingsWindow window) {
        DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1.3) };
        timer.Tick+=delegate {
            timer.Stop(); string folder=AppDomain.CurrentDomain.BaseDirectory;
            try {
                Assert(AppInfo.Version=="1.0.0"&&AppInfo.DisplayVersion=="v1.0.0","version remains 1.0.0");
                Assert(AppInfo.AuthorTag=="@notyaffi","author tag");
                Assert(AppInfo.ProductName=="Mic Control"&&Preferences.PathName.IndexOf("Mic Control",StringComparison.Ordinal)>=0,"project rename is complete");
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
                    Assert(legacy.Key==123&&legacy.Modifiers==4&&!legacy.Overlay&&legacy.IndicatorScale==100&&legacy.PaletteIndex==0,"legacy preferences migrate without losing bindings");
                    legacy.IndicatorScale=175; legacy.PaletteIndex=4; legacy.Save(prefTest);
                    Preferences restored=Preferences.Load(prefTest);
                    Assert(restored.IndicatorScale==175&&restored.PaletteIndex==4&&restored.Key==123,"appearance persists after restart");
                    File.WriteAllText(prefTest,"<MicControl modifiers=\"3\" key=\"77\" overlay=\"True\" indicatorScale=\"999\" palette=\"99\" />");
                    Preferences bounded=Preferences.Load(prefTest); Assert(bounded.IndicatorScale==200&&bounded.PaletteIndex==0,"invalid appearance values are bounded");
                } finally { if(File.Exists(prefTest))File.Delete(prefTest); }
                int originalScale=window.Prefs.IndicatorScale,originalPalette=window.Prefs.PaletteIndex;
                window.sizeSlider.Value=50; Assert(window.overlay.Width==44 && window.overlay.Heart.Width==44,"slider resizes overlay to 50%");
                window.sizeSlider.Value=200; Assert(window.overlay.Width==176 && window.overlay.Heart.Width==176,"slider resizes overlay to 200%");
                window.paletteButtons[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(window.Prefs.PaletteIndex==4,"palette card changes selected colors");
                window.sizeSlider.Value=originalScale; window.paletteButtons[originalPalette].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                using(KeyboardHook hook=new KeyboardHook()) { }
                bool audio=Audio.Read();
                Capture(window,Path.Combine(folder,"settings-preview.png"));
                try { window.Toggle(); Assert(Audio.Read()!=audio,"settings button toggles microphone"); }
                finally { if(Audio.Read()!=audio)window.Toggle(); }
                Assert(Audio.Read()==audio,"microphone state restored");
                HeartView active=new HeartView(220); active.SetState(false); active.Measure(new Size(220,220)); active.Arrange(new Rect(0,0,220,220));
                HeartView broken=new HeartView(220); broken.SetState(true); broken.Measure(new Size(220,220)); broken.Arrange(new Rect(0,0,220,220));
                UniformGrid paletteSheet=new UniformGrid { Columns=5,Width=900,Height=365,Background=Theme.Brush("#110D19") };
                for(int i=0;i<Palette.All.Length;i++) {
                    StackPanel card=new StackPanel { Margin=new Thickness(5,15,5,5) }; TextBlock name=Theme.Label(Palette.All[i].Name,16,Theme.Text); name.HorizontalAlignment=HorizontalAlignment.Center; card.Children.Add(name);
                    foreach(bool off in new bool[]{false,true}) { HeartView sample=new HeartView(148); sample.SetState(off); sample.ApplyPalette(i); card.Children.Add(sample); }
                    paletteSheet.Children.Add(card);
                }
                paletteSheet.Measure(new Size(900,365)); paletteSheet.Arrange(new Rect(0,0,900,365));
                window.OpenEditor();
                DispatcherTimer second=new DispatcherTimer { Interval=TimeSpan.FromSeconds(.7) }; second.Tick+=delegate { second.Stop(); try {
                    Capture(window,Path.Combine(folder,"binding-preview.png")); Capture(active,Path.Combine(folder,"heart-active.png")); Capture(broken,Path.Combine(folder,"heart-muted.png")); Capture(paletteSheet,Path.Combine(folder,"palettes-preview.png"));
                    File.WriteAllText(Path.Combine(folder,"self-test.txt"),"PASS: keyboard state machine, repeat suppression, solo modifiers, Win+F12, Esc, media, numpad/Num Enter, OEM, editor input; 248 selectable key codes.\r\nPASS: legacy preferences, appearance save/load, invalid values, live size slider and palette selection.\r\nPASS: low-level hook installation and cleanup.\r\nPASS: Core Audio read, settings toggle, original state restored; muted="+audio+"\r\nPASS: English settings, binding editor and all 5 palettes in both states.\r\n");
                } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); } finally { window.Exit(); } }; second.Start();
            } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); window.Exit(); }
        }; timer.Start();
    }
    internal static void Capture(FrameworkElement element,string path) {
        element.UpdateLayout(); RenderTargetBitmap b=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32); b.Render(element);
        PngBitmapEncoder encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(b)); using(FileStream f=File.Create(path))encoder.Save(f);
    }
}
}
