"""Port the PS2 2D/system graphics glue (ps2_dummy, ps2_NaSystem, ps2_loadtim2, ps2_NaDraw2D)
onto the GS emulator for TARGET_PC."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source, exclude_function

P = "src/ps2/veronica/prog/"

# ---- ps2_NaDraw2D.c ----
s = Source(P + "ps2_NaDraw2D.c")
s.port_asm(0, """
{
    int k;

    for (k = 0; k < 4; k++)
    {
        v0[k] = v1[k] / 4096.0f;
    }
}
""")
s.save()

# ---- ps2_NaSystem.c ----
s = Source(P + "ps2_NaSystem.c")
s.port_lines("void\tnjInitSystem( Int, Int, Int )", "void\tnjInitSystem( Int mode, Int frame, Int count )")
s.port_lines("int vsync_func(int) ", "int vsync_func(int cause)")
blocks = s.asm_blocks()
assert len(blocks) == 2, blocks
s.port_asm(0, "pcKernelBusyWait();")
s.port_asm(0, "")
i = [k for k, l in enumerate(s.lines) if "TICK = " in l][0]
s._wrap(i, i + 2, "    /* PC: no COP0 tick counter */")
s.save()

# ---- ps2_loadtim2.c: DMA helpers are reimplemented on the GS emulator (pc_libgraph.c) ----
s = Source(P + "ps2_loadtim2.c")
for f in ["void SyncPath()", "void D2_SyncTag()", "void loadImage(void* tags)"]:
    exclude_function(s, f, "pc/src/pc_libgraph.c")
s.port_lines("    return (void*)(((u_int)val & UNCMASK)|UNCBASE);", "    return val;")
s.save()

# ---- ps2_dummy.c ----
s = Source(P + "ps2_dummy.c")
for f in ["void _builtin_set_imask(int mask) ", "void Ps2Init() ", "void Ps2LoadModule(char* p)",
          "void Snd_init()", "void Cd_init()", "void Card_init()", "void PS2_jikken()"]:
    exclude_function(s, f, "pc/src/pc_system.c")

blocks = s.asm_blocks()
# AddPrim3D (int colors), Ex, Ex1P, Mod (float colors), then AddOT x6, BitCount, ExI
names = []
import re
for a, b in blocks:
    for i in range(a, -1, -1):
        m = re.match(r"^[A-Za-z].*?(\w+)\s*\(.*\)\s*$", s.lines[i])
        if m:
            names.append(m.group(1))
            break
print(names)
for name in names:
    if name == "Ps2AddPrim3D":
        code = "pcPs2Prim3DCore(dp, p, num, 0);"
    elif name.startswith("Ps2AddPrim3D"):
        code = "pcPs2Prim3DCore(dp, p, num, 1);"
    elif name == "Ps2AddOT":
        code = None
    elif name == "Ps2BitCount":
        code = """
{
    unsigned int v = value - 1;
    unsigned int n = 0;

    if (value == 0)
    {
        return 0;
    }

    while (v != 0)
    {
        n++;
        v >>= 1;
    }

    return n;
}
"""
    else:
        raise SystemExit("unexpected asm in " + name)
    if code is None:
        # "cvt.w.s f12, f12" + "mfc1 otz, f12": EE float->int conversion truncates
        line = s.lines[s.asm_blocks()[0][0]]
        code = "otz = (unsigned int)(int)z;" if "cvt.w.s" in line else "/* (mfc1) */"
    s.port_asm(0, code)

s.port_lines("    Ps2_PP = (void*)((int)&Ps2_PBUFF | 0x30000000); ", "    Ps2_PP = (void*)Ps2_PBUFF;")
s.port_lines("        *(u_long128*)&ps2_tp_cache[i] = 0; ", "        memset(&ps2_tp_cache[i], 0, 16);")
s.save()
