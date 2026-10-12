/* recvx-config.exe: settings for the PC port without editing recvx.ini by hand.
 *
 * A plain Win32 window next to recvx.exe. It reads recvx.ini, shows the display, folder and
 * control settings, and writes them back, keeping every line it does not manage. Settings at
 * their default are removed from the file. Keys and controller buttons are captured through
 * SDL (in a small SDL window), so the names written are exactly the ones the game looks up.
 */
#define WIN32_LEAN_AND_MEAN
#define SDL_MAIN_HANDLED
#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <shlobj.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <SDL.h>
#include <SDL_syswm.h>

#include "pc_bindings.h"
#include "setup_game.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(linker, "\"/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' " \
                        "version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")

#define APP_TITLE "Resident Evil Code: Veronica X - Settings"

/* ------------------------------------------------------------------------------------------- */
/* recvx.ini: kept as lines so unknown settings and comments survive */

#define MAX_LINES 512

static char exe_dir[MAX_PATH];
static char ini_path[MAX_PATH];
static char* lines[MAX_LINES];
static int nlines;

static int line_is(const char* line, const char* key)
{
    size_t n = strlen(key);

    return (_strnicmp(line, key, n) == 0) && (line[n] == '=');
}

static void ini_load(void)
{
    char buf[1300];
    FILE* f = fopen(ini_path, "r");

    nlines = 0;

    if (f == NULL)
    {
        return;
    }

    while ((nlines < MAX_LINES) && fgets(buf, sizeof(buf), f))
    {
        buf[strcspn(buf, "\r\n")] = 0;
        lines[nlines++] = _strdup(buf);
    }

    fclose(f);
}

/* last value wins, like the game */
static const char* ini_get(const char* key)
{
    int i;

    for (i = nlines - 1; i >= 0; i--)
    {
        if (line_is(lines[i], key))
        {
            const char* v = lines[i] + strlen(key) + 1;

            return v;
        }
    }

    return NULL;
}

/* value NULL or "" removes the key */
static void ini_set(const char* key, const char* value)
{
    char buf[1300];
    int i, j, kept = -1;

    for (i = nlines - 1; i >= 0; i--)
    {
        if (line_is(lines[i], key))
        {
            if ((kept < 0) && (value != NULL) && (value[0] != 0))
            {
                kept = i;
                continue;
            }

            free(lines[i]);

            for (j = i; j < nlines - 1; j++)
            {
                lines[j] = lines[j + 1];
            }

            nlines--;

            if (kept > i)
            {
                kept--;
            }
        }
    }

    if ((value == NULL) || (value[0] == 0))
    {
        return;
    }

    snprintf(buf, sizeof(buf), "%s=%s", key, value);

    if (kept >= 0)
    {
        free(lines[kept]);
        lines[kept] = _strdup(buf);
    }
    else if (nlines < MAX_LINES)
    {
        lines[nlines++] = _strdup(buf);
    }
}

static int ini_save(void)
{
    FILE* f = fopen(ini_path, "w");
    int i;

    if (f == NULL)
    {
        return 0;
    }

    for (i = 0; i < nlines; i++)
    {
        fprintf(f, "%s\n", lines[i]);
    }

    fclose(f);
    return 1;
}

/* ------------------------------------------------------------------------------------------- */
/* settings model */

static const struct { const char* value; const char* label; } aspects[] = {
    { "4:3", "4:3 (original)" }, { "16:9", "16:9" }, { "16:10", "16:10" }, { "21:9", "21:9" }, { "32:9", "32:9" },
};
#define NASPECTS ((int)(sizeof(aspects) / sizeof(aspects[0])))

static const char* modes[] = { "Windowed", "Fullscreen", "Borderless fullscreen" };

#define MAX_RES 64
static struct { int w, h; } res[MAX_RES];   /* res[0] = automatic */
static int nres;

static char bind_key[PC_BINDING_COUNT][64];
static char bind_pad[PC_BINDING_COUNT][64];

static float aspect_ratio(int i)
{
    int w = 4, h = 3;

    sscanf(aspects[i].value, "%d:%d", &w, &h);
    return (float)w / (float)h;
}

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

    return (x[0] != y[0]) ? (y[0] - x[0]) : (y[1] - x[1]);
}

static void list_resolutions(void)
{
    int i, n = SDL_GetNumDisplayModes(0);

    nres = 1;   /* automatic */

    for (i = 0; i < n; i++)
    {
        SDL_DisplayMode m;

        if ((SDL_GetDisplayMode(0, i, &m) == 0) && (m.w >= 640) && (m.h >= 480))
        {
            add_res(m.w, m.h);
        }
    }

    qsort(&res[1], nres - 1, sizeof(res[0]), cmp_res);
}

/* friendly names for SDL controller buttons */
static const char* pad_label(const char* name)
{
    static const struct { const char* sdl, *label; } names[] = {
        { "a", "A / Cross" }, { "b", "B / Circle" }, { "x", "X / Square" }, { "y", "Y / Triangle" },
        { "back", "Back / Select" }, { "guide", "Guide" }, { "start", "Start" },
        { "leftstick", "Left stick click" }, { "rightstick", "Right stick click" },
        { "leftshoulder", "Left bumper" }, { "rightshoulder", "Right bumper" },
        { "lefttrigger", "Left trigger" }, { "righttrigger", "Right trigger" },
        { "dpup", "D-pad up" }, { "dpdown", "D-pad down" }, { "dpleft", "D-pad left" }, { "dpright", "D-pad right" },
        { "misc1", "Share / Capture" }, { "touchpad", "Touchpad" },
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

/* ------------------------------------------------------------------------------------------- */
/* window */

enum {
    ID_RES = 100, ID_ASPECT, ID_MODE, ID_SCALE,
    ID_HD, ID_HD_BROWSE, ID_SAVES, ID_SAVES_BROWSE,
    ID_RESET, ID_SAVE, ID_PLAY, ID_CLOSE, ID_SETUP, ID_PAUSE, ID_UPSCALE, ID_MOVIES_HD,
    ID_SKIP_DOORS,
    ID_SAVE_ANYWHERE,
    ID_SKIP_INTRO,
    ID_KEY = 1000, ID_PAD = 1100,
};

static HWND main_wnd;
static HWND h_res, h_aspect, h_mode, h_scale, h_hd, h_saves, h_game, h_pause, h_movies_hd, h_skip_doors, h_save_anywhere, h_skip_intro;
static int loaded_res;   /* resolution entry when the settings were loaded */
static HWND h_key[PC_BINDING_COUNT], h_pad[PC_BINDING_COUNT];
static HFONT font, bold_font;
static float dpi = 1.0f;
static int dirty;
static int layout_h;    /* height of the laid out controls, unscaled */

#define S(v) ((int)((v) * dpi + 0.5f))

static HWND make(const char* cls, const char* text, DWORD style, int x, int y, int w, int h, int id)
{
    HWND c = CreateWindowExA((strcmp(cls, "EDIT") == 0) ? WS_EX_CLIENTEDGE : 0, cls, text, WS_CHILD | WS_VISIBLE | style,
                             S(x), S(y), S(w), S(h), main_wnd, (HMENU)(INT_PTR)id, GetModuleHandle(NULL), NULL);

    SendMessageA(c, WM_SETFONT, (WPARAM)font, TRUE);
    return c;
}

static HWND label(const char* text, int x, int y, int w)
{
    return make("STATIC", text, SS_LEFT, x, y + 4, w, 20, -1);
}

static HWND heading(const char* text, int x, int y, int w)
{
    HWND c = make("STATIC", text, SS_LEFT, x, y, w, 20, -1);

    SendMessageA(c, WM_SETFONT, (WPARAM)bold_font, TRUE);
    return c;
}

static HWND combo(int x, int y, int w, int id)
{
    return make("COMBOBOX", "", CBS_DROPDOWNLIST | WS_VSCROLL | WS_TABSTOP, x, y, w, 300, id);
}

static void set_binding_text(int i)
{
    char buf[96];

    if (h_key[i] == NULL)
    {
        return;   /* hidden action */
    }

    SetWindowTextA(h_key[i], (bind_key[i][0] != 0) ? bind_key[i] : "-");
    snprintf(buf, sizeof(buf), "%s", (bind_pad[i][0] != 0) ? pad_label(bind_pad[i]) : "-");
    SetWindowTextA(h_pad[i], buf);
}

static void fill_scale_combo(void)
{
    int sel = (int)SendMessageA(h_scale, CB_GETCURSEL, 0, 0);
    int a = (int)SendMessageA(h_aspect, CB_GETCURSEL, 0, 0);
    float ar = aspect_ratio((a < 0) ? 0 : a);
    int s;

    if (sel < 0)
    {
        sel = 2;
    }

    SendMessageA(h_scale, CB_RESETCONTENT, 0, 0);

    for (s = 1; s <= 8; s++)
    {
        char buf[64];
        int sx = (int)(s * ar / (4.0f / 3.0f) + 0.5f);

        snprintf(buf, sizeof(buf), "%dx  (%d x %d)%s", s, 640 * sx, 480 * s, (s == 3) ? "  default" : "");
        SendMessageA(h_scale, CB_ADDSTRING, 0, (LPARAM)buf);
    }

    SendMessageA(h_scale, CB_SETCURSEL, sel, 0);
}

static void load_settings(void)
{
    const char* v;
    char buf[64];
    int i, sel, w, h;

    ini_load();
    list_resolutions();

    /* resolution: the configured one is added if the monitor does not list it */
    sel = 0;

    if (((v = ini_get("window")) != NULL) && (sscanf(v, "%dx%d", &w, &h) == 2))
    {
        add_res(w, h);

        for (i = 1; i < nres; i++)
        {
            if ((res[i].w == w) && (res[i].h == h))
            {
                sel = i;
            }
        }
    }

    SendMessageA(h_res, CB_RESETCONTENT, 0, 0);
    SendMessageA(h_res, CB_ADDSTRING, 0, (LPARAM)"Automatic (960 pixels high)");

    for (i = 1; i < nres; i++)
    {
        snprintf(buf, sizeof(buf), "%d x %d", res[i].w, res[i].h);
        SendMessageA(h_res, CB_ADDSTRING, 0, (LPARAM)buf);
    }

    SendMessageA(h_res, CB_SETCURSEL, sel, 0);
    loaded_res = sel;

    sel = 0;

    if ((v = ini_get("aspect")) != NULL)
    {
        for (i = 0; i < NASPECTS; i++)
        {
            if (strcmp(v, aspects[i].value) == 0)
            {
                sel = i;
            }
        }
    }

    SendMessageA(h_aspect, CB_SETCURSEL, sel, 0);

    v = ini_get("fullscreen");
    sel = (v == NULL) ? 0 : (_stricmp(v, "desktop") == 0) ? 2 : (atoi(v) != 0) ? 1 : 0;
    SendMessageA(h_mode, CB_SETCURSEL, sel, 0);

    v = ini_get("pause_unfocused");
    SendMessageA(h_pause, BM_SETCHECK, ((v == NULL) || (atoi(v) != 0)) ? BST_CHECKED : BST_UNCHECKED, 0);

    v = ini_get("movies_hd");
    SendMessageA(h_movies_hd, BM_SETCHECK, ((v == NULL) || (atoi(v) != 0)) ? BST_CHECKED : BST_UNCHECKED, 0);

    v = ini_get("skip_doors");
    SendMessageA(h_skip_doors, BM_SETCHECK, ((v != NULL) && (atoi(v) != 0)) ? BST_CHECKED : BST_UNCHECKED, 0);

    v = ini_get("skip_intro");
    SendMessageA(h_skip_intro, BM_SETCHECK, ((v != NULL) && (atoi(v) != 0)) ? BST_CHECKED : BST_UNCHECKED, 0);

    v = ini_get("debug_save");
    SendMessageA(h_save_anywhere, BM_SETCHECK, ((v != NULL) && (atoi(v) != 0)) ? BST_CHECKED : BST_UNCHECKED, 0);

    fill_scale_combo();   /* the list must exist before an entry can be selected */
    v = ini_get("scale");
    sel = (v != NULL) ? atoi(v) - 1 : 2;
    SendMessageA(h_scale, CB_SETCURSEL, (sel < 0) ? 0 : (sel > 7) ? 7 : sel, 0);

    SetWindowTextA(h_hd, ((v = ini_get("hd_textures")) != NULL) ? v : "");
    SetWindowTextA(h_saves, ((v = ini_get("saves")) != NULL) ? v : "");

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        char key[64];

        snprintf(key, sizeof(key), "key_%s", pc_binding_defs[i].name);
        v = ini_get(key);
        snprintf(bind_key[i], sizeof(bind_key[i]), "%s", (v == NULL) ? pc_binding_defs[i].key : (_stricmp(v, "none") == 0) ? "" : v);

        snprintf(key, sizeof(key), "pad_%s", pc_binding_defs[i].name);
        v = ini_get(key);
        snprintf(bind_pad[i], sizeof(bind_pad[i]), "%s", (v == NULL) ? pc_binding_defs[i].pad : (_stricmp(v, "none") == 0) ? "" : v);

        set_binding_text(i);
    }

    dirty = 0;
}

static int save_settings(void)
{
    char buf[MAX_PATH + 16];
    int i, sel;

    sel = (int)SendMessageA(h_res, CB_GETCURSEL, 0, 0);

    if (sel != loaded_res)
    {
        ini_set("window_max", NULL);   /* the game remembers a maximized window; a new size replaces it */
    }

    loaded_res = sel;

    if (sel > 0)
    {
        snprintf(buf, sizeof(buf), "%dx%d", res[sel].w, res[sel].h);
        ini_set("window", buf);
    }
    else
    {
        ini_set("window", NULL);
    }

    sel = (int)SendMessageA(h_aspect, CB_GETCURSEL, 0, 0);
    ini_set("aspect", (sel > 0) ? aspects[sel].value : NULL);

    sel = (int)SendMessageA(h_mode, CB_GETCURSEL, 0, 0);
    ini_set("fullscreen", (sel == 1) ? "1" : (sel == 2) ? "desktop" : NULL);

    ini_set("pause_unfocused", (SendMessageA(h_pause, BM_GETCHECK, 0, 0) == BST_CHECKED) ? NULL : "0");
    ini_set("movies_hd", (SendMessageA(h_movies_hd, BM_GETCHECK, 0, 0) == BST_CHECKED) ? NULL : "0");
    ini_set("skip_doors", (SendMessageA(h_skip_doors, BM_GETCHECK, 0, 0) == BST_CHECKED) ? "1" : NULL);
    ini_set("skip_intro", (SendMessageA(h_skip_intro, BM_GETCHECK, 0, 0) == BST_CHECKED) ? "1" : NULL);
    ini_set("debug_save", (SendMessageA(h_save_anywhere, BM_GETCHECK, 0, 0) == BST_CHECKED) ? "1" : NULL);

    sel = (int)SendMessageA(h_scale, CB_GETCURSEL, 0, 0) + 1;
    snprintf(buf, sizeof(buf), "%d", sel);
    ini_set("scale", (sel != 3) ? buf : NULL);

    GetWindowTextA(h_hd, buf, sizeof(buf));
    ini_set("hd_textures", buf);
    GetWindowTextA(h_saves, buf, sizeof(buf));
    ini_set("saves", buf);

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        const PcBindingDef* d = &pc_binding_defs[i];
        char key[64];

        snprintf(key, sizeof(key), "key_%s", d->name);
        ini_set(key, (_stricmp(bind_key[i], d->key) == 0) ? NULL : (bind_key[i][0] != 0) ? bind_key[i] : "none");

        snprintf(key, sizeof(key), "pad_%s", d->name);
        ini_set(key, (_stricmp(bind_pad[i], d->pad) == 0) ? NULL : (bind_pad[i][0] != 0) ? bind_pad[i] : "none");
    }

    if (!ini_save())
    {
        MessageBoxA(main_wnd, "Could not write recvx.ini next to the program.", APP_TITLE, MB_ICONERROR);
        return 0;
    }

    dirty = 0;
    return 1;
}

/* ------------------------------------------------------------------------------------------- */
/* binding capture: a small SDL window takes the next key or controller button */

static void paint_prompt(SDL_Window* w, const char* text)
{
    SDL_SysWMinfo info;
    HDC dc;
    RECT rc;
    HFONT old;

    SDL_VERSION(&info.version);

    if (!SDL_GetWindowWMInfo(w, &info))
    {
        return;
    }

    dc = GetDC(info.info.win.window);
    GetClientRect(info.info.win.window, &rc);
    FillRect(dc, &rc, GetSysColorBrush(COLOR_WINDOW));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, GetSysColor(COLOR_WINDOWTEXT));
    old = (HFONT)SelectObject(dc, font);
    InflateRect(&rc, -S(16), -S(12));
    DrawTextA(dc, text, -1, &rc, DT_CENTER | DT_WORDBREAK);
    SelectObject(dc, old);
    ReleaseDC(info.info.win.window, dc);
}

/* 1 = bound (name in out), 0 = cancelled, -1 = cleared */
static int capture(int pad, int index, char* out, int size)
{
    const PcBindingDef* d = &pc_binding_defs[index];
    char text[256];
    RECT r;
    SDL_Window* w;
    SDL_Event ev;
    int result = 0, done = 0;

    snprintf(text, sizeof(text), pad ? "Press a controller button for %s\n\nEsc: cancel     Delete: clear" :
                                       "Press a key for %s\n\nEsc: cancel     Delete: clear", d->label);

    GetWindowRect(main_wnd, &r);
    w = SDL_CreateWindow(pad ? "Controller binding" : "Keyboard binding",
                         (r.left + r.right) / 2 - S(180), (r.top + r.bottom) / 2 - S(50), S(360), S(100),
                         SDL_WINDOW_ALWAYS_ON_TOP | SDL_WINDOW_SKIP_TASKBAR);

    if (w == NULL)
    {
        return 0;
    }

    EnableWindow(main_wnd, FALSE);
    SDL_RaiseWindow(w);

    while (!done)
    {
        while (SDL_PollEvent(&ev))
        {
            if ((ev.type == SDL_QUIT) || ((ev.type == SDL_WINDOWEVENT) && (ev.window.event == SDL_WINDOWEVENT_CLOSE)))
            {
                done = 1;
            }
            else if (ev.type == SDL_CONTROLLERDEVICEADDED)
            {
                SDL_GameControllerOpen(ev.cdevice.which);
            }
            else if ((ev.type == SDL_KEYDOWN) && !ev.key.repeat)
            {
                if (ev.key.keysym.scancode == SDL_SCANCODE_ESCAPE)
                {
                    done = 1;
                }
                else if (ev.key.keysym.scancode == SDL_SCANCODE_DELETE)
                {
                    result = -1;
                    done = 1;
                }
                else if (!pad)
                {
                    snprintf(out, size, "%s", SDL_GetScancodeName(ev.key.keysym.scancode));
                    result = (out[0] != 0);
                    done = 1;
                }
            }
            else if (pad && (ev.type == SDL_CONTROLLERBUTTONDOWN))
            {
                snprintf(out, size, "%s", SDL_GameControllerGetStringForButton((SDL_GameControllerButton)ev.cbutton.button));
                result = 1;
                done = 1;
            }
            else if (pad && (ev.type == SDL_CONTROLLERAXISMOTION) && (ev.caxis.value > 16000) &&
                     ((ev.caxis.axis == SDL_CONTROLLER_AXIS_TRIGGERLEFT) || (ev.caxis.axis == SDL_CONTROLLER_AXIS_TRIGGERRIGHT)))
            {
                snprintf(out, size, "%s", SDL_GameControllerGetStringForAxis((SDL_GameControllerAxis)ev.caxis.axis));
                result = 1;
                done = 1;
            }
        }

        paint_prompt(w, text);
        SDL_Delay(15);
    }

    SDL_DestroyWindow(w);
    EnableWindow(main_wnd, TRUE);
    SetForegroundWindow(main_wnd);

    return result;
}

static void bind(int pad, int i)
{
    char name[64];
    char* dst = pad ? bind_pad[i] : bind_key[i];
    int r = capture(pad, i, name, sizeof(name));
    int j;

    if (r == 0)
    {
        return;
    }

    if (r < 0)
    {
        dst[0] = 0;
    }
    else
    {
        /* one key or button per action: take it away from any other action */
        for (j = 0; j < PC_BINDING_COUNT; j++)
        {
            char* other = pad ? bind_pad[j] : bind_key[j];

            if ((j != i) && (_stricmp(other, name) == 0))
            {
                other[0] = 0;
                set_binding_text(j);
            }
        }

        snprintf(dst, 64, "%s", name);
    }

    set_binding_text(i);
    dirty = 1;
}

static void browse(HWND edit, const char* title)
{
    BROWSEINFOA bi;
    LPITEMIDLIST id;
    char path[MAX_PATH];

    memset(&bi, 0, sizeof(bi));
    bi.hwndOwner = main_wnd;
    bi.lpszTitle = title;
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;

    if ((id = SHBrowseForFolderA(&bi)) != NULL)
    {
        if (SHGetPathFromIDListA(id, path))
        {
            SetWindowTextA(edit, path);
            dirty = 1;
        }

        CoTaskMemFree(id);
    }
}

static int game_ready(void);

static void launch_game(void)
{
    if (!game_ready())
    {
        MessageBoxA(main_wnd, "Set the game up from your disc image first (\"Set up from disc image...\").", APP_TITLE, MB_ICONINFORMATION);
        return;
    }

    char exe[MAX_PATH + 16];
    STARTUPINFOA si;
    PROCESS_INFORMATION pi;

    snprintf(exe, sizeof(exe), "%s\\recvx.exe", exe_dir);
    memset(&si, 0, sizeof(si));
    si.cb = sizeof(si);

    if (!CreateProcessA(exe, NULL, NULL, NULL, FALSE, 0, NULL, exe_dir, &si, &pi))
    {
        MessageBoxA(main_wnd, "Could not start recvx.exe (it should be in the same folder as this program).", APP_TITLE, MB_ICONERROR);
        return;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    DestroyWindow(main_wnd);
}

static void reset_controls(void)
{
    int i;

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        snprintf(bind_key[i], sizeof(bind_key[i]), "%s", pc_binding_defs[i].key);
        snprintf(bind_pad[i], sizeof(bind_pad[i]), "%s", pc_binding_defs[i].pad);
        set_binding_text(i);
    }

    dirty = 1;
}

/* ------------------------------------------------------------------------------------------- */
/* game setup (setup_game.c) */

static int game_ready(void)
{
    char data[MAX_PATH + 8], text[MAX_PATH + 64];
    const char* iso = ini_get("iso");
    int ready = SetupGameIsReady(exe_dir, data, sizeof(data));

    if (ready)
    {
        snprintf(text, sizeof(text), "Ready: %s", data);
    }
    else if ((iso != NULL) && (iso[0] != 0))
    {
        snprintf(text, sizeof(text), "Runs from the disc image (no movies): %s", iso);
    }
    else
    {
        snprintf(text, sizeof(text), "Not set up yet: choose your disc image (.iso) to set it up.");
    }

    SetWindowTextA(h_game, text);
    return ready || ((iso != NULL) && (iso[0] != 0));
}

static char cmd_iso[MAX_PATH];   /* a disc image given on the command line (or dropped on the exe) */

/* the movies enlarged by AI (setup_game.c); the game uses them while "Use upscaled movies" is on */
static void upscale_movies(void)
{
    char data[MAX_PATH + 8], error[1024];
    int r;

    if (!SetupGameIsReady(exe_dir, data, sizeof(data)))
    {
        MessageBoxA(main_wnd, "Set the game up from your disc image first.", APP_TITLE, MB_ICONINFORMATION);
        return;
    }

    if (MessageBoxA(main_wnd,
                    "This enlarges the game's movies 4x with an AI upscaler (Real-ESRGAN) running on your "
                    "graphics card. It downloads the upscaler once (about 45 MB).\n\n"
                    "It takes about 45 minutes on a fast graphics card and can take several hours on a slow "
                    "one. It needs about 2 GB next to the game and 5 GB of temporary space while it works. "
                    "You can stop it and continue later; finished movies are kept.\n\n"
                    "Start now?", APP_TITLE, MB_OKCANCEL | MB_ICONQUESTION) != IDOK)
    {
        return;
    }

    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);   /* no sleep while it works */
    r = UpscaleMovies(main_wnd, font, dpi, exe_dir, error, sizeof(error));
    SetThreadExecutionState(ES_CONTINUOUS);

    if (r > 0)
    {
        MessageBoxA(main_wnd, "The movies are upscaled. The game uses them while \"Use upscaled movies\" is on.",
                    APP_TITLE, MB_ICONINFORMATION);
    }
    else if (r == 0)
    {
        MessageBoxA(main_wnd, (error[0] != 0) ? error : "Upscaling the movies failed.", APP_TITLE, MB_ICONERROR);
    }
}

static void setup_game(const char* given)
{
    char iso[MAX_PATH] = "", error[1024];
    OPENFILENAMEA ofn;
    int r;

    memset(&ofn, 0, sizeof(ofn));
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = main_wnd;
    ofn.lpstrFilter = "Disc images (*.iso)\0*.iso\0All files\0*.*\0";
    ofn.lpstrFile = iso;
    ofn.nMaxFile = sizeof(iso);
    ofn.lpstrTitle = "Your Resident Evil Code: Veronica X (USA, SLUS-20184) disc image";
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;

    if (given != NULL)
    {
        snprintf(iso, sizeof(iso), "%s", given);
    }
    else if (!GetOpenFileNameA(&ofn))
    {
        return;
    }

    r = SetupGameFromIso(main_wnd, font, dpi, exe_dir, iso, error, sizeof(error));

    if (r > 0)
    {
        /* the game finds the data folder next to it by itself */
        ini_set("data", NULL);
        ini_save();
        game_ready();
        MessageBoxA(main_wnd, "The game is set up. The disc image is no longer needed.", APP_TITLE, MB_ICONINFORMATION);
    }
    else if (r == 0)
    {
        MessageBoxA(main_wnd, (error[0] != 0) ? error : "Setting up the game failed.", APP_TITLE, MB_ICONERROR);
    }
}

/* ------------------------------------------------------------------------------------------- */

static void create_controls(void)
{
    int left_end;
    int y, i;
    const int lx = 16, cx = 150, cw = 230;     /* left column: labels, inputs */
    const int rx = 420;                        /* right column: controls */

    heading("Game", lx, 14, 200);
    h_game = make("STATIC", "", SS_LEFT | SS_PATHELLIPSIS, lx, 40, cx + cw - lx, 20, -1);
    make("BUTTON", "Set up from disc image...", BS_PUSHBUTTON | WS_TABSTOP, lx, 64, 180, 28, ID_SETUP);
    make("BUTTON", "Upscale movies (AI)...", BS_PUSHBUTTON | WS_TABSTOP, lx + 190, 64, 170, 28, ID_UPSCALE);
    h_movies_hd = make("BUTTON", "Use upscaled movies (when made)", BS_AUTOCHECKBOX | WS_TABSTOP,
                       lx, 98, cx + cw - lx, 24, ID_MOVIES_HD);
    h_skip_doors = make("BUTTON", "Skip doors (press OK / Attack during a door)", BS_AUTOCHECKBOX | WS_TABSTOP,
                        lx, 124, cx + cw - lx, 24, ID_SKIP_DOORS);
    h_skip_intro = make("BUTTON", "Skip the intro (start at the title screen)", BS_AUTOCHECKBOX | WS_TABSTOP,
                        lx, 150, cx + cw - lx, 24, ID_SKIP_INTRO);
    h_save_anywhere = make("BUTTON", "Save anywhere (debug; set its key under Controls)",
                           BS_AUTOCHECKBOX | WS_TABSTOP, lx, 176, cx + cw - lx, 24, ID_SAVE_ANYWHERE);

    heading("Display", lx, 220, 200);
    y = 246;
    label("Resolution", lx, y, cx - lx);
    h_res = combo(cx, y, cw, ID_RES);
    y += 32;
    label("Aspect ratio", lx, y, cx - lx);
    h_aspect = combo(cx, y, cw, ID_ASPECT);
    y += 32;
    label("Display mode", lx, y, cx - lx);
    h_mode = combo(cx, y, cw, ID_MODE);
    y += 32;
    label("Internal resolution", lx, y, cx - lx);
    h_scale = combo(cx, y, cw, ID_SCALE);
    y += 32;
    h_pause = make("BUTTON", "Pause the game while its window is not active", BS_AUTOCHECKBOX | WS_TABSTOP,
                   lx, y, cx + cw - lx, 24, ID_PAUSE);
    y += 30;
    make("STATIC", "Widescreen widens the 3D view; menus, text and movies stay 4:3 in the middle. "
                   "In the game, F11 or Alt+Enter switches between window and fullscreen.",
         SS_LEFT, lx, y, cx + cw - lx, 52, -1);

    y += 70;
    heading("Folders", lx, y, 200);
    y += 26;
    label("HD texture pack", lx, y, cx - lx);
    h_hd = make("EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP, cx, y, cw - 70, 24, ID_HD);
    make("BUTTON", "Browse...", BS_PUSHBUTTON | WS_TABSTOP, cx + cw - 64, y, 64, 24, ID_HD_BROWSE);
    y += 32;
    label("Saves folder", lx, y, cx - lx);
    h_saves = make("EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP, cx, y, cw - 70, 24, ID_SAVES);
    make("BUTTON", "Browse...", BS_PUSHBUTTON | WS_TABSTOP, cx + cw - 64, y, 64, 24, ID_SAVES_BROWSE);
    y += 30;
    make("STATIC", "Leave empty for no HD textures / the \"saves\" folder next to the game.",
         SS_LEFT, lx, y, cx + cw - lx, 36, -1);
    left_end = y + 36;

    for (i = 0; i < NASPECTS; i++)
    {
        SendMessageA(h_aspect, CB_ADDSTRING, 0, (LPARAM)aspects[i].label);
    }

    for (i = 0; i < 3; i++)
    {
        SendMessageA(h_mode, CB_ADDSTRING, 0, (LPARAM)modes[i]);
    }

    heading("Controls", rx, 14, 120);
    make("STATIC", "Keyboard", SS_LEFT, rx + 150, 18, 130, 18, -1);
    make("STATIC", "Controller", SS_LEFT, rx + 290, 18, 150, 18, -1);
    y = 40;

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        if (pc_binding_defs[i].hidden)
        {
            continue;   /* buttons the game does not use: not shown, kept as configured */
        }

        if (i == PC_BINDING_PS2_BUTTONS)
        {
            y += 8;   /* gap before the port's own actions */
        }

        label(pc_binding_defs[i].label, rx, y - 1, 145);
        h_key[i] = make("BUTTON", "", BS_PUSHBUTTON | WS_TABSTOP, rx + 150, y, 130, 24, ID_KEY + i);
        h_pad[i] = make("BUTTON", "", BS_PUSHBUTTON | WS_TABSTOP, rx + 290, y, 150, 24, ID_PAD + i);
        y += 27;
    }

    y += 6;
    make("STATIC", "Click a button, then press the new key or controller button.", SS_LEFT, rx, y, 440, 20, -1);

    y += 34;

    if (y < left_end + 12)
    {
        y = left_end + 12;   /* the bottom buttons go below whichever column is longer */
    }

    make("BUTTON", "Reset controls", BS_PUSHBUTTON | WS_TABSTOP, lx, y, 120, 28, ID_RESET);
    make("BUTTON", "Save", BS_PUSHBUTTON | WS_TABSTOP, rx + 440 - 3 * 110 - 16, y, 100, 28, ID_SAVE);
    make("BUTTON", "Save and play", BS_DEFPUSHBUTTON | WS_TABSTOP, rx + 440 - 2 * 110 - 8, y, 110, 28, ID_PLAY);
    make("BUTTON", "Close", BS_PUSHBUTTON | WS_TABSTOP, rx + 440 - 100, y, 100, 28, ID_CLOSE);
    layout_h = y + 28 + 16;
}

static LRESULT CALLBACK wnd_proc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg)
    {
    case WM_CREATE:
        main_wnd = hwnd;
        create_controls();
        load_settings();

        if (cmd_iso[0] != 0)
        {
            PostMessageA(hwnd, WM_APP, 1, 0);   /* set up from the given disc image */
        }
        else if (!game_ready())
        {
            PostMessageA(hwnd, WM_APP, 0, 0);   /* offer the setup once the window shows */
        }

        /* fit the window to the controls */
        {
            RECT rc = { 0, 0, S(876), S(layout_h) };

            AdjustWindowRect(&rc, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, FALSE);
            SetWindowPos(hwnd, NULL, 0, 0, rc.right - rc.left, rc.bottom - rc.top, SWP_NOMOVE | SWP_NOZORDER);
        }
        return 0;

    case WM_APP:
        if (wp == 1)
        {
            setup_game(cmd_iso);
            return 0;
        }

        if (MessageBoxA(hwnd, "The game has not been set up yet. It needs the files from your own copy of the "
                              "game: a disc image (.iso) of Resident Evil Code: Veronica X for the US "
                              "PlayStation 2 (SLUS-20184).\n\nSet it up now?", APP_TITLE, MB_YESNO | MB_ICONQUESTION) == IDYES)
        {
            setup_game(NULL);
        }
        return 0;

    case WM_CTLCOLORSTATIC:
        SetBkMode((HDC)wp, TRANSPARENT);
        return (LRESULT)GetSysColorBrush(COLOR_WINDOW);

    case WM_COMMAND:
    {
        int id = LOWORD(wp), code = HIWORD(wp);

        if ((id >= ID_KEY) && (id < ID_KEY + PC_BINDING_COUNT))
        {
            bind(0, id - ID_KEY);
        }
        else if ((id >= ID_PAD) && (id < ID_PAD + PC_BINDING_COUNT))
        {
            bind(1, id - ID_PAD);
        }
        else if (code == CBN_SELCHANGE)
        {
            if (id == ID_ASPECT)
            {
                fill_scale_combo();
            }

            dirty = 1;
        }
        else if ((code == EN_CHANGE) || (id == ID_PAUSE) || (id == ID_MOVIES_HD) || (id == ID_SKIP_DOORS) ||
                 (id == ID_SAVE_ANYWHERE) || (id == ID_SKIP_INTRO))
        {
            dirty = 1;
        }
        else if (id == ID_HD_BROWSE)
        {
            browse(h_hd, "Folder of the HD texture pack (.dds files)");
        }
        else if (id == ID_SAVES_BROWSE)
        {
            browse(h_saves, "Folder for save games");
        }
        else if (id == ID_RESET)
        {
            reset_controls();
        }
        else if (id == ID_SETUP)
        {
            setup_game(NULL);
        }
        else if (id == ID_UPSCALE)
        {
            upscale_movies();
        }
        else if (id == ID_SAVE)
        {
            save_settings();
        }
        else if (id == ID_PLAY)
        {
            if (save_settings())
            {
                launch_game();
            }
        }
        else if ((id == ID_CLOSE) || (id == IDCANCEL))
        {
            SendMessageA(hwnd, WM_CLOSE, 0, 0);
        }

        return 0;
    }

    case WM_CLOSE:
        if (dirty)
        {
            int r = MessageBoxA(hwnd, "Save the changed settings?", APP_TITLE, MB_YESNOCANCEL | MB_ICONQUESTION);

            if ((r == IDCANCEL) || ((r == IDYES) && !save_settings()))
            {
                return 0;
            }
        }

        DestroyWindow(hwnd);
        return 0;

    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProcA(hwnd, msg, wp, lp);
}

int WINAPI WinMain(HINSTANCE inst, HINSTANCE prev, LPSTR cmd, int show)
{
    INITCOMMONCONTROLSEX icc = { sizeof(icc), ICC_STANDARD_CLASSES };
    NONCLIENTMETRICSA ncm;
    WNDCLASSA wc;
    RECT rc;
    MSG m;
    HDC dc;
    char* slash;
    HWND hwnd;

    (void)prev; (void)cmd;

    SetProcessDPIAware();
    InitCommonControlsEx(&icc);
    CoInitialize(NULL);

    GetModuleFileNameA(NULL, exe_dir, sizeof(exe_dir));

    if ((slash = strrchr(exe_dir, '\\')) != NULL)
    {
        *slash = 0;
    }

    snprintf(ini_path, sizeof(ini_path), "%s\\recvx.ini", exe_dir);

    /* recvx-config.exe "C:\path\game.iso" (also what dropping an .iso on the exe does) */
    if ((cmd != NULL) && (cmd[0] != 0))
    {
        snprintf(cmd_iso, sizeof(cmd_iso), "%s", (cmd[0] == '"') ? cmd + 1 : cmd);
        cmd_iso[strcspn(cmd_iso, "\"")] = 0;

        if (GetFileAttributesA(cmd_iso) == INVALID_FILE_ATTRIBUTES)
        {
            cmd_iso[0] = 0;
        }
    }

    SDL_SetMainReady();
    SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMECONTROLLER) != 0)
    {
        MessageBoxA(NULL, SDL_GetError(), APP_TITLE, MB_ICONERROR);
        return 1;
    }

    dc = GetDC(NULL);
    dpi = GetDeviceCaps(dc, LOGPIXELSY) / 96.0f;
    ReleaseDC(NULL, dc);

    ncm.cbSize = sizeof(ncm);
    SystemParametersInfoA(SPI_GETNONCLIENTMETRICS, sizeof(ncm), &ncm, 0);
    font = CreateFontIndirectA(&ncm.lfMessageFont);
    ncm.lfMessageFont.lfWeight = FW_BOLD;
    bold_font = CreateFontIndirectA(&ncm.lfMessageFont);

    memset(&wc, 0, sizeof(wc));
    wc.lpfnWndProc = wnd_proc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
    wc.hIcon = LoadIcon(inst, MAKEINTRESOURCE(1));   /* pc/res/recvx-config.ico */
    wc.lpszClassName = "recvx_config";
    RegisterClassA(&wc);

    rc.left = 0;
    rc.top = 0;
    rc.right = S(876);
    rc.bottom = S(640);
    AdjustWindowRect(&rc, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, FALSE);

    hwnd = CreateWindowA("recvx_config", APP_TITLE, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
                         CW_USEDEFAULT, CW_USEDEFAULT, rc.right - rc.left, rc.bottom - rc.top, NULL, NULL, inst, NULL);
    ShowWindow(hwnd, show);

    while (GetMessageA(&m, NULL, 0, 0) > 0)
    {
        if (!IsDialogMessageA(hwnd, &m))
        {
            TranslateMessage(&m);
            DispatchMessageA(&m);
        }
    }

    SDL_Quit();
    CoUninitialize();
    return 0;
}
