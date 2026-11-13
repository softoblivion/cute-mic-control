using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SelfUpdate;
using Forms = System.Windows.Forms;
namespace MicControl {
// What the window shows and changes: the audio engine, or a fixed sample for pictures (SampleAudio).
interface IAudioSource {
    EndpointState State(Flow flow); List<AudioDevice> Devices(Flow flow); void SetDefault(Flow flow,string id);
    void SetVolume(Flow flow,float scalar); bool ToggleMute(Flow flow); bool IsLocked(Flow flow); void SetLock(Flow flow,bool on);
    bool? Listening { get; } void SetListening(bool on); float Peak(Flow flow);
}
sealed class HubSource : IAudioSource {
    readonly AudioHub hub; internal HubSource(AudioHub hub) { this.hub=hub; }
    public EndpointState State(Flow flow) { return hub.State(flow); }
    public List<AudioDevice> Devices(Flow flow) { return hub.Devices(flow); }
    public void SetDefault(Flow flow,string id) { hub.SetDefault(flow,id); }
    public void SetVolume(Flow flow,float scalar) { hub.SetVolume(flow,scalar); }
    public bool ToggleMute(Flow flow) { return hub.ToggleMute(flow); }
    public bool IsLocked(Flow flow) { return hub.IsLocked(flow); }
    public void SetLock(Flow flow,bool on) { hub.SetLock(flow,on); }
    public bool? Listening { get { return hub.Listening; } }
    public void SetListening(bool on) { hub.SetListening(on); }
    public float Peak(Flow flow) { return hub.Peak(flow); }
}
// A fixed audio state with invented devices, so previews and screenshots carry no device name of the machine they are made on.
sealed class SampleAudio : IAudioSource {
    readonly List<AudioDevice>[] lists={new List<AudioDevice>(),new List<AudioDevice>()};
    readonly float[] volume={.82f,.64f},peak={.2f,.09f}; readonly bool[] muted=new bool[2],locked=new bool[2];
    internal bool? Listen=false; internal bool Fail; // Fail: the device and Listen writes throw, as a refused Windows call does
    internal SampleAudio() {
        Add(Flow.Capture,"Microphone (Studio USB)",true); Add(Flow.Capture,"Headset Microphone (Wireless)",false); Add(Flow.Capture,"Microphone Array (Laptop)",false);
        Add(Flow.Render,"Speakers (Desktop Audio)",true); Add(Flow.Render,"Headphones (USB Audio)",false);
    }
    void Add(Flow flow,string name,bool isDefault) { List<AudioDevice> l=lists[(int)flow]; l.Add(new AudioDevice { Id="{sample}."+(flow==Flow.Capture?"capture.":"render.")+l.Count,Name=name,IsDefault=isDefault }); }
    AudioDevice Default(Flow flow) { foreach(AudioDevice d in lists[(int)flow])if(d.IsDefault)return d; return null; }
    public EndpointState State(Flow flow) { EndpointState s=new EndpointState(); AudioDevice d=Default(flow); if(d==null)return s; s.Present=true; s.Id=d.Id; s.Name=d.Name; s.Volume=volume[(int)flow]; s.Muted=muted[(int)flow]; return s; }
    public List<AudioDevice> Devices(Flow flow) { return lists[(int)flow].Select(d=>new AudioDevice { Id=d.Id,Name=d.Name,IsDefault=d.IsDefault }).ToList(); }
    public void SetDefault(Flow flow,string id) { if(Fail)throw new InvalidOperationException("Sample failure."); if(!lists[(int)flow].Any(d=>d.Id==id))throw new ArgumentException("Unknown sample device.","id"); foreach(AudioDevice d in lists[(int)flow])d.IsDefault=d.Id==id; }
    public void SetVolume(Flow flow,float scalar) { volume[(int)flow]=Math.Max(0f,Math.Min(1f,scalar)); }
    public bool ToggleMute(Flow flow) { muted[(int)flow]=!muted[(int)flow]; return muted[(int)flow]; }
    internal void SetMuted(Flow flow,bool on) { muted[(int)flow]=on; }
    public bool IsLocked(Flow flow) { return locked[(int)flow]; }
    public void SetLock(Flow flow,bool on) { locked[(int)flow]=on; }
    public bool? Listening { get { return Listen; } }
    public void SetListening(bool on) { if(Fail)throw new InvalidOperationException("Sample failure."); Listen=on; }
    public float Peak(Flow flow) { return peak[(int)flow]; }
}
// One card of the main view: a flow's state, mute button, default device, level with its meter, and the hold button.
sealed class FlowCard {
    internal Flow Flow; internal Ellipse Dot; internal DropShadowEffect DotGlow; internal TextBlock State,Value; internal MuteButton Mute;
    internal DeviceButton Device; internal LevelSlider Level; internal LockButton Lock; internal double Shown; internal EndpointState Last;
}
// The app window: the standard title bar, the audio console (a Microphone card and a Speakers card, each with its mute
// button, default device, level and hold; the microphone card also carries the hotkey and Listen), the footer with the
// notice line and Quit, and three layers over it: the device list, Settings (the gear) and the hotkey editor.
// Fixed size; the Scale setting multiplies the logical 420 x 456 layout and the window size together.
class SettingsWindow : Window {
    internal const double ContentWidth=420,ContentHeight=456,Edge=12; // logical layout; Edge is the shadow margin around it
    internal Preferences Prefs; internal KeyboardHook Hook;
    internal GlowOverlay overlay; Forms.NotifyIcon tray; System.Drawing.Icon trayIcon; DispatcherTimer poll,appearanceSave,meterTimer,noticeTimer;
    internal Grid root,content; Grid shell,editor,sheet; Border panel,sheetPanel,noticeBox; TextBlock bindText,feedback,captureText,searchHint,listenHint;
    ToggleButton startToggle; ListBox keyList; TextBox search; Button record; List<Button> modButtons=new List<Button>();
    internal ToggleButton OverlayToggle,ListenToggle; internal Grid SizeRow,ListenRow;
    internal Slider sizeSlider; internal List<Button> paletteButtons=new List<Button>(); TextBlock sizeText; bool appearanceDirty,locksDirty,lockSaveFailed; int renderedPalette=-1; DispatcherTimer lockSave;
    internal ThemeLook Look=new ThemeLook(); internal List<Button> themeButtons=new List<Button>(); Forms.ContextMenuStrip menu; Forms.ToolStripMenuItem micMenu,speakerMenu;
    internal TitleBar TitleBar; internal Segmented scaleControl; internal bool SheetOpen;
    internal AudioHub Hub; HubSource hubSource; internal IAudioSource Source; internal FlowCard MicCard,SpeakerCard; readonly List<FlowCard> cards=new List<FlowCard>();
    internal DeviceList DeviceList; List<AudioDevice> listed=new List<AudioDevice>(); DateTime meterAt;
    uint draftKey,draftMods; bool startup,quitting,ready,editing; bool? muted; bool initialized; readonly bool hidden;
    UpdateHost updates; Forms.ToolStripMenuItem updateItem; TextBlock updateText; Button updateButton; ToggleButton autoUpdateToggle; Action balloonAction;
    internal SettingsWindow(bool startHidden) {
        hidden=startHidden; Prefs=Preferences.Load();
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Title=AppInfo.ProductName+" · Settings · "+AppInfo.DisplayVersion;
        Resources=Theme.Resources(Look); FontFamily=TypeRoles.TextFamily; Foreground=Theme.Text; Look.Apply(Themes.Get(Prefs.Theme),false);
        appearanceSave=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(350) }; appearanceSave.Tick+=delegate { CommitAppearance(); };
        noticeTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(10) }; noticeTimer.Tick+=delegate { CloseNotice(); };
        meterTimer=new DispatcherTimer(DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(33) }; meterTimer.Tick+=delegate { Meter(); };
        // One audio engine for the window; its events arrive on this thread. Locks come from the settings before the first refresh.
        Hub=new AudioHub(delegate(Action a) { Dispatcher.BeginInvoke(a); }); hubSource=new HubSource(Hub); Source=hubSource; Hub.Locks=Prefs.Locks;
        Hub.Changed+=delegate { if(!quitting)RefreshAudio(true); };
        // A held level moves with every step of a slider drag: the settings keep it at once, the file is written 350 ms after
        // the last change, at the end of a drag, on hiding the window and on Exit.
        lockSave=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(350) }; lockSave.Tick+=delegate { CommitLocks(); };
        Hub.LocksChanged+=delegate { if(quitting)return; Prefs.Locks=Hub.Locks; locksDirty=true; lockSave.Stop(); lockSave.Start(); RefreshAudio(false); };
        Hub.LockRestored+=delegate(Flow flow) { if(quitting||Source!=hubSource)return; FlowCard c=flow==Flow.Capture?MicCard:SpeakerCard; c.Lock.Shake(); c.Level.Pulse(); };
        Hub.LockFailed+=delegate(Flow flow) { if(!quitting&&Source==hubSource)Error("Could not hold the level."); };
        Build(); overlay=new GlowOverlay(); ApplyAppearance(false);
        if(!Program.Test) {
            Hook=new KeyboardHook(); Hook.Engine.Key=Prefs.Key; Hook.Engine.Modifiers=Prefs.Modifiers;
            Hook.Engine.Fired=delegate { Dispatcher.BeginInvoke((Action)Toggle); };
            Hook.Engine.Captured=delegate(uint mods,uint key) { Dispatcher.BeginInvoke((Action)delegate { Captured(mods,key); }); };
        }
        menu=new Forms.ContextMenuStrip(); menu.ForeColor=System.Drawing.Color.FromArgb(245,243,236); menu.ShowImageMargin=false; ApplyTheme(false);
        menu.Items.Add("Toggle microphone",null,delegate { Toggle(); });
        micMenu=new Forms.ToolStripMenuItem("Microphone"); speakerMenu=new Forms.ToolStripMenuItem("Speakers"); menu.Items.Add(micMenu); menu.Items.Add(speakerMenu);
        menu.Opening+=delegate { FillDevices(micMenu,Flow.Capture); FillDevices(speakerMenu,Flow.Render); };
        menu.Items.Add("Settings",null,delegate { OpenSettings(); }); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(AppInfo.ProductName+" "+AppInfo.DisplayVersion) { Enabled=false });
        updateItem=new Forms.ToolStripMenuItem("Check for updates",null,delegate { if(updates!=null)updates.PillClicked(); }) { Enabled=!Program.Test }; menu.Items.Add(updateItem);
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit",null,delegate { Exit(); });
        tray=new Forms.NotifyIcon { Text=AppInfo.ProductName,ContextMenuStrip=menu,Visible=!Program.Test }; tray.DoubleClick+=delegate { OpenSettings(); };
        tray.BalloonTipClicked+=delegate { Action click=balloonAction; balloonAction=null; if(click!=null)click(); };
        if(!Program.Test) {
            updates=new UpdateHost(this,Look,Resources,delegate { return Prefs.AutoDownloadUpdates; },CommitAppearance,Exit);
            updates.ShowToast=ShowToast; updates.Bind(ShowUpdate);
        }
        ApplyScale(false); // before the first show: the window opens at its stored scale
        poll=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) }; poll.Tick+=delegate { RefreshAudio(false); }; poll.Start();
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(!quitting) { e.Cancel=true; HideSettings(); } };
        Deactivated+=delegate { if(Hook!=null)Hook.Engine.CancelCapture(); if(editing)SetRecording(false); };
        // The level meters need the capture stream (Windows shows its microphone-in-use icon), so it runs only while the window is visible.
        IsVisibleChanged+=delegate { if(!IsVisible && Hook!=null)Hook.Engine.CancelCapture(); Metering(IsVisible); if(IsVisible)RefreshAudio(true); };
        PreviewKeyDown+=delegate(object s,KeyEventArgs e) {
            if(e.Key!=Key.Escape)return;
            if(editing) { CloseEditor(); e.Handled=true; } else if(DeviceList.IsOpen) { DeviceList.Close(); e.Handled=true; } else if(SheetOpen) { CloseSheet(); e.Handled=true; }
        };
        Loaded+=delegate { if(!initialized) { initialized=true; shell.Opacity=0; TranslateTransform move=(TranslateTransform)shell.RenderTransform; move.Y=14; Theme.Fade(shell,1,.55); move.BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.65)); } };
        RefreshAudio(true); ready=true;
    }
    internal void Start() {
        if(!hidden)Show(); if(Prefs.Overlay)overlay.Show();
        if(updates!=null)updates.Start();
    }
    void Build() {
        root=new Grid { Margin=new Thickness(Edge),Width=ContentWidth,Height=ContentHeight }; Content=root;
        shell=new Grid { RenderTransform=new TranslateTransform(),Background=Brushes.Transparent }; root.Children.Add(shell);
        Border frame=new Border { CornerRadius=new CornerRadius(24),BorderThickness=new Thickness(1),BorderBrush=Look.FrameEdge,Background=Look.Frame,Effect=Look.FrameShadow }; shell.Children.Add(frame);
        content=new Grid { Margin=new Thickness(1) }; shell.Children.Add(content);
        content.SizeChanged+=delegate { content.Clip=new RectangleGeometry(new Rect(content.RenderSize),23,23); };
        Canvas atmosphere=new Canvas { IsHitTestVisible=false }; content.Children.Add(atmosphere);
        // The "deep field" of the shared look: a large soft field of the hue drifting across the top, a smaller dimmer one of the accent in the bottom-right corner.
        Ellipse nebula=new Ellipse { Width=360,Height=280,Opacity=.42,Fill=Look.Nebula,RenderTransform=new TranslateTransform() };
        Canvas.SetLeft(nebula,40); Canvas.SetTop(nebula,-80); atmosphere.Children.Add(nebula);
        DoubleAnimation drift=new DoubleAnimation(-12,20,TimeSpan.FromSeconds(9)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }; ((TranslateTransform)nebula.RenderTransform).BeginAnimation(TranslateTransform.XProperty,drift);
        Ellipse field=new Ellipse { Width=230,Height=200,Opacity=.5,Fill=Look.Field,RenderTransform=new TranslateTransform() };
        Canvas.SetLeft(field,230); Canvas.SetTop(field,280); atmosphere.Children.Add(field);
        DoubleAnimation fieldDrift=new DoubleAnimation(10,-14,TimeSpan.FromSeconds(13)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }; ((TranslateTransform)field.RenderTransform).BeginAnimation(TranslateTransform.YProperty,fieldDrift);
        TitleBar=new TitleBar(Look,Resources,AppInfo.ProductName,AppInfo.DisplayVersion) { VerticalAlignment=VerticalAlignment.Top }; content.Children.Add(TitleBar);
        TitleBar.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.ChangedButton==MouseButton.Left)DragMove(); };
        TitleBar.Pill.Click+=delegate { if(updates!=null)updates.PillClicked(); }; TitleBar.Pill.IsEnabled=!Program.Test;
        TitleBar.Gear.Click+=delegate { if(SheetOpen)CloseSheet(); else OpenSheet(); };
        TitleBar.Minimize.Click+=delegate { HideSettings(); }; TitleBar.Close.Click+=delegate { HideSettings(); };
        StackPanel stack=new StackPanel { Margin=new Thickness(14,54,14,0),VerticalAlignment=VerticalAlignment.Top }; content.Children.Add(stack);
        MicCard=BuildCard(stack,Flow.Capture); SpeakerCard=BuildCard(stack,Flow.Render);
        Grid footer=new Grid { VerticalAlignment=VerticalAlignment.Bottom,Height=28,Margin=new Thickness(16,0,14,10) }; content.Children.Add(footer);
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        // The notice line: one line with an ellipsis and the full text as tooltip; a click closes it, an error closes itself after 10 s.
        feedback=Theme.Label("",TypeRoles.Caption,Theme.Rose); feedback.TextTrimming=TextTrimming.CharacterEllipsis;
        noticeBox=new Border { Background=Brushes.Transparent,Child=feedback,Margin=new Thickness(0,0,12,0),Cursor=Cursors.Hand,Visibility=Visibility.Collapsed }; noticeBox.MouseLeftButtonUp+=delegate { CloseNotice(); }; footer.Children.Add(noticeBox);
        Button exit=new Button { Content="Quit",Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(12,0,12,0) }; TypeRoles.LabelS.Apply(exit); exit.Click+=delegate { Exit(); }; Grid.SetColumn(exit,1); footer.Children.Add(exit);
        DeviceList=new DeviceList(Look); content.Children.Add(DeviceList);
        DeviceList.Chosen+=delegate(int i) { FlowCard c=DeviceList.Owner as FlowCard; if(c!=null&&i<listed.Count)ChooseDevice(c.Flow,listed[i].Id); };
        DeviceList.Closed+=delegate { FlowCard c=DeviceList.Owner as FlowCard; if(c!=null)c.Device.SetOpen(false); };
        DeviceList.SoundSettings+=delegate { if(Program.Test)return; try { Process.Start("ms-settings:sound"); } catch { Error("Could not open Windows sound settings."); } };
        BuildSheet(content);
        BuildEditor();
    }
    // A card: radius 20, a faint glass fill with a hairline, an inner top light and a soft shadow outside it.
    FlowCard BuildCard(Panel parent,Flow flow) {
        bool mic=flow==Flow.Capture; FlowCard c=new FlowCard { Flow=flow };
        Grid host=new Grid { Margin=new Thickness(0,mic?0:10,0,0) }; parent.Children.Add(host);
        host.Children.Add(Glyph.OuterShadow(20,false,new DropShadowEffect { Color=Colors.Black,BlurRadius=22,ShadowDepth=8,Direction=270,Opacity=.22 }));
        LinearGradientBrush fill=new LinearGradientBrush(Glyph.Hex("#10FFFFFF"),Glyph.Hex("#09FFFFFF"),90); fill.Freeze();
        host.Children.Add(new Border { CornerRadius=new CornerRadius(20),Background=fill,BorderBrush=Theme.Brush("#1AFFFFFF"),BorderThickness=new Thickness(1) });
        LinearGradientBrush top=new LinearGradientBrush { MappingMode=BrushMappingMode.Absolute,StartPoint=new Point(0,0),EndPoint=new Point(0,3) };
        top.GradientStops.Add(new GradientStop(Glyph.Hex("#12FFFFFF"),0)); top.GradientStops.Add(new GradientStop(Glyph.Hex("#00FFFFFF"),1)); top.Freeze();
        host.Children.Add(new Border { CornerRadius=new CornerRadius(19),Margin=new Thickness(1),BorderBrush=top,BorderThickness=new Thickness(1),IsHitTestVisible=false });
        StackPanel body=new StackPanel { Margin=new Thickness(15,14,15,mic?7:15) }; host.Children.Add(body);
        Grid head=new Grid { Height=20,Margin=new Thickness(2,0,2,0) }; body.Children.Add(head);
        head.Children.Add(Theme.Label(mic?"Microphone":"Speakers",TypeRoles.Section,Theme.Muted));
        StackPanel state=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right }; head.Children.Add(state);
        c.DotGlow=new DropShadowEffect { ShadowDepth=0,BlurRadius=8,Opacity=0 }; c.Dot=new Ellipse { Width=6,Height=6,VerticalAlignment=VerticalAlignment.Center,Effect=c.DotGlow }; state.Children.Add(c.Dot);
        c.State=Theme.Label("",TypeRoles.Caption,Theme.Muted); c.State.Margin=new Thickness(7,0,0,0); state.Children.Add(c.State);
        Grid strip=new Grid { Height=64,Margin=new Thickness(0,8,0,0) }; body.Children.Add(strip);
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(56) }); strip.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(14) }); strip.ColumnDefinitions.Add(new ColumnDefinition());
        c.Mute=new MuteButton(mic,Look) { VerticalAlignment=VerticalAlignment.Center }; c.Mute.Click+=delegate { ToggleFlow(flow); }; strip.Children.Add(c.Mute);
        StackPanel column=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(column,2); strip.Children.Add(column);
        c.Device=new DeviceButton(); AutomationProperties.SetName(c.Device,mic?"Microphone device":"Speakers device"); c.Device.Click+=delegate { ToggleList(c); }; column.Children.Add(c.Device);
        Grid level=new Grid { Height=28,Margin=new Thickness(0,6,0,0) }; column.Children.Add(level);
        foreach(double w in new double[]{-1,10,28,10,28})level.ColumnDefinitions.Add(new ColumnDefinition { Width=w<0?new GridLength(1,GridUnitType.Star):new GridLength(w) });
        c.Level=new LevelSlider(Look); AutomationProperties.SetName(c.Level,mic?"Microphone level":"Speakers level"); level.Children.Add(c.Level);
        c.Value=Theme.Label("0",TypeRoles.Mono,Theme.Ink2); c.Value.TextAlignment=TextAlignment.Right; c.Value.HorizontalAlignment=HorizontalAlignment.Right; Grid.SetColumn(c.Value,2); level.Children.Add(c.Value);
        c.Level.ValueChanged+=delegate { c.Value.Text=((int)c.Level.Value).ToString(); };
        c.Level.UserChanged+=delegate(int v) { try { Source.SetVolume(flow,v/100f); } catch { Error(mic?"Could not change the microphone level.":"Could not change the speakers level."); } if(!c.Level.Dragging)RefreshAudio(false); };
        c.Lock=new LockButton(Look); AutomationProperties.SetName(c.Lock,mic?"Hold microphone level":"Hold speakers level"); Grid.SetColumn(c.Lock,4); level.Children.Add(c.Lock);
        c.Level.DragEnded+=delegate { CommitLocks(); RefreshAudio(false); };
        c.Lock.Click+=delegate { lockSaveFailed=false; try { Source.SetLock(flow,!Source.IsLocked(flow)); } catch { Error("Could not hold the level."); } RefreshAudio(false); };
        if(mic) {
            body.Children.Add(new Border { Height=1,Background=Theme.Brush("#14FFFFFF"),Margin=new Thickness(2,11,2,3) });
            Grid keyRow=new Grid { MinHeight=46,Margin=new Thickness(2,0,2,0) }; keyRow.ColumnDefinitions.Add(new ColumnDefinition()); keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            keyRow.Children.Add(Theme.Label("Hotkey",TypeRoles.Label,Theme.Text));
            Button binding=new Button { MinWidth=120,MaxWidth=210,Height=32,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(12,0,12,0) };
            bindText=Theme.Label(Preferences.KeyName(Prefs.Modifiers,Prefs.Key),TypeRoles.Label,Look.AccentInk); bindText.TextTrimming=TextTrimming.CharacterEllipsis; binding.Content=bindText; binding.ToolTip=bindText.Text; binding.Click+=delegate { OpenEditor(); }; Grid.SetColumn(binding,1); keyRow.Children.Add(binding); body.Children.Add(keyRow);
            ListenToggle=SwitchRow(body,"Listen","Play this microphone through the speakers",false,out listenHint,out ListenRow); ListenRow.Margin=new Thickness(2,0,2,0);
            ListenToggle.Click+=delegate { if(!ready)return; bool want=ListenToggle.IsChecked==true; try { Source.SetListening(want); } catch { Error("Could not change Listen."); } ShowListen(Source.Listening); };
        }
        cards.Add(c); return c;
    }
    ToggleButton SwitchRow(Panel parent,string title,string hint,bool on) { TextBlock h; Grid row; return SwitchRow(parent,title,hint,on,out h,out row); }
    ToggleButton SwitchRow(Panel parent,string title,string hint,bool on,out TextBlock hintText,out Grid row) {
        row=new Grid { MinHeight=46 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        StackPanel text=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0) }; text.Children.Add(Theme.Label(title,TypeRoles.Label,Theme.Text)); hintText=null;
        if(hint!=null) { hintText=Theme.Label(hint,TypeRoles.Caption,Theme.Muted); hintText.TextWrapping=TextWrapping.Wrap; hintText.Margin=new Thickness(0,2,0,0); text.Children.Add(hintText); }
        row.Children.Add(text);
        ToggleButton toggle=new ToggleButton { IsChecked=on,Background=Look.AccentGrad,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center }; AutomationProperties.SetName(toggle,title); Grid.SetColumn(toggle,1); row.Children.Add(toggle); parent.Children.Add(row); return toggle;
    }
    static TextBlock Section(string title) { TextBlock t=Theme.Label(title,TypeRoles.Section,Theme.Muted); t.Margin=new Thickness(0,16,0,6); return t; }
    static StackPanel Heading(string title,string hint) {
        StackPanel heading=new StackPanel { Margin=new Thickness(0,6,0,0) }; heading.Children.Add(Theme.Label(title,TypeRoles.Label,Theme.Text));
        if(hint!=null) { TextBlock h=Theme.Label(hint,TypeRoles.Caption,Theme.Muted); h.Margin=new Thickness(0,2,0,0); heading.Children.Add(h); }
        return heading;
    }
    // The Settings sheet: opened by the gear, closed by the gear, its close button or Esc.
    void BuildSheet(Grid content) {
        sheet=new Grid { Visibility=Visibility.Collapsed,Background=Look.Scrim,Margin=new Thickness(0,52,0,0) }; content.Children.Add(sheet);
        sheet.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.OriginalSource==sheet)CloseSheet(); };
        sheetPanel=new Border { CornerRadius=new CornerRadius(20),Margin=new Thickness(10,2,10,10),Background=Look.Sheet,BorderThickness=new Thickness(1),BorderBrush=Theme.Hairline,RenderTransform=new TranslateTransform(),Padding=new Thickness(18,10,6,14) }; sheet.Children.Add(sheetPanel);
        Grid layout=new Grid(); sheetPanel.Child=layout; layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        Grid title=new Grid { Margin=new Thickness(0,0,8,2) }; title.Children.Add(Theme.Label("Settings",TypeRoles.Display,Theme.Text));
        Button close=new Button { Style=(Style)Resources["TitleButton"],Content=Icons.Make(Icons.Close,17),HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close" }; AutomationProperties.SetName(close,"Close settings");
        close.Click+=delegate { CloseSheet(); }; title.Children.Add(close); layout.Children.Add(title);
        ScrollViewer scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,12,0) }; Grid.SetRow(scroll,1); layout.Children.Add(scroll);
        StackPanel stack=new StackPanel(); scroll.Content=stack;
        TextBlock first=Section("Appearance"); first.Margin=new Thickness(0,6,0,6); stack.Children.Add(first);
        BuildAppearance(stack);
        stack.Children.Add(Section("General"));
        startup=IsStartup(); startToggle=SwitchRow(stack,"Launch at login",null,startup); startToggle.Click+=delegate { if(!ready)return; try { using(RegistryKey k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(startToggle.IsChecked==true) { k.SetValue("MicControlGlow","\""+Assembly.GetExecutingAssembly().Location+"\" --startup"); k.DeleteValue("MicMuteGlow",false); } else { k.DeleteValue("MicControlGlow",false); k.DeleteValue("MicMuteGlow",false); } startup=startToggle.IsChecked==true; } } catch { startToggle.IsChecked=startup; Error("Could not update launch at login. Check your Windows permissions."); } };
        stack.Children.Add(Section("Updates"));
        autoUpdateToggle=SwitchRow(stack,"Download updates automatically","Checked every 5 minutes; installing still needs a restart",Prefs.AutoDownloadUpdates); autoUpdateToggle.Click+=delegate { if(!ready)return; bool previous=Prefs.AutoDownloadUpdates; Prefs.AutoDownloadUpdates=autoUpdateToggle.IsChecked==true; try { Prefs.Save(); } catch { Prefs.AutoDownloadUpdates=previous; autoUpdateToggle.IsChecked=previous; Error("Could not save settings. Check folder permissions."); return; } if(updates!=null)updates.Presenter.AutoDownloadChanged(Prefs.AutoDownloadUpdates); };
        Grid updateRow=new Grid { MinHeight=40,Margin=new Thickness(0,2,0,0) }; updateRow.ColumnDefinitions.Add(new ColumnDefinition()); updateRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        updateText=Theme.Label(Program.Test?"Updates are not checked in the self-test":"Updates are not checked yet",TypeRoles.Caption,Theme.Muted); updateText.TextWrapping=TextWrapping.Wrap; updateText.Margin=new Thickness(0,0,12,0); updateRow.Children.Add(updateText);
        updateButton=new Button { Content="Check for updates",Height=28,Padding=new Thickness(12,0,12,0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsEnabled=!Program.Test }; TypeRoles.LabelS.Apply(updateButton);
        updateButton.Click+=delegate { if(updates!=null&&updateButton.Tag is UpdateCommand)updates.Presenter.Run((UpdateCommand)updateButton.Tag); }; Grid.SetColumn(updateButton,1); updateRow.Children.Add(updateButton); stack.Children.Add(updateRow);
    }
    internal void OpenSheet() {
        if(SheetOpen)return; DeviceList.Close(); SheetOpen=true; TitleBar.SetSettingsOpen(true);
        sheet.Visibility=Visibility.Visible; sheet.Opacity=0; ((TranslateTransform)sheetPanel.RenderTransform).Y=12; Theme.Fade(sheet,1,.2); ((TranslateTransform)sheetPanel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.3));
    }
    internal void CloseSheet() { if(!SheetOpen)return; SheetOpen=false; TitleBar.SetSettingsOpen(false); CommitAppearance(); sheet.Visibility=Visibility.Collapsed; }
    void BuildAppearance(Panel parent) {
        OverlayToggle=SwitchRow(parent,"Desktop indicator",null,Prefs.Overlay); OverlayToggle.Margin=new Thickness(0,0,0,0);
        OverlayToggle.Click+=delegate { if(!ready)return; bool previous=Prefs.Overlay; Prefs.Overlay=OverlayToggle.IsChecked==true; try { Prefs.Save(); if(Prefs.Overlay)overlay.Show(); else overlay.Hide(); } catch { Prefs.Overlay=previous; OverlayToggle.IsChecked=previous; Error("Could not save settings. Check folder permissions."); } SyncIndicatorRows(); };
        SizeRow=new Grid { Height=24 }; parent.Children.Add(SizeRow);
        SizeRow.Children.Add(Theme.Label("Indicator size",TypeRoles.Label,Theme.Text)); sizeText=Theme.Label(Prefs.IndicatorScale+"%",TypeRoles.Caption,Theme.Ink2); sizeText.HorizontalAlignment=HorizontalAlignment.Right; SizeRow.Children.Add(sizeText);
        sizeSlider=new Slider { Minimum=50,Maximum=200,Value=Prefs.IndicatorScale,TickFrequency=5,IsSnapToTickEnabled=true,SmallChange=5,LargeChange=25,IsMoveToPointEnabled=true,Height=24,Margin=new Thickness(0,0,0,4),ToolTip="Resize the desktop indicator",Foreground=Look.Accent,BorderBrush=Look.AccentInk,Tag=Look.ThumbGlow };
        AutomationProperties.SetName(sizeSlider,"Indicator size"); parent.Children.Add(sizeSlider);
        sizeSlider.ValueChanged+=delegate { if(!ready)return; Prefs.IndicatorScale=(int)Math.Round(sizeSlider.Value); ApplyAppearance(true); };
        SyncIndicatorRows();
        BuildThemeRow(parent);
        Grid paletteHeading=new Grid { Margin=new Thickness(0,6,0,0) }; parent.Children.Add(paletteHeading); paletteHeading.Children.Add(Theme.Label("Color palette",TypeRoles.Label,Theme.Text));
        TextBlock legend=Theme.Label("On / Off",TypeRoles.Section,Theme.Muted); legend.HorizontalAlignment=HorizontalAlignment.Right; paletteHeading.Children.Add(legend);
        UniformGrid cardsRow=new UniformGrid { Columns=5,Rows=1,Margin=new Thickness(-2,8,-2,4),Height=54 }; parent.Children.Add(cardsRow);
        for(int i=0;i<Palette.All.Length;i++) {
            int selected=i; Palette palette=Palette.All[i];
            Button card=new Button { Padding=new Thickness(3),Margin=new Thickness(2,0,2,0),HorizontalContentAlignment=HorizontalAlignment.Center,ToolTip=palette.Description,Tag=i };
            AutomationProperties.SetName(card,palette.Name+" palette: "+palette.Description);
            StackPanel contents=new StackPanel(); StackPanel swatches=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,5) };
            foreach(ColorRamp ramp in new ColorRamp[]{palette.On,palette.Off})swatches.Children.Add(new Ellipse { Width=12,Height=12,Margin=new Thickness(3,0,3,0),Fill=ramp.Brush(),Effect=new DropShadowEffect { ShadowDepth=0,BlurRadius=8,Color=ramp.Glow,Opacity=.45 } });
            contents.Children.Add(swatches); TextBlock name=Theme.Label(palette.Name,TypeRoles.Caption,Theme.Muted); name.HorizontalAlignment=HorizontalAlignment.Center; contents.Children.Add(name); card.Content=contents;
            card.Click+=delegate { Prefs.PaletteIndex=selected; ApplyAppearance(true); }; cardsRow.Children.Add(card); paletteButtons.Add(card);
        }
        parent.Children.Add(Heading("Scale","Size of the window and its text"));
        string[] labels=new string[UiScale.Steps.Length]; for(int i=0;i<labels.Length;i++)labels[i]=UiScale.Steps[i]+" %";
        scaleControl=new Segmented(Resources,"Scale",labels,TypeRoles.LabelS,26) { Margin=new Thickness(0,8,0,4) }; parent.Children.Add(scaleControl);
        scaleControl.Changed+=delegate(int index) { SelectScale(UiScale.Steps[index]); };
    }
    // The size row only means something while the desktop indicator is on.
    void SyncIndicatorRows() { bool on=Prefs.Overlay; foreach(FrameworkElement e in new FrameworkElement[]{SizeRow,sizeSlider}) { e.IsEnabled=on; e.Opacity=on?1:.38; } }
    // The Theme row of the shared look: four chips (a round swatch of the hue with the accent dot, then the label); arrows move the choice, a choice applies at once and is saved.
    void BuildThemeRow(Panel parent) {
        parent.Children.Add(Heading("Theme","Background colour and accent of the window"));
        UniformGrid row=new UniformGrid { Columns=Themes.All.Length,Rows=1,Height=32,Margin=new Thickness(-2,8,-2,8) }; AutomationProperties.SetName(row,"Theme"); parent.Children.Add(row);
        for(int i=0;i<Themes.All.Length;i++) {
            ThemeSpec theme=Themes.All[i]; int index=i;
            Button chip=new Button { Height=32,Padding=new Thickness(3,0,3,0),Margin=new Thickness(2,0,2,0),HorizontalContentAlignment=HorizontalAlignment.Center,Background=Theme.Brush("#0DFFFFFF"),BorderBrush=Brushes.Transparent,Tag=i };
            AutomationProperties.SetName(chip,theme.Label+" theme");
            StackPanel contents=new StackPanel { Orientation=Orientation.Horizontal }; contents.Children.Add(Swatch(theme));
            TextBlock name=Theme.Label(theme.Label,TypeRoles.LabelS,Theme.Ink2); name.Margin=new Thickness(4,0,0,0); name.TextTrimming=TextTrimming.CharacterEllipsis; contents.Children.Add(name); chip.Content=contents;
            chip.Click+=delegate { SelectTheme(index); };
            chip.KeyDown+=delegate(object s,KeyEventArgs e) { int step=e.Key==Key.Right||e.Key==Key.Down?1:e.Key==Key.Left||e.Key==Key.Up?-1:0; if(step==0)return; int next=(index+step+Themes.All.Length)%Themes.All.Length; themeButtons[next].Focus(); SelectTheme(next); e.Handled=true; };
            row.Children.Add(chip); themeButtons.Add(chip);
        }
    }
    static Grid Swatch(ThemeSpec theme) {
        Grid swatch=new Grid { Width=18,Height=18,Clip=new EllipseGeometry(new Point(9,9),9,9) };
        swatch.Children.Add(new Ellipse { Fill=new LinearGradientBrush(ThemeSpec.Mix(theme.Hue,Colors.Black,.3),theme.Hue,45),Stroke=Theme.Brush("#40FFFFFF"),StrokeThickness=1 });
        swatch.Children.Add(new Ellipse { Width=10,Height=10,Fill=new SolidColorBrush(theme.Accent),Stroke=Theme.Brush("#59000000"),StrokeThickness=1.5,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,-2,-2) });
        return swatch;
    }
    void SelectTheme(int index) {
        string previous=Prefs.Theme,next=Themes.All[index].Name; if(previous==next)return; Prefs.Theme=next; ApplyTheme(true);
        if(Program.Test)return; try { Prefs.Save(); } catch { Prefs.Theme=previous; ApplyTheme(true); Error("Could not save settings. Check folder permissions."); }
    }
    internal void ApplyTheme(bool animate) {
        ThemeSpec theme=Themes.Get(Prefs.Theme); Look.Apply(theme,animate);
        foreach(Button chip in themeButtons) {
            bool on=(int)chip.Tag==Themes.IndexOf(theme.Name);
            chip.Background=Theme.Brush(on?"#21FFFFFF":"#0DFFFFFF"); chip.BorderBrush=on?(Brush)Look.AccentInk:Brushes.Transparent;
            ((TextBlock)((StackPanel)chip.Content).Children[1]).Foreground=on?Theme.Text:Theme.Ink2;
        }
        menu.BackColor=ThemeSpec.Drawing(theme.BgMid);
    }
    // A Scale choice applies at once (window and content together) and is saved; a stored step that does not fit the
    // work area is shown and applied as the largest step that fits, without changing the stored value.
    void SelectScale(int step) {
        int previous=Prefs.UiScale; Prefs.UiScale=step; ApplyScale(true);
        if(Program.Test)return; try { Prefs.Save(); } catch { Prefs.UiScale=previous; ApplyScale(true); Error("Could not save settings. Check folder permissions."); }
    }
    internal int AppliedScale { get; private set; }
    internal void ApplyScale(bool keepCentre) {
        Size layout=new Size(ContentWidth,ContentHeight); int step=UiScale.Effective(Prefs.UiScale,layout,Edge); double f=step/100.0;
        double cx=Left+ActualWidth/2,cy=Top+ActualHeight/2; bool place=keepCentre&&IsVisible&&!double.IsNaN(Left);
        root.LayoutTransform=step==100?Transform.Identity:new ScaleTransform(f,f); Width=ContentWidth*f+2*Edge; Height=ContentHeight*f+2*Edge; AppliedScale=step;
        for(int i=0;i<UiScale.Steps.Length;i++)scaleControl.SetOff(i,!UiScale.Fits(UiScale.Steps[i],layout,Edge),"Does not fit this screen");
        scaleControl.Select(Array.IndexOf(UiScale.Steps,step),keepCentre);
        if(place) { Rect a=SystemParameters.WorkArea; Left=Math.Max(a.Left,Math.Min(a.Right-Width,cx-Width/2)); Top=Math.Max(a.Top,Math.Min(a.Bottom-Height,cy-Height/2)); }
        if(updates!=null)updates.SetScale(f);
    }
    internal void ApplyAppearance(bool save) {
        sizeText.Text=Prefs.IndicatorScale+"%"; overlay.SetScale(Prefs.IndicatorScale);
        if(renderedPalette!=Prefs.PaletteIndex) {
            renderedPalette=Prefs.PaletteIndex; overlay.Heart.ApplyPalette(Prefs.PaletteIndex);
            foreach(Button card in paletteButtons) {
                bool selected=(int)card.Tag==Prefs.PaletteIndex;
                card.BorderBrush=selected?(Brush)Look.AccentInk:Theme.Hairline; card.Background=selected?Theme.FillHover:Theme.Fill;
                ((TextBlock)((StackPanel)card.Content).Children[1]).Foreground=selected?Theme.Text:Theme.Muted;
            }
            if(ready)RefreshAudio(false); // the cards recolour in the new palette
            if(tray!=null)UpdateState();
        }
        if(save && !Program.Test) { appearanceDirty=true; appearanceSave.Stop(); appearanceSave.Start(); }
    }
    void CommitAppearance() {
        appearanceSave.Stop(); if(!appearanceDirty)return;
        try { Prefs.Save(); appearanceDirty=false; } catch { Error("Could not save appearance. Check folder permissions."); }
    }
    // Writes the held levels; a failure shows its notice once until a save succeeds or the user holds or releases a level again.
    internal void CommitLocks() {
        lockSave.Stop(); if(!locksDirty)return;
        try { Prefs.Save(); locksDirty=false; lockSaveFailed=false; } catch { if(!lockSaveFailed) { lockSaveFailed=true; Error("Could not save settings. Check folder permissions."); } }
    }
    void BuildEditor() {
        editor=new Grid { Visibility=Visibility.Collapsed,Background=Look.Scrim }; root.Children.Add(editor);
        panel=new Border { CornerRadius=new CornerRadius(20),Margin=new Thickness(11,54,11,11),Background=Look.Sheet,BorderThickness=new Thickness(1),BorderBrush=Theme.Hairline,RenderTransform=new TranslateTransform(),Padding=new Thickness(18,10,18,16) }; editor.Children.Add(panel);
        Grid layout=new Grid(); panel.Child=layout; layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        StackPanel top=new StackPanel(); layout.Children.Add(top);
        Grid title=new Grid { Margin=new Thickness(0,0,0,10) }; title.Children.Add(Theme.Label("Hotkey",TypeRoles.Display,Theme.Text));
        Button cancel=new Button { Style=(Style)Resources["TitleButton"],Content=Icons.Make(Icons.Close,17),HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close" }; AutomationProperties.SetName(cancel,"Close hotkey editor");
        cancel.Click+=delegate { CloseEditor(); }; title.Children.Add(cancel); top.Children.Add(title);
        record=new Button { Height=36,HorizontalContentAlignment=HorizontalAlignment.Center }; captureText=Theme.Label("Record a shortcut",TypeRoles.Label,Theme.Text); record.Content=captureText;
        record.ToolTip="Click, then press a key or shortcut. Close this panel to cancel.";
        record.Click+=delegate { if(Hook!=null) { Hook.Engine.BeginCapture(); SetRecording(true); } }; top.Children.Add(record);
        UniformGrid mods=new UniformGrid { Rows=1,Columns=4,Margin=new Thickness(-3,10,-3,10) }; string[] names={"Ctrl","Alt","Shift","Win"}; uint[] bits={2,1,4,8};
        for(int i=0;i<names.Length;i++) { uint bit=bits[i]; Button chip=new Button { Content=names[i],Tag=bit,Height=28,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center,Margin=new Thickness(3,0,3,0) }; TypeRoles.LabelS.Apply(chip); chip.Click+=delegate { draftMods^=bit; SyncDraft(); }; mods.Children.Add(chip); modButtons.Add(chip); } top.Children.Add(mods);
        search=new TextBox { Height=32,Padding=new Thickness(10,6,10,5),Foreground=Theme.Text,Background=Look.BgTop,BorderBrush=Theme.Hairline,CaretBrush=Look.AccentInk,VerticalContentAlignment=VerticalAlignment.Center }; TypeRoles.Mono.Apply(search); search.ToolTip="Search keys";
        Grid searchBox=new Grid(); searchBox.Children.Add(search); searchHint=Theme.Label("Search keys",TypeRoles.Mono,Theme.Muted); searchHint.Margin=new Thickness(12,0,0,0); searchHint.IsHitTestVisible=false; searchBox.Children.Add(searchHint);
        search.TextChanged+=delegate { searchHint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed; FilterKeys(); }; top.Children.Add(searchBox);
        keyList=new ListBox { Background=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(0,8,0,10),Tag=Look.AccentSoft }; TypeRoles.Body.Apply(keyList); ScrollViewer.SetHorizontalScrollBarVisibility(keyList,ScrollBarVisibility.Disabled); VirtualizingStackPanel.SetIsVirtualizing(keyList,true); keyList.SelectionChanged+=delegate { KeyItem item=keyList.SelectedItem as KeyItem; if(item!=null) { draftKey=item.Code; SyncDraft(); } }; Grid.SetRow(keyList,1); layout.Children.Add(keyList);
        StackPanel bottom=new StackPanel(); Grid.SetRow(bottom,2); layout.Children.Add(bottom);
        TextBlock note=Theme.Label("Fn and protected shortcuts depend on Windows.",TypeRoles.Caption,Theme.Muted); note.TextWrapping=TextWrapping.Wrap; note.Margin=new Thickness(0,0,0,10); bottom.Children.Add(note);
        Button apply=new Button { Name="ApplyBinding",Height=36,Content="Apply",HorizontalContentAlignment=HorizontalAlignment.Center }; apply.Click+=delegate { ApplyBinding(); }; bottom.Children.Add(apply);
    }
    internal void OpenEditor() {
        DeviceList.Close(); editing=true; if(Hook!=null)Hook.Engine.Suspended=true; draftKey=Prefs.Key; draftMods=Prefs.Modifiers; search.Text=""; FilterKeys(); SyncDraft();
        editor.Visibility=Visibility.Visible; editor.Opacity=0; ((TranslateTransform)panel.RenderTransform).Y=14; Theme.Fade(editor,1,.22); ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.3));
        foreach(KeyItem item in keyList.Items)if(item.Code==draftKey) { keyList.SelectedItem=item; keyList.ScrollIntoView(item); break; }
    }
    internal void CloseEditor() { editing=false; if(Hook!=null) { Hook.Engine.CancelCapture(); Hook.Engine.Suspended=false; } SetRecording(false); editor.Visibility=Visibility.Collapsed; }
    void FilterKeys() { if(keyList==null)return; string q=search.Text.Trim(); keyList.ItemsSource=KeyCatalog.All().Where(k=>k.Label.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToList(); }
    void SyncDraft() {
        foreach(Button b in modButtons) { bool on=(draftMods&(uint)b.Tag)!=0; b.Background=on?Theme.FillHover:Theme.Fill; b.BorderBrush=on?(Brush)Look.AccentInk:Theme.Hairline; }
        if(captureText!=null && (Hook==null||!Hook.Engine.Capturing))captureText.Text=Preferences.KeyName(draftMods,draftKey);
    }
    void SetRecording(bool active) { captureText.Text=active?"Press a key or shortcut…":Preferences.KeyName(draftMods,draftKey); record.BorderBrush=active?(Brush)Look.AccentInk:Theme.Hairline; }
    void Captured(uint mods,uint key) { if(!editing)return; draftMods=mods; draftKey=key; search.Text=""; SetRecording(false); SyncDraft(); foreach(KeyItem item in keyList.Items)if(item.Code==key) { keyList.SelectedItem=item; keyList.ScrollIntoView(item); break; } }
    void ApplyBinding() {
        if(Hook!=null)Hook.Engine.CancelCapture();
        uint previousKey=Prefs.Key,previousMods=Prefs.Modifiers; Prefs.Key=draftKey; Prefs.Modifiers=draftMods&~KeyEngine.Modifier(draftKey);
        try { Prefs.Save(); if(Hook!=null) { Hook.Engine.Key=Prefs.Key; Hook.Engine.Modifiers=Prefs.Modifiers; } bindText.Text=Preferences.KeyName(Prefs.Modifiers,Prefs.Key); ((Button)bindText.Parent).ToolTip=bindText.Text; CloseEditor(); RefreshAudio(false); }
        catch { Prefs.Key=previousKey; Prefs.Modifiers=previousMods; CloseEditor(); Error("Could not save the shortcut. Check folder permissions."); }
    }
    internal string Notice { get { return noticeBox.Visibility==Visibility.Visible?feedback.Text:""; } }
    void Error(string text) { feedback.Text=text; noticeBox.ToolTip=text; noticeBox.Visibility=Visibility.Visible; feedback.Opacity=0; Theme.Fade(feedback,1,.2); noticeTimer.Stop(); noticeTimer.Start(); }
    internal void CloseNotice() { noticeTimer.Stop(); noticeBox.Visibility=Visibility.Collapsed; noticeBox.ToolTip=null; feedback.Text=""; }
    // ---- audio
    internal void Toggle() { if(editing)return; ToggleFlow(Flow.Capture); }
    void ToggleFlow(Flow flow) {
        try { Source.ToggleMute(flow); }
        catch { Error(flow==Flow.Capture?"Microphone unavailable. Check your default input device.":"Speakers unavailable. Check your default output device."); }
        RefreshAudio(false);
    }
    void ChooseDevice(Flow flow,string id) { try { Source.SetDefault(flow,id); } catch { Error("Could not change the default device."); } RefreshAudio(true); }
    // Opens the device list of a card under (or above) its device button; a second click on the button closes it.
    internal void ToggleList(FlowCard c) {
        if(DeviceList.IsOpen) { bool same=DeviceList.Owner==c; DeviceList.Close(); if(same)return; }
        listed=Source.Devices(c.Flow); if(listed.Count==0)return;
        Point at=c.Device.TranslatePoint(new Point(0,0),content);
        DeviceList.Open(c,listed,new Rect(at,new Size(c.Device.ActualWidth,c.Device.ActualHeight)),content.RenderSize); c.Device.SetOpen(true);
    }
    void FillDevices(Forms.ToolStripMenuItem item,Flow flow) {
        item.DropDownItems.Clear(); Forms.ToolStripDropDownMenu drop=item.DropDown as Forms.ToolStripDropDownMenu;
        if(drop!=null) { drop.ShowImageMargin=false; drop.ShowCheckMargin=true; drop.BackColor=menu.BackColor; drop.ForeColor=menu.ForeColor; }
        List<AudioDevice> list=Source.Devices(flow);
        if(list.Count==0)item.DropDownItems.Add(new Forms.ToolStripMenuItem(flow==Flow.Capture?"No microphone":"No output device") { Enabled=false });
        foreach(AudioDevice d in list) { string id=d.Id; Forms.ToolStripMenuItem choice=new Forms.ToolStripMenuItem(d.Name) { Checked=d.IsDefault }; choice.Click+=delegate { ChooseDevice(flow,id); }; item.DropDownItems.Add(choice); }
    }
    // Feeds the window from a fixed sample (previews, screenshots) or, with null, from the audio engine again.
    internal void UseSample(SampleAudio sample) {
        DeviceList.Close(); Source=sample!=null?(IAudioSource)sample:hubSource;
        foreach(FlowCard c in cards) { c.Shown=Math.Sqrt(Math.Max(0,Source.Peak(c.Flow))); }
        bool wasReady=ready; ready=false; try { RefreshAudio(true); } finally { ready=wasReady; }
        foreach(FlowCard c in cards)c.Level.ShowMeter(c.Last.Present?c.Shown:0,c.Last.Muted);
    }
    internal void RefreshAudio(bool listen) {
        if(cards.Count==0)return; Palette p=Palette.Get(Prefs.PaletteIndex); bool animate=ready&&IsVisible;
        foreach(FlowCard c in cards)ShowCard(c,Source.State(c.Flow),p,animate);
        if(listen)ShowListen(Source.Listening);
        bool? next=MicCard.Last.Present?(bool?)MicCard.Last.Muted:null;
        if(next!=muted||trayIcon==null) { muted=next; UpdateState(); }
        if(overlay!=null)overlay.Place();
    }
    void ShowCard(FlowCard c,EndpointState s,Palette p,bool animate) {
        bool mic=c.Flow==Flow.Capture; c.Last=s;
        MuteLook state=!s.Present?MuteLook.None:mic?(s.Muted?MuteLook.Muted:MuteLook.Live):(s.Muted?MuteLook.SpeakerMuted:MuteLook.SpeakerOn);
        c.Mute.Apply(state,p,animate);
        Brush dot,ink; string text; double glow=0;
        switch(state) {
            case MuteLook.Live: dot=new SolidColorBrush(p.On.Main); c.DotGlow.Color=p.On.Glow; glow=1; ink=new SolidColorBrush(p.On.Light); text="Live"; break;
            case MuteLook.Muted: case MuteLook.SpeakerMuted: dot=new SolidColorBrush(p.Off.Main); ink=new SolidColorBrush(p.Off.Light); text="Muted"; break;
            case MuteLook.SpeakerOn: dot=Look.Accent; ink=Theme.Muted; text="On"; break;
            default: dot=Theme.Brush("#4DFFFFFF"); ink=Theme.Muted; text=mic?"No microphone":"No output device"; break;
        }
        if(c.State.Text!=text||!SameBrush(c.Dot.Fill,dot)) { c.Dot.Fill=dot; c.State.Foreground=ink; c.State.Text=text; }
        c.DotGlow.Opacity=glow;
        string tip=mic?(s.Muted?"Unmute microphone (":"Mute microphone (")+Preferences.KeyName(Prefs.Modifiers,Prefs.Key)+")":s.Muted?"Unmute speakers":"Mute speakers";
        c.Mute.ToolTip=s.Present?tip:null; AutomationProperties.SetName(c.Mute,tip);
        c.Device.SetDevice(s.Name,s.Present);
        c.Level.IsEnabled=s.Present; c.Level.SetLevel(s.Present?AudioHub.Percent(s.Volume):0);
        bool held=s.Present&&Source.IsLocked(c.Flow); c.Lock.IsEnabled=s.Present; c.Lock.SetHeld(held,animate); c.Level.SetHeld(held);
        c.Value.Foreground=held?(Brush)Look.AccentInk:Theme.Ink2;
        c.Lock.ToolTip=held?"Held at "+(int)c.Level.Value+". Click to release.":"Hold this level. Changes made by Windows or other apps are undone.";
    }
    static bool SameBrush(Brush a,Brush b) { if(a==b)return true; SolidColorBrush x=a as SolidColorBrush,y=b as SolidColorBrush; return x!=null&&y!=null&&x.Color==y.Color&&x.Opacity==y.Opacity; }
    void ShowListen(bool? on) {
        if(ListenToggle==null)return; ListenToggle.IsChecked=on==true; ListenRow.IsEnabled=on.HasValue; ListenRow.Opacity=on.HasValue?1:.38;
        listenHint.Text=TypeRoles.Caption.Text(on.HasValue?"Play this microphone through the speakers":"Not available for this device");
    }
    void Metering(bool on) {
        if(quitting)on=false; if(Hub!=null)Hub.SetMetering(on);
        if(on) { meterAt=DateTime.UtcNow; meterTimer.Start(); } else meterTimer.Stop();
    }
    // The meters: sqrt of the endpoint peak, instant attack, decay 1.5 per second.
    void Meter() {
        DateTime now=DateTime.UtcNow; double dt=Math.Min(.25,(now-meterAt).TotalSeconds); meterAt=now;
        foreach(FlowCard c in cards) { double level=Math.Sqrt(Math.Max(0,Source.Peak(c.Flow))); c.Shown=Math.Max(level,c.Shown-1.5*dt); c.Level.ShowMeter(c.Last.Present?c.Shown:0,c.Last.Muted); }
    }
    internal bool MeterRunning { get { return meterTimer.IsEnabled; } }
    internal string ListenHint { get { return listenHint.Text; } }
    void UpdateState() {
        string text=!muted.HasValue?"Microphone unavailable":muted.Value?"Microphone off":"Microphone on";
        overlay.Heart.SetState(muted);
        if(tray==null)return;
        System.Drawing.Icon old=trayIcon; trayIcon=MakeIcon(muted,Prefs.PaletteIndex); tray.Icon=trayIcon; if(old!=null)old.Dispose(); tray.Text=text;
    }
    internal static System.Drawing.Icon MakeIcon(bool? muted,int paletteIndex) {
        using(System.Drawing.Bitmap b=new System.Drawing.Bitmap(32,32)) {
            using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(b)) {
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(System.Drawing.Drawing2D.GraphicsPath p=new System.Drawing.Drawing2D.GraphicsPath()) {
                    p.AddBezier(16,29,11,24,2,18,2,10); p.AddBezier(2,10,2,2,12,1,16,8); p.AddBezier(16,8,20,1,30,2,30,10); p.AddBezier(30,10,30,18,21,25,16,29);
                    Color color=muted==false?Palette.Get(paletteIndex).On.Main:Palette.Get(paletteIndex).Off.Main;
                    using(System.Drawing.Brush brush=new System.Drawing.SolidBrush(!muted.HasValue?System.Drawing.Color.Gray:System.Drawing.Color.FromArgb(color.R,color.G,color.B)))g.FillPath(brush,p);
                    if(muted!=false) { g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy; using(System.Drawing.Pen pen=new System.Drawing.Pen(System.Drawing.Color.Transparent,2.5f))g.DrawLines(pen,new System.Drawing.Point[]{new System.Drawing.Point(17,5),new System.Drawing.Point(14,12),new System.Drawing.Point(19,17),new System.Drawing.Point(15,22),new System.Drawing.Point(17,30)}); }
                }
            }
            IntPtr h=b.GetHicon(); try { using(System.Drawing.Icon i=System.Drawing.Icon.FromHandle(h))return (System.Drawing.Icon)i.Clone(); } finally { Native.DestroyIcon(h); }
        }
    }
    static bool IsStartup() { try { using(RegistryKey k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&(k.GetValue("MicControlGlow")!=null||k.GetValue("MicMuteGlow")!=null); } catch { return false; } }
    // The presenter's models in the three places of the settings window: the title bar pill, the tray item and the update
    // row of the Settings sheet. The update window draws the sheet model itself (UpdateWindow.Render).
    void ShowUpdate(UpdateView view) {
        PanelView p=view.Panel;
        TitleBar.ShowUpdate(view.Pill,Look);
        updateText.Text=p.StatusLine;
        updateButton.Content=p.RowAction.Label; updateButton.IsEnabled=p.RowAction.Enabled; updateButton.Tag=p.RowAction.Command; updateButton.ToolTip=p.RowAction.Tooltip;
        updateItem.Text=p.Action.Label; updateItem.Enabled=p.Action.Enabled;
    }
    // A toast of the presenter ("Updated to v2.3.0", "Mic Control 2.3.0 is ready") as a tray balloon.
    void ShowToast(UpdateToast toast) { string title,text; AppUpdate.Balloon(toast,out title,out text); Balloon(title,text,toast.Action); }
    void Balloon(string title,string text,Action onClick) { if(quitting||!tray.Visible)return; balloonAction=onClick; tray.BalloonTipTitle=title; tray.BalloonTipText=text; tray.ShowBalloonTip(8000); }
    internal void HideSettings() { CommitAppearance(); CommitLocks(); CloseEditor(); DeviceList.Close(); CloseSheet(); Hide(); }
    void OpenSettings() { Show(); WindowState=WindowState.Normal; Activate(); shell.BeginAnimation(OpacityProperty,null); shell.Opacity=1; }
    internal void Exit() {
        if(quitting)return; quitting=true; CommitAppearance(); CommitLocks(); poll.Stop(); meterTimer.Stop(); noticeTimer.Stop(); if(updates!=null)updates.Dispose(); if(Hook!=null)Hook.Dispose();
        if(Hub!=null) { Hub.Dispose(); Hub=null; }
        overlay.Close(); tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); if(trayIcon!=null)trayIcon.Dispose(); Close(); Application.Current.Shutdown();
    }
}
// The Scale setting: 100 / 125 / 150 / 175 / 200 % on top of Windows' own display scaling. The window content scales as
// one LayoutTransform and the window size follows, so the logical layout stays the same at every step.
static class UiScale {
    internal static readonly int[] Steps={100,125,150,175,200};
    internal const int Default=100;
    // The nearest step for any value; the default for zero, negative or unreadable values.
    internal static int Normalize(int value) { if(value<=0)return Default; int best=Steps[0]; foreach(int s in Steps)if(Math.Abs(s-value)<Math.Abs(best-value))best=s; return best; }
    internal static bool Fits(int step,Size layout,double edge) { Rect a=SystemParameters.WorkArea; double f=step/100.0; return layout.Width*f+2*edge<=a.Width&&layout.Height*f+2*edge<=a.Height; }
    // The step that is applied: the stored one, or the largest smaller step that fits the work area.
    internal static int Effective(int step,Size layout,double edge) { for(int i=Array.IndexOf(Steps,Normalize(step));i>0;i--)if(Fits(Steps[i],layout,edge))return Steps[i]; return Default; }
}
}
