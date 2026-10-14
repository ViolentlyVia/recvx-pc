/* PC port: SDL window, frame presentation, logging and controller input. */
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <io.h>

#include <SDL.h>
#include <SDL_syswm.h>

#include "pc_platform.h"
#include "pc_os.h"
#include "pc_gs.h"
#include "pc_bindings.h"
#include "pc_audio.h"

static unsigned int frames;   /* frames presented */
static struct { double last, sum, worst, swap; int n; } frame_time;

#define PS2_WIDTH  640
#define PS2_HEIGHT 480

static SDL_Window* window;
static int pcPadFullscreenKey(void);
static int pcPadFullscreenButton(void);
static int title_event(const SDL_Event* ev);
static void save_event(const SDL_Event* ev);
static void save_tick(void);
static void fps_tick(void);
static void update_title(int force);
static SDL_GLContext gl_context;
static FILE* log_file;

/* ------------------------------------------------------------------------------------------- */
/* file output on a thread of its own: recvx.log and the input recording are written there, so a
 * write that blocks (OneDrive or a virus scanner holding the file, a slow disk) never holds up a
 * frame. Writes to one file stay in order. */

typedef struct
{
    FILE* f;
    char* buf;
    size_t len, cap;
} OutQueue;

static SDL_mutex* out_lock;
static SDL_cond* out_wake;
static OutQueue out_q[2];   /* 0 = log, 1 = input recording */
static int out_busy;

static int out_thread(void* param)
{
    (void)param;

    for (;;)
    {
        int i;

        SDL_LockMutex(out_lock);

        while ((out_q[0].len == 0) && (out_q[1].len == 0))
        {
            SDL_CondWait(out_wake, out_lock);
        }

        out_busy = 1;

        for (i = 0; i < 2; i++)
        {
            OutQueue* q = &out_q[i];

            if (q->len != 0)
            {
                char* buf = q->buf;
                size_t len = q->len;
                FILE* f = q->f;

                q->buf = NULL;
                q->len = q->cap = 0;
                SDL_UnlockMutex(out_lock);

                fwrite(buf, 1, len, f);
                fflush(f);
                free(buf);

                SDL_LockMutex(out_lock);
            }
        }

        out_busy = 0;
        SDL_UnlockMutex(out_lock);
    }
}

static void out_write(int which, FILE* f, const char* text, size_t len)
{
    OutQueue* q = &out_q[which];

    if (out_lock == NULL)
    {
        out_lock = SDL_CreateMutex();
        out_wake = SDL_CreateCond();
        SDL_DetachThread(SDL_CreateThread(out_thread, "file output", NULL));
        atexit(pcLogFlush);
    }

    SDL_LockMutex(out_lock);

    if (q->len + len > q->cap)
    {
        size_t cap = (q->cap != 0) ? q->cap : 4096;
        char* buf;

        while (q->len + len > cap)
        {
            cap *= 2;
        }

        if ((buf = (char*)realloc(q->buf, cap)) == NULL)
        {
            SDL_UnlockMutex(out_lock);
            return;
        }

        q->buf = buf;
        q->cap = cap;
    }

    q->f = f;
    memcpy(q->buf + q->len, text, len);
    q->len += len;
    SDL_CondSignal(out_wake);
    SDL_UnlockMutex(out_lock);
}

/* waits (at most 3 seconds) until everything queued is in the files: before exiting, and on crashes */
void pcLogFlush(void)
{
    Uint32 start = SDL_GetTicks();

    if (out_lock == NULL)
    {
        return;
    }

    while (SDL_GetTicks() - start < 3000)
    {
        int done;

        SDL_LockMutex(out_lock);
        done = (out_q[0].len == 0) && (out_q[1].len == 0) && !out_busy;
        SDL_UnlockMutex(out_lock);

        if (done)
        {
            break;
        }

        SDL_Delay(5);
    }
}

/* ------------------------------------------------------------------------------------------- */
/* logging */

void pcLog(const char* fmt, ...)
{
    va_list ap;

    if (log_file == NULL)
    {
        char path[1024];

        if (getenv("RECVX_CONSOLE") != NULL)
        {
            pcOsOpenConsole();
        }

        pcPlatformExeDir(path, sizeof(path) - 16);
        strcat(path, "/recvx.log");
        log_file = fopen(path, "w");
    }

    va_start(ap, fmt);
    vprintf(fmt, ap);
    va_end(ap);
    printf("\n");

    if (log_file != NULL)
    {
        char line[4096];
        int n;

        va_start(ap, fmt);
        n = vsnprintf(line, sizeof(line) - 1, fmt, ap);
        va_end(ap);

        if ((n < 0) || (n > (int)sizeof(line) - 2))
        {
            n = (int)strlen(line);
        }

        line[n++] = '\n';
        out_write(0, log_file, line, (size_t)n);
    }
}

void pcPlatformFatal(const char* fmt, ...)
{
    char msg[2048];
    va_list ap;

    va_start(ap, fmt);
    vsnprintf(msg, sizeof(msg), fmt, ap);
    va_end(ap);

    pcLog("FATAL: %s", msg);
    pcLogFlush();
    SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, "Resident Evil Code: Veronica X", msg, window);
    exit(1);
}

void pcPlatformNotSetUp(void)
{
    static const SDL_MessageBoxButtonData buttons[] = {
        { SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT, 1, "Open settings" },
        { SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT, 0, "Quit" },
    };
    SDL_MessageBoxData box;
    char dir[1024], exe[1100];
    int choice = 0;

    pcLog("FATAL: the game is not set up (no data folder or disc image)");

    if (window != NULL)
    {
        SDL_HideWindow(window);   /* not an empty black game window behind the question */
    }

    memset(&box, 0, sizeof(box));
    box.flags = SDL_MESSAGEBOX_INFORMATION;
    box.title = "Resident Evil Code: Veronica X";
    box.message = "The game is not set up yet.\n\n"
                  "It needs the files from your own copy of the game: a disc image (.iso) of Resident Evil "
                  "Code: Veronica X for the US PlayStation 2 (SLUS-20184).\n\n"
                  "Open the settings app to set it up?";
    box.numbuttons = 2;
    box.buttons = buttons;

    pcPlatformExeDir(dir, sizeof(dir));
    snprintf(exe, sizeof(exe), "%s\\recvx-config.exe", dir);

    if ((SDL_ShowMessageBox(&box, &choice) == 0) && (choice == 1) && !pcOsLaunch(exe))
    {
        SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, "Resident Evil Code: Veronica X",
                                 "Could not start recvx-config.exe. It should be in the same folder as recvx.exe.", NULL);
    }

    exit(1);
}

void pcPlatformExeDir(char* out, int size)
{
    char* base = SDL_GetBasePath();
    size_t n;

    if (base == NULL)
    {
        strncpy(out, ".", size);
        return;
    }

    strncpy(out, base, size - 1);
    out[size - 1] = '\0';
    SDL_free(base);

    n = strlen(out);

    if ((n > 0) && ((out[n - 1] == '\\') || (out[n - 1] == '/')))
    {
        out[n - 1] = '\0';
    }
}

int pcIniGet(const char* key, char* out, int size)
{
    char dir[1024], ini[1100];

    pcPlatformExeDir(dir, sizeof(dir));
    snprintf(ini, sizeof(ini), "%s/recvx.ini", dir);

    return pcIniFileGet(ini, key, out, size);
}

int pcIniFileGet(const char* ini, const char* key, char* out, int size)
{
    char line[4200];
    size_t klen = strlen(key);
    int found = 0;
    FILE* f;

    if ((f = fopen(ini, "r")) == NULL)
    {
        return 0;
    }

    while (fgets(line, sizeof(line), f) != NULL)
    {
        if ((_strnicmp(line, key, klen) == 0) && (line[klen] == '='))
        {
            size_t n;

            snprintf(out, size, "%s", line + klen + 1);
            n = strlen(out);

            while ((n > 0) && ((out[n - 1] == '\n') || (out[n - 1] == '\r') || (out[n - 1] == ' ') ||
                               (out[n - 1] == '\t')))
            {
                out[--n] = 0;
            }

            found = 1;
        }
    }

    fclose(f);
    return found;
}

void pcIniSet(const char* key, const char* value)
{
    char dir[1024], ini[1100], line[1300];
    char* out = NULL;
    size_t len = 0, cap = 0, klen = strlen(key);
    int written = 0;
    FILE* f;

    pcPlatformExeDir(dir, sizeof(dir));
    snprintf(ini, sizeof(ini), "%s/recvx.ini", dir);

    if ((f = fopen(ini, "r")) != NULL)
    {
        while (fgets(line, sizeof(line), f) != NULL)
        {
            size_t n;

            if ((_strnicmp(line, key, klen) == 0) && (line[klen] == '='))
            {
                if (written || (value == NULL))
                {
                    continue;   /* drop it (and any duplicates) */
                }

                snprintf(line, sizeof(line), "%s=%s\n", key, value);
                written = 1;
            }

            n = strlen(line);

            if (len + n + 2 > cap)
            {
                cap = (cap + n + 2) * 2;
                out = (char*)realloc(out, cap);
            }

            memcpy(out + len, line, n);
            len += n;

            if (out[len - 1] != '\n')
            {
                out[len++] = '\n';
            }
        }

        fclose(f);
    }

    if ((f = fopen(ini, "w")) == NULL)
    {
        free(out);
        return;
    }

    if (len > 0)
    {
        fwrite(out, 1, len, f);
    }

    if (!written && (value != NULL))
    {
        fprintf(f, "%s=%s\n", key, value);
    }

    fclose(f);
    free(out);
}

int pcIniGetInt(const char* key, int def)
{
    char v[64];

    return pcIniGet(key, v, sizeof(v)) ? atoi(v) : def;
}

int pcIniFileGetInt(const char* ini, const char* key, int def)
{
    char v[64];

    return pcIniFileGet(ini, key, v, sizeof(v)) ? atoi(v) : def;
}

int pcPlatformFindFile(const char* dir, const char* ext, char* out, int size)
{
    /* SDL2 has no directory listing; use the CRT one */
    char pattern[1100];
    struct _finddata_t fd;
    intptr_t h;

    snprintf(pattern, sizeof(pattern), "%s/*%s", dir, ext);
    h = _findfirst(pattern, &fd);

    if (h == -1)
    {
        return 0;
    }

    snprintf(out, size, "%s/%s", dir, fd.name);
    _findclose(h);

    return 1;
}

/* ------------------------------------------------------------------------------------------- */
/* video */

/* recvx.ini video settings: scale=N, window=WxH, fullscreen=0|1|desktop, aspect=W:H */
static struct {
    int loaded;
    int win_w, win_h;
    int win_x, win_y, has_pos;   /* window_pos= (remembered from the last run) */
    int maximized;               /* window_max= */
    int fullscreen;          /* 0 window, 1 exclusive, 2 desktop */
    int pause_unfocused;     /* pause_unfocused= (default 1) */
    float aspect;            /* display aspect, 4:3 = 1.333 */
} video_cfg;

/* the window as it was last seen windowed: saved to recvx.ini when the game closes */
static struct {
    int x, y, w, h, maximized;
} win_state;

static void video_settings(void)
{
    char v[64];
    int w, h;

    if (video_cfg.loaded)
    {
        return;
    }

    video_cfg.loaded = 1;
    video_cfg.win_w = PS2_WIDTH * 2;
    video_cfg.win_h = PS2_HEIGHT * 2;
    video_cfg.aspect = 4.0f / 3.0f;

    pcGsScale = pcIniGetInt("scale", pcGsScale);

    if (pcGsScale < 1) pcGsScale = 1;
    if (pcGsScale > 8) pcGsScale = 8;

    if (pcIniGet("fullscreen", v, sizeof(v)))
    {
        video_cfg.fullscreen = (_stricmp(v, "desktop") == 0) ? 2 : (atoi(v) != 0);
    }

    if (pcIniGet("aspect", v, sizeof(v)) && (sscanf(v, "%d:%d", &w, &h) == 2) && (w > 0) && (h > 0))
    {
        float a = (float)w / (float)h;

        /* narrower than 4:3 is not supported (the 2D layout is 4:3); cap at 32:9 */
        if (a < 4.0f / 3.0f) a = 4.0f / 3.0f;
        if (a > 32.0f / 9.0f) a = 32.0f / 9.0f;

        video_cfg.aspect = a;
        video_cfg.win_w = (int)(video_cfg.win_h * a + 0.5f);
    }

    if (pcIniGet("window", v, sizeof(v)) && (sscanf(v, "%dx%d", &w, &h) == 2) && (w >= 320) && (h >= 240))
    {
        video_cfg.win_w = w;
        video_cfg.win_h = h;
    }

    if (pcIniGet("window_pos", v, sizeof(v)) && (sscanf(v, "%d,%d", &w, &h) == 2))
    {
        video_cfg.win_x = w;
        video_cfg.win_y = h;
        video_cfg.has_pos = 1;
    }

    video_cfg.maximized = pcIniGetInt("window_max", 0) != 0;
    video_cfg.pause_unfocused = pcIniGetInt("pause_unfocused", 1) != 0;

    /* the 640 wide frame is stretched to the display aspect: render more columns to match */
    pcGsScaleX = (int)(pcGsScale * video_cfg.aspect / (4.0f / 3.0f) + 0.5f);
}

float pcVideoAspect(void)
{
    video_settings();
    return video_cfg.aspect;
}

/* horizontal factor that maps the widened 3D view (and the 4:3 2D layout) into the 640 wide
 * frame buffer: 1.0 at 4:3, 0.75 at 16:9 */
float pcVideoWideScale(void)
{
    video_settings();
    return (4.0f / 3.0f) / video_cfg.aspect;
}

static void toggle_fullscreen(void)
{
    Uint32 flags = SDL_GetWindowFlags(window);

    if (flags & (SDL_WINDOW_FULLSCREEN | SDL_WINDOW_FULLSCREEN_DESKTOP))
    {
        SDL_SetWindowFullscreen(window, 0);
    }
    else
    {
        SDL_SetWindowFullscreen(window, (video_cfg.fullscreen == 1) ? SDL_WINDOW_FULLSCREEN : SDL_WINDOW_FULLSCREEN_DESKTOP);
    }
}

/* the remembered spot is used only while it is still on a connected monitor */
static int position_visible(int x, int y, int w, int h)
{
    int i;

    for (i = 0; i < SDL_GetNumVideoDisplays(); i++)
    {
        SDL_Rect r;

        if ((SDL_GetDisplayUsableBounds(i, &r) == 0) && (x + w / 2 >= r.x) && (x + w / 2 < r.x + r.w) &&
            (y >= r.y - 8) && (y + 32 < r.y + r.h))
        {
            return 1;
        }
    }

    return 0;
}

static void track_window(void)
{
    Uint32 flags = SDL_GetWindowFlags(window);

    if (flags & (SDL_WINDOW_FULLSCREEN | SDL_WINDOW_FULLSCREEN_DESKTOP | SDL_WINDOW_MINIMIZED))
    {
        return;
    }

    win_state.maximized = (flags & SDL_WINDOW_MAXIMIZED) != 0;

    if (!win_state.maximized)
    {
        SDL_GetWindowPosition(window, &win_state.x, &win_state.y);
        SDL_GetWindowSize(window, &win_state.w, &win_state.h);
    }
}

/* the next run opens the window where this one was left */
static void save_window(void)
{
    char v[64];

    if ((window == NULL) || (win_state.w == 0) || (getenv("RECVX_INPUT") != NULL) || (getenv("RECVX_REPLAY") != NULL))
    {
        return;   /* scripted test runs leave the settings alone */
    }

    if ((win_state.w != video_cfg.win_w) || (win_state.h != video_cfg.win_h))
    {
        snprintf(v, sizeof(v), "%dx%d", win_state.w, win_state.h);
        pcIniSet("window", v);
    }

    if (!video_cfg.has_pos || (win_state.x != video_cfg.win_x) || (win_state.y != video_cfg.win_y))
    {
        snprintf(v, sizeof(v), "%d,%d", win_state.x, win_state.y);
        pcIniSet("window_pos", v);
    }

    if (win_state.maximized != video_cfg.maximized)
    {
        pcIniSet("window_max", win_state.maximized ? "1" : NULL);
    }
}

void pcVideoInit(void)
{
    Uint32 flags = SDL_WINDOW_RESIZABLE | SDL_WINDOW_OPENGL;
    int x = SDL_WINDOWPOS_CENTERED, y = SDL_WINDOWPOS_CENTERED;

    if (window != NULL)
    {
        return;
    }

    pcOsInstallDiagnostics();
    SDL_SetMainReady();

    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMECONTROLLER | SDL_INIT_AUDIO) != 0)
    {
        pcPlatformFatal("SDL_Init failed: %s", SDL_GetError());
    }

    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 3);
    SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK, SDL_GL_CONTEXT_PROFILE_CORE);
    SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);

    video_settings();

    if (video_cfg.fullscreen)
    {
        flags |= (video_cfg.fullscreen == 1) ? SDL_WINDOW_FULLSCREEN : SDL_WINDOW_FULLSCREEN_DESKTOP;
    }

    if (video_cfg.has_pos && position_visible(video_cfg.win_x, video_cfg.win_y, video_cfg.win_w, video_cfg.win_h))
    {
        x = video_cfg.win_x;
        y = video_cfg.win_y;
    }

    if (video_cfg.maximized && !video_cfg.fullscreen)
    {
        flags |= SDL_WINDOW_MAXIMIZED;
    }

    window = SDL_CreateWindow("Resident Evil Code: Veronica X", x, y, video_cfg.win_w, video_cfg.win_h, flags);

    if (window == NULL)
    {
        pcPlatformFatal("Could not create the window: %s", SDL_GetError());
    }

    {
        SDL_SysWMinfo info;

        SDL_VERSION(&info.version);

        if (SDL_GetWindowWMInfo(window, &info))
        {
            pcOsSetWindowIcon(info.info.win.window);
        }
    }

    /* the windowed size and spot, also when starting fullscreen or maximized */
    win_state.x = (x == SDL_WINDOWPOS_CENTERED) ? 0 : x;
    win_state.y = (y == SDL_WINDOWPOS_CENTERED) ? 0 : y;
    win_state.w = video_cfg.win_w;
    win_state.h = video_cfg.win_h;
    win_state.maximized = video_cfg.maximized;
    track_window();
    atexit(save_window);

    gl_context = SDL_GL_CreateContext(window);

    if (gl_context == NULL)
    {
        pcPlatformFatal("Could not create an OpenGL 3.3 context: %s", SDL_GetError());
    }

    SDL_GL_SetSwapInterval(0);   /* frame pacing comes from the emulated VBLANK */

    {
        unsigned char* spr = pcOsAllocScratchpad();

        if (spr == NULL)
        {
            pcPlatformFatal("Could not reserve the PS2 scratchpad address range (0x70000000).");
        }

        pcGsInit(spr);
    }

    pcPadInit();

    pcLog("video: SDL %d.%d.%d, window %dx%d%s, aspect %.3f", SDL_MAJOR_VERSION, SDL_MINOR_VERSION, SDL_PATCHLEVEL,
          video_cfg.win_w, video_cfg.win_h, video_cfg.fullscreen ? " (fullscreen)" : "", video_cfg.aspect);
}

/* testing aids drive the game without the window being active: never pause for them */
static int scripted_run(void)
{
    return (getenv("RECVX_INPUT") != NULL) || (getenv("RECVX_REPLAY") != NULL) ||
           (getenv("RECVX_AUTOPRESS") != NULL) || (getenv("RECVX_SHOTS") != NULL);
}

/* the window lost focus: hold the game (and its sound) until it is active again;
 * returns 0 when the window was closed meanwhile */
static int pause_until_active(void)
{
    SDL_Event ev;

    pcAudioPause(1);
    SDL_SetWindowTitle(window, "Resident Evil Code: Veronica X  [PAUSED]");
    SDL_ShowCursor(SDL_ENABLE);

    for (;;)
    {
        pcOsHeartbeat();   /* paused on purpose, not hung */

        if (!SDL_WaitEventTimeout(&ev, 500))
        {
            continue;
        }

        if (ev.type == SDL_QUIT)
        {
            return 0;
        }

        if (ev.type == SDL_WINDOWEVENT)
        {
            if (ev.window.event == SDL_WINDOWEVENT_FOCUS_GAINED)
            {
                break;
            }

            if ((ev.window.event == SDL_WINDOWEVENT_MOVED) || (ev.window.event == SDL_WINDOWEVENT_SIZE_CHANGED))
            {
                track_window();
            }

            if ((ev.window.event == SDL_WINDOWEVENT_EXPOSED) || (ev.window.event == SDL_WINDOWEVENT_SIZE_CHANGED))
            {
                pcGsPresent();   /* redraw the frozen picture */
                SDL_GL_SwapWindow(window);
            }
        }

        if ((ev.type == SDL_CONTROLLERDEVICEADDED) || (ev.type == SDL_CONTROLLERDEVICEREMOVED))
        {
            pcPadInit();
        }
    }

    update_title(1);
    pcAudioPause(0);
    return 1;
}

static Uint32 last_mouse_move;

int pcVideoPumpEvents(void)
{
    SDL_Event ev;

    while (SDL_PollEvent(&ev))
    {
        if (ev.type == SDL_QUIT)
        {
            return 0;
        }

        if (ev.type == SDL_WINDOWEVENT)
        {
            if ((ev.window.event == SDL_WINDOWEVENT_MOVED) || (ev.window.event == SDL_WINDOWEVENT_SIZE_CHANGED) ||
                (ev.window.event == SDL_WINDOWEVENT_MAXIMIZED) || (ev.window.event == SDL_WINDOWEVENT_RESTORED))
            {
                track_window();
            }

            if ((ev.window.event == SDL_WINDOWEVENT_FOCUS_LOST) && video_cfg.pause_unfocused && !scripted_run() &&
                !pause_until_active())
            {
                return 0;
            }
        }

        /* the mouse is not used by the game: it hides after 2 seconds without moving */
        if (ev.type == SDL_MOUSEMOTION)
        {
            last_mouse_move = SDL_GetTicks();
            SDL_ShowCursor(SDL_ENABLE);
        }

        /* Alt+Enter: fullscreen on/off, like other Windows games */
        if ((ev.type == SDL_KEYDOWN) && !ev.key.repeat && (ev.key.keysym.scancode == SDL_SCANCODE_RETURN) &&
            (ev.key.keysym.mod & KMOD_ALT))
        {
            toggle_fullscreen();
            continue;
        }

        if ((ev.type == SDL_CONTROLLERDEVICEADDED) || (ev.type == SDL_CONTROLLERDEVICEREMOVED))
        {
            pcPadInit();
        }

        if (((ev.type == SDL_KEYDOWN) && !ev.key.repeat && (ev.key.keysym.scancode != SDL_SCANCODE_UNKNOWN) &&
             (ev.key.keysym.scancode == pcPadFullscreenKey())) ||
            ((ev.type == SDL_CONTROLLERBUTTONDOWN) && (ev.cbutton.button == pcPadFullscreenButton())))
        {
            toggle_fullscreen();
        }

        save_event(&ev);

        if (!title_event(&ev))
        {
            return 0;
        }
    }

    return 1;
}

void pcVideoPresent(void)
{
    static double last_report;

    if (!pcVideoPumpEvents())
    {
        pcLog("window closed");
        exit(0);
    }

    if ((SDL_GetTicks() - last_mouse_move > 2000) && (SDL_ShowCursor(SDL_QUERY) == SDL_ENABLE))
    {
        SDL_ShowCursor(SDL_DISABLE);
    }

    pcPadUpdate();
    pcOsHeartbeat();
    fps_tick();

    pcGsPresent();

    /* testing aid: RECVX_SHOTS=<seconds> saves the display buffer to shots/ periodically */
    {
        static int interval = -1;
        static unsigned int last;

        if (interval < 0)
        {
            const char* e = SDL_getenv("RECVX_SHOTS");

            interval = (e != NULL) ? atoi(e) * 1000 : 0;
        }

        if ((interval > 0) && (SDL_GetTicks() - last >= (unsigned int)interval))
        {
            extern void pcGsScreenshot(const char* path, unsigned int fbp);
            extern unsigned int pcGsDisplayFbp(void);
            char path[1100], dir[1024];

            last = SDL_GetTicks();
            pcPlatformExeDir(dir, sizeof(dir));
            snprintf(path, sizeof(path), "%s/shots/shot_%05u.png", dir, frames);
            pcGsScreenshot(path, pcGsDisplayFbp());
        }
    }


    {
        double t0 = pcOsTime();

        SDL_GL_SwapWindow(window);
        frame_time.swap += pcOsTime() - t0;
    }

    /* frame times for the periodic log line */
    {
        double now = pcOsTime();

        if (frame_time.last > 0)
        {
            double d = now - frame_time.last;

            frame_time.sum += d;
            frame_time.n++;

            if (d > frame_time.worst)
            {
                frame_time.worst = d;
            }
        }

        frame_time.last = now;
    }

    frames++;
    save_tick();

    if (SDL_GetTicks() - last_report >= 5000)
    {
        pcLog("frame %u: %.1f ms per frame (worst %.1f ms), %.1f ms waiting for the GPU", frames,
              frame_time.n ? frame_time.sum * 1000 / frame_time.n : 0.0, frame_time.worst * 1000,
              frame_time.n ? frame_time.swap * 1000 / frame_time.n : 0.0);
        frame_time.sum = frame_time.swap = frame_time.worst = 0;
        frame_time.n = 0;
        {
            extern long pcAudioStarved(int v);
            static long starved_seen[16];
            int v;

            for (v = 0; v < 16; v++)
            {
                long now = pcAudioStarved(v);

                if (now != starved_seen[v])
                {
                    pcLog("audio: voice %d ran dry %ld time(s)", v, now - starved_seen[v]);
                    starved_seen[v] = now;
                }
            }
        }
        {
            extern void pcGameStateLog(void);
            extern void pcGsLogStats(void);

            pcGameStateLog();
            pcGsLogStats();
        }
        last_report = SDL_GetTicks();
    }
}

/* ------------------------------------------------------------------------------------------- */
/* controller: SDL game controller or keyboard -> DualShock 2 report */

static SDL_GameController* controller;
static unsigned char pad_report[32];

typedef struct {
    int byte, bit, pressure;           /* report byte/bit (active low), pressure byte or -1 */
    SDL_Scancode key;
    SDL_GameControllerButton button;
    SDL_GameControllerAxis axis;       /* a trigger used as a button, or INVALID */
} PadMap;

/* report position of each DualShock 2 button, in pc_binding_defs order */
static const struct { int byte, bit, pressure; } pad_bits[PC_BINDING_PS2_BUTTONS] = {
    { 3, 6, 14 }, { 3, 7, 15 }, { 3, 5, 13 }, { 3, 4, 12 },    /* cross square circle triangle */
    { 3, 2, 16 }, { 3, 3, 17 }, { 3, 0, 18 }, { 3, 1, 19 },    /* l1 r1 l2 r2 */
    { 2, 1, -1 }, { 2, 2, -1 }, { 2, 3, -1 }, { 2, 0, -1 },    /* l3 r3 start select */
    { 2, 4, 10 }, { 2, 6, 11 }, { 2, 7,  9 }, { 2, 5,  8 },    /* up down left right */
};

/* keyboard and controller bindings: pc_binding_defs defaults, overridden by recvx.ini
 * key_<name>= / pad_<name>= lines (written by recvx-config.exe) */
static PadMap pad_map[PC_BINDING_COUNT];
#define PAD_FASTFORWARD PC_BINDING_PS2_BUTTONS
#define PAD_FULLSCREEN  (PC_BINDING_PS2_BUTTONS + 1)
#define PAD_TITLE       (PC_BINDING_PS2_BUTTONS + 2)
#define PAD_SAVE        (PC_BINDING_PS2_BUTTONS + 3)

static void load_bindings(void)
{
    static int loaded;
    int i;

    if (loaded)
    {
        return;
    }

    loaded = 1;

    for (i = 0; i < PC_BINDING_COUNT; i++)
    {
        const PcBindingDef* d = &pc_binding_defs[i];
        PadMap* m = &pad_map[i];
        char ini[64], key[64], pad[64];

        snprintf(key, sizeof(key), "%s", d->key);
        snprintf(pad, sizeof(pad), "%s", d->pad);
        snprintf(ini, sizeof(ini), "key_%s", d->name);
        pcIniGet(ini, key, sizeof(key));
        snprintf(ini, sizeof(ini), "pad_%s", d->name);
        pcIniGet(ini, pad, sizeof(pad));

        m->byte = (i < PC_BINDING_PS2_BUTTONS) ? pad_bits[i].byte : -1;
        m->bit = (i < PC_BINDING_PS2_BUTTONS) ? pad_bits[i].bit : 0;
        m->pressure = (i < PC_BINDING_PS2_BUTTONS) ? pad_bits[i].pressure : -1;
        m->key = SDL_SCANCODE_UNKNOWN;
        m->button = SDL_CONTROLLER_BUTTON_INVALID;
        m->axis = SDL_CONTROLLER_AXIS_INVALID;

        if ((key[0] != 0) && (_stricmp(key, "none") != 0))
        {
            m->key = SDL_GetScancodeFromName(key);

            if (m->key == SDL_SCANCODE_UNKNOWN)
            {
                pcLog("input: unknown key \"%s\" for %s", key, d->name);
            }
        }

        if ((pad[0] != 0) && (_stricmp(pad, "none") != 0))
        {
            m->button = SDL_GameControllerGetButtonFromString(pad);

            if (m->button == SDL_CONTROLLER_BUTTON_INVALID)
            {
                m->axis = SDL_GameControllerGetAxisFromString(pad);

                if ((m->axis != SDL_CONTROLLER_AXIS_TRIGGERLEFT) && (m->axis != SDL_CONTROLLER_AXIS_TRIGGERRIGHT))
                {
                    m->axis = SDL_CONTROLLER_AXIS_INVALID;
                    pcLog("input: unknown controller button \"%s\" for %s", pad, d->name);
                }
            }
        }
    }
}

static int pcPadFullscreenKey(void)
{
    load_bindings();
    return pad_map[PAD_FULLSCREEN].key;
}

static int pcPadFullscreenButton(void)
{
    load_bindings();
    return pad_map[PAD_FULLSCREEN].button;
}

/* ------------------------------------------------------------------------------------------- */
/* back to the title screen: the "title" binding (F9), like the PC versions of the classic games.
 * The game pauses on a black screen asking to confirm; pressing it again confirms, any other key
 * or button goes back to the game. The game takes the request (pcTitleRequested, from
 * bhSysCallSndMonitor) and does its own soft reset. */

static unsigned int title_request;   /* SDL ticks of the confirming press, 0 = none */

static int is_title_key(const SDL_Event* ev)
{
    load_bindings();

    return ((ev->type == SDL_KEYDOWN) && !ev->key.repeat && (ev->key.keysym.scancode != SDL_SCANCODE_UNKNOWN) &&
            (ev->key.keysym.scancode == pad_map[PAD_TITLE].key)) ||
           ((ev->type == SDL_CONTROLLERBUTTONDOWN) && (ev->cbutton.button == pad_map[PAD_TITLE].button));
}

/* the question on a black screen, the game and its sound held meanwhile; returns 0 when the
 * window was closed */
static int title_event(const SDL_Event* ev)
{
    extern void pcOverlayNotice(const char* text);
    extern int pcTitleAllowed(void);
    SDL_Event e;
    char text[200];
    int answer = -1, open = 1;

    if (!is_title_key(ev) || !pcTitleAllowed())
    {
        return 1;   /* also: not at the title screen and its menus, nor in a movie */
    }

    snprintf(text, sizeof(text), "Return to the title screen?\n%s: yes      any other key: no",
             (pad_map[PAD_TITLE].key != SDL_SCANCODE_UNKNOWN) ? SDL_GetScancodeName(pad_map[PAD_TITLE].key) : "Again");
    pcOverlayNotice(text);
    pcAudioPause(1);

    while (answer < 0)
    {
        pcOsHeartbeat();   /* paused on purpose, not hung */
        pcGsPresent();
        SDL_GL_SwapWindow(window);

        if (!SDL_WaitEventTimeout(&e, 100))
        {
            continue;
        }

        if (e.type == SDL_QUIT)
        {
            open = 0;
            break;
        }

        if ((e.type == SDL_WINDOWEVENT) && ((e.window.event == SDL_WINDOWEVENT_MOVED) ||
                                            (e.window.event == SDL_WINDOWEVENT_SIZE_CHANGED)))
        {
            track_window();
        }

        if ((e.type == SDL_CONTROLLERDEVICEADDED) || (e.type == SDL_CONTROLLERDEVICEREMOVED))
        {
            pcPadInit();
        }

        if (is_title_key(&e))
        {
            answer = 1;
        }
        else if (((e.type == SDL_KEYDOWN) && !e.key.repeat) || (e.type == SDL_CONTROLLERBUTTONDOWN))
        {
            answer = 0;
        }
    }

    pcOverlayNotice(NULL);
    pcAudioPause(0);

    if (answer == 1)
    {
        title_request = SDL_GetTicks() | 1;
        pcLog("back to the title screen requested");
    }

    return open;
}

/* ------------------------------------------------------------------------------------------- */
/* window title: the frame rate, and [FAST FORWARD] while that is held */

static int title_fps;

static const char* title_notice;     /* shown in the title bar for a few seconds */
static unsigned int title_notice_until;

static void title_notice_show(const char* text)
{
    title_notice = text;
    title_notice_until = SDL_GetTicks() + 4000;
    update_title(0);
}

static void update_title(int force)
{
    extern int pcFastForward;
    static char shown[192];
    char text[192];
    int notice = (title_notice != NULL) && ((int)(title_notice_until - SDL_GetTicks()) > 0);

    snprintf(text, sizeof(text), "Resident Evil Code: Veronica X  [%d FPS]%s%s%s%s", title_fps,
             pcFastForward ? "  [FAST FORWARD]" : "", notice ? "  [" : "", notice ? title_notice : "",
             notice ? "]" : "");

    if (force || (strcmp(text, shown) != 0))
    {
        SDL_SetWindowTitle(window, text);
        snprintf(shown, sizeof(shown), "%s", text);
    }
}

/* once per presented frame: frames per second over the last half second */
static void fps_tick(void)
{
    static unsigned int count, since;
    unsigned int now = SDL_GetTicks();

    count++;

    if (since == 0)
    {
        since = now;
    }

    if (now - since >= 500)
    {
        title_fps = (int)(count * 1000.0 / (now - since) + 0.5);
        count = 0;
        since = now;
        update_title(0);
    }
}

/* save anywhere (debug, debug_save=1 in recvx.ini): the "save" binding (F5) opens the
 * typewriter's save screen without a typewriter or an ink ribbon. The game takes the request
 * (pcSaveAnywhereRequested, from bhCheckSubTask) only while the player could open the status
 * screen; a request it could not take within a second is dropped. */

static unsigned int save_request;    /* SDL ticks of the press, 0 = none */

static void save_event(const SDL_Event* ev)
{
    static int enabled = -1;

    if (enabled < 0)
    {
        enabled = pcIniGetInt("debug_save", 0) != 0;
    }

    load_bindings();

    if (((ev->type == SDL_KEYDOWN) && !ev->key.repeat && (ev->key.keysym.scancode != SDL_SCANCODE_UNKNOWN) &&
         (ev->key.keysym.scancode == pad_map[PAD_SAVE].key)) ||
        ((ev->type == SDL_CONTROLLERBUTTONDOWN) && (ev->cbutton.button == pad_map[PAD_SAVE].button)))
    {
        if (!enabled)
        {
            title_notice_show("Save anywhere is off: turn it on in recvx-config.exe");
            pcLog("save anywhere key pressed, but debug_save is off");
            return;
        }

        save_request = SDL_GetTicks() | 1;
        pcLog("save anywhere requested");
    }
}

/* once per frame: a request the game did not take within a second is dropped, and the player
 * is told (cutscenes, doors, menus and enemy grabs do not allow it) */
static void save_tick(void)
{
    if ((save_request != 0) && (SDL_GetTicks() - save_request > 1000))
    {
        save_request = 0;
        title_notice_show("Save anywhere: not possible right now");
        pcLog("save anywhere: not taken (not in normal play within a second)");
    }
}

/* 1 once after the press, if it was within the last second */
int pcSaveAnywhereRequested(void)
{
    if ((save_request == 0) || (SDL_GetTicks() - save_request > 1000))
    {
        return 0;
    }

    save_request = 0;
    return 1;
}

/* 1 once after the confirming press (the game asks only while a soft reset can be done; a
 * request it could not take within a second is dropped) */
int pcTitleRequested(void)
{
    if ((title_request == 0) || (SDL_GetTicks() - title_request > 1000))
    {
        title_request = 0;
        return 0;
    }

    title_request = 0;
    return 1;
}

void pcPadInit(void)
{
    int i;

    load_bindings();

    if (controller != NULL)
    {
        SDL_GameControllerClose(controller);
        controller = NULL;
    }

    for (i = 0; i < SDL_NumJoysticks(); i++)
    {
        if (SDL_IsGameController(i))
        {
            controller = SDL_GameControllerOpen(i);

            if (controller != NULL)
            {
                pcLog("controller: %s", SDL_GameControllerName(controller));
                break;
            }
        }
    }
}

/* a binding is held on the keyboard or the controller */
static int pad_down(const PadMap* m, const Uint8* keys)
{
    int down = (m->key != SDL_SCANCODE_UNKNOWN) && keys[m->key];

    if ((m->key == SDL_SCANCODE_RETURN) && (SDL_GetModState() & KMOD_ALT))
    {
        down = 0;   /* Alt+Enter is the fullscreen toggle */
    }

    if (controller != NULL)
    {
        if (m->button != SDL_CONTROLLER_BUTTON_INVALID)
        {
            down |= SDL_GameControllerGetButton(controller, m->button);
        }

        if (m->axis != SDL_CONTROLLER_AXIS_INVALID)
        {
            down |= SDL_GameControllerGetAxis(controller, m->axis) > 8000;
        }
    }

    return down;
}

static unsigned char axis_to_byte(Sint16 v)
{
    return (unsigned char)((v + 32768) >> 8);
}

/* Testing aid: RECVX_RECORD=<file> (default last_run_input.txt) writes the controller state of every frame where it changed
 * ("<frame> <buttons hex> <rx> <ry> <lx> <ly>"); RECVX_REPLAY=<file> plays such a file back
 * instead of the keyboard/controller. */
static void pcPadRecordReplay(unsigned char* r)
{
    static int mode = -1;
    static FILE* f;
    static unsigned int last_buttons = 0xFFFFFFFF, last_axes;
    static struct { unsigned int frame, buttons, axes; } next;
    static int have_next;
    static unsigned int cur_buttons = 0xFFFF, cur_axes = 0x80808080;
    size_t i;

    if (mode < 0)
    {
        const char* rec = SDL_getenv("RECVX_RECORD");
        const char* rep = SDL_getenv("RECVX_REPLAY");

        mode = 0;

        if ((rep != NULL) && ((f = fopen(rep, "r")) != NULL))
        {
            mode = 2;
            pcLog("input: replaying %s", rep);
        }
        else
        {
            /* every session is recorded (to last_run_input.txt by default) so a problem can be replayed */
            char path[1100], dir[1024];

            if (rec == NULL)
            {
                char prev[1100];

                /* scripted test runs are not worth recording */
                if ((SDL_getenv("RECVX_INPUT") != NULL) || (SDL_getenv("RECVX_AUTOPRESS") != NULL))
                {
                    return;
                }

                /* keep the previous session's recording one more run */
                pcPlatformExeDir(dir, sizeof(dir));
                snprintf(path, sizeof(path), "%s/last_run_input.txt", dir);
                snprintf(prev, sizeof(prev), "%s/last_run_input.prev.txt", dir);
                remove(prev);
                rename(path, prev);
                rec = path;
            }

            if ((f = fopen(rec, "w")) != NULL)
            {
                mode = 1;
                pcLog("input: recording to %s", rec);
            }
        }
    }

    if (mode == 1)
    {
        unsigned int buttons = r[2] | (r[3] << 8);
        unsigned int axes = r[4] | (r[5] << 8) | (r[6] << 16) | ((unsigned int)r[7] << 24);

        if ((buttons != last_buttons) || (axes != last_axes))
        {
            char line[64];
            int n = snprintf(line, sizeof(line), "%u %04x %u %u %u %u\n", frames, buttons, r[4], r[5], r[6], r[7]);

            out_write(1, f, line, (size_t)n);
            last_buttons = buttons;
            last_axes = axes;
        }
    }
    else if (mode == 2)
    {
        for (;;)
        {
            if (!have_next)
            {
                unsigned int a, b, c, d;

                if (fscanf(f, "%u %x %u %u %u %u", &next.frame, &next.buttons, &a, &b, &c, &d) != 6)
                {
                    break;
                }

                next.axes = a | (b << 8) | (c << 16) | (d << 24);
                have_next = 1;
            }

            if (next.frame > frames)
            {
                break;
            }

            cur_buttons = next.buttons;
            cur_axes = next.axes;
            have_next = 0;
        }

        r[2] = (unsigned char)cur_buttons;
        r[3] = (unsigned char)(cur_buttons >> 8);
        r[4] = (unsigned char)cur_axes;
        r[5] = (unsigned char)(cur_axes >> 8);
        r[6] = (unsigned char)(cur_axes >> 16);
        r[7] = (unsigned char)(cur_axes >> 24);

        for (i = 8; i < 20; i++)
        {
            r[i] = 0;
        }

        for (i = 0; i < PC_BINDING_PS2_BUTTONS; i++)
        {
            if ((pad_map[i].pressure >= 0) && !(r[pad_map[i].byte] & (1 << pad_map[i].bit)))
            {
                r[pad_map[i].pressure] = 0xFF;
            }
        }
    }
}

void pcPadUpdate(void)
{
    const Uint8* keys = SDL_GetKeyboardState(NULL);
    unsigned char* r = pad_report;
    size_t i;

    memset(r, 0, sizeof(pad_report));

    r[0] = 0;        /* success */
    r[1] = 0x79;     /* DualShock 2, pressure-sensitive mode, 9 halfwords follow */
    r[2] = 0xFF;
    r[3] = 0xFF;
    r[4] = r[5] = r[6] = r[7] = 0x80;

    /* hold the fast-forward binding (Numpad 0) to fast-forward */
    {
        extern int pcFastForward;
        static int shown;

        pcFastForward = pad_down(&pad_map[PAD_FASTFORWARD], keys);

        if (pcFastForward != shown)
        {
            shown = pcFastForward;
            update_title(0);
        }
    }

    for (i = 0; i < PC_BINDING_PS2_BUTTONS; i++)
    {
        const PadMap* m = &pad_map[i];

        if (pad_down(m, keys))
        {
            r[m->byte] &= ~(1 << m->bit);

            if (m->pressure >= 0)
            {
                r[m->pressure] = 0xFF;
            }
        }
    }

    /* testing aid: RECVX_INPUT=<file> replays button presses. Each line: "<frame> <button>[+<button>...] [hold]"
     * where <frame> is a frame number (only until the status screen first opens), "title+<n>"
     * (n frames after the title screen first starts, so load times don't shift it) or
     * "status+<n>" (n frames after the status screen opens, each time);
     * with buttons start select cross circle square triangle up down left right l1 r1 l2 r2;
     * the buttons are held for <hold> frames (default 4) from that frame on. */
    {
        static struct { unsigned int frame, hold; unsigned short mask; int rel; } script[512];
        static unsigned int status_frame = 0xFFFFFFFF;
        static unsigned int title_frame = 0xFFFFFFFF;
        static int in_status;
        static int nscript = -1;
        static const char* names[16] = { "select", "l3", "r3", "start", "up", "right", "down", "left",
                                         "l2", "r2", "l1", "r1", "triangle", "circle", "cross", "square" };
        int k;

        if (nscript < 0)
        {
            const char* e = SDL_getenv("RECVX_INPUT");
            FILE* f = (e != NULL) ? fopen(e, "r") : NULL;
            char line[256];

            nscript = 0;

            while ((f != NULL) && (nscript < 512) && fgets(line, sizeof(line), f))
            {
                char btn[200];
                unsigned int fr, hold = 4;
                char* tok;
                unsigned short mask = 0;

                int rel = 0;

                if (strncmp(line, "status+", 7) == 0)
                {
                    rel = 1;
                    memmove(line, line + 7, strlen(line + 7) + 1);
                }
                else if (strncmp(line, "title+", 6) == 0)
                {
                    rel = 2;
                    memmove(line, line + 6, strlen(line + 6) + 1);
                }

                if (sscanf(line, "%u %199s %u", &fr, btn, &hold) < 2)
                {
                    continue;
                }

                for (tok = strtok(btn, "+"); tok != NULL; tok = strtok(NULL, "+"))
                {
                    for (k = 0; k < 16; k++)
                    {
                        if (strcmp(tok, names[k]) == 0)
                        {
                            mask |= 1 << k;
                        }
                    }
                }

                script[nscript].frame = fr;
                script[nscript].hold = hold;
                script[nscript].mask = mask;
                script[nscript].rel = rel;
                nscript++;
            }

            if (f != NULL)
            {
                fclose(f);
                pcLog("input script: %d entries", nscript);
            }
        }

        {
            extern int pcGameInStatusScreen(void);
            int now = pcGameInStatusScreen();

            if (now && !in_status)
            {
                status_frame = frames;
            }

            in_status = now;
        }

        {
            extern int pcGameInTitle(void);

            if ((title_frame == 0xFFFFFFFF) && pcGameInTitle())
            {
                title_frame = frames;
            }
        }

        for (k = 0; k < nscript; k++)
        {
            unsigned int at = (script[k].rel == 1) ? status_frame + script[k].frame :
                              (script[k].rel == 2) ? title_frame + script[k].frame : script[k].frame;

            /* status entries wait for the status screen; the others stop once it has opened */
            if ((status_frame == 0xFFFFFFFF) == (script[k].rel == 1))
            {
                continue;
            }

            if ((script[k].rel == 2) && (title_frame == 0xFFFFFFFF))
            {
                continue;
            }

            if ((frames >= at) && (frames < at + script[k].hold))
            {
                int b;

                for (b = 0; b < 16; b++)
                {
                    if (script[k].mask & (1 << b))
                    {
                        r[2 + (b >> 3)] &= ~(1 << (b & 7));
                    }
                }

                if (script[k].mask & 0x4000)
                {
                    r[14] = 0xFF;
                }
            }
        }
    }

    /* testing aid: RECVX_AUTOPRESS=1 taps START and CROSS every two seconds;
     * RECVX_AUTOPRESS=2 stops pressing START after the title (stays out of the status screen) */
    {
        static int autopress = -1;
        static unsigned int count;

        if (autopress < 0)
        {
            const char* e = SDL_getenv("RECVX_AUTOPRESS");

            autopress = (e != NULL) ? atoi(e) : 0;
        }

        if (autopress && ((++count % 60) < 3))
        {
            if ((autopress != 2) || (count < 1200))
            {
                r[2] &= ~0x08;
            }

            r[3] &= ~0x40;
            r[14] = 0xFF;
        }
    }

    if (controller != NULL)
    {
        r[4] = axis_to_byte(SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_RIGHTX));
        r[5] = axis_to_byte(SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_RIGHTY));
        r[6] = axis_to_byte(SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_LEFTX));
        r[7] = axis_to_byte(SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_LEFTY));
    }

    pcPadRecordReplay(r);
}

/* an OK button (Cross or Square, both OK / Attack in the game) held this frame, from the keyboard,
 * controller, input script or replay: skipping doors */
int pcPadOkDown(void)
{
    return (pad_report[3] & ((1 << 6) | (1 << 7))) != ((1 << 6) | (1 << 7));
}

void pcPadGetState(int port, unsigned char* rdata)
{
    if (port == 0)
    {
        memcpy(rdata, pad_report, sizeof(pad_report));
    }
    else
    {
        memset(rdata, 0, 32);
        rdata[0] = 0xFF;   /* nothing connected */
    }
}
