using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SelfUpdate;
namespace MicControl {
// A damped spring as an easing function: the kit's "bouncy" spring (response 0.42 s, damping 0.62) over a 0.6 s
// animation; it overshoots once and settles. Used with EasingMode.EaseIn, so EaseInCore is the curve itself.
class SpringEase : EasingFunctionBase {
    const double Response=.42,Damping=.62;
    internal const double Seconds=.6;
    internal SpringEase() { EasingMode=EasingMode.EaseIn; }
    protected override double EaseInCore(double t) {
        if(t>=1)return 1; double s=t*Seconds,w=2*Math.PI/Response,wd=w*Math.Sqrt(1-Damping*Damping);
        return 1-Math.Exp(-Damping*w*s)*(Math.Cos(wd*s)+Damping*w/wd*Math.Sin(wd*s));
    }
    protected override Freezable CreateInstanceCore() { return new SpringEase(); }
    internal static DoubleAnimation To(double value) { return new DoubleAnimation(value,TimeSpan.FromSeconds(Seconds)) { EasingFunction=new SpringEase() }; }
}
// Line icons on the kit's 24 x 24 grid (1.8 px stroke, round caps), drawn in the button's foreground.
static class Icons {
    internal const string Gear="M12 8.6a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8Z M19.4 13.5a7.6 7.6 0 0 0 0-3l1.8-1.4-1.8-3.1-2.2.8a7.5 7.5 0 0 0-2.6-1.5L14.2 3h-3.6l-.4 2.3a7.5 7.5 0 0 0-2.6 1.5l-2.2-.8-1.8 3.1 1.8 1.4a7.6 7.6 0 0 0 0 3l-1.8 1.4 1.8 3.1 2.2-.8a7.5 7.5 0 0 0 2.6 1.5l.4 2.3h3.6l.4-2.3a7.5 7.5 0 0 0 2.6-1.5l2.2.8 1.8-3.1Z",
        Minimize="M6 12h12",Close="M7 7l10 10M17 7 7 17";
    internal static FrameworkElement Make(string data,double size) {
        Path p=new Path { Data=Geometry.Parse(data),StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=24,Height=24,Stretch=Stretch.None };
        p.SetBinding(Shape.StrokeProperty,new Binding("Foreground") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Control),1) });
        return new Viewbox { Width=size,Height=size,Child=new Canvas { Width=24,Height=24,Children={ p } },RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform() };
    }
}
// The segmented control of the kit: equal options on a sunken pill track, the selected one under a raised glass
// thumb that travels with the bouncy spring. Options that cannot be chosen are dimmed (kit "is-off"). Arrow keys move
// the choice while the control has the focus.
class Segmented : Grid {
    internal event Action<int> Changed; // a choice made by the user (click or key)
    internal readonly List<Button> Options=new List<Button>();
    readonly UniformGrid row; readonly Border thumb; readonly TranslateTransform slide=new TranslateTransform(); bool[] off; int index=-1;
    internal Segmented(ResourceDictionary resources,string name,string[] labels,TypeRole role,double height) {
        Focusable=true; FocusVisualStyle=null; Height=height+6; System.Windows.Automation.AutomationProperties.SetName(this,name);
        Children.Add(new Border { CornerRadius=new CornerRadius((height+6)/2),Background=Theme.Brush("#42000000"),BorderBrush=Theme.Brush("#0FFFFFFF"),BorderThickness=new Thickness(1) });
        Canvas layer=new Canvas { Margin=new Thickness(3),IsHitTestVisible=false,ClipToBounds=false }; Children.Add(layer);
        thumb=new Border { Height=height,CornerRadius=new CornerRadius(height/2),Background=Theme.Brush("#29FFFFFF"),BorderBrush=Theme.Brush("#3DFFFFFF"),BorderThickness=new Thickness(1),RenderTransform=slide,Opacity=0,
            Effect=new System.Windows.Media.Effects.DropShadowEffect { Color=Colors.Black,BlurRadius=10,ShadowDepth=3,Direction=270,Opacity=.3 } };
        layer.Children.Add(thumb);
        row=new UniformGrid { Rows=1,Columns=labels.Length,Margin=new Thickness(3) }; Children.Add(row); off=new bool[labels.Length];
        for(int i=0;i<labels.Length;i++) {
            int at=i; TextBlock text=Theme.Label(labels[i],role,null); text.ClearValue(TextBlock.ForegroundProperty);
            Button b=new Button { Style=(Style)resources["Segment"],Height=height,Content=text };
            System.Windows.Automation.AutomationProperties.SetName(b,labels[i]);
            b.Click+=delegate { Choose(at); }; row.Children.Add(b); Options.Add(b);
        }
        SizeChanged+=delegate { Place(false); };
        PreviewKeyDown+=delegate(object s,KeyEventArgs e) {
            int step=e.Key==Key.Right||e.Key==Key.Down?1:e.Key==Key.Left||e.Key==Key.Up?-1:0; if(step==0)return; e.Handled=true;
            for(int i=index+step;i>=0&&i<Options.Count;i+=step)if(!off[i]) { Choose(i); break; }
        };
        MouseLeftButtonDown+=delegate { Focus(); };
    }
    internal int Index { get { return index; } }
    internal double ThumbX { get { return slide.X; } } // the animated position, for the self-test
    internal void Select(int i,bool animate) { if(i<0||i>=Options.Count)return; index=i; Restyle(); Place(animate); }
    internal void SetOff(int i,bool value,string reason) { off[i]=value; Options[i].IsEnabled=!value; Options[i].Opacity=value?.34:1; ToolTipService.SetShowOnDisabled(Options[i],true); Options[i].ToolTip=value?reason:null; }
    internal bool IsOff(int i) { return off[i]; }
    void Choose(int i) { if(off[i]||i==index)return; Select(i,true); Action<int> handler=Changed; if(handler!=null)handler(i); }
    void Restyle() { for(int i=0;i<Options.Count;i++) { if(i==index)Options[i].Foreground=Theme.Text; else Options[i].ClearValue(Control.ForegroundProperty); } }
    void Place(bool animate) {
        if(index<0||row.ActualWidth<=0)return; double w=row.ActualWidth/Options.Count,x=w*index;
        if(animate&&thumb.Opacity>0) { slide.BeginAnimation(TranslateTransform.XProperty,SpringEase.To(x)); thumb.BeginAnimation(WidthProperty,SpringEase.To(w)); }
        else { slide.BeginAnimation(TranslateTransform.XProperty,null); thumb.BeginAnimation(WidthProperty,null); slide.X=x; thumb.Width=w; thumb.Opacity=1; }
    }
}
// The standard title bar of the softoblivion apps: app glyph, name and version pill on the left; one capsule with
// settings | minimize, close on the right; the empty band between them drags the window. 52 px high.
class TitleBar : Grid {
    internal readonly Button Pill,Gear,Minimize,Close; readonly Ellipse dot; readonly TextBlock version;
    internal TitleBar(ThemeLook look,ResourceDictionary resources,string name,string versionText) {
        Height=52; Background=Brushes.Transparent; Margin=new Thickness(12,0,10,0);
        ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        StackPanel left=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center }; Children.Add(left);
        Path glyph=new Path { Data=Geometry.Parse("M 10,18.2 C 8.6,16.8 1.6,12.6 1.6,7.2 C 1.6,2.6 7.2,2 10,5.8 C 12.8,2 18.4,2.6 18.4,7.2 C 18.4,12.6 11.4,16.8 10,18.2 Z"),Width=20,Height=20,Fill=look.AccentGrad,Effect=look.GlyphGlow,IsHitTestVisible=false,Stretch=Stretch.None,LayoutTransform=new ScaleTransform(1.2,1.2) }; // drawn at 24 px
        left.Children.Add(glyph);
        TextBlock title=Theme.Label(name,TypeRoles.Title,Theme.Text); title.Margin=new Thickness(9,0,0,0); title.IsHitTestVisible=false; left.Children.Add(title);
        StackPanel pillContent=new StackPanel { Orientation=Orientation.Horizontal };
        dot=new Ellipse { Width=6,Height=6,Fill=look.Accent,Margin=new Thickness(0,0,5,0),Visibility=Visibility.Collapsed,VerticalAlignment=VerticalAlignment.Center }; pillContent.Children.Add(dot);
        version=new TextBlock { Text=versionText,VerticalAlignment=VerticalAlignment.Center }; TypeRoles.MonoS.Apply(version); pillContent.Children.Add(version);
        Pill=new Button { Style=(Style)resources["Pill"],Content=pillContent,Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center,ToolTip="Check for updates" };
        System.Windows.Automation.AutomationProperties.SetName(Pill,"Version "+versionText); left.Children.Add(Pill);
        Grid cluster=new Grid { Height=38,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(cluster,1); Children.Add(cluster);
        cluster.Children.Add(new Border { CornerRadius=new CornerRadius(19),Background=Theme.Brush("#09FFFFFF"),BorderBrush=Theme.Brush("#1FFFFFFF"),BorderThickness=new Thickness(1),
            Effect=new System.Windows.Media.Effects.DropShadowEffect { Color=Colors.Black,BlurRadius=10,ShadowDepth=2,Direction=270,Opacity=.28 } });
        StackPanel buttons=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(3) }; cluster.Children.Add(buttons);
        Gear=Make(resources,"TitleButton",Icons.Gear,19,"Settings"); buttons.Children.Add(Gear);
        buttons.Children.Add(new Border { Width=1,Height=17,Margin=new Thickness(3,0,3,0),Background=Theme.Brush("#24FFFFFF"),VerticalAlignment=VerticalAlignment.Center });
        Minimize=Make(resources,"TitleButton",Icons.Minimize,17,"Minimize"); Minimize.Margin=new Thickness(0,0,2,0); buttons.Children.Add(Minimize);
        Close=Make(resources,"CloseButton",Icons.Close,17,"Close"); buttons.Children.Add(Close);
        // The gear turns by 60 degrees on hover and while the settings sheet is open, with the bouncy spring.
        Gear.MouseEnter+=delegate { TurnGear(); }; Gear.MouseLeave+=delegate { TurnGear(); };
    }
    static Button Make(ResourceDictionary resources,string style,string icon,double size,string name) {
        Button b=new Button { Style=(Style)resources[style],Content=Icons.Make(icon,size),ToolTip=name };
        System.Windows.Automation.AutomationProperties.SetName(b,name); return b;
    }
    internal void SetSettingsOpen(bool open) { Gear.Tag=open?"on":null; TurnGear(); }
    void TurnGear() { RotateTransform r=(RotateTransform)((FrameworkElement)Gear.Content).RenderTransform; r.BeginAnimation(RotateTransform.AngleProperty,SpringEase.To(Gear.IsMouseOver||(string)Gear.Tag=="on"?60:0)); }
    // The pill shows the presenter's PillView: a dot while a newer version is known (accent while offered, pulsing accent
    // while downloading, mint when ready), the version text and the tooltip.
    internal void ShowUpdate(PillView v,ThemeLook look) {
        bool newer=v.State!=PillState.Idle;
        dot.Visibility=newer?Visibility.Visible:Visibility.Collapsed;
        dot.Fill=v.State==PillState.Ready?Theme.Mint:(Brush)look.Accent;
        if(newer)Pill.Foreground=look.AccentInk; else Pill.ClearValue(Control.ForegroundProperty);
        version.Text=v.Text; Pill.ToolTip=v.Tooltip; System.Windows.Automation.AutomationProperties.SetName(Pill,"Version "+v.Text);
        if(v.State==PillState.Downloading) { if(dot.Tag==null) { dot.Tag="pulse"; dot.BeginAnimation(OpacityProperty,new DoubleAnimation(1,.35,TimeSpan.FromSeconds(.7)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase() }); } }
        else { dot.Tag=null; dot.BeginAnimation(OpacityProperty,null); dot.Opacity=1; }
    }
}
}
