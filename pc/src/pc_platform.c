/* PC port: SDL window, frame presentation, logging and controller input. */
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <io.h>

#include <SDL.h>

#include "pc_platform.h"
#include "pc_os.h"
#include "pc_gs.h"
#include "pc_bindings.h"

static unsigned int frames;   /* frames presented */

#define PS2_WIDTH  640
#define PS2_HEIGHT 480

static SDL_Window* window;
static int pcPadFullscreenKey(void);
static int pcPadFullscreenButton(void);
static SDL_GLContext gl_context;
static FILE* log_file;

/* ------------------------------------------------------------------------------------------- */
/* logging */

void pcLog(const char* fmt, ...)
{
    va_list ap;

    if (log_file == NULL)
    {
        char path[1024];

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
        va_start(ap, fmt);
        vfprintf(log_file, fmt, ap);
        va_end(ap);
        fprintf(log_file, "\n");
        fflush(log_file);
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
    SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, "Resident Evil Code: Veronica X", msg, window);
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
    char dir[1024], ini[1100], line[1300];
    size_t klen = strlen(key);
    int found = 0;
    FILE* f;

    pcPlatformExeDir(dir, sizeof(dir));
    snprintf(ini, sizeof(ini), "%s/recvx.ini", dir);

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

int pcIniGetInt(const char* key, int def)
{
    char v[64];

    return pcIniGet(key, v, sizeof(v)) ? atoi(v) : def;
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
    int fullscreen;          /* 0 window, 1 exclusive, 2 desktop */
    float aspect;            /* display aspect, 4:3 = 1.333 */
} video_cfg;

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

void pcVideoInit(void)
{
    Uint32 flags = SDL_WINDOW_RESIZABLE | SDL_WINDOW_OPENGL;

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

    window = SDL_CreateWindow("Resident Evil Code: Veronica X", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                              video_cfg.win_w, video_cfg.win_h, flags);

    if (window == NULL)
    {
        pcPlatformFatal("Could not create the window: %s", SDL_GetError());
    }

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

int pcVideoPumpEvents(void)
{
    SDL_Event ev;

    while (SDL_PollEvent(&ev))
    {
        if (ev.type == SDL_QUIT)
        {
            return 0;
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

    pcPadUpdate();
    pcOsHeartbeat();

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

    SDL_GL_SwapWindow(window);

    frames++;

    if (SDL_GetTicks() - last_report >= 5000)
    {
        pcLog("frame %u", frames);
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
            fprintf(f, "%u %04x %u %u %u %u\n", frames, buttons, r[4], r[5], r[6], r[7]);
            fflush(f);
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
            SDL_SetWindowTitle(window, pcFastForward ? "Resident Evil Code: Veronica X  [FAST FORWARD]" : "Resident Evil Code: Veronica X");
            shown = pcFastForward;
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
