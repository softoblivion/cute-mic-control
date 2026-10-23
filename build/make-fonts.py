"""Static TTF instances of the variable Sora (latin subset) and Geist Mono woff2 releases for WPF, which renders only
the default instance of a variable font. Writes Sora-W450 / W550 / W600 / W650.ttf and GeistMono-W450.ttf.
Usage: python build/make-fonts.py <folder with Sora-latin.woff2 and GeistMono-Variable.woff2> src/fonts
Needs fontTools and brotli (pip install fonttools brotli). Each instance keeps the family name and declares its
exact weight in usWeightClass, so WPF picks the file by FontWeight within one family."""
import sys
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools import subset

src = Path(sys.argv[1])      # folder with the variable woff2 files
out = Path(sys.argv[2])      # destination folder
out.mkdir(parents=True, exist_ok=True)

# (source, family, weights, subset): the mono face is cut to Basic Latin and Latin-1 plus the ellipsis and the
# middle dot (versions, key names, release-note code); the Sora file is already a latin subset.
JOBS = [
    ("Sora-latin.woff2", "Sora", [450, 550, 600, 650], None),
    ("GeistMono-Variable.woff2", "Geist Mono", [450], list(range(0x20, 0x7F)) + list(range(0xA0, 0x100)) + [0x2026, 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022]),
]

for file, family, weights, keep in JOBS:
    base = TTFont(src / file)
    print(file, "axes:", [(a.axisTag, a.minValue, a.defaultValue, a.maxValue) for a in base["fvar"].axes])
    for w in weights:
        font = TTFont(src / file)
        inst = instancer.instantiateVariableFont(font, {"wght": w}, updateFontNames=False)
        inst.flavor = None
        # The STAT table of the instance names only the hundreds; WPF reads the weight from it when present and reports
        # weight 1 for 450 / 550 / 650. Without STAT it takes usWeightClass, so the exact weight is declared.
        if "STAT" in inst:
            del inst["STAT"]
        cls, sub = w, {450: "Regular", 550: "Medium", 600: "SemiBold", 650: "Bold"}[w]
        inst["OS/2"].usWeightClass = cls
        bold = cls >= 650
        inst["OS/2"].fsSelection = (inst["OS/2"].fsSelection & ~0b1100001) | (0b100000 if bold else 0b1000000)
        inst["head"].macStyle = (inst["head"].macStyle & ~0b11) | (1 if bold else 0)
        name = inst["name"]
        ps = family.replace(" ", "") + "-" + sub
        file_name = family.replace(" ", "") + "-W%d" % w
        for rec in list(name.names):
            if rec.nameID in (1, 16):
                rec.string = family
            elif rec.nameID in (2, 17):
                rec.string = sub
            elif rec.nameID == 4:
                rec.string = family + " " + sub
            elif rec.nameID == 6:
                rec.string = ps
            elif rec.nameID == 3:
                rec.string = ps
            elif rec.nameID >= 256:
                pass
        # Drop typographic family / subfamily so the legacy family (1) is the one WPF groups by.
        name.names = [r for r in name.names if r.nameID not in (16, 17, 25)]
        if keep:
            options = subset.Options()
            options.name_IDs = ["*"]
            options.layout_features = ["*"]
            options.notdef_outline = True
            sub_setter = subset.Subsetter(options)
            sub_setter.populate(unicodes=keep)
            sub_setter.subset(inst)
        target = out / (file_name + ".ttf")
        inst.save(target)
        print("  wrote", target.name, target.stat().st_size)
