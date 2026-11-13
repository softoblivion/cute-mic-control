using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
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
// Stroke glyphs of the audio console, drawn at their own box size (no Viewbox), and the helpers the console controls share.
static class Glyph {
    internal const string MicStand="M5.5 11.5a6.5 6.5 0 0 0 13 0M12 18v3",SpeakerBody="M4 9.5v5h3.2L12 18.5v-13L7.2 9.5z",Waves="M15.5 9a4.2 4.2 0 0 1 0 6M18.2 6.5a7.8 7.8 0 0 1 0 11",
        Cross="M16 9.5l5 5M21 9.5l-5 5",Shackle="M5.4 7.2V5.1a2.6 2.6 0 0 1 5.2 0v2.1",Chevron="M3.5 5.5L7 9l3.5-3.5",Arrow="M4 9l5-5M5 4h4v4";
    internal static Path Stroke(Geometry data,Brush brush,double thickness) { return new Path { Data=data,Stroke=brush,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,IsHitTestVisible=false }; }
    internal static Path Stroke(string data,Brush brush,double thickness) { return Stroke(Geometry.Parse(data),brush,thickness); }
    internal static Canvas Box(double size,params UIElement[] shapes) { Canvas c=new Canvas { Width=size,Height=size,IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center }; foreach(UIElement s in shapes)c.Children.Add(s); return c; }
    static ControlTemplate bare;
    // A button template that draws only its content: the console controls draw every state themselves.
    internal static ControlTemplate Bare { get { if(bare==null)bare=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>"); return bare; } }
    internal static void Dim(Control c) { c.IsEnabledChanged+=delegate { c.Opacity=c.IsEnabled?1:.38; }; }
    internal static SolidColorBrush Bound(SolidColorBrush source,double opacity) { SolidColorBrush b=new SolidColorBrush { Opacity=opacity }; BindingOperations.SetBinding(b,SolidColorBrush.ColorProperty,new Binding("Color") { Source=source }); return b; }
    internal static DoubleAnimation Ease(double to,double seconds) { return Theme.Motion(to,seconds); }
    internal static void Fade(UIElement e,double to,double seconds) { if(seconds>0)Theme.Fade(e,to,seconds); else { e.BeginAnimation(UIElement.OpacityProperty,null); e.Opacity=to; } }
    internal static void Tint(SolidColorBrush b,Color to,double seconds) { if(seconds>0)b.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(to,TimeSpan.FromSeconds(seconds))); else { b.BeginAnimation(SolidColorBrush.ColorProperty,null); b.Color=to; } }
    internal static Color Hex(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    // A CSS-like outer shadow: drawn outside the shape only, so it shows under a translucent fill as it does on the web.
    // An opaque copy of the shape (1 px inside the edge, so no dark fringe) casts the shadow; a clip removes the copy.
    internal static Grid OuterShadow(double radius,bool round,DropShadowEffect effect) {
        Grid host=new Grid { IsHitTestVisible=false };
        FrameworkElement body=round?(FrameworkElement)new Ellipse { Fill=Brushes.Black,Margin=new Thickness(1) }:new Border { Background=Brushes.Black,CornerRadius=new CornerRadius(Math.Max(0,radius-1)),Margin=new Thickness(1) };
        body.Effect=effect; host.Children.Add(body);
        host.SizeChanged+=delegate { Rect r=new Rect(host.RenderSize); Geometry inner=round?(Geometry)new EllipseGeometry(r):new RectangleGeometry(r,radius,radius); host.Clip=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(-80,-80,r.Width+160,r.Height+160)),inner); };
        return host;
    }
}
enum MuteLook { Live, Muted, SpeakerOn, SpeakerMuted, None }
// The round mute button of a card (56 px), one class for both flows. Live microphone: the palette's On ramp with its
// glow (the one saturated fill of the window); every other state is a glass disc with a ring. A state change
// crossfades the fill (180 ms), sends a ripple ring out (420 ms) and draws or retracts the microphone slash.
class MuteButton : Button {
    readonly bool mic; readonly ThemeLook look;
    readonly ScaleTransform squish=new ScaleTransform(1,1),rippleScale=new ScaleTransform(1,1);
    readonly Ellipse lit,sheen,ripple; readonly Grid shadow;
    readonly SolidColorBrush plainFill=new SolidColorBrush(Colors.Transparent),ring=new SolidColorBrush(Colors.Transparent),rippleInk=new SolidColorBrush(Colors.Transparent);
    readonly DropShadowEffect litGlow=new DropShadowEffect { BlurRadius=20,ShadowDepth=6,Direction=270,Opacity=.5 },inkShadow=new DropShadowEffect { Color=Colors.Black,BlurRadius=1.5,ShadowDepth=1,Direction=270,Opacity=0 };
    readonly LineGeometry slash=new LineGeometry(new Point(4.5,4.5),new Point(4.5,4.5));
    readonly List<Path> strokes=new List<Path>(); readonly Path slashPath,waves,cross; bool applied; Palette palette;
    internal MuteLook Shown { get; private set; }
    internal MuteButton(bool microphone,ThemeLook look) {
        mic=microphone; this.look=look; Width=Height=56; Template=Glyph.Bare; Cursor=Cursors.Hand; SetResourceReference(FocusVisualStyleProperty,"Focus"); Shown=MuteLook.None;
        Grid g=new Grid { RenderTransformOrigin=new Point(.5,.5),RenderTransform=squish,Background=Brushes.Transparent };
        ripple=new Ellipse { Stroke=rippleInk,StrokeThickness=1.5,Opacity=0,RenderTransformOrigin=new Point(.5,.5),RenderTransform=rippleScale,IsHitTestVisible=false }; g.Children.Add(ripple);
        shadow=Glyph.OuterShadow(28,true,new DropShadowEffect { Color=Colors.Black,BlurRadius=10,ShadowDepth=4,Direction=270,Opacity=.25 }); shadow.Opacity=0; g.Children.Add(shadow);
        g.Children.Add(new Ellipse { Fill=plainFill,Stroke=ring,StrokeThickness=1 });
        lit=new Ellipse { Opacity=0,Effect=litGlow }; g.Children.Add(lit);
        LinearGradientBrush top=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(0,1) }; top.GradientStops.Add(new GradientStop(Glyph.Hex("#80FFFFFF"),0)); top.GradientStops.Add(new GradientStop(Glyph.Hex("#00FFFFFF"),.3)); top.Freeze();
        sheen=new Ellipse { Margin=new Thickness(.5),Stroke=top,StrokeThickness=1,Opacity=0,IsHitTestVisible=false }; g.Children.Add(sheen);
        Canvas icon;
        if(mic) {
            slashPath=Glyph.Stroke(slash,Brushes.White,1.9); slashPath.Opacity=0;
            icon=Glyph.Box(24,Glyph.Stroke(new RectangleGeometry(new Rect(9,3,6,11),3,3),Brushes.White,1.9),Glyph.Stroke(Glyph.MicStand,Brushes.White,1.9),slashPath);
        } else {
            waves=Glyph.Stroke(Glyph.Waves,Brushes.White,1.9); cross=Glyph.Stroke(Glyph.Cross,Brushes.White,1.9); cross.Opacity=0;
            icon=Glyph.Box(24,Glyph.Stroke(Glyph.SpeakerBody,Brushes.White,1.9),waves,cross);
        }
        foreach(UIElement e in icon.Children)strokes.Add((Path)e); icon.Effect=inkShadow; g.Children.Add(icon); Content=g;
    }
    // Shows a state in the palette's colours; animate=false sets it at once (first show, previews).
    internal void Apply(MuteLook state,Palette p,bool animate) {
        if(applied&&state==Shown&&p==palette&&animate)return; // a call without animation also ends any transition in flight
        if(!animate) { ripple.BeginAnimation(OpacityProperty,null); ripple.Opacity=0; }
        bool changed=applied&&state!=Shown; Shown=state; palette=p; applied=true;
        Color fill,ringColor=Colors.Transparent,rippleColor=Colors.Transparent; Brush ink; double litTo=0,shadowTo=0; bool slashOn=false,crossOn=false;
        switch(state) {
            case MuteLook.Live: lit.Fill=p.On.Brush(); litGlow.Color=p.On.Glow; litTo=1; fill=Glyph.Hex("#0EFFFFFF"); ink=Brushes.White; rippleColor=p.On.Main; break;
            case MuteLook.Muted: fill=Glyph.Hex("#0EFFFFFF"); ringColor=ThemeSpec.Alpha(p.Off.Main,.55); ink=new SolidColorBrush(p.Off.Light); slashOn=true; rippleColor=p.Off.Main; break;
            case MuteLook.SpeakerOn: fill=Glyph.Hex("#12FFFFFF"); ringColor=Glyph.Hex("#24FFFFFF"); shadowTo=1; ink=look.AccentInk; rippleColor=look.Accent.Color; break;
            case MuteLook.SpeakerMuted: fill=Glyph.Hex("#12FFFFFF"); ringColor=ThemeSpec.Alpha(p.Off.Main,.55); ink=new SolidColorBrush(p.Off.Light); crossOn=true; rippleColor=p.Off.Main; break;
            default: fill=Glyph.Hex("#0AFFFFFF"); ringColor=Glyph.Hex("#14FFFFFF"); ink=Theme.Brush("#4DFFFFFF"); break;
        }
        double s=animate?.18:0;
        Glyph.Fade(lit,litTo,s); Glyph.Fade(sheen,litTo,s); Glyph.Fade(shadow,shadowTo,s); inkShadow.Opacity=litTo>0?.3:0;
        Glyph.Tint(plainFill,fill,s); Glyph.Tint(ring,ringColor,s);
        foreach(Path path in strokes)path.Stroke=ink;
        if(mic) {
            Point end=slashOn?new Point(19.5,19.5):new Point(4.5,4.5);
            if(s>0) { if(slashOn) { slashPath.BeginAnimation(OpacityProperty,null); slashPath.Opacity=1; } else Glyph.Fade(slashPath,0,s); slash.BeginAnimation(LineGeometry.EndPointProperty,new PointAnimation(end,TimeSpan.FromSeconds(s)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } }); }
            else { slash.BeginAnimation(LineGeometry.EndPointProperty,null); slash.EndPoint=end; Glyph.Fade(slashPath,slashOn?1:0,0); }
        } else { Glyph.Fade(waves,crossOn?0:1,s); Glyph.Fade(cross,crossOn?1:0,s); }
        IsEnabled=state!=MuteLook.None;
        if(changed&&animate&&state!=MuteLook.None) {
            rippleInk.Color=rippleColor; CubicEase ease=new CubicEase { EasingMode=EasingMode.EaseOut };
            ripple.BeginAnimation(OpacityProperty,new DoubleAnimation(.6,0,TimeSpan.FromSeconds(.42)) { EasingFunction=ease });
            DoubleAnimation grow=new DoubleAnimation(1,1.38,TimeSpan.FromSeconds(.42)) { EasingFunction=ease }; rippleScale.BeginAnimation(ScaleTransform.ScaleXProperty,grow); rippleScale.BeginAnimation(ScaleTransform.ScaleYProperty,grow);
        }
    }
    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e) {
        base.OnIsPressedChanged(e);
        DoubleAnimation a=IsPressed?new DoubleAnimation(.94,TimeSpan.FromSeconds(.09)):new DoubleAnimation(1,TimeSpan.FromSeconds(.32)) { EasingFunction=new SpringEase() };
        squish.BeginAnimation(ScaleTransform.ScaleXProperty,a); squish.BeginAnimation(ScaleTransform.ScaleYProperty,a);
    }
}
// The level slider of a card, 28 px high: the track (y 8-14) with the accent fill up to the thumb, the 18 px thumb, and
// under it the meter lane (y 21-24) of 6 px segments. A Slider underneath, so the keys (arrows 1, Page 10, Home/End),
// the automation range value and the focus ring come with it; the template is only a host for the drawing.
class LevelSlider : Slider {
    static ControlTemplate host;
    static readonly Brush Unlit=Theme.Brush("#17FFFFFF"),MutedLit=Theme.Brush("#57FFFFFF");
    readonly ThemeLook look; readonly Grid visual=new Grid { Background=Brushes.Transparent }; readonly Border fill; readonly Grid thumb; readonly Ellipse heldDot;
    readonly ScaleTransform thumbScale=new ScaleTransform(1,1); readonly TranslateTransform thumbMove=new TranslateTransform(); readonly DropShadowEffect glow;
    readonly Canvas meter=new Canvas { Height=3,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,21,0,0),IsHitTestVisible=false }; readonly List<Rectangle> segments=new List<Rectangle>();
    bool dragging,syncing,litMuted; int lit=-1;
    internal event Action<int> UserChanged; // a change made by the user (pointer, wheel, keys)
    internal event Action DragEnded; // the pointer let go of the thumb (or lost it)
    internal LevelSlider(ThemeLook look) {
        this.look=look; Minimum=0; Maximum=100; SmallChange=1; LargeChange=10; Height=28; Focusable=true; Cursor=Cursors.Hand; Glyph.Dim(this);
        if(host==null)host=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'><Border x:Name='host' Background='Transparent'/></ControlTemplate>");
        Template=host; SetResourceReference(FocusVisualStyleProperty,"Focus");
        visual.Children.Add(new Border { Height=6,CornerRadius=new CornerRadius(3),Background=Theme.Brush("#1AFFFFFF"),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,8,0,0) });
        LinearGradientBrush gradient=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
        foreach(SolidColorBrush stop in new SolidColorBrush[]{look.Accent,look.AccentLight}) { GradientStop g=new GradientStop { Offset=gradient.GradientStops.Count }; BindingOperations.SetBinding(g,GradientStop.ColorProperty,new Binding("Color") { Source=stop }); gradient.GradientStops.Add(g); }
        fill=new Border { Height=6,Width=0,CornerRadius=new CornerRadius(3),Background=gradient,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0) }; visual.Children.Add(fill);
        visual.Children.Add(meter);
        glow=new DropShadowEffect { BlurRadius=10,ShadowDepth=0,Opacity=.65 }; BindingOperations.SetBinding(glow,DropShadowEffect.ColorProperty,new Binding("Color") { Source=look.Accent });
        TransformGroup move=new TransformGroup(); move.Children.Add(thumbScale); move.Children.Add(thumbMove);
        thumb=new Grid { Width=18,Height=18,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,2,0,0),RenderTransformOrigin=new Point(.5,.5),RenderTransform=move,IsHitTestVisible=false };
        thumb.Children.Add(new Ellipse { Fill=Theme.Brush("#F6F6F8"),Effect=glow });
        heldDot=new Ellipse { Width=6,Height=6,Fill=look.Accent,Visibility=Visibility.Collapsed }; thumb.Children.Add(heldDot); visual.Children.Add(thumb);
        visual.SizeChanged+=delegate { BuildMeter(); Place(); };
    }
    public override void OnApplyTemplate() {
        base.OnApplyTemplate(); Border b=GetTemplateChild("host") as Border; if(b==null)return;
        Decorator old=visual.Parent as Decorator; if(old!=null)old.Child=null; b.Child=visual;
    }
    internal bool Dragging { get { return dragging; } }
    internal bool Held { get { return heldDot.Visibility==Visibility.Visible; } }
    internal int LitSegments { get { return Math.Max(0,lit); } }
    internal int Segments { get { return segments.Count; } }
    // A value from the audio state; ignored while the user drags, so the thumb stays under the pointer.
    internal void SetLevel(int value) { if(dragging)return; syncing=true; try { Value=value; } finally { syncing=false; } }
    internal void SetHeld(bool on) { heldDot.Visibility=on?Visibility.Visible:Visibility.Collapsed; }
    internal void Pulse() { glow.BeginAnimation(DropShadowEffect.OpacityProperty,new DoubleAnimation(.65,1,TimeSpan.FromSeconds(.15)) { AutoReverse=true }); }
    protected override void OnValueChanged(double oldValue,double newValue) {
        base.OnValueChanged(oldValue,newValue); double whole=Math.Round(newValue); if(whole!=newValue) { Value=whole; return; }
        Place(); if(!syncing) { Action<int> h=UserChanged; if(h!=null)h((int)whole); }
    }
    void Place() { double w=visual.ActualWidth; if(w<=18)return; double x=9+(Value-Minimum)/(Maximum-Minimum)*(w-18); fill.Width=x; thumbMove.X=x-9; }
    void MoveTo(double x) { double w=visual.ActualWidth; if(w<=18)return; Value=Math.Round(Math.Max(0,Math.Min(1,(x-9)/(w-18)))*100); }
    void Zoom() { double s=dragging?1.15:IsMouseOver?1.1:1; DoubleAnimation a=Theme.Motion(s,.12); thumbScale.BeginAnimation(ScaleTransform.ScaleXProperty,a); thumbScale.BeginAnimation(ScaleTransform.ScaleYProperty,a); }
    // A drag: BeginDrag when the pointer takes the thumb, EndDrag when it lets go; the self-test drives a drag through them.
    internal void BeginDrag() { dragging=true; Zoom(); }
    internal void EndDrag() { bool was=dragging; dragging=false; Zoom(); if(was) { Action h=DragEnded; if(h!=null)h(); } }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); if(CaptureMouse()) { BeginDrag(); MoveTo(e.GetPosition(visual).X); } e.Handled=true; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(dragging)MoveTo(e.GetPosition(visual).X); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if(dragging) { ReleaseMouseCapture(); e.Handled=true; } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); EndDrag(); }
    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); Zoom(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); Zoom(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { Value=Math.Max(Minimum,Math.Min(Maximum,Value+(e.Delta>0?2:-2))); e.Handled=true; }
    void BuildMeter() {
        meter.Children.Clear(); segments.Clear(); lit=-1; int n=(int)Math.Floor((visual.ActualWidth+2)/8);
        for(int i=0;i<n;i++) { Rectangle r=new Rectangle { Width=6,Height=3,RadiusX=1,RadiusY=1,Fill=Unlit }; Canvas.SetLeft(r,i*8); meter.Children.Add(r); segments.Add(r); }
    }
    // The meter: segments that start below level x width are lit; the last 12 % of the width is the rose zone; a muted flow lights grey.
    internal void ShowMeter(double level,bool muted) {
        double w=visual.ActualWidth,edge=Math.Max(0,Math.Min(1,level))*w; int count=0; while(count<segments.Count&&count*8<edge)count++;
        if(count==lit&&muted==litMuted)return; lit=count; litMuted=muted;
        for(int i=0;i<segments.Count;i++)segments[i].Fill=i>=count?Unlit:muted?MutedLit:i*8>=.88*w?Theme.Rose:(Brush)look.AccentInk;
    }
}
// The hold button of a level row: a 28 px circle with a padlock. Released: ghost (plate on hover), shackle open (-32
// degrees about its left foot). Held: a filled disc with an accent ring, shackle closed; the shackle turns with the spring.
class LockButton : Button {
    readonly ThemeLook look; readonly Ellipse plate,heldRing; readonly Path body,shackle; readonly RotateTransform turn=new RotateTransform(-32,5.4,7.2); readonly TranslateTransform shake=new TranslateTransform(); bool applied;
    internal bool Held { get; private set; }
    internal LockButton(ThemeLook look) {
        this.look=look; Width=Height=28; Template=Glyph.Bare; Cursor=Cursors.Hand; SetResourceReference(FocusVisualStyleProperty,"Focus"); Glyph.Dim(this);
        Grid g=new Grid { RenderTransform=shake,Background=Brushes.Transparent };
        plate=new Ellipse { Fill=Theme.Fill,Stroke=Theme.Hairline,StrokeThickness=1,Opacity=0 }; g.Children.Add(plate);
        heldRing=new Ellipse { Fill=Theme.FillHover,Stroke=Glyph.Bound(look.AccentInk,.55),StrokeThickness=1,Opacity=0 }; g.Children.Add(heldRing);
        body=Glyph.Stroke(new RectangleGeometry(new Rect(3,7.2,10,6.6),2,2),Theme.Muted,1.5); shackle=Glyph.Stroke(Glyph.Shackle,Theme.Muted,1.5); shackle.RenderTransform=turn;
        g.Children.Add(Glyph.Box(16,body,shackle)); Content=g;
        MouseEnter+=delegate { Hover(); }; MouseLeave+=delegate { Hover(); };
    }
    void Hover() { Theme.Fade(plate,IsMouseOver&&!Held?1:0,.12); }
    internal void SetHeld(bool on,bool animate) {
        if(applied&&on==Held&&animate)return; applied=true; Held=on; Brush ink=on?(Brush)look.AccentInk:Theme.Muted; body.Stroke=ink; shackle.Stroke=ink;
        Glyph.Fade(heldRing,on?1:0,animate?.18:0);
        if(animate)turn.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(on?0:-32,TimeSpan.FromSeconds(.42)) { EasingFunction=new SpringEase() });
        else { turn.BeginAnimation(RotateTransform.AngleProperty,null); turn.Angle=on?0:-32; }
        Hover();
    }
    // A foreign change was undone: a short shake (x 0, -3, 3, -2, 2, 0 over 300 ms).
    internal void Shake() {
        DoubleAnimationUsingKeyFrames k=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(.3) }; double[] xs={0,-3,3,-2,2,0};
        for(int i=0;i<xs.Length;i++)k.KeyFrames.Add(new LinearDoubleKeyFrame(xs[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.06*i)))); shake.BeginAnimation(TranslateTransform.XProperty,k);
    }
}
// The device button of a card: the friendly name of the default device and a chevron, on a ghost plate that extends
// 8 px past the column on both sides (so the name sits on the slider's left edge). The plate stays while the list is open.
class DeviceButton : Button {
    readonly Border plate; readonly TextBlock name; readonly RotateTransform turn=new RotateTransform(0,7,7.25); bool open;
    internal DeviceButton() {
        Height=28; Margin=new Thickness(-8,0,-8,0); Template=Glyph.Bare; Cursor=Cursors.Hand; SetResourceReference(FocusVisualStyleProperty,"Focus");
        Grid g=new Grid { Background=Brushes.Transparent };
        plate=new Border { CornerRadius=new CornerRadius(14),Background=Theme.Fill,BorderBrush=Theme.Hairline,BorderThickness=new Thickness(1),Opacity=0 }; g.Children.Add(plate);
        Grid row=new Grid { Margin=new Thickness(8,0,8,0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); g.Children.Add(row);
        name=Theme.Label("",TypeRoles.Label,Theme.Text); name.TextTrimming=TextTrimming.CharacterEllipsis; row.Children.Add(name);
        Path chevron=Glyph.Stroke(Glyph.Chevron,Theme.Muted,1.6); chevron.RenderTransform=turn; Canvas box=Glyph.Box(14,chevron); box.Margin=new Thickness(8,0,0,0); Grid.SetColumn(box,1); row.Children.Add(box);
        Content=g; MouseEnter+=delegate { Hover(); }; MouseLeave+=delegate { Hover(); };
    }
    internal string Text { get { return name.Text; } }
    internal bool IsOpen { get { return open; } }
    internal void SetDevice(string text,bool present) { string shown=present?text:"No device"; if(name.Text!=shown)name.Text=shown; ToolTip=present?text:null; IsEnabled=present; name.Opacity=present?1:.5; }
    internal void SetOpen(bool on) { if(on==open)return; open=on; plate.Background=on?Theme.FillHover:Theme.Fill; Hover(); turn.BeginAnimation(RotateTransform.AngleProperty,Theme.Motion(on?180:0,.2)); }
    void Hover() { Theme.Fade(plate,open||IsMouseOver?1:0,.12); }
}
// The device list of a card: one overlay for both flows inside the window's scaled tree (scrim below the title bar,
// a panel from 84 to 20 px of the content's edges, 6 px under its device button or above it near the bottom edge).
// Rows of 40 px; the default device carries the accent selection, which slides to a chosen row with the spring.
class DeviceList : Grid {
    internal event Action<int> Chosen; internal event Action SoundSettings; internal event Action Closed;
    readonly ThemeLook look; readonly Grid frame; readonly StackPanel rows=new StackPanel(); readonly Border selection; readonly ScrollViewer scroll;
    readonly TranslateTransform selectionMove=new TranslateTransform(),rise=new TranslateTransform(); readonly DispatcherTimer closer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(220) };
    readonly List<Button> buttons=new List<Button>(); readonly List<Ellipse> rings=new List<Ellipse>(),dots=new List<Ellipse>(); readonly List<TextBlock> names=new List<TextBlock>();
    int selected=-1;
    internal bool IsOpen { get; private set; }
    internal object Owner { get; private set; }
    internal DeviceList(ThemeLook look) {
        this.look=look; Visibility=Visibility.Collapsed; Background=look.Scrim; Margin=new Thickness(0,52,0,0);
        MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.OriginalSource==this)Close(); };
        frame=new Grid { VerticalAlignment=VerticalAlignment.Top,RenderTransform=rise }; Children.Add(frame);
        frame.Children.Add(new Border { CornerRadius=new CornerRadius(16),Background=look.Sheet,Effect=new DropShadowEffect { Color=Colors.Black,BlurRadius=34,ShadowDepth=14,Direction=270,Opacity=.55 } });
        Border panel=new Border { CornerRadius=new CornerRadius(16),Background=look.Sheet,BorderBrush=Theme.Hairline,BorderThickness=new Thickness(1),Padding=new Thickness(6) }; frame.Children.Add(panel);
        StackPanel stack=new StackPanel(); panel.Child=stack;
        Grid list=new Grid(); selection=new Border { Height=40,CornerRadius=new CornerRadius(11),Background=look.AccentSoft,VerticalAlignment=VerticalAlignment.Top,RenderTransform=selectionMove,Visibility=Visibility.Collapsed,IsHitTestVisible=false };
        list.Children.Add(selection); list.Children.Add(rows);
        scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=200,Content=list,Focusable=false }; stack.Children.Add(scroll);
        stack.Children.Add(new Border { Height=1,Background=Theme.Brush("#14FFFFFF"),Margin=new Thickness(10,4,10,2) });
        Grid line=new Grid { Height=34,Margin=new Thickness(10,0,10,0) }; TextBlock label=Theme.Label("Windows sound settings",TypeRoles.Caption,Theme.Muted); line.Children.Add(label);
        Canvas arrow=Glyph.Box(13,Glyph.Stroke(Glyph.Arrow,Theme.Muted,1.4)); arrow.HorizontalAlignment=HorizontalAlignment.Right; line.Children.Add(arrow);
        Button settings=Row(line); System.Windows.Automation.AutomationProperties.SetName(settings,"Windows sound settings"); stack.Children.Add(settings);
        settings.Click+=delegate { Action h=SoundSettings; if(h!=null)h(); Close(); };
        closer.Tick+=delegate { closer.Stop(); Close(); };
        PreviewKeyDown+=delegate(object s,KeyEventArgs e) {
            int at=buttons.IndexOf(Keyboard.FocusedElement as Button);
            if(e.Key==Key.Down||e.Key==Key.Up) { int next=at<0?Math.Max(0,selected):Math.Max(0,Math.Min(buttons.Count-1,at+(e.Key==Key.Down?1:-1))); if(next<buttons.Count) { buttons[next].Focus(); buttons[next].BringIntoView(); } e.Handled=true; }
            else if(e.Key==Key.Enter&&at>=0) { Choose(at); e.Handled=true; }
        };
    }
    // A row: a bare button over a hover plate (#0FFFFFFF, radius 11).
    static Button Row(FrameworkElement content) {
        Grid g=new Grid { Background=Brushes.Transparent }; Border hover=new Border { CornerRadius=new CornerRadius(11),Background=Theme.Brush("#0FFFFFFF"),Opacity=0 }; g.Children.Add(hover); g.Children.Add(content);
        Button b=new Button { Template=Glyph.Bare,Content=g,Cursor=Cursors.Hand }; b.SetResourceReference(FocusVisualStyleProperty,"Focus");
        b.MouseEnter+=delegate { Theme.Fade(hover,1,.12); }; b.MouseLeave+=delegate { Theme.Fade(hover,0,.12); }; return b;
    }
    internal int Count { get { return buttons.Count; } }
    internal int Selected { get { return selected; } }
    internal string RowName(int i) { return names[i].Text; }
    internal IList<Button> Rows { get { return buttons; } }
    // Opens for an owner (the card) with its devices; anchor is the device button in the content's coordinates.
    internal void Open(object owner,IList<AudioDevice> devices,Rect anchor,Size content) {
        closer.Stop(); Owner=owner; rows.Children.Clear(); buttons.Clear(); rings.Clear(); dots.Clear(); names.Clear(); selected=-1; selection.Visibility=Visibility.Collapsed;
        for(int i=0;i<devices.Count;i++) {
            int index=i; Grid line=new Grid { Height=40,Margin=new Thickness(10,0,10,0) }; line.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(26) }); line.ColumnDefinitions.Add(new ColumnDefinition());
            Grid mark=new Grid { Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Left }; Ellipse ring=new Ellipse { StrokeThickness=1.5,Stroke=Theme.Brush("#4DFFFFFF") }; mark.Children.Add(ring);
            Ellipse dot=new Ellipse { Width=8,Height=8,Fill=look.Accent,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(0,0) }; mark.Children.Add(dot); line.Children.Add(mark);
            TextBlock name=Theme.Label(devices[i].Name,TypeRoles.Label,Theme.Ink2); name.TextTrimming=TextTrimming.CharacterEllipsis; Grid.SetColumn(name,1); line.Children.Add(name);
            Button b=Row(line); b.ToolTip=devices[i].Name; System.Windows.Automation.AutomationProperties.SetName(b,devices[i].Name); b.Click+=delegate { Choose(index); };
            rows.Children.Add(b); buttons.Add(b); rings.Add(ring); dots.Add(dot); names.Add(name);
        }
        for(int i=0;i<devices.Count;i++)if(devices[i].IsDefault)Mark(i,false);
        frame.Margin=new Thickness(84,0,20,0); frame.Measure(new Size(content.Width,double.PositiveInfinity)); double h=frame.DesiredSize.Height,top=anchor.Bottom+6;
        if(top+h>content.Height-10)top=anchor.Top-6-h;
        frame.Margin=new Thickness(84,Math.Max(0,top-Margin.Top),20,0);
        IsOpen=true; Visibility=Visibility.Visible; Opacity=0; Theme.Fade(this,1,.16); rise.Y=6; rise.BeginAnimation(TranslateTransform.YProperty,Theme.Motion(0,.22));
        scroll.ScrollToTop(); if(selected>=0)Dispatcher.BeginInvoke((Action)delegate { if(IsOpen&&selected<buttons.Count) { buttons[selected].BringIntoView(); buttons[selected].Focus(); } },DispatcherPriority.Loaded);
    }
    internal void Close() { if(!IsOpen)return; IsOpen=false; closer.Stop(); Visibility=Visibility.Collapsed; Action h=Closed; if(h!=null)h(); }
    // Choosing: the selection slides to the row and its dot pops in, the change runs at once, the list closes 220 ms later.
    internal void Choose(int i) { if(!IsOpen||closer.IsEnabled||i<0||i>=buttons.Count)return; Mark(i,true); Action<int> h=Chosen; if(h!=null)h(i); closer.Start(); }
    void Mark(int i,bool animate) {
        if(selected>=0&&selected<buttons.Count&&selected!=i) { rings[selected].Stroke=Theme.Brush("#4DFFFFFF"); names[selected].Foreground=Theme.Ink2; Pop(dots[selected],0,animate); }
        bool first=selected<0; selected=i; rings[i].Stroke=look.Accent; names[i].Foreground=Theme.Text; Pop(dots[i],1,animate);
        selection.Visibility=Visibility.Visible;
        if(animate&&!first)selectionMove.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(i*40,TimeSpan.FromSeconds(.42)) { EasingFunction=new SpringEase() });
        else { selectionMove.BeginAnimation(TranslateTransform.YProperty,null); selectionMove.Y=i*40; }
    }
    static void Pop(Ellipse dot,double to,bool animate) {
        ScaleTransform s=(ScaleTransform)dot.RenderTransform;
        if(animate) { DoubleAnimation a=to>0?SpringEase.To(to):Theme.Motion(to,.12); s.BeginAnimation(ScaleTransform.ScaleXProperty,a); s.BeginAnimation(ScaleTransform.ScaleYProperty,a); }
        else { s.BeginAnimation(ScaleTransform.ScaleXProperty,null); s.BeginAnimation(ScaleTransform.ScaleYProperty,null); s.ScaleX=s.ScaleY=to; }
    }
}
}
