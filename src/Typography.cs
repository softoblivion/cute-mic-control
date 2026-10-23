using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace MicControl {
// The type scale shared by the softoblivion apps, as WPF values: seven roles with fixed sizes in device-independent
// pixels (= CSS px at 100 %), plus two size variants. Every text of the app is one of these roles; no element sets
// its own size. Faces: Sora for text, Geist Mono for versions and codes, embedded as static instances (fonts\*.ttf,
// one file per weight, in the assembly's resources); WPF picks the file by FontWeight. WPF has no letter-spacing,
// so the kit's tracking values are left out; the section role is written in capitals as in the kit.
class TypeRole {
    internal readonly string Name; internal readonly double Size,LineHeight; internal readonly FontWeight Weight; internal readonly bool Mono,Caps;
    internal TypeRole(string name,int weight,double size,double lineHeight,bool mono,bool caps) { Name=name; Weight=FontWeight.FromOpenTypeWeight(weight); Size=size; LineHeight=Math.Round(size*lineHeight,2); Mono=mono; Caps=caps; }
    internal FontFamily Family { get { return Mono?TypeRoles.MonoFamily:TypeRoles.TextFamily; } }
    internal string Text(string text) { return Caps&&text!=null?text.ToUpper(CultureInfo.InvariantCulture):text; }
    internal void Apply(TextBlock t) { t.FontFamily=Family; t.FontSize=Size; t.FontWeight=Weight; t.LineHeight=LineHeight; t.LineStackingStrategy=LineStackingStrategy.BlockLineHeight; }
    internal void Apply(Control c) { c.FontFamily=Family; c.FontSize=Size; c.FontWeight=Weight; }
}
static class TypeRoles {
    // weight, size px / line height
    internal static readonly TypeRole
        Display=new TypeRole("Display",650,20,1.2,false,false),   // sheet and window titles
        Title=new TypeRole("Title",650,16,1.25,false,false),      // panel titles, the main button
        Section=new TypeRole("Section",600,11,1.3,false,true),    // group headers, chips, tags (capitals)
        Body=new TypeRole("Body",450,13,1.45,false,false),        // text, notes, hints
        Label=new TypeRole("Label",550,12.5,1.3,false,false),     // buttons, segmented options, setting labels
        LabelS=new TypeRole("LabelS",550,11.5,1.3,false,false),   // small buttons, compact segments
        Caption=new TypeRole("Caption",450,11.5,1.35,false,false),// meta lines, secondary lines, status
        Mono=new TypeRole("Mono",450,12,1.4,true,false),          // key names, codes
        MonoS=new TypeRole("MonoS",450,11,1.3,true,false);        // the version pill
    internal static readonly TypeRole[] All={Display,Title,Section,Body,Label,LabelS,Caption,Mono,MonoS};
    static FontFamily text,mono;
    // Created on first use, after the Application object exists (the pack:// scheme is registered by then).
    internal static FontFamily TextFamily { get { if(text==null)text=new FontFamily(new Uri("pack://application:,,,/"),"./fonts/#Sora, Segoe UI"); return text; } }
    internal static FontFamily MonoFamily { get { if(mono==null)mono=new FontFamily(new Uri("pack://application:,,,/"),"./fonts/#Geist Mono, Cascadia Mono, Consolas"); return mono; } }
    // True when the embedded face itself is used (not the fallback) for the given weight.
    internal static bool Embedded(FontFamily family,string name,FontWeight weight) {
        Typeface face=new Typeface(family,FontStyles.Normal,weight,FontStretches.Normal); GlyphTypeface glyphs;
        if(!face.TryGetGlyphTypeface(out glyphs))return false;
        foreach(string n in glyphs.FamilyNames.Values)if(n==name)return glyphs.FontUri.AbsoluteUri.StartsWith("pack://",StringComparison.OrdinalIgnoreCase)&&glyphs.Weight==weight;
        return false;
    }
    internal static string Describe(FontFamily family,FontWeight weight) {
        GlyphTypeface glyphs; if(!new Typeface(family,FontStyles.Normal,weight,FontStretches.Normal).TryGetGlyphTypeface(out glyphs))return "no glyph typeface";
        return glyphs.FontUri+" "+string.Join("/",new List<string>(glyphs.FamilyNames.Values).ToArray())+" "+glyphs.Weight;
    }
    // The roles as a resource dictionary for templates: "Type.<Role>" (TextBlock style), "Type.<Role>.Size" (double),
    // "Type.<Role>.Weight" (FontWeight), "Type.Text" / "Type.Mono" (FontFamily).
    internal static ResourceDictionary Dictionary() {
        ResourceDictionary d=new ResourceDictionary(); d["Type.Text"]=TextFamily; d["Type.Mono"]=MonoFamily;
        foreach(TypeRole r in All) {
            Style s=new Style(typeof(TextBlock));
            s.Setters.Add(new Setter(TextBlock.FontFamilyProperty,r.Family)); s.Setters.Add(new Setter(TextBlock.FontSizeProperty,r.Size)); s.Setters.Add(new Setter(TextBlock.FontWeightProperty,r.Weight));
            s.Setters.Add(new Setter(TextBlock.LineHeightProperty,r.LineHeight)); s.Setters.Add(new Setter(TextBlock.LineStackingStrategyProperty,LineStackingStrategy.BlockLineHeight)); s.Seal();
            d["Type."+r.Name]=s; d["Type."+r.Name+".Size"]=r.Size; d["Type."+r.Name+".Weight"]=r.Weight;
        }
        return d;
    }
}
}
