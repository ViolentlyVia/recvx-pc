"""Translate the VU0 macro-mode assembly in the 3D pipeline files to C (asm2c.py)."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source
import asm2c

FILES = ["src/ps2/veronica/prog/ps2_Vu1Strip.c", "src/ps2/veronica/prog/ps2_Vu1Scissor2.c",
         "src/ps2/veronica/prog/ps2_NinjaCnk.c"]


def port(path, dry=False):
    s = Source(path)
    n = 0
    errors = 0
    while True:
        blocks = s.asm_blocks()
        if not blocks:
            break
        a, b = blocks[0]
        tag = os.path.splitext(os.path.basename(path))[0].replace("ps2_", "") + "_a%d" % n
        try:
            code = asm2c.translate(s, a, b, tag)
        except Exception as e:
            import re
            fn = next((l for l in reversed(s.lines[:a]) if re.match(r"^[A-Za-z].*\(.*\)\s*$", l)), "?")
            print("%s: %s: %s" % (path, fn.strip(), e))
            errors += 1
            code = "#error asm block not translated"
        s.port_asm(0, code)
        n += 1
    if not dry and errors == 0:
        s.save()
    print(path, n, "blocks,", errors, "errors")
    return errors


def port_ninjacnk_specials():
    """Hand ports in ps2_NinjaCnk.c that the translator cannot do."""
    s = Source("src/ps2/veronica/prog/ps2_NinjaCnk.c")
    # _CVV: clip flags of the vertex transformed by vf24-vf27 (result returned in v0 via "jr ra")
    i = s.lines.index("int _CVV(float* v0)")
    a, b = next((a, b) for a, b in s.asm_blocks() if a > i)
    s.port_asm([x for x in s.asm_blocks()].index((a, b)), """
{
    VuVec r_;
    int k_;

    for (k_ = 0; k_ < 4; k_++)
    {
        r_.f[k_] = vu_f(vu_f(vu_f(pcVu0.vf[24].f[k_] * v0[0] + pcVu0.vf[25].f[k_] * v0[1]) + pcVu0.vf[26].f[k_] * v0[2]) + pcVu0.vf[27].f[k_]);
    }

    vu_clip(&pcVu0, &r_, r_.f[3]);
    ret = (int)pcVu0.clip;

    return ret;
}
""")
    # strips are always drawn by the EE path (the "vu1" C functions); no VU1 microcode on PC
    cond = "    if ((ulDrawGeneralPurposeWater != 0) || ((pVu1Func != vu1DrawTriangleStripTransDouble) && (pVu1Func != vu1DrawTriangleStripTransDoubleI))) "
    hits = [k for k, l in enumerate(s.lines) if l == cond]
    for k in reversed(hits):
        s._wrap(k, k, "    if (0) /* PC: always the EE strip path below, no VU1 */")
    print("VU1 path disabled in", len(hits), "functions")
    s.save()


if __name__ == "__main__":
    if "--specials" in sys.argv:
        port_ninjacnk_specials()
        sys.exit(0)
    dry = "--dry" in sys.argv
    files = [f for f in sys.argv[1:] if not f.startswith("--")] or FILES
    sys.exit(sum(port(f, dry) for f in files) != 0)

