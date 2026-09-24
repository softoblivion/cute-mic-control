using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
namespace MicControl {
static class Theme {
    internal static Color Pink=Color.FromRgb(255,112,190),Purple=Color.FromRgb(161,112,255);
    internal static Brush Text=Brush("#F3ECFA"),Muted=Brush("#8F839E");
    internal static SolidColorBrush Brush(string hex) { SolidColorBrush b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    internal static TextBlock Label(string text,double size,Brush brush) { return new TextBlock { Text=text,FontSize=size,Foreground=brush,FontFamily=new FontFamily("Bahnschrift"),VerticalAlignment=VerticalAlignment.Center }; }
    internal static DoubleAnimation Motion(double to,double seconds) { return new DoubleAnimation(to,TimeSpan.FromSeconds(seconds)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } }; }
    internal static void Fade(UIElement el,double to,double seconds) { el.BeginAnimation(UIElement.OpacityProperty,Motion(to,seconds)); }
    internal static LinearGradientBrush Gradient(string a,string b) { return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a),(Color)ColorConverter.ConvertFromString(b),35); }
    internal static ResourceDictionary Resources() {
        const string xaml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'>
  <Setter Property='Foreground' Value='#F3ECFA'/><Setter Property='FontFamily' Value='Bahnschrift'/><Setter Property='FontSize' Value='14'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Background' Value='#191222'/><Setter Property='BorderBrush' Value='#392643'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='16,10'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Grid RenderTransformOrigin='0.5,0.5'><Grid.RenderTransform><ScaleTransform x:Name='scale'/></Grid.RenderTransform>
    <Border x:Name='body' CornerRadius='14' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>
    <Border x:Name='hover' CornerRadius='14' Opacity='0' Background='#20E68CF6' IsHitTestVisible='False'/>
    <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.2'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.3'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>
    <Trigger Property='IsPressed' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='scale' Storyboard.TargetProperty='ScaleX' To='0.975' Duration='0:0:0.09'/><DoubleAnimation Storyboard.TargetName='scale' Storyboard.TargetProperty='ScaleY' To='0.975' Duration='0:0:0.09'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='scale' Storyboard.TargetProperty='ScaleX' To='1' Duration='0:0:0.22'/><DoubleAnimation Storyboard.TargetName='scale' Storyboard.TargetProperty='ScaleY' To='1' Duration='0:0:0.22'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='body' Property='BorderBrush' Value='#CC8CEF'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ToggleButton'>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='Width' Value='48'/><Setter Property='Height' Value='28'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>
   <Grid><Border CornerRadius='14' Background='#24192E' BorderBrush='#4B325A' BorderThickness='1'/><Border x:Name='lit' CornerRadius='14' Opacity='0'><Border.Background><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#D778C1'/><GradientStop Color='#8959D1' Offset='1'/></LinearGradientBrush></Border.Background></Border>
   <Ellipse x:Name='knob' Fill='#F8EAFF' Width='18' Height='18' HorizontalAlignment='Left' Margin='5,0,0,0'><Ellipse.RenderTransform><TranslateTransform x:Name='slide'/></Ellipse.RenderTransform></Ellipse>
   </Grid><ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='slide' Storyboard.TargetProperty='X' To='20' Duration='0:0:0.25'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation><DoubleAnimation Storyboard.TargetName='lit' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.25'/></Storyboard></BeginStoryboard></Trigger.EnterActions><Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='slide' Storyboard.TargetProperty='X' To='0' Duration='0:0:0.25'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation><DoubleAnimation Storyboard.TargetName='lit' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.25'/></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='knob' Property='Fill' Value='#FFAFE1'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ListBoxItem'><Setter Property='Foreground' Value='#D7CAE3'/><Setter Property='Padding' Value='12,8'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='b' Background='Transparent' CornerRadius='8' Padding='{TemplateBinding Padding}' BorderThickness='1' BorderBrush='Transparent'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='b' Property='Background' Value='#513058'/><Setter Property='Foreground' Value='#FFE6F6'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#AF71B9'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ScrollBar'><Setter Property='Width' Value='7'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True'><Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='grip' Background='#61466E' CornerRadius='3' Margin='1,0'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='grip' Property='Background' Value='#B07EBD'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='Slider'><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
  <Grid Margin='0,0,0,0'><Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' Orientation='Horizontal'>
   <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='3' CornerRadius='2' Background='#B774CC'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
   <Track.Thumb><Thumb Width='15' Height='15'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse x:Name='dot' Fill='#F4DDFB' Stroke='#D598E5' StrokeThickness='1'><Ellipse.Effect><DropShadowEffect Color='#C17EE2' BlurRadius='10' ShadowDepth='0' Opacity='0.65'/></Ellipse.Effect></Ellipse><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='dot' Property='Fill' Value='White'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='dot' Property='Fill' Value='#FFB5EB'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
   <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='3' CornerRadius='2' Background='#37273F'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
  </Track></Grid><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>";
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }
}
// Vector emoji hearts keep their color and sharp edges independent of OS emoji fonts.
class HeartView : Grid {
    const string Whole="M 50,89 C 43,82 8,61 8,34 C 8,11 36,8 50,29 C 64,8 92,11 92,34 C 92,61 57,82 50,89 Z";
    const string Left="M 48,27 C 34,8 8,11 8,34 C 8,60 37,78 47,87 L 43,62 53,50 42,40 48,27 Z";
    const string Right="M 54,24 C 69,8 92,12 92,34 C 92,60 64,80 53,89 L 50,64 61,49 49,39 54,24 Z";
    Grid full,broken; ScaleTransform beat; TranslateTransform leftMove=new TranslateTransform(),rightMove=new TranslateTransform(); List<Ellipse> halos=new List<Ellipse>(); bool? last; bool set;
    internal HeartView(double size) {
        Width=Height=size; IsHitTestVisible=false;
        Grid scene=new Grid { Width=180,Height=180 };
        for(int i=0;i<2;i++) {
            Ellipse halo=new Ellipse { Width=158-i*28,Height=158-i*28,Opacity=0,RenderTransformOrigin=new Point(.5,.5) };
            RadialGradientBrush fill=new RadialGradientBrush(); fill.GradientStops.Add(new GradientStop(Color.FromArgb(0,244,75,174),0)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(85,228,65,161),.4)); fill.GradientStops.Add(new GradientStop(Color.FromArgb(40,157,80,255),.63)); fill.GradientStops.Add(new GradientStop(Colors.Transparent,1));
            halo.Fill=fill; halo.RenderTransform=new ScaleTransform(1,1); scene.Children.Add(halo); halos.Add(halo);
        }
        Grid hearts=new Grid { Width=100,Height=100,RenderTransformOrigin=new Point(.5,.52) }; beat=new ScaleTransform(1,1); hearts.RenderTransform=beat;
        full=new Grid(); full.Children.Add(Path(Whole,false));
        broken=new Grid { Opacity=0 };
        System.Windows.Shapes.Path left=Path(Left,true); left.RenderTransform=leftMove; broken.Children.Add(left);
        System.Windows.Shapes.Path right=Path(Right,true); rightMove.Y=2; right.RenderTransform=rightMove; broken.Children.Add(right);
        hearts.Children.Add(full); hearts.Children.Add(broken); scene.Children.Add(hearts);
        Viewbox box=new Viewbox { Child=scene }; Children.Add(box);
    }
    static System.Windows.Shapes.Path Path(string data,bool off) {
        System.Windows.Shapes.Path p=new System.Windows.Shapes.Path { Data=Geometry.Parse(data),StrokeThickness=.9,Stroke=Theme.Brush(off?"#C39FE2":"#FFA9DB") };
        p.Fill=new LinearGradientBrush(new GradientStopCollection { new GradientStop((Color)ColorConverter.ConvertFromString(off?"#C8A3EF":"#FFB4DC"),0),new GradientStop((Color)ColorConverter.ConvertFromString(off?"#A574D1":"#FC69B3"),.42),new GradientStop((Color)ColorConverter.ConvertFromString(off?"#634185":"#A539A5"),1) },new Point(.18,0),new Point(.8,1));
        p.Effect=new DropShadowEffect { Color=off?Theme.Purple:Theme.Pink,ShadowDepth=0,BlurRadius=off?9:15,Opacity=off?.22:.65,RenderingBias=RenderingBias.Quality }; return p;
    }
    internal void ApplyPalette(int index) {
        Palette palette=Palette.Get(index);
        foreach(System.Windows.Shapes.Path path in full.Children)ColorPath(path,palette.On);
        foreach(System.Windows.Shapes.Path path in broken.Children)ColorPath(path,palette.Off);
        foreach(Ellipse halo in halos) {
            RadialGradientBrush brush=(RadialGradientBrush)halo.Fill;
            Color a=palette.On.Glow; a.A=85; Color b=palette.On.Main; b.A=40;
            brush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(a,TimeSpan.FromSeconds(.32)));
            brush.GradientStops[2].BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(b,TimeSpan.FromSeconds(.32)));
        }
    }
    static void ColorPath(System.Windows.Shapes.Path path,ColorRamp ramp) {
        LinearGradientBrush brush=(LinearGradientBrush)path.Fill;
        Color[] colors={ramp.Light,ramp.Main,ramp.Deep};
        for(int i=0;i<colors.Length;i++)brush.GradientStops[i].BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(colors[i],TimeSpan.FromSeconds(.32)));
        path.Stroke=new SolidColorBrush(ramp.Light);
        ((DropShadowEffect)path.Effect).BeginAnimation(DropShadowEffect.ColorProperty,new ColorAnimation(ramp.Glow,TimeSpan.FromSeconds(.32)));
    }
    internal void SetState(bool? muted) {
        if(set && last==muted)return; set=true; last=muted;
        bool live=muted==false;
        leftMove.BeginAnimation(TranslateTransform.XProperty,Theme.Motion(live?0:-3,.4)); rightMove.BeginAnimation(TranslateTransform.XProperty,Theme.Motion(live?0:3,.4));
        Theme.Fade(full,live?1:0,.28); Theme.Fade(broken,live?0:1,.28); Opacity=muted.HasValue?1:.32;
        beat.BeginAnimation(ScaleTransform.ScaleXProperty,null); beat.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        if(live) { DoubleAnimationUsingKeyFrames pulse=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(2.3),RepeatBehavior=RepeatBehavior.Forever };
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1,KeyTime.FromPercent(0)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.095,KeyTime.FromPercent(.18),new KeySpline(.2,0,.2,1)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.018,KeyTime.FromPercent(.34),new KeySpline(.25,0,.4,1)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.055,KeyTime.FromPercent(.46),new KeySpline(.2,0,.2,1)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1,KeyTime.FromPercent(.72),new KeySpline(.25,0,.4,1)));
            pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1,KeyTime.FromPercent(1)));
            beat.BeginAnimation(ScaleTransform.ScaleXProperty,pulse); beat.BeginAnimation(ScaleTransform.ScaleYProperty,pulse);
        }
        for(int i=0;i<halos.Count;i++) {
            Ellipse h=halos[i]; ScaleTransform scale=(ScaleTransform)h.RenderTransform;
            h.BeginAnimation(OpacityProperty,null); scale.BeginAnimation(ScaleTransform.ScaleXProperty,null); scale.BeginAnimation(ScaleTransform.ScaleYProperty,null); h.Opacity=0;
            if(live) {
                DoubleAnimation expand=new DoubleAnimation(.66,1.2,TimeSpan.FromSeconds(2.3)) { RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(i*.3),EasingFunction=new SineEase { EasingMode=EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,expand); scale.BeginAnimation(ScaleTransform.ScaleYProperty,expand);
                DoubleAnimationUsingKeyFrames a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(2.3),RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromSeconds(i*.3) };
                a.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromPercent(0))); a.KeyFrames.Add(new SplineDoubleKeyFrame(1,KeyTime.FromPercent(.2),new KeySpline(.2,0,.2,1))); a.KeyFrames.Add(new SplineDoubleKeyFrame(0,KeyTime.FromPercent(1),new KeySpline(.2,0,.5,1))); h.BeginAnimation(OpacityProperty,a);
            }
        }
    }
}
class GlowOverlay : Window {
    internal HeartView Heart=new HeartView(88);
    internal GlowOverlay() {
        Width=Height=88; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent;
        Topmost=true; ShowInTaskbar=false; ShowActivated=false; Focusable=false; Content=Heart;
        SourceInitialized+=delegate { IntPtr h=new WindowInteropHelper(this).Handle; Native.SetWindowLong(h,-20,Native.GetWindowLong(h,-20)|0x20|0x80|0x08000000); };
        Place();
    }
    internal void Place() { Rect a=SystemParameters.WorkArea; Left=a.Left+8; Top=a.Top+8; }
    internal void SetScale(int percent) { double size=88*Math.Max(50,Math.Min(200,percent))/100.0; Width=Height=size; Heart.Width=Heart.Height=size; }
}
}
