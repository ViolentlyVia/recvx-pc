/* PC port, GS emulator: HD texture replacement using PCSX2 texture packs.
 *
 * PCSX2 names a replacement "<texture hash>-<palette hash>-<bits>.dds" where the texture hash is
 * XXH3-64 over the GS memory blocks the texture covers (in block order, 256 bytes each), the
 * palette hash is XXH3-64 over the palette expanded to 32 bits (16 or 256 entries) and bits holds
 * PSM | TW << 6 | TH << 10 | TEXA (TEXA only for non-palette formats). This computes the same
 * hashes for paletted 4/8-bit textures (what these packs contain) and loads matching DDS files
 * (BC1/BC3/BC7) on first use.
 *
 * The pack folder comes from RECVX_HDTEX or an "hd_textures=" line in recvx.ini.
 */
#include <io.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define XXH_INLINE_ALL
#include "../../third_party/xxhash.h"

#include "gs_internal.h"
#include "gl_funcs.h"
#include "../../include/pc_platform.h"

typedef struct {
    unsigned long long tex_hash, clut_hash;
    unsigned int bits;
    char* file;
    GLuint tex;          /* loaded texture, 0 = not loaded yet */
    int w, h;
    int failed;
} Replacement;

static Replacement* reps;
static int nreps;
static char rep_dir[1024];
static int enabled;
static int have_bptc, have_s3tc;
static unsigned int hits;

static int cmp_rep(const void* a, const void* b)
{
    const Replacement* x = (const Replacement*)a;
    const Replacement* y = (const Replacement*)b;

    if (x->tex_hash != y->tex_hash) return (x->tex_hash < y->tex_hash) ? -1 : 1;
    if (x->clut_hash != y->clut_hash) return (x->clut_hash < y->clut_hash) ? -1 : 1;
    if (x->bits != y->bits) return (x->bits < y->bits) ? -1 : 1;
    return 0;
}

static void find_dir(void)
{
    const char* e = getenv("RECVX_HDTEX");
    if (e != NULL)
    {
        snprintf(rep_dir, sizeof(rep_dir), "%s", e);
        return;
    }

    pcIniGet("hd_textures", rep_dir, sizeof(rep_dir));
}

static void check_extensions(void)
{
    GLint n = 0, i;

    glGetIntegerv(GL_NUM_EXTENSIONS, &n);

    for (i = 0; i < n; i++)
    {
        const char* ext = (const char*)glGetStringi(GL_EXTENSIONS, (GLuint)i);

        if (ext == NULL) continue;
        if (strcmp(ext, "GL_ARB_texture_compression_bptc") == 0) have_bptc = 1;
        if (strcmp(ext, "GL_EXT_texture_compression_s3tc") == 0) have_s3tc = 1;
    }
}

void gsReplaceInit(void)
{
    char pattern[1100];
    struct _finddata_t fd;
    intptr_t h;
    int cap = 0;

    find_dir();

    if (rep_dir[0] == 0)
    {
        return;
    }

    check_extensions();

    snprintf(pattern, sizeof(pattern), "%s/*.dds", rep_dir);
    h = _findfirst(pattern, &fd);

    if (h == -1)
    {
        pcLog("hd textures: no .dds files in %s", rep_dir);
        return;
    }

    do
    {
        Replacement r;
        unsigned long long a, b;
        unsigned int bits;
        char extra;

        memset(&r, 0, sizeof(r));

        /* region textures ("-r<w>x<h>-") are not supported */
        if (sscanf(fd.name, "%llx-%llx-%x.%c", &a, &b, &bits, &extra) == 4)
        {
            r.tex_hash = a;
            r.clut_hash = b;
            r.bits = bits;
        }
        else if (sscanf(fd.name, "%llx-%x.%c", &a, &bits, &extra) == 3)
        {
            r.tex_hash = a;
            r.bits = bits;
        }
        else
        {
            continue;
        }

        if (nreps == cap)
        {
            cap = cap ? cap * 2 : 256;
            reps = realloc(reps, cap * sizeof(*reps));
        }

        r.file = _strdup(fd.name);
        reps[nreps++] = r;
    } while (_findnext(h, &fd) == 0);

    _findclose(h);

    qsort(reps, nreps, sizeof(*reps), cmp_rep);
    enabled = (nreps > 0);

    pcLog("hd textures: %d replacements in %s (BC7 %s, BC1-3 %s)", nreps, rep_dir, have_bptc ? "yes" : "NO",
          have_s3tc ? "yes" : "NO");
}

/* ------------------------------------------------------------------------------------------- */
/* DDS loading */

static GLuint load_dds(Replacement* r)
{
    char path[1400];
    FILE* f;
    long size;
    unsigned char* data;
    unsigned int h, w, mips, fourcc, offset = 128, dxgi = 0, block_bytes;
    GLenum fmt;
    GLuint tex;
    unsigned int level;
    size_t pos;

    snprintf(path, sizeof(path), "%s/%s", rep_dir, r->file);

    if ((f = fopen(path, "rb")) == NULL)
    {
        return 0;
    }

    fseek(f, 0, SEEK_END);
    size = ftell(f);
    fseek(f, 0, SEEK_SET);
    data = malloc(size);

    if (fread(data, 1, size, f) != (size_t)size)
    {
        fclose(f);
        free(data);
        return 0;
    }

    fclose(f);

    if ((size < 128) || (memcmp(data, "DDS ", 4) != 0))
    {
        free(data);
        return 0;
    }

    memcpy(&h, data + 12, 4);
    memcpy(&w, data + 16, 4);
    memcpy(&mips, data + 28, 4);
    memcpy(&fourcc, data + 84, 4);

    if (mips == 0)
    {
        mips = 1;
    }

    if (fourcc == 0x30315844) /* "DX10" */
    {
        memcpy(&dxgi, data + 128, 4);
        offset = 148;
    }

    if ((fourcc == 0x31545844) && have_s3tc)                     /* DXT1 */
    {
        fmt = PC_GL_COMPRESSED_RGBA_S3TC_DXT1;
        block_bytes = 8;
    }
    else if ((fourcc == 0x33545844) && have_s3tc)                /* DXT3 */
    {
        fmt = PC_GL_COMPRESSED_RGBA_S3TC_DXT3;
        block_bytes = 16;
    }
    else if ((fourcc == 0x35545844) && have_s3tc)                /* DXT5 */
    {
        fmt = PC_GL_COMPRESSED_RGBA_S3TC_DXT5;
        block_bytes = 16;
    }
    else if (((dxgi == 98) || (dxgi == 99)) && have_bptc)        /* BC7 */
    {
        fmt = PC_GL_COMPRESSED_RGBA_BPTC_UNORM;
        block_bytes = 16;
    }
    else if (((dxgi == 77) || (dxgi == 78)) && have_s3tc)        /* BC3 */
    {
        fmt = PC_GL_COMPRESSED_RGBA_S3TC_DXT5;
        block_bytes = 16;
    }
    else if (((dxgi == 71) || (dxgi == 72)) && have_s3tc)        /* BC1 */
    {
        fmt = PC_GL_COMPRESSED_RGBA_S3TC_DXT1;
        block_bytes = 8;
    }
    else
    {
        pcLog("hd textures: %s: unsupported DDS format (fourcc %08x dxgi %u)", r->file, fourcc, dxgi);
        free(data);
        return 0;
    }

    while (glGetError() != 0) {}

    glGenTextures(1, &tex);
    glBindTexture(GL_TEXTURE_2D, tex);

    pos = offset;

    for (level = 0; level < mips; level++)
    {
        unsigned int lw = (w >> level) ? (w >> level) : 1;
        unsigned int lh = (h >> level) ? (h >> level) : 1;
        unsigned int bytes = ((lw + 3) / 4) * ((lh + 3) / 4) * block_bytes;

        if (pos + bytes > (size_t)size)
        {
            break;
        }

        glCompressedTexImage2D(GL_TEXTURE_2D, level, fmt, lw, lh, 0, bytes, data + pos);
        pos += bytes;
    }

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, (level > 0) ? (GLint)level - 1 : 0);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (level > 1) ? GL_LINEAR_MIPMAP_LINEAR : GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);

    free(data);

    if (glGetError() != 0)
    {
        glDeleteTextures(1, &tex);
        pcLog("hd textures: %s: upload failed", r->file);
        return 0;
    }

    r->w = (int)w;
    r->h = (int)h;

    return tex;
}

/* ------------------------------------------------------------------------------------------- */
/* lookup */

static unsigned long long hash_texture(unsigned int psm, unsigned int tbp, unsigned int tbw, int tw, int th)
{
    XXH3_state_t st;
    int bx = (psm == GS_PSMT4) ? 32 : (psm == GS_PSMT8) ? 16 : 8;
    int by = (psm == GS_PSMCT32) ? 8 : 16;
    int x, y;

    XXH3_64bits_reset(&st);

    for (y = 0; y < th; y += by)
    {
        for (x = 0; x < tw; x += bx)
        {
            int b = gsBlockNumber(psm, tbp, tbw, x, y);

            XXH3_64bits_update(&st, &gsVram[b * 256], 256);
        }
    }

    return XXH3_64bits_digest(&st);
}

/* Returns the replacement texture for a texture about to be decoded, or 0. */
GLuint gsReplaceLookup(u64 tex0, u64 texa)
{
    unsigned int psm = GSF(tex0, 20, 6);
    unsigned int tw = GSF(tex0, 26, 4), th = GSF(tex0, 30, 4);
    int entries = (psm == GS_PSMT8) ? 256 : (psm == GS_PSMT4) ? 16 : 0;
    unsigned int clut[256];
    Replacement key, *r;

    if (!enabled || (entries == 0) || (tw > 10) || (th > 10))
    {
        return 0;
    }

    /* below one block PCSX2 hashes the expanded texels; not supported (none in these packs) */
    if (((1 << tw) < ((psm == GS_PSMT4) ? 32 : 16)) || ((1 << th) < 16))
    {
        return 0;
    }

    gsReadClut32(tex0, texa, entries, clut);

    memset(&key, 0, sizeof(key));
    key.tex_hash = hash_texture(psm, GSF(tex0, 0, 14), GSF(tex0, 14, 6), 1 << tw, 1 << th);
    key.clut_hash = XXH3_64bits(clut, entries * 4);
    key.bits = psm | (tw << 6) | (th << 10);

    r = bsearch(&key, reps, nreps, sizeof(*reps), cmp_rep);

    if ((r == NULL) || r->failed)
    {
        return 0;
    }

    if (r->tex == 0)
    {
        r->tex = load_dds(r);

        if (r->tex == 0)
        {
            r->failed = 1;
            return 0;
        }

        hits++;
        pcLog("hd textures: using %s (%dx%d)", r->file, r->w, r->h);
    }

    return r->tex;
}

int gsReplaceEnabled(void)
{
    return enabled;
}
