/* PC port: a notice shown instead of the game picture (the "return to the title screen?"
 * question). The text is drawn with Windows' own font into a bitmap, which the renderer shows
 * in the middle of a black screen (gsRendererPresent). */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#pragma comment(lib, "gdi32.lib")

static char notice[256];

static unsigned char* image;
static int image_w, image_h;
static char image_text[256];
static int image_height;

/* shows text (lines separated by \n) until called with NULL */
void pcOverlayNotice(const char* text)
{
    snprintf(notice, sizeof(notice), "%s", (text != NULL) ? text : "");
}

/* draws text white on black, centred, with lines line_h pixels apart, into image (RGBA, top row
 * first) */
static int render(const char* text, int line_h)
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

    free(image);
    image = malloc(w * height * 4);

    if (image != NULL)
    {
        for (i = 0; i < w * height; i++)
        {
            image[i * 4 + 0] = bits[i * 4 + 2];
            image[i * 4 + 1] = bits[i * 4 + 1];
            image[i * 4 + 2] = bits[i * 4 + 0];
            image[i * 4 + 3] = 255;
        }
    }

    SelectObject(dc, old_bmp);
    DeleteObject(bmp);
    SelectObject(dc, old_font);
    DeleteObject(font);
    DeleteDC(dc);

    image_w = w;
    image_h = height;
    return image != NULL;
}

/* the notice as an RGBA image (top row first) with lines height pixels apart, or NULL when there
 * is none */
const unsigned char* pcOverlayImage(int height, int* w, int* h)
{
    if ((notice[0] == 0) || (height < 8))
    {
        return NULL;
    }

    if ((image == NULL) || (height != image_height) || (strcmp(image_text, notice) != 0))
    {
        if (!render(notice, height))
        {
            return NULL;
        }

        image_height = height;
        snprintf(image_text, sizeof(image_text), "%s", notice);
    }

    *w = image_w;
    *h = image_h;
    return image;
}
