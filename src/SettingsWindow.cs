using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
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
// The app window: the standard title bar, the main view (heart, microphone state, Mute, hotkey, indicator switch) and
// two sheets over it: Settings (the gear: Appearance with the Scale row, General, Updates) and the hotkey editor.
// Fixed size; the Scale setting multiplies the logical 372 x 500 layout and the window size together.
class SettingsWindow : Window {
    internal const double ContentWidth=372,ContentHeight=500,Edge=12; // logical layout; Edge is the shadow margin around it
    internal Preferences Prefs; internal KeyboardHook Hook; internal HeartView Heart;
    internal GlowOverlay overlay; Forms.NotifyIcon tray; System.Drawing.Icon trayIcon; DispatcherTimer poll,appearanceSave;
    internal Grid root; Grid shell,editor,sheet; Border panel,sheetPanel; TextBlock status,bindText,feedback,captureText,searchHint; Button mainToggle;
    ToggleButton overlayToggle,startToggle; ListBox keyList; TextBox search; Button record; List<Button> modButtons=new List<Button>();
    internal Slider sizeSlider; internal List<Button> paletteButtons=new List<Button>(); TextBlock sizeText; bool appearanceDirty; int renderedPalette=-1;
    internal ThemeLook Look=new ThemeLook(); internal List<Button> themeButtons=new List<Button>(); Forms.ContextMenuStrip menu;
    internal TitleBar TitleBar; internal Segmented scaleControl; internal bool SheetOpen;
    uint draftKey,draftMods; bool startup,quitting,ready,editing; bool? muted; bool initialized; readonly bool hidden;
    UpdateHost updates; Forms.ToolStripMenuItem updateItem; TextBlock updateText; Button updateButton; ToggleButton autoUpdateToggle; Action balloonAction;
    internal SettingsWindow(bool startHidden) {
        hidden=startHidden; Prefs=Preferences.Load();
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Title=AppInfo.ProductName+" · Settings · "+AppInfo.DisplayVersion;
        Resources=Theme.Resources(Look); FontFamily=TypeRoles.TextFamily; Foreground=Theme.Text; Look.Apply(Themes.Get(Prefs.Theme),false);
        appearanceSave=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(350) }; appearanceSave.Tick+=delegate { CommitAppearance(); };
        Build(); overlay=new GlowOverlay(); ApplyAppearance(false);
        if(!Program.Test) {
            Hook=new KeyboardHook(); Hook.Engine.Key=Prefs.Key; Hook.Engine.Modifiers=Prefs.Modifiers;
            Hook.Engine.Fired=delegate { Dispatcher.BeginInvoke((Action)Toggle); };
            Hook.Engine.Captured=delegate(uint mods,uint key) { Dispatcher.BeginInvoke((Action)delegate { Captured(mods,key); }); };
        }
        menu=new Forms.ContextMenuStrip(); menu.ForeColor=System.Drawing.Color.FromArgb(245,243,236); menu.ShowImageMargin=false; ApplyTheme(false);
        menu.Items.Add("Toggle microphone",null,delegate { Toggle(); }); menu.Items.Add("Settings",null,delegate { OpenSettings(); }); menu.Items.Add(new Forms.ToolStripSeparator());
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
        poll=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) }; poll.Tick+=delegate { RefreshAudio(); }; poll.Start();
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(!quitting) { e.Cancel=true; HideSettings(); } };
        Deactivated+=delegate { if(Hook!=null)Hook.Engine.CancelCapture(); if(editing)SetRecording(false); };
        IsVisibleChanged+=delegate { if(!IsVisible && Hook!=null)Hook.Engine.CancelCapture(); };
        PreviewKeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key!=Key.Escape)return; if(editing) { CloseEditor(); e.Handled=true; } else if(SheetOpen) { CloseSheet(); e.Handled=true; } };
        Loaded+=delegate { if(!initialized) { initialized=true; shell.Opacity=0; TranslateTransform move=(TranslateTransform)shell.RenderTransform; move.Y=14; Theme.Fade(shell,1,.55); move.BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.65)); } };
        RefreshAudio(); ready=true;
    }
    internal void Start() {
        if(!hidden)Show(); if(Prefs.Overlay)overlay.Show();
        if(updates!=null)updates.Start();
    }
    static Border Line(double around) { return new Border { Height=1,Background=Theme.Brush("#14FFFFFF"),Margin=new Thickness(0,around,0,around) }; }
    void Build() {
        root=new Grid { Margin=new Thickness(Edge),Width=ContentWidth,Height=ContentHeight }; Content=root;
        shell=new Grid { RenderTransform=new TranslateTransform(),Background=Brushes.Transparent }; root.Children.Add(shell);
        Border frame=new Border { CornerRadius=new CornerRadius(24),BorderThickness=new Thickness(1),BorderBrush=Look.FrameEdge,Background=Look.Frame,Effect=Look.FrameShadow }; shell.Children.Add(frame);
        Grid content=new Grid { Margin=new Thickness(1) }; shell.Children.Add(content);
        content.SizeChanged+=delegate { content.Clip=new RectangleGeometry(new Rect(content.RenderSize),23,23); };
        Canvas atmosphere=new Canvas { IsHitTestVisible=false }; content.Children.Add(atmosphere);
        // The "deep field" of the shared look: a large soft field of the hue drifting across the top, a smaller dimmer one of the accent in the bottom-right corner.
        Ellipse nebula=new Ellipse { Width=320,Height=270,Opacity=.42,Fill=Look.Nebula,RenderTransform=new TranslateTransform() };
        Canvas.SetLeft(nebula,30); Canvas.SetTop(nebula,-70); atmosphere.Children.Add(nebula);
        DoubleAnimation drift=new DoubleAnimation(-12,20,TimeSpan.FromSeconds(9)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }; ((TranslateTransform)nebula.RenderTransform).BeginAnimation(TranslateTransform.XProperty,drift);
        Ellipse field=new Ellipse { Width=220,Height=190,Opacity=.5,Fill=Look.Field,RenderTransform=new TranslateTransform() };
        Canvas.SetLeft(field,200); Canvas.SetTop(field,ContentHeight-200); atmosphere.Children.Add(field);
        DoubleAnimation fieldDrift=new DoubleAnimation(10,-14,TimeSpan.FromSeconds(13)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }; ((TranslateTransform)field.RenderTransform).BeginAnimation(TranslateTransform.YProperty,fieldDrift);
        Random random=new Random(42);
        for(int i=0;i<13;i++) { Ellipse star=new Ellipse { Width=i%4==0?2:1,Height=i%4==0?2:1,Fill=Look.Line,Opacity=.15 }; Canvas.SetLeft(star,20+random.Next(330)); Canvas.SetTop(star,60+random.Next(230)); atmosphere.Children.Add(star); star.BeginAnimation(OpacityProperty,new DoubleAnimation(.08,.4,TimeSpan.FromSeconds(2+random.NextDouble()*4)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(random.NextDouble()*2) }); }
        TitleBar=new TitleBar(Look,Resources,AppInfo.ProductName,AppInfo.DisplayVersion) { VerticalAlignment=VerticalAlignment.Top }; content.Children.Add(TitleBar);
        TitleBar.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.ChangedButton==MouseButton.Left)DragMove(); };
        TitleBar.Pill.Click+=delegate { if(updates!=null)updates.PillClicked(); }; TitleBar.Pill.IsEnabled=!Program.Test;
        TitleBar.Gear.Click+=delegate { if(SheetOpen)CloseSheet(); else OpenSheet(); };
        TitleBar.Minimize.Click+=delegate { HideSettings(); }; TitleBar.Close.Click+=delegate { HideSettings(); };
        StackPanel stack=new StackPanel { Margin=new Thickness(22,48,22,0),VerticalAlignment=VerticalAlignment.Top }; content.Children.Add(stack);
        Grid hero=new Grid { Height=150 };
        Ellipse orbit=new Ellipse { Width=168,Height=50,StrokeThickness=.7,Stroke=Look.Orbit,Margin=new Thickness(0,0,0,30),RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(-23),IsHitTestVisible=false };
        ((RotateTransform)orbit.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(-23,-13,TimeSpan.FromSeconds(9)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }); hero.Children.Add(orbit);
        Heart=new HeartView(160); Heart.VerticalAlignment=VerticalAlignment.Top; Heart.Margin=new Thickness(0,-18,0,0); hero.Children.Add(Heart);
        status=Theme.Label("",TypeRoles.Caption,Theme.Muted); status.HorizontalAlignment=HorizontalAlignment.Center; status.VerticalAlignment=VerticalAlignment.Bottom; status.Margin=new Thickness(0,0,0,4); hero.Children.Add(status); stack.Children.Add(hero);
        mainToggle=new Button { Height=48,Content="Mute",HorizontalContentAlignment=HorizontalAlignment.Center,Style=(Style)Resources["Tinted"],Background=Look.AccentGrad,Effect=Look.AccentGlow,Margin=new Thickness(56,8,56,0) };
        TypeRoles.Title.Apply(mainToggle); mainToggle.Click+=delegate { Toggle(); }; stack.Children.Add(mainToggle);
        stack.Children.Add(Line(14));
        Grid keyRow=new Grid { Height=46 }; keyRow.ColumnDefinitions.Add(new ColumnDefinition()); keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        keyRow.Children.Add(Theme.Label("Hotkey",TypeRoles.Label,Theme.Text));
        Button binding=new Button { MinWidth=120,MaxWidth=210,Height=32,HorizontalContentAlignment=HorizontalAlignment.Center,Padding=new Thickness(12,0,12,0) };
        bindText=Theme.Label(Preferences.KeyName(Prefs.Modifiers,Prefs.Key),TypeRoles.Label,Look.AccentInk); bindText.TextTrimming=TextTrimming.CharacterEllipsis; binding.Content=bindText; binding.ToolTip=bindText.Text; binding.Click+=delegate { OpenEditor(); }; Grid.SetColumn(binding,1); keyRow.Children.Add(binding); stack.Children.Add(keyRow);
        stack.Children.Add(new Border { Height=4 });
        overlayToggle=SwitchRow(stack,"Indicator",null,Prefs.Overlay); overlayToggle.Click+=delegate { if(!ready)return; bool previous=Prefs.Overlay; Prefs.Overlay=overlayToggle.IsChecked==true; try { Prefs.Save(); if(Prefs.Overlay)overlay.Show(); else overlay.Hide(); } catch { Prefs.Overlay=previous; overlayToggle.IsChecked=previous; Error("Could not save settings. Check folder permissions."); } };
        feedback=Theme.Label("",TypeRoles.Caption,Theme.Rose); feedback.TextWrapping=TextWrapping.Wrap; feedback.Margin=new Thickness(0,6,0,0); feedback.MaxHeight=34; stack.Children.Add(feedback);
        Grid footer=new Grid { VerticalAlignment=VerticalAlignment.Bottom,Height=40,Margin=new Thickness(22,0,16,8) }; content.Children.Add(footer);
        footer.Children.Add(Theme.Label("Default input device",TypeRoles.Caption,Theme.Muted));
        Button exit=new Button { Content="Quit",Height=28,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(12,0,12,0) }; TypeRoles.LabelS.Apply(exit); exit.Click+=delegate { Exit(); }; footer.Children.Add(exit);
        BuildSheet(content);
        BuildEditor();
    }
    ToggleButton SwitchRow(Panel parent,string title,string hint,bool on) {
        Grid row=new Grid { MinHeight=46 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        StackPanel text=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0) }; text.Children.Add(Theme.Label(title,TypeRoles.Label,Theme.Text));
        if(hint!=null) { TextBlock h=Theme.Label(hint,TypeRoles.Caption,Theme.Muted); h.TextWrapping=TextWrapping.Wrap; h.Margin=new Thickness(0,2,0,0); text.Children.Add(h); }
        row.Children.Add(text);
        ToggleButton toggle=new ToggleButton { IsChecked=on,Background=Look.AccentGrad,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center }; System.Windows.Automation.AutomationProperties.SetName(toggle,title); Grid.SetColumn(toggle,1); row.Children.Add(toggle); parent.Children.Add(row); return toggle;
    }
    static TextBlock Section(string title) { TextBlock t=Theme.Label(title,TypeRoles.Section,Theme.Muted); t.Margin=new Thickness(0,16,0,6); return t; }
    static StackPanel Heading(string title,string hint) {
        StackPanel heading=new StackPanel { Margin=new Thickness(0,6,0,0) }; heading.Children.Add(Theme.Label(title,TypeRoles.Label,Theme.Text));
        if(hint!=null) { TextBlock h=Theme.Label(hint,TypeRoles.Caption,Theme.Muted); h.Margin=new Thickness(0,2,0,0); heading.Children.Add(h); }
        return heading;
    }
    // The Settings sheet: opened by the gear, closed by the gear, its close button or Esc.
    void BuildSheet(Grid content) {
        sheet=new Grid { Visibility=Visibility.Collapsed,Background=Look.Scrim,Margin=new Thickness(0,44,0,0) }; content.Children.Add(sheet);
        sheet.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.OriginalSource==sheet)CloseSheet(); };
        sheetPanel=new Border { CornerRadius=new CornerRadius(20),Margin=new Thickness(10,2,10,10),Background=Look.Sheet,BorderThickness=new Thickness(1),BorderBrush=Theme.Hairline,RenderTransform=new TranslateTransform(),Padding=new Thickness(18,10,6,14) }; sheet.Children.Add(sheetPanel);
        Grid layout=new Grid(); sheetPanel.Child=layout; layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        Grid title=new Grid { Margin=new Thickness(0,0,8,2) }; title.Children.Add(Theme.Label("Settings",TypeRoles.Display,Theme.Text));
        Button close=new Button { Style=(Style)Resources["TitleButton"],Content=Icons.Make(Icons.Close,15),HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close" }; System.Windows.Automation.AutomationProperties.SetName(close,"Close settings");
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
        if(SheetOpen)return; SheetOpen=true; TitleBar.SetSettingsOpen(true);
        sheet.Visibility=Visibility.Visible; sheet.Opacity=0; ((TranslateTransform)sheetPanel.RenderTransform).Y=12; Theme.Fade(sheet,1,.2); ((TranslateTransform)sheetPanel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.3));
    }
    internal void CloseSheet() { if(!SheetOpen)return; SheetOpen=false; TitleBar.SetSettingsOpen(false); CommitAppearance(); sheet.Visibility=Visibility.Collapsed; }
    void BuildAppearance(Panel parent) {
        Grid sizeRow=new Grid { Height=24 }; parent.Children.Add(sizeRow);
        sizeRow.Children.Add(Theme.Label("Indicator size",TypeRoles.Label,Theme.Text)); sizeText=Theme.Label(Prefs.IndicatorScale+"%",TypeRoles.Caption,Theme.Ink2); sizeText.HorizontalAlignment=HorizontalAlignment.Right; sizeRow.Children.Add(sizeText);
        sizeSlider=new Slider { Minimum=50,Maximum=200,Value=Prefs.IndicatorScale,TickFrequency=5,IsSnapToTickEnabled=true,SmallChange=5,LargeChange=25,IsMoveToPointEnabled=true,Height=24,Margin=new Thickness(0,0,0,4),ToolTip="Resize the desktop indicator",Foreground=Look.Accent,BorderBrush=Look.AccentInk,Tag=Look.ThumbGlow };
        System.Windows.Automation.AutomationProperties.SetName(sizeSlider,"Indicator size"); parent.Children.Add(sizeSlider);
        sizeSlider.ValueChanged+=delegate { if(!ready)return; Prefs.IndicatorScale=(int)Math.Round(sizeSlider.Value); ApplyAppearance(true); };
        BuildThemeRow(parent);
        Grid paletteHeading=new Grid { Margin=new Thickness(0,6,0,0) }; parent.Children.Add(paletteHeading); paletteHeading.Children.Add(Theme.Label("Color palette",TypeRoles.Label,Theme.Text));
        TextBlock legend=Theme.Label("On / Off",TypeRoles.Section,Theme.Muted); legend.HorizontalAlignment=HorizontalAlignment.Right; paletteHeading.Children.Add(legend);
        UniformGrid cards=new UniformGrid { Columns=5,Rows=1,Margin=new Thickness(-2,8,-2,4),Height=54 }; parent.Children.Add(cards);
        for(int i=0;i<Palette.All.Length;i++) {
            int selected=i; Palette palette=Palette.All[i];
            Button card=new Button { Padding=new Thickness(3),Margin=new Thickness(2,0,2,0),HorizontalContentAlignment=HorizontalAlignment.Center,ToolTip=palette.Description,Tag=i };
            System.Windows.Automation.AutomationProperties.SetName(card,palette.Name+" palette: "+palette.Description);
            StackPanel contents=new StackPanel(); StackPanel swatches=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,5) };
            foreach(ColorRamp ramp in new ColorRamp[]{palette.On,palette.Off})swatches.Children.Add(new Ellipse { Width=12,Height=12,Margin=new Thickness(3,0,3,0),Fill=ramp.Brush(),Effect=new DropShadowEffect { ShadowDepth=0,BlurRadius=8,Color=ramp.Glow,Opacity=.45 } });
            contents.Children.Add(swatches); TextBlock name=Theme.Label(palette.Name,TypeRoles.Caption,Theme.Muted); name.HorizontalAlignment=HorizontalAlignment.Center; contents.Children.Add(name); card.Content=contents;
            card.Click+=delegate { Prefs.PaletteIndex=selected; ApplyAppearance(true); }; cards.Children.Add(card); paletteButtons.Add(card);
        }
        parent.Children.Add(Heading("Scale","Size of the window and its text"));
        string[] labels=new string[UiScale.Steps.Length]; for(int i=0;i<labels.Length;i++)labels[i]=UiScale.Steps[i]+" %";
        scaleControl=new Segmented(Resources,"Scale",labels,TypeRoles.LabelS,26) { Margin=new Thickness(0,8,0,4) }; parent.Children.Add(scaleControl);
        scaleControl.Changed+=delegate(int index) { SelectScale(UiScale.Steps[index]); };
    }
    // The Theme row of the shared look: four chips (a round swatch of the hue with the accent dot, then the label); arrows move the choice, a choice applies at once and is saved.
    void BuildThemeRow(Panel parent) {
        parent.Children.Add(Heading("Theme","Background colour and accent of the window"));
        UniformGrid row=new UniformGrid { Columns=Themes.All.Length,Rows=1,Height=32,Margin=new Thickness(-2,8,-2,8) }; System.Windows.Automation.AutomationProperties.SetName(row,"Theme"); parent.Children.Add(row);
        for(int i=0;i<Themes.All.Length;i++) {
            ThemeSpec theme=Themes.All[i]; int index=i;
            Button chip=new Button { Height=32,Padding=new Thickness(4,0,6,0),Margin=new Thickness(2,0,2,0),HorizontalContentAlignment=HorizontalAlignment.Left,Background=Theme.Brush("#0DFFFFFF"),BorderBrush=Brushes.Transparent,Tag=i };
            System.Windows.Automation.AutomationProperties.SetName(chip,theme.Label+" theme");
            StackPanel contents=new StackPanel { Orientation=Orientation.Horizontal }; contents.Children.Add(Swatch(theme));
            TextBlock name=Theme.Label(theme.Label,TypeRoles.LabelS,Theme.Ink2); name.Margin=new Thickness(5,0,0,0); name.TextTrimming=TextTrimming.CharacterEllipsis; contents.Children.Add(name); chip.Content=contents;
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
    void ApplyTheme(bool animate) {
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
            renderedPalette=Prefs.PaletteIndex; Heart.ApplyPalette(Prefs.PaletteIndex); overlay.Heart.ApplyPalette(Prefs.PaletteIndex);
            foreach(Button card in paletteButtons) {
                bool selected=(int)card.Tag==Prefs.PaletteIndex;
                card.BorderBrush=selected?(Brush)Look.AccentInk:Theme.Hairline; card.Background=selected?Theme.FillHover:Theme.Fill;
                ((TextBlock)((StackPanel)card.Content).Children[1]).Foreground=selected?Theme.Text:Theme.Muted;
            }
            if(tray!=null)UpdateState();
        }
        if(save && !Program.Test) { appearanceDirty=true; appearanceSave.Stop(); appearanceSave.Start(); }
    }
    void CommitAppearance() {
        appearanceSave.Stop(); if(!appearanceDirty)return;
        try { Prefs.Save(); appearanceDirty=false; } catch { Error("Could not save appearance. Check folder permissions."); }
    }
    void BuildEditor() {
        editor=new Grid { Visibility=Visibility.Collapsed,Background=Look.Scrim }; root.Children.Add(editor);
        panel=new Border { CornerRadius=new CornerRadius(20),Margin=new Thickness(11,46,11,11),Background=Look.Sheet,BorderThickness=new Thickness(1),BorderBrush=Theme.Hairline,RenderTransform=new TranslateTransform(),Padding=new Thickness(18,10,18,16) }; editor.Children.Add(panel);
        Grid layout=new Grid(); panel.Child=layout; layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        StackPanel top=new StackPanel(); layout.Children.Add(top);
        Grid title=new Grid { Margin=new Thickness(0,0,0,10) }; title.Children.Add(Theme.Label("Hotkey",TypeRoles.Display,Theme.Text));
        Button cancel=new Button { Style=(Style)Resources["TitleButton"],Content=Icons.Make(Icons.Close,15),HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close" }; System.Windows.Automation.AutomationProperties.SetName(cancel,"Close hotkey editor");
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
        editing=true; if(Hook!=null)Hook.Engine.Suspended=true; draftKey=Prefs.Key; draftMods=Prefs.Modifiers; search.Text=""; FilterKeys(); SyncDraft();
        editor.Visibility=Visibility.Visible; editor.Opacity=0; ((TranslateTransform)panel.RenderTransform).Y=14; Theme.Fade(editor,1,.22); ((TranslateTransform)panel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.3));
        foreach(KeyItem item in keyList.Items)if(item.Code==draftKey) { keyList.SelectedItem=item; keyList.ScrollIntoView(item); break; }
    }
    void CloseEditor() { editing=false; if(Hook!=null) { Hook.Engine.CancelCapture(); Hook.Engine.Suspended=false; } SetRecording(false); editor.Visibility=Visibility.Collapsed; }
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
        try { Prefs.Save(); if(Hook!=null) { Hook.Engine.Key=Prefs.Key; Hook.Engine.Modifiers=Prefs.Modifiers; } bindText.Text=Preferences.KeyName(Prefs.Modifiers,Prefs.Key); CloseEditor(); }
        catch { Prefs.Key=previousKey; Prefs.Modifiers=previousMods; CloseEditor(); Error("Could not save the shortcut. Check folder permissions."); }
    }
    void Error(string text) { feedback.Text=text; Theme.Fade(feedback,1,.2); }
    internal void Toggle() { if(editing)return; try { muted=Audio.Toggle(); feedback.Text=""; UpdateState(); } catch { muted=null; UpdateState(); Error("Microphone unavailable. Check your default input device."); } }
    void RefreshAudio() { bool? next; try { next=Audio.Read(); } catch { next=null; } if(next!=muted||trayIcon==null) { muted=next; UpdateState(); } overlay.Place(); }
    void UpdateState() {
        string text=!muted.HasValue?"Microphone unavailable":muted.Value?"Microphone off":"Microphone on";
        status.Text=text; mainToggle.Content=muted==true?"Unmute":"Mute"; mainToggle.IsEnabled=muted.HasValue;
        Heart.SetState(muted); overlay.Heart.SetState(muted);
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
    void HideSettings() { CommitAppearance(); CloseEditor(); CloseSheet(); Hide(); }
    void OpenSettings() { Show(); WindowState=WindowState.Normal; Activate(); shell.BeginAnimation(OpacityProperty,null); shell.Opacity=1; }
    internal void Exit() { if(quitting)return; quitting=true; CommitAppearance(); poll.Stop(); if(updates!=null)updates.Dispose(); if(Hook!=null)Hook.Dispose(); overlay.Close(); tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); if(trayIcon!=null)trayIcon.Dispose(); Close(); Application.Current.Shutdown(); }
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
