/* PC port: Linux (POSIX) implementation of pc_os.h (built with _GNU_SOURCE) */
#include <execinfo.h>
#include <link.h>
#include <pthread.h>
#include <signal.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/wait.h>
#include <time.h>
#include <ucontext.h>
#include <unistd.h>

#include <SDL.h>

#include "pc_os.h"

void pcLog(const char* fmt, ...);
void pcLogFlush(void);

/* ------------------------------------------------------------------------------------------- */
/* string literals: the game writes into some (gdFsInit uppercases file names in place), which
 * MSVC allows with /GF-. GCC keeps them in read-only pages, so those pages are made writable
 * before main runs. */

static int writable_segment(struct dl_phdr_info* info, size_t size, void* data)
{
    long page = sysconf(_SC_PAGESIZE);
    int i;

    (void)size;
    (void)data;

    /* the first object is the program itself */
    for (i = 0; i < info->dlpi_phnum; i++)
    {
        const ElfW(Phdr)* ph = &info->dlpi_phdr[i];
        uintptr_t start, end;

        if ((ph->p_type != PT_LOAD) || (ph->p_flags & PF_W))
        {
            continue;
        }

        start = (info->dlpi_addr + ph->p_vaddr) & ~(uintptr_t)(page - 1);
        end = (info->dlpi_addr + ph->p_vaddr + ph->p_memsz + page - 1) & ~(uintptr_t)(page - 1);
        mprotect((void*)start, end - start, PROT_READ | PROT_WRITE | ((ph->p_flags & PF_X) ? PROT_EXEC : 0));
    }

    return 1;
}

__attribute__((constructor)) static void writable_constants(void)
{
    dl_iterate_phdr(writable_segment, NULL);
}

/* ------------------------------------------------------------------------------------------- */
/* fibers on ucontext */

typedef struct {
    ucontext_t ctx;
    void* stack;
    size_t stack_size;
    void (*entry)(void*);
    void* arg;
} Fiber;

static Fiber main_fiber_data;
static Fiber* main_fiber;
static Fiber* current;

void* pcOsFiberFromThread(void)
{
    if (main_fiber == NULL)
    {
        main_fiber = &main_fiber_data;
        current = main_fiber;
    }

    return main_fiber;
}

static void fiber_proc(void)
{
    current->entry(current->arg);
    /* a fiber's entry never returns (as on Windows, where that ends the thread) */
    abort();
}

void* pcOsFiberCreate(unsigned int stack_size, void (*entry)(void*), void* arg)
{
    Fiber* f = calloc(1, sizeof(Fiber));

    if (f == NULL)
    {
        return NULL;
    }

    if (stack_size < 0x10000)
    {
        stack_size = 0x10000;
    }

    f->stack_size = stack_size;
    f->stack = mmap(NULL, stack_size, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_STACK, -1, 0);

    if (f->stack == MAP_FAILED)
    {
        free(f);
        return NULL;
    }

    f->entry = entry;
    f->arg = arg;
    getcontext(&f->ctx);
    f->ctx.uc_stack.ss_sp = f->stack;
    f->ctx.uc_stack.ss_size = stack_size;
    f->ctx.uc_link = NULL;
    makecontext(&f->ctx, fiber_proc, 0);

    return f;
}

void pcOsFiberSwitch(void* fiber)
{
    Fiber* from = current;

    if ((Fiber*)fiber == from)
    {
        return;
    }

    current = fiber;
    swapcontext(&from->ctx, &current->ctx);
}

void pcOsFiberDelete(void* fiber)
{
    Fiber* f = fiber;

    if ((f == NULL) || (f == main_fiber) || (f == current))
    {
        return;
    }

    munmap(f->stack, f->stack_size);
    free(f);
}

/* The PS2 scratchpad (16 KB at 0x70000000) is addressed with literal constants by the game
 * (WORKBASE, DMA tags), so it is placed at that very address. */
void* pcOsAllocScratchpad(void)
{
    void* want = (void*)0x70000000;
    void* p = mmap(want, 0x4000, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED_NOREPLACE, -1, 0);

    if (p == MAP_FAILED)
    {
        return NULL;
    }

    if (p != want)
    {
        munmap(p, 0x4000);
        return NULL;
    }

    return p;
}

double pcOsTime(void)
{
    struct timespec ts;

    clock_gettime(CLOCK_MONOTONIC, &ts);

    return (double)ts.tv_sec + (double)ts.tv_nsec * 1e-9;
}

void pcOsSleep(double seconds)
{
    double end = pcOsTime() + seconds;

    /* coarse sleep, then spin for the last millisecond for accurate frame pacing */
    if (seconds > 0.002)
    {
        double coarse = seconds - 0.0015;
        struct timespec ts;

        ts.tv_sec = (time_t)coarse;
        ts.tv_nsec = (long)((coarse - (double)ts.tv_sec) * 1e9);
        nanosleep(&ts, NULL);
    }

    while (pcOsTime() < end)
    {
        __builtin_ia32_pause();
    }
}

/* ------------------------------------------------------------------------------------------- */
/* diagnostics: stack traces on crashes and when the game stops presenting frames */

static pthread_t main_thread;
static int diagnostics_on;
static volatile int heartbeat;
static volatile int watchdog_reported;

/* the watchdog asks the main thread for its stack (SIGUSR2); the handler only fills these, the
 * watchdog logs them (the main thread may hold the heap or the log's lock then) */
static void* stall_frames[40];
static volatile int stall_depth = -1;

static void print_frames(void* const* frames, int depth, const char* why)
{
    char** names = backtrace_symbols(frames, depth);
    int i;

    pcLog("---- %s ----", why);

    for (i = 0; i < depth; i++)
    {
        if (names != NULL)
        {
            pcLog("  %s", names[i]);
        }
        else
        {
            pcLog("  %p", frames[i]);
        }
    }

    free(names);
}

static void crash_handler(int sig, siginfo_t* si, void* uctx)
{
    void* frames[40];
    int depth = backtrace(frames, 40);
    char why[128];

    (void)uctx;

    snprintf(why, sizeof(why), "crash: signal %d (%s), address %p", sig, strsignal(sig), si->si_addr);
    print_frames(frames, depth, why);
    pcLogFlush();

    /* tell the player instead of just vanishing (scripted test runs stay unattended) */
    if ((getenv("RECVX_INPUT") == NULL) && (getenv("RECVX_REPLAY") == NULL))
    {
        SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, "Resident Evil Code: Veronica X",
                                 "The game stopped because of an error.\n\n"
                                 "Details were saved to recvx.log next to recvx. If you report the problem, "
                                 "please include recvx.log and last_run_input.txt (a recording of this session).",
                                 NULL);
    }

    signal(sig, SIG_DFL);
    raise(sig);
}

static void stall_handler(int sig)
{
    (void)sig;
    stall_depth = backtrace(stall_frames, 40);
}

static void* watchdog(void* param)
{
    int last = -1;
    int stalled = 0;

    (void)param;

    for (;;)
    {
        struct timespec ts = { 0, 250000000 };

        nanosleep(&ts, NULL);

        if (heartbeat != last)
        {
            last = heartbeat;
            stalled = 0;
            continue;
        }

        /* checked 4 times a second: a stall of 2 seconds logs the main thread's stack */
        if ((++stalled == 8) && !watchdog_reported)
        {
            int wait;

            watchdog_reported = 1;
            stall_depth = -1;
            pthread_kill(main_thread, SIGUSR2);

            for (wait = 0; (wait < 100) && (stall_depth < 0); wait++)
            {
                struct timespec ms = { 0, 1000000 };

                nanosleep(&ms, NULL);
            }

            if (stall_depth > 0)
            {
                print_frames(stall_frames, stall_depth, "watchdog: no frame presented for 2 seconds");
            }
        }
    }

    return NULL;
}

/* the log already goes to the terminal recvx was started from */
void pcOsOpenConsole(void)
{
}

int pcOsLaunch(const char* exe_path)
{
    char dir[1024];
    char* slash;
    pid_t pid;

    snprintf(dir, sizeof(dir), "%s", exe_path);

    if ((slash = strrchr(dir, '/')) != NULL)
    {
        *slash = 0;
    }

    if (access(exe_path, X_OK) != 0)
    {
        return 0;
    }

    pid = fork();

    if (pid < 0)
    {
        return 0;
    }

    if (pid == 0)
    {
        /* detached: a grandchild, so nobody has to wait for it */
        if (fork() == 0)
        {
            if (slash != NULL)
            {
                chdir(dir);
            }

            execl(exe_path, exe_path, (char*)NULL);
        }

        _exit(0);
    }

    waitpid(pid, NULL, 0);
    return 1;
}

/* the window icon is set through SDL on Linux (pcPlatform) */
void pcOsSetWindowIcon(void* hwnd)
{
    (void)hwnd;
}

void pcOsInstallDiagnostics(void)
{
    struct sigaction sa;
    pthread_t thread;
    static const int crashes[] = { SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGABRT };
    int i;

    if (diagnostics_on)
    {
        return;
    }

    diagnostics_on = 1;
    main_thread = pthread_self();

    memset(&sa, 0, sizeof(sa));
    sa.sa_sigaction = crash_handler;
    sa.sa_flags = SA_SIGINFO | SA_ONSTACK;
    sigemptyset(&sa.sa_mask);

    for (i = 0; i < (int)(sizeof(crashes) / sizeof(crashes[0])); i++)
    {
        sigaction(crashes[i], &sa, NULL);
    }

    memset(&sa, 0, sizeof(sa));
    sa.sa_handler = stall_handler;
    sa.sa_flags = SA_RESTART;
    sigemptyset(&sa.sa_mask);
    sigaction(SIGUSR2, &sa, NULL);

    if (pthread_create(&thread, NULL, watchdog, NULL) == 0)
    {
        pthread_detach(thread);
    }
}

void pcOsHeartbeat(void)
{
    __sync_fetch_and_add(&heartbeat, 1);
    watchdog_reported = 0;
}

/* debug: log the current call stack */
void pcOsLogStack(const char* why)
{
    void* frames[40];
    int depth = backtrace(frames, 40);

    print_frames(frames, depth, why);
}
