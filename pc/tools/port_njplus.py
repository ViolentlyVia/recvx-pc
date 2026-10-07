"""Port PS2-specific code in njplus.c (model morphing, UV scrolling) for TARGET_PC."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source

src = Source("src/ps2/veronica/prog/njplus.c")

# Uncached-accelerated mirror of main RAM (0x30000000 segment): plain pointer on PC.
for line in src.lines[:]:
    if "| 0x30000000)" in line and not line.startswith("#"):
        indent = line[:len(line) - len(line.lstrip())]
        src.port_lines(line, indent + "/* PC: no uncached-accelerated RAM mirror */")

uv_loop = """
{
    /* walk the strips: [count] then count * (index, u, v); count < 0 means reversed strip */
    unsigned char* p = (unsigned char*)plp;

    do
    {
        int cnt = *(short*)p;

        p += 2;

        if (cnt < 0)
        {
            cnt = -cnt;
        }

        do
        {
            ((short*)p)[1] += offu;
            ((short*)p)[2] += offv;

            p += 6;
        } while (--cnt > 0);

        p += usr_num * 2;
    } while (--srp_num > 0);

    plp = (short*)p;
}
"""

skin_init = """
{
    /* two copies of the object matrix with the character position removed from the translation */
    float* m = owp->mtx;
    float* pos = &((BH_PWORK*)pwp)->px;
    float* d = (float*)np.mxp[i][0];
    int k;

    for (k = 0; k < 16; k++)
    {
        d[k] = d[k + 16] = m[k];
    }

    for (k = 0; k < 3; k++)
    {
        d[12 + k] = d[28 + k] = m[12 + k] - pos[k];
    }
}
"""

skin_blend_pos = """
{
    /* blend vertex position between the two bone matrices by lv */
    float* m0 = (float*)np.mxp[rg0][0];
    float* m1 = (float*)np.mxp[rg0][1];
    float a[3], b[3];

    pcVu0XformPoint(m0, (float*)p0, a);
    pcVu0XformPoint(m1, (float*)p0, b);

    p1[0] = (b[0] - a[0]) * lv + a[0];
    p1[1] = (b[1] - a[1]) * lv + a[1];
    p1[2] = (b[2] - a[2]) * lv + a[2];
    p1[3] = m1[11];
}
"""

skin_blend_nrm = """
{
    /* blend vertex normal between the two bone matrices by lv */
    float* m0 = (float*)np.mxp[rg0][0];
    float* m1 = (float*)np.mxp[rg0][1];
    float a[3], b[3];

    pcVu0XformVector(m0, (float*)p0, a);
    pcVu0XformVector(m1, (float*)p0, b);

    p1[0] = (b[0] - a[0]) * lv + a[0];
    p1[1] = (b[1] - a[1]) * lv + a[1];
    p1[2] = (b[2] - a[2]) * lv + a[2];
    p1[3] = m0[11];
}
"""

skin_full = """
{
    /* vertex fully bound to the second bone matrix: transform position and normal */
    float* m1 = (float*)np.mxp[rg0][1];

    pcVu0XformPoint(m1, (float*)&p0[0], &p1[0]);
    p1[3] = p0[0].w;

    pcVu0XformVector(m1, (float*)&p0[1], &p1[4]);
    p1[7] = p0[1].w;
}
"""

blocks = [
# 0: npLinkModel? - interpolate two vertex positions/normals: fp = sp + (dp - sp) * no (w from dp)
"""
{
    float* s = (float*)sp;
    float* d = (float*)dp;
    float* f = (float*)fp;

    f[0] = (d[0] - s[0]) * no + s[0];
    f[1] = (d[1] - s[1]) * no + s[1];
    f[2] = (d[2] - s[2]) * no + s[2];
    f[3] = d[3];

    f[4] = (d[4] - s[4]) * no + s[4];
    f[5] = (d[5] - s[5]) * no + s[5];
    f[6] = (d[6] - s[6]) * no + s[6];
    f[7] = d[7];
}
""",
# 1: unchanged vertex - copy position and normal
"""
pcCopyQword(&fp[0], &sp[0]);
pcCopyQword(&fp[1], &sp[1]);
""",
# 2: npSetOffsetUV
uv_loop,
# 3: npSetOffsetUV2
uv_loop,
# 4-7: npInitCalcSkin / npCalcSkin
skin_init, skin_blend_pos, skin_blend_nrm, skin_full,
# 8-11: npInitCalcSkinFM / npCalcSkinFM
skin_init, skin_blend_pos, skin_blend_nrm, skin_full,
]
assert len(src.asm_blocks()) == len(blocks), src.asm_blocks()
for code in blocks:
    src.port_asm(0, code)
src.save()
