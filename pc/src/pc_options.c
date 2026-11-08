/* PC port: the PC settings pages of the game's OPTION screen, drawn by adv.c in the game's font:
 * SCREEN SETUP (display) and KEY CONFIG (keyboard and controller bindings). Values come from and
 * go to recvx.ini (the same keys recvx-config.exe writes) and take effect at once. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <SDL.h>

#include "pc_platform.h"
#include "pc_bindings.h"

#ifndef _WIN32
#define _stricmp strcasecmp
#endif

int pcIniGet(const char* key, char* out, int size);
void pcIniSet(const char* key, const char* value);
float pcVideoAspect(void);
int pcVideoMode(void);
void pcVideoSetMode(int mode);
void pcVideoWindowSize(int* w, int* h);
void pcVideoSetWindowSize(int w, int h, int automatic);
int pcVideoPauseUnfocused(void);
void pcVideoSetPauseUnfocused(int on);
int pcVideoSetAspect(int w, int h);
int pcVideoScale(void);
void pcVideoSetScale(int scale);
int pcGsInterpEnabled(void);
void pcGsInterpSetEnabled(int on);
void pcPadReloadBindings(void);
void pcPadCaptureStart(int pad);
int pcPadCaptureResult(char* name, int size);

enum { PAGE_DISPLAY, PAGE_CONTROLS };

/* ------------------------------------------------------------------------------------------- */
/* SCREEN SETUP */

enum { D_RES, D_MODE, D_ASPECT, D_SCALE, D_FPS, D_PAUSE, D_BACK, D_ROWS };

static const char* display_labels[D_ROWS] = {
    "Resolution", "Display mode", "Aspect ratio", "Internal resolution", "Frame rate", "Pause when inactive", "Back",
};

static const struct { int w, h; const char* label; } aspects[] = {
    { 4, 3, "4:3 (original)" }, { 16, 9, "16:9" }, { 16, 10, "16:10" }, { 21, 9, "21:9" }, { 32, 9, "32:9" },
};
#define NASPECTS ((int)(sizeof(aspects) / sizeof(aspects[0])))

static const char* mode_labels[3] = { "Windowed", "Fullscreen", "Borderless" };

#define MAX_RES 64
static struct { int w, h; } res[MAX_RES];   /* res[0]: automatic (960 pixels high) */
static int nres;

static void add_res(int w, int h)
{
    int i;

    for (i = 1; i < nres; i++)
    {
        if ((res[i].w == w) && (res[i].h == h))
        {
            return;
        }
    }

    if (nres < MAX_RES)
    {
        res[nres].w = w;
        res[nres].h = h;
        nres++;
    }
}

static int cmp_res(const void* a, const void* b)
{
    const int* x = (const int*)a;
    const int* y = (const int*)b;

    return (x[0] != y[0]) ? (x[0] - y[0]) : (x[1] - y[1]);
}

/* the monitor's modes and the current size, smallest first */
static void list_resolutions(void)
{
    int i, n = SDL_GetNumDisplayModes(0), w, h;

    nres = 1;

    for (i = 0; i < n; i++)
    {
        SDL_DisplayMode m;

        if ((SDL_GetDisplayMode(0, i, &m) == 0) && (m.w >= 640) && (m.h >= 480))
        {
            add_res(m.w, m.h);
        }
    }

    pcVideoWindowSize(&w, &h);
    add_res(w, h);
    qsort(&res[1], nres - 1, sizeof(res[0]), cmp_res);
}

static int current_res(void)
{
    char v[32];
    int i, w, h;

    if (!pcIniGet("window", v, sizeof(v)))
    {
        return 0;
    }

    pcVideoWindowSize(&w, &h);

    for (i = 1; i < nres; i++)
    {
        if ((res[i].w == w) && (res[i].h == h))
        {
            return i;
        }
    }

    return 0;
}

static int current_aspect(void)
{
    float a = pcVideoAspect();
    int i, best = 0;

    for (i = 0; i < NASPECTS; i++)
    {
        float d = a - (float)aspects[i].w / aspects[i].h, e = a - (float)aspects[best].w / aspects[best].h;

        if (((d < 0) ? -d : d) < ((e < 0) ? -e : e))
        {
            best = i;
        }
    }

    return best;
}

static int wrap(int v, int dir, int n)
{
    return (v + dir + n) % n;
}

static void display_change(int row, int dir)
{
    switch (row)
    {
    case D_RES:
    {
        int i = wrap(current_res(), dir, nres);

        if (i == 0)
        {
            int h = 960;

            pcVideoSetWindowSize((int)(h * pcVideoAspect() + 0.5f), h, 1);
        }
        else
        {
            pcVideoSetWindowSize(res[i].w, res[i].h, 0);
        }

        break;
    }
    case D_MODE:
        pcVideoSetMode(wrap(pcVideoMode(), dir, 3));
        break;
    case D_ASPECT:
    {
        int i = wrap(current_aspect(), dir, NASPECTS);
        char v[16];

        snprintf(v, sizeof(v), "%d:%d", aspects[i].w, aspects[i].h);
        pcIniSet("aspect", (i == 0) ? NULL : v);
        pcVideoSetAspect(aspects[i].w, aspects[i].h);
        break;
    }
    case D_SCALE:
    {
        int s = wrap(pcVideoScale() - 1, dir, 8) + 1;
        char v[8];

        snprintf(v, sizeof(v), "%d", s);
        pcIniSet("scale", (s == 3) ? NULL : v);
        pcVideoSetScale(s);
        break;
    }
    case D_FPS:
        pcGsInterpSetEnabled(!pcGsInterpEnabled());
        pcIniSet("smooth_motion", pcGsInterpEnabled() ? "1" : NULL);
        break;
    case D_PAUSE:
        pcVideoSetPauseUnfocused(!pcVideoPauseUnfocused());
        break;
    }
}

static const char* display_value(int row)
{
    static char v[64];

    switch (row)
    {
    case D_RES:
    {
        int i = current_res(), w, h;

        if (i == 0)
        {
            return "Automatic";
        }

        pcVideoWindowSize(&w, &h);
        snprintf(v, sizeof(v), "%d x %d", w, h);
        return v;
    }
    case D_MODE:
        return mode_labels[pcVideoMode()];
    case D_ASPECT:
        return aspects[current_aspect()].label;
    case D_SCALE:
    {
        int s = pcVideoScale();
        int sx = (int)(s * pcVideoAspect() / (4.0f / 3.0f) + 0.5f);   /* as pc_platform.c renders */

        snprintf(v, sizeof(v), "%dx (%d x %d)", s, 640 * sx, 480 * s);
        return v;
    }
    case D_FPS:
        return pcGsInterpEnabled() ? "60 (smooth motion)" : "30 (original)";
    case D_PAUSE:
        return pcVideoPauseUnfocused() ? "On" : "Off";
    }

    return "";
}

/* ------------------------------------------------------------------------------------------- */
/* KEY CONFIG */

#define MAX_BIND PC_BINDING_COUNT
static int bind_index[MAX_BIND];   /* rows: the bindings shown in recvx-config.exe */
static int nbind;
static int capturing = -1;         /* row being set */
static int capturing_pad;

static void list_bindings(void)
{
    int i;

    nbind = 0;

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        if (!pc_binding_defs[i].hidden)
        {
            bind_index[nbind++] = i;
        }
    }
}

/* the binding as recvx.ini has it: the SDL name, "" for none */
static void binding(int def, int pad, char* out, int size)
{
    const PcBindingDef* d = &pc_binding_defs[def];
    char key[64];

    snprintf(out, size, "%s", pad ? d->pad : d->key);
    snprintf(key, sizeof(key), "%s_%s", pad ? "pad" : "key", d->name);
    pcIniGet(key, out, size);

    if (_stricmp(out, "none") == 0)
    {
        out[0] = 0;
    }
}

static void set_binding(int def, int pad, const char* name)
{
    const PcBindingDef* d = &pc_binding_defs[def];
    char key[64];
    const char* deflt = pad ? d->pad : d->key;

    snprintf(key, sizeof(key), "%s_%s", pad ? "pad" : "key", d->name);

    if (_stricmp(name, deflt) == 0)
    {
        pcIniSet(key, NULL);
    }
    else
    {
        pcIniSet(key, (name[0] != 0) ? name : "none");
    }
}

static const char* pad_label(const char* name)
{
    static const struct { const char* sdl, *label; } names[] = {
        { "a", "A / Cross" }, { "b", "B / Circle" }, { "x", "X / Square" }, { "y", "Y / Triangle" },
        { "back", "Back / Select" }, { "guide", "Guide" }, { "start", "Start" },
        { "leftstick", "Left stick" }, { "rightstick", "Right stick" },
        { "leftshoulder", "Left bumper" }, { "rightshoulder", "Right bumper" },
        { "lefttrigger", "Left trigger" }, { "righttrigger", "Right trigger" },
        { "dpup", "D-pad up" }, { "dpdown", "D-pad down" }, { "dpleft", "D-pad left" }, { "dpright", "D-pad right" },
        { "misc1", "Share" }, { "touchpad", "Touchpad" },
        { "paddle1", "Paddle 1" }, { "paddle2", "Paddle 2" }, { "paddle3", "Paddle 3" }, { "paddle4", "Paddle 4" },
    };
    int i;

    for (i = 0; i < (int)(sizeof(names) / sizeof(names[0])); i++)
    {
        if (_stricmp(names[i].sdl, name) == 0)
        {
            return names[i].label;
        }
    }

    return name;
}

static void reset_bindings(void)
{
    int i;
    char key[64];

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        snprintf(key, sizeof(key), "key_%s", pc_binding_defs[i].name);
        pcIniSet(key, NULL);
        snprintf(key, sizeof(key), "pad_%s", pc_binding_defs[i].name);
        pcIniSet(key, NULL);
    }

    pcPadReloadBindings();
}

/* ------------------------------------------------------------------------------------------- */
/* interface for adv.c (all text is plain ASCII) */

void pcOptOpen(int page)
{
    if (page == PAGE_DISPLAY)
    {
        list_resolutions();
    }
    else
    {
        list_bindings();
    }

    capturing = -1;
}

int pcOptRows(int page)
{
    return (page == PAGE_DISPLAY) ? D_ROWS : nbind + 2;   /* + reset, back */
}

/* the row that leaves the page */
int pcOptBackRow(int page)
{
    return pcOptRows(page) - 1;
}

const char* pcOptLabel(int page, int row)
{
    if (page == PAGE_DISPLAY)
    {
        return display_labels[row];
    }

    if (row < nbind)
    {
        return pc_binding_defs[bind_index[row]].label;
    }

    return (row == nbind) ? "Reset to defaults" : "Back";
}

/* col: display 0; controls 0 keyboard, 1 controller */
const char* pcOptValue(int page, int row, int col)
{
    static char v[64];

    if (page == PAGE_DISPLAY)
    {
        return display_value(row);
    }

    if (row >= nbind)
    {
        return "";
    }

    if ((capturing == row) && (capturing_pad == col))
    {
        return col ? "Press a button" : "Press a key";
    }

    binding(bind_index[row], col, v, sizeof(v));

    if (v[0] == 0)
    {
        return "-";
    }

    return col ? pad_label(v) : v;
}

/* left / right on a row (display pages: change the value) */
void pcOptChange(int page, int row, int dir)
{
    if (page == PAGE_DISPLAY)
    {
        display_change(row, dir);
    }
}

/* OK on a row */
void pcOptActivate(int page, int row, int col)
{
    if (page == PAGE_DISPLAY)
    {
        display_change(row, 1);
        return;
    }

    if (row < nbind)
    {
        capturing = row;
        capturing_pad = col;
        pcPadCaptureStart(col);
    }
    else if (row == nbind)
    {
        reset_bindings();
    }
}

/* 1 while a key or button is being waited for (the page takes no other input then) */
int pcOptBusy(void)
{
    char name[64];
    int r;

    if (capturing < 0)
    {
        return 0;
    }

    r = pcPadCaptureResult(name, sizeof(name));

    if (r == 0)
    {
        return 1;
    }

    if (r > 0)
    {
        set_binding(bind_index[capturing], capturing_pad, name);
        pcPadReloadBindings();
    }

    capturing = -1;
    return 1;   /* this frame's buttons are the ones just pressed */
}

/* the line at the bottom of the page */
const char* pcOptHelp(int page, int row)
{
    if (page == PAGE_DISPLAY)
    {
        return "Left / Right: change          Cancel: back";
    }

    if (capturing >= 0)
    {
        return "Esc: cancel          Delete: no binding";
    }

    return (row < nbind) ? "Left / Right: keyboard or controller          OK: set" : "OK: select          Cancel: back";
}
