using System;
using System.Windows.Media;
namespace MicControl {
class ColorRamp {
    internal readonly Color Light,Main,Deep,Glow;
    internal ColorRamp(string light,string main,string deep,string glow) {
        Light=Parse(light); Main=Parse(main); Deep=Parse(deep); Glow=Parse(glow);
    }
    static Color Parse(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    internal LinearGradientBrush Brush() { return new LinearGradientBrush(new GradientStopCollection {
        new GradientStop(Light,0),new GradientStop(Main,.42),new GradientStop(Deep,1)
    },new System.Windows.Point(.18,0),new System.Windows.Point(.8,1)); }
}
class Palette {
    internal readonly string Name,Description;
    internal readonly ColorRamp On,Off;
    Palette(string name,string description,ColorRamp on,ColorRamp off) { Name=name; Description=description; On=on; Off=off; }
    internal static readonly Palette[] All={
        new Palette("Nebula","Pink on / violet off",
            new ColorRamp("#FFB4DC","#FC69B3","#A539A5","#FF70BE"),
            new ColorRamp("#C8A3EF","#A574D1","#634185","#A170FF")),
        new Palette("Aurora","Mint on / rose off",
            new ColorRamp("#B4FFD8","#50E4B4","#238080","#52F1CA"),
            new ColorRamp("#FFD2CD","#F6979C","#884674","#EF9AA7")),
        new Palette("Glacier","Ice blue on / lavender off",
            new ColorRamp("#B6F5FF","#5EC7ED","#315BAD","#64DFFF"),
            new ColorRamp("#DFDAFC","#AAA0DC","#605B95","#BAAEFA")),
        new Palette("Ember","Amber on / coral off",
            new ColorRamp("#FFDEA5","#F9B85D","#B65B48","#FFAA57"),
            new ColorRamp("#F3B9BB","#CF747E","#74455F","#E38A9C")),
        new Palette("Lunar","Pearl on / muted lilac off",
            new ColorRamp("#F0EFFF","#CDD2F5","#7F85B8","#C6D4FF"),
            new ColorRamp("#B5ADCB","#82798F","#4C445F","#ACA1CB"))
    };
    internal static Palette Get(int index) { return All[Math.Max(0,Math.Min(All.Length-1,index))]; }
}
}
