using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace MicControl {
// The update window: a newer release with its notes, the download progress and Install / Later. Behaviour contract:
// docs\SELF_UPDATE.md, section 8. A top-level window of its own (it works while the settings window is hidden in the
// tray), fixed size like every window of the app: no resize, no maximize; the height follows the content up to 70 %
// of the work area. It shares the settings window's ThemeLook and control styles, so a theme change recolours both.
class UpdateWindow : Window {
    readonly Updater updater; readonly ThemeLook look; readonly Window anchor; readonly Action apply;
    Grid shell; TextBlock title,meta,progressText,message; StackPanel notes; Border progressBox; ColumnDefinition doneColumn,restColumn; Button install;
    UpdateStatus last; string rendered; bool armed,closing; readonly DispatcherTimer retry=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(400) };
    internal UpdateWindow(Updater updater,ThemeLook look,ResourceDictionary resources,Window anchor,Action apply) {
        this.updater=updater; this.look=look; this.anchor=anchor; this.apply=apply;
        Width=484; SizeToContent=SizeToContent.Height; MaxHeight=Math.Round(SystemParameters.WorkArea.Height*.7)+24;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent; WindowStartupLocation=WindowStartupLocation.Manual;
        Title=AppInfo.ProductName+" · Update"; FontFamily=new FontFamily("Bahnschrift"); Foreground=Theme.Text; Resources=resources;
        retry.Tick+=delegate { retry.Stop(); StartDownload(); };
        Build();
        PreviewKeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key==Key.Escape) { e.Handled=true; Later(); } };
        Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e) { if(!closing) { e.Cancel=true; Later(); } };
    }
    void Build() {
        Grid root=new Grid { Margin=new Thickness(12) }; Content=root;
        shell=new Grid { RenderTransform=new TranslateTransform(),Background=Brushes.Transparent }; root.Children.Add(shell);
        shell.Children.Add(new Border { CornerRadius=new CornerRadius(26),BorderThickness=new Thickness(1),BorderBrush=look.FrameEdge,Background=look.Frame,Effect=look.FrameShadow });
        Grid content=new Grid { Margin=new Thickness(1) }; shell.Children.Add(content);
        content.SizeChanged+=delegate { content.Clip=new RectangleGeometry(new Rect(content.RenderSize),25,25); };
        Canvas atmosphere=new Canvas { IsHitTestVisible=false }; content.Children.Add(atmosphere);
        Ellipse nebula=new Ellipse { Width=340,Height=250,Opacity=.36,Fill=look.Nebula }; Canvas.SetLeft(nebula,40); Canvas.SetTop(nebula,-120); atmosphere.Children.Add(nebula);
        Grid layout=new Grid { Margin=new Thickness(28,16,18,22) }; content.Children.Add(layout);
        foreach(GridLength h in new GridLength[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})layout.RowDefinitions.Add(new RowDefinition { Height=h });
        Grid header=new Grid { Background=Brushes.Transparent,Margin=new Thickness(0,0,0,14) }; layout.Children.Add(header);
        header.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.ChangedButton==MouseButton.Left)DragMove(); };
        StackPanel heading=new StackPanel { Margin=new Thickness(0,8,40,0) }; header.Children.Add(heading);
        title=Theme.Label(AppInfo.ProductName,18,Theme.Text); title.TextTrimming=TextTrimming.CharacterEllipsis; heading.Children.Add(title);
        meta=Theme.Label("",11,Theme.Muted); meta.TextWrapping=TextWrapping.Wrap; meta.Margin=new Thickness(0,5,0,0); heading.Children.Add(meta);
        Button close=new Button { Content="×",Width=30,Height=30,Padding=new Thickness(0,0,0,2),HorizontalContentAlignment=HorizontalAlignment.Center,Style=(Style)Resources["Ghost"],FontSize=19,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,ToolTip="Later" };
        System.Windows.Automation.AutomationProperties.SetName(close,"Close"); close.Click+=delegate { Later(); }; header.Children.Add(close);
        Border sheet=new Border { CornerRadius=new CornerRadius(18),Background=look.Sheet,BorderBrush=Theme.Hairline,BorderThickness=new Thickness(1),Padding=new Thickness(18,12,6,12),Margin=new Thickness(0,0,10,0) }; Grid.SetRow(sheet,1); layout.Children.Add(sheet);
        ScrollViewer scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,12,0) }; sheet.Child=scroll;
        notes=new StackPanel(); scroll.Content=notes; System.Windows.Automation.AutomationProperties.SetName(scroll,"Release notes");
        progressBox=new Border { Visibility=Visibility.Collapsed,Margin=new Thickness(2,16,12,0) }; Grid.SetRow(progressBox,2); layout.Children.Add(progressBox);
        StackPanel progress=new StackPanel(); progressBox.Child=progress;
        Grid track=new Grid { Height=6 }; track.Children.Add(new Border { CornerRadius=new CornerRadius(3),Background=Theme.Brush("#1FFFFFFF") });
        Grid bar=new Grid(); doneColumn=new ColumnDefinition { Width=new GridLength(0,GridUnitType.Star) }; restColumn=new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }; bar.ColumnDefinitions.Add(doneColumn); bar.ColumnDefinitions.Add(restColumn);
        bar.Children.Add(new Border { CornerRadius=new CornerRadius(3),Background=look.AccentGrad }); track.Children.Add(bar); progress.Children.Add(track);
        progressText=Theme.Label("",11,Theme.Ink2); progressText.Margin=new Thickness(0,8,0,0); progress.Children.Add(progressText);
        message=Theme.Label("",11,Theme.Rose); message.TextWrapping=TextWrapping.Wrap; message.Visibility=Visibility.Collapsed; message.Margin=new Thickness(2,14,12,0); Grid.SetRow(message,3); layout.Children.Add(message);
        StackPanel buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,10,0) }; Grid.SetRow(buttons,4); layout.Children.Add(buttons);
        Button later=new Button { Content="Later",Height=40,MinWidth=104,Padding=new Thickness(18,0,18,0),HorizontalContentAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,10,0) }; later.Click+=delegate { Later(); }; buttons.Children.Add(later);
        install=new Button { Content="Install",Height=40,MinWidth=160,Padding=new Thickness(20,0,20,0),HorizontalContentAlignment=HorizontalAlignment.Center,Style=(Style)Resources["Tinted"],Background=look.AccentGrad,Effect=look.AccentGlow };
        ToolTipService.SetShowOnDisabled(install,true); install.Click+=delegate { Install(); }; buttons.Children.Add(install);
    }
    internal bool HasRelease { get { return last!=null; } }
    // Every updater status passes through here (on the dispatcher), whether the window is visible or not.
    internal void ShowStatus(UpdateStatus s) {
        bool newer=s.Latest!=null&&SemVer.IsNewer(s.Latest,s.Current);
        if(s.Stage==UpdateStage.UpToDate) { armed=false; retry.Stop(); last=null; if(IsVisible)Hide(); return; }
        if(newer)last=s; if(last==null)return;
        UpdateStatus info=newer?s:last;
        title.Text=AppInfo.ProductName+" "+info.Latest; meta.Text=Meta(info);
        string key=info.Latest+"\n"+info.Notes; if(rendered!=key) { rendered=key; MarkdownView.Render(notes,info.Notes,look); }
        bool downloading=s.Stage==UpdateStage.Downloading; int percent=(int)Math.Floor(Math.Max(0,Math.Min(1,s.Progress))*100);
        progressBox.Visibility=downloading?Visibility.Visible:Visibility.Collapsed;
        if(downloading) { doneColumn.Width=new GridLength(percent,GridUnitType.Star); restColumn.Width=new GridLength(100-percent,GridUnitType.Star); progressText.Text=ProgressText(s,percent); }
        message.Visibility=s.Stage==UpdateStage.Error?Visibility.Visible:Visibility.Collapsed; message.Text=s.Stage==UpdateStage.Error?s.Message:"";
        install.ToolTip=null;
        switch(s.Stage) {
            case UpdateStage.Available: install.Content="Install"; install.IsEnabled=true; if(armed)StartDownload(); break;
            case UpdateStage.Downloading: install.Content="Downloading… "+percent+"%"; install.IsEnabled=false; break;
            case UpdateStage.Ready:
                if(armed&&s.CanApply) { armed=false; install.Content="Restarting…"; install.IsEnabled=false; Dispatcher.BeginInvoke((Action)delegate { apply(); }); break; }
                armed=false; install.Content="Restart to update"; install.IsEnabled=s.CanApply;
                if(!s.CanApply)install.ToolTip="This copy cannot replace its own file. Download the release from GitHub.";
                break;
            case UpdateStage.Error: install.Content="Try again"; install.IsEnabled=true; break;
            default: install.Content="Checking…"; install.IsEnabled=false; break;
        }
    }
    void Install() {
        UpdateStatus s=updater.Status;
        if(s.Stage==UpdateStage.Ready) { apply(); return; }
        armed=true;
        if(s.Stage==UpdateStage.Error&&s.Latest==null)updater.CheckInBackground(); else StartDownload();
    }
    // Starts the download of an armed install; while the updater still finishes a check, retries shortly (an
    // automatic download that starts meanwhile makes the retry a no-op, and its Ready applies the armed install).
    void StartDownload() {
        if(!armed)return; UpdateStatus s=updater.Status;
        if(s.Stage!=UpdateStage.Available&&!(s.Stage==UpdateStage.Error&&s.Latest!=null))return;
        if(updater.Busy) { retry.Stop(); retry.Start(); return; }
        updater.DownloadInBackground();
    }
    internal void Later() { armed=false; retry.Stop(); if(IsVisible)Hide(); }
    internal void Open() {
        if(last==null)return;
        if(IsVisible) { Activate(); return; }
        shell.BeginAnimation(OpacityProperty,null); shell.Opacity=0; Show(); UpdateLayout(); Place();
        Topmost=true; Activate(); Topmost=false;
        TranslateTransform move=(TranslateTransform)shell.RenderTransform; move.Y=14; Theme.Fade(shell,1,.45); move.BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.55));
    }
    // Centred over the settings window while it is visible, otherwise on the work area; kept inside the work area.
    void Place() {
        Rect a=SystemParameters.WorkArea; double w=ActualWidth,h=ActualHeight,cx=a.Left+a.Width/2,cy=a.Top+a.Height/2;
        if(anchor!=null&&anchor.IsVisible&&anchor.WindowState==WindowState.Normal) { cx=anchor.Left+anchor.ActualWidth/2; cy=anchor.Top+anchor.ActualHeight/2; }
        Left=Math.Max(a.Left,Math.Min(a.Right-w,cx-w/2)); Top=Math.Max(a.Top,Math.Min(a.Bottom-h,cy-h/2));
    }
    internal void Shutdown() { closing=true; retry.Stop(); Close(); }
    static readonly CultureInfo English=CultureInfo.GetCultureInfo("en-US");
    internal static string Meta(UpdateStatus s) {
        List<string> parts=new List<string>(); parts.Add("Installed v"+s.Current);
        if(s.Size>=0)parts.Add(Bytes(s.Size));
        if(s.PublishedAt>=0)parts.Add(new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(s.PublishedAt).ToLocalTime().ToString("MMM d, yyyy",English));
        return string.Join(" · ",parts.ToArray());
    }
    internal static string Bytes(long n) { return n<1048576?Math.Round(n/1024.0).ToString("0",English)+" KB":(n/1048576.0).ToString("0.0",English)+" MB"; }
    static string ProgressText(UpdateStatus s,int percent) { return s.Size>0?percent+"% · "+Bytes((long)(Math.Min(1,s.Progress)*s.Size))+" of "+Bytes(s.Size):percent+"%"; }
}
// Release notes from Markdown into WPF text: headings (bold lines), "-" / "*" / "+" bullets and numbered items with
// their indent, quotes, fenced code, rules and paragraphs; inline **bold**, *italic*, `code`, [text](url) and bare
// http(s) links (opened in the browser on click). Anything else, HTML included, stays plain text.
static class MarkdownView {
    static readonly Regex Heading=new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)(?:\s+#+)?\s*$"),Rule=new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$"),
        Bullet=new Regex(@"^(\s*)[-*+]\s+(.*)$"),Numbered=new Regex(@"^(\s*)(\d{1,3})[.)]\s+(.*)$"),Quote=new Regex(@"^\s{0,3}>\s?(.*)$"),
        Token=new Regex(@"\*\*(.+?)\*\*|__(.+?)__|`([^`]+)`|\[([^\]]+)\]\(\s*([^)\s]+)[^)]*\)|(https?://[^\s<>()]*[^\s<>().,;:!?'""])|(?<![\w*])\*(?![\s*])([^*]+?)\*(?![\w*])|\\([\\`*_{}\[\]()#+\-.!>])");
    internal static void Render(Panel target,string text,ThemeLook look) {
        target.Children.Clear();
        if(string.IsNullOrWhiteSpace(text)) { target.Children.Add(Block("No release notes for this version.",12.5,Theme.Muted,look,false)); return; }
        StringBuilder paragraph=new StringBuilder(); StringBuilder code=null;
        Action flush=delegate { if(paragraph.Length==0)return; target.Children.Add(Spaced(Block(paragraph.ToString(),12.5,Theme.Ink2,look,true),4,6)); paragraph.Length=0; };
        foreach(string raw in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
            string line=raw.TrimEnd(); Match m;
            if(code!=null) { if(line.TrimStart().StartsWith("```")) { target.Children.Add(Code(code.ToString().TrimEnd('\n'))); code=null; } else code.Append(raw).Append('\n'); continue; }
            if(line.TrimStart().StartsWith("```")) { flush(); code=new StringBuilder(); continue; }
            if(line.Trim().Length==0) { flush(); continue; }
            if((m=Heading.Match(line)).Success) { flush(); TextBlock h=Block(m.Groups[2].Value,m.Groups[1].Length<=2?14.5:13,Theme.Text,look,true); h.FontWeight=FontWeights.SemiBold; target.Children.Add(Spaced(h,10,4)); continue; }
            if(Rule.IsMatch(line)) { flush(); target.Children.Add(new Border { Height=1,Background=Theme.Brush("#1FFFFFFF"),Margin=new Thickness(0,8,0,8) }); continue; }
            if((m=Bullet.Match(line)).Success) { flush(); target.Children.Add(Item("•",m.Groups[2].Value,m.Groups[1].Value.Length,look)); continue; }
            if((m=Numbered.Match(line)).Success) { flush(); target.Children.Add(Item(m.Groups[2].Value+".",m.Groups[3].Value,m.Groups[1].Value.Length,look)); continue; }
            if((m=Quote.Match(line)).Success) { flush(); target.Children.Add(new Border { BorderThickness=new Thickness(2,0,0,0),BorderBrush=Theme.Hairline,Padding=new Thickness(10,1,0,1),Margin=new Thickness(0,3,0,3),Child=Block(m.Groups[1].Value,12.5,Theme.Muted,look,true) }); continue; }
            if(paragraph.Length>0)paragraph.Append(' '); paragraph.Append(line.Trim());
        }
        if(code!=null)target.Children.Add(Code(code.ToString().TrimEnd('\n'))); flush();
        if(target.Children.Count>0) { FrameworkElement first=(FrameworkElement)target.Children[0]; Thickness t=first.Margin; first.Margin=new Thickness(t.Left,0,t.Right,t.Bottom); }
    }
    static FrameworkElement Spaced(FrameworkElement e,double top,double bottom) { e.Margin=new Thickness(0,top,0,bottom); return e; }
    static TextBlock Block(string text,double size,Brush brush,ThemeLook look,bool inline) {
        TextBlock b=new TextBlock { FontFamily=new FontFamily("Bahnschrift"),FontSize=size,Foreground=brush,TextWrapping=TextWrapping.Wrap,LineHeight=Math.Round(size*1.45) };
        if(inline)Inlines(b.Inlines,text,look); else b.Text=text; return b;
    }
    static Grid Item(string marker,string text,int indent,ThemeLook look) {
        Grid row=new Grid { Margin=new Thickness(2+Math.Min(indent,8)/2*14,2,0,2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto,MinWidth=16 }); row.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock dot=Block(marker,12.5,look.AccentInk,look,false); dot.Margin=new Thickness(0,0,6,0); row.Children.Add(dot);
        TextBlock body=Block(text,12.5,Theme.Ink2,look,true); Grid.SetColumn(body,1); row.Children.Add(body); return row;
    }
    static Border Code(string text) {
        return new Border { CornerRadius=new CornerRadius(8),Background=Theme.Brush("#14FFFFFF"),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,4,0,6),
            Child=new TextBlock { Text=text,FontFamily=new FontFamily("Consolas"),FontSize=11.5,Foreground=Theme.Ink2,TextWrapping=TextWrapping.Wrap } };
    }
    static void Inlines(InlineCollection into,string text,ThemeLook look) {
        int at=0;
        foreach(Match m in Token.Matches(text)) {
            if(m.Index>at)into.Add(new Run(text.Substring(at,m.Index-at)));
            GroupCollection g=m.Groups;
            if(g[1].Success||g[2].Success) { Bold b=new Bold { Foreground=Theme.Text }; Inlines(b.Inlines,g[1].Success?g[1].Value:g[2].Value,look); into.Add(b); }
            else if(g[3].Success)into.Add(new Run(g[3].Value) { FontFamily=new FontFamily("Consolas"),Background=Theme.Brush("#1FFFFFFF"),Foreground=Theme.Text });
            else if(g[4].Success)into.Add(Link(g[4].Value,g[5].Value,look));
            else if(g[6].Success)into.Add(Link(g[6].Value,g[6].Value,look));
            else if(g[7].Success) { Italic i=new Italic(); Inlines(i.Inlines,g[7].Value,look); into.Add(i); }
            else into.Add(new Run(g[8].Value));
            at=m.Index+m.Length;
        }
        if(at<text.Length)into.Add(new Run(text.Substring(at)));
    }
    // Only http(s) targets become links; anything else (file:, javascript:, relative paths) stays its plain text.
    static Inline Link(string text,string url,ThemeLook look) {
        Uri uri; if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||(uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps))return new Run(text);
        Hyperlink link=new Hyperlink(new Run(text)) { NavigateUri=uri,Foreground=look.AccentInk,Cursor=Cursors.Hand,ToolTip=uri.AbsoluteUri };
        link.Click+=delegate { try { using(Process p=Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute=true })) { } } catch { } };
        return link;
    }
}
}
