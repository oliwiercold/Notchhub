"""Creates static font instances usable by WPF (which ignores variable axes).
Run: python -I tools/fonts/instance_fonts.py <outdir>"""
import sys, os
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

here = os.path.dirname(os.path.abspath(__file__))
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "out")
os.makedirs(out, exist_ok=True)

def set_names(font, family, style, weight_class, is_bold=False, is_regular=False):
    name = font["name"]
    # drop any typographic / variation names
    name.names = [n for n in name.names if n.nameID not in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25) and n.nameID < 256]
    ribbi_style = "Bold" if is_bold else "Regular"
    ribbi_family = family if (is_bold or is_regular) else f"{family} {style}"
    full = f"{family} {style}"
    ps = f"{family.replace(' ', '')}-{style.replace(' ', '')}"
    for pid, eid, lid in ((3, 1, 0x409), (1, 0, 0)):
        name.setName(ribbi_family, 1, pid, eid, lid)
        name.setName(ribbi_style, 2, pid, eid, lid)
        name.setName(f"{ps};NotchHub", 3, pid, eid, lid)
        name.setName(full, 4, pid, eid, lid)
        name.setName(ps, 6, pid, eid, lid)
        name.setName(family, 16, pid, eid, lid)
        name.setName(style, 17, pid, eid, lid)
    os2 = font["OS/2"]
    os2.usWeightClass = weight_class
    fs = os2.fsSelection & ~0b1100001  # clear italic, bold, regular
    if is_bold: fs |= 0b100000
    elif is_regular: fs |= 0b1000000
    os2.fsSelection = fs
    font["head"].macStyle = 1 if is_bold else 0
    for t in ("STAT", "fvar", "gvar", "avar", "HVAR", "MVAR", "cvar"):
        if t in font: del font[t]

def make(src, dst, axes, family, style, weight_class, **kw):
    f = TTFont(src)
    inst = instancer.instantiateVariableFont(f, axes, inplace=False)
    set_names(inst, family, style, weight_class, **kw)
    inst.save(os.path.join(out, dst))
    print("wrote", dst)

sans = os.path.join(here, "GoogleSansFlex-VF.ttf")
for w, style, extra in ((400, "Regular", {"is_regular": True}), (500, "Medium", {}), (600, "SemiBold", {}), (700, "Bold", {"is_bold": True}), (800, "ExtraBold", {})):
    make(sans, f"NHSans-{style}.ttf", {"wght": w, "wdth": 100, "opsz": 14, "slnt": 0, "ROND": 0}, "NH Sans", style, w, **extra)
for w, style, extra in ((500, "Medium", {}), (700, "Bold", {"is_bold": True}), (800, "ExtraBold", {})):
    make(sans, f"NHSansRounded-{style}.ttf", {"wght": w, "wdth": 100, "opsz": 36, "slnt": 0, "ROND": 100}, "NH Sans Rounded", style, w, **extra)

sym = os.path.join(here, "MaterialSymbolsRounded-VF.ttf")
make(sym, "NHSymbols-Outline.ttf", {"FILL": 0, "wght": 400, "GRAD": 0, "opsz": 24}, "NH Symbols", "Regular", 400, is_regular=True)
make(sym, "NHSymbols-Filled.ttf", {"FILL": 1, "wght": 400, "GRAD": 0, "opsz": 24}, "NH Symbols Filled", "Regular", 400, is_regular=True)
