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
using Forms = System.Windows.Forms;
namespace MicControl {
class SettingsWindow : Window {
    internal Preferences Prefs; internal KeyboardHook Hook; internal HeartView Heart;
    internal GlowOverlay overlay; Forms.NotifyIcon tray; System.Drawing.Icon trayIcon; DispatcherTimer poll,appearanceSave;
    Grid shell,root,editor; Border panel; TextBlock status,bindText,feedback,captureText,searchHint; Button mainToggle;
    ToggleButton overlayToggle,startToggle; ListBox keyList; TextBox search; Button record; List<Button> modButtons=new List<Button>();
    internal Slider sizeSlider; internal List<Button> paletteButtons=new List<Button>(); TextBlock sizeText; bool appearanceDirty; int renderedPalette=-1;
    internal ThemeLook Look=new ThemeLook(); internal List<Button> themeButtons=new List<Button>(); Forms.ContextMenuStrip menu;
    uint draftKey,draftMods; bool startup,quitting,ready,editing; bool? muted; bool initialized; readonly bool hidden;
    Updater updater; DispatcherTimer updateTimer; Forms.ToolStripMenuItem checkItem,restartItem; TextBlock updateText; Button updateButton; ToggleButton autoUpdateToggle; bool readyAnnounced,balloonApplies; int updateFailures;
    UpdateWindow updateWindow; string announcedVersion;
    internal SettingsWindow(bool startHidden) {
        hidden=startHidden; Prefs=Preferences.Load();
        Width=490; Height=Math.Min(880,SystemParameters.WorkArea.Height-24); WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Title=AppInfo.ProductName+" · Settings · "+AppInfo.DisplayVersion;
        FontFamily=new FontFamily("Bahnschrift"); Foreground=Theme.Text; Resources=Theme.Resources(Look); Look.Apply(Themes.Get(Prefs.Theme),false);
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
        checkItem=new Forms.ToolStripMenuItem("Check for updates",null,delegate { UpdateAction(); }) { Enabled=!Program.Test }; menu.Items.Add(checkItem);
        restartItem=new Forms.ToolStripMenuItem("Restart to update",null,delegate { OpenUpdate(); }) { Visible=false }; menu.Items.Add(restartItem);
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Quit",null,delegate { Exit(); });
        tray=new Forms.NotifyIcon { Text=AppInfo.ProductName,ContextMenuStrip=menu,Visible=!Program.Test }; tray.DoubleClick+=delegate { OpenSettings(); };
        tray.BalloonTipClicked+=delegate { if(balloonApplies)ApplyUpdate(); };
        if(!Program.Test) {
            updater=new Updater(new UpdateConfig { Repo=AppInfo.Repository,AssetName=AppInfo.AssetName,AppName=AppInfo.ProductName,Version=AppInfo.Version,UpdatesDir=AppPaths.Updates,ExePath=AppPaths.ExePath,ReleaseUrlVariable=AppInfo.ReleaseUrlVariable },null);
            updater.AutoDownload=delegate { return Prefs.AutoDownloadUpdates; };
            updateWindow=new UpdateWindow(updater,Look,Resources,this,ApplyUpdate);
            updater.OnStatus=delegate(UpdateStatus s) { Dispatcher.BeginInvoke((Action)delegate { ShowUpdate(s); }); };
            updater.OnToast=delegate(string text) { Dispatcher.BeginInvoke((Action)delegate { Balloon(AppInfo.ProductName,text,false); }); };
            updateTimer=new DispatcherTimer { Interval=UpdateSchedule.Wait(0) }; updateTimer.Tick+=delegate { updateTimer.Interval=UpdateSchedule.Wait(updateFailures); if(UpdateSchedule.HasConnection() && updater.Status.Stage!=UpdateStage.Ready)updater.CheckInBackground(); };
        }
        poll=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) }; poll.Tick+=delegate { RefreshAudio(); }; poll.Start();
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(!quitting) { e.Cancel=true; HideSettings(); } };
        Deactivated+=delegate { if(Hook!=null)Hook.Engine.CancelCapture(); if(editing)SetRecording(false); };
        IsVisibleChanged+=delegate { if(!IsVisible && Hook!=null)Hook.Engine.CancelCapture(); };
        Loaded+=delegate { if(!initialized) { initialized=true; shell.Opacity=0; TranslateTransform move=(TranslateTransform)shell.RenderTransform; move.Y=14; Theme.Fade(shell,1,.55); move.BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.65)); } };
        RefreshAudio(); ready=true;
    }
    internal void Start() {
        if(!hidden)Show(); if(Prefs.Overlay)overlay.Show();
        if(updater!=null) { updater.Startup(); updater.CheckInBackground(); updateTimer.Start(); }
    }
    static Border Line() { return new Border { Height=1,Background=Theme.Brush("#14FFFFFF"),Margin=new Thickness(0,21,0,21) }; }
    void Build() {
        root=new Grid { Margin=new Thickness(12) }; Content=root;
        shell=new Grid { RenderTransform=new TranslateTransform(),Background=Brushes.Transparent }; root.Children.Add(shell);
        Border frame=new Border { CornerRadius=new CornerRadius(28),BorderThickness=new Thickness(1),BorderBrush=Look.FrameEdge,Background=Look.Frame,Effect=Look.FrameShadow }; shell.Children.Add(frame);
        Grid content=new Grid { Margin=new Thickness(1),ClipToBounds=true }; shell.Children.Add(content);
        Canvas atmosphere=new Canvas { IsHitTestVisible=false }; content.Children.Add(atmosphere);
        // The "deep field" of the shared look: a large soft field of the hue drifting across the top, a smaller dimmer one of the accent in the bottom-right corner.
        Ellipse nebula=new Ellipse { Width=390,Height=330,Opacity=.42,Fill=Look.Nebula,RenderTransform=new TranslateTransform() };
        Canvas.SetLeft(nebula,60); Canvas.SetTop(nebula,-75); atmosphere.Children.Add(nebula);
        DoubleAnimation drift=new DoubleAnimation(-12,20,TimeSpan.FromSeconds(9)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }; ((TranslateTransform)nebula.RenderTransform).BeginAnimation(TranslateTransform.XProperty,drift);
        Ellipse field=new Ellipse { Width=260,Height=220,Opacity=.5,Fill=Look.Field,RenderTransform=new TranslateTransform() };
        Canvas.SetLeft(field,300); Canvas.SetTop(field,Height-240); atmosphere.Children.Add(field);
        DoubleAnimation fieldDrift=new DoubleAnimation(10,-14,TimeSpan.FromSeconds(13)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }; ((TranslateTransform)field.RenderTransform).BeginAnimation(TranslateTransform.YProperty,fieldDrift);
        Random random=new Random(42);
        for(int i=0;i<15;i++) { Ellipse star=new Ellipse { Width=i%4==0?2:1,Height=i%4==0?2:1,Fill=Look.Line,Opacity=.15 }; Canvas.SetLeft(star,25+random.Next(400)); Canvas.SetTop(star,50+random.Next(275)); atmosphere.Children.Add(star); star.BeginAnimation(OpacityProperty,new DoubleAnimation(.08,.4,TimeSpan.FromSeconds(2+random.NextDouble()*4)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(random.NextDouble()*2) }); }
        Grid header=new Grid { Height=64,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(26,8,16,0),Background=Brushes.Transparent }; content.Children.Add(header);
        header.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.ChangedButton==MouseButton.Left)DragMove(); };
        TextBlock heading=Theme.Label("Settings",18,Theme.Text); heading.HorizontalAlignment=HorizontalAlignment.Left; header.Children.Add(heading);
        StackPanel chrome=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
        Button min=Tiny("−"),close=Tiny("×"); min.Click+=delegate { HideSettings(); }; close.Click+=delegate { HideSettings(); }; chrome.Children.Add(min); chrome.Children.Add(close); header.Children.Add(chrome);
        ScrollViewer settingsScroll=new ScrollViewer { Margin=new Thickness(30,68,30,62),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,3,0) }; content.Children.Add(settingsScroll);
        StackPanel stack=new StackPanel(); settingsScroll.Content=stack;
        Grid hero=new Grid { Height=155 };
        Ellipse orbit=new Ellipse { Width=193,Height=57,StrokeThickness=.7,Stroke=Look.Orbit,Margin=new Thickness(0,0,0,27),RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(-23),IsHitTestVisible=false };
        ((RotateTransform)orbit.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(-23,-13,TimeSpan.FromSeconds(9)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } }); hero.Children.Add(orbit);
        Heart=new HeartView(190); Heart.VerticalAlignment=VerticalAlignment.Top; Heart.Margin=new Thickness(0,-22,0,0); hero.Children.Add(Heart);
        status=Theme.Label("",13,Theme.Muted); status.HorizontalAlignment=HorizontalAlignment.Center; status.VerticalAlignment=VerticalAlignment.Bottom; status.Margin=new Thickness(0,0,0,7); hero.Children.Add(status); stack.Children.Add(hero);
        mainToggle=new Button { Height=46,Content="Mute",HorizontalContentAlignment=HorizontalAlignment.Center,Style=(Style)Resources["Tinted"],Background=Look.AccentGrad,Effect=Look.AccentGlow,Margin=new Thickness(61,10,61,0) }; mainToggle.Click+=delegate { Toggle(); }; stack.Children.Add(mainToggle);
        stack.Children.Add(Line());
        Grid keyRow=new Grid { Height=55 }; keyRow.ColumnDefinitions.Add(new ColumnDefinition()); keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        TextBlock keyLabel=Theme.Label("Hotkey",14,Theme.Text); keyRow.Children.Add(keyLabel);
        Button binding=new Button { MinWidth=136,MaxWidth=230,Height=45,HorizontalContentAlignment=HorizontalAlignment.Center,Padding=new Thickness(13,7,13,7) };
        bindText=Theme.Label(Preferences.KeyName(Prefs.Modifiers,Prefs.Key),13,Look.AccentInk); bindText.TextTrimming=TextTrimming.CharacterEllipsis; binding.Content=bindText; binding.ToolTip=bindText.Text; binding.Click+=delegate { OpenEditor(); }; Grid.SetColumn(binding,1); keyRow.Children.Add(binding); stack.Children.Add(keyRow);
        stack.Children.Add(new Border { Height=14 });
        overlayToggle=SwitchRow(stack,"Indicator",Prefs.Overlay); overlayToggle.Click+=delegate { if(!ready)return; bool previous=Prefs.Overlay; Prefs.Overlay=overlayToggle.IsChecked==true; try { Prefs.Save(); if(Prefs.Overlay)overlay.Show(); else overlay.Hide(); } catch { Prefs.Overlay=previous; overlayToggle.IsChecked=previous; Error("Could not save settings. Check folder permissions."); } };
        BuildAppearance(stack);
        startup=IsStartup(); startToggle=SwitchRow(stack,"Launch at login",startup); startToggle.Click+=delegate { if(!ready)return; try { using(RegistryKey k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(startToggle.IsChecked==true) { k.SetValue("MicControlGlow","\""+Assembly.GetExecutingAssembly().Location+"\" --startup"); k.DeleteValue("MicMuteGlow",false); } else { k.DeleteValue("MicControlGlow",false); k.DeleteValue("MicMuteGlow",false); } startup=startToggle.IsChecked==true; } } catch { startToggle.IsChecked=startup; Error("Could not update launch at login. Check your Windows permissions."); } };
        autoUpdateToggle=SwitchRow(stack,"Download updates automatically",Prefs.AutoDownloadUpdates); autoUpdateToggle.Click+=delegate { if(!ready)return; bool previous=Prefs.AutoDownloadUpdates; Prefs.AutoDownloadUpdates=autoUpdateToggle.IsChecked==true; try { Prefs.Save(); } catch { Prefs.AutoDownloadUpdates=previous; autoUpdateToggle.IsChecked=previous; Error("Could not save settings. Check folder permissions."); return; } if(Prefs.AutoDownloadUpdates && updater!=null && updater.Status.Stage==UpdateStage.Available)updater.DownloadInBackground(); };
        Grid updateRow=new Grid { MinHeight=44 }; updateRow.ColumnDefinitions.Add(new ColumnDefinition()); updateRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        updateText=Theme.Label(Program.Test?"Updates are not checked in the self-test":"Updates are not checked yet",11,Theme.Muted); updateText.TextWrapping=TextWrapping.Wrap; updateText.Margin=new Thickness(0,0,12,0); updateRow.Children.Add(updateText);
        updateButton=new Button { Content="Check for updates",Height=34,FontSize=11,Padding=new Thickness(12,5,12,5),HorizontalContentAlignment=HorizontalAlignment.Center,IsEnabled=!Program.Test }; updateButton.Click+=delegate { UpdateAction(); }; Grid.SetColumn(updateButton,1); updateRow.Children.Add(updateButton); stack.Children.Add(updateRow);
        feedback=Theme.Label("",11,Theme.Rose); feedback.TextWrapping=TextWrapping.Wrap; feedback.Margin=new Thickness(0,6,0,0); feedback.MaxHeight=35; stack.Children.Add(feedback);
        Grid footer=new Grid { VerticalAlignment=VerticalAlignment.Bottom,Height=44,Margin=new Thickness(30,0,27,8) }; content.Children.Add(footer);
        StackPanel meta=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        TextBlock device=Theme.Label("Default input device",10,Theme.Muted); meta.Children.Add(device);
        TextBlock signature=Theme.Label(AppInfo.DisplayVersion+"  ·  "+AppInfo.AuthorTag,9,Theme.Muted); signature.Opacity=.48; meta.Children.Add(signature); footer.Children.Add(meta);
        Button exit=new Button { Content="Quit",Height=30,FontSize=11,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(13,0,13,0) }; exit.Click+=delegate { Exit(); }; footer.Children.Add(exit);
        BuildEditor();
    }
    Button Tiny(string symbol) { return new Button { Content=symbol,Width=30,Height=30,Padding=new Thickness(0,0,0,2),HorizontalContentAlignment=HorizontalAlignment.Center,Style=(Style)Resources["Ghost"],FontSize=19,Margin=new Thickness(4,0,0,0) }; }
    ToggleButton SwitchRow(Panel parent,string title,bool on) {
        Grid row=new Grid { Height=56 }; row.Children.Add(Theme.Label(title,14,Theme.Text)); ToggleButton toggle=new ToggleButton { IsChecked=on,Background=Look.AccentGrad,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center }; System.Windows.Automation.AutomationProperties.SetName(toggle,title); row.Children.Add(toggle); parent.Children.Add(row); return toggle;
    }
    void BuildAppearance(Panel parent) {
        Grid sizeRow=new Grid { Height=35,Margin=new Thickness(0,4,0,0) }; parent.Children.Add(sizeRow);
        sizeRow.Children.Add(Theme.Label("Indicator size",13,Theme.Text)); sizeText=Theme.Label(Prefs.IndicatorScale+"%",12,Theme.Ink2); sizeText.HorizontalAlignment=HorizontalAlignment.Right; sizeRow.Children.Add(sizeText);
        sizeSlider=new Slider { Minimum=50,Maximum=200,Value=Prefs.IndicatorScale,TickFrequency=5,IsSnapToTickEnabled=true,SmallChange=5,LargeChange=25,IsMoveToPointEnabled=true,Height=27,Margin=new Thickness(0,0,0,7),ToolTip="Resize the desktop indicator",Foreground=Look.Accent,BorderBrush=Look.AccentInk,Tag=Look.ThumbGlow };
        System.Windows.Automation.AutomationProperties.SetName(sizeSlider,"Indicator size"); parent.Children.Add(sizeSlider);
        sizeSlider.ValueChanged+=delegate { if(!ready)return; Prefs.IndicatorScale=(int)Math.Round(sizeSlider.Value); ApplyAppearance(true); };
        BuildThemeRow(parent);
        Grid paletteHeading=new Grid { Height=34 }; parent.Children.Add(paletteHeading); paletteHeading.Children.Add(Theme.Label("Color palette",13,Theme.Text));
        TextBlock legend=Theme.Label("ON / OFF",9,Theme.Muted); legend.HorizontalAlignment=HorizontalAlignment.Right; paletteHeading.Children.Add(legend);
        UniformGrid cards=new UniformGrid { Columns=5,Rows=1,Margin=new Thickness(-2,3,-2,13),Height=65 }; parent.Children.Add(cards);
        for(int i=0;i<Palette.All.Length;i++) {
            int selected=i; Palette palette=Palette.All[i];
            Button card=new Button { Padding=new Thickness(4),Margin=new Thickness(2,0,2,0),HorizontalContentAlignment=HorizontalAlignment.Center,ToolTip=palette.Description,Tag=i };
            System.Windows.Automation.AutomationProperties.SetName(card,palette.Name+" palette: "+palette.Description);
            StackPanel contents=new StackPanel(); StackPanel swatches=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,4,0,8) };
            foreach(ColorRamp ramp in new ColorRamp[]{palette.On,palette.Off})swatches.Children.Add(new Ellipse { Width=14,Height=14,Margin=new Thickness(4,0,4,0),Fill=ramp.Brush(),Effect=new DropShadowEffect { ShadowDepth=0,BlurRadius=8,Color=ramp.Glow,Opacity=.45 } });
            contents.Children.Add(swatches); TextBlock name=Theme.Label(palette.Name,10,Theme.Muted); name.HorizontalAlignment=HorizontalAlignment.Center; contents.Children.Add(name); card.Content=contents;
            card.Click+=delegate { Prefs.PaletteIndex=selected; ApplyAppearance(true); }; cards.Children.Add(card); paletteButtons.Add(card);
        }
    }
    // The Theme row of the shared look: four chips (a round swatch of the hue with the accent dot, then the label); arrows move the choice, a choice applies at once and is saved.
    void BuildThemeRow(Panel parent) {
        StackPanel heading=new StackPanel { Margin=new Thickness(0,2,0,0) }; heading.Children.Add(Theme.Label("Theme",13,Theme.Text));
        TextBlock hint=Theme.Label("Background colour and accent of the window",10,Theme.Muted); hint.Margin=new Thickness(0,2,0,0); heading.Children.Add(hint); parent.Children.Add(heading);
        UniformGrid row=new UniformGrid { Columns=Themes.All.Length,Rows=1,Height=36,Margin=new Thickness(-3,9,-3,16) }; System.Windows.Automation.AutomationProperties.SetName(row,"Theme"); parent.Children.Add(row);
        for(int i=0;i<Themes.All.Length;i++) {
            ThemeSpec theme=Themes.All[i]; int index=i;
            Button chip=new Button { Height=36,Padding=new Thickness(5,0,10,0),Margin=new Thickness(3,0,3,0),HorizontalContentAlignment=HorizontalAlignment.Left,Background=Theme.Brush("#0DFFFFFF"),BorderBrush=Brushes.Transparent,Tag=i };
            System.Windows.Automation.AutomationProperties.SetName(chip,theme.Label+" theme");
            StackPanel contents=new StackPanel { Orientation=Orientation.Horizontal }; contents.Children.Add(Swatch(theme));
            TextBlock name=Theme.Label(theme.Label,12,Theme.Ink2); name.Margin=new Thickness(8,0,0,0); contents.Children.Add(name); chip.Content=contents;
            chip.Click+=delegate { SelectTheme(index); };
            chip.KeyDown+=delegate(object s,KeyEventArgs e) { int step=e.Key==Key.Right||e.Key==Key.Down?1:e.Key==Key.Left||e.Key==Key.Up?-1:0; if(step==0)return; int next=(index+step+Themes.All.Length)%Themes.All.Length; themeButtons[next].Focus(); SelectTheme(next); e.Handled=true; };
            row.Children.Add(chip); themeButtons.Add(chip);
        }
    }
    static Grid Swatch(ThemeSpec theme) {
        Grid swatch=new Grid { Width=24,Height=24,Clip=new EllipseGeometry(new Point(12,12),12,12) };
        swatch.Children.Add(new Ellipse { Fill=new LinearGradientBrush(ThemeSpec.Mix(theme.Hue,Colors.Black,.3),theme.Hue,45),Stroke=Theme.Brush("#40FFFFFF"),StrokeThickness=1 });
        swatch.Children.Add(new Ellipse { Width=13,Height=13,Fill=new SolidColorBrush(theme.Accent),Stroke=Theme.Brush("#59000000"),StrokeThickness=1.5,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,-2,-2) });
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
        panel=new Border { CornerRadius=new CornerRadius(23),Margin=new Thickness(18,45,18,28),Background=Look.Sheet,BorderThickness=new Thickness(1),BorderBrush=Theme.Hairline,RenderTransform=new TranslateTransform(),Padding=new Thickness(24) }; editor.Children.Add(panel);
        Grid layout=new Grid(); panel.Child=layout; layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        StackPanel top=new StackPanel(); layout.Children.Add(top);
        Grid title=new Grid { Margin=new Thickness(0,0,0,16) }; title.Children.Add(Theme.Label("Hotkey",18,Theme.Text)); Button cancel=Tiny("×"); cancel.HorizontalAlignment=HorizontalAlignment.Right; cancel.Click+=delegate { CloseEditor(); }; title.Children.Add(cancel); top.Children.Add(title);
        record=new Button { Height=43,HorizontalContentAlignment=HorizontalAlignment.Center }; captureText=Theme.Label("Record a shortcut",13,Theme.Text); record.Content=captureText;
        record.ToolTip="Click, then press a key or shortcut. Close this panel to cancel.";
        record.Click+=delegate { if(Hook!=null) { Hook.Engine.BeginCapture(); SetRecording(true); } }; top.Children.Add(record);
        StackPanel mods=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,14) }; string[] names={"Ctrl","Alt","Shift","Win"}; uint[] bits={2,1,4,8};
        for(int i=0;i<names.Length;i++) { uint bit=bits[i]; Button chip=new Button { Content=names[i],Tag=bit,Width=73,Height=34,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center,FontSize=12,Margin=new Thickness(0,0,7,0) }; chip.Click+=delegate { draftMods^=bit; SyncDraft(); }; mods.Children.Add(chip); modButtons.Add(chip); } top.Children.Add(mods);
        search=new TextBox { FontFamily=new FontFamily("Bahnschrift"),FontSize=13,Height=36,Padding=new Thickness(10,8,10,7),Foreground=Theme.Text,Background=Look.BgTop,BorderBrush=Theme.Hairline,CaretBrush=Look.AccentInk }; search.ToolTip="Search keys";
        Grid searchBox=new Grid(); searchBox.Children.Add(search); searchHint=Theme.Label("Search keys",12,Theme.Muted); searchHint.Margin=new Thickness(11,0,0,0); searchHint.IsHitTestVisible=false; searchBox.Children.Add(searchHint);
        search.TextChanged+=delegate { searchHint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed; FilterKeys(); }; top.Children.Add(searchBox);
        keyList=new ListBox { Background=Brushes.Transparent,BorderThickness=new Thickness(0),FontFamily=new FontFamily("Bahnschrift"),FontSize=13,Margin=new Thickness(0,10,0,12),Tag=Look.AccentSoft }; ScrollViewer.SetHorizontalScrollBarVisibility(keyList,ScrollBarVisibility.Disabled); VirtualizingStackPanel.SetIsVirtualizing(keyList,true); keyList.SelectionChanged+=delegate { KeyItem item=keyList.SelectedItem as KeyItem; if(item!=null) { draftKey=item.Code; SyncDraft(); } }; Grid.SetRow(keyList,1); layout.Children.Add(keyList);
        StackPanel bottom=new StackPanel(); Grid.SetRow(bottom,2); layout.Children.Add(bottom);
        TextBlock note=Theme.Label("Fn and protected shortcuts depend on Windows.",10,Theme.Muted); note.TextWrapping=TextWrapping.Wrap; note.Margin=new Thickness(0,0,0,12); bottom.Children.Add(note);
        Button apply=new Button { Name="ApplyBinding",Height=43,Content="Apply",HorizontalContentAlignment=HorizontalAlignment.Center }; apply.Click+=delegate { ApplyBinding(); }; bottom.Children.Add(apply);
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
    // While a newer version is known (offered, downloading, ready, or its download failed) the tray item and the settings
    // button open the update window instead of acting directly; otherwise they start a check.
    static bool NewerKnown(UpdateStatus s) { return s.Latest!=null&&(s.Stage==UpdateStage.Available||s.Stage==UpdateStage.Downloading||s.Stage==UpdateStage.Ready||s.Stage==UpdateStage.Error); }
    void UpdateAction() { if(updater==null)return; if(NewerKnown(updater.Status))OpenUpdate(); else updater.CheckInBackground(); }
    void OpenUpdate() { if(updateWindow!=null&&!quitting)updateWindow.Open(); }
    void ShowUpdate(UpdateStatus s) {
        bool checking=s.Stage==UpdateStage.Checking,failedDownload=s.Stage==UpdateStage.Error&&s.Latest!=null;
        updateText.Text=s.Message; updateButton.IsEnabled=!checking;
        updateButton.Content=s.Stage==UpdateStage.Ready?"Restart to update":s.Stage==UpdateStage.Available?"Download":s.Stage==UpdateStage.Downloading?"Show progress":failedDownload?"Show update":"Check for updates";
        checkItem.Text=s.Stage==UpdateStage.Available||failedDownload?"Download update":s.Stage==UpdateStage.Downloading?"Downloading update…":"Check for updates"; checkItem.Enabled=!checking&&s.Stage!=UpdateStage.Ready; restartItem.Visible=s.Stage==UpdateStage.Ready;
        // The update window opens by itself once per newer version in a run (never for an up-to-date or failed check);
        // the ready balloon remains for a background download that finishes while the window is closed.
        if(updateWindow!=null) {
            updateWindow.ShowStatus(s);
            if(NewerKnown(s)&&s.Stage!=UpdateStage.Error&&s.Latest!=announcedVersion) { announcedVersion=s.Latest; OpenUpdate(); }
        }
        if(s.Stage==UpdateStage.Ready && !readyAnnounced) { readyAnnounced=true; if(updateWindow==null||!updateWindow.IsVisible)Balloon(AppInfo.ProductName+" "+s.Latest+" is ready","Click to restart and update.",true); }
        // The periodic check runs every 5 minutes; each failed check doubles the wait up to 60 minutes, the next check that ends in another stage restores 5 minutes.
        if(updateTimer!=null && (s.Stage==UpdateStage.Error || (updateFailures>0 && (s.Stage==UpdateStage.UpToDate || s.Stage==UpdateStage.Available || s.Stage==UpdateStage.Ready)))) { updateFailures=s.Stage==UpdateStage.Error?updateFailures+1:0; updateTimer.Stop(); updateTimer.Interval=UpdateSchedule.Wait(updateFailures); updateTimer.Start(); }
    }
    void Balloon(string title,string text,bool applies) { if(quitting||!tray.Visible)return; balloonApplies=applies; tray.BalloonTipTitle=title; tray.BalloonTipText=text; tray.ShowBalloonTip(8000); }
    void ApplyUpdate() { if(updater==null||quitting)return; CommitAppearance(); if(updater.Apply())Exit(); }
    void HideSettings() { CommitAppearance(); CloseEditor(); Hide(); }
    void OpenSettings() { Show(); WindowState=WindowState.Normal; Activate(); shell.BeginAnimation(OpacityProperty,null); shell.Opacity=1; }
    internal void Exit() { if(quitting)return; quitting=true; CommitAppearance(); poll.Stop(); if(updateTimer!=null)updateTimer.Stop(); if(Hook!=null)Hook.Dispose(); overlay.Close(); if(updateWindow!=null)updateWindow.Shutdown(); tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); if(trayIcon!=null)trayIcon.Dispose(); Close(); Application.Current.Shutdown(); }
}
}
