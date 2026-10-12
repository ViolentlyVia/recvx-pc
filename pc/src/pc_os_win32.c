/* PC port: Windows implementation of pc_os.h */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <timeapi.h>

#include "pc_os.h"

#pragma comment(lib, "winmm.lib")
#pragma comment(lib, "user32.lib")

typedef struct {
    void (*entry)(void*);
    void* arg;
} FiberStart;

static void* main_fiber;

void* pcOsFiberFromThread(void)
{
    if (main_fiber == NULL)
    {
        main_fiber = ConvertThreadToFiber(NULL);
    }

    return main_fiber;
}

static void CALLBACK fiber_proc(void* param)
{
    FiberStart start = *(FiberStart*)param;

    HeapFree(GetProcessHeap(), 0, param);
    start.entry(start.arg);
}

void* pcOsFiberCreate(unsigned int stack_size, void (*entry)(void*), void* arg)
{
    FiberStart* start = HeapAlloc(GetProcessHeap(), 0, sizeof(FiberStart));

    start->entry = entry;
    start->arg = arg;

    return CreateFiber(stack_size, fiber_proc, start);
}

void pcOsFiberSwitch(void* fiber)
{
    SwitchToFiber(fiber);
}

void pcOsFiberDelete(void* fiber)
{
    DeleteFiber(fiber);
}

/* The PS2 scratchpad (16 KB at 0x70000000) is addressed with literal constants by the game
 * (WORKBASE, DMA tags), so it is placed at that very address. */
void* pcOsAllocScratchpad(void)
{
    void* p = VirtualAlloc((void*)0x70000000, 0x4000, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);

    return p;
}

double pcOsTime(void)
{
    static LARGE_INTEGER freq;
    LARGE_INTEGER now;

    if (freq.QuadPart == 0)
    {
        QueryPerformanceFrequency(&freq);
        timeBeginPeriod(1);
    }

    QueryPerformanceCounter(&now);

    return (double)now.QuadPart / (double)freq.QuadPart;
}

void pcOsSleep(double seconds)
{
    double end = pcOsTime() + seconds;

    /* coarse sleep, then spin for the last millisecond for accurate frame pacing */
    if (seconds > 0.002)
    {
        Sleep((DWORD)((seconds - 0.0015) * 1000.0));
    }

    while (pcOsTime() < end)
    {
        YieldProcessor();
    }
}

/* ------------------------------------------------------------------------------------------- */
/* diagnostics: stack traces on crashes and when the game stops presenting frames */

#include <dbghelp.h>
#include <stdarg.h>
#include <stdio.h>

#pragma comment(lib, "dbghelp.lib")

static HANDLE main_thread;
static volatile LONG heartbeat;
static volatile LONG watchdog_reported;
static CRITICAL_SECTION sym_lock;

void pcLog(const char* fmt, ...);
void pcLogFlush(void);

/* the watchdog walks the main thread's stack while that thread is suspended: it may hold the
 * heap or the log's lock then, so the lines are collected here and logged after it resumes */
static char stack_text[8192];
static size_t stack_len;
static int stack_collect;

static void stack_line(const char* fmt, ...)
{
    va_list ap;
    char line[512];

    va_start(ap, fmt);
    vsnprintf(line, sizeof(line), fmt, ap);
    va_end(ap);

    if (!stack_collect)
    {
        pcLog("%s", line);
    }
    else if (stack_len + strlen(line) + 2 < sizeof(stack_text))
    {
        stack_len += (size_t)snprintf(stack_text + stack_len, sizeof(stack_text) - stack_len, "%s\n", line);
    }
}

static void print_stack(HANDLE thread, CONTEXT* ctx, const char* why)
{
    STACKFRAME64 frame;
    HANDLE process = GetCurrentProcess();
    int depth;

    EnterCriticalSection(&sym_lock);

    memset(&frame, 0, sizeof(frame));
    frame.AddrPC.Offset = ctx->Eip;
    frame.AddrPC.Mode = AddrModeFlat;
    frame.AddrFrame.Offset = ctx->Ebp;
    frame.AddrFrame.Mode = AddrModeFlat;
    frame.AddrStack.Offset = ctx->Esp;
    frame.AddrStack.Mode = AddrModeFlat;

    stack_line("---- %s ----", why);

    for (depth = 0; depth < 40; depth++)
    {
        char buf[sizeof(SYMBOL_INFO) + 256];
        SYMBOL_INFO* sym = (SYMBOL_INFO*)buf;
        IMAGEHLP_LINE64 line;
        DWORD64 disp = 0;
        DWORD ldisp = 0;

        if (!StackWalk64(IMAGE_FILE_MACHINE_I386, process, thread, &frame, ctx, NULL,
                         SymFunctionTableAccess64, SymGetModuleBase64, NULL) || (frame.AddrPC.Offset == 0))
        {
            break;
        }

        sym->SizeOfStruct = sizeof(SYMBOL_INFO);
        sym->MaxNameLen = 255;
        line.SizeOfStruct = sizeof(line);

        if (SymFromAddr(process, frame.AddrPC.Offset, &disp, sym))
        {
            if (SymGetLineFromAddr64(process, frame.AddrPC.Offset, &ldisp, &line))
            {
                stack_line("  %s  %s:%lu", sym->Name, line.FileName, line.LineNumber);
            }
            else
            {
                stack_line("  %s+0x%llx", sym->Name, disp);
            }
        }
        else
        {
            stack_line("  0x%08llx", frame.AddrPC.Offset);
        }
    }

    LeaveCriticalSection(&sym_lock);
}

static LONG WINAPI crash_handler(EXCEPTION_POINTERS* ep)
{
    char why[128];

    snprintf(why, sizeof(why), "crash: exception 0x%08lx at 0x%p", ep->ExceptionRecord->ExceptionCode,
             ep->ExceptionRecord->ExceptionAddress);
    /* the stack walk updates the context it is given: walk a copy, keep the registers intact */
    {
        CONTEXT walk = *ep->ContextRecord;
        const CONTEXT* c = ep->ContextRecord;

        print_stack(GetCurrentThread(), &walk, why);

        /* registers and, for access violations, the faulting address: names the bad pointer */
        pcLog("  eax %08lx ebx %08lx ecx %08lx edx %08lx esi %08lx edi %08lx ebp %08lx esp %08lx",
              c->Eax, c->Ebx, c->Ecx, c->Edx, c->Esi, c->Edi, c->Ebp, c->Esp);

        if ((ep->ExceptionRecord->ExceptionCode == EXCEPTION_ACCESS_VIOLATION) &&
            (ep->ExceptionRecord->NumberParameters >= 2))
        {
            pcLog("  %s address 0x%08lx", (ep->ExceptionRecord->ExceptionInformation[0] == 1) ? "writing" : "reading",
                  (unsigned long)ep->ExceptionRecord->ExceptionInformation[1]);
        }
    }

    pcLogFlush();

    /* tell the player instead of just vanishing (scripted test runs stay unattended) */
    if ((getenv("RECVX_INPUT") == NULL) && (getenv("RECVX_REPLAY") == NULL))
    {
        MessageBoxA(NULL, "The game stopped because of an error.\n\n"
                          "Details were saved to recvx.log next to recvx.exe. If you report the problem, "
                          "please include recvx.log and last_run_input.txt (a recording of this session).",
                    "Resident Evil Code: Veronica X", MB_ICONERROR | MB_OK | MB_TOPMOST | MB_SETFOREGROUND);
    }

    return EXCEPTION_EXECUTE_HANDLER;
}

static DWORD WINAPI watchdog(LPVOID param)
{
    LONG last = -1;
    int stalled = 0;

    (void)param;

    for (;;)
    {
        Sleep(250);

        if (heartbeat != last)
        {
            last = heartbeat;
            stalled = 0;
            continue;
        }

        /* checked 4 times a second: a stall of 2 seconds logs the main thread's stack */
        if ((++stalled == 8) && !watchdog_reported)
        {
            CONTEXT ctx;

            watchdog_reported = 1;

            stack_len = 0;
            stack_text[0] = 0;
            stack_collect = 1;
            SuspendThread(main_thread);
            memset(&ctx, 0, sizeof(ctx));
            ctx.ContextFlags = CONTEXT_FULL;

            if (GetThreadContext(main_thread, &ctx))
            {
                print_stack(main_thread, &ctx, "watchdog: no frame presented for 2 seconds");
            }

            ResumeThread(main_thread);
            stack_collect = 0;

            if (stack_len != 0)
            {
                stack_text[stack_len - 1] = 0;   /* pcLog adds the last line break */
                pcLog("%s", stack_text);
            }
        }
    }
}

/* recvx.exe is a Windows (not console) program: the log goes to recvx.log, and a console
 * window for it only opens when asked for */
void pcOsOpenConsole(void)
{
    if (AllocConsole())
    {
        freopen("CONOUT$", "w", stdout);
        freopen("CONOUT$", "w", stderr);
        SetConsoleTitleA("recvx log");
    }
}

int pcOsLaunch(const char* exe_path)
{
    char cmd[MAX_PATH + 4], dir[MAX_PATH];
    STARTUPINFOA si;
    PROCESS_INFORMATION pi;
    char* slash;

    snprintf(cmd, sizeof(cmd), "\"%s\"", exe_path);
    snprintf(dir, sizeof(dir), "%s", exe_path);

    if ((slash = strrchr(dir, '\\')) != NULL || (slash = strrchr(dir, '/')) != NULL)
    {
        *slash = 0;
    }

    memset(&si, 0, sizeof(si));
    si.cb = sizeof(si);

    if (!CreateProcessA(NULL, cmd, NULL, NULL, FALSE, 0, NULL, dir, &si, &pi))
    {
        return 0;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 1;
}

void pcOsSetWindowIcon(void* hwnd)
{
    HICON big = (HICON)LoadImageA(GetModuleHandleA(NULL), MAKEINTRESOURCEA(1), IMAGE_ICON,
                                  GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), 0);
    HICON small = (HICON)LoadImageA(GetModuleHandleA(NULL), MAKEINTRESOURCEA(1), IMAGE_ICON,
                                    GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), 0);

    if (big != NULL)
    {
        SendMessageA((HWND)hwnd, WM_SETICON, ICON_BIG, (LPARAM)big);
    }

    if (small != NULL)
    {
        SendMessageA((HWND)hwnd, WM_SETICON, ICON_SMALL, (LPARAM)small);
    }
}

void pcOsInstallDiagnostics(void)
{
    if (main_thread != NULL)
    {
        return;
    }

    InitializeCriticalSection(&sym_lock);
    SymSetOptions(SYMOPT_LOAD_LINES | SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS);
    SymInitialize(GetCurrentProcess(), NULL, TRUE);

    DuplicateHandle(GetCurrentProcess(), GetCurrentThread(), GetCurrentProcess(), &main_thread, 0, FALSE,
                    DUPLICATE_SAME_ACCESS);

    SetUnhandledExceptionFilter(crash_handler);
    CloseHandle(CreateThread(NULL, 0, watchdog, NULL, 0, NULL));
}

void pcOsHeartbeat(void)
{
    InterlockedIncrement(&heartbeat);
    watchdog_reported = 0;
}

/* debug: log the current call stack */
void pcOsLogStack(const char* why)
{
    CONTEXT ctx;

    RtlCaptureContext(&ctx);
    print_stack(GetCurrentThread(), &ctx, why);
}
