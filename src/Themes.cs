using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
namespace MicControl {
// One of the four themes shared by the workspace apps: a hue family for the window and one contrast accent.
// The derived colours follow the shared kit's formula (mix(a,b,k) = a + (b - a) * k per channel), so the apps match.
class ThemeSpec {
    internal readonly string Name,Label;
    internal readonly Color Hue,Accent,AccentLight,AccentDeep,AccentInk,BgTop,BgMid,BgBottom,Line,GlyphGlow,ButtonGlow,FocusRing,PanelTint,SheetTint;
    internal ThemeSpec(string name,string label,string hue,string accent) {
        Name=name; Label=label; Hue=Parse(hue); Accent=Parse(accent);
        AccentLight=Mix(Accent,Colors.White,.22); AccentDeep=Mix(Accent,Hue,.42); AccentInk=Mix(Accent,Colors.White,.56);
        BgTop=Mix(Hue,Colors.Black,.95); BgMid=Mix(Hue,Colors.Black,.86); BgBottom=Mix(Hue,Colors.Black,.74); Line=Mix(Hue,Colors.White,.74);
        GlyphGlow=Alpha(Mix(Hue,Colors.White,.2),.55); ButtonGlow=Alpha(Accent,.55); FocusRing=Alpha(Mix(Accent,Colors.White,.45),.95);
        PanelTint=Alpha(Mix(Hue,Colors.Black,.82),.3); SheetTint=Alpha(Mix(Hue,Colors.Black,.88),.52);
    }
    static Color Parse(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    static byte Channel(double v) { return (byte)Math.Max(0,Math.Min(255,Math.Floor(v+.5))); }
    internal static Color Mix(Color a,Color b,double k) { return Color.FromRgb(Channel(a.R+(b.R-a.R)*k),Channel(a.G+(b.G-a.G)*k),Channel(a.B+(b.B-a.B)*k)); }
    internal static Color Alpha(Color c,double alpha) { return Color.FromArgb(Channel(255*alpha),c.R,c.G,c.B); }
    internal static Color Opaque(Color c) { return Color.FromRgb(c.R,c.G,c.B); }
    internal static System.Drawing.Color Drawing(Color c) { return System.Drawing.Color.FromArgb(c.R,c.G,c.B); }
}
static class Themes {
    internal const string Default="bloom";
    internal static readonly ThemeSpec[] All={
        new ThemeSpec("dusk","Dusk","#2a5bd8","#ff7a59"),
        new ThemeSpec("nova","Nova","#6a3df0","#ff8a4a"),
        new ThemeSpec("frost","Frost","#2d7fc9","#b58cff"),
        new ThemeSpec("bloom","Bloom","#d2409a","#3ee6b8")
    };
    internal static int IndexOf(string name) { for(int i=0;i<All.Length;i++)if(All[i].Name==name)return i; return -1; }
    internal static bool IsKnown(string name) { return IndexOf(name)>=0; }
    internal static ThemeSpec Get(string name) { int i=IndexOf(name); return All[i>=0?i:IndexOf(Default)]; }
}
// The live brushes and effects of the current theme. The window chrome and the controls hold them as local values
// or bindings (never through a style resource: WPF freezes a brush that a style setter hands out, which ends the
// animation); Apply() recolours them in place, animated over 320 ms, so every consumer follows. The focus ring is
// the one exception: a frozen brush replaced in the resources on every change (the ring is only visible while
// the keyboard moves the focus).
class ThemeLook {
    internal ThemeSpec Current; ResourceDictionary resources;
    internal readonly SolidColorBrush Accent=Solid(),AccentInk=Solid(),AccentLight=Solid(),AccentSoft=Solid(),Line=Solid(),BgTop=Solid(),BgMid=Solid(),Sheet=Solid(),Scrim=Solid();
    internal readonly LinearGradientBrush AccentGrad=Linear(new Point(0,0),new Point(1,1),0,.48,1),Frame=Linear(new Point(0,0),new Point(0,1),0,.5,1),FrameEdge=Linear(new Point(0,0),new Point(1,1),0,1),Orbit=Linear(new Point(0,0),new Point(1,1),0,1);
    internal readonly RadialGradientBrush Nebula=Radial(),Field=Radial();
    internal readonly DropShadowEffect AccentGlow=new DropShadowEffect { BlurRadius=22,ShadowDepth=6,Direction=270,Opacity=.55 },ThumbGlow=new DropShadowEffect { BlurRadius=10,ShadowDepth=0,Opacity=.65 },FrameShadow=new DropShadowEffect { BlurRadius=22,ShadowDepth=0,Opacity=.45 },GlyphGlow=new DropShadowEffect { BlurRadius=8,ShadowDepth=2,Direction=270,Opacity=1 };
    static SolidColorBrush Solid() { return new SolidColorBrush(Colors.Transparent); }
    static LinearGradientBrush Linear(Point a,Point b,params double[] offsets) { LinearGradientBrush g=new LinearGradientBrush { StartPoint=a,EndPoint=b }; foreach(double o in offsets)g.GradientStops.Add(new GradientStop(Colors.Transparent,o)); return g; }
    static RadialGradientBrush Radial() { RadialGradientBrush g=new RadialGradientBrush(); g.GradientStops.Add(new GradientStop(Colors.Transparent,0)); g.GradientStops.Add(new GradientStop(Colors.Transparent,1)); return g; }
    internal void Register(ResourceDictionary r) { resources=r; }
    internal void Apply(ThemeSpec t,bool animate) {
        Current=t; double s=animate?.32:0;
        if(resources!=null) { SolidColorBrush focus=new SolidColorBrush(t.FocusRing); focus.Freeze(); resources["T.Focus"]=focus; }
        Tint(Accent,t.Accent,s); Tint(AccentInk,t.AccentInk,s); Tint(AccentLight,t.AccentLight,s); Tint(AccentSoft,ThemeSpec.Alpha(t.Accent,.22),s); Tint(Line,t.Line,s);
        Tint(BgTop,t.BgTop,s); Tint(BgMid,t.BgMid,s); Tint(Sheet,ThemeSpec.Opaque(t.SheetTint),s); Tint(Scrim,ThemeSpec.Alpha(t.BgTop,.72),s);
        Stops(AccentGrad,s,t.AccentLight,t.Accent,t.AccentDeep); Stops(Frame,s,t.BgTop,t.BgMid,t.BgBottom);
        Stops(FrameEdge,s,ThemeSpec.Alpha(ThemeSpec.Mix(t.Hue,Colors.White,.3),.6),ThemeSpec.Alpha(t.Hue,.16)); Stops(Orbit,s,ThemeSpec.Alpha(t.AccentInk,.6),ThemeSpec.Alpha(t.AccentInk,0));
        Stops(Nebula,s,ThemeSpec.Alpha(t.Hue,.41),ThemeSpec.Alpha(t.Hue,0)); Stops(Field,s,ThemeSpec.Alpha(t.Accent,.28),ThemeSpec.Alpha(t.Accent,0));
        Glow(AccentGlow,t.Accent,s); Glow(ThumbGlow,t.Accent,s); Glow(FrameShadow,t.Hue,s); Glow(GlyphGlow,t.GlyphGlow,s);
    }
    static ColorAnimation Ease(Color to,double seconds) { return new ColorAnimation(to,TimeSpan.FromSeconds(seconds)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } }; }
    static void Tint(SolidColorBrush b,Color to,double s) { if(s<=0) { b.BeginAnimation(SolidColorBrush.ColorProperty,null); b.Color=to; } else b.BeginAnimation(SolidColorBrush.ColorProperty,Ease(to,s)); }
    static void Glow(DropShadowEffect e,Color to,double s) { if(s<=0) { e.BeginAnimation(DropShadowEffect.ColorProperty,null); e.Color=to; } else e.BeginAnimation(DropShadowEffect.ColorProperty,Ease(to,s)); }
    static void Stops(GradientBrush g,double s,params Color[] colors) { for(int i=0;i<colors.Length;i++) { GradientStop stop=g.GradientStops[i]; if(s<=0) { stop.BeginAnimation(GradientStop.ColorProperty,null); stop.Color=colors[i]; } else stop.BeginAnimation(GradientStop.ColorProperty,Ease(colors[i],s)); } }
}
}
