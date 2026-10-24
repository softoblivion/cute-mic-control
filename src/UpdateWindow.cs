using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SelfUpdate;
namespace MicControl {
// The update window: draws the presenter's SheetView (a newer release with its notes, the download progress, Install /
// Later) and sends its clicks back; when it opens, what it says and what the buttons do is the presenter's decision
// (docs\SELF_UPDATE.md, sections 8 and 9). A top-level window of its own (it works while the settings window is hidden in
// the tray), fixed size like every window of the app: no resize, no maximize; the height follows the content up to 70 %
// of the work area. It shares the settings window's ThemeLook and control styles, so a theme change recolours both.
class UpdateWindow : Window {
    readonly UpdatePresenter presenter; readonly ThemeLook look; readonly Window anchor; readonly Func<string,bool> openLink;
    Grid shell,root; TextBlock title,meta,progressText,message; StackPanel notes; Border progressBox; ColumnDefinition doneColumn,restColumn; Button later,action;
    List<NoteBlock> rendered; bool closing;
    const double ContentWidth=420;
    internal UpdateWindow(UpdatePresenter presenter,ThemeLook look,ResourceDictionary resources,Window anchor,Func<string,bool> openLink) {
        this.presenter=presenter; this.look=look; this.anchor=anchor; this.openLink=openLink;
        Width=ContentWidth+24; SizeToContent=SizeToContent.Height; MaxHeight=Math.Round(SystemParameters.WorkArea.Height*.7)+24;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent; WindowStartupLocation=WindowStartupLocation.Manual;
        Title=AppInfo.ProductName+" · Update"; Resources=resources; FontFamily=TypeRoles.TextFamily; Foreground=Theme.Text;
        Build();
        PreviewKeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key==Key.Escape) { e.Handled=true; presenter.Later(); } };
        Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e) { if(!closing) { e.Cancel=true; presenter.Later(); } };
    }
    // The app's Scale setting: the content scales as one LayoutTransform, the width follows, the height follows the content.
    internal void SetScale(double factor) { root.LayoutTransform=factor==1?Transform.Identity:new ScaleTransform(factor,factor); Width=ContentWidth*factor+24; }
    void Build() {
        root=new Grid { Margin=new Thickness(12),Width=ContentWidth }; Content=root;
        shell=new Grid { RenderTransform=new TranslateTransform(),Background=Brushes.Transparent }; root.Children.Add(shell);
        shell.Children.Add(new Border { CornerRadius=new CornerRadius(26),BorderThickness=new Thickness(1),BorderBrush=look.FrameEdge,Background=look.Frame,Effect=look.FrameShadow });
        Grid content=new Grid { Margin=new Thickness(1) }; shell.Children.Add(content);
        content.SizeChanged+=delegate { content.Clip=new RectangleGeometry(new Rect(content.RenderSize),25,25); };
        Canvas atmosphere=new Canvas { IsHitTestVisible=false }; content.Children.Add(atmosphere);
        Ellipse nebula=new Ellipse { Width=340,Height=250,Opacity=.36,Fill=look.Nebula }; Canvas.SetLeft(nebula,40); Canvas.SetTop(nebula,-120); atmosphere.Children.Add(nebula);
        Grid layout=new Grid { Margin=new Thickness(22,10,12,18) }; content.Children.Add(layout);
        foreach(GridLength h in new GridLength[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})layout.RowDefinitions.Add(new RowDefinition { Height=h });
        Grid header=new Grid { Background=Brushes.Transparent,Margin=new Thickness(0,0,0,12) }; layout.Children.Add(header);
        header.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.ChangedButton==MouseButton.Left)DragMove(); };
        StackPanel heading=new StackPanel { Margin=new Thickness(0,8,40,0) }; header.Children.Add(heading);
        title=Theme.Label(AppInfo.ProductName,TypeRoles.Display,Theme.Text); title.TextTrimming=TextTrimming.CharacterEllipsis; heading.Children.Add(title);
        meta=Theme.Label("",TypeRoles.Caption,Theme.Muted); meta.TextWrapping=TextWrapping.Wrap; meta.Margin=new Thickness(0,4,0,0); heading.Children.Add(meta);
        Button close=new Button { Style=(Style)Resources["TitleButton"],Content=Icons.Make(Icons.Close,15),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,0,0),ToolTip="Later" };
        System.Windows.Automation.AutomationProperties.SetName(close,"Close"); close.Click+=delegate { presenter.Later(); }; header.Children.Add(close);
        Border sheet=new Border { CornerRadius=new CornerRadius(16),Background=look.Sheet,BorderBrush=Theme.Hairline,BorderThickness=new Thickness(1),Padding=new Thickness(16,10,6,10),Margin=new Thickness(0,0,10,0) }; Grid.SetRow(sheet,1); layout.Children.Add(sheet);
        ScrollViewer scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,12,0) }; sheet.Child=scroll;
        notes=new StackPanel(); scroll.Content=notes; System.Windows.Automation.AutomationProperties.SetName(scroll,UpdateText.ReleaseNotes);
        progressBox=new Border { Visibility=Visibility.Collapsed,Margin=new Thickness(2,16,12,0) }; Grid.SetRow(progressBox,2); layout.Children.Add(progressBox);
        StackPanel progress=new StackPanel(); progressBox.Child=progress;
        Grid track=new Grid { Height=6 }; track.Children.Add(new Border { CornerRadius=new CornerRadius(3),Background=Theme.Brush("#1FFFFFFF") });
        Grid bar=new Grid(); doneColumn=new ColumnDefinition { Width=new GridLength(0,GridUnitType.Star) }; restColumn=new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) }; bar.ColumnDefinitions.Add(doneColumn); bar.ColumnDefinitions.Add(restColumn);
        bar.Children.Add(new Border { CornerRadius=new CornerRadius(3),Background=look.AccentGrad }); track.Children.Add(bar); progress.Children.Add(track);
        progressText=Theme.Label("",TypeRoles.Caption,Theme.Ink2); progressText.Margin=new Thickness(0,7,0,0); progress.Children.Add(progressText);
        message=Theme.Label("",TypeRoles.Caption,Theme.Rose); message.TextWrapping=TextWrapping.Wrap; message.Visibility=Visibility.Collapsed; message.Margin=new Thickness(2,12,12,0); Grid.SetRow(message,3); layout.Children.Add(message);
        StackPanel buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,10,0) }; Grid.SetRow(buttons,4); layout.Children.Add(buttons);
        later=new Button { Content=UpdateText.Later,Height=32,MinWidth=88,Padding=new Thickness(16,0,16,0),HorizontalContentAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,8,0) }; later.Click+=delegate { presenter.Later(); }; buttons.Children.Add(later);
        action=new Button { Content=UpdateText.Install,Height=32,MinWidth=140,Padding=new Thickness(18,0,18,0),HorizontalContentAlignment=HorizontalAlignment.Center,Style=(Style)Resources["Tinted"],Background=look.AccentGrad,Effect=look.AccentGlow };
        ToolTipService.SetShowOnDisabled(action,true);
        action.Click+=delegate { UpdateCommand c=action.Tag is UpdateCommand?(UpdateCommand)action.Tag:UpdateCommand.None; if(c!=UpdateCommand.None)presenter.Run(c); };
        buttons.Children.Add(action);
    }
    // Every presenter change, whether the window is visible or not: open, close or update the sheet.
    internal void Render(SheetView v) {
        if(closing)return;
        if(!v.Open) { if(IsVisible)Hide(); return; }
        title.Text=v.Title; meta.Text=v.Subtitle;
        // The notes list is the same instance while the release is the same: re-render only on a change, so the scroll
        // position survives the progress updates.
        if(!object.ReferenceEquals(rendered,v.Notes)) { rendered=v.Notes; NotesView.Render(notes,v.Notes,v.EmptyNotes,look,openLink); }
        progressBox.Visibility=v.ShowProgress?Visibility.Visible:Visibility.Collapsed;
        if(v.ShowProgress) { int percent=UpdateText.Percent(v.Progress); doneColumn.Width=new GridLength(percent,GridUnitType.Star); restColumn.Width=new GridLength(100-percent,GridUnitType.Star); progressText.Text=v.ProgressText; }
        message.Visibility=v.Error!=null?Visibility.Visible:Visibility.Collapsed; message.Text=v.Error??"";
        later.Content=v.LaterLabel;
        action.Content=v.Action.Label; action.IsEnabled=v.Action.Enabled; action.Tag=v.Action.Command; action.ToolTip=v.Action.Tooltip;
        if(!IsVisible)Open();
    }
    void Open() {
        shell.BeginAnimation(OpacityProperty,null); shell.Opacity=0; Show(); UpdateLayout(); Place();
        Topmost=true; Activate(); Topmost=false;
        TranslateTransform move=(TranslateTransform)shell.RenderTransform; move.Y=14; Theme.Fade(shell,1,.45); move.BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.55));
    }
    // A click on the version pill or the tray item while the sheet is already open: bring it forward.
    internal void Raise() { if(IsVisible&&!closing)Activate(); }
    // Centred over the settings window while it is visible, otherwise on the work area; kept inside the work area.
    void Place() {
        Rect a=SystemParameters.WorkArea; double w=ActualWidth,h=ActualHeight,cx=a.Left+a.Width/2,cy=a.Top+a.Height/2;
        if(anchor!=null&&anchor.IsVisible&&anchor.WindowState==WindowState.Normal) { cx=anchor.Left+anchor.ActualWidth/2; cy=anchor.Top+anchor.ActualHeight/2; }
        Left=Math.Max(a.Left,Math.Min(a.Right-w,cx-w/2)); Top=Math.Max(a.Top,Math.Min(a.Bottom-h,cy-h/2));
    }
    internal void Shutdown() { closing=true; Close(); }
}
// Release notes (the library's NoteBlocks) as WPF text in the app's type roles and colours. Only https targets arrive as
// links (the library drops the others); a click opens the target in the browser through the host.
static class NotesView {
    internal static void Render(Panel target,List<NoteBlock> blocks,string empty,ThemeLook look,Func<string,bool> openLink) {
        target.Children.Clear();
        if(blocks==null||blocks.Count==0) { target.Children.Add(Plain(empty,TypeRoles.Body,Theme.Muted)); return; }
        foreach(NoteBlock b in blocks) {
            switch(b.Kind) {
                case NoteBlockKind.Heading: target.Children.Add(Spaced(Rich(b,b.Level<=2?TypeRoles.Title:TypeRoles.Label,Theme.Text,look,openLink),10,4)); break;
                case NoteBlockKind.Paragraph: target.Children.Add(Spaced(Rich(b,TypeRoles.Body,Theme.Ink2,look,openLink),4,6)); break;
                case NoteBlockKind.Bullet: case NoteBlockKind.Numbered: target.Children.Add(Item(b,look,openLink)); break;
                case NoteBlockKind.Quote: target.Children.Add(new Border { BorderThickness=new Thickness(2,0,0,0),BorderBrush=Theme.Hairline,Padding=new Thickness(10,1,0,1),Margin=new Thickness(0,3,0,3),Child=Rich(b,TypeRoles.Body,Theme.Muted,look,openLink) }); break;
                case NoteBlockKind.Code: target.Children.Add(Code(b.Text)); break;
                case NoteBlockKind.Rule: target.Children.Add(new Border { Height=1,Background=Theme.Brush("#1FFFFFFF"),Margin=new Thickness(0,8,0,8) }); break;
            }
        }
        if(target.Children.Count>0) { FrameworkElement first=(FrameworkElement)target.Children[0]; Thickness t=first.Margin; first.Margin=new Thickness(t.Left,0,t.Right,t.Bottom); }
    }
    static FrameworkElement Spaced(FrameworkElement e,double top,double bottom) { e.Margin=new Thickness(0,top,0,bottom); return e; }
    static TextBlock Plain(string text,TypeRole role,Brush brush) {
        TextBlock b=new TextBlock { Text=text,Foreground=brush,TextWrapping=TextWrapping.Wrap }; role.Apply(b); return b;
    }
    static TextBlock Rich(NoteBlock note,TypeRole role,Brush brush,ThemeLook look,Func<string,bool> openLink) {
        TextBlock b=new TextBlock { Foreground=brush,TextWrapping=TextWrapping.Wrap }; role.Apply(b);
        foreach(NoteSpan s in note.Spans)b.Inlines.Add(Span(s,look,openLink));
        return b;
    }
    static Grid Item(NoteBlock note,ThemeLook look,Func<string,bool> openLink) {
        Grid row=new Grid { Margin=new Thickness(2+note.Indent*14,2,0,2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto,MinWidth=16 }); row.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock dot=Plain(note.Marker,TypeRoles.Body,look.AccentInk); dot.Margin=new Thickness(0,0,6,0); row.Children.Add(dot);
        TextBlock body=Rich(note,TypeRoles.Body,Theme.Ink2,look,openLink); Grid.SetColumn(body,1); row.Children.Add(body); return row;
    }
    static Border Code(string text) {
        TextBlock t=Plain(text,TypeRoles.Mono,Theme.Ink2);
        return new Border { CornerRadius=new CornerRadius(8),Background=Theme.Brush("#14FFFFFF"),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,4,0,6),Child=t };
    }
    static Inline Span(NoteSpan s,ThemeLook look,Func<string,bool> openLink) {
        Run run=new Run(s.Text); Inline inline=run;
        if(s.Code) { run.FontFamily=TypeRoles.MonoFamily; run.Background=Theme.Brush("#1FFFFFFF"); run.Foreground=Theme.Text; }
        if(s.Url!=null) {
            string url=s.Url; Hyperlink link=new Hyperlink(inline) { Foreground=look.AccentInk,Cursor=Cursors.Hand,ToolTip=url };
            link.Click+=delegate { if(openLink!=null)openLink(url); }; inline=link;
        }
        if(s.Bold)inline=new Bold(inline) { Foreground=Theme.Text };
        if(s.Italic)inline=new Italic(inline);
        return inline;
    }
}
}
