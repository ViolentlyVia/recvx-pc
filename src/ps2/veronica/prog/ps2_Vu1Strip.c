#include "../../../ps2/veronica/prog/ps2_Vu1Strip.h"
#include "../../../ps2/veronica/prog/ps2_NaFog.h"
#include "../../../ps2/veronica/prog/ps2_NaView.h"
#include "../../../ps2/veronica/prog/ps2_dummy.h"
#include "../../../ps2/veronica/prog/main.h"

static SCISSOR_PLANE planeset;
static SCISSOR scissorflip;
static NODE node;
float fVu1Projection;
float fVu1NearClip;
float fVu1InvNearClip;
float fVu1FarClip;
float fVu1OffsetX;
float fVu1OffsetY;
float fVu1AspectW;
float fVu1AspectH;
VU1_PRIM_BUF vu1ScessorBuf[16];
void (*pColorCalcFuncTbl[11])(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim) = 
{
	vu1GetVertexColor,
	vu1GetVertexColorCM,
	vu1GetVertexColorIgnore,
	vu1GetVertexColorDif,
	vu1GetVertexColorDifAmb,
	vu1GetVertexColorDifSpe1,
	vu1GetVertexColorDifSpe2,
	vu1GetVertexColorDifSpe3,
	vu1GetVertexColorDifSpe1Amb,
	vu1GetVertexColorDifSpe2Amb,
	vu1GetVertexColorDifSpe3Amb
};
VU1_COLOR vu1Diffuse = { 1.0f, 1.0f, 1.0f, 1.0f };
VU1_COLOR vu1Specula = { 1.0f, 1.0f, 1.0f, 1.0f };
VU1_COLOR vu1Ambient = { 1.0f, 1.0f, 1.0f, 1.0f };
float fVu1AlphaRatio = 128.0f;

extern void VU0_CLIP_VIEW_VOLUME_ALL() __attribute__((section(".vutext")));
extern void VU0_CALC_COLOR() __attribute__((section(".vutext")));
extern void VU0_INIT_CALC_COLOR() __attribute__((section(".vutext")));

// 100% matching!
void vu1SetScreenProjection(float fProjection)
{
    fVu1Projection = fProjection;
    
#ifdef TARGET_PC
    {
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_12 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        fr_12.f = fProjection;
        g_t0.d[0] = A2C_SX32(fr_12.u);
        memcpy(&pcVu0.vf[4], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[2] = vu_f(pcVu0.vf[0].f[2] + pcVu0.vf[4].f[0]); pcVu0.vf[23].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 2); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    {

        mfc1     t0, $f12
        nop
        
        qmtc2    t0, $vf4

        vaddx.z  $vf23, $vf0, $vf4x
        
    }
#endif
    
    _Make_ClipMatrix(ClipMatrix2, fVu1Projection, _fNaViwClipNear, _fNaViwClipFar);
}

// 100% matching!
void vu1SetNearFarClip(float fNear, float fFar)
{
    fVu1NearClip = fNear;
    fVu1FarClip = fFar;
    
    fVu1InvNearClip = 1.0f / fVu1NearClip;

#ifdef TARGET_PC
    {
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_12 = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_13 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        fr_12.f = fNear;
        fr_13.f = fFar;
        g_t0.d[0] = A2C_SX32(fr_12.u);
        g_t1.d[0] = A2C_SX32(fr_13.u);
        memcpy(&pcVu0.vf[4], &g_t0, 16);
        memcpy(&pcVu0.vf[5], &g_t1, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[4].f[0]); pcVu0.vf[23].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[1] = vu_f(pcVu0.vf[0].f[1] + pcVu0.vf[5].f[0]); pcVu0.vf[23].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 4); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    {
        
        mfc1     t0, $f12
        mfc1     t1, $f13

        qmtc2    t0, $vf4
        qmtc2    t1, $vf5

        vaddx.x  $vf23x, $vf0x, $vf4x
        vaddx.y  $vf23y, $vf0y, $vf5x
        
    }
#endif
    
    _Make_ClipMatrix(ClipMatrix2, fVu1Projection, _fNaViwClipNear, _fNaViwClipFar);
}

// 100% matching!
void vu1SetScreenOffset(float fOffsetX, float fOffsetY)
{
    fVu1OffsetX = fOffsetX;
    fVu1OffsetY = fOffsetY;
    
#ifdef TARGET_PC
    {
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_12 = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_13 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        fr_12.f = fOffsetX;
        fr_13.f = fOffsetY;
        g_t0.d[0] = A2C_SX32(fr_12.u);
        g_t1.d[0] = A2C_SX32(fr_13.u);
        memcpy(&pcVu0.vf[4], &g_t0, 16);
        memcpy(&pcVu0.vf[5], &g_t1, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[4].f[0]); pcVu0.vf[16].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[1] = vu_f(pcVu0.vf[0].f[1] + pcVu0.vf[5].f[0]); pcVu0.vf[16].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 4); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    {
        
        mfc1     t0, $f12
        mfc1     t1, $f13

        qmtc2    t0, $vf4
        qmtc2    t1, $vf5

        vaddx.x  $vf16, $vf0, $vf4x
        vaddx.y  $vf16, $vf0, $vf5x
        
    }
#endif
}

// 100% matching!
void vu1SetScreenAspect(float fAspectW, float fAspectH)
{
    fVu1AspectW = fAspectW;
    fVu1AspectH = fAspectH;

#ifdef TARGET_PC
    {
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_12 = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_13 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        fr_12.f = fAspectW;
        fr_13.f = fAspectH;
        g_t0.d[0] = A2C_SX32(fr_12.u);
        g_t1.d[0] = A2C_SX32(fr_13.u);
        memcpy(&pcVu0.vf[4], &g_t0, 16);
        memcpy(&pcVu0.vf[5], &g_t1, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[4].f[0]); pcVu0.vf[17].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[1] = vu_f(pcVu0.vf[0].f[1] + pcVu0.vf[5].f[0]); pcVu0.vf[17].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 4); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    {
        
        mfc1     t0, $f12
        mfc1     t1, $f13

        qmtc2    t0, $vf4
        qmtc2    t1, $vf5

        vaddx.x  $vf17x, $vf0x, $vf4x
        vaddx.y  $vf17y, $vf0y, $vf5x
        
    }
#endif
}

// 100% matching!
void vu1SetDiffuseMaterial(VU1_COLOR* pDiffuse)
{
    vu1Diffuse.fR = 128.0f * pDiffuse->fR;
    vu1Diffuse.fG = 128.0f * pDiffuse->fG;
    vu1Diffuse.fB = 128.0f * pDiffuse->fB;
    
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_t2 = { 0 };
        PcGpr g_t3 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_op1 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pDiffuse));
        fr_op1.f = (float)(128.0f);
        memcpy(&g_t3, A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(fr_op1.u);
        g_t1.d[0] = A2C_SX32(fr_op1.u);
        g_t2.d[0] = A2C_SX32(fr_op1.u);
        { PcGpr r_; r_.w[0] = g_t0.w[0]; r_.w[1] = g_t1.w[0]; r_.w[2] = g_t0.w[1]; r_.w[3] = g_t1.w[1]; g_t0 = r_; }
        { PcGpr r_; r_.d[0] = g_t0.d[0]; r_.d[1] = g_t2.d[0]; g_t0 = r_; }
        memcpy(&pcVu0.vf[4], &g_t3, 16);
        memcpy(&pcVu0.vf[5], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[0].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[0].f[0]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[0].f[0]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] + pcVu0.vf[0].f[0]); pcVu0.vf[20].f[0] = r_.f[0]; pcVu0.vf[20].f[1] = r_.f[1]; pcVu0.vf[20].f[2] = r_.f[2]; pcVu0.vf[20].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lq         t3, VU1_COLOR.fR(%0)
    
        mfc1       t0, %1
        mfc1       t1, %1
        mfc1       t2, %1
        
        pextlw     t0, t1, t0 
        pcpyld     t0, t2, t0 
    
        qmtc2      t3, $vf4 
        qmtc2      t0, $vf5 
    
        vmul.xyz   $vf4,  $vf4, $vf5 
        vaddx.xyzw $vf20, $vf4, $vf0x 
    " : : "r"(pDiffuse), "f"(128.0f) : "t0"
    );
#endif
}

// 100% matching!
void vu1SetSpeculaMaterial(VU1_COLOR* pSpecula)
{
    vu1Specula.fR = 31.0f * pSpecula->fR;
    vu1Specula.fG = 31.0f * pSpecula->fG;
    vu1Specula.fB = 31.0f * pSpecula->fB;
    
#ifdef TARGET_PC
    {
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_t2 = { 0 };
        PcGpr g_t3 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        fr_op0.f = (float)(31.0f);
        g_t0.d[0] = A2C_SX32(fr_op0.u);
        g_t1.d[0] = A2C_SX32(fr_op0.u);
        g_t2.d[0] = A2C_SX32(fr_op0.u);
        { PcGpr r_; r_.w[0] = g_t0.w[0]; r_.w[1] = g_t1.w[0]; r_.w[2] = g_t0.w[1]; r_.w[3] = g_t1.w[1]; g_t0 = r_; }
        { PcGpr r_; r_.d[0] = g_t0.d[0]; r_.d[1] = g_t2.d[0]; g_t0 = r_; }
        memcpy(&pcVu0.vf[4], &g_t3, 16);
        memcpy(&pcVu0.vf[5], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[0].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[0].f[0]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[0].f[0]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] + pcVu0.vf[0].f[0]); pcVu0.vf[21].f[0] = r_.f[0]; pcVu0.vf[21].f[1] = r_.f[1]; pcVu0.vf[21].f[2] = r_.f[2]; pcVu0.vf[21].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        mfc1       t0, %0
        mfc1       t1, %0
        mfc1       t2, %0
        
        pextlw     t0, t1, t0 
        pcpyld     t0, t2, t0 
    
        qmtc2      t3, $vf4 
        qmtc2      t0, $vf5 
    
        vmul.xyz   $vf4,  $vf4, $vf5 
        vaddx.xyzw $vf21, $vf4, $vf0x 
    " : : "f"(31.0f) : "t0", "t1", "t2"
    );
#endif
}

// 100% matching!
void vu1SetAmbient(VU1_COLOR* pAmbient)
{
    vu1Ambient.fR = pAmbient->fR;
    vu1Ambient.fG = pAmbient->fG;
    vu1Ambient.fB = pAmbient->fB;
    
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pAmbient));
        memcpy(&pcVu0.vf[22], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile
    ("
        lqc2 vf22, VU1_COLOR.fR(%0)
    " : : "r"(pAmbient) : 
    );
#endif
}

// 100% matching!
void vu1SetAlphaRatio(float fAlpha)
{
    fVu1AlphaRatio = 128.0f * fAlpha;

#ifdef TARGET_PC
    {
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        fr_op0.f = (float)(fVu1AlphaRatio);
        g_t0.d[0] = A2C_SX32(fr_op0.u);
        memcpy(&pcVu0.vf[4], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[4].f[0]); pcVu0.vf[23].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("

        mfc1    t0, %0       
        qmtc2   t0, $vf4 

        vmulx.w $vf23, $vf0, $vf4x

    " : : "f"(fVu1AlphaRatio) : "t0"
    );
#endif
}

#include "ps2_Vu1Scissor2.c"

// 100% matching!
void InitNodeArraySet(SCISSOR* scissor)
{
    scissor->rotflag = 0;
    scissor->flipflag = 0;

#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_t2 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(scissor));
        g_t0.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)(g_op0.w[0]));
        g_t2.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)((int32_t)(3)));
        g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(offsetof(SCISSOR, narray))));
        g_t1.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(sizeof(SCISSOR_NODE))));
        *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, in))))) = (uint32_t)g_t0.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, out))))) = (uint32_t)g_t1.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_t0.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_zero.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_zero.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_t2.d[0];
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        add  t0, zero, %0
        
        addi t2, zero, 3
        
        addi t0, t0, SCISSOR.narray
        addi t1, t0, sizeof(SCISSOR_NODE)
        
        sw   t0, SCISSOR.in(%0)
        sw   t1, SCISSOR.out(%0)
        
        sw   zero, SCISSOR_NODE.nodeNum(t0)
        sw   zero, SCISSOR_NODE.nodeNum(t1)
        
        sw   t2, SCISSOR_NODE.nodeNum(%0)
    " : : "r"(scissor) : 
    );
#endif
}

// 100% matching!
static void InitScissorPlane(SCISSOR_PLANE* sp)
{
    sp->planeNum = 5;
    
    sp->plane[0].clipmask = 0x820;
    sp->plane[0].xyzflag = 0x12;
    
    sp->plane[1].clipmask = 0x208;
    sp->plane[1].xyzflag = 0x11;
    
    sp->plane[2].clipmask = 0x104;
    sp->plane[2].xyzflag = 0x1;
    
    sp->plane[3].clipmask = 0x82;
    sp->plane[3].xyzflag = 0x10;
    
    sp->plane[4].clipmask = 0x41;
    sp->plane[4].xyzflag = 0;
}

// 100% matching!
void _Init_ScissorSystem()
{
    InitScissorPlane(&planeset);
    InitNodeArraySet(&scissorflip);
}

// 100% matching! 
int _Clip_ViewVolume(float* clip, float local_clip[4], float* vertex)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_v0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(clip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(vertex));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op1.w[0] + (0))) & ~15u), 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[24].f[0] * pcVu0.vf[4].f[0]); r_.f[1] = vu_f(pcVu0.vf[24].f[1] * pcVu0.vf[4].f[0]); r_.f[2] = vu_f(pcVu0.vf[24].f[2] * pcVu0.vf[4].f[0]); r_.f[3] = vu_f(pcVu0.vf[24].f[3] * pcVu0.vf[4].f[0]); pcVu0.acc.f[0] = r_.f[0]; pcVu0.acc.f[1] = r_.f[1]; pcVu0.acc.f[2] = r_.f[2]; pcVu0.acc.f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.acc.f[0] + pcVu0.vf[25].f[0] * pcVu0.vf[4].f[1]); r_.f[1] = vu_f(pcVu0.acc.f[1] + pcVu0.vf[25].f[1] * pcVu0.vf[4].f[1]); r_.f[2] = vu_f(pcVu0.acc.f[2] + pcVu0.vf[25].f[2] * pcVu0.vf[4].f[1]); r_.f[3] = vu_f(pcVu0.acc.f[3] + pcVu0.vf[25].f[3] * pcVu0.vf[4].f[1]); pcVu0.acc.f[0] = r_.f[0]; pcVu0.acc.f[1] = r_.f[1]; pcVu0.acc.f[2] = r_.f[2]; pcVu0.acc.f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.acc.f[0] + pcVu0.vf[26].f[0] * pcVu0.vf[4].f[2]); r_.f[1] = vu_f(pcVu0.acc.f[1] + pcVu0.vf[26].f[1] * pcVu0.vf[4].f[2]); r_.f[2] = vu_f(pcVu0.acc.f[2] + pcVu0.vf[26].f[2] * pcVu0.vf[4].f[2]); r_.f[3] = vu_f(pcVu0.acc.f[3] + pcVu0.vf[26].f[3] * pcVu0.vf[4].f[2]); pcVu0.acc.f[0] = r_.f[0]; pcVu0.acc.f[1] = r_.f[1]; pcVu0.acc.f[2] = r_.f[2]; pcVu0.acc.f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.acc.f[0] + pcVu0.vf[27].f[0] * pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.acc.f[1] + pcVu0.vf[27].f[1] * pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.acc.f[2] + pcVu0.vf[27].f[2] * pcVu0.vf[0].f[3]); r_.f[3] = vu_f(pcVu0.acc.f[3] + pcVu0.vf[27].f[3] * pcVu0.vf[0].f[3]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        vu_clip(&pcVu0, &pcVu0.vf[5], pcVu0.vf[5].f[3]);
        memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (0))) & ~15u), &pcVu0.vf[5], 16);
        g_v0.d[0] = A2C_SX32(a2c_cfc2(18));
        (void)g_zero; (void)lo_; (void)fcc_;
        return (int)g_v0.w[0];
    }
#else
    asm volatile
    ("
        lqc2         vf4, 0(%1)
            
        vmulax.xyzw  ACC, vf24, vf4x
            
        vmadday.xyzw ACC, vf25, vf4y
        vmaddaz.xyzw ACC, vf26, vf4z
            
        vmaddw.xyzw  vf5, vf27, vf0w
            
        vclipw.xyz   vf5xyz, vf5w
        vnop
        vnop
        vnop
        vnop
        
        sqc2         vf5, 0(%0)
        
        cfc2         v0, vi18
    " : : "r"(clip), "r"(vertex) : 
    );
#endif
}

// 100% matching!
void PushTriangleNodeArray(SCISSOR* scissor, NODE* nod)
{
#ifdef TARGET_PC
    {
        PcGpr g_at = { 0 };
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_t2 = { 0 };
        PcGpr g_v1 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(scissor));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&node));
        g_v1.d[0] = g_op1.d[0];
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, rotflag))))));
        g_at.d[0] = (g_zero.d[0] + (uint64_t)(int64_t)((int64_t)(sizeof(NODE))));
        lo_ = A2C_SX32((uint32_t)((int64_t)(int32_t)g_t0.w[0] * (int32_t)g_at.w[0]));
        g_t1.d[0] = lo_;
        g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
        g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)(g_op0.w[0]));
        g_t0.d[0] = (g_t0.d[0] & (uint64_t)(uint16_t)(0x3));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_v1.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_v1.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_v1.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_v1.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
        memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(SCISSOR, triangle.node[0].vertex)))) & ~15u), &pcVu0.vf[4], 16);
        memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(SCISSOR, triangle.node[0].color)))) & ~15u), &pcVu0.vf[5], 16);
        memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(SCISSOR, triangle.node[0].texUV)))) & ~15u), &pcVu0.vf[6], 16);
        memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(SCISSOR, triangle.node[0].clipV)))) & ~15u), &pcVu0.vf[7], 16);
        g_t2.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(-3)));
        { int c_ = ((int64_t)g_t2.d[0] < 0);  if (c_) goto Vu1Strip_a10_l_002D4150; }
        g_t0.d[0] = A2C_SX32(0u - g_zero.w[0]);
        Vu1Strip_a10_l_002D4150:;
        *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, rotflag))))) = (uint32_t)g_t0.d[0];
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        move   v1, %1
        
        lw     t0, SCISSOR.rotflag(%0)
        
        daddiu at, zero, sizeof(NODE)
    
        mult   t0, at
        nop
        nop
    
        mflo   t1
    
        addi   t0, t0, 1
        add    t1, t1, %0
        
        andi   t0, t0, 0x3
    
        lqc2   vf4, NODE.vertex(v1)
        lqc2   vf5, NODE.color(v1)
        lqc2   vf6, NODE.texUV(v1)
        lqc2   vf7, NODE.clipV(v1)
        
        sqc2   vf4, SCISSOR.triangle.node[0].vertex(t1)
        sqc2   vf5, SCISSOR.triangle.node[0].color(t1)
        sqc2   vf6, SCISSOR.triangle.node[0].texUV(t1)
        sqc2   vf7, SCISSOR.triangle.node[0].clipV(t1)
        
        addi   t2, t0, -3
        
        bltz   t2, l_002D4150
        nop
    
        neg    t0, zero
        
        l_002D4150:
        sw     t0, SCISSOR.rotflag(%0)
    " : : "r"(scissor), "r"(&node) : "v1"
    );
#endif
}

// 100% matching!
void ResetNodeArraySet(SCISSOR* scissor)
{
    scissor->flipflag = 0;

#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_t2 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(scissor));
        g_t0.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)(g_op0.w[0]));
        g_t2.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)((int32_t)(3)));
        g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(offsetof(SCISSOR, narray))));
        g_t1.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(sizeof(SCISSOR_NODE))));
        *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, in))))) = (uint32_t)g_t0.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, out))))) = (uint32_t)g_t1.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_t0.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_zero.d[0];
        *(uint32_t*)A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_zero.d[0];
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        add  t0, zero, %0
        
        addi t2, zero, 3
        
        addi t0, t0, SCISSOR.narray
        addi t1, t0, sizeof(SCISSOR_NODE)
        
        sw   t0, SCISSOR.in(%0)
        sw   t1, SCISSOR.out(%0)
        
        sw   zero, SCISSOR_NODE.nodeNum(t0)
        sw   zero, SCISSOR_NODE.nodeNum(t1)
    " : : "r"(scissor) : 
    );
#endif
}

// 100% matching! 
void ScissorTriangle(SCISSOR* scissor, SCISSOR_PLANE* plane_set)
{
    int i;                 
    int j;                  
    int clip;               
    int mask;               
    int xyz;                
    float sgn;              
    SCISSOR_NODE* inarray;  
    SCISSOR_NODE* outarray; 
    NODE* currN;            
    NODE* nextN;            
    NODE interN;            
    int n;                 

    nextN = NULL;
    currN = NULL;

    sgn = 0;

    clip = 0;
    
    inarray = &scissor->triangle;
    outarray = scissor->out;
    
    for (i = 0; i < plane_set->planeNum; i++) 
    {
#ifdef TARGET_PC
        {
            PcGpr g_op1 = { 0 };
            PcGpr g_op2 = { 0 };
            PcGpr g_op3 = { 0 };
            PcGpr g_op4 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_t1 = { 0 };
            PcGpr g_t2 = { 0 };
            PcGpr g_t3 = { 0 };
            PcGpr g_t4 = { 0 };
            PcGpr g_v0 = { 0 };
            PcGpr g_zero = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            fr_op0.f = (float)(sgn);
            g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(plane_set));
            g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&mask));
            g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&xyz));
            g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&i));
            g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op4.w[0] + (0)))));
            lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(PLANE)))); g_t0.d[0] = lo_;
            g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)(g_op1.w[0]));
            g_t1.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_t0.w[0] + (offsetof(PLANE, xyzflag))))));
            g_t4.d[0] = (g_t1.d[0] & (uint64_t)(uint16_t)(16));
            g_t2.d[0] = (uint64_t)(int64_t)(int32_t)(-1);
            g_t3.d[0] = (uint64_t)(int64_t)(int32_t)(1);
            if (g_t4.d[0] == 0) { g_t2.d[0] = g_t3.d[0]; }
            memcpy(&pcVu0.vf[4], &g_t2, 16);
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = (float)pcVu0.vf[4].i[0] / 1.0f; r_.f[1] = (float)pcVu0.vf[4].i[1] / 1.0f; r_.f[2] = (float)pcVu0.vf[4].i[2] / 1.0f; r_.f[3] = (float)pcVu0.vf[4].i[3] / 1.0f; pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; }
            g_t2.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_t0.w[0] + (offsetof(PLANE, clipmask))))));
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (0)))) = (uint32_t)g_t2.d[0];
            g_t1.d[0] = (g_t1.d[0] & (uint64_t)(uint16_t)(0xF));
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (0)))) = (uint32_t)g_t1.d[0];
            memcpy(&g_v0, &pcVu0.vf[4], 16);
            fr_op0.u = g_v0.w[0];
            sgn = fr_op0.f;
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile
        ("
            lw          t0, 0(%4)
            
            muli        t0, t0, sizeof(PLANE)
        
            add         t0, t0, %1
        
            lw          t1, PLANE.xyzflag(t0)
            
            andi        t4, t1, 16
        
            li          t2, -1 
            li          t3,  1 
            
            movz        t2, t3, t4
            
            qmtc2       t2, vf4
        
            vitof0.xyzw vf4, vf4
            
            lw          t2, PLANE.clipmask(t0)
        
            sw          t2, 0(%2)
            
            andi        t1, t1, 0xF
        
            sw          t1, 0(%3)
            
            qmfc2       v0, vf4
        
            mtc1        v0, %0
        " : "=r"(sgn) : "r"(plane_set), "r"(&mask), "r"(&xyz), "r"(&i) :
        ); 
#endif
        
        for (j = 0; j < inarray->nodeNum; j++) 
        {
#ifdef TARGET_PC
            {
                PcGpr g_op0 = { 0 };
                PcGpr g_op1 = { 0 };
                PcGpr g_op2 = { 0 };
                PcGpr g_op3 = { 0 };
                PcGpr g_t0 = { 0 };
                PcGpr g_t1 = { 0 };
                PcGpr g_t2 = { 0 };
                PcGpr g_t3 = { 0 };
                PcGpr g_t4 = { 0 };
                PcGpr g_zero = { 0 };
                uint64_t lo_ = 0; int fcc_ = 0;
                g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(currN));
                g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(nextN));
                g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(inarray));
                g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&j));
                g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (0)))));
                g_t1.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
                lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(NODE)))); g_t2.d[0] = lo_;
                g_t3.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
                g_t4.d[0] = A2C_SX32(g_t3.w[0] - g_t1.w[0]);
                { int c_ = ((int64_t)g_t4.d[0] < 0);  if (c_) goto Vu1Strip_a13_l_002D4244; }
                g_t3.d[0] = A2C_SX32(0u - g_zero.w[0]);
                Vu1Strip_a13_l_002D4244:;
                lo_ = A2C_SX32((uint32_t)((int32_t)g_t3.w[0] * (int32_t)(sizeof(NODE)))); g_t3.d[0] = lo_;
                g_op0.d[0] = A2C_SX32(g_t2.w[0] + (uint32_t)(g_op2.w[0]));
                g_op1.d[0] = A2C_SX32(g_t3.w[0] + (uint32_t)(g_op2.w[0]));
                *(uint32_t*)&(currN) = g_op0.w[0];
                *(uint32_t*)&(nextN) = g_op1.w[0];
                (void)g_zero; (void)lo_; (void)fcc_;
            }
#else
            asm volatile
            ("
                lw   t0, 0(%3)
                lw   t1, SCISSOR_NODE.nodeNum(%2)
                
                muli t2, t0, sizeof(NODE)
            
                addi t3, t0, 1 
            
                sub  t4, t3, t1 
                
                bltz t4, l_002D4244
                vnop
            
                neg  t3, zero
                
            l_002D4244:
                muli t3, t3, sizeof(NODE)
                                    
                add  %0, t2, %2
                add  %1, t3, %2 
            " : "=r"(currN), "=r"(nextN) : "r"(inarray), "r"(&j)
            );
#endif
                
#ifdef TARGET_PC
            {
                PcGpr g_op0 = { 0 };
                PcGpr g_op1 = { 0 };
                PcGpr g_op2 = { 0 };
                PcGpr g_zero = { 0 };
                uint64_t lo_ = 0; int fcc_ = 0;
                g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(clip));
                g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(currN));
                g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(nextN));
                memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                vu_clip(&pcVu0, &pcVu0.vf[4], pcVu0.vf[4].f[3]);
                vu_clip(&pcVu0, &pcVu0.vf[5], pcVu0.vf[5].f[3]);
                g_op0.d[0] = A2C_SX32(a2c_cfc2(18));
                *(uint32_t*)&(clip) = g_op0.w[0];
                (void)g_zero; (void)lo_; (void)fcc_;
            }
#else
            asm volatile
            ("
                lqc2       vf4, NODE.clipV(%1)
                lqc2       vf5, NODE.clipV(%2) 
            
                vclipw.xyz vf4, vf4                    
                vclipw.xyz vf5, vf5                   
                vnop
                vnop
                vnop
                vnop
                vnop
            
                cfc2       %0, vi18
            " : "=r"(clip) : "r"(currN), "r"(nextN)
            );
#endif
            
            clip &= mask;
            
            if (clip == 0) 
            {
#ifdef TARGET_PC
                {
                    PcGpr g_op2 = { 0 };
                    PcGpr g_op4 = { 0 };
                    PcGpr g_t0 = { 0 };
                    PcGpr g_t1 = { 0 };
                    PcGpr g_zero = { 0 };
                    uint64_t lo_ = 0; int fcc_ = 0;
                    g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(outarray));
                    g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(currN));
                    g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
                    lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(NODE)))); g_t1.d[0] = lo_;
                    g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)(g_op2.w[0]));
                    memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, vertex)))) & ~15u), &pcVu0.vf[4], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, color)))) & ~15u), &pcVu0.vf[5], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, texUV)))) & ~15u), &pcVu0.vf[6], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, clipV)))) & ~15u), &pcVu0.vf[7], 16);
                    g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
                    *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_t0.d[0];
                    (void)g_zero; (void)lo_; (void)fcc_;
                }
#else
                asm volatile
                ("  
                    lw   t0, SCISSOR_NODE.nodeNum(%2)
                    
                    muli t1, t0, sizeof(NODE)
                
                    add  t1, t1, %2 
                
                    lqc2 vf4, NODE.vertex(%4)
                    lqc2 vf5, NODE.color(%4)
                    lqc2 vf6, NODE.texUV(%4)
                    lqc2 vf7, NODE.clipV(%4)
                    
                    sqc2 vf4, NODE.vertex(t1)
                    sqc2 vf5, NODE.color(t1)
                    sqc2 vf6, NODE.texUV(t1)
                    sqc2 vf7, NODE.clipV(t1)
                    
                    addi t0, t0, 1 
                
                    sw   t0, SCISSOR_NODE.nodeNum(%2)
                " : : "r"(inarray), "r"(&j), "r"(outarray), "r"(mask), "r"(currN)
                );
#endif
            } 
            else if (((clip & 0x3F)) && ((clip & 0xFC0)))
            {
                
            } 
            else if (((clip & 0x3F)) && (!(clip & 0xFC0)))
            {
#ifdef TARGET_PC
                {
                    PcGpr g_op1 = { 0 };
                    PcGpr g_op2 = { 0 };
                    PcGpr g_op3 = { 0 };
                    PcGpr g_op4 = { 0 };
                    PcGpr g_t0 = { 0 };
                    PcGpr g_t1 = { 0 };
                    PcGpr g_t2 = { 0 };
                    PcGpr g_zero = { 0 };
                    union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
                    uint64_t lo_ = 0; int fcc_ = 0;
                    fr_op0.f = (float)(sgn);
                    g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&interN));
                    g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(xyz));
                    g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(currN));
                    g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(nextN));
                    memcpy(&pcVu0.vf[8], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[9], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    g_t0.d[0] = A2C_SX32(fr_op0.u);
                    memcpy(&pcVu0.vf[10], &g_t0, 16);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[8].f[3] * pcVu0.vf[10].f[0]); pcVu0.vf[11].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[9].f[3] * pcVu0.vf[10].f[0]); pcVu0.vf[12].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[8].f[0] - pcVu0.vf[11].f[3]); r_.f[1] = vu_f(pcVu0.vf[8].f[1] - pcVu0.vf[11].f[3]); r_.f[2] = vu_f(pcVu0.vf[8].f[2] - pcVu0.vf[11].f[3]); r_.f[3] = vu_f(pcVu0.vf[8].f[3] - pcVu0.vf[11].f[3]); pcVu0.vf[11].f[0] = r_.f[0]; pcVu0.vf[11].f[1] = r_.f[1]; pcVu0.vf[11].f[2] = r_.f[2]; pcVu0.vf[11].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] - pcVu0.vf[12].f[3]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] - pcVu0.vf[12].f[3]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] - pcVu0.vf[12].f[3]); r_.f[3] = vu_f(pcVu0.vf[9].f[3] - pcVu0.vf[12].f[3]); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; pcVu0.vf[12].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = fabsf(pcVu0.vf[11].f[0]); r_.f[1] = fabsf(pcVu0.vf[11].f[1]); r_.f[2] = fabsf(pcVu0.vf[11].f[2]); r_.f[3] = fabsf(pcVu0.vf[11].f[3]); pcVu0.vf[11].f[0] = r_.f[0]; pcVu0.vf[11].f[1] = r_.f[1]; pcVu0.vf[11].f[2] = r_.f[2]; pcVu0.vf[11].f[3] = r_.f[3]; }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = fabsf(pcVu0.vf[12].f[0]); r_.f[1] = fabsf(pcVu0.vf[12].f[1]); r_.f[2] = fabsf(pcVu0.vf[12].f[2]); r_.f[3] = fabsf(pcVu0.vf[12].f[3]); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; pcVu0.vf[12].f[3] = r_.f[3]; }
                    memcpy(&g_t1, &pcVu0.vf[11], 16);
                    memcpy(&g_t2, &pcVu0.vf[12], 16);
                    Vu1Strip_a16_l_002D4350:;
                    { int c_ = (g_op2.d[0] == 0);  if (c_) goto Vu1Strip_a16_l_002D4368; }
                    { PcGpr r_ = g_t1; r_.w[0] = g_t1.w[1]; r_.w[1] = g_t1.w[2]; r_.w[2] = g_t1.w[0]; g_t1 = r_; }
                    { PcGpr r_ = g_t2; r_.w[0] = g_t2.w[1]; r_.w[1] = g_t2.w[2]; r_.w[2] = g_t2.w[0]; g_t2 = r_; }
                    g_op2.d[0] = A2C_SX32(g_op2.w[0] + (uint32_t)((int32_t)(-1)));
                    { int c_ = 1;  if (c_) goto Vu1Strip_a16_l_002D4350; }
                    Vu1Strip_a16_l_002D4368:;
                    memcpy(&pcVu0.vf[11], &g_t1, 16);
                    memcpy(&pcVu0.vf[12], &g_t2, 16);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] - pcVu0.vf[8].f[0]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] - pcVu0.vf[8].f[1]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] - pcVu0.vf[8].f[2]); r_.f[3] = vu_f(pcVu0.vf[9].f[3] - pcVu0.vf[8].f[3]); pcVu0.vf[13].f[0] = r_.f[0]; pcVu0.vf[13].f[1] = r_.f[1]; pcVu0.vf[13].f[2] = r_.f[2]; pcVu0.vf[13].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[12].f[0] + pcVu0.vf[11].f[0]); r_.f[1] = vu_f(pcVu0.vf[12].f[1] + pcVu0.vf[11].f[1]); r_.f[2] = vu_f(pcVu0.vf[12].f[2] + pcVu0.vf[11].f[2]); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
                    memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    vu_set_q_now(&pcVu0, vu_div(pcVu0.vf[11].f[0], pcVu0.vf[12].f[0]));
                    memcpy(&pcVu0.vf[9], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[10], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[11], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] - pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[6].f[1] - pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[6].f[2] - pcVu0.vf[9].f[2]); r_.f[3] = vu_f(pcVu0.vf[6].f[3] - pcVu0.vf[9].f[3]); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] - pcVu0.vf[10].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] - pcVu0.vf[10].f[1]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] - pcVu0.vf[10].f[2]); r_.f[3] = vu_f(pcVu0.vf[5].f[3] - pcVu0.vf[10].f[3]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] - pcVu0.vf[11].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] - pcVu0.vf[11].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] - pcVu0.vf[11].f[2]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] - pcVu0.vf[11].f[3]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    vu_waitq(&pcVu0);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + vu_q(&pcVu0)); r_.f[1] = vu_f(pcVu0.vf[0].f[1] + vu_q(&pcVu0)); r_.f[2] = vu_f(pcVu0.vf[0].f[2] + vu_q(&pcVu0)); r_.f[3] = vu_f(pcVu0.vf[0].f[3] + vu_q(&pcVu0)); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; pcVu0.vf[12].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = fabsf(pcVu0.vf[12].f[0]); pcVu0.vf[12].f[0] = r_.f[0]; }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[13].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[13].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[13].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[13].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; pcVu0.vf[7].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[6].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[6].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[6].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[5].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[8].f[0] + pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[8].f[1] + pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[8].f[2] + pcVu0.vf[7].f[2]); r_.f[3] = vu_f(pcVu0.vf[8].f[3] + pcVu0.vf[7].f[3]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; pcVu0.vf[7].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] + pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[6].f[1] + pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[6].f[2] + pcVu0.vf[9].f[2]); r_.f[3] = vu_f(pcVu0.vf[6].f[3] + pcVu0.vf[9].f[3]); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] + pcVu0.vf[10].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] + pcVu0.vf[10].f[1]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] + pcVu0.vf[10].f[2]); r_.f[3] = vu_f(pcVu0.vf[5].f[3] + pcVu0.vf[10].f[3]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[11].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[11].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[11].f[2]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] + pcVu0.vf[11].f[3]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[2] = vu_f(fmaxf(pcVu0.vf[6].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[6].f[2] = r_.f[2]; }
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, color)))) & ~15u), &pcVu0.vf[5], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, texUV)))) & ~15u), &pcVu0.vf[4], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, clipV)))) & ~15u), &pcVu0.vf[7], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, vertex)))) & ~15u), &pcVu0.vf[6], 16);
                    (void)g_zero; (void)lo_; (void)fcc_;
                }
#else
                asm volatile
                ("
                    lqc2       vf8, NODE.clipV(%3)
                    lqc2       vf9, NODE.clipV(%4)
                    
                    mfc1       t0, %0
                
                    qmtc2      t0, vf10
                    
                    vmulx.w    vf11, vf8, vf10
                    vmulx.w    vf12, vf9, vf10
                    
                    vsubw.xyzw vf11, vf8, vf11
                    vsubw.xyzw vf12, vf9, vf12
                    
                    vabs.xyzw  vf11, vf11
                    vabs.xyzw  vf12, vf12
                    
                    qmfc2      t1, vf11
                    qmfc2      t2, vf12
                    nop
                
                l_002D4350:   
                    beqz       %2, l_002D4368
                        
                    prot3w     t1, t1
                    prot3w     t2, t2
                
                    addi       %2, -1
                    j l_002D4350  
                    
                l_002D4368:    
                    qmtc2      t1, vf11
                    qmtc2      t2, vf12
                
                    vsub.xyzw  vf13, vf9, vf8
                    
                    vadd.xyz   vf12, vf12, vf11
                    
                    lqc2       vf6, NODE.vertex(%4)
                    lqc2       vf5, NODE.color(%4)
                    lqc2       vf4, NODE.texUV(%4)
                    
                    vdiv       Q, vf11, vf12
                
                    lqc2       vf9,  NODE.vertex(%3)
                    lqc2       vf10, NODE.color(%3)
                    lqc2       vf11, NODE.texUV(%3)
                    
                    vsub.xyzw  vf6, vf6, vf9
                    vsub.xyzw  vf5, vf5, vf10
                    vsub.xyzw  vf4, vf4, vf11
                    
                    vwaitq
                    
                    vaddq.xyzw vf12, vf0, Q
                    
                    vabs.x     vf12, vf12
                    
                    vmulx.xyzw vf7, vf13, vf12
                    vmulx.xyzw vf6, vf6,  vf12
                    vmulx.xyzw vf5, vf5,  vf12
                    vmulx.xyzw vf4, vf4,  vf12
                    
                    vadd.xyzw  vf7, vf8, vf7
                    vadd.xyzw  vf6, vf6, vf9
                    vadd.xyzw  vf5, vf5, vf10
                    vadd.xyzw  vf4, vf4, vf11
                    
                    vmaxw.z    vf6, vf6, vf0
                    
                    sqc2       vf5, NODE.color(%1)
                    sqc2       vf4, NODE.texUV(%1)
                    sqc2       vf7, NODE.clipV(%1)
                    sqc2       vf6, NODE.vertex(%1)
                " : : "r"(sgn), "r"(&interN), "r"(xyz), "r"(currN), "r"(nextN)
                );
#endif
                    
#ifdef TARGET_PC
                {
                    PcGpr g_op2 = { 0 };
                    PcGpr g_op3 = { 0 };
                    PcGpr g_t0 = { 0 };
                    PcGpr g_t1 = { 0 };
                    PcGpr g_zero = { 0 };
                    uint64_t lo_ = 0; int fcc_ = 0;
                    g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(outarray));
                    g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(currN));
                    g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
                    lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(NODE)))); g_t1.d[0] = lo_;
                    g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)(g_op2.w[0]));
                    memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, vertex)))) & ~15u), &pcVu0.vf[4], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, color)))) & ~15u), &pcVu0.vf[5], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, texUV)))) & ~15u), &pcVu0.vf[6], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, clipV)))) & ~15u), &pcVu0.vf[7], 16);
                    g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
                    *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_t0.d[0];
                    (void)g_zero; (void)lo_; (void)fcc_;
                }
#else
                asm volatile
                ("
                    lw   t0, SCISSOR_NODE.nodeNum(%2)
                
                    muli t1, t0, sizeof(NODE)
                    
                    add  t1, t1, %2 
                
                    lqc2 vf4, NODE.vertex(%3)
                    lqc2 vf5, NODE.color(%3)
                    lqc2 vf6, NODE.texUV(%3)
                    lqc2 vf7, NODE.clipV(%3)
                    
                    sqc2 vf4, NODE.vertex(t1)
                    sqc2 vf5, NODE.color(t1)
                    sqc2 vf6, NODE.texUV(t1)
                    sqc2 vf7, NODE.clipV(t1)
                    
                    addi t0, t0, 1 
                
                    sw   t0, SCISSOR_NODE.nodeNum(%2)
                " : : "r"(&interN), "r"(xyz), "r"(outarray), "r"(currN)
                );
#endif
                    
#ifdef TARGET_PC
                {
                    PcGpr g_op0 = { 0 };
                    PcGpr g_op2 = { 0 };
                    PcGpr g_t0 = { 0 };
                    PcGpr g_t1 = { 0 };
                    PcGpr g_zero = { 0 };
                    uint64_t lo_ = 0; int fcc_ = 0;
                    g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&interN));
                    g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(outarray));
                    g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
                    lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(NODE)))); g_t1.d[0] = lo_;
                    g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)(g_op2.w[0]));
                    memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, vertex)))) & ~15u), &pcVu0.vf[4], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, color)))) & ~15u), &pcVu0.vf[5], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, texUV)))) & ~15u), &pcVu0.vf[6], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, clipV)))) & ~15u), &pcVu0.vf[7], 16);
                    g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
                    *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_t0.d[0];
                    (void)g_zero; (void)lo_; (void)fcc_;
                }
#else
                asm volatile
                ("
                    lw   t0, SCISSOR_NODE.nodeNum(%2)
                    
                    muli t1, t0, sizeof(NODE)
                    
                    add  t1, t1, %2 
                
                    lqc2 vf4, NODE.vertex(%0)
                    lqc2 vf5, NODE.color(%0)
                    lqc2 vf6, NODE.texUV(%0)
                    lqc2 vf7, NODE.clipV(%0)
                    
                    sqc2 vf4, NODE.vertex(t1)
                    sqc2 vf5, NODE.color(t1)
                    sqc2 vf6, NODE.texUV(t1)
                    sqc2 vf7, NODE.clipV(t1)
                    
                    addi t0, t0, 1 
                
                    sw   t0, SCISSOR_NODE.nodeNum(%2)
                " : : "r"(&interN), "r"(xyz), "r"(outarray)
                );
#endif
            } 
            else if ((!(clip & 0x3F)) && ((clip & 0xFC0))) 
            {
#ifdef TARGET_PC
                {
                    PcGpr g_op1 = { 0 };
                    PcGpr g_op2 = { 0 };
                    PcGpr g_op3 = { 0 };
                    PcGpr g_op4 = { 0 };
                    PcGpr g_t0 = { 0 };
                    PcGpr g_t1 = { 0 };
                    PcGpr g_t2 = { 0 };
                    PcGpr g_zero = { 0 };
                    union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
                    uint64_t lo_ = 0; int fcc_ = 0;
                    fr_op0.f = (float)(sgn);
                    g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&interN));
                    g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(xyz));
                    g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(nextN));
                    g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(currN));
                    memcpy(&pcVu0.vf[8], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[9], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    g_t0.d[0] = A2C_SX32(fr_op0.u);
                    memcpy(&pcVu0.vf[10], &g_t0, 16);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[8].f[3] * pcVu0.vf[10].f[0]); pcVu0.vf[11].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[9].f[3] * pcVu0.vf[10].f[0]); pcVu0.vf[12].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[8].f[0] - pcVu0.vf[11].f[3]); r_.f[1] = vu_f(pcVu0.vf[8].f[1] - pcVu0.vf[11].f[3]); r_.f[2] = vu_f(pcVu0.vf[8].f[2] - pcVu0.vf[11].f[3]); r_.f[3] = vu_f(pcVu0.vf[8].f[3] - pcVu0.vf[11].f[3]); pcVu0.vf[11].f[0] = r_.f[0]; pcVu0.vf[11].f[1] = r_.f[1]; pcVu0.vf[11].f[2] = r_.f[2]; pcVu0.vf[11].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] - pcVu0.vf[12].f[3]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] - pcVu0.vf[12].f[3]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] - pcVu0.vf[12].f[3]); r_.f[3] = vu_f(pcVu0.vf[9].f[3] - pcVu0.vf[12].f[3]); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; pcVu0.vf[12].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = fabsf(pcVu0.vf[11].f[0]); r_.f[1] = fabsf(pcVu0.vf[11].f[1]); r_.f[2] = fabsf(pcVu0.vf[11].f[2]); r_.f[3] = fabsf(pcVu0.vf[11].f[3]); pcVu0.vf[11].f[0] = r_.f[0]; pcVu0.vf[11].f[1] = r_.f[1]; pcVu0.vf[11].f[2] = r_.f[2]; pcVu0.vf[11].f[3] = r_.f[3]; }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = fabsf(pcVu0.vf[12].f[0]); r_.f[1] = fabsf(pcVu0.vf[12].f[1]); r_.f[2] = fabsf(pcVu0.vf[12].f[2]); r_.f[3] = fabsf(pcVu0.vf[12].f[3]); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; pcVu0.vf[12].f[3] = r_.f[3]; }
                    memcpy(&g_t1, &pcVu0.vf[11], 16);
                    memcpy(&g_t2, &pcVu0.vf[12], 16);
                    Vu1Strip_a19_l_002D44C8:;
                    { int c_ = (g_op2.d[0] == 0);  if (c_) goto Vu1Strip_a19_l_002D44E0; }
                    { PcGpr r_ = g_t1; r_.w[0] = g_t1.w[1]; r_.w[1] = g_t1.w[2]; r_.w[2] = g_t1.w[0]; g_t1 = r_; }
                    { PcGpr r_ = g_t2; r_.w[0] = g_t2.w[1]; r_.w[1] = g_t2.w[2]; r_.w[2] = g_t2.w[0]; g_t2 = r_; }
                    g_op2.d[0] = A2C_SX32(g_op2.w[0] + (uint32_t)((int32_t)(-1)));
                    { int c_ = 1;  if (c_) goto Vu1Strip_a19_l_002D44C8; }
                    Vu1Strip_a19_l_002D44E0:;
                    memcpy(&pcVu0.vf[11], &g_t1, 16);
                    memcpy(&pcVu0.vf[12], &g_t2, 16);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] - pcVu0.vf[8].f[0]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] - pcVu0.vf[8].f[1]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] - pcVu0.vf[8].f[2]); r_.f[3] = vu_f(pcVu0.vf[9].f[3] - pcVu0.vf[8].f[3]); pcVu0.vf[13].f[0] = r_.f[0]; pcVu0.vf[13].f[1] = r_.f[1]; pcVu0.vf[13].f[2] = r_.f[2]; pcVu0.vf[13].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[12].f[0] + pcVu0.vf[11].f[0]); r_.f[1] = vu_f(pcVu0.vf[12].f[1] + pcVu0.vf[11].f[1]); r_.f[2] = vu_f(pcVu0.vf[12].f[2] + pcVu0.vf[11].f[2]); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
                    memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    vu_set_q_now(&pcVu0, vu_div(pcVu0.vf[11].f[0], pcVu0.vf[12].f[0]));
                    memcpy(&pcVu0.vf[9], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[10], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[11], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] - pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[6].f[1] - pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[6].f[2] - pcVu0.vf[9].f[2]); r_.f[3] = vu_f(pcVu0.vf[6].f[3] - pcVu0.vf[9].f[3]); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] - pcVu0.vf[10].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] - pcVu0.vf[10].f[1]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] - pcVu0.vf[10].f[2]); r_.f[3] = vu_f(pcVu0.vf[5].f[3] - pcVu0.vf[10].f[3]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] - pcVu0.vf[11].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] - pcVu0.vf[11].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] - pcVu0.vf[11].f[2]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] - pcVu0.vf[11].f[3]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    vu_waitq(&pcVu0);
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + vu_q(&pcVu0)); r_.f[1] = vu_f(pcVu0.vf[0].f[1] + vu_q(&pcVu0)); r_.f[2] = vu_f(pcVu0.vf[0].f[2] + vu_q(&pcVu0)); r_.f[3] = vu_f(pcVu0.vf[0].f[3] + vu_q(&pcVu0)); pcVu0.vf[12].f[0] = r_.f[0]; pcVu0.vf[12].f[1] = r_.f[1]; pcVu0.vf[12].f[2] = r_.f[2]; pcVu0.vf[12].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = fabsf(pcVu0.vf[12].f[0]); pcVu0.vf[12].f[0] = r_.f[0]; }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[13].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[13].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[13].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[13].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; pcVu0.vf[7].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[6].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[6].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[6].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[5].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[12].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[12].f[0]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[12].f[0]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] * pcVu0.vf[12].f[0]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[8].f[0] + pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[8].f[1] + pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[8].f[2] + pcVu0.vf[7].f[2]); r_.f[3] = vu_f(pcVu0.vf[8].f[3] + pcVu0.vf[7].f[3]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; pcVu0.vf[7].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] + pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[6].f[1] + pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[6].f[2] + pcVu0.vf[9].f[2]); r_.f[3] = vu_f(pcVu0.vf[6].f[3] + pcVu0.vf[9].f[3]); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] + pcVu0.vf[10].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] + pcVu0.vf[10].f[1]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] + pcVu0.vf[10].f[2]); r_.f[3] = vu_f(pcVu0.vf[5].f[3] + pcVu0.vf[10].f[3]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[11].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[11].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[11].f[2]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] + pcVu0.vf[11].f[3]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
                    { VuVec r_ = pcVu0.vf[0]; r_.f[2] = vu_f(fmaxf(pcVu0.vf[6].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[6].f[2] = r_.f[2]; }
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, color)))) & ~15u), &pcVu0.vf[5], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, texUV)))) & ~15u), &pcVu0.vf[4], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, clipV)))) & ~15u), &pcVu0.vf[7], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, vertex)))) & ~15u), &pcVu0.vf[6], 16);
                    (void)g_zero; (void)lo_; (void)fcc_;
                }
#else
                asm volatile
                ("
                    lqc2       vf8, NODE.clipV(%3)
                    lqc2       vf9, NODE.clipV(%4)
                    
                    mfc1       t0, %0
                
                    qmtc2      t0, vf10
                    
                    vmulx.w    vf11, vf8, vf10
                    vmulx.w    vf12, vf9, vf10
                    
                    vsubw.xyzw vf11, vf8, vf11
                    vsubw.xyzw vf12, vf9, vf12
                    
                    vabs.xyzw  vf11, vf11
                    vabs.xyzw  vf12, vf12
                    
                    qmfc2      t1, vf11
                    qmfc2      t2, vf12
                    nop
                
                l_002D44C8:
                    beqz       %2, l_002D44E0
                        
                    prot3w     t1, t1
                    prot3w     t2, t2
                
                    addi       %2, %2, -1 
                    j          l_002D44C8
                    
                l_002D44E0:
                    qmtc2      t1, vf11
                    qmtc2      t2, vf12
                
                    vsub.xyzw  vf13, vf9, vf8
                    
                    vadd.xyz   vf12, vf12, vf11
                    
                    lqc2       vf6, NODE.vertex(%4)
                    lqc2       vf5, NODE.color(%4)
                    lqc2       vf4, NODE.texUV(%4)
                    
                    vdiv       Q, vf11, vf12
                
                    lqc2       vf9,  NODE.vertex(%3)
                    lqc2       vf10, NODE.color(%3)
                    lqc2       vf11, NODE.texUV(%3)
                    
                    vsub.xyzw  vf6, vf6, vf9
                    vsub.xyzw  vf5, vf5, vf10
                    vsub.xyzw  vf4, vf4, vf11
                    
                    vwaitq
                    
                    vaddq.xyzw vf12, vf0, Q
                    
                    vabs.x     vf12, vf12
                    
                    vmulx.xyzw vf7, vf13, vf12
                    vmulx.xyzw vf6, vf6, vf12
                    vmulx.xyzw vf5, vf5, vf12
                    vmulx.xyzw vf4, vf4, vf12
                    
                    vadd.xyzw  vf7, vf8, vf7
                    vadd.xyzw  vf6, vf6, vf9
                    vadd.xyzw  vf5, vf5, vf10
                    vadd.xyzw  vf4, vf4, vf11
                    
                    vmaxw.z    vf6, vf6, vf0
                    
                    sqc2       vf5, NODE.color(%1)
                    sqc2       vf4, NODE.texUV(%1)
                    sqc2       vf7, NODE.clipV(%1)
                    sqc2       vf6, NODE.vertex(%1)
                " : : "r"(sgn), "r"(&interN), "r"(xyz), "r"(nextN), "r"(currN)
                );
#endif
                
#ifdef TARGET_PC
                {
                    PcGpr g_op1 = { 0 };
                    PcGpr g_op2 = { 0 };
                    PcGpr g_t0 = { 0 };
                    PcGpr g_t1 = { 0 };
                    PcGpr g_zero = { 0 };
                    uint64_t lo_ = 0; int fcc_ = 0;
                    g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&interN));
                    g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(outarray));
                    g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
                    lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(NODE)))); g_t1.d[0] = lo_;
                    g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)(g_op2.w[0]));
                    memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, vertex)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, color)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, texUV)))) & ~15u), 16);
                    memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(NODE, clipV)))) & ~15u), 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, vertex)))) & ~15u), &pcVu0.vf[4], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, color)))) & ~15u), &pcVu0.vf[5], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, texUV)))) & ~15u), &pcVu0.vf[6], 16);
                    memcpy(A2C_PTR(((uint32_t)(g_t1.w[0] + (offsetof(NODE, clipV)))) & ~15u), &pcVu0.vf[7], 16);
                    g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
                    *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))) = (uint32_t)g_t0.d[0];
                    (void)g_zero; (void)lo_; (void)fcc_;
                }
#else
                asm volatile
                ("
                    lw   t0, SCISSOR_NODE.nodeNum(%2)
                    
                    muli t1, t0, sizeof(NODE)
                    
                    add  t1, t1, %2 
                    
                    lqc2 vf4, NODE.vertex(%1)
                    lqc2 vf5, NODE.color(%1)
                    lqc2 vf6, NODE.texUV(%1)
                    lqc2 vf7, NODE.clipV(%1)
                    
                    sqc2 vf4, NODE.vertex(t1)
                    sqc2 vf5, NODE.color(t1)
                    sqc2 vf6, NODE.texUV(t1)
                    sqc2 vf7, NODE.clipV(t1)
                    
                    addi t0, t0, 1 
                    
                    sw   t0, SCISSOR_NODE.nodeNum(%2)
                " : : "r"(xyz), "r"(&interN), "r"(outarray)
                );
#endif
            }
        }

#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_t1 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(scissor));
            g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, flipflag))))));
            g_t1.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(1)));
            g_t1.d[0] = (g_t1.d[0] & (uint64_t)(uint16_t)(1));
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, flipflag))))) = (uint32_t)g_t1.d[0];
            lo_ = A2C_SX32((uint32_t)((int32_t)g_t0.w[0] * (int32_t)(sizeof(SCISSOR_NODE)))); g_t0.d[0] = lo_;
            lo_ = A2C_SX32((uint32_t)((int32_t)g_t1.w[0] * (int32_t)(sizeof(SCISSOR_NODE)))); g_t1.d[0] = lo_;
            g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)(g_op0.w[0]));
            g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)(g_op0.w[0]));
            g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(sizeof(SCISSOR_NODE))));
            g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)((int32_t)(sizeof(SCISSOR_NODE))));
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, out))))) = (uint32_t)g_t0.d[0];
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, in))))) = (uint32_t)g_t1.d[0];
            *(uint32_t*)A2C_PTR(((uint32_t)(g_t0.w[0] + (offsetof(SCISSOR, triangle.nodeNum))))) = (uint32_t)g_zero.d[0];
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile
        ("
            lw   t0, SCISSOR.flipflag(%0)
            
            addi t1, t0, 1 
        
            andi t1, t1, 1
            
            sw   t1, SCISSOR.flipflag(%0)
            
            muli t0, t0, sizeof(SCISSOR_NODE)
            muli t1, t1, sizeof(SCISSOR_NODE)
        
            add  t0, t0, %0 
            add  t1, t1, %0 
            
            addi t0, t0, sizeof(SCISSOR_NODE) 
            addi t1, t1, sizeof(SCISSOR_NODE) 
            
            sw   t0,   SCISSOR.out(%0)
            sw   t1,   SCISSOR.in(%0)
            sw   zero, SCISSOR.triangle.nodeNum(t0)
        " : : "r"(scissor)
        );
#endif
                
        inarray = scissor->in;   
        outarray = scissor->out;
    }
}

// 100% matching!
int _Check_DisplayAreaPoint(NJS_VECTOR* vec)
{
    if (((vec->x >= 1.0f) && (vec->x < 4095.0f)) && ((vec->y >= 1.0f) && (vec->y < 4095.0f)) && ((vec->z >= 1.0f) && (vec->z < 65535.0f))) 
    {
        return 0;
    }
    
    return 1;
}

// 100% matching!
void DrawScissorPolygonOpaque(SCISSOR* scissor, unsigned long ulType)
{
    SCISSOR_NODE* in; 
    VU1_PRIM_BUF* pP; // not from the debugging symbols

    in = scissor->in;
    
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t6 = { 0 };
        PcGpr g_t7 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(scissor));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(vu1ScessorBuf));
        g_t6.d[0] = A2C_SX32(g_op0.w[0] + (uint32_t)((int32_t)(offsetof(SCISSOR, in))));
        g_t6.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0]))))));
        g_t7.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)(g_op1.w[0]));
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
        Vu1Strip_a22_l_002D4720:;
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0].vertex)))) & ~15u), 16);
        vu_set_q_now(&pcVu0, vu_div(pcVu0.vf[0].f[3], pcVu0.vf[4].f[2]));
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0].color)))) & ~15u), 16);
        memcpy(A2C_PTR(((uint32_t)(g_t7.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[7], 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[23].f[2]); pcVu0.vf[5].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0].texUV)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(-1)));
        vu_waitq(&pcVu0);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] * vu_q(&pcVu0)); pcVu0.vf[5].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] * vu_q(&pcVu0)); r_.f[1] = vu_f(pcVu0.vf[6].f[1] * vu_q(&pcVu0)); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[2] = vu_f(pcVu0.vf[0].f[2] + vu_q(&pcVu0)); pcVu0.vf[6].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 2); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[6].f[3] - pcVu0.vf[6].f[3]); pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[0]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        memcpy(A2C_PTR(((uint32_t)(g_t7.w[0] + (offsetof(VU1_PRIM_BUF, fS)))) & ~15u), &pcVu0.vf[6], 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[17].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[17].f[1]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[16].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[16].f[1]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        g_t6.d[0] = A2C_SX32(g_t6.w[0] + (uint32_t)((int32_t)(sizeof(NODE))));
        memcpy(A2C_PTR(((uint32_t)(g_t7.w[0] + (offsetof(VU1_PRIM_BUF, fX)))) & ~15u), &pcVu0.vf[4], 16);
        g_t7.d[0] = A2C_SX32(g_t7.w[0] + (uint32_t)((int32_t)(sizeof(VU1_PRIM_BUF))));
        { int c_ = (g_t0.d[0] != 0);  if (c_) goto Vu1Strip_a22_l_002D4720; }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile
    ("
        addi     t6, %0, SCISSOR.in
    
        lw       t6, SCISSOR.triangle.node[0](t6)
        
        add      t7, zero, %1
    
        lw       t0, SCISSOR_NODE.nodeNum(t6)
        nop
        
        l_002D4720:
        lqc2     vf4, SCISSOR.triangle.node[0].vertex(t6)
        
        vdiv     Q, vf0w, vf4z
    
        lqc2     vf7, SCISSOR.triangle.node[0].color(t6)
        
        sqc2     vf7, VU1_PRIM_BUF.fR(t7)
    
        vaddz.x  vf5, vf0, vf23z
        
        lqc2     vf6, SCISSOR.triangle.node[0].texUV(t6)
    
        addi     t0, t0, -1
        
        vwaitq
        
        vmulq.x  vf5, vf5, Q
        vmulq.xy vf6, vf6, Q
        
        vaddq.z  vf6, vf0, Q
        
        vsub.w   vf6, vf6, vf6
        
        vmulx.xy vf4, vf4, vf5x
        
        sqc2     vf6, VU1_PRIM_BUF.fS(t7)
    
        vmul.xy  vf4, vf4, vf17
        
        vadd.xy  vf4, vf4, vf16
        
        addi     t6, t6, sizeof(NODE)
    
        sqc2     vf4, VU1_PRIM_BUF.fX(t7)
    
        addi     t7, t7, sizeof(VU1_PRIM_BUF)
    
        bnez     t0, l_002D4720
        vnop
    " : : "r"(scissor), "r"(vu1ScessorBuf) : "t7"
    );
#endif

    ulType = (ulType & 0xFFFDFFFFFFFFFFFF) | 0x6800000000000;
    
    pP = vu1ScessorBuf;
    
    Ps2AddPrim3DEx(ulType, pP, in->nodeNum);
}

// 100% matching! 
void vu1DrawTriangleStripOpaqueSingle(unsigned long ulType, VU1_STRIP_BUF* pStripTop, unsigned short usStripMax, unsigned short usMode)
{
    VU1_PRIM_BUF* pP;                            
    VU1_STRIP_BUF* pS0, *pS1, *pS2;                         
    float fIz;                                   
    unsigned short usStripCnt;                  
    void (*pFunc)(VU1_STRIP_BUF*, VU1_PRIM_BUF*); 
    unsigned int clipflag;                     
    int flg;                                    
    int count;                                

    pS0 = pStripTop; 
    pS1 = &pS0[1];
    pS2 = &pS1[1];
    
    InitNodeArraySet2();
    
    pP = (VU1_PRIM_BUF*)Ps2_DRAW_TMP;
    
    pFunc = pColorCalcFuncTbl[(usMode & 0xFFFF) & 0xF];
    
    pFunc(pS0, pP);
    
    _Clip_ViewVolume2((NJS_POINT4*)&pS0->fVx);
    
    fIz = pS0->fIz;
   
    pP->fS = pS0->fU * fIz;
    pP->fT = pS0->fV * fIz;
    pP->fQ = fIz;
    
    pP->ulKick = 0;
    
    pP->fX = fVu1OffsetX + pS0->fSx;
    pP->fY = fVu1OffsetY + pS0->fSy;
    pP->fZ = pS0->fVz;
    
    pP->fF = pS0->fFog;
    
    flg = _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
    
    flg <<= 1;
    
    pP++;
    
    pFunc(pS1, pP);
    
    _Clip_ViewVolume2((NJS_POINT4*)&pS1->fVx);
    
    fIz = pS1->fIz;
    
    pP->fS = pS1->fU * fIz;
    pP->fT = pS1->fV * fIz;
    pP->fQ = fIz;
    
    pP->ulKick = 0;
    
    pP->fX = fVu1OffsetX + pS1->fSx;
    pP->fY = fVu1OffsetY + pS1->fSy;
    pP->fZ = pS1->fVz;
    
    pP->fF = pS1->fFog;
    
    flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
    
    flg <<= 1;
    
    pP++; 
    
    for (usStripCnt = 2; usStripCnt < usStripMax; usStripCnt++, pS0++, pS1++, pS2++, pP++) 
    {
        pFunc(pS2, pP);
        
        _Check_ClipViewAll((NJS_POINT4*)&pS2->fVx);
        
        fIz = pS2->fIz;
        
        pP->fS = pS2->fU * fIz;
        pP->fT = pS2->fV * fIz;
        pP->fQ = fIz;
        
        pP->ulKick = 0;
        
        pP->fX = fVu1OffsetX + pS2->fSx;
        pP->fY = fVu1OffsetY + pS2->fSy;
        pP->fZ = pS2->fVz;
        
        pP->fF = pS2->fFog;
        
        flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
        
        clipflag = _Get_ClipViewVolume2();
        
        if ((0 < pS0->fNz) && (0 < pS1->fNz) && (0 < pS2->fNz))  
        {
            pP->ulKick = 32768;
        }
        else if (((flg & 0x7)) && ((clipflag & 0x3FFFF)))
        {
            pP->ulKick = 32768;
            
            if (_Get_ClipVolumePlane() != 0) 
            {
                _Set_NodeArray(pS2, pP);
                
                count = _Check_ScissorPlane();
                
                if (count != 0)
                {
                    DrawScissorPolygonOpaque2(count, ulType);
                }
            }
        }
        
        flg <<= 1;
    }
    
    Ps2AddPrim3DEx(ulType, Ps2_DRAW_TMP, usStripMax);
}

// 100% matching! 
void vu1DrawTriangleStripOpaqueDouble(unsigned long ulType, VU1_STRIP_BUF* pS, unsigned short usStripMax, unsigned short usMode)
{
    VU1_PRIM_BUF* pP;                            
    float fIz;                                   
    unsigned short usStripCnt;                  
    void (*pFunc)(VU1_STRIP_BUF*, VU1_PRIM_BUF*); 
    unsigned int clipflag;                     
    unsigned int flg;                                    
    int count;   

    InitNodeArraySet2();
    
    pP = (VU1_PRIM_BUF*)Ps2_DRAW_TMP;
    
    pFunc = pColorCalcFuncTbl[(usMode & 0xFFFF) & 0xF];
    
    pFunc(pS, pP);
    
    fIz = pS->fIz;
   
    pP->fS = pS->fU * fIz;
    pP->fT = pS->fV * fIz;
    pP->fQ = fIz;
    
    pP->ulKick = 0;

    _Clip_ViewVolume2((NJS_POINT4*)&pS->fVx);
    
    pP->fX = fVu1OffsetX + pS->fSx;
    pP->fY = fVu1OffsetY + pS->fSy;
    pP->fZ = pS->fVz;
    
    pP->fF = pS->fFog;
    
    flg = _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
    
    flg <<= 1;
    
    pP++;
    pS++;
    
    pFunc(pS, pP);
    
    fIz = pS->fIz;
    
    pP->fS = pS->fU * fIz;
    pP->fT = pS->fV * fIz;
    pP->fQ = fIz;
    
    pP->ulKick = 0;

    _Clip_ViewVolume2((NJS_POINT4*)&pS->fVx);
    
    pP->fX = fVu1OffsetX + pS->fSx;
    pP->fY = fVu1OffsetY + pS->fSy;
    pP->fZ = pS->fVz;
    
    pP->fF = pS->fFog;
    
    flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
    
    flg <<= 1;
    
    pP++; 
    pS++;
    
    for (usStripCnt = 2; usStripCnt < usStripMax; usStripCnt++, pS++, pP++) 
    {
        pFunc(pS, pP);
        
        fIz = pS->fIz;
        
        pP->fS = pS->fU * fIz;
        pP->fT = pS->fV * fIz;
        pP->fQ = fIz;
        
        pP->ulKick = 0;

        _Check_ClipViewAll((NJS_POINT4*)&pS->fVx);
        
        pP->fX = fVu1OffsetX + pS->fSx;
        pP->fY = fVu1OffsetY + pS->fSy;
        pP->fZ = pS->fVz;
        
        pP->fF = pS->fFog;
        
        flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
        
        clipflag = _Get_ClipViewVolume2();
        
        if (((flg & 0x7)) && ((clipflag & 0x3FFFF)))
        {
            pP->ulKick = 32768;
            
            if (_Get_ClipVolumePlane() != 0) 
            {
                _Set_NodeArray(pS, pP);
                
                count = _Check_ScissorPlane();
                
                if (count != 0)
                {
                    DrawScissorPolygonOpaque2(count, ulType);
                }
            }
        }
        
        flg <<= 1;
    }
    
    Ps2AddPrim3DEx(ulType, Ps2_DRAW_TMP, usStripMax);
}

// 100% matching! 
void vu1DrawTriangleStripTransSingle(unsigned long ulType, VU1_STRIP_BUF* pStripTop, unsigned short usStripMax, unsigned short usMode)
{
    VU1_PRIM_BUF* pP;                            
    VU1_STRIP_BUF* pS0, *pS1, *pS2;                         
    float fIz;                                   
    unsigned short usStripCnt;                  
    void (*pFunc)(VU1_STRIP_BUF*, VU1_PRIM_BUF*); 
    unsigned int clipflag;                     
    int flg;                                    
    int count;                                

    pS0 = pStripTop; 
    pS1 = &pS0[1];
    pS2 = &pS1[1];
    
    InitNodeArraySet2();
    
    pP = (VU1_PRIM_BUF*)Ps2_DRAW_TMP;
    
    pFunc = pColorCalcFuncTbl[(usMode & 0xFFFF) & 0xF];
    
    pFunc(pS0, pP);
    
    _Clip_ViewVolume2((NJS_POINT4*)&pS0->fVx);
    
    fIz = pS0->fIz;
   
    pP->fS = pS0->fU * fIz;
    pP->fT = pS0->fV * fIz;
    pP->fQ = fIz;
    
    pP->ulKick = 0;
    
    pP->fX = fVu1OffsetX + pS0->fSx;
    pP->fY = fVu1OffsetY + pS0->fSy;
    pP->fZ = pS0->fVz;
    
    pP->fF = pS0->fFog;
    
    flg = _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
    
    flg <<= 1;
    
    pP++;
    
    pFunc(pS1, pP);
    
    _Clip_ViewVolume2((NJS_POINT4*)&pS1->fVx);
    
    fIz = pS1->fIz;
    
    pP->fS = pS1->fU * fIz;
    pP->fT = pS1->fV * fIz;
    pP->fQ = fIz;
    
    pP->ulKick = 0;
    
    pP->fX = fVu1OffsetX + pS1->fSx;
    pP->fY = fVu1OffsetY + pS1->fSy;
    pP->fZ = pS1->fVz;
    
    pP->fF = pS1->fFog;
    
    flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
    
    flg <<= 1;
    
    pP++; 
    
    for (usStripCnt = 2; usStripCnt < usStripMax; usStripCnt++, pS0++, pS1++, pS2++, pP++) 
    {
        pFunc(pS2, pP);
        
        _Check_ClipViewAll((NJS_POINT4*)&pS2->fVx);
        
        fIz = pS2->fIz;
        
        pP->fS = pS2->fU * fIz;
        pP->fT = pS2->fV * fIz;
        pP->fQ = fIz;
        
        pP->ulKick = 0;
        
        pP->fX = fVu1OffsetX + pS2->fSx;
        pP->fY = fVu1OffsetY + pS2->fSy;
        pP->fZ = pS2->fVz;
        
        pP->fF = pS2->fFog;
        
        flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX);
        
        clipflag = _Get_ClipViewVolume2();
        
        if ((0 < pS0->fNz) && (0 < pS1->fNz) && (0 < pS2->fNz))  
        {
            pP->ulKick = 32768;
        }
        else if (((flg & 0x7)) && ((clipflag & 0x3FFFF)))
        {
            pP->ulKick = 32768;
            
            if (_Get_ClipVolumePlane() != 0) 
            {
                _Set_NodeArray(pS2, pP);
                
                count = _Check_ScissorPlane();
                
                if (count != 0)
                {
                    DrawScissorPolygonOpaque2(count, ulType);
                }
            }
        }
        
        flg <<= 1;
    }
    
    Ps2AddPrim3DEx(ulType, Ps2_DRAW_TMP, usStripMax);
}

// 99.38% matching
int _Clip_Screen(float* clip)
{
    int ret;         
    static float para[2][4] = { 2048.0f, 2048.0f, 0, 240.0f, 0.75f, 1.0f, 0, 0 }; 

    ret = 0;

#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(ret));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(clip));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&para));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op1.w[0] + (0))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (0))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op2.w[0] + (16))) & ~15u), 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] - pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] - pcVu0.vf[5].f[1]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[6].f[2]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] * pcVu0.vf[6].f[3]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        vu_clip(&pcVu0, &pcVu0.vf[4], pcVu0.vf[5].f[3]);
        g_op0.d[0] = A2C_SX32(a2c_cfc2(18));
        *(uint32_t*)&(ret) = g_op0.w[0];
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile
    ("
        lqc2       vf4, 0(%1)
        lqc2       vf5, 0(%2)
        lqc2       vf6, 16(%2)
    
        vsub.xy    vf4, vf4, vf5
        
        vmul.xyzw  vf4, vf4, vf6
        
        vclipw.xyz vf4, vf5
        vnop
        vnop
        vnop
        vnop
        vnop
    
        cfc2       %0, vi18
    " : "=r"(ret) : "f"(clip), "r"(&para) :
    );
#endif

    return ret;
}

// 97.44% matching
void vu1DrawTriangleStripTransDouble(unsigned long ulType, VU1_STRIP_BUF* pS, unsigned short usStripMax, unsigned short usMode)
{ 
    VU1_PRIM_BUF* pP;                            
    float fIz;                                   
    unsigned short usStripCnt;                  
    unsigned int clipflag;                     
    int flg;                                    
    int count;   
    int clipswitch;

    clipswitch = 0; 
    
    clipflag = 0;
    
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_v0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(usMode));
        g_v0.d[0] = (g_op0.d[0] & (uint64_t)(uint16_t)(0xF));
        g_t0.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)(g_v0.w[0]));
        a2c_ctc2(1, g_t0.w[0]);
        pcVu0Micro(365); /* VU0_INIT_CALC_COLOR */
        *(uint32_t*)&(usMode) = g_op0.w[0];
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
    .set noreorder
        andi    v0, %0, 0xF    

        add     t0, zero, v0 
        
        ctc2    t0, vi1      
    
        vcallms VU0_INIT_CALC_COLOR 
        nop
    .set reorder
    " : "r"(usMode) : 
    );
#endif
   
    pP = (VU1_PRIM_BUF*)Ps2_DRAW_TMP; 
    
    if (clipswitch == 0) 
    { 
#ifdef TARGET_PC
        {
            PcGpr g_op2 = { 0 };
            PcGpr g_op3 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_zero = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_1 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_2 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_3 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_4 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_5 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_op1 = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            fr_op0.f = (float)(fVu1OffsetX);
            fr_op1.f = (float)(fVu1OffsetY);
            g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
            g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
            memcpy(&pcVu0.vf[14], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
            pcVu0Micro(394); /* VU0_CALC_COLOR */
            fr_1.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fU)))));
            fr_2.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fV)))));
            fr_3.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fIz)))));
            memcpy(&g_t0, A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
            fr_4.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSx)))));
            fr_5.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSy)))));
            fr_1.f = fr_1.f * fr_3.f;
            fr_2.f = fr_2.f * fr_3.f;
            fr_4.f = fr_4.f + fr_op0.f;
            fr_5.f = fr_5.f + fr_op1.f;
            memcpy(A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX)))) & ~15u), &g_t0, 16);
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fS))))) = fr_1.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fT))))) = fr_2.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fQ))))) = fr_3.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, ulKick))))) = (uint32_t)g_zero.d[0];
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX))))) = fr_4.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fY))))) = fr_5.u;
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile
        ("
        .set noreorder
            lqc2    vf4, VU1_STRIP_BUF.fIr(%2) 
            lqc2    vf14, VU1_STRIP_BUF.fVx(%2)
            
            vcallms VU0_CALC_COLOR  
        
            lwc1    f1, VU1_STRIP_BUF.fU(%2)
            lwc1    f2, VU1_STRIP_BUF.fV(%2) 
            lwc1    f3, VU1_STRIP_BUF.fIz(%2) 
            
            lq      t0, VU1_STRIP_BUF.fVx(%2) 
            
            lwc1    f4, VU1_STRIP_BUF.fSx(%2) 
            lwc1    f5, VU1_STRIP_BUF.fSy(%2) 
            
            mul.s   f1, f1, f3 
            mul.s   f2, f2, f3 
            
            add.s   f4, f4, %0 
            add.s   f5, f5, %1 
            
            sq      t0, VU1_PRIM_BUF.fX(%3) 
            
            swc1    f1, VU1_PRIM_BUF.fS(%3) 
            swc1    f2, VU1_PRIM_BUF.fT(%3) 
            swc1    f3, VU1_PRIM_BUF.fQ(%3) 
            
            sw      zero, VU1_PRIM_BUF.ulKick(%3) 
            
            swc1    f4, VU1_PRIM_BUF.fX(%3) 
            swc1    f5, VU1_PRIM_BUF.fY(%3) 
        .set reorder
        " : : "f"(fVu1OffsetX), "f"(fVu1OffsetY), "r"(pS), "r"(pP) : "$f1", "$f2", "$f3", "$f4", "$f5", "$t0", "memory"
        );  
#endif
            
        flg = _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX) << 2;
        
#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(&g_t0, &pcVu0.vf[4], 16);
            memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &g_t0, 16);
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        ("
            qmfc2.i t0, vf4    
            
            sq      t0, VU1_PRIM_BUF.fR(%0) 
        " : : "r"(pP) : "$t0", "memory" 
        );
#endif
        
        pP++; 
        pS++; 
        
#ifdef TARGET_PC
        {
            PcGpr g_op2 = { 0 };
            PcGpr g_op3 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_zero = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_1 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_2 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_3 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_4 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_5 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
            union { float f; uint32_t u; int32_t i; } fr_op1 = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            fr_op0.f = (float)(fVu1OffsetX);
            fr_op1.f = (float)(fVu1OffsetY);
            g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
            g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
            memcpy(&pcVu0.vf[14], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
            pcVu0Micro(394); /* VU0_CALC_COLOR */
            fr_1.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fU)))));
            fr_2.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fV)))));
            fr_3.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fIz)))));
            memcpy(&g_t0, A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
            fr_4.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSx)))));
            fr_5.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSy)))));
            fr_1.f = fr_1.f * fr_3.f;
            fr_2.f = fr_2.f * fr_3.f;
            fr_4.f = fr_4.f + fr_op0.f;
            fr_5.f = fr_5.f + fr_op1.f;
            memcpy(A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX)))) & ~15u), &g_t0, 16);
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fS))))) = fr_1.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fT))))) = fr_2.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fQ))))) = fr_3.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, ulKick))))) = (uint32_t)g_zero.d[0];
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX))))) = fr_4.u;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fY))))) = fr_5.u;
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        (" 
        .set noreorder
            lqc2    vf4, VU1_STRIP_BUF.fIr(%2) 
            lqc2    vf14, VU1_STRIP_BUF.fVx(%2)
            
            vcallms VU0_CALC_COLOR  
        
            lwc1    f1, VU1_STRIP_BUF.fU(%2)
            lwc1    f2, VU1_STRIP_BUF.fV(%2) 
            lwc1    f3, VU1_STRIP_BUF.fIz(%2) 
            
            lq      t0, VU1_STRIP_BUF.fVx(%2) 
            
            lwc1    f4, VU1_STRIP_BUF.fSx(%2) 
            lwc1    f5, VU1_STRIP_BUF.fSy(%2) 
            
            mul.s   f1, f1, f3 
            mul.s   f2, f2, f3 
            
            add.s   f4, f4, %0 
            add.s   f5, f5, %1 
            
            sq      t0, VU1_PRIM_BUF.fX(%3) 
            
            swc1    f1, VU1_PRIM_BUF.fS(%3) 
            swc1    f2, VU1_PRIM_BUF.fT(%3) 
            swc1    f3, VU1_PRIM_BUF.fQ(%3) 
            
            sw      zero, VU1_PRIM_BUF.ulKick(%3) 
            
            swc1    f4, VU1_PRIM_BUF.fX(%3) 
            swc1    f5, VU1_PRIM_BUF.fY(%3) 
        .set reorder
        " : : "f"(fVu1OffsetX), "f"(fVu1OffsetY), "r"(pS), "r"(pP) : "$f1", "$f2", "$f3", "$f4", "$f5", "$t0", "memory"
        );
#endif
        
        flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX) << 1; 
        
#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(&g_t0, &pcVu0.vf[4], 16);
            memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &g_t0, 16);
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        ("
            qmfc2.i t0, vf4    
            
            sq      t0, VU1_PRIM_BUF.fR(%0) 
        " : : "r"(pP) : "$t0", "memory" 
        );
#endif
        
        pP++; 
        pS++;
        
        for (usStripCnt = 2; usStripCnt < usStripMax; usStripCnt++, pS++, pP++) 
        {
#ifdef TARGET_PC
            {
                PcGpr g_op2 = { 0 };
                PcGpr g_op3 = { 0 };
                PcGpr g_t0 = { 0 };
                PcGpr g_zero = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_1 = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_2 = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_3 = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_4 = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_5 = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_op0 = { 0 };
                union { float f; uint32_t u; int32_t i; } fr_op1 = { 0 };
                uint64_t lo_ = 0; int fcc_ = 0;
                fr_op0.f = (float)(fVu1OffsetX);
                fr_op1.f = (float)(fVu1OffsetY);
                g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
                g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
                memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
                memcpy(&pcVu0.vf[14], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
                pcVu0Micro(237); /* VU0_CLIP_VIEW_VOLUME_ALL */
                fr_1.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fU)))));
                fr_2.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fV)))));
                fr_3.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fIz)))));
                memcpy(&g_t0, A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
                fr_4.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSx)))));
                fr_5.u = *(uint32_t*)A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSy)))));
                fr_1.f = fr_1.f * fr_3.f;
                fr_2.f = fr_2.f * fr_3.f;
                fr_4.f = fr_4.f + fr_op0.f;
                fr_5.f = fr_5.f + fr_op1.f;
                memcpy(A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX)))) & ~15u), &g_t0, 16);
                *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fS))))) = fr_1.u;
                *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fT))))) = fr_2.u;
                *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fQ))))) = fr_3.u;
                *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, ulKick))))) = (uint32_t)g_zero.d[0];
                *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX))))) = fr_4.u;
                *(uint32_t*)A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fY))))) = fr_5.u;
                (void)g_zero; (void)lo_; (void)fcc_;
            }
#else
            asm volatile 
            (" 
            .set noreorder
                lqc2    vf4, VU1_STRIP_BUF.fIr(%2) 
                lqc2    vf14, VU1_STRIP_BUF.fVx(%2)
                
                vcallms VU0_CLIP_VIEW_VOLUME_ALL  
            
                lwc1    f1, VU1_STRIP_BUF.fU(%2)
                lwc1    f2, VU1_STRIP_BUF.fV(%2) 
                lwc1    f3, VU1_STRIP_BUF.fIz(%2) 
                
                lq      t0, VU1_STRIP_BUF.fVx(%2) 
                
                lwc1    f4, VU1_STRIP_BUF.fSx(%2) 
                lwc1    f5, VU1_STRIP_BUF.fSy(%2) 
                
                mul.s   f1, f1, f3 
                mul.s   f2, f2, f3 
                
                add.s   f4, f4, %0 
                add.s   f5, f5, %1 
                
                sq      t0, VU1_PRIM_BUF.fX(%3) 
                
                swc1    f1, VU1_PRIM_BUF.fS(%3) 
                swc1    f2, VU1_PRIM_BUF.fT(%3) 
                swc1    f3, VU1_PRIM_BUF.fQ(%3) 
                
                sw      zero, VU1_PRIM_BUF.ulKick(%3) 
                
                swc1    f4, VU1_PRIM_BUF.fX(%3) 
                swc1    f5, VU1_PRIM_BUF.fY(%3) 
            .set reorder
            " : : "f"(fVu1OffsetX), "f"(fVu1OffsetY), "r"(pS), "r"(pP) : "$f1", "$f2", "$f3", "$f4", "$f5", "$t0", "memory"
            );
#endif
                
            flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX); 
            
#ifdef TARGET_PC
            {
                PcGpr g_op0 = { 0 };
                PcGpr g_op1 = { 0 };
                PcGpr g_t0 = { 0 };
                PcGpr g_zero = { 0 };
                uint64_t lo_ = 0; int fcc_ = 0;
                g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(clipflag));
                g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
                memcpy(&g_t0, &pcVu0.vf[4], 16);
                memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &g_t0, 16);
                g_op0.d[0] = A2C_SX32(a2c_cfc2(18));
                g_op0.d[0] = A2C_SX32(g_op0.w[0] + (uint32_t)((int32_t)(0)));
                *(uint32_t*)&(clipflag) = g_op0.w[0];
                (void)g_zero; (void)lo_; (void)fcc_;
            }
#else
            asm volatile 
            ("
                qmfc2.i t0, vf4     
                
                sq      t0, VU1_PRIM_BUF.fR(%1) 
                
                cfc2    %0, vi18    
                
                addi    %0, %0, 0
            " : "=r"(clipflag) : "r"(pP) : "$t0" 
            );
#endif

            if ((flg != 0) && ((clipflag & 0x3FFFF)))
            { 
                pP->ulKick = 32768; 
                
                if (_Get_ClipVolumePlane() != 0) 
                { 
                    _Set_NodeArray(pS, pP); 
                    
                    count = _Check_ScissorPlane(); 
                    
                    if (count != 0)
                    { 
                        DrawScissorPolygonOpaque2(count, ulType); 
                    }
                }
            }
            
            flg = (flg << 1) & 0x7; 
        } 
    }
    else
    {
#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
            memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
            pcVu0Micro(394); /* VU0_CALC_COLOR */
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        ("
        .set noreorder
            lqc2    vf4, VU1_STRIP_BUF.fIr(%0) 
            
            vcallms VU0_CALC_COLOR  
            nop
        .set reorder
        " : : "r"(pS) : 
        );   
#endif
        
        fIz = pS->fIz; 
        
        pP->fS = pS->fU * fIz; 
        pP->fT = pS->fV * fIz; 
        pP->fQ = fIz; 
        
        pP->ulKick = 0; 
        
        pP->fX = fVu1OffsetX + pS->fSx; 
        pP->fY = fVu1OffsetY + pS->fSy; 
        pP->fZ = pS->fVz; 
        
        pP->fF = pS->fFog; 
        
#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        ("
            sqc2 vf4, VU1_PRIM_BUF.fR(%0) 
            nop                         
        " : : "r"(pP) : 
        );
#endif

        _Clip_ViewVolume2((NJS_POINT4*)&pS->fVx); 
        
        pP++; 
        pS++; 

#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
            memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
            pcVu0Micro(394); /* VU0_CALC_COLOR */
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        ("
        .set noreorder
            lqc2    vf4, VU1_STRIP_BUF.fIr(%0) 
            
            vcallms VU0_CALC_COLOR  
            nop
        .set reorder
        " : : "r"(pS) : 
        );
#endif
        
        fIz = pS->fIz; 
        
        pP->fS = pS->fU * fIz;  
        pP->fT = pS->fV * fIz;
        pP->fQ = fIz; 
        
        pP->ulKick = 0; 
        
        pP->fX = fVu1OffsetX + pS->fSx; 
        pP->fY = fVu1OffsetY + pS->fSy; 
        pP->fZ = pS->fVz; 
        
        pP->fF = pS->fFog; 
        
#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile 
        ("
            sqc2 vf4, VU1_PRIM_BUF.fR(%0) 
            nop                         
        " : : "r"(pP) : 
        );
#endif
    
        _Clip_ViewVolume2((NJS_POINT4*)&pS->fVx); 
        
        pP++; 
        pS++; 
        
        for (usStripCnt = 2; usStripCnt < usStripMax; usStripCnt++, pS++, pP++) 
        {
#ifdef TARGET_PC
            {
                PcGpr g_op0 = { 0 };
                PcGpr g_zero = { 0 };
                uint64_t lo_ = 0; int fcc_ = 0;
                g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
                memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
                pcVu0Micro(394); /* VU0_CALC_COLOR */
                (void)g_zero; (void)lo_; (void)fcc_;
            }
#else
            asm volatile 
            ("
            .set noreorder
                lqc2    vf4, VU1_STRIP_BUF.fIr(%0) 
                
                vcallms VU0_CALC_COLOR  
                nop
            .set reorder
            " : : "r"(pS) : 
            );
#endif
            
            fIz = pS->fIz; 
            
            pP->fS = pS->fU * fIz;
            pP->fT = pS->fV * fIz; 
            pP->fQ = fIz; 
            
            pP->ulKick = 0; 
            
            pP->fX = fVu1OffsetX + pS->fSx; 
            pP->fY = fVu1OffsetY + pS->fSy; 
            pP->fZ = pS->fVz; 
            
            pP->fF = pS->fFog; 
            
#ifdef TARGET_PC
            {
                PcGpr g_op0 = { 0 };
                PcGpr g_zero = { 0 };
                uint64_t lo_ = 0; int fcc_ = 0;
                g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
                memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
                (void)g_zero; (void)lo_; (void)fcc_;
            }
#else
            asm volatile 
            ("
                sqc2 vf4, VU1_PRIM_BUF.fR(%0) 
                nop                         
            " : : "r"(pP) : 
            );
#endif
            
            _Clip_ViewVolume2((NJS_POINT4*)&pS->fVx); 
            
            clipflag = _Get_ClipViewVolume2(); 
            
            if ((clipflag & 0x3FFFF))
            { 
                pP->ulKick = 32768; 
            } 
        } 
    } 
    
    Ps2AddPrim3DEx(ulType, Ps2_DRAW_TMP, usStripMax); 
} 

// 100% matching!
void vu1GetVertexColor(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_t2 = { 0 };
        PcGpr g_t3 = { 0 };
        PcGpr g_zero = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_op2 = { 0 };
        union { float f; uint32_t u; int32_t i; } fr_op3 = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        fr_op2.f = (float)(128.0f);
        fr_op3.f = (float)(256.0f);
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(fr_op2.u);
        g_t1.d[0] = A2C_SX32(fr_op2.u);
        g_t2.d[0] = A2C_SX32(fr_op2.u);
        g_t3.d[0] = A2C_SX32(fr_op3.u);
        { PcGpr r_; r_.w[0] = g_t0.w[0]; r_.w[1] = g_t1.w[0]; r_.w[2] = g_t0.w[1]; r_.w[3] = g_t1.w[1]; g_t0 = r_; }
        { PcGpr r_; r_.w[0] = g_t2.w[0]; r_.w[1] = g_t3.w[0]; r_.w[2] = g_t2.w[1]; r_.w[3] = g_t3.w[1]; g_t2 = r_; }
        { PcGpr r_; r_.d[0] = g_t0.d[0]; r_.d[1] = g_t2.d[0]; g_t0 = r_; }
        memcpy(&pcVu0.vf[5], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[5].f[2]); r_.f[3] = vu_f(pcVu0.vf[4].f[3] * pcVu0.vf[5].f[3]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2      vf4, VU1_STRIP_BUF.fIr(%0)
        
        mfc1      t0, %2
        mfc1      t1, %2
        mfc1      t2, %2
        mfc1      t3, %3
        
        pextlw    t0, t1, t0
        pextlw    t2, t3, t2
        pcpyld    t0, t2, t0
    
        qmtc2     t0, vf5
    
        vmul.xyzw vf4, vf4, vf5 
        
        sqc2      vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "f"(128.0f), "f"(256.0f) : "$t0", "$t1", "$t2", "$t3"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorCM(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim) // first parameter is not present on the debugging symbols
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[5], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[5].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2    vf4, VU1_COLOR.fR(%1)

        lw      t0, fVu1AlphaRatio

        qmtc2   t0, vf5 
    
        vmulx.w vf4, vf0, vf5x 

        sqc2    vf4, VU1_PRIM_BUF.fR(%0)
    " : : "r"(pPrim), "r"(&vu1Diffuse), "f"(fVu1AlphaRatio) : "$v1", "$t0"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorIgnore(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim) // first parameter is not present on the debugging symbols
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_t0.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)((int32_t)(128)));
        memcpy(&pcVu0.vf[5], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = (float)pcVu0.vf[5].i[0] / 1.0f; r_.f[1] = (float)pcVu0.vf[5].i[1] / 1.0f; r_.f[2] = (float)pcVu0.vf[5].i[2] / 1.0f; r_.f[3] = (float)pcVu0.vf[5].i[3] / 1.0f; pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; }
        g_t1.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[6], &g_t1, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[0].f[1] + pcVu0.vf[5].f[0]); r_.f[2] = vu_f(pcVu0.vf[0].f[2] + pcVu0.vf[5].f[0]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[6].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        addi        t0, zero, 128
        
        qmtc2       t0, vf5
        
        vitof0.xyzw vf5, vf5
        
        lw          t1, fVu1AlphaRatio
    
        qmtc2       t1, vf6
    
        vaddx.xyz   vf4, vf0, vf5x
        vmulx.w     vf4, vf0, vf6x 

        sqc2        vf4, VU1_PRIM_BUF.fR(%0)
    " : : "r"(pPrim), "f"(fVu1AlphaRatio) : "$t1"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDif(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        memcpy(&pcVu0.vf[6], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[6].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)
        lqc2       vf5, VU1_COLOR.fR(%2)

        lw         t0, fVu1AlphaRatio

        vminiw.xyz vf4, vf4, vf0w
        vmaxx.xyz  vf4, vf4, vf0x 
        
        qmtc2      t0, vf6 

        vmul.xyz   vf4, vf4, vf5
        vmulx.w    vf4, vf0, vf6x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "r"(&vu1Diffuse), "f"(fVu1AlphaRatio) : "$v1", "$t0"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDifAmb(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim) 
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[22].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[22].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[22].f[2]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[7].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[7].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[7].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[7].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[7].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[7].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[7].f[0] * pcVu0.vf[20].f[0]); r_.f[1] = vu_f(pcVu0.vf[7].f[1] * pcVu0.vf[20].f[1]); r_.f[2] = vu_f(pcVu0.vf[7].f[2] * pcVu0.vf[20].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)

        lw         t0, fVu1AlphaRatio
        
        qmtc2      t0, vf8

        vadd.xyz   vf7, vf4, vf22
        
        vmaxx.xyz  vf7, vf7, vf0x
        vminiw.xyz vf7, vf7, vf0w
        
        vmul.xyz   vf4, vf7, vf20
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "f"(fVu1AlphaRatio) : "$t0"
    );
#endif
}

// 96.47% matching
void vu1GetVertexColorDifSpe1(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        { VuVec r_ = pcVu0.vf[0]; r_.u[0] = pcVu0.vf[4].u[0]; r_.u[1] = pcVu0.vf[4].u[1]; r_.u[2] = pcVu0.vf[4].u[2]; r_.u[3] = pcVu0.vf[4].u[3]; pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; pcVu0.vf[5].f[3] = r_.f[3]; }
        { VuVec r_ = pcVu0.vf[0]; r_.u[0] = pcVu0.vf[4].u[0]; r_.u[1] = pcVu0.vf[4].u[1]; r_.u[2] = pcVu0.vf[4].u[2]; r_.u[3] = pcVu0.vf[4].u[3]; pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; pcVu0.vf[6].f[2] = r_.f[2]; pcVu0.vf[6].f[3] = r_.f[3]; }
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] - pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] - pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] - pcVu0.vf[0].f[3]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[7].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[7].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[7].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[7].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[7].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[7].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[7].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[7].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[7].f[2] * pcVu0.vf[6].f[2]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[7].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)

        lw         t0, fVu1AlphaRatio
    
        vmove.xyzw vf5, vf4
        vmove.xyzw vf6, vf4
        
        qmtc2      t0, vf8
    
        vsubw.xyz  vf7, vf4, vf0w

        vmaxx.xyz  vf4, vf4, vf0x
        vminiw.xyz vf4, vf4, vf0w
        
        vmaxx.xyz  vf7, vf7, vf0x
        vminiw.xyz vf7, vf7, vf0w
        
        vmul.xyz   vf4, vf4, vf5
        vmul.xyz   vf7, vf7, vf6
        
        vadd.xyz   vf4, vf4, vf7
        
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "f"(fVu1AlphaRatio) : "$t0", "memory"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDifSpe2(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_op3 = { 0 };
        PcGpr g_op4 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Ambient));
        g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Specula));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[5].f[2]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] - pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] - pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] - pcVu0.vf[0].f[3]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[5].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[5].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[5].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[5].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[5].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[5].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[6].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] * pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] * pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[5].f[2] * pcVu0.vf[7].f[2]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; pcVu0.vf[5].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)
        lqc2       vf5, VU1_COLOR.fR(%2)
        lqc2       vf6, VU1_COLOR.fR(%3)
        lqc2       vf7, VU1_COLOR.fR(%4)

        lw         t0, fVu1AlphaRatio
        
        qmtc2      t0, vf8
    
        vadd.xyz   vf5, vf4, vf5
        vsubw.xyz  vf5, vf5, vf0w

        vmaxx.xyz  vf4, vf4, vf0x
        vminiw.xyz vf4, vf4, vf0w
        
        vmaxx.xyz  vf5, vf5, vf0x
        vminiw.xyz vf5, vf5, vf0w
        
        vmul.xyz   vf4, vf4, vf6
        vmul.xyz   vf5, vf5, vf7
        
        vadd.xyz   vf4, vf4, vf5
        
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "r"(&vu1Ambient), "r"(&vu1Diffuse), "r"(&vu1Specula), "f"(fVu1AlphaRatio) : "$v1", "$a2", "$a3", "$t0"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDifSpe3(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_op3 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Specula));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        g_t1.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)((int32_t)(17)));
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.vf[0].f[1] + pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.vf[0].f[2] + pcVu0.vf[0].f[3]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        Vu1Strip_a44_l_002D5770:;
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[7].f[0] * pcVu0.vf[4].f[0]); r_.f[1] = vu_f(pcVu0.vf[7].f[1] * pcVu0.vf[4].f[1]); r_.f[2] = vu_f(pcVu0.vf[7].f[2] * pcVu0.vf[4].f[2]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)((int32_t)(-1)));
        { int c_ = (g_t1.d[0] != 0);  if (c_) goto Vu1Strip_a44_l_002D5770; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[7].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[7].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[7].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[7].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[7].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[7].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[7].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[7].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[7].f[2] * pcVu0.vf[6].f[2]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[7].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)
        lqc2       vf5, VU1_COLOR.fR(%2)
        lqc2       vf6, VU1_COLOR.fR(%3)

        lw         t0, fVu1AlphaRatio
        
        qmtc2      t0, vf8

        addi       t1, zero, 17

        vaddw.xyz  vf7, vf0, vf0w

        l_002D5770:
        vmul.xyz   vf7, vf7, vf4

        addi       t1, t1, -1
        nop
        nop
        nop

        bnez       t1, l_002D5770
        nop

        vmaxx.xyz  vf4, vf4, vf0x
        vminiw.xyz vf4, vf4, vf0w
        
        vmaxx.xyz  vf7, vf7, vf0x
        vminiw.xyz vf7, vf7, vf0w
        
        vmul.xyz   vf4, vf4, vf5
        vmul.xyz   vf7, vf7, vf6
        
        vadd.xyz   vf4, vf4, vf7
        
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "r"(&vu1Diffuse), "r"(&vu1Specula), "f"(fVu1AlphaRatio) : "$v1", "$a2", "$t0"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDifSpe1Amb(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_op3 = { 0 };
        PcGpr g_op4 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Ambient));
        g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Specula));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] - pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] - pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] - pcVu0.vf[0].f[3]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[9].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[9].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[9].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[9].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[9].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[9].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] * pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] * pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] * pcVu0.vf[7].f[2]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[6].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[9].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)
        lqc2       vf5, VU1_COLOR.fR(%2)
        lqc2       vf6, VU1_COLOR.fR(%3)
        lqc2       vf7, VU1_COLOR.fR(%4)

        lw         t0, fVu1AlphaRatio
        
        qmtc2      t0, vf8
    
        vsubw.xyz  vf9, vf4, vf0w

        vmaxx.xyz  vf9, vf9, vf0x
        vminiw.xyz vf9, vf9, vf0w

        vadd.xyz   vf4, vf4, vf5
        
        vmaxx.xyz  vf4, vf4, vf0x
        vminiw.xyz vf4, vf4, vf0w
        
        vmul.xyz   vf9, vf9, vf7
        vmul.xyz   vf4, vf4, vf6
        
        vadd.xyz   vf4, vf4, vf9
        
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "r"(&vu1Ambient), "r"(&vu1Diffuse), "r"(&vu1Specula), "f"(fVu1AlphaRatio) : "$v1", "$a2", "$a3", "$t0"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDifSpe2Amb(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_op3 = { 0 };
        PcGpr g_op4 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Ambient));
        g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Specula));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] - pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] - pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] - pcVu0.vf[0].f[3]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[9].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[9].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[9].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[9].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[9].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[9].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] * pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] * pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] * pcVu0.vf[7].f[2]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[6].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[9].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)
        lqc2       vf5, VU1_COLOR.fR(%2)
        lqc2       vf6, VU1_COLOR.fR(%3)
        lqc2       vf7, VU1_COLOR.fR(%4)

        lw         t0, fVu1AlphaRatio
        
        qmtc2      t0, vf8

        vadd.xyz   vf4, vf4, vf5
        vsubw.xyz  vf9, vf4, vf0w

        vmaxx.xyz  vf9, vf9, vf0x
        vminiw.xyz vf9, vf9, vf0w
        
        vmaxx.xyz  vf4, vf4, vf0x
        vminiw.xyz vf4, vf4, vf0w
        
        vmul.xyz   vf9, vf9, vf7
        vmul.xyz   vf4, vf4, vf6
        
        vadd.xyz   vf4, vf4, vf9
        
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "r"(&vu1Ambient), "r"(&vu1Diffuse), "r"(&vu1Specula), "f"(fVu1AlphaRatio) : "$v1", "$a2", "$a3", "$t0"
    );
#endif
}

// 100% matching!
void vu1GetVertexColorDifSpe3Amb(VU1_STRIP_BUF* pStrip, VU1_PRIM_BUF* pPrim)
{
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_op2 = { 0 };
        PcGpr g_op3 = { 0 };
        PcGpr g_op4 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t1 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pStrip));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pPrim));
        g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Ambient));
        g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Diffuse));
        g_op4.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&vu1Specula));
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(VU1_STRIP_BUF, fIr)))) & ~15u), 16);
        memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_op4.w[0] + (offsetof(VU1_COLOR, fR)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(uintptr_t)&(fVu1AlphaRatio))));
        memcpy(&pcVu0.vf[8], &g_t0, 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[5].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[5].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        g_t1.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)((int32_t)(17)));
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.vf[0].f[1] + pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.vf[0].f[2] + pcVu0.vf[0].f[3]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        Vu1Strip_a47_l_002D58E0:;
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] * pcVu0.vf[4].f[0]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] * pcVu0.vf[4].f[1]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] * pcVu0.vf[4].f[2]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        g_t1.d[0] = A2C_SX32(g_t1.w[0] + (uint32_t)((int32_t)(-1)));
        { int c_ = (g_t1.d[0] != 0);  if (c_) goto Vu1Strip_a47_l_002D58E0; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[4].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[4].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[4].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fmaxf(pcVu0.vf[9].f[0], pcVu0.vf[0].f[0])); r_.f[1] = vu_f(fmaxf(pcVu0.vf[9].f[1], pcVu0.vf[0].f[0])); r_.f[2] = vu_f(fmaxf(pcVu0.vf[9].f[2], pcVu0.vf[0].f[0])); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(fminf(pcVu0.vf[9].f[0], pcVu0.vf[0].f[3])); r_.f[1] = vu_f(fminf(pcVu0.vf[9].f[1], pcVu0.vf[0].f[3])); r_.f[2] = vu_f(fminf(pcVu0.vf[9].f[2], pcVu0.vf[0].f[3])); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[6].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[6].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] * pcVu0.vf[6].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[9].f[0] * pcVu0.vf[7].f[0]); r_.f[1] = vu_f(pcVu0.vf[9].f[1] * pcVu0.vf[7].f[1]); r_.f[2] = vu_f(pcVu0.vf[9].f[2] * pcVu0.vf[7].f[2]); pcVu0.vf[9].f[0] = r_.f[0]; pcVu0.vf[9].f[1] = r_.f[1]; pcVu0.vf[9].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[9].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[9].f[1]); r_.f[2] = vu_f(pcVu0.vf[4].f[2] + pcVu0.vf[9].f[2]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; pcVu0.vf[4].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 14); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[0].f[3] * pcVu0.vf[8].f[0]); pcVu0.vf[4].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        memcpy(A2C_PTR(((uint32_t)(g_op1.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[4], 16);
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile 
    ("
        lqc2       vf4, VU1_STRIP_BUF.fIr(%0)
        lqc2       vf5, VU1_COLOR.fR(%2)
        lqc2       vf6, VU1_COLOR.fR(%3)
        lqc2       vf7, VU1_COLOR.fR(%4)

        lw         t0, fVu1AlphaRatio
        
        qmtc2      t0, vf8

        vadd.xyz   vf4, vf4, vf5

        addi       t1, zero, 17

        vaddw.xyz  vf9, vf0, vf0w

        l_002D58E0:
        vmul.xyz   vf9, vf9, vf4

        addi       t1, t1, -1
        nop
        nop
        nop

        bnez       t1, l_002D58E0
        nop

        vmaxx.xyz  vf4, vf4, vf0x
        vminiw.xyz vf4, vf4, vf0w
        
        vmaxx.xyz  vf9, vf9, vf0x
        vminiw.xyz vf9, vf9, vf0w
        
        vmul.xyz   vf4, vf4, vf6
        vmul.xyz   vf9, vf9, vf7
        
        vadd.xyz   vf4, vf4, vf9
        
        vmulx.w    vf4, vf0, vf8x 

        sqc2       vf4, VU1_PRIM_BUF.fR(%1)
    " : : "r"(pStrip), "r"(pPrim), "r"(&vu1Ambient), "r"(&vu1Diffuse), "r"(&vu1Specula), "f"(fVu1AlphaRatio) : "$v1", "$a2", "$a3", "$t0"
    );
#endif
}

// 100% matching! 
void vu1RotTransStripBuf(NJS_MATRIX* pMatrix, NJS_VECTOR* pVector, VU1_STRIP_BUF* pBuf)
{
    float fZ;
    float fIz;
    
    njCalcPoint(pMatrix, pVector, (NJS_POINT3*)&pBuf->fVx);
    
    fIz = 1.0f / pBuf->fVz;
    
    fZ = fVu1Projection * fIz;
    
    pBuf->fSx = fVu1AspectW * (pBuf->fVx * fZ);
    pBuf->fSy = fVu1AspectH * (pBuf->fVy * fZ);
    
    pBuf->fIz = fIz;
    
    pBuf->fFog = njCalcFogPowerEx(fIz);
}

// 100% matching! 
void vu1DrawTriangleStripTransDoubleI(unsigned long ulType, VU1_STRIP_BUF* pS, unsigned short usStripMax, unsigned short usMode)
{ 
    VU1_PRIM_BUF* pP;                              
    unsigned short usStripCnt;                      
    void (*pFunc)(VU1_STRIP_BUF*, VU1_PRIM_BUF*); 
    unsigned int clipflag;                                     
    int flg;                                              

    clipflag = 0; 
    flg = 0;

    InitNodeArraySet(&scissorflip); 
    
    pP = (VU1_PRIM_BUF*)Ps2_DRAW_TMP; 
    
    pFunc = pColorCalcFuncTbl[usMode & 0xF]; 
    
    for (usStripCnt = 0; usStripCnt < usStripMax; usStripCnt++, pS++, pP++) 
    {
        pFunc(pS, pP);
        
#ifdef TARGET_PC
        {
            PcGpr g_op0 = { 0 };
            PcGpr g_op1 = { 0 };
            PcGpr g_op2 = { 0 };
            PcGpr g_op3 = { 0 };
            PcGpr g_t0 = { 0 };
            PcGpr g_t1 = { 0 };
            PcGpr g_t2 = { 0 };
            PcGpr g_t3 = { 0 };
            PcGpr g_t4 = { 0 };
            PcGpr g_t5 = { 0 };
            PcGpr g_zero = { 0 };
            uint64_t lo_ = 0; int fcc_ = 0;
            g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&scissorflip));
            g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(&clipflag));
            g_op2.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pS));
            g_op3.d[0] = A2C_SX32((uint32_t)(uintptr_t)(pP));
            memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fSx)))) & ~15u), 16);
            memcpy(&pcVu0.vf[5], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fU)))) & ~15u), 16);
            memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] * pcVu0.vf[4].f[2]); r_.f[1] = vu_f(pcVu0.vf[5].f[1] * pcVu0.vf[4].f[2]); pcVu0.vf[5].f[0] = r_.f[0]; pcVu0.vf[5].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
            { VuVec r_ = pcVu0.vf[0]; r_.f[2] = vu_f(pcVu0.vf[0].f[2] + pcVu0.vf[4].f[2]); pcVu0.vf[5].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 2); }
            { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[5].f[3] - pcVu0.vf[5].f[3]); pcVu0.vf[5].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
            memcpy(A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fS)))) & ~15u), &pcVu0.vf[5], 16);
            memcpy(&pcVu0.vf[8], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fVx)))) & ~15u), 16);
            memcpy(&pcVu0.vf[10], A2C_PTR(((uint32_t)(g_op2.w[0] + (offsetof(VU1_STRIP_BUF, fU)))) & ~15u), 16);
            memcpy(&pcVu0.vf[9], A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), 16);
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[24].f[0] * pcVu0.vf[8].f[0]); r_.f[1] = vu_f(pcVu0.vf[24].f[1] * pcVu0.vf[8].f[0]); r_.f[2] = vu_f(pcVu0.vf[24].f[2] * pcVu0.vf[8].f[0]); r_.f[3] = vu_f(pcVu0.vf[24].f[3] * pcVu0.vf[8].f[0]); pcVu0.acc.f[0] = r_.f[0]; pcVu0.acc.f[1] = r_.f[1]; pcVu0.acc.f[2] = r_.f[2]; pcVu0.acc.f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.acc.f[0] + pcVu0.vf[25].f[0] * pcVu0.vf[8].f[1]); r_.f[1] = vu_f(pcVu0.acc.f[1] + pcVu0.vf[25].f[1] * pcVu0.vf[8].f[1]); r_.f[2] = vu_f(pcVu0.acc.f[2] + pcVu0.vf[25].f[2] * pcVu0.vf[8].f[1]); r_.f[3] = vu_f(pcVu0.acc.f[3] + pcVu0.vf[25].f[3] * pcVu0.vf[8].f[1]); pcVu0.acc.f[0] = r_.f[0]; pcVu0.acc.f[1] = r_.f[1]; pcVu0.acc.f[2] = r_.f[2]; pcVu0.acc.f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.acc.f[0] + pcVu0.vf[26].f[0] * pcVu0.vf[8].f[2]); r_.f[1] = vu_f(pcVu0.acc.f[1] + pcVu0.vf[26].f[1] * pcVu0.vf[8].f[2]); r_.f[2] = vu_f(pcVu0.acc.f[2] + pcVu0.vf[26].f[2] * pcVu0.vf[8].f[2]); r_.f[3] = vu_f(pcVu0.acc.f[3] + pcVu0.vf[26].f[3] * pcVu0.vf[8].f[2]); pcVu0.acc.f[0] = r_.f[0]; pcVu0.acc.f[1] = r_.f[1]; pcVu0.acc.f[2] = r_.f[2]; pcVu0.acc.f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.acc.f[0] + pcVu0.vf[27].f[0] * pcVu0.vf[0].f[3]); r_.f[1] = vu_f(pcVu0.acc.f[1] + pcVu0.vf[27].f[1] * pcVu0.vf[0].f[3]); r_.f[2] = vu_f(pcVu0.acc.f[2] + pcVu0.vf[27].f[2] * pcVu0.vf[0].f[3]); r_.f[3] = vu_f(pcVu0.acc.f[3] + pcVu0.vf[27].f[3] * pcVu0.vf[0].f[3]); pcVu0.vf[11].f[0] = r_.f[0]; pcVu0.vf[11].f[1] = r_.f[1]; pcVu0.vf[11].f[2] = r_.f[2]; pcVu0.vf[11].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 15); }
            vu_clip(&pcVu0, &pcVu0.vf[11], pcVu0.vf[11].f[3]);
            { VuVec r_ = pcVu0.vf[0]; r_.u[0] = pcVu0.vf[6].u[0]; r_.u[1] = pcVu0.vf[6].u[1]; r_.u[2] = pcVu0.vf[6].u[2]; r_.u[3] = pcVu0.vf[6].u[3]; pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; pcVu0.vf[7].f[2] = r_.f[2]; pcVu0.vf[7].f[3] = r_.f[3]; }
            { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[16].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[16].f[1]); pcVu0.vf[7].f[0] = r_.f[0]; pcVu0.vf[7].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
            memcpy(A2C_PTR(((uint32_t)(g_op3.w[0] + (offsetof(VU1_PRIM_BUF, fX)))) & ~15u), &pcVu0.vf[7], 16);
            g_t1.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op1.w[0] + (0)))));
            g_t0.d[0] = A2C_SX32(a2c_cfc2(18));
            g_t0.d[0] = (g_t0.d[0] & (uint64_t)(uint16_t)(0x3F));
            g_t1.d[0] = (g_t0.d[0] | g_t1.d[0]);
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op1.w[0] + (0)))) = (uint32_t)g_t1.d[0];
            g_t2.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, rotflag))))));
            lo_ = A2C_SX32((uint32_t)((int32_t)g_t2.w[0] * (int32_t)(64))); g_t3.d[0] = lo_;
            g_t2.d[0] = A2C_SX32(g_t2.w[0] + (uint32_t)((int32_t)(1)));
            g_t4.d[0] = A2C_SX32(g_t3.w[0] + (uint32_t)(g_op0.w[0]));
            g_t2.d[0] = (g_t2.d[0] & (uint64_t)(uint16_t)(0x3));
            memcpy(A2C_PTR(((uint32_t)(g_t4.w[0] + (offsetof(SCISSOR, triangle.node[0].vertex)))) & ~15u), &pcVu0.vf[8], 16);
            memcpy(A2C_PTR(((uint32_t)(g_t4.w[0] + (offsetof(SCISSOR, triangle.node[0].color)))) & ~15u), &pcVu0.vf[9], 16);
            memcpy(A2C_PTR(((uint32_t)(g_t4.w[0] + (offsetof(SCISSOR, triangle.node[0].texUV)))) & ~15u), &pcVu0.vf[10], 16);
            memcpy(A2C_PTR(((uint32_t)(g_t4.w[0] + (offsetof(SCISSOR, triangle.node[0].clipV)))) & ~15u), &pcVu0.vf[11], 16);
            g_t5.d[0] = A2C_SX32(g_t2.w[0] + (uint32_t)((int32_t)(-3)));
            { int c_ = ((int64_t)g_t5.d[0] < 0);  if (c_) goto Vu1Strip_a48_l_002D5AE0; }
            g_t2.d[0] = A2C_SX32(0u - g_zero.w[0]);
            Vu1Strip_a48_l_002D5AE0:;
            *(uint32_t*)A2C_PTR(((uint32_t)(g_op0.w[0] + (offsetof(SCISSOR, rotflag))))) = (uint32_t)g_t2.d[0];
            (void)g_zero; (void)lo_; (void)fcc_;
        }
#else
        asm volatile
        ("
            lqc2         vf4, VU1_STRIP_BUF.fSx(%2)   
            lqc2         vf5, VU1_STRIP_BUF.fU(%2)    
            lqc2         vf6, VU1_STRIP_BUF.fVx(%2)    
              
            vmulz.xy     vf5, vf5, vf4z  
              
            vadd.z       vf5, vf0, vf4   
            vsub.w       vf5, vf5, vf5   
              
            sqc2         vf5, VU1_PRIM_BUF.fS(%3)      
          
            lqc2         vf8, VU1_STRIP_BUF.fVx(%2)    
            lqc2         vf10, VU1_STRIP_BUF.fU(%2)   
            lqc2         vf9, VU1_PRIM_BUF.fR(%3)    
        
            vmulax.xyzw  ACC, vf24, vf8x
            
            vmadday.xyzw ACC, vf25, vf8y 
            vmaddaz.xyzw ACC, vf26, vf8z 
            vmaddw.xyzw  vf11, vf27, vf0w 
            
            vclipw.xyz   vf11, vf11w       
            
            vmove.xyzw   vf7, vf6         
            
            vadd.xy      vf7, vf4, vf16  
              
            sqc2         vf7, VU1_PRIM_BUF.fX(%3)   
              
            lw           t1, 0(%1)     
              
            cfc2.ni      t0, vi18        
              
            andi         t0, t0, 0x3F   
            or           t1, t0, t1     
              
            sw           t1, 0(%1)     
              
            lw           t2, SCISSOR.rotflag(%0)   
              
            muli         t3, t2, 64    
          
            addi         t2, t2, 1     
            add          t4, t3, %0      
              
            andi         t2, t2, 0x3   
              
            sqc2         vf8, SCISSOR.triangle.node[0].vertex(t4)    
            sqc2         vf9, SCISSOR.triangle.node[0].color(t4)    
            sqc2         vf10, SCISSOR.triangle.node[0].texUV(t4)   
            sqc2         vf11, SCISSOR.triangle.node[0].clipV(t4) 
              
            addi         t5, t2, -3    
              
            bltz         t5, l_002D5AE0        
            vnop                      
          
            neg          t2, zero   
            
            l_002D5AE0:
            sw           t2, SCISSOR.rotflag(%0)    
        " : : "r"(&scissorflip), "r"(&clipflag), "r"(pS), "r"(pP) : "$t0", "$t1", "$t2", "$t3", "$t4", "$t5", "memory");
#endif
    
        flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX); 
        
        if ((((flg & 0x7)) && ((clipflag & 0x3FFFF))) && (usStripCnt >= 2)) 
        { 
            pP->ulKick = 32768;
            
            ScissorTriangle(&scissorflip, &planeset); 
            
            if (scissorflip.in->nodeNum != 0) 
            { 
                DrawScissorPolygonOpaque(&scissorflip, ulType); 
       
                ResetNodeArraySet(&scissorflip); 
            } 
        }
        
        clipflag <<= 6; 
        flg <<= 1; 
    } 
    
    Ps2AddPrim3DExI(ulType, Ps2_DRAW_TMP, usStripMax); 
} 

// 100% matching!
void DrawScissorPolygonTrans1P(SCISSOR* scissor, unsigned long ulType)
{
    SCISSOR_NODE* in; 
    VU1_PRIM_BUF* pP; // not from the debugging symbols

    in = scissor->in;
    
#ifdef TARGET_PC
    {
        PcGpr g_op0 = { 0 };
        PcGpr g_op1 = { 0 };
        PcGpr g_t0 = { 0 };
        PcGpr g_t6 = { 0 };
        PcGpr g_t7 = { 0 };
        PcGpr g_zero = { 0 };
        uint64_t lo_ = 0; int fcc_ = 0;
        g_op0.d[0] = A2C_SX32((uint32_t)(uintptr_t)(scissor));
        g_op1.d[0] = A2C_SX32((uint32_t)(uintptr_t)(vu1ScessorBuf));
        g_t6.d[0] = A2C_SX32(g_op0.w[0] + (uint32_t)((int32_t)(offsetof(SCISSOR, in))));
        g_t6.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0]))))));
        g_t7.d[0] = A2C_SX32(g_zero.w[0] + (uint32_t)(g_op1.w[0]));
        g_t0.d[0] = A2C_SX32(*(uint32_t*)A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR_NODE, nodeNum))))));
        Vu1Strip_a49_l_002D4720:;
        memcpy(&pcVu0.vf[4], A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0].vertex)))) & ~15u), 16);
        vu_set_q_now(&pcVu0, vu_div(pcVu0.vf[0].f[3], pcVu0.vf[4].f[2]));
        memcpy(&pcVu0.vf[7], A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0].color)))) & ~15u), 16);
        memcpy(A2C_PTR(((uint32_t)(g_t7.w[0] + (offsetof(VU1_PRIM_BUF, fR)))) & ~15u), &pcVu0.vf[7], 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[0].f[0] + pcVu0.vf[23].f[2]); pcVu0.vf[5].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        memcpy(&pcVu0.vf[6], A2C_PTR(((uint32_t)(g_t6.w[0] + (offsetof(SCISSOR, triangle.node[0].texUV)))) & ~15u), 16);
        g_t0.d[0] = A2C_SX32(g_t0.w[0] + (uint32_t)((int32_t)(-1)));
        vu_waitq(&pcVu0);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[5].f[0] * vu_q(&pcVu0)); pcVu0.vf[5].f[0] = r_.f[0]; vu_set_mac(&pcVu0, &r_, 8); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[6].f[0] * vu_q(&pcVu0)); r_.f[1] = vu_f(pcVu0.vf[6].f[1] * vu_q(&pcVu0)); pcVu0.vf[6].f[0] = r_.f[0]; pcVu0.vf[6].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[2] = vu_f(pcVu0.vf[0].f[2] + vu_q(&pcVu0)); pcVu0.vf[6].f[2] = r_.f[2]; vu_set_mac(&pcVu0, &r_, 2); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[3] = vu_f(pcVu0.vf[6].f[3] - pcVu0.vf[6].f[3]); pcVu0.vf[6].f[3] = r_.f[3]; vu_set_mac(&pcVu0, &r_, 1); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[5].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[5].f[0]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        memcpy(A2C_PTR(((uint32_t)(g_t7.w[0] + (offsetof(VU1_PRIM_BUF, fS)))) & ~15u), &pcVu0.vf[6], 16);
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] * pcVu0.vf[17].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] * pcVu0.vf[17].f[1]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        { VuVec r_ = pcVu0.vf[0]; r_.f[0] = vu_f(pcVu0.vf[4].f[0] + pcVu0.vf[16].f[0]); r_.f[1] = vu_f(pcVu0.vf[4].f[1] + pcVu0.vf[16].f[1]); pcVu0.vf[4].f[0] = r_.f[0]; pcVu0.vf[4].f[1] = r_.f[1]; vu_set_mac(&pcVu0, &r_, 12); }
        g_t6.d[0] = A2C_SX32(g_t6.w[0] + (uint32_t)((int32_t)(sizeof(NODE))));
        memcpy(A2C_PTR(((uint32_t)(g_t7.w[0] + (offsetof(VU1_PRIM_BUF, fX)))) & ~15u), &pcVu0.vf[4], 16);
        g_t7.d[0] = A2C_SX32(g_t7.w[0] + (uint32_t)((int32_t)(sizeof(VU1_PRIM_BUF))));
        { int c_ = (g_t0.d[0] != 0);  if (c_) goto Vu1Strip_a49_l_002D4720; }
        (void)g_zero; (void)lo_; (void)fcc_;
    }
#else
    asm volatile
    ("
        addi     t6, %0, SCISSOR.in
    
        lw       t6, SCISSOR.triangle.node[0](t6)
        
        add      t7, zero, %1
    
        lw       t0, SCISSOR_NODE.nodeNum(t6)
        nop
        
        l_002D4720:
        lqc2     vf4, SCISSOR.triangle.node[0].vertex(t6)
        
        vdiv     Q, vf0w, vf4z
    
        lqc2     vf7, SCISSOR.triangle.node[0].color(t6)
        
        sqc2     vf7, VU1_PRIM_BUF.fR(t7)
    
        vaddz.x  vf5, vf0, vf23z
        
        lqc2     vf6, SCISSOR.triangle.node[0].texUV(t6)
    
        addi     t0, t0, -1
        
        vwaitq
        
        vmulq.x  vf5, vf5, Q
        vmulq.xy vf6, vf6, Q
        
        vaddq.z  vf6, vf0, Q
        
        vsub.w   vf6, vf6, vf6
        
        vmulx.xy vf4, vf4, vf5x
        
        sqc2     vf6, VU1_PRIM_BUF.fS(t7)
    
        vmul.xy  vf4, vf4, vf17
        
        vadd.xy  vf4, vf4, vf16
        
        addi     t6, t6, sizeof(NODE)
    
        sqc2     vf4, VU1_PRIM_BUF.fX(t7)
    
        addi     t7, t7, sizeof(VU1_PRIM_BUF)
    
        bnez     t0, l_002D4720
        vnop
    " : : "r"(scissor), "r"(vu1ScessorBuf) : "t7"
    );
#endif

    ulType = (ulType & 0xFFFDFFFFFFFFFFFF) | 0x6800000000000;
    
    pP = vu1ScessorBuf;
    
    Ps2AddPrim3DEx1P(ulType, pP, in->nodeNum);
}

// 100% matching!
void vu1DrawTriangleStripTransDouble1P(unsigned long ulType, VU1_STRIP_BUF* pS, unsigned short usStripMax, unsigned short usMode)
{ 
    VU1_PRIM_BUF* pP;                                      
    float fIz;                                    
    float sam;                                    
    unsigned short usStripCnt;                                   
    void (*pFunc)(VU1_STRIP_BUF*, VU1_PRIM_BUF*); 
    unsigned int clipflag;                                                           
    int flg;                                                             

    clipflag = 0; 
    flg = 0;

    InitNodeArraySet(&scissorflip); 
    
    pP = (VU1_PRIM_BUF*)Ps2_DRAW_TMP; 

    sam = 0;
    
    pFunc = pColorCalcFuncTbl[usMode & 0xF]; 
    
    for (usStripCnt = 0; usStripCnt < usStripMax; usStripCnt++)
    {
        sam += pS[usStripCnt].fVz;
    }
    
    Ps2AddPrimPrio = sam / usStripMax;
    
    for (usStripCnt = 0; usStripCnt < usStripMax; usStripCnt++, pS++, pP++) 
    {
        pFunc(pS, pP);

        fIz = pS->fIz;
       
        pP->fS = pS->fU * fIz;
        pP->fT = pS->fV * fIz;
        pP->fQ = fIz;
        
        pP->fX = fVu1OffsetX + pS->fSx;
        pP->fY = fVu1OffsetY + pS->fSy;
        pP->fZ = pS->fVz;
        
        pP->fF = pS->fFog;

        pP->ulKick = 0;

        clipflag |= _Clip_ViewVolume(node.clipV, ClipMatrix2[0], &pS->fVx) & 0x3F;
    
        flg |= _Check_DisplayAreaPoint((NJS_VECTOR*)&pP->fX); 

        *(u_long128*)&node.texUV = *(u_long128*)&pS->fU;
        *(u_long128*)&node.vertex = *(u_long128*)&pS->fVx;
        *(u_long128*)&node.color = *(u_long128*)&pP->fR;
        
        PushTriangleNodeArray(&scissorflip, &node);

        if ((((flg & 0x7)) && ((clipflag & 0x3FFFF))) && (usStripCnt >= 2)) 
        { 
            pP->ulKick = 32768;
            
            ScissorTriangle(&scissorflip, &planeset); 
            
            if (scissorflip.in->nodeNum != 0) 
            { 
                DrawScissorPolygonTrans1P(&scissorflip, ulType); 
       
                ResetNodeArraySet(&scissorflip); 
            } 
        }
        
        clipflag <<= 6; 
        flg <<= 1; 
    } 
    
    Ps2AddPrim3DEx1P(ulType, Ps2_DRAW_TMP, usStripMax); 
} 
