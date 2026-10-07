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

static unsigned int frames;   /* frames presented */

#define PS2_WIDTH  640
#define PS2_HEIGHT 480

static SDL_Window* window;
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

void pcVideoInit(void)
{
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

    window = SDL_CreateWindow("Resident Evil Code: Veronica X", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                              PS2_WIDTH * 2, PS2_HEIGHT * 2, SDL_WINDOW_RESIZABLE | SDL_WINDOW_OPENGL);

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

    pcLog("video: SDL %d.%d.%d, window %dx%d", SDL_MAJOR_VERSION, SDL_MINOR_VERSION, SDL_PATCHLEVEL,
          PS2_WIDTH * 2, PS2_HEIGHT * 2);
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

void pcPadInit(void)
{
    int i;

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

typedef struct {
    int byte, bit, pressure;           /* report byte/bit (active low), pressure byte or -1 */
    SDL_Scancode key;
    SDL_GameControllerButton button;
} PadMap;

static const PadMap pad_map[] = {
    { 2, 0, -1, SDL_SCANCODE_BACKSPACE, SDL_CONTROLLER_BUTTON_BACK },          /* SELECT   */
    { 2, 1, -1, SDL_SCANCODE_UNKNOWN,   SDL_CONTROLLER_BUTTON_LEFTSTICK },     /* L3       */
    { 2, 2, -1, SDL_SCANCODE_UNKNOWN,   SDL_CONTROLLER_BUTTON_RIGHTSTICK },    /* R3       */
    { 2, 3, -1, SDL_SCANCODE_RETURN,    SDL_CONTROLLER_BUTTON_START },         /* START    */
    { 2, 4, 10, SDL_SCANCODE_UP,        SDL_CONTROLLER_BUTTON_DPAD_UP },       /* UP       */
    { 2, 5,  8, SDL_SCANCODE_RIGHT,     SDL_CONTROLLER_BUTTON_DPAD_RIGHT },    /* RIGHT    */
    { 2, 6, 11, SDL_SCANCODE_DOWN,      SDL_CONTROLLER_BUTTON_DPAD_DOWN },     /* DOWN     */
    { 2, 7,  9, SDL_SCANCODE_LEFT,      SDL_CONTROLLER_BUTTON_DPAD_LEFT },     /* LEFT     */
    { 3, 2, 16, SDL_SCANCODE_A,         SDL_CONTROLLER_BUTTON_LEFTSHOULDER },  /* L1       */
    { 3, 3, 17, SDL_SCANCODE_S,         SDL_CONTROLLER_BUTTON_RIGHTSHOULDER }, /* R1       */
    { 3, 4, 12, SDL_SCANCODE_V,         SDL_CONTROLLER_BUTTON_Y },             /* TRIANGLE */
    { 3, 5, 13, SDL_SCANCODE_C,         SDL_CONTROLLER_BUTTON_B },             /* CIRCLE   */
    { 3, 6, 14, SDL_SCANCODE_Z,         SDL_CONTROLLER_BUTTON_A },             /* CROSS    */
    { 3, 7, 15, SDL_SCANCODE_X,         SDL_CONTROLLER_BUTTON_X },             /* SQUARE   */
};

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
                pcPlatformExeDir(dir, sizeof(dir));
                snprintf(path, sizeof(path), "%s/last_run_input.txt", dir);
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

        for (i = 0; i < sizeof(pad_map) / sizeof(pad_map[0]); i++)
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

    /* hold Numpad 0 to fast-forward */
    {
        extern int pcFastForward;
        static int shown;

        pcFastForward = keys[SDL_SCANCODE_KP_0] != 0;

        if (pcFastForward != shown)
        {
            SDL_SetWindowTitle(window, pcFastForward ? "Resident Evil Code: Veronica X  [FAST FORWARD]" : "Resident Evil Code: Veronica X");
            shown = pcFastForward;
        }
    }

    for (i = 0; i < sizeof(pad_map) / sizeof(pad_map[0]); i++)
    {
        const PadMap* m = &pad_map[i];
        int down = (m->key != SDL_SCANCODE_UNKNOWN) && keys[m->key];

        if (controller != NULL)
        {
            down |= SDL_GameControllerGetButton(controller, m->button);
        }

        if (down)
        {
            r[m->byte] &= ~(1 << m->bit);

            if (m->pressure >= 0)
            {
                r[m->pressure] = 0xFF;
            }
        }
    }

    /* L2 / R2 */
    {
        int l2 = keys[SDL_SCANCODE_Q], r2 = keys[SDL_SCANCODE_W];

        if (controller != NULL)
        {
            l2 |= SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_TRIGGERLEFT) > 8000;
            r2 |= SDL_GameControllerGetAxis(controller, SDL_CONTROLLER_AXIS_TRIGGERRIGHT) > 8000;
        }

        if (l2)
        {
            r[3] &= ~0x01;
            r[18] = 0xFF;
        }

        if (r2)
        {
            r[3] &= ~0x02;
            r[19] = 0xFF;
        }
    }

    /* testing aid: RECVX_INPUT=<file> replays button presses. Each line: "<frame> <button>[+<button>...] [hold]"
     * where <frame> is a frame number (only until the status screen first opens) or "status+<n>"
     * (n frames after the status screen opens, each time);
     * with buttons start select cross circle square triangle up down left right l1 r1 l2 r2;
     * the buttons are held for <hold> frames (default 4) from that frame on. */
    {
        static struct { unsigned int frame, hold; unsigned short mask; int rel; } script[512];
        static unsigned int status_frame = 0xFFFFFFFF;
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

        for (k = 0; k < nscript; k++)
        {
            unsigned int at = script[k].rel ? status_frame + script[k].frame : script[k].frame;

            /* relative entries wait for the status screen; absolute ones stop once it has opened */
            if ((status_frame == 0xFFFFFFFF) == (script[k].rel != 0))
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
