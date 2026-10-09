/* PC port: movie playback (replaces ps2_MovieFunc.c).
 *
 * The game drives movies through the Sofdec wrapper (ps2_sfd_mw.c), which calls the functions
 * below like the PS2 IPU player: initAll() when a movie starts, readMpeg() until a frame is
 * ready, setImageTag() to upload it to GS memory and vbrank_draw() to put it on screen, and
 * termAll() at the end. Frames come from MV_*.MPG (made by pc/tools/convert_movies.py) in the
 * extracted data folder, decoded by pc_mpeg.c; they are uploaded and drawn with the same GS
 * packets as on the PS2. Without converted movies (e.g. playing from the ISO) a movie reports
 * PREP -> PLAYING -> PLAYEND at once so the game moves on.
 *
 * Upscaled movies (MOVIE_HD/MV_*.MPG, made by recvx-config.exe's "Upscale movies") are used when
 * present and movies_hd= is not 0 in recvx.ini. Their frames are 4x the originals and do not fit
 * in GS memory: the game still draws its sprite as on the PS2, and the renderer samples the
 * upscaled frame instead (pcGsMovieFrame), the way HD texture packs replace textures.
 */
#include "../../include/ps2/veronica/prog/ps2_MovieFunc.h"
#include "../../include/ps2/veronica/prog/main.h"

#include "pc_platform.h"

extern unsigned int Ps2_vcount;

RMI_WORK rmi;
MDSIZE_WORK mdSize;
int movie_draw;
StrFile infile;
u_long128 test_tag[1400];
VoBuf voBuf;

/* MWE_PLY_STAT */
#define MOVIE_STATE_PREP    1
#define MOVIE_STATE_PLAYING 2
#define MOVIE_STATE_END     3

/* GS memory the PS2 player decodes into (TBP 14208, TBW 5 = 320 pixels, CT32) */
#define MOVIE_TBP 14208
#define MOVIE_TBW 5

int pcMpegOpen(const char* path);
void pcMpegClose(void);
void pcMpegPause(int on);
void pcMpegSetVolume(int vol);
int pcMpegUpdate(unsigned char* rgba, int max_bytes, int* w, int* h);
int pcMpegSize(int* w, int* h);
void pcGsMovieFrame(unsigned int tbp, const unsigned char* rgba, int w, int h, int scale);
void gsRendererFlush(void);
const char* pcCdDataDir(void);
void gsGifTransfer(const void* data, int qwc);

static int playing;                 /* a converted movie is open */
static int image_w, image_h, image_new;
static unsigned char* image;        /* RGBA, 16 bytes of GIF tags in front */
static int image_cap = 512 * 512 * 4;
#define IMAGE_MAX image_cap
#define HD_SCALE 4                  /* upscaled movies are 4x the originals */
static int hd;                      /* playing an upscaled movie */
static int hd_w, hd_h;              /* its frame size */

void initAll()
{
    rmi.iMovieState = MOVIE_STATE_PREP;
    rmi.iMovieFrame = Ps2_vcount;
    playing = 0;
}

static int file_exists(const char* path)
{
    FILE* f = fopen(path, "rb");

    if (f == NULL)
    {
        return 0;
    }

    fclose(f);
    return 1;
}

/* called by ps2mwPlyStartFname with the movie's name ("MV_000.PSS") */
void pcMovieStart(const char* name)
{
    const char* dir = pcCdDataDir();
    char path[1200];
    size_t n;

    if (dir == NULL)
    {
        pcLog("movie: %s skipped (movies need the extracted data folder, see pc/tools/convert_movies.py)", name);
        return;
    }

    /* the upscaled version first (unless turned off), then the converted original */
    hd = 0;
    snprintf(path, sizeof(path), "%s/MOVIE_HD/%s", dir, name);
    n = strlen(path);

    if ((n > 4) && (_stricmp(path + n - 4, ".PSS") == 0))
    {
        strcpy(path + n - 4, ".MPG");
    }

    if ((pcIniGetInt("movies_hd", 1) != 0) && file_exists(path) && pcMpegOpen(path))
    {
        hd = 1;
    }
    else
    {
        snprintf(path, sizeof(path), "%s/MOVIE/%s", dir, name);
        n = strlen(path);

        if ((n > 4) && (_stricmp(path + n - 4, ".PSS") == 0))
        {
            strcpy(path + n - 4, ".MPG");
        }
    }

    if (!hd && !pcMpegOpen(path))
    {
        pcLog("movie: %s not found, skipped (run pc/tools/convert_movies.py)", path);
        return;
    }

    {
        int w, h;

        if (pcMpegSize(&w, &h) && (w * h * 4 > image_cap))
        {
            free(image);
            image = NULL;
            image_cap = w * h * 4;
        }
    }

    if (image == NULL)
    {
        image = malloc(16 + IMAGE_MAX);
    }

    playing = 1;
    image_new = 0;
}

void readMpeg()
{
    int r;

    movie_draw = 1;

    if (!playing)
    {
        rmi.iMovieState = (rmi.iMovieState == MOVIE_STATE_PREP) ? MOVIE_STATE_PLAYING : MOVIE_STATE_END;
        return;
    }

    if (rmi.iMovieState == MOVIE_STATE_PREP)
    {
        rmi.iMovieState = MOVIE_STATE_PLAYING;
        rmi.iMovieFrame = Ps2_vcount;
    }

    pcMpegPause((rmi.uiContFlag & 0x2) != 0);

    r = pcMpegUpdate(image + 16, IMAGE_MAX, &image_w, &image_h);

    if ((r > 0) && hd)
    {
        /* the game's sprite keeps the original frame size; the renderer gets the big one */
        hd_w = image_w;
        hd_h = image_h;
        image_w /= HD_SCALE;
        image_h /= HD_SCALE;
    }

    if (r < 0)
    {
        rmi.iMovieState = MOVIE_STATE_END;
    }
    else if (r > 0)
    {
        image_new = 1;
    }
}

void termAll()
{
    if (playing)
    {
        pcMpegClose();
        playing = 0;
    }

    if (hd)
    {
        gsRendererFlush();
        pcGsMovieFrame(0, NULL, 0, 0, 0);
        hd = 0;
    }
}

/* upload the current frame where the PS2 player put it */
void setImageTag(unsigned int* tags, void* unused)
{
    u_long gif[12];
    int qwc;

    (void)tags;
    (void)unused;

    if (!playing || !image_new)
    {
        return;
    }

    image_new = 0;

    if (hd)
    {
        gsRendererFlush();   /* the previous frame's sprite may still be queued */
        pcGsMovieFrame(MOVIE_TBP, image + 16, hd_w, hd_h, HD_SCALE);
        return;
    }

    gif[0] = SCE_GIF_SET_TAG(4, SCE_GS_TRUE, SCE_GS_FALSE, 0, SCE_GIF_PACKED, 1);
    gif[1] = SCE_GIF_PACKED_AD;
    gif[2] = SCE_GS_SET_BITBLTBUF(0, 0, 0, MOVIE_TBP, MOVIE_TBW, SCE_GS_PSMCT32);
    gif[3] = SCE_GS_BITBLTBUF;
    gif[4] = SCE_GS_SET_TRXPOS(0, 0, 0, 0, 0);
    gif[5] = SCE_GS_TRXPOS;
    gif[6] = SCE_GS_SET_TRXREG(image_w, image_h);
    gif[7] = SCE_GS_TRXREG;
    gif[8] = SCE_GS_SET_TRXDIR(0);
    gif[9] = SCE_GS_TRXDIR;
    gsGifTransfer(gif, 5);

    qwc = (image_w * image_h * 4) / 16;
    ((u_long*)image)[0] = SCE_GIF_SET_TAG(qwc, SCE_GS_TRUE, SCE_GS_FALSE, 0, SCE_GIF_IMAGE, 0);
    ((u_long*)image)[1] = 0;
    gsGifTransfer(image, 1 + qwc);
}

/* the original's sprite: decoded image (TBP 14208) stretched to the movie's display rectangle */
void vbrank_draw()
{
    u_long gif[18];
    int u, v, x0, x1, y, w, h;
    float k = pcVideoWideScale();

    if (!playing || (image_w == 0))
    {
        return;
    }

    u = (image_w - 1) * 16;
    v = (image_h - 1) * 16;
    y = (int)(mdSize.sDispY + 1824) * 16;
    w = (int)mdSize.sWidth * 16;
    h = (int)mdSize.sHeight * 16;

    /* widescreen: keep the 4:3 movie at 4:3 in the middle of the stretched frame */
    x0 = 32768 + (int)((27648 - 32768) * k);
    x1 = 32768 + (int)((27648 + w - 32768) * k);

    gif[0] = SCE_GIF_SET_TAG(8, SCE_GS_TRUE, SCE_GS_FALSE, 0, SCE_GIF_PACKED, 1);
    gif[1] = SCE_GIF_PACKED_AD;
    gif[2] = 0;
    gif[3] = SCE_GS_TEXFLUSH;
    gif[4] = SCE_GS_SET_TEX1_1(0, 0, 1, 1, 0, 0, 0);
    gif[5] = SCE_GS_TEX1_2;
    gif[6] = SCE_GS_SET_TEX0_2(MOVIE_TBP, MOVIE_TBW, SCE_GS_PSMCT32, 9, 9, 0, SCE_GS_DECAL, 0, SCE_GS_PSMCT32, 0, 0, 0);
    gif[7] = SCE_GS_TEX0_2;
    gif[8] = SCE_GS_SET_PRIM(SCE_GS_PRIM_SPRITE, 0, 1, 0, 0, 0, 1, 1, 0);
    gif[9] = SCE_GS_PRIM;
    gif[10] = SCE_GS_SET_UV(8, 8);
    gif[11] = SCE_GS_UV;
    gif[12] = SCE_GS_SET_XYZ2(x0, y, 0);
    gif[13] = SCE_GS_XYZ2;
    gif[14] = SCE_GS_SET_UV(u + 8, v + 8);
    gif[15] = SCE_GS_UV;
    gif[16] = SCE_GS_SET_XYZ2(x1, y + h, 0);
    gif[17] = SCE_GS_XYZ2;
    gsGifTransfer(gif, 9);
}

int sendToIOP(int dst, u_char* src, int size)
{
    return size;
}

/* 0 .. 32767 from ps2mwPlySetOutVol */
void changeInputVolume(u_int val)
{
    double lin = val / 32767.0;
    int vol = (lin <= 0.00002) ? -960 : (int)(200.0 * log10(lin));

    pcMpegSetVolume((vol < -960) ? -960 : (vol > 0) ? 0 : vol);
}
