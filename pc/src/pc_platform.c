/* PC port: SDL window, frame presentation, logging and controller input. */
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <io.h>

#include <SDL.h>

#include "pc_platform.h"
#include "pc_os.h"

#define PS2_WIDTH  640
#define PS2_HEIGHT 448

static SDL_Window* window;
static SDL_Renderer* renderer;
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

    window = SDL_CreateWindow("Resident Evil Code: Veronica X", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                              PS2_WIDTH * 2, PS2_HEIGHT * 2, SDL_WINDOW_RESIZABLE);

    if (window == NULL)
    {
        pcPlatformFatal("Could not create the window: %s", SDL_GetError());
    }

    renderer = SDL_CreateRenderer(window, -1, SDL_RENDERER_ACCELERATED);

    if (renderer == NULL)
    {
        renderer = SDL_CreateRenderer(window, -1, 0);
    }

    SDL_RenderSetLogicalSize(renderer, PS2_WIDTH, PS2_HEIGHT);

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
    static unsigned int frames;
    static double last_report;

    if (!pcVideoPumpEvents())
    {
        pcLog("window closed");
        exit(0);
    }

    pcPadUpdate();
    pcOsHeartbeat();

    SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
    SDL_RenderClear(renderer);
    SDL_RenderPresent(renderer);

    frames++;

    if (SDL_GetTicks() - last_report >= 5000)
    {
        pcLog("frame %u", frames);
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

    /* testing aid: RECVX_AUTOPRESS=1 taps START and CROSS every two seconds */
    {
        static int autopress = -1;
        static unsigned int count;

        if (autopress < 0)
        {
            autopress = (SDL_getenv("RECVX_AUTOPRESS") != NULL);
        }

        if (autopress && ((++count % 60) < 3))
        {
            r[2] &= ~0x08;
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
