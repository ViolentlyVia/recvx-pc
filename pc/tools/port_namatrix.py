"""Port the VU0 assembly in ps2_NaMatrix.c (Ninja matrix library) to C for TARGET_PC.

Conventions (see pc/include/pc_vu0.h): matrices are float[16] in row order, a point p is
transformed as p.x * row0 + p.y * row1 + p.z * row2 + row3. Unless noted, the w components of
matrix rows are left untouched (the VU code only writes .xyz; the w it stored was stale data).
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from srcedit import Source

src = Source("src/ps2/veronica/prog/ps2_NaMatrix.c")

src.port_lines("int lNaMatIsUnitMatrix;", """int lNaMatIsUnitMatrix;
float pcVu0CN[16];
float pcVu0Last[4];""")

COPY_MATRIX = "pcCopyMatrix((float*){dst}, (const float*){src});"

blocks = [
# 0 njInitMatrix: only loads VU0 constants (vf1-vf3, vf16) and the VU0 microprogram jump table
"""
/* PC: VU0 constants are folded into the C code that used them */
(void)pi;
""",
# 1 njCalcPoints (the VU loop always runs at least once)
"""
{
    float* fm = (float*)m;
    int n = num;

    do
    {
        pcVu0XformPoint(fm, (float*)ps, pcVu0Last);

        pd->x = pcVu0Last[0];
        pd->y = pcVu0Last[1];
        pd->z = pcVu0Last[2];

        ps++;
        pd++;
    } while (--n > 0);
}
""",
# 2 njGetTranslation
"""
p->x = (*m)[12];
p->y = (*m)[13];
p->z = (*m)[14];
""",
# 3 njUnitTransPortion
"""
(*m)[12] = 0;
(*m)[13] = 0;
(*m)[14] = 0;
(*m)[15] = 1.0f;
""",
# 4 njUnitRotPortion
"""
{
    float* fm = (float*)m;
    int i;

    for (i = 0; i < 12; i++)
    {
        fm[i] = ((i % 5) == 0) ? 1.0f : 0;
    }
}
""",
# 5 njPushMatrix
COPY_MATRIX.format(dst="fpDst", src="fpSrc"),
# 6 njUnitMatrix
"""
{
    float* fm = (float*)m;
    int i;

    for (i = 0; i < 16; i++)
    {
        fm[i] = ((i % 5) == 0) ? 1.0f : 0;
    }
}
""",
# 7 njSetMatrix
COPY_MATRIX.format(dst="fpDst", src="fpSrc"),
# 8 njSetMatrixCN
COPY_MATRIX.format(dst="pcVu0CN", src="pMat"),
# 9 njGetMatrix
COPY_MATRIX.format(dst="fpDst", src="fpSrc"),
# 10 njMultiMatrix: md = ms * md (each row of ms transformed by md; w column comes from ms)
"""
{
    float* d = (float*)md;
    float* s = (float*)ms;
    float r[16];
    int i;

    for (i = 0; i < 3; i++)
    {
        pcVu0XformVector(d, &s[i * 4], &r[i * 4]);
        r[i * 4 + 3] = s[i * 4 + 3];
    }

    pcVu0XformPoint(d, &s[12], &r[12]);
    r[15] = s[15];

    pcCopyMatrix(d, r);
}
""",
# 11 njTranslate: row3 += x * row0 + y * row1 + z * row2
"""
{
    float* fm = (float*)m;
    float v[3];

    v[0] = x;
    v[1] = y;
    v[2] = z;

    pcVu0XformPoint(fm, v, &fm[12]);
}
""",
# 12 njTranslateV
"""
{
    float* fm = (float*)m;

    pcVu0XformPoint(fm, (float*)v, &fm[12]);
}
""",
# 13 njRotateX
"pcRotRows(&(*m)[4], &(*m)[8], fSin, fCos);",
# 14 njRotateY
"pcRotRows(&(*m)[0], &(*m)[8], -fSin, fCos);",
# 15 njRotateZ
"pcRotRows(&(*m)[0], &(*m)[4], fSin, fCos);",
# 16-18 njRotXYZ: X, then Y, then Z, using the sin/cos njSinCos just produced
"pcRotRows(&(*pMatrix)[4], &(*pMatrix)[8], fSin, fCos);",
"pcRotRows(&(*pMatrix)[0], &(*pMatrix)[8], -fSin, fCos);",
"pcRotRows(&(*pMatrix)[0], &(*pMatrix)[4], fSin, fCos);",
# 19 njRotate: rotation about axis v by ang (quaternion with half angle), m = R * m
"""
{
    float* fm = (float*)m;
    float x = v->x, y = v->y, z = v->z;
    float q = pcVu0Rsqrt((x * x + y * y) + z * z) * fSin;
    float w = fCos;
    float w2, r[9];
    int i;

    x *= q;
    y *= q;
    z *= q;

    w2 = w * w;

    r[0] = ((w2 + x * x) - y * y) - z * z;
    r[1] = (x * y + w * z) * 2;
    r[2] = (x * z - w * y) * 2;

    r[3] = (x * y - w * z) * 2;
    r[4] = ((w2 - x * x) + y * y) - z * z;
    r[5] = (y * z + w * x) * 2;

    r[6] = (x * z + w * y) * 2;
    r[7] = (y * z - w * x) * 2;
    r[8] = ((w2 - x * x) - y * y) + z * z;

    {
        float o[12];

        for (i = 0; i < 3; i++)
        {
            pcVu0XformVector(fm, &r[i * 3], &o[i * 4]);
        }

        for (i = 0; i < 3; i++)
        {
            fm[i * 4 + 0] = o[i * 4 + 0];
            fm[i * 4 + 1] = o[i * 4 + 1];
            fm[i * 4 + 2] = o[i * 4 + 2];
        }
    }
}
""",
# 20 njScale
"""
{
    float* fm = (float*)m;
    int i;

    for (i = 0; i < 3; i++)
    {
        fm[0 + i] *= sx;
        fm[4 + i] *= sy;
        fm[8 + i] *= sz;
    }
}
""",
# 21 njScaleV
"""
{
    float* fm = (float*)m;
    int i;

    for (i = 0; i < 3; i++)
    {
        fm[0 + i] *= v->x;
        fm[4 + i] *= v->y;
        fm[8 + i] *= v->z;
    }
}
""",
# 22 njInvertMatrix: rigid-body inverse (transpose 3x3, w column cleared; translation -T * R^T)
"""
{
    float* fm = (float*)m;
    float r[16];
    int i, j;

    for (i = 0; i < 3; i++)
    {
        for (j = 0; j < 3; j++)
        {
            r[i * 4 + j] = fm[j * 4 + i];
        }

        r[i * 4 + 3] = 0;
    }

    pcVu0XformVector(r, &fm[12], &r[12]);

    r[12] = 0 - r[12];
    r[13] = 0 - r[13];
    r[14] = 0 - r[14];
    r[15] = fm[15];

    pcCopyMatrix(fm, r);
}
""",
# 23 njTransposeMatrix (3x3 part)
"""
{
    float* fm = (float*)m;
    float t;

    t = fm[1]; fm[1] = fm[4]; fm[4] = t;
    t = fm[2]; fm[2] = fm[8]; fm[8] = t;
    t = fm[6]; fm[6] = fm[9]; fm[9] = t;
}
""",
# 24 njMirror: reflection matrix for the plane, built from the angles that rotate its normal
#    onto the Y axis (the C code after this block finishes the job). The VU version also has a
#    shortcut for planes through the origin that returns without touching m; njMirror is not
#    used by the game.
"""
{
    float vx = pl->vx, vy = pl->vy, vz = pl->vz;
    float inv = pcVu0Rsqrt((vx * vx + vy * vy) + vz * vz);
    float d = (vx * inv) * pl->px + (vy * inv) * pl->py + (vz * inv) * pl->pz;

    mat[12] = vx * (d * 2);
    mat[13] = vy * (d * 2);
    mat[14] = vz * (d * 2);
    mat[15] = 1.0f;

    lAngleX = (int)((njAtan2b(vz, vy) * 32768.0f) / 3.141592f);
    lAngleZ = -(int)((njAtan2b(vx, sqrtf(vy * vy + vz * vz)) * 32768.0f) / 3.141592f);
}
""",
# 25 njCalcPoint
"""
pcVu0XformPoint((float*)m, (float*)ps, pcVu0Last);

pd->x = pcVu0Last[0];
pd->y = pcVu0Last[1];
pd->z = pcVu0Last[2];
""",
# 26 njCalcPoint4
"""
pcVu0XformPoint((float*)pMatrix, (float*)pSrcPoint, pcVu0Last);

pDstPoint->x = pcVu0Last[0];
pDstPoint->y = pcVu0Last[1];
pDstPoint->z = pcVu0Last[2];
pDstPoint->w = pSrcPoint->w;
""",
# 27 njCalcPointCN
"""
pcVu0XformPoint(pcVu0CN, (float*)pSrcPoint, pcVu0Last);

pDstPoint->x = pcVu0Last[0];
pDstPoint->y = pcVu0Last[1];
pDstPoint->z = pcVu0Last[2];
""",
# 28 njAddVector
"""
vd->x = vd->x + vs->x;
vd->y = vd->y + vs->y;
vd->z = vd->z + vs->z;
""",
# 29 njSubVector
"""
vd->x = vd->x - vs->x;
vd->y = vd->y - vs->y;
vd->z = vd->z - vs->z;
""",
# 30 njCalcVector
"""
{
    float r[3];

    pcVu0XformVector((float*)m, (float*)vs, r);

    vd->x = r[0];
    vd->y = r[1];
    vd->z = r[2];
}
""",
# 31 njUnitVector: normalizes v and returns its original length
"""
{
    float l2 = (v->x * v->x + v->y * v->y) + v->z * v->z;
    float q = pcVu0Rsqrt(l2);

    v->x *= q;
    v->y *= q;
    v->z *= q;

    ret = l2 * q;
}
""",
# 32 njScalor
"ret = sqrtf((v->x * v->x + v->y * v->y) + v->z * v->z);",
# 33 njScalor2
"ret = (v->x * v->x + v->y * v->y) + v->z * v->z;",
# 34 njProjectScreen (Point / vf18 hold the CN-transformed point)
"""
{
    float q = pcVu0Div(_nj_screen_.dist, pcVu0Last[2]);

    p2->x = pcVu0Last[0] * q + fNaViwOffsetX;
    p2->y = pcVu0Last[1] * q + fNaViwOffsetY;
}
""",
# 35 njOuterProduct: ov = v1 x v2, returns |ov|
"""
{
    float x = v1->y * v2->z - v2->y * v1->z;
    float y = v1->z * v2->x - v2->z * v1->x;
    float z = v1->x * v2->y - v2->x * v1->y;

    ov->x = x;
    ov->y = y;
    ov->z = z;

    ret = sqrtf((x * x + y * y) + z * z);
}
""",
# 36 njInnerProduct
"ret = (v1->x * v2->x + v1->y * v2->y) + v1->z * v2->z;",
# 37 njTranslateEx: also leaves the current matrix in CN
"""
pcCopyMatrix(pcVu0CN, (float*)pNaMatMatrixStuckPtr);
pcVu0XformPoint(pcVu0CN, (float*)v, &pcVu0CN[12]);

(*pNaMatMatrixStuckPtr)[12] = pcVu0CN[12];
(*pNaMatMatrixStuckPtr)[13] = pcVu0CN[13];
(*pNaMatMatrixStuckPtr)[14] = pcVu0CN[14];
""",
# 38 njScaleEx: rows 0-2 of the current matrix scaled (all four components), also into CN
"""
{
    float* fm = (float*)pNaMatMatrixStuckPtr;
    int i;

    for (i = 0; i < 4; i++)
    {
        pcVu0CN[0 + i] = fm[0 + i] = fm[0 + i] * v->x;
        pcVu0CN[4 + i] = fm[4 + i] = fm[4 + i] * v->y;
        pcVu0CN[8 + i] = fm[8 + i] = fm[8 + i] * v->z;
    }
}
""",
# 39 njRotTransPers: x, y projected, z stays view space, iz = 1 / z
"""
{
    float q = pcVu0Div(1.0f, pcVu0Last[2]);
    float s = _nj_screen_.dist * q;

    pScreen->x = pcVu0Last[0] * s + fNaViwOffsetX;
    pScreen->y = pcVu0Last[1] * s + fNaViwOffsetY;
    pScreen->iz = q;
}
""",
# 40 njPers
"""
{
    float q = pcVu0Div(1.0f, pScreen->z);
    float s = _nj_screen_.dist * q;

    pcVu0Last[0] = pScreen->x;
    pcVu0Last[1] = pScreen->y;
    pcVu0Last[2] = pScreen->z;

    pScreen->x = pcVu0Last[0] * s + fNaViwOffsetX;
    pScreen->y = pcVu0Last[1] * s + fNaViwOffsetY;
    pScreen->iz = q;
}
""",
# 41 njCopyMatrix
COPY_MATRIX.format(dst="pDstMat", src="pSrcMat"),
# 42 njMulMatrixCN: CN = Mat2 * Mat1 (all four components)
"""
{
    float* a = (float*)pSrcMat1;
    float* b = (float*)pSrcMat2;
    int i, j;

    for (i = 0; i < 4; i++)
    {
        for (j = 0; j < 4; j++)
        {
            float v = (a[j] * b[i * 4 + 0] + a[4 + j] * b[i * 4 + 1]) + a[8 + j] * b[i * 4 + 2];

            pcVu0CN[i * 4 + j] = (i == 3) ? v + a[12 + j] : v;
        }
    }
}
""",
]

found = src.asm_blocks()
assert len(found) == len(blocks), (len(found), len(blocks))
for code in blocks:
    src.port_asm(0, code)
src.save()
