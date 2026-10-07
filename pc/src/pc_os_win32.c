/* PC port: Windows implementation of pc_os.h */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <timeapi.h>

#include "pc_os.h"

#pragma comment(lib, "winmm.lib")

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
#include <stdio.h>

#pragma comment(lib, "dbghelp.lib")

static HANDLE main_thread;
static volatile LONG heartbeat;
static volatile LONG watchdog_reported;
static CRITICAL_SECTION sym_lock;

void pcLog(const char* fmt, ...);

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

    pcLog("---- %s ----", why);

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
                pcLog("  %s  %s:%lu", sym->Name, line.FileName, line.LineNumber);
            }
            else
            {
                pcLog("  %s+0x%llx", sym->Name, disp);
            }
        }
        else
        {
            pcLog("  0x%08llx", frame.AddrPC.Offset);
        }
    }

    LeaveCriticalSection(&sym_lock);
}

static LONG WINAPI crash_handler(EXCEPTION_POINTERS* ep)
{
    char why[128];

    snprintf(why, sizeof(why), "crash: exception 0x%08lx at 0x%p", ep->ExceptionRecord->ExceptionCode,
             ep->ExceptionRecord->ExceptionAddress);
    print_stack(GetCurrentThread(), ep->ContextRecord, why);

    return EXCEPTION_EXECUTE_HANDLER;
}

static DWORD WINAPI watchdog(LPVOID param)
{
    LONG last = -1;
    int stalled = 0;

    (void)param;

    for (;;)
    {
        Sleep(1000);

        if (heartbeat != last)
        {
            last = heartbeat;
            stalled = 0;
            continue;
        }

        if ((++stalled == 5) && !watchdog_reported)
        {
            CONTEXT ctx;

            watchdog_reported = 1;

            SuspendThread(main_thread);
            memset(&ctx, 0, sizeof(ctx));
            ctx.ContextFlags = CONTEXT_FULL;

            if (GetThreadContext(main_thread, &ctx))
            {
                print_stack(main_thread, &ctx, "watchdog: no frame presented for 5 seconds");
            }

            ResumeThread(main_thread);
        }
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
