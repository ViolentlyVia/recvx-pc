/* PC port, GS emulator: OpenGL 3.3 renderer.
 *
 * Render targets: every frame buffer the game draws to (FRAME.FBP) is an FBO, rendered at
 * pcGsScale times the PS2 resolution. A texture whose TBP0 is the address of a render target
 * samples that FBO (the PS2 copies its draw buffer to the display buffer this way). Other
 * textures are decoded from GS memory and cached until the pages they cover are written.
 *
 * Colors stay in GS units in the shader (0x80 = 1.0 for vertex colors and alpha); render target
 * alpha holds As / 128 so the GS blend factor As maps to GL_SRC_ALPHA.
 */
#include <math.h>
#include <stdlib.h>
#include <string.h>

#include "gs_internal.h"
#include "gl_funcs.h"
#include "../../include/pc_platform.h"

int pcGsScale = 3;

#define PC_GL_DEFINE(ret, name, args) PFN_pc_##name pc_##name;
PC_GL_FUNCS(PC_GL_DEFINE)
#undef PC_GL_DEFINE

int pcGlLoad(void)
{
#define PC_GL_LOAD(ret, name, args) \
    pc_##name = (PFN_pc_##name)SDL_GL_GetProcAddress(#name); \
    if (pc_##name == NULL) { pcLog("OpenGL: missing %s", #name); return 0; }
    PC_GL_FUNCS(PC_GL_LOAD)
#undef PC_GL_LOAD
    return 1;
}

/* ------------------------------------------------------------------------------------------- */
/* shaders */

static const char* vs_src =
    "#version 330 core\n"
    "layout(location = 0) in vec3 aPos;\n"      /* x, y in pixels, z 0..1 */
    "layout(location = 1) in vec4 aColor;\n"    /* GS color / 255 */
    "layout(location = 2) in vec3 aSTQ;\n"
    "layout(location = 3) in vec2 aUV;\n"
    "layout(location = 4) in float aFog;\n"
    "uniform vec2 uTarget;\n"                   /* render target size in PS2 pixels */
    "out vec4 vColor;\n"
    "flat out vec4 vFlatColor;\n"
    "noperspective out vec3 vSTQ;\n"
    "noperspective out vec2 vUV;\n"
    "noperspective out float vFog;\n"
    "void main() {\n"
    "    gl_Position = vec4(aPos.xy / uTarget * 2.0 - 1.0, aPos.z * 2.0 - 1.0, 1.0);\n"
    "    vColor = aColor; vFlatColor = aColor; vSTQ = aSTQ; vUV = aUV; vFog = aFog;\n"
    "}\n";

static const char* fs_src =
    "#version 330 core\n"
    "in vec4 vColor;\n"
    "flat in vec4 vFlatColor;\n"
    "noperspective in vec3 vSTQ;\n"
    "noperspective in vec2 vUV;\n"
    "noperspective in float vFog;\n"
    "uniform sampler2D uTex;\n"
    "uniform int uTme, uFst, uTfx, uTcc, uIip;\n"
    "uniform vec3 uTexa;\n"                    /* TA0, TA1, AEM */
    "uniform int uTexRT;\n"                     /* 1: texture is a render target */
    "uniform vec2 uTexSize;\n"                  /* 2^TW, 2^TH */
    "uniform vec2 uRTSize;\n"                   /* render target logical size (RT textures) */
    "uniform ivec2 uWrap;\n"
    "uniform ivec4 uRegion;\n"                  /* minu, maxu, minv, maxv */
    "uniform int uLinear;\n"
    "uniform int uAte, uAtst, uAref, uAtestInvert;\n"
    "uniform int uFge;\n"
    "uniform vec3 uFogCol;\n"
    "out vec4 oColor;\n"
    "int wrapc(int c, int size, int mode, int mn, int mx) {\n"
    "    if (mode == 0) return c & (size - 1);\n"
    "    if (mode == 1) return clamp(c, 0, size - 1);\n"
    "    if (mode == 2) return clamp(c, mn, mx);\n"
    "    return (c & mn) | mx;\n"
    "}\n"
    "vec4 fetch(ivec2 c) {\n"
    "    ivec2 sz = ivec2(uTexSize);\n"
    "    c.x = wrapc(c.x, sz.x, uWrap.x, uRegion.x, uRegion.y);\n"
    "    c.y = wrapc(c.y, sz.y, uWrap.y, uRegion.z, uRegion.w);\n"
    "    return texelFetch(uTex, c, 0);\n"
    "}\n"
    "vec4 sampleTex(vec2 uv) {\n"
    "    if (uTexRT == 2) {\n"
    "        vec4 t = texture(uTex, uv / uRTSize);\n"
    "        vec3 c = floor(t.rgb * 31.0 + 0.5) * 8.0;\n"
    "        float a = (t.a * 128.0 >= 127.5) ? uTexa.y : ((uTexa.z != 0.0 && c == vec3(0.0)) ? 0.0 : uTexa.x);\n"
    "        return vec4(c, a);\n"
    "    }\n"
    "    if (uTexRT != 0) {\n"
    "        vec4 t = texture(uTex, uv / uRTSize);\n"
    "        return vec4(t.rgb * 255.0, t.a * 128.0);\n"
    "    }\n"
    "    vec4 t;\n"
    "    if (uLinear != 0) {\n"
    "        vec2 p = uv - 0.5; ivec2 i = ivec2(floor(p)); vec2 f = fract(p);\n"
    "        t = mix(mix(fetch(i), fetch(i + ivec2(1, 0)), f.x), mix(fetch(i + ivec2(0, 1)), fetch(i + ivec2(1, 1)), f.x), f.y);\n"
    "    } else {\n"
    "        t = fetch(ivec2(floor(uv)));\n"
    "    }\n"
    "    return t * 255.0;\n"
    "}\n"
    "bool atest(float a) {\n"
    "    int ia = int(a);\n"
    "    bool r = true;\n"
    "    if (uAtst == 0) r = false; else if (uAtst == 2) r = ia < uAref; else if (uAtst == 3) r = ia <= uAref;\n"
    "    else if (uAtst == 4) r = ia == uAref; else if (uAtst == 5) r = ia >= uAref; else if (uAtst == 6) r = ia > uAref;\n"
    "    else if (uAtst == 7) r = ia != uAref;\n"
    "    return r;\n"
    "}\n"
    "void main() {\n"
    "    vec4 vc = (uIip != 0 ? vColor : vFlatColor) * 255.0;\n"
    "    vec3 rgb = vc.rgb; float a = vc.a;\n"
    "    if (uTme != 0) {\n"
    "        vec2 uv = (uFst != 0) ? vUV : (vSTQ.xy / vSTQ.z) * uTexSize;\n"
    "        vec4 t = sampleTex(uv);\n"
    "        if (uTfx == 0) { rgb = t.rgb * vc.rgb / 128.0; a = (uTcc != 0) ? t.a * vc.a / 128.0 : vc.a; }\n"
    "        else if (uTfx == 1) { rgb = t.rgb; a = (uTcc != 0) ? t.a : vc.a; }\n"
    "        else if (uTfx == 2) { rgb = t.rgb * vc.rgb / 128.0 + vc.a; a = (uTcc != 0) ? t.a + vc.a : vc.a; }\n"
    "        else { rgb = t.rgb * vc.rgb / 128.0 + vc.a; a = (uTcc != 0) ? t.a : vc.a; }\n"
    "    }\n"
    "    rgb = clamp(floor(rgb), 0.0, 255.0); a = clamp(floor(a), 0.0, 255.0);\n"
    "    if (uFge != 0) { float f = vFog * 255.0; rgb = floor((f * rgb + (255.0 - f) * uFogCol) / 256.0); }\n"
    "    if (uAte != 0) { bool pass = atest(a); if (pass == (uAtestInvert != 0)) discard; }\n"
    "    oColor = vec4(rgb / 255.0, min(a / 128.0, 1.0));\n"
    "}\n";

static const char* present_vs =
    "#version 330 core\n"
    "layout(location = 0) in vec3 aPos;\n"
    "layout(location = 2) in vec3 aSTQ;\n"
    "out vec2 vTC;\n"
    "void main() { gl_Position = vec4(aPos.xy, 0.0, 1.0); vTC = aSTQ.xy; }\n";

static const char* present_fs =
    "#version 330 core\n"
    "in vec2 vTC; uniform sampler2D uTex; out vec4 oColor;\n"
    "void main() { oColor = vec4(texture(uTex, vTC).rgb, 1.0); }\n";

static GLuint prog, present_prog;
static GLuint vao, vbo;

static struct {
    GLint target, tme, fst, tfx, tcc, iip, texrt, texsize, rtsize, wrap, region, linear;
    GLint ate, atst, aref, atinv, fge, fogcol, tex, texa;
} U;

static GLuint compile(GLenum type, const char* src)
{
    GLuint s = glCreateShader(type);
    GLint ok = 0;

    glShaderSource(s, 1, &src, NULL);
    glCompileShader(s);
    glGetShaderiv(s, GL_COMPILE_STATUS, &ok);

    if (!ok)
    {
        char log[2048];

        glGetShaderInfoLog(s, sizeof(log), NULL, log);
        pcPlatformFatal("Shader compile error:\n%s", log);
    }

    return s;
}

static GLuint link(const char* vs, const char* fs)
{
    GLuint p = glCreateProgram();
    GLint ok = 0;

    glAttachShader(p, compile(GL_VERTEX_SHADER, vs));
    glAttachShader(p, compile(GL_FRAGMENT_SHADER, fs));
    glLinkProgram(p);
    glGetProgramiv(p, GL_LINK_STATUS, &ok);

    if (!ok)
    {
        char log[2048];

        glGetProgramInfoLog(p, sizeof(log), NULL, log);
        pcPlatformFatal("Shader link error:\n%s", log);
    }

    return p;
}

/* ------------------------------------------------------------------------------------------- */
/* vertex stream */

typedef struct {
    float x, y, z;
    unsigned char r, g, b, a;
    float s, t, q;
    float u, v;
    float fog;
} GlVertex;

#define MAX_GL_VERTS 65536

static GlVertex glverts[MAX_GL_VERTS];

/* ------------------------------------------------------------------------------------------- */
/* render targets */

typedef struct {
    int used;
    unsigned int fbp, fbw;
    int w, h;              /* PS2 pixels */
    GLuint fbo, color;
    GLuint depth_attached;
    int vram_dirty;        /* GS memory was written inside this buffer */
    int rendered;          /* drawn into since it last matched GS memory */
    unsigned int psm;      /* FRAME.PSM last drawn with */
    unsigned int last_use;
} RenderTarget;

typedef struct {
    int used;
    unsigned int zbp;
    int w, h;
    GLuint tex;
} DepthBuffer;

#define MAX_RT 16
#define MAX_DB 8

static RenderTarget rts[MAX_RT];
static DepthBuffer dbs[MAX_DB];
static unsigned int frame_counter;
static int dump_frame;

/* per-interval statistics for the periodic log */
static struct { unsigned int frames, draws, verts, rt_loads, rt_reads, tex_decodes, tex_texels; double t_load, t_read, t_decode; } stats;

double pcOsTime(void);

void pcGsLogStats(void)
{
    unsigned int f = stats.frames ? stats.frames : 1;

    pcLog("gs/frame: %u draws, %u verts, %.1f rt loads (%.1f ms), %.1f rt reads (%.1f ms), %.1f tex decodes (%u texels, %.1f ms)",
          stats.draws / f, stats.verts / f, (double)stats.rt_loads / f, stats.t_load * 1000 / f, (double)stats.rt_reads / f,
          stats.t_read * 1000 / f, (double)stats.tex_decodes / f, stats.tex_texels / f, stats.t_decode * 1000 / f);
    memset(&stats, 0, sizeof(stats));
}

static GLuint make_texture(int w, int h, GLint ifmt, GLenum fmt, GLenum type, GLint filter)
{
    GLuint t;

    glGenTextures(1, &t);
    glBindTexture(GL_TEXTURE_2D, t);
    glTexImage2D(GL_TEXTURE_2D, 0, ifmt, w, h, 0, fmt, type, NULL);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filter);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filter);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

    return t;
}

static void rt_load_from_vram(RenderTarget* rt);

static RenderTarget* find_rt(unsigned int fbp)
{
    int i;

    for (i = 0; i < MAX_RT; i++)
    {
        if (rts[i].used && (rts[i].fbp == fbp))
        {
            return &rts[i];
        }
    }

    return NULL;
}

static RenderTarget* get_rt(unsigned int fbp, unsigned int fbw)
{
    RenderTarget* rt = find_rt(fbp);
    int i, oldest = 0;

    if (fbw == 0)
    {
        fbw = 10;
    }

    if ((rt != NULL) && (rt->fbw == fbw))
    {
        return rt;
    }

    if (rt == NULL)
    {
        for (i = 0; i < MAX_RT; i++)
        {
            if (!rts[i].used)
            {
                break;
            }

            if (rts[i].last_use < rts[oldest].last_use)
            {
                oldest = i;
            }
        }

        rt = &rts[(i < MAX_RT) ? i : oldest];
    }

    if (rt->used)
    {
        glDeleteFramebuffers(1, &rt->fbo);
        glDeleteTextures(1, &rt->color);
    }

    memset(rt, 0, sizeof(*rt));
    rt->used = 1;
    rt->fbp = fbp;
    rt->fbw = fbw;
    rt->w = fbw * 64;
    rt->h = 512;
    rt->color = make_texture(rt->w * pcGsScale, rt->h * pcGsScale, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, GL_LINEAR);

    glGenFramebuffers(1, &rt->fbo);
    glBindFramebuffer(GL_FRAMEBUFFER, rt->fbo);
    glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, rt->color, 0);

    /* start from whatever GS memory holds there (normally cleared) */
    rt_load_from_vram(rt);

    pcLog("gs: render target FBP %u (%ux%u)", fbp, rt->w, rt->h);

    return rt;
}

static DepthBuffer* get_db(unsigned int zbp, int w, int h)
{
    int i;

    for (i = 0; i < MAX_DB; i++)
    {
        if (dbs[i].used && (dbs[i].zbp == zbp) && (dbs[i].w == w) && (dbs[i].h == h))
        {
            return &dbs[i];
        }
    }

    for (i = 0; i < MAX_DB; i++)
    {
        if (!dbs[i].used)
        {
            dbs[i].used = 1;
            dbs[i].zbp = zbp;
            dbs[i].w = w;
            dbs[i].h = h;
            dbs[i].tex = make_texture(w * pcGsScale, h * pcGsScale, GL_DEPTH_COMPONENT24, GL_DEPTH_COMPONENT,
                                      GL_UNSIGNED_INT, GL_NEAREST);
            return &dbs[i];
        }
    }

    return &dbs[0];
}

/* ------------------------------------------------------------------------------------------- */
/* texture cache (textures decoded from GS memory) */

typedef struct {
    int used;
    u64 key0, key1, key2;
    int w, h;
    unsigned int page_lo, page_hi, clut_page;
    unsigned long long gen;
    GLuint tex;
    unsigned int last_use;
} CachedTex;

#define MAX_TEX 2048

static CachedTex texcache[MAX_TEX];
static unsigned int* decode_buf;

static void psm_page_size(int psm, int* pw, int* ph)
{
    switch (gsPsmBits(psm))
    {
    case 32: *pw = 64; *ph = 32; break;
    case 16: *pw = 64; *ph = 64; break;
    case 8:  *pw = 128; *ph = 64; break;
    default: *pw = 128; *ph = 128; break;
    }
}

/* Content hash of GS memory pages (8 KB), recomputed only when a page's generation moves.
 * Textures are cached by content: the game streams many textures through the same memory every
 * frame, so the same bytes come back again and again at the same address. */
static unsigned long long page_hash[GS_PAGES];
static unsigned int page_hash_gen[GS_PAGES];
static unsigned char page_hash_valid[GS_PAGES];

static unsigned long long hash_page(unsigned int p)
{
    p &= GS_PAGES - 1;

    if (!page_hash_valid[p] || (page_hash_gen[p] != gsPageGen[p]))
    {
        const unsigned long long* w = (const unsigned long long*)&gsVram[p * 8192];
        unsigned long long h = 0x9E3779B97F4A7C15ULL ^ p;
        int i;

        for (i = 0; i < 8192 / 8; i++)
        {
            h = (h ^ w[i]) * 0x100000001B3ULL;
            h ^= h >> 29;
        }

        page_hash[p] = h;
        page_hash_gen[p] = gsPageGen[p];
        page_hash_valid[p] = 1;
    }

    return page_hash[p];
}

static unsigned long long gen_sum(unsigned int lo, unsigned int hi)
{
    unsigned long long s = 0;
    unsigned int p;

    for (p = lo; p <= hi; p++)
    {
        s = (s * 0x100000001B3ULL) ^ hash_page(p);
    }

    return s;
}

static GLuint get_texture(u64 tex0, u64 texa, u64 texclut, int w, int h)
{
    unsigned int psm = GSF(tex0, 20, 6);
    unsigned int tbp = GSF(tex0, 0, 14);
    unsigned int tbw = GSF(tex0, 14, 6);
    u64 key0 = tex0 & 0x0000001FFFFFFFFFULL & ~(0x3ULL << 35) & ~(1ULL << 34);   /* TBP..TH, drop TCC/TFX and CLUT fields */
    u64 cpsm_key = ((u64)GSF(tex0, 51, 4) << 37);
    u64 key1 = ((psm == GS_PSMCT24) || (gsPsmBits(psm) == 16) || (GSF(tex0, 51, 4) >= 2)) ? texa : 0;
    u64 key2 = gsClutHash(tex0);   /* palette contents (0-entry hash for direct color) */
    int pw, ph, i, slot = -1, oldest = 0;
    unsigned int page_lo, page_hi, clut_page, stride;
    unsigned long long gen;
    CachedTex* t;

    key0 |= cpsm_key;
    psm_page_size(psm, &pw, &ph);
    stride = (tbw * 64 + pw - 1) / pw;

    if (stride == 0)
    {
        stride = 1;
    }

    page_lo = tbp >> 5;
    page_hi = page_lo + ((h + ph - 1) / ph - 1) * stride + (w + pw - 1) / pw;
    clut_page = GSF(tex0, 37, 14) >> 5;
    gen = gen_sum(page_lo, page_hi);

    for (i = 0; i < MAX_TEX; i++)
    {
        t = &texcache[i];

        if (t->used && (t->gen == gen) && (t->key0 == key0) && (t->key1 == key1) && (t->key2 == key2) && (t->w == w) && (t->h == h))
        {
            t->last_use = frame_counter;
            return t->tex;
        }

        if (!t->used && (slot < 0))
        {
            slot = i;
        }

        if (t->last_use < texcache[oldest].last_use)
        {
            oldest = i;
        }
    }

    if (slot < 0)
    {
        slot = oldest;
        glDeleteTextures(1, &texcache[slot].tex);
    }

    t = &texcache[slot];
    t->used = 1;
    t->key0 = key0;
    t->key1 = key1;
    t->key2 = key2;
    t->w = w;
    t->h = h;
    t->page_lo = page_lo;
    t->page_hi = page_hi;
    t->clut_page = clut_page;
    t->gen = gen;
    t->last_use = frame_counter;
    t->tex = make_texture(w, h, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, GL_NEAREST);

    { double t0 = pcOsTime(); gsDecodeTexture(tex0, texa, texclut, w, h, decode_buf); stats.tex_decodes++; stats.tex_texels += w * h; stats.t_decode += pcOsTime() - t0; }
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, decode_buf);

    return t->tex;
}

/* ------------------------------------------------------------------------------------------- */
/* GS memory <-> render targets */

static int rt_overlaps(const RenderTarget* rt, unsigned int bp, unsigned int bw, unsigned int psm, unsigned int y, unsigned int h)
{
    unsigned int rt_lo = rt->fbp * 32;
    unsigned int rt_hi = rt_lo + (rt->w / 64) * (rt->h / 32) * 32;
    unsigned int lo = bp + (y / 32) * bw * 32;
    unsigned int hi = bp + ((y + h + 31) / 32) * (bw ? bw : 1) * 32;

    (void)psm;

    return (lo < rt_hi) && (hi > rt_lo);
}

static void rt_load_from_vram(RenderTarget* rt)
{
    double t0 = pcOsTime();
    int x, y, sx, sy;
    unsigned int* buf = malloc(rt->w * rt->h * 4 * pcGsScale * pcGsScale);

    for (y = 0; y < rt->h; y++)
    {
        for (x = 0; x < rt->w; x++)
        {
            unsigned int c = gsMemRead(GS_PSMCT32, rt->fbp * 32, rt->fbw, x, y);
            unsigned int a = (c >> 24) >= 0x80 ? 255 : (c >> 24) * 2;

            c = (c & 0xFFFFFF) | (a << 24);

            for (sy = 0; sy < pcGsScale; sy++)
            {
                for (sx = 0; sx < pcGsScale; sx++)
                {
                    buf[(y * pcGsScale + sy) * rt->w * pcGsScale + x * pcGsScale + sx] = c;
                }
            }
        }
    }

    glBindTexture(GL_TEXTURE_2D, rt->color);
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, rt->w * pcGsScale, rt->h * pcGsScale, GL_RGBA, GL_UNSIGNED_BYTE, buf);
    free(buf);

    rt->vram_dirty = 0;
    rt->rendered = 0;
    stats.rt_loads++;
    stats.t_load += pcOsTime() - t0;
}

void gsXferDone(unsigned int bp, unsigned int bw, unsigned int psm, unsigned int x, unsigned int y, unsigned int w, unsigned int h, int changed)
{
    int i;

    (void)x; (void)w;

    for (i = 0; i < MAX_RT; i++)
    {
        if (rts[i].used && (changed || rts[i].rendered) && rt_overlaps(&rts[i], bp, bw, psm, y, h))
        {
            rts[i].vram_dirty = 1;
        }
    }
}

/* Before the GS memory of a render target is read (local->local / local->host transfers),
 * copy the rendered pixels back into GS memory. */
void gsFlushLocalToLocal(unsigned int bp, unsigned int bw, unsigned int psm, unsigned int x, unsigned int y, unsigned int w, unsigned int h)
{
    int i;

    gsRendererFlush();

    for (i = 0; i < MAX_RT; i++)
    {
        RenderTarget* rt = &rts[i];

        if (rt->used && rt_overlaps(rt, bp, bw, psm, y, h))
        {
            unsigned int* buf = malloc(rt->w * rt->h * 4 * pcGsScale * pcGsScale);
            int px, py;
            double t0 = pcOsTime();

            glBindFramebuffer(GL_FRAMEBUFFER, rt->fbo);
            glPixelStorei(GL_PACK_ALIGNMENT, 4);
            glReadPixels(0, 0, rt->w * pcGsScale, rt->h * pcGsScale, GL_RGBA, GL_UNSIGNED_BYTE, buf);

            for (py = 0; py < rt->h; py++)
            {
                for (px = 0; px < rt->w; px++)
                {
                    unsigned int c = buf[(py * pcGsScale) * rt->w * pcGsScale + px * pcGsScale];
                    unsigned int a = ((c >> 24) * 128 + 127) / 255;

                    gsMemWrite(GS_PSMCT32, rt->fbp * 32, rt->fbw, px, py, (c & 0xFFFFFF) | (a << 24));
                }
            }

            free(buf);
            rt->vram_dirty = 0;
            rt->rendered = 0;
            stats.rt_reads++;
            stats.t_read += pcOsTime() - t0;
        }
    }

    (void)x; (void)w;
}

/* ------------------------------------------------------------------------------------------- */

void gsRendererInit(void)
{
    if (!pcGlLoad())
    {
        pcPlatformFatal("OpenGL 3.3 is required.");
    }

    decode_buf = malloc(1024 * 1024 * 4);

    prog = link(vs_src, fs_src);
    present_prog = link(present_vs, present_fs);

#define UL(f, n) U.f = glGetUniformLocation(prog, n)
    UL(target, "uTarget"); UL(tme, "uTme"); UL(fst, "uFst"); UL(tfx, "uTfx"); UL(tcc, "uTcc"); UL(iip, "uIip");
    UL(texrt, "uTexRT"); UL(texsize, "uTexSize"); UL(rtsize, "uRTSize"); UL(wrap, "uWrap"); UL(region, "uRegion");
    UL(linear, "uLinear"); UL(ate, "uAte"); UL(atst, "uAtst"); UL(aref, "uAref"); UL(atinv, "uAtestInvert");
    UL(fge, "uFge"); UL(fogcol, "uFogCol"); UL(tex, "uTex"); UL(texa, "uTexa");
#undef UL

    glGenVertexArrays(1, &vao);
    glBindVertexArray(vao);
    glGenBuffers(1, &vbo);
    glBindBuffer(GL_ARRAY_BUFFER, vbo);

    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, sizeof(GlVertex), (void*)offsetof(GlVertex, x));
    glVertexAttribPointer(1, 4, GL_UNSIGNED_BYTE, GL_TRUE, sizeof(GlVertex), (void*)offsetof(GlVertex, r));
    glVertexAttribPointer(2, 3, GL_FLOAT, GL_FALSE, sizeof(GlVertex), (void*)offsetof(GlVertex, s));
    glVertexAttribPointer(3, 2, GL_FLOAT, GL_FALSE, sizeof(GlVertex), (void*)offsetof(GlVertex, u));
    glVertexAttribPointer(4, 1, GL_FLOAT, GL_FALSE, sizeof(GlVertex), (void*)offsetof(GlVertex, fog));

    for (int i = 0; i < 5; i++)
    {
        glEnableVertexAttribArray(i);
    }

    pcLog("gs: renderer ready (internal resolution x%d)", pcGsScale);
}

void gsRendererFlush(void)
{
    extern void gsFlushPrims(void);

    gsFlushPrims();
}

static float z_scale(u64 zbuf)
{
    switch (GSF(zbuf, 24, 4))
    {
    case 0: return 1.0f / 4294967296.0f;   /* Z32 */
    case 1: return 1.0f / 16777216.0f;     /* Z24 */
    default: return 1.0f / 65536.0f;       /* Z16 / Z16S */
    }
}

static GLenum blend_factor(int c, int src_side)
{
    (void)src_side;

    switch (c)
    {
    case 0: return GL_SRC_ALPHA;
    case 1: return GL_DST_ALPHA;
    default: return GL_CONSTANT_ALPHA;
    }
}

static GLenum one_minus(GLenum f)
{
    switch (f)
    {
    case GL_SRC_ALPHA: return GL_ONE_MINUS_SRC_ALPHA;
    case GL_DST_ALPHA: return GL_ONE_MINUS_DST_ALPHA;
    default: return GL_ONE_MINUS_CONSTANT_ALPHA;
    }
}

/* GS blending: ((A - B) * C >> 7) + D with A, B, D in {Cs, Cd, 0}, C in {As, Ad, FIX} */
static void setup_blend(const GsDrawState* st)
{
    int A = GSF(st->alpha, 0, 2), B = GSF(st->alpha, 2, 2), C = GSF(st->alpha, 4, 2), D = GSF(st->alpha, 6, 2);
    GLenum f = blend_factor(C, 1);
    GLenum src = GL_ZERO, dst = GL_ZERO, eq = GL_FUNC_ADD;

    if (!st->abe)
    {
        glDisable(GL_BLEND);
        return;
    }

    glEnable(GL_BLEND);
    glBlendColor(0, 0, 0, GSF(st->alpha, 32, 8) / 128.0f);

    if (A == B)
    {
        /* result is D */
        src = (D == 0) ? GL_ONE : GL_ZERO;
        dst = (D == 1) ? GL_ONE : GL_ZERO;
    }
    else if ((A == 0) && (B == 1))          /* (Cs - Cd) * C + D */
    {
        if (D == 1)      { src = f; dst = one_minus(f); }
        else if (D == 0) { src = GL_ONE; dst = GL_ZERO; /* approx: Cs + (Cs - Cd)C */ src = f; dst = one_minus(f); }
        else             { src = f; dst = f; eq = GL_FUNC_SUBTRACT; }
    }
    else if ((A == 1) && (B == 0))          /* (Cd - Cs) * C + D */
    {
        if (D == 0)      { src = one_minus(f); dst = f; }
        else if (D == 1) { src = f; dst = GL_ONE; eq = GL_FUNC_REVERSE_SUBTRACT; /* approx: Cd - Cs*C */ }
        else             { src = f; dst = f; eq = GL_FUNC_REVERSE_SUBTRACT; }
    }
    else if ((A == 0) && (B == 2))          /* Cs * C + D */
    {
        src = f;
        dst = (D == 1) ? GL_ONE : GL_ZERO;
        if (D == 0) { src = f; dst = GL_ZERO; /* Cs*C + Cs approx */ }
    }
    else if ((A == 1) && (B == 2))          /* Cd * C + D */
    {
        dst = f;
        src = (D == 0) ? GL_ONE : GL_ZERO;
    }
    else if ((A == 2) && (B == 0))          /* -Cs * C + D */
    {
        src = f;
        dst = (D == 1) ? GL_ONE : GL_ZERO;
        eq = GL_FUNC_REVERSE_SUBTRACT;
    }
    else                                     /* (0 - Cd) * C + D */
    {
        dst = f;
        src = (D == 0) ? GL_ONE : GL_ZERO;
        eq = GL_FUNC_SUBTRACT;
    }

    /* the written alpha is As (never blended) */
    glBlendEquationSeparate(eq, GL_FUNC_ADD);
    glBlendFuncSeparate(src, dst, GL_ONE, GL_ZERO);
}

void gsRendererDraw(const GsDrawState* st, const GsVertex* v, int nverts)
{
    unsigned int fbp = GSF(st->frame, 0, 9);
    unsigned int fbw = GSF(st->frame, 16, 6);
    unsigned int fbmsk = GSF(st->frame, 32, 32);
    unsigned int fpsm = GSF(st->frame, 24, 6);
    RenderTarget* rt = get_rt(fbp, fbw);
    int ztst = GSF(st->test, 17, 2);
    int zmsk = GSF(st->zbuf, 32, 1);
    int use_depth = (ztst != 1) || !zmsk;
    float zs = z_scale(st->zbuf);
    int afail = GSF(st->test, 12, 2);
    int ate = GSF(st->test, 0, 1);
    int atst = GSF(st->test, 1, 3);
    GLenum mode;
    int n = 0, i;

    rt->last_use = frame_counter;
    stats.draws++;
    stats.verts += nverts;

    if (dump_frame)
    {
        float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
        unsigned int z0 = 0xFFFFFFFF, z1 = 0;

        for (i = 0; i < nverts; i++)
        {
            if (v[i].x < x0) x0 = v[i].x;
            if (v[i].x > x1) x1 = v[i].x;
            if (v[i].y < y0) y0 = v[i].y;
            if (v[i].y > y1) y1 = v[i].y;
            if (v[i].z < z0) z0 = v[i].z;
            if (v[i].z > z1) z1 = v[i].z;
        }

        pcLog("draw fb %u/%u psm %u msk %08x type %d n %d iip %d tme %d fst %d abe %d fge %d | tex0 %016llx tex1 %llx clamp %llx"
              " | alpha %010llx test %05llx zbuf %llx sc %016llx texa %llx | x %.1f..%.1f y %.1f..%.1f z %u..%u c0 %02x%02x%02x%02x q %g st %g,%g",
              fbp, fbw, fpsm, fbmsk, st->type, nverts, st->iip, st->tme, st->fst, st->abe, st->fge,
              st->tex0, st->tex1, st->clamp, st->alpha, st->test, st->zbuf, st->scissor, st->texa,
              x0, x1, y0, y1, z0, z1, v[0].r, v[0].g, v[0].b, v[0].a, v[0].q, v[0].s, v[0].t);
    }

    if (rt->vram_dirty)
    {
        /* a full-screen opaque untextured sprite overwrites everything anyway (the shadow
         * buffer is cleared like this every frame after textures were streamed over it) */
        int full_clear = (st->type == 6) && !st->tme && !st->abe && (fbmsk == 0) && (nverts == 2) &&
                         (fminf(v[0].x, v[1].x) <= 0) && (fminf(v[0].y, v[1].y) <= 0) &&
                         (fmaxf(v[0].x, v[1].x) >= 640) && (fmaxf(v[0].y, v[1].y) >= 480);

        if (full_clear)
        {
            rt->vram_dirty = 0;
        }
        else
        {
            rt_load_from_vram(rt);
        }
    }

    rt->rendered = 1;
    rt->psm = fpsm;

    /* build GL vertices */
    if (st->type == 6)
    {
        for (i = 0; i + 1 < nverts && n + 6 <= MAX_GL_VERTS; i += 2)
        {
            const GsVertex* a = &v[i];
            const GsVertex* b = &v[i + 1];
            GlVertex c[4];
            int k;

            for (k = 0; k < 4; k++)
            {
                const GsVertex* sx = (k & 1) ? b : a;
                const GsVertex* sy = (k & 2) ? b : a;

                c[k].x = sx->x;
                c[k].y = sy->y;
                c[k].z = b->z * zs;
                c[k].r = b->r; c[k].g = b->g; c[k].b = b->b; c[k].a = b->a;
                c[k].s = sx->s; c[k].t = sy->t; c[k].q = b->q;
                c[k].u = sx->u; c[k].v = sy->v;
                c[k].fog = b->f / 255.0f;
            }

            glverts[n++] = c[0]; glverts[n++] = c[1]; glverts[n++] = c[2];
            glverts[n++] = c[1]; glverts[n++] = c[3]; glverts[n++] = c[2];
        }

        mode = GL_TRIANGLES;
    }
    else
    {
        for (i = 0; i < nverts && n < MAX_GL_VERTS; i++)
        {
            GlVertex* o = &glverts[n++];

            o->x = v[i].x;
            o->y = v[i].y;
            o->z = v[i].z * zs;
            o->r = v[i].r; o->g = v[i].g; o->b = v[i].b; o->a = v[i].a;
            o->s = v[i].s; o->t = v[i].t; o->q = v[i].q;
            o->u = v[i].u; o->v = v[i].v;
            o->fog = v[i].f / 255.0f;
        }

        mode = (st->type == 0) ? GL_POINTS : ((st->type <= 2) ? GL_LINES : GL_TRIANGLES);
    }

    for (i = 0; i < n; i++)
    {
        if (glverts[i].z > 1.0f) glverts[i].z = 1.0f;
    }

    glBindFramebuffer(GL_FRAMEBUFFER, rt->fbo);
    glViewport(0, 0, rt->w * pcGsScale, rt->h * pcGsScale);

    /* depth */
    if (use_depth)
    {
        DepthBuffer* db = get_db(GSF(st->zbuf, 0, 9), rt->w, rt->h);

        if (rt->depth_attached != db->tex)
        {
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, db->tex, 0);
            rt->depth_attached = db->tex;
        }

        glEnable(GL_DEPTH_TEST);
        glDepthFunc((ztst == 0) ? GL_NEVER : (ztst == 1) ? GL_ALWAYS : (ztst == 2) ? GL_GEQUAL : GL_GREATER);
        glDepthMask(zmsk ? GL_FALSE : GL_TRUE);
    }
    else
    {
        glDisable(GL_DEPTH_TEST);
        glDepthMask(GL_FALSE);
    }

    /* scissor */
    {
        int x0 = GSF(st->scissor, 0, 11), x1 = GSF(st->scissor, 16, 11);
        int y0 = GSF(st->scissor, 32, 11), y1 = GSF(st->scissor, 48, 11);

        glEnable(GL_SCISSOR_TEST);
        glScissor(x0 * pcGsScale, y0 * pcGsScale, (x1 - x0 + 1) * pcGsScale, (y1 - y0 + 1) * pcGsScale);
    }

    glColorMask((fbmsk & 0xFF) != 0xFF, (fbmsk & 0xFF00) != 0xFF00, (fbmsk & 0xFF0000) != 0xFF0000,
                ((fbmsk & 0xFF000000) != 0xFF000000) && (fpsm != GS_PSMCT24));

    setup_blend(st);

    glUseProgram(prog);
    glUniform2f(U.target, (float)rt->w, (float)rt->h);
    glUniform1i(U.iip, (st->type == 6) ? 0 : st->iip);
    glUniform1i(U.tme, st->tme);
    glUniform1i(U.fst, st->fst);
    glUniform1i(U.fge, st->fge);
    glUniform3f(U.fogcol, (float)GSF(st->fogcol, 0, 8), (float)GSF(st->fogcol, 8, 8), (float)GSF(st->fogcol, 16, 8));
    glUniform1i(U.ate, ate);
    glUniform1i(U.atst, atst);
    glUniform1i(U.aref, GSF(st->test, 4, 8));
    glUniform1i(U.atinv, 0);
    glUniform1i(U.tex, 0);

    if (st->tme)
    {
        int tw = 1 << GSF(st->tex0, 26, 4), th = 1 << GSF(st->tex0, 30, 4);
        unsigned int tbp = GSF(st->tex0, 0, 14);
        unsigned int tpsm = GSF(st->tex0, 20, 6);
        RenderTarget* src = NULL;
        int linear = GSF(st->tex1, 5, 1);

        if (tw > 1024) tw = 1024;
        if (th > 1024) th = 1024;

        if ((tbp & 31) == 0)
        {
            src = find_rt(tbp >> 5);

            /* a render target is sampled directly when the texture reads it in its own pixel size */
            if ((src != NULL) && ((gsPsmBits(tpsm) != gsPsmBits(src->psm)) ||
                                  ((tpsm != GS_PSMCT32) && (tpsm != GS_PSMCT24) && (tpsm != GS_PSMCT16) && (tpsm != GS_PSMCT16S))))
            {
                src = NULL;
            }
        }

        glActiveTexture(GL_TEXTURE0);

        if ((src != NULL) && (src != rt))
        {
            if (src->vram_dirty)
            {
                rt_load_from_vram(src);
                glBindFramebuffer(GL_FRAMEBUFFER, rt->fbo);
            }

            glBindTexture(GL_TEXTURE_2D, src->color);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, linear ? GL_LINEAR : GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, linear ? GL_LINEAR : GL_NEAREST);
            glUniform1i(U.texrt, (gsPsmBits(tpsm) == 16) ? 2 : 1);
            glUniform2f(U.rtsize, (float)src->w, (float)src->h);
            glUniform3f(U.texa, (float)GSF(st->texa, 0, 8), (float)GSF(st->texa, 32, 8), (float)GSF(st->texa, 15, 1));
        }
        else
        {
            glBindTexture(GL_TEXTURE_2D, get_texture(st->tex0, st->texa, st->texclut, tw, th));
            glUniform1i(U.texrt, 0);
        }

        glUniform2f(U.texsize, (float)tw, (float)th);
        glUniform1i(U.tfx, GSF(st->tex0, 35, 2));
        glUniform1i(U.tcc, GSF(st->tex0, 34, 1));
        glUniform2i(U.wrap, GSF(st->clamp, 0, 2), GSF(st->clamp, 2, 2));
        glUniform4i(U.region, GSF(st->clamp, 4, 10), GSF(st->clamp, 14, 10), GSF(st->clamp, 24, 10), GSF(st->clamp, 34, 10));
        glUniform1i(U.linear, linear);
    }

    glBindVertexArray(vao);
    glBindBuffer(GL_ARRAY_BUFFER, vbo);
    glBufferData(GL_ARRAY_BUFFER, n * sizeof(GlVertex), glverts, GL_STREAM_DRAW);

    if (ate && (afail != 0) && (atst != 1))
    {
        /* alpha test with a fail action: pass 1 normal, pass 2 the failing pixels with the
         * buffers AFAIL still allows (FB_ONLY: color, ZB_ONLY: depth, RGB_ONLY: rgb) */
        glDrawArrays(mode, 0, n);

        glUniform1i(U.atinv, 1);

        if (afail == 1)
        {
            glDepthMask(GL_FALSE);
        }
        else if (afail == 2)
        {
            glColorMask(GL_FALSE, GL_FALSE, GL_FALSE, GL_FALSE);
        }
        else
        {
            glDepthMask(GL_FALSE);
            glColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_FALSE);
        }

        glDrawArrays(mode, 0, n);
    }
    else
    {
        glDrawArrays(mode, 0, n);
    }
}

/* ------------------------------------------------------------------------------------------- */

void gsRendererPresent(unsigned int dispfb_fbp, unsigned int dispfb_fbw)
{
    RenderTarget* rt = find_rt(dispfb_fbp);
    int ww, wh, vw, vh;
    GlVertex q[6];
    float tw, th;
    int i;

    gsRendererFlush();
    frame_counter++;
    stats.frames++;

    /* debug: RECVX_GSDUMP=<frame> logs every draw of that frame (and the next one) */
    {
        static int dump_at = -2;

        if (dump_at == -2)
        {
            const char* e = SDL_getenv("RECVX_GSDUMP");

            dump_at = (e != NULL) ? atoi(e) : -1;
        }

        if (dump_frame)
        {
            extern void pcGsScreenshot(const char* path, unsigned int fbp);

            pcLog("present fbp %u fbw %u", dispfb_fbp, dispfb_fbw);
            pcGsScreenshot("gsdump.png", dispfb_fbp);
        }

        {
            extern int gsDumpFrame;

            gsDumpFrame = (dump_at >= 0) && ((frame_counter == (unsigned int)dump_at) || (frame_counter == (unsigned int)dump_at + 1));
        }

        dump_frame = (dump_at >= 0) && ((frame_counter == (unsigned int)dump_at) || (frame_counter == (unsigned int)dump_at + 1));
    }

    SDL_GL_GetDrawableSize(SDL_GL_GetCurrentWindow(), &ww, &wh);

    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    glViewport(0, 0, ww, wh);
    glDisable(GL_SCISSOR_TEST);
    glDisable(GL_DEPTH_TEST);
    glDisable(GL_BLEND);
    glColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE);
    glClearColor(0, 0, 0, 1);
    glClear(GL_COLOR_BUFFER_BIT);

    if (rt == NULL)
    {
        return;
    }

    if (rt->vram_dirty)
    {
        rt_load_from_vram(rt);
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
    }

    /* 640x480 shown at 4:3 */
    vh = wh;
    vw = wh * 4 / 3;

    if (vw > ww)
    {
        vw = ww;
        vh = ww * 3 / 4;
    }

    glViewport((ww - vw) / 2, (wh - vh) / 2, vw, vh);

    tw = 640.0f / rt->w;
    th = 480.0f / rt->h;

    memset(q, 0, sizeof(q));
    {
        float px[4] = { -1, 1, -1, 1 }, py[4] = { 1, 1, -1, -1 };
        float ts[4] = { 0, tw, 0, tw }, tt[4] = { 0, 0, th, th };
        int idx[6] = { 0, 1, 2, 1, 3, 2 };

        for (i = 0; i < 6; i++)
        {
            q[i].x = px[idx[i]];
            q[i].y = py[idx[i]];
            q[i].s = ts[idx[i]];
            q[i].t = tt[idx[i]];
        }
    }

    glUseProgram(present_prog);
    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, rt->color);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glBindVertexArray(vao);
    glBindBuffer(GL_ARRAY_BUFFER, vbo);
    glBufferData(GL_ARRAY_BUFFER, sizeof(q), q, GL_STREAM_DRAW);
    glDrawArrays(GL_TRIANGLES, 0, 6);

    (void)dispfb_fbw;
}

/* Debug: save a render target (PS2 resolution, top row first) as PNG. */
int pcWritePng(const char* path, const unsigned char* rgb, int w, int h);

void pcGsScreenshot(const char* path, unsigned int fbp)
{
    RenderTarget* rt = find_rt(fbp);
    unsigned char* px;
    unsigned char* rgb;
    int W, H, x, y;

    if (rt == NULL)
    {
        return;
    }

    gsRendererFlush();

    W = rt->w * pcGsScale;
    H = rt->h * pcGsScale;
    px = malloc(W * H * 4);
    rgb = malloc(640 * 480 * 3);

    glBindFramebuffer(GL_FRAMEBUFFER, rt->fbo);
    glPixelStorei(GL_PACK_ALIGNMENT, 4);
    glReadPixels(0, 0, W, H, GL_RGBA, GL_UNSIGNED_BYTE, px);

    for (y = 0; y < 480; y++)
    {
        for (x = 0; x < 640; x++)
        {
            const unsigned char* s = &px[((y * pcGsScale) * W + x * pcGsScale) * 4];

            rgb[(y * 640 + x) * 3 + 0] = s[0];
            rgb[(y * 640 + x) * 3 + 1] = s[1];
            rgb[(y * 640 + x) * 3 + 2] = s[2];
        }
    }

    pcWritePng(path, rgb, 640, 480);

    free(px);
    free(rgb);
}

unsigned int pcGsDisplayFbp(void);
