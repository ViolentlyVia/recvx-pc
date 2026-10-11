/* PC port: text drawn with Windows' own font into bitmaps that the renderer shows
 * (gsRendererPresent): a notice instead of the game picture (the "return to the title screen?"
 * question), and the build number at the bottom of the title screen. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../res/version.h"

#pragma comment(lib, "gdi32.lib")

typedef struct {
    unsigned char* rgba;              /* top row first */
    int w, h;
    char text[256];                   /* what it was drawn from */
    int height;
} OverlayImage;

static char notice[256];
static OverlayImage notice_image, build_image;
static int title_frames;              /* presents left that show the build number */

/* shows text (lines separated by \n) until called with NULL */
void pcOverlayNotice(const char* text)
{
    snprintf(notice, sizeof(notice), "%s", (text != NULL) ? text : "");
}

/* the title screen was drawn this frame (DisplayTitleBg): show the build number for the next
 * few presents, so it stays up however often the game draws */
void pcOverlayTitleShown(void)
{
    title_frames = 4;
}

/* draws text centred, with lines line_h pixels apart, into out: white on black, or grey text
 * premultiplied by its coverage (transparent, for blending over the picture) */
static int render(OverlayImage* out, const char* text, int line_h, int transparent)
{
    HDC dc = CreateCompatibleDC(NULL);
    HFONT font, old_font;
    HBITMAP bmp, old_bmp;
    BITMAPINFO bi;
    unsigned char* bits;
    RECT rc;
    int w, height, i;

    if (dc == NULL)
    {
        return 0;
    }

    font = CreateFontA(-(line_h * 11 / 20), 0, 0, 0, FW_SEMIBOLD, 0, 0, 0, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
                       CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_SWISS, "Segoe UI");
    old_font = (HFONT)SelectObject(dc, font);
    SetRect(&rc, 0, 0, 0, 0);
    DrawTextA(dc, text, -1, &rc, DT_CALCRECT | DT_CENTER | DT_NOPREFIX);
    w = rc.right + line_h;
    height = rc.bottom + line_h / 2;

    memset(&bi, 0, sizeof(bi));
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader);
    bi.bmiHeader.biWidth = w;
    bi.bmiHeader.biHeight = -height;   /* top row first */
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    bmp = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, (void**)&bits, NULL, 0);

    if (bmp == NULL)
    {
        SelectObject(dc, old_font);
        DeleteObject(font);
        DeleteDC(dc);
        return 0;
    }

    old_bmp = (HBITMAP)SelectObject(dc, bmp);
    rc.left = 0;
    rc.top = 0;
    rc.right = w;
    rc.bottom = height;
    FillRect(dc, &rc, (HBRUSH)GetStockObject(BLACK_BRUSH));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(235, 235, 235));
    rc.top = line_h / 4;
    DrawTextA(dc, text, -1, &rc, DT_CENTER | DT_NOPREFIX);
    GdiFlush();

    free(out->rgba);
    out->rgba = malloc(w * height * 4);

    if (out->rgba != NULL)
    {
        unsigned char* image = out->rgba;

        for (i = 0; i < w * height; i++)
        {
            if (transparent)
            {
                /* grey text premultiplied by its coverage (the renderer adds it over the picture,
                 * so black is see-through) */
                unsigned char a = (unsigned char)(bits[i * 4 + 1] * 200 / 235);

                image[i * 4 + 0] = a;
                image[i * 4 + 1] = a;
                image[i * 4 + 2] = a;
                image[i * 4 + 3] = a;
            }
            else
            {
                image[i * 4 + 0] = bits[i * 4 + 2];
                image[i * 4 + 1] = bits[i * 4 + 1];
                image[i * 4 + 2] = bits[i * 4 + 0];
                image[i * 4 + 3] = 255;
            }
        }
    }

    SelectObject(dc, old_bmp);
    DeleteObject(bmp);
    SelectObject(dc, old_font);
    DeleteObject(font);
    DeleteDC(dc);

    out->w = w;
    out->h = height;
    return out->rgba != NULL;
}

/* img drawn from text at height, redrawn when either changed */
static const unsigned char* cached(OverlayImage* img, const char* text, int height, int transparent, int* w, int* h)
{
    if ((img->rgba == NULL) || (height != img->height) || (strcmp(img->text, text) != 0))
    {
        if (!render(img, text, height, transparent))
        {
            return NULL;
        }

        img->height = height;
        snprintf(img->text, sizeof(img->text), "%s", text);
    }

    *w = img->w;
    *h = img->h;
    return img->rgba;
}

/* the notice as an RGBA image (top row first) with lines height pixels apart, or NULL when there
 * is none */
const unsigned char* pcOverlayImage(int height, int* w, int* h)
{
    if ((notice[0] == 0) || (height < 8))
    {
        return NULL;
    }

    return cached(&notice_image, notice, height, 0, w, h);
}

/* "build x.y.z" as a transparent RGBA image while the title screen is showing, else NULL */
const unsigned char* pcOverlayBuildImage(int height, int* w, int* h)
{
    if ((title_frames <= 0) || (height < 8))
    {
        return NULL;
    }

    title_frames--;
    return cached(&build_image, "build " RECVX_VERSION_STR, height, 1, w, h);
}
