"""Port the VU0/FPU assembly in ps2_NaMath.c and ps2_NaView.c to C for TARGET_PC."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source

# Sine lookup: SinTable holds a quarter wave (16384 entries). Bit 14 of the angle selects the
# mirrored half of the quadrant (index 16383 - i, as on the PS2), bit 15 the negative half.
SIN = """
{
    int a = n;
    int i = a & 16383;

    ret = (a & 0x4000) ? SinTable[16383 - i] : SinTable[i];

    if (a & 0x8000)
    {
        ret = -ret;
    }
}
"""
COS = """
{
    int a = n;
    int i = a & 16383;

    if (a & 0x4000)
    {
        ret = SinTable[i];

        if (!(a & 0x8000))
        {
            ret = -ret;
        }
    }
    else
    {
        ret = SinTable[16383 - i];

        if (a & 0x8000)
        {
            ret = -ret;
        }
    }
}
"""
SINCOS = """
{
    int a = lAngle;
    int i = a & 16383;
    float s, c;

    if (a & 0x4000)
    {
        s = SinTable[16383 - i];
        c = SinTable[i];

        if (a & 0x8000)
        {
            s = -s;
        }
        else
        {
            c = -c;
        }
    }
    else
    {
        s = SinTable[i];
        c = SinTable[16383 - i];

        if (a & 0x8000)
        {
            s = -s;
            c = -c;
        }
    }

    *sin = s;
    *cos = c;
}
"""

src = Source("src/ps2/veronica/prog/ps2_NaMath.c")
blocks = [
    SIN,
    COS,
    SINCOS,
    "ret = sqrtf(fabsf(n));",
    "ret = pcVu0Rsqrt(n);",
    # njLinear
    """
odata[0] = (idata[3] - idata[0]) * frame + idata[0];
odata[1] = (idata[4] - idata[1]) * frame + idata[1];
odata[2] = (idata[5] - idata[2]) * frame + idata[2];
""",
    # njOverhauserSpline: Catmull-Rom through idata[0..3] (VU0 constants 3, 4, 5 from njInitMatrix)
    """
{
    float t = frame;
    float t2 = t * t;
    float t3 = t2 * t;
    float w0 = ((t2 + t2) - t3) - t;
    float w1 = ((t3 * 3.0f) - (t2 * 5.0f)) + 2.0f;
    float w2 = ((t2 * 4.0f) + t) + (t3 * -3.0f);
    float w3 = t3 - t2;
    int k;

    for (k = 0; k < 3; k++)
    {
        odata[k] = (((idata[k] * w0 + idata[3 + k] * w1) + idata[6 + k] * w2) + idata[9 + k] * w3) * ftmp;
    }
}
""",
]
found = src.asm_blocks()
assert len(found) == len(blocks), (len(found), len(blocks))
for code in blocks:
    src.port_asm(0, code)
src.save()

src = Source("src/ps2/veronica/prog/ps2_NaView.c")
blocks = [
    "/* PC: ClipMatrix2 is read directly by the renderer (was loaded into vf24-vf27) */\n(void)mp2;",
    "fM[11] = *fw;\nfM[10] = *fh;",
]
found = src.asm_blocks()
assert len(found) == len(blocks), (len(found), len(blocks))
for code in blocks:
    src.port_asm(0, code)
src.save()
