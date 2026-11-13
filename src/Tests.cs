using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SelfUpdate;
namespace MicControl {
static class TestSuite {
    static void Assert(bool value,string message) { if(!value)throw new Exception("FAIL: "+message); }
    static void Click(UIElement e) { e.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
    // A card shows its endpoint: the mute face, the state text, the device, the level and the hold.
    static bool CardShows(FlowCard c,EndpointState s,IAudioSource source) {
        if(!s.Present)return c.Mute.Shown==MuteLook.None&&!c.Device.IsEnabled&&c.Device.Text=="No device";
        bool mic=c.Flow==Flow.Capture; int p=AudioHub.Percent(s.Volume);
        MuteLook look=mic?(s.Muted?MuteLook.Muted:MuteLook.Live):(s.Muted?MuteLook.SpeakerMuted:MuteLook.SpeakerOn);
        return c.Mute.Shown==look&&c.State.Text==(s.Muted?"Muted":mic?"Live":"On")&&c.Device.Text==s.Name&&(int)c.Level.Value==p&&c.Value.Text==p.ToString()&&c.Lock.Held==source.IsLocked(c.Flow)&&c.Last.Id==s.Id;
    }
    static string Describe(FlowCard c) { return c.Mute.Shown+" '"+c.State.Text+"' '"+c.Device.Text+"' "+c.Value.Text+(c.Lock.Held?" held":""); }
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
                    Assert(bounded.Locks.Count==0,"a file without locks loads as no locks");
                    string micId="{0.0.1.00000000}.{11111111-2222-3333-4444-555555555555}",speakerId="{0.0.0.00000000}.{66666666-7777-8888-9999-AAAAAAAAAAAA}";
                    Preferences locked=new Preferences(); locked.Locks[micId]=37; locked.Locks[speakerId]=0; locked.Save(prefTest); Preferences relocked=Preferences.Load(prefTest);
                    Assert(relocked.Locks.Count==2&&relocked.Locks[micId]==37&&relocked.Locks[speakerId]==0&&File.ReadAllText(prefTest).Contains("locks=\""+micId+"=37|"+speakerId+"=0\""),"volume locks persist after restart as locks=\"id=percent|...\"");
                    File.WriteAllText(prefTest,"<MicControl modifiers=\"3\" key=\"77\" overlay=\"True\" locks=\"garbage|"+micId+"=101|=5|{y}=abc|{z}=-1|"+speakerId+"=42||\" />");
                    Preferences damaged=Preferences.Load(prefTest); Assert(damaged.Locks.Count==1&&damaged.Locks[speakerId]==42&&damaged.Key==77,"broken lock entries load as no lock and keep the rest");
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
                // The console on the audio engine: the real default devices; every change is undone in finally.
                Assert(window.MeterRunning&&(!window.Hub.State(Flow.Capture).Present||window.Hub.MeterStreamOpen),"meters run while the window is visible");
                List<string> console=new List<string>(),skipped=new List<string>(); List<FlowCard> live=new List<FlowCard>();
                foreach(FlowCard c in new FlowCard[]{window.MicCard,window.SpeakerCard}) {
                    string name=c.Flow==Flow.Capture?"microphone":"speakers"; window.RefreshAudio(true); EndpointState s=window.Hub.State(c.Flow);
                    Assert(CardShows(c,s,window.Source),name+" card shows the engine state: "+Describe(c));
                    if(!s.Present) { skipped.Add("SKIP: "+name+" card controls: no default device on this machine."); continue; }
                    live.Add(c);
                    if(c.Flow==Flow.Capture)Assert(((string)c.Mute.ToolTip).EndsWith("microphone ("+Preferences.KeyName(window.Prefs.Modifiers,window.Prefs.Key)+")"),"microphone mute tooltip names the hotkey: "+c.Mute.ToolTip);
                    try { Click(c.Mute); EndpointState m=window.Hub.State(c.Flow); Assert(m.Muted==!s.Muted&&CardShows(c,m,window.Source),name+" mute button toggles the endpoint: "+Describe(c)); }
                    finally { if(window.Hub.State(c.Flow).Muted!=s.Muted)window.Hub.SetMute(c.Flow,s.Muted); window.RefreshAudio(false); }
                    Assert(window.Hub.State(c.Flow).Muted==s.Muted&&CardShows(c,window.Hub.State(c.Flow),window.Source),name+" mute restored");
                    int p0=AudioHub.Percent(s.Volume),target=p0>50?p0-10:p0+10;
                    try { c.Level.Value=target; Assert(AudioHub.Percent(window.Hub.State(c.Flow).Volume)==target&&c.Value.Text==target.ToString(),name+" level slider reaches the endpoint"); }
                    finally { window.Hub.SetVolume(c.Flow,s.Volume); window.RefreshAudio(false); }
                    Assert(Math.Abs(window.Hub.State(c.Flow).Volume-s.Volume)<0.002f&&(int)c.Level.Value==p0,name+" level restored");
                    List<AudioDevice> devices=window.Hub.Devices(c.Flow); window.ToggleList(c);
                    Assert(window.DeviceList.IsOpen&&window.DeviceList.Owner==c&&c.Device.IsOpen&&window.DeviceList.Count==devices.Count&&window.DeviceList.Selected==devices.FindIndex(d=>d.IsDefault)&&window.DeviceList.RowName(window.DeviceList.Selected)==s.Name,name+" device list opens with the engine's devices, the default marked");
                    window.ToggleList(c); Assert(!window.DeviceList.IsOpen&&!c.Device.IsOpen,name+" device button closes the list");
                    console.Add(name+" ("+devices.Count+" device(s))");
                }
                bool? listening=window.Hub.Listening; Assert(window.ListenToggle.IsChecked==(listening==true)&&window.ListenRow.IsEnabled==listening.HasValue,"Listen switch shows the engine's value ("+listening+")");
                // The Desktop indicator switch of the sheet: saved, shows or hides the indicator, enables the size row.
                bool overlayShown=window.overlay.IsVisible;
                foreach(bool on in new bool[]{!window.Prefs.Overlay,window.Prefs.Overlay}) {
                    window.OverlayToggle.IsChecked=on; Click(window.OverlayToggle);
                    Assert(window.Prefs.Overlay==on&&Preferences.Load().Overlay==on&&window.overlay.IsVisible==on&&window.SizeRow.IsEnabled==on&&window.sizeSlider.IsEnabled==on&&window.SizeRow.Opacity==(on?1:.38),"Desktop indicator switch "+(on?"on":"off")+" is saved and drives the indicator and the size row");
                }
                if(!overlayShown)window.overlay.Hide();
                // The sample state: invented devices, no write reaches Windows.
                SampleAudio sample=new SampleAudio(); window.UseSample(sample);
                Assert(CardShows(window.MicCard,sample.State(Flow.Capture),sample)&&CardShows(window.SpeakerCard,sample.State(Flow.Render),sample)&&window.MicCard.Level.LitSegments>0&&window.MicCard.State.Text=="Live","sample state renders: "+Describe(window.MicCard)+" / "+Describe(window.SpeakerCard));
                window.ListenToggle.IsChecked=true; Click(window.ListenToggle); Assert(sample.Listen==true&&window.ListenToggle.IsChecked==true,"Listen switch writes");
                sample.Fail=true; window.ListenToggle.IsChecked=false; Click(window.ListenToggle);
                Assert(sample.Listen==true&&window.ListenToggle.IsChecked==true&&window.Notice=="Could not change Listen.","a refused Listen write puts the switch back and says so ("+window.Notice+")");
                window.CloseNotice(); Assert(window.Notice=="","the notice closes");
                sample.Listen=null; window.UseSample(sample); Assert(window.ListenToggle.IsChecked==false&&!window.ListenRow.IsEnabled&&window.ListenHint=="Not available for this device","Listen without the setting is disabled with its hint");
                sample.Listen=false; window.UseSample(sample); Assert(window.ListenRow.IsEnabled&&window.ListenHint=="Play this microphone through the speakers","Listen hint returns");
                window.ToggleList(window.MicCard); Click(window.DeviceList.Rows[2]);
                Assert(window.Notice=="Could not change the default device."&&sample.State(Flow.Capture).Name=="Microphone (Studio USB)","a refused device change says so ("+window.Notice+")");
                sample.Fail=false; window.CloseNotice(); window.DeviceList.Close();
                window.ToggleList(window.SpeakerCard); Assert(window.DeviceList.IsOpen&&window.DeviceList.Count==2&&window.DeviceList.Selected==0,"speakers list of the sample");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
                Assert(!window.DeviceList.IsOpen&&!window.SpeakerCard.Device.IsOpen,"Esc closes the device list");
                Click(window.SpeakerCard.Lock); Assert(sample.IsLocked(Flow.Render)&&window.SpeakerCard.Lock.Held&&window.SpeakerCard.Level.Held&&(string)window.SpeakerCard.Lock.ToolTip=="Held at 64. Click to release.","sample lock holds: "+window.SpeakerCard.Lock.ToolTip);
                Click(window.SpeakerCard.Lock); Assert(!sample.IsLocked(Flow.Render)&&!window.SpeakerCard.Lock.Held&&(string)window.SpeakerCard.Lock.ToolTip=="Hold this level. Changes made by Windows or other apps are undone.","sample lock releases");
                // Previews from the sample: the live console, muted + held + listening, and the four themes.
                window.UseSample(sample); window.UpdateLayout(); Capture(window,Path.Combine(folder,"main-live.png"),2);
                string themeBefore=window.Prefs.Theme;
                foreach(ThemeSpec t in Themes.All) { window.Prefs.Theme=t.Name; window.ApplyTheme(false); Capture(window,Path.Combine(folder,"theme-"+t.Name+".png"),2); }
                window.Prefs.Theme=themeBefore; window.ApplyTheme(false); window.UseSample(null);
                // The layout fits: the speakers card ends above the footer (28 high, 10 from the bottom).
                FrameworkElement speakerHost=(FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(window.SpeakerCard.Mute)));
                double cardsEnd=speakerHost.TransformToAncestor(window.content).Transform(new Point(0,speakerHost.ActualHeight)).Y;
                Assert(cardsEnd<=window.content.ActualHeight-38,"the cards end above the footer ("+cardsEnd+" of "+window.content.ActualHeight+")");
                HeartView active=new HeartView(220); active.SetState(false); active.Measure(new Size(220,220)); active.Arrange(new Rect(0,0,220,220));
                HeartView broken=new HeartView(220); broken.SetState(true); broken.Measure(new Size(220,220)); broken.Arrange(new Rect(0,0,220,220));
                UniformGrid paletteSheet=new UniformGrid { Columns=5,Width=900,Height=365,Background=new SolidColorBrush(window.Look.Current.BgMid) };
                for(int i=0;i<Palette.All.Length;i++) {
                    StackPanel card=new StackPanel { Margin=new Thickness(5,15,5,5) }; TextBlock name=Theme.Label(Palette.All[i].Name,TypeRoles.Title,Theme.Text); name.HorizontalAlignment=HorizontalAlignment.Center; card.Children.Add(name);
                    foreach(bool off in new bool[]{false,true}) { HeartView heart=new HeartView(148); heart.SetState(off); heart.ApplyPalette(i); card.Children.Add(heart); }
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
                FrameworkElement[] marks={window.MicCard.Mute,window.MicCard.Device,window.MicCard.Lock,window.ListenToggle,window.SpeakerCard.Level,window.SpeakerCard.Lock};
                Func<FrameworkElement,Point> logical=delegate(FrameworkElement m) { window.UpdateLayout(); Point p=m.TransformToAncestor(window).Transform(new Point(0,0)); double f=window.AppliedScale/100.0; return new Point((p.X-SettingsWindow.Edge)/f,(p.Y-SettingsWindow.Edge)/f); };
                Point[] atStart=marks.Select(logical).ToArray();
                window.scaleControl.Options[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); int largest=window.AppliedScale;
                for(int i=0;i<marks.Length;i++) { Point p=logical(marks[i]); Assert(Math.Abs(p.X-atStart[i].X)<.5&&Math.Abs(p.Y-atStart[i].Y)<.5,"Scale "+largest+" % keeps the layout: "+marks[i].GetType().Name+" "+atStart[i]+" -> "+p); }
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
                        window.CloseEditor();
                        // Hold on the engine: the lock button holds the current level, the settings file keeps it, a second click releases it.
                        foreach(FlowCard c in live) { Click(c.Lock); Assert(window.Hub.IsLocked(c.Flow)&&c.Lock.Held&&c.Level.Held&&((string)c.Lock.ToolTip).StartsWith("Held at "),c.Flow+" lock button holds: "+c.Lock.ToolTip); }
                        string dragLine="";
                        after(.5,delegate {
                            foreach(FlowCard c in live) { string id=window.Hub.State(c.Flow).Id; Assert(window.Prefs.Locks.ContainsKey(id)&&Preferences.Load().Locks.ContainsKey(id),c.Flow+" lock is saved"); }
                            DragChecks(window,live,after,delegate(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); window.Exit(); },delegate(string drag) {
                            dragLine=drag;
                            foreach(FlowCard c in live) { Click(c.Lock); Assert(!window.Hub.IsLocked(c.Flow)&&!c.Lock.Held&&!c.Level.Held,c.Flow+" lock button releases"); }
                            after(.5,delegate {
                                Assert(window.Prefs.Locks.Count==0&&Preferences.Load().Locks.Count==0,"released locks leave the settings file");
                                // The switch template animates its knob, so this preview waits for it.
                                sample.SetMuted(Flow.Capture,true); sample.SetLock(Flow.Render,true); sample.Listen=true; window.UseSample(sample);
                                Assert(window.MicCard.Mute.Shown==MuteLook.Muted&&window.SpeakerCard.Lock.Held&&window.ListenToggle.IsChecked==true,"muted, held and listening sample");
                                after(.5,delegate {
                                Capture(window,Path.Combine(folder,"main-muted-held-listening.png"),2);
                                sample.SetMuted(Flow.Capture,false); sample.SetLock(Flow.Render,false); sample.Listen=false; window.UseSample(sample); window.ToggleList(window.MicCard);
                                after(.5,delegate {
                                    Assert(window.DeviceList.IsOpen&&window.DeviceList.Count==3&&window.DeviceList.Selected==0,"sample microphone list opens");
                                    Capture(window,Path.Combine(folder,"main-devices.png"),2);
                                    // Keys need the window to hold the keyboard focus; a self-test started in the background does not get it.
                                    bool keys=window.IsActive&&Keyboard.FocusedElement==window.DeviceList.Rows[0];
                                    if(keys) {
                                        window.DeviceList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Down) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
                                        Assert(Keyboard.FocusedElement==window.DeviceList.Rows[1],"Down moves to the next device");
                                        window.DeviceList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Enter) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
                                    } else window.DeviceList.Choose(1);
                                    Assert(window.DeviceList.Selected==1&&sample.State(Flow.Capture).Name=="Headset Microphone (Wireless)","choosing a device sets it as the default");
                                    after(.45,delegate {
                                        Assert(!window.DeviceList.IsOpen&&!window.MicCard.Device.IsOpen&&window.MicCard.Device.Text=="Headset Microphone (Wireless)","the list closes after a choice and the card shows the new default");
                                        window.UseSample(null);
                                        window.Hide(); Assert(!window.MeterRunning,"hiding the window stops the meters"); // the release of the stream: first audio engine step
                                        AudioChecks.Run(window.Dispatcher,delegate(string engine) {
                                            File.WriteAllText(Path.Combine(folder,"self-test.txt"),"PASS: keyboard state machine, repeat suppression, solo modifiers, Win+F12, Esc, media, numpad/Num Enter, OEM, editor input; 248 selectable key codes.\r\nPASS: legacy preferences, appearance, theme and update settings save/load, invalid values, live size slider, palette and theme selection, volume locks save/load and broken entries.\r\nPASS: low-level hook installation and cleanup.\r\n"
                                                +"PASS: main window on the audio engine: "+(console.Count>0?string.Join(", ",console.ToArray()):"no device")+": cards show the engine state, mute toggles and is restored, a level change reaches the endpoint and is restored, the device list opens with the engine's devices and closes; hold and release with the settings file; Listen shows "+(listening.HasValue?listening.Value.ToString():"not available")+"; meters run while visible and stop when hidden; "+dragLine+".\r\n"
                                                +"PASS: main window on the sample: card states, Listen write, refused write and unavailable, refused device change, device choice "+(keys?"by keys":"by Choose (keys not checked: the window had no keyboard focus)")+" with the list closing, Esc, hold tooltips, Desktop indicator switch round trip, Scale "+largest+" % keeps the layout, the cards end above the footer.\r\n"
                                                +string.Join("",skipped.Select(x=>x+"\r\n").ToArray())+engine
                                                +"PASS: English settings, previews (main-live, main-muted-held-listening, main-devices, theme-dusk/nova/frost/bloom, settings sheet, binding editor), all 5 palettes in both states.\r\nPASS: embedded Sora and Geist Mono, every text on a type role, title bar with version pill and gear, settings sheet, Scale 150 % on content and window, ui_scale save/load and bounds.\r\n");
                                            window.Exit();
                                        },delegate(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); window.Exit(); });
                                    });
                                });
                                });
                            });
                            });
                        });
                    });
                });
            } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"self-test.txt"),ex.ToString()); window.Exit(); }
        }; timer.Start();
    }
    // A slider drag of 60 one-percent steps (30 away, 30 back) on a held level: the hold follows every step and the settings
    // file is written at most twice; then a refused save shows its notice once. The level ends where it started.
    static void DragChecks(SettingsWindow window,List<FlowCard> live,Action<double,Action> after,Action<Exception> fail,Action<string> next) {
        FlowCard c=live.FirstOrDefault(x=>x.Flow==Flow.Capture)??live.FirstOrDefault();
        if(c==null) { next("slider drag not checked: no device"); return; }
        Flow flow=c.Flow; EndpointState s=window.Hub.State(flow); float v0=s.Volume; string id=s.Id,file=Preferences.PathName;
        int start=(int)c.Level.Value,dir=start>=30?-1:1,far=start,step=0; int[] writes=new int[1];
        FileSystemWatcher watch=new FileSystemWatcher(Path.GetDirectoryName(file),Path.GetFileName(file)+".tmp");
        watch.Created+=delegate { System.Threading.Interlocked.Increment(ref writes[0]); }; watch.EnableRaisingEvents=true;
        Action restore=delegate {
            watch.Dispose(); try { File.SetAttributes(file,File.GetAttributes(file)&~FileAttributes.ReadOnly); } catch { }
            if(c.Level.Dragging)c.Level.EndDrag(); try { window.Hub.SetVolume(flow,v0); } catch { } window.RefreshAudio(false);
        };
        Action<Action> guard=delegate(Action body) { try { body(); } catch(Exception ex) { restore(); fail(ex); } };
        DispatcherTimer t=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(15) };
        c.Level.BeginDrag();
        t.Tick+=delegate { guard(delegate {
            if(step<60) { step++; c.Level.Value=start+dir*(step<=30?step:60-step); if(step==30)far=window.Hub.Locks[id]; return; }
            t.Stop(); c.Level.EndDrag(); // in the tick after the last step, as a mouse release follows the last move
            after(.6,delegate { guard(delegate {
                int n=writes[0];
                Assert(n>=1&&n<=2,"a 60-step drag on a held level writes the settings file at most twice ("+n+" writes)");
                Assert(far==start+dir*30&&window.Prefs.Locks[id]==start&&Preferences.Load().Locks[id]==start,"the hold follows the drag to "+far+" and back, and the file keeps "+start);
                File.SetAttributes(file,File.GetAttributes(file)|FileAttributes.ReadOnly); window.CloseNotice();
                c.Level.BeginDrag(); c.Level.Value=start+dir;
                after(.1,delegate { guard(delegate {
                    c.Level.EndDrag(); string first=window.Notice; window.CloseNotice();
                    Assert(first=="Could not save settings. Check folder permissions.","a refused save says so ("+first+")");
                    c.Level.BeginDrag(); c.Level.Value=start;
                    after(.1,delegate { guard(delegate {
                        c.Level.EndDrag(); Assert(window.Notice=="","a second refused save shows no second notice ("+window.Notice+")");
                        File.SetAttributes(file,File.GetAttributes(file)&~FileAttributes.ReadOnly); window.CommitLocks();
                        Assert(Preferences.Load().Locks[id]==start&&window.Notice=="","the save succeeds once the file is writable again");
                        restore(); next("a 60-step slider drag on a held level wrote the settings file "+n+" time(s), a refused save showed its notice once");
                    }); });
                }); });
            }); });
        }); };
        t.Start();
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
// AudioHub on the live default devices. Every change is undone: the undo list runs in reverse after the last step and on
// any failure, the synchronous checks before the first step included. Steps run on the UI thread every 100 ms, since lock
// enforcement and events arrive through the dispatcher. Failures of Windows calls come from the hub's Fault seam.
static class AudioChecks {
    static void Assert(bool value,string message) { if(!value)throw new Exception("FAIL: audio engine: "+message); }
    static readonly Guid Foreign=new Guid("3B0F9C2A-5D4E-4A71-8E26-91C7D0F4B385");
    // MIC_CONTROL_TEST_FAULT=volume|mute|roles fails the check right after that change, which proves that the undo list runs.
    static void Seeded(string point) { if(string.Equals(Environment.GetEnvironmentVariable("MIC_CONTROL_TEST_FAULT"),point,StringComparison.OrdinalIgnoreCase))throw new Exception("FAIL: audio engine: seeded failure after the "+point+" change (MIC_CONTROL_TEST_FAULT)"); }
    static double Since(DateTime t) { return (DateTime.UtcNow-t).TotalSeconds; }
    internal static void Run(Dispatcher dispatcher,Action<string> done,Action<Exception> fail) {
        Action<Action> post=delegate(Action a) { dispatcher.BeginInvoke(a); };
        AudioHub hub=new AudioHub(post); List<AudioHub> probes=new List<AudioHub>();
        List<string> pass=new List<string>(),skip=new List<string>(); List<Action> undo=new List<Action>(); List<Func<bool>> steps=new List<Func<bool>>();
        Action cleanup=delegate { for(int i=undo.Count-1;i>=0;i--)try { undo[i](); } catch { } hub.Dispose(); foreach(AudioHub p in probes)p.Dispose(); };
        try { Build(hub,post,probes,pass,skip,undo,steps); }
        catch(Exception ex) { cleanup(); fail(ex); return; }
        int index=0; DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        timer.Tick+=delegate {
            try {
                while(index<steps.Count&&steps[index]())index++; if(index<steps.Count)return;
                timer.Stop(); cleanup(); done("PASS: audio engine: "+string.Join("; ",pass.ToArray())+".\r\n"+string.Join("",skip.Select(x=>x+"\r\n").ToArray()));
            } catch(Exception ex) { timer.Stop(); cleanup(); fail(ex); }
        };
        timer.Start();
    }
    static void Build(AudioHub hub,Action<Action> post,List<AudioHub> probes,List<string> pass,List<string> skip,List<Action> undo,List<Func<bool>> steps) {
        int[] restored=new int[2],lockFailed=new int[2]; int locksChanged=0;
        hub.LockRestored+=delegate(Flow f) { restored[(int)f]++; }; hub.LockFailed+=delegate(Flow f) { lockFailed[(int)f]++; }; hub.LocksChanged+=delegate { locksChanged++; };
        // Device choice writes three roles; a refused role puts back the ones already written, newest first.
        List<string> calls=new List<string>(); bool thrown=false;
        try { AudioHub.SetRoles("N",new string[]{"A","B","A"},delegate(string d,int r) { calls.Add(d+r); if(d=="N"&&r==2)throw new InvalidOperationException("refused"); }); } catch(InvalidOperationException) { thrown=true; }
        Assert(thrown&&string.Join(",",calls.ToArray())=="N0,N1,N2,B1,A0","a refused role writes back the roles already changed: "+string.Join(",",calls.ToArray()));
        // The window hid before these checks: Windows' microphone privacy record must show its capture stream stopped.
        string record=Privacy.Key(); DateTime hidden=DateTime.UtcNow; string windowRelease="";
        steps.Add(delegate {
            bool? use=Privacy.InUse(record);
            if(use==null) { windowRelease="hidden window: no Windows privacy record, engine flag only"; return true; }
            if(use==false) { windowRelease="hidden window released the microphone (privacy record, "+(int)(Since(hidden)*1000)+" ms)"; return true; }
            Assert(Since(hidden)<2,"the hidden window releases the microphone within 2 s (Windows still lists the app as using it)"); return false;
        });
        bool capturePresent=false,captureLive=false; Flow lockFlow=Flow.Capture; bool lockFlowSet=false; float lockV0=0;
        foreach(Flow flow in new Flow[]{Flow.Capture,Flow.Render}) {
            EndpointState s=hub.State(flow); string name=flow==Flow.Render?"playback":"recording";
            if(!s.Present) { skip.Add("SKIP: audio engine "+name+" checks: no default "+name+" device on this machine."); continue; }
            if(flow==Flow.Capture) { capturePresent=true; captureLive=!s.Muted&&s.Volume>0.01f; }
            List<AudioDevice> list=hub.Devices(flow);
            Assert(list.Count>0&&list.Count(d=>d.Id==s.Id&&d.IsDefault)==1&&list.Count(d=>d.IsDefault)==1&&list.All(d=>!string.IsNullOrEmpty(d.Name)&&!string.IsNullOrEmpty(d.Id)),name+" device list holds the default once, with names");
            Assert(!string.IsNullOrEmpty(s.Name)&&s.Volume>=0&&s.Volume<=1,name+" state");
            float v0=s.Volume; bool m0=s.Muted; string id=s.Id; if(!lockFlowSet) { lockFlow=flow; lockFlowSet=true; lockV0=v0; }
            undo.Add(delegate { hub.SetVolume(flow,v0); }); undo.Add(delegate { hub.SetMute(flow,m0); });
            float target=v0>0.5f?v0-0.1f:v0+0.1f; hub.SetVolume(flow,target); Seeded("volume");
            Assert(Math.Abs(hub.State(flow).Volume-target)<0.01f&&Math.Abs(Separate.Read(flow)-target)<0.01f,name+" SetVolume reaches the endpoint");
            hub.SetVolume(flow,v0); Assert(Math.Abs(hub.State(flow).Volume-v0)<0.002f,name+" volume restored");
            bool m1=hub.ToggleMute(flow); Seeded("mute"); Assert(m1==!m0&&hub.State(flow).Muted==m1,name+" ToggleMute"); hub.SetMute(flow,m0); Assert(hub.State(flow).Muted==m0,name+" mute restored");
            string[] roles=Separate.Defaults(flow); undo.Add(delegate { for(int r=0;r<3;r++)if(roles[r]!=null&&Separate.Defaults(flow)[r]!=roles[r])Policy.SetDefault(roles[r],r); });
            hub.SetDefault(flow,id); Seeded("roles"); Assert(hub.State(flow).Id==id&&Separate.Defaults(flow)[0]==id,name+" SetDefault to the current default keeps it");
            bool refused=false; hub.Fault=delegate(string step) { return step=="role 1"; };
            try { hub.SetDefault(flow,id); } catch(System.Runtime.InteropServices.COMException) { refused=true; } finally { hub.Fault=null; }
            Assert(refused&&Separate.Defaults(flow).SequenceEqual(roles)&&hub.State(flow).Id==id,name+" a refused role fails SetDefault and leaves the roles as they were");
            pass.Add(name+": "+list.Count+" device(s), volume and mute round trips, SetDefault(current), a refused role");
            // Lock: hold the level, a foreign write must come back within 2 s with LockRestored; SetVolume moves the lock instead.
            DateTime started=DateTime.MinValue; int percent=0; int before=0;
            steps.Add(delegate {
                undo.Add(delegate { hub.SetLock(flow,false); });
                hub.SetLock(flow,true); percent=AudioHub.Percent(hub.State(flow).Volume);
                Assert(hub.IsLocked(flow)&&hub.Locks.ContainsKey(id)&&hub.Locks[id]==percent,name+" lock holds the current level");
                float level=percent/100f; Separate.Write(flow,level>0.5f?level-0.15f:level+0.15f); started=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(restored[(int)flow]>0&&Math.Abs(Separate.Read(flow)-percent/100f)<=0.006f) { pass.Add(name+" lock restored a foreign change in "+(int)(DateTime.UtcNow-started).TotalMilliseconds+" ms"); return true; }
                Assert(DateTime.UtcNow-started<TimeSpan.FromSeconds(2),name+" lock restores a foreign change within 2 s (LockRestored "+restored[(int)flow]+", level "+Separate.Read(flow)+")"); return false;
            });
            steps.Add(delegate {
                float level=percent/100f,moved=level>0.5f?level-0.05f:level+0.05f; before=restored[(int)flow]; hub.SetVolume(flow,moved);
                Assert(hub.Locks[id]==AudioHub.Percent(hub.State(flow).Volume)&&hub.Locks[id]!=percent,name+" SetVolume moves the lock"); started=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(DateTime.UtcNow-started<TimeSpan.FromSeconds(1.3))return false; // spans one safety tick
                Assert(restored[(int)flow]==before&&Math.Abs(Separate.Read(flow)-hub.Locks[id]/100f)<=0.006f,name+" the user's own change is not undone");
                hub.SetLock(flow,false); Assert(!hub.IsLocked(flow)&&!hub.Locks.ContainsKey(id)&&locksChanged>=2,name+" unlock"); hub.SetVolume(flow,v0);
                Assert(Math.Abs(Separate.Read(flow)-v0)<0.002f,name+" volume restored after the lock"); pass.Add(name+" SetVolume moves the lock, unlock"); return true;
            });
        }
        if(lockFlowSet) {
            // A hold whose restore write fails: LockFailed once for the whole streak (the foreign notification and the ticks
            // retry), then the hold works again as soon as the write does.
            Flow flow=lockFlow; string name=flow==Flow.Render?"playback":"recording",fault="restore "+(flow==Flow.Render?"render":"capture"); int held=0,before=0; DateTime started=DateTime.MinValue;
            steps.Add(delegate {
                undo.Add(delegate { hub.Fault=null; hub.SetLock(flow,false); });
                hub.SetLock(flow,true); held=AudioHub.Percent(hub.State(flow).Volume); before=restored[(int)flow];
                hub.Fault=delegate(string step) { return step==fault; };
                float level=held/100f; Separate.Write(flow,level>0.5f?level-0.15f:level+0.15f); started=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(Since(started)<2.3)return false; // the foreign notification and two safety ticks
                Assert(lockFailed[(int)flow]==1&&restored[(int)flow]==before,name+" a failed restore raises LockFailed once (LockFailed "+lockFailed[(int)flow]+", LockRestored "+(restored[(int)flow]-before)+")");
                hub.Fault=null; started=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(restored[(int)flow]>before&&Math.Abs(Separate.Read(flow)-held/100f)<=0.006f) {
                    pass.Add(name+" a failed restore raises LockFailed once and the hold resumes in "+(int)(Since(started)*1000)+" ms");
                    hub.SetLock(flow,false); hub.SetVolume(flow,lockV0); return true;
                }
                Assert(Since(started)<2,name+" the hold resumes within 2 s once the write works again"); return false;
            });
        }
        if(capturePresent) { bool? listen=hub.Listening; if(listen.HasValue)pass.Add("Listen to this device reads "+listen.Value); else skip.Add("SKIP: audio engine Listen: Windows did not return the setting (Listening is null)."); }
        // Meters: the metering stream runs while on (Windows records the app as using the microphone), carries the microphone's
        // signal, is reopened when Windows invalidates it, and is released when off.
        float[] peaks=new float[2]; DateTime meterStart=DateTime.MinValue; bool[] recordSeen=new bool[1]; int opens=0; string reopen="";
        steps.Add(delegate { undo.Add(delegate { hub.Fault=null; hub.SetMetering(false); }); hub.SetMetering(true); Assert(!capturePresent||hub.MeterStreamOpen,"metering opens the capture stream"); meterStart=DateTime.UtcNow; return true; });
        steps.Add(delegate {
            for(int f=0;f<2;f++)peaks[f]=Math.Max(peaks[f],hub.Peak((Flow)f));
            if(Since(meterStart)<0.8||(captureLive&&peaks[0]<=0&&Since(meterStart)<2.5))return false;
            Assert(!captureLive||peaks[0]>0,"the capture peak rises above 0 with the stream on a live microphone");
            bool? use=capturePresent?Privacy.InUse(record):null; recordSeen[0]=use.HasValue; if(use.HasValue)Assert(use.Value,"Windows lists the app as using the microphone while metering");
            if(!capturePresent)return true;
            bool shot=false; opens=hub.StreamOpens; hub.Fault=delegate(string step) { if(step!="stream"||shot)return false; shot=true; return true; }; meterStart=DateTime.UtcNow; return true;
        });
        steps.Add(delegate {
            if(!capturePresent)return true;
            if(hub.StreamOpens>opens&&hub.MeterStreamOpen) { hub.Fault=null; reopen="; an invalidated stream reopens in "+(int)(Since(meterStart)*1000)+" ms"; return true; }
            Assert(Since(meterStart)<2.2,"an invalidated capture stream is reopened on the next safety tick"); return false;
        });
        steps.Add(delegate { hub.SetMetering(false); meterStart=DateTime.UtcNow; return true; });
        steps.Add(delegate {
            if(recordSeen[0]&&Privacy.InUse(record)!=false) { Assert(Since(meterStart)<2,"Windows ends the microphone use within 2 s of metering off"); return false; }
            pass.Add("meters: peak capture "+peaks[0].ToString("0.0000")+(captureLive?"":" (microphone muted or at 0, not asserted)")+", playback "+peaks[1].ToString("0.0000")+reopen+"; stream released"+(recordSeen[0]?" (the Windows microphone privacy record shows the start and the stop)":" (no Windows privacy record; engine flag only)")+"; "+windowRelease); return true;
        });
        // An endpoint that cannot be read or opened: the hub retries it on the 1 s tick only, so a window that refreshes on
        // every Changed (and reads State and Listening, as RefreshAudio does) gets about one refresh a second, not a loop.
        if(capturePresent||lockFlowSet) {
            AudioHub probe=null; int[] refreshes=new int[1]; int unreadable=0; DateTime t0=DateTime.MinValue;
            steps.Add(delegate {
                probe=new AudioHub(post); probes.Add(probe);
                probe.Changed+=delegate { refreshes[0]++; probe.State(Flow.Capture); probe.State(Flow.Render); bool? l=probe.Listening; };
                t0=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(Since(t0)<0.3)return false;
                probe.Fault=delegate(string step) { return step.StartsWith("read "); }; refreshes[0]=0;
                Assert(!probe.State(Flow.Capture).Present&&!probe.State(Flow.Render).Present,"a failed read shows no device"); t0=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(Since(t0)<3)return false; unreadable=refreshes[0];
                Assert(unreadable<=5,"an endpoint that opens but cannot be read is retried once a second ("+unreadable+" refreshes in 3 s)");
                probe.Fault=delegate(string step) { return step.StartsWith("read ")||step.StartsWith("open "); }; refreshes[0]=0; t0=DateTime.UtcNow; return true;
            });
            steps.Add(delegate {
                if(Since(t0)<3)return false; int unopenable=refreshes[0];
                Assert(unopenable<=1,"an endpoint that cannot be opened raises no refresh ("+unopenable+" refreshes in 3 s)");
                probe.Fault=null; pass.Add("a failing default endpoint: "+unreadable+" refreshes in 3 s when it cannot be read, "+unopenable+" when it cannot be opened"); return true;
            });
        }
    }
    // A second endpoint-volume instance, apart from the hub, writing with its own event context.
    static class Separate {
        static T With<T>(Flow flow,Func<IAudioEndpointVolume,T> use) {
            IMMDeviceEnumerator e=(IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice d=null; IAudioEndpointVolume v=null;
            try { Com.Check(e.GetDefaultAudioEndpoint(flow==Flow.Render?0:1,0,out d)); v=Com.Activate<IAudioEndpointVolume>(d); return use(v); } finally { Com.Release(v); Com.Release(d); Com.Release(e); }
        }
        internal static float Read(Flow flow) { return With(flow,delegate(IAudioEndpointVolume v) { float level; Com.Check(v.GetMasterVolumeLevelScalar(out level)); return level; }); }
        internal static void Write(Flow flow,float level) { With(flow,delegate(IAudioEndpointVolume v) { Guid g=Foreign; Com.Check(v.SetMasterVolumeLevelScalar(level,ref g)); return 0; }); }
        internal static string[] Defaults(Flow flow) {
            string[] ids=new string[3]; IMMDeviceEnumerator e=(IMMDeviceEnumerator)new MMDeviceEnumerator();
            try { for(int r=0;r<3;r++) { IMMDevice d; if(e.GetDefaultAudioEndpoint(flow==Flow.Render?0:1,r,out d)==0) { d.GetId(out ids[r]); Com.Release(d); } } } finally { Com.Release(e); }
            return ids;
        }
    }
    // Windows' microphone privacy record of this exe: LastUsedTimeStop is 0 or older than LastUsedTimeStart while a capture stream runs.
    static class Privacy {
        const string Root=@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged\";
        internal static string Key() { return Root+System.Reflection.Assembly.GetExecutingAssembly().Location.Replace('\\','#'); }
        internal static bool? InUse(string key) {
            using(Microsoft.Win32.RegistryKey k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) {
                if(k==null)return null; object start=k.GetValue("LastUsedTimeStart"),stop=k.GetValue("LastUsedTimeStop"); if(!(start is long)||!(stop is long))return null;
                return (long)stop==0||(long)stop<(long)start;
            }
        }
    }
}
}
