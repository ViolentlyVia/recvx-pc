"""Port the VU0 inline assembly in face_bh.c (facial animation) to C for TARGET_PC."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source

src = Source("src/ps2/veronica/prog/face_bh.c")
blocks = [
# 0: _fmCnkCalcMuscle - copy the jaw vertices from source to destination
"""
for ( ; n > 0; n--, jaw++)
{
    pcCopyQword(&dvp[jaw->id * 8], &svp[jaw->id * 8]);
}
""",
# 1: _fmCnkCalcMuscle - accumulate weighted muscle vectors (walks the lists backwards)
"""
if (m > 0)
{
    NJS_POINT3** vp = mvp;
    float* rp = mrp;
    float x = dvp[0], y = dvp[1], z = dvp[2];

    for ( ; m > 0; m--)
    {
        float r = *--rp * rsum;
        NJS_POINT3* v = *--vp;

        x += v->x * r;
        y += v->y * r;
        z += v->z * r;
    }

    dvp[0] = x;
    dvp[1] = y;
    dvp[2] = z;
}
""",
# 2: _fmCnkCalcJaw - vec = mat * dvp1
"""
pcVu0XformPoint((float*)mat, (float*)dvp1, (float*)&vec);
vec.w = dvp1->w;
""",
# 3: _fmCnkCalcJaw - vec = (jmat1 * (mat2 translated by jawtrans along its X row)) * dvp1
"""
{
    float* m2 = (float*)mat2;
    float* j1 = (float*)jmat1;
    float mt[16], t[16];
    int r;

    for (r = 0; r < 12; r++)
    {
        mt[r] = m2[r];
    }

    mt[12] = m2[0] * jawtrans + m2[12];
    mt[13] = m2[1] * jawtrans + m2[13];
    mt[14] = m2[2] * jawtrans + m2[14];
    mt[15] = m2[15];

    for (r = 0; r < 3; r++)
    {
        pcVu0XformVector(mt, &j1[r * 4], &t[r * 4]);
    }

    pcVu0XformPoint(mt, &j1[12], &t[12]);

    pcVu0XformPoint(t, (float*)dvp1, (float*)&vec);
    vec.w = dvp1->w;
}
""",
# 4: _fmCnkCalcEye - r3 = M * t3, q3 = (M * pos + eye) - M * pos
"""
{
    float* em = (float*)&fm->eyemat[i];
    float a[3], b[3];

    pcVu0XformPoint(em, (float*)&obj->pos, a);
    pcVu0XformPoint(em, (float*)&t3, b);

    q3.x = (a[0] + fm->param.eye.x) - a[0];
    q3.y = (a[1] + fm->param.eye.y) - a[1];
    q3.z = (a[2] + fm->param.eye.z) - a[2];

    r3.x = b[0];
    r3.y = b[1];
    r3.z = b[2];
}
""",
# 5: _fmCnkCalcTang - load identity into mat
"""
{
    float* m = (float*)mat;
    int r;

    for (r = 0; r < 16; r++)
    {
        m[r] = ((r % 5) == 0) ? 1.0f : 0.0f;
    }
}
""",
# 6: _fmCnkCalcTang - dvp = mat (with row 2 scaled by tangz) * svp
"""
{
    float* m = (float*)mat;
    float* s = &svp[calc_ofs];
    float* d = &dvp[calc_ofs];
    float x = s[0], y = s[1], z = s[2];

    d[0] = m[0] * x + m[4] * y + (m[8]  * tangz) * z + m[12];
    d[1] = m[1] * x + m[5] * y + (m[9]  * tangz) * z + m[13];
    d[2] = m[2] * x + m[6] * y + (m[10] * tangz) * z + m[14];
}
""",
]
assert len(src.asm_blocks()) == len(blocks), len(src.asm_blocks())
for code in blocks:
    src.port_asm(0, code)   # always the first remaining unported block
src.save()
