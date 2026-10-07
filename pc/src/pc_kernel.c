/* PC port: emulation of the PS2 EE kernel services the game uses.
 *
 * EE threads are strictly priority scheduled (lower number = higher priority) and only switch
 * when the running thread blocks, changes priorities, or an interrupt handler wakes something
 * up. That maps well onto Windows fibers on a single OS thread:
 *
 *  - every EE thread is a fiber; pcKernelReschedule() switches to the highest-priority ready
 *    thread (FIFO among equal priorities);
 *  - "interrupts" (VBLANK and alarms) are generated from the real clock and run whenever the
 *    kernel is entered (any call here, pcKernelBusyWait(), or when every thread is blocked);
 *  - i*-functions only mark threads ready; the switch happens when the handlers are done.
 *
 * Loops that busy-wait for an interrupt on the PS2 must call pcKernelBusyWait() on PC.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "sce_pc.h"
#include "pc_os.h"
#include "pc_kernel.h"

#define MAX_THREADS 64
#define MAX_SEMAS   128
#define MAX_ALARMS  64
#define MAX_INTC    8

#define THS_RUN         0x01
#define THS_READY       0x02
#define THS_WAIT        0x04
#define THS_SUSPEND     0x08
#define THS_WAITSUSPEND 0x0C
#define THS_DORMANT     0x10

#define WAIT_NONE  0
#define WAIT_SLEEP 1
#define WAIT_SEMA  2

#define HSYNC_HZ  15734.0      /* NTSC */
#define VBLANK_HZ 59.94

typedef struct {
    int used;
    int dormant;
    int waiting;          /* WAIT_* */
    int wait_id;
    int suspended;
    int prio;
    int init_prio;
    int wakeup_count;
    unsigned int seq;     /* FIFO order within a priority */
    void (*entry)(void*);
    void* arg;
    void* fiber;
    int stack_size;
} PcThread;

typedef struct {
    int used;
    int count;
    int max;
    int init;
    u_int attr;
    u_int option;
} PcSema;

typedef struct {
    int used;
    double due;
    unsigned short time;
    void (*handler)(int, unsigned short, void*);
    void* arg;
} PcAlarm;

typedef struct {
    int (*handler)(int);
    int next;
} PcIntc;

static PcThread threads[MAX_THREADS];   /* index = thread id, 0 unused */
static PcSema   semas[MAX_SEMAS];
static PcAlarm  alarms[MAX_ALARMS];
static PcIntc   intc_handlers[16][MAX_INTC];
static int      intc_enabled[16];

static int (*gs_vsync_callback)(int);

static int      cur_tid;
static unsigned int seq_counter;
static int      in_interrupt;
static int      initialized;
static double   next_vblank;
static unsigned int vblank_count;

static void kernel_init(void)
{
    if (initialized)
    {
        return;
    }

    initialized = 1;

    /* thread 1 is the main thread; its default priority on the EE is 1 */
    threads[1].used = 1;
    threads[1].prio = threads[1].init_prio = 1;
    threads[1].seq = ++seq_counter;
    threads[1].fiber = pcOsFiberFromThread();
    cur_tid = 1;

    next_vblank = pcOsTime() + 1.0 / VBLANK_HZ;
}

static int is_ready(const PcThread* t)
{
    return t->used && !t->dormant && t->waiting == WAIT_NONE && !t->suspended;
}

static int pick_next(void)
{
    int i, best = 0;

    for (i = 1; i < MAX_THREADS; i++)
    {
        PcThread* t = &threads[i];

        if (!is_ready(t))
        {
            continue;
        }

        if ((best == 0) || (t->prio < threads[best].prio) ||
            ((t->prio == threads[best].prio) && (t->seq < threads[best].seq)))
        {
            best = i;
        }
    }

    return best;
}

/* Run due interrupt handlers. Returns nonzero if any fired. */
static int run_interrupts(void)
{
    double now = pcOsTime();
    int fired = 0;
    int i, k;

    if (in_interrupt)
    {
        return 0;
    }

    in_interrupt = 1;

    /* VBLANK start (INTC cause 2); never let a long stall replay hundreds of fields */
    if (now - next_vblank > 0.25)
    {
        next_vblank = now;
    }

    while (now >= next_vblank)
    {
        next_vblank += 1.0 / VBLANK_HZ;
        vblank_count++;

        if (intc_enabled[2])
        {
            for (k = 0; k < MAX_INTC; k++)
            {
                if (intc_handlers[2][k].handler != NULL)
                {
                    intc_handlers[2][k].handler(2);
                }
            }
        }

        if (gs_vsync_callback != NULL)
        {
            gs_vsync_callback(0);
        }

        fired = 1;
    }

    for (i = 0; i < MAX_ALARMS; i++)
    {
        PcAlarm* a = &alarms[i];

        if (a->used && (now >= a->due))
        {
            a->used = 0;
            a->handler(i + 1, a->time, a->arg);
            fired = 1;
        }
    }

    in_interrupt = 0;

    return fired;
}

static double next_event_time(void)
{
    double t = next_vblank;
    int i;

    for (i = 0; i < MAX_ALARMS; i++)
    {
        if (alarms[i].used && (alarms[i].due < t))
        {
            t = alarms[i].due;
        }
    }

    return t;
}

static void switch_to(int tid)
{
    PcThread* from = &threads[cur_tid];

    if (tid == cur_tid)
    {
        return;
    }

    cur_tid = tid;
    pcOsFiberSwitch(threads[tid].fiber);

    (void)from;
}

/* Switch to the best ready thread; if nothing can run, wait for interrupts. */
void pcKernelReschedule(void)
{
    int next;

    kernel_init();

    if (in_interrupt)
    {
        return;
    }

    for (;;)
    {
        run_interrupts();

        next = pick_next();

        if (next != 0)
        {
            switch_to(next);
            return;
        }

        /* every thread is blocked: sleep until the next interrupt */
        pcOsSleep(next_event_time() - pcOsTime());
    }
}

/* Preempt the running thread if an interrupt made a higher-priority thread ready. */
static void check_preempt(void)
{
    int next;

    if (in_interrupt)
    {
        return;
    }

    run_interrupts();

    next = pick_next();

    if ((next != 0) && (next != cur_tid))
    {
        if (!is_ready(&threads[cur_tid]) || (threads[next].prio < threads[cur_tid].prio))
        {
            switch_to(next);
        }
    }
}

void pcKernelPoll(void)
{
    kernel_init();
    check_preempt();
}

void pcKernelBusyWait(void)
{
    kernel_init();

    if (!run_interrupts())
    {
        pcOsSleep(next_event_time() - pcOsTime());
        run_interrupts();
    }

    check_preempt();
}

unsigned int pcKernelVblankCount(void)
{
    return vblank_count;
}

/* ------------------------------------------------------------------------------------------- */
/* threads */

static void thread_entry(void* p)
{
    int tid = (int)(size_t)p;

    threads[tid].entry(threads[tid].arg);

    /* returning from the entry function is ExitThread() */
    threads[tid].dormant = 1;
    pcKernelReschedule();

    /* never resumed: a dormant thread gets a new fiber on StartThread */
    for (;;)
    {
        pcKernelReschedule();
    }
}

int CreateThread(struct ThreadParam* param)
{
    int i;

    kernel_init();

    for (i = 2; i < MAX_THREADS; i++)
    {
        if (!threads[i].used)
        {
            memset(&threads[i], 0, sizeof(PcThread));

            threads[i].used = 1;
            threads[i].dormant = 1;
            threads[i].prio = threads[i].init_prio = param->initPriority;
            threads[i].entry = param->entry;
            threads[i].stack_size = param->stackSize;

            return i;
        }
    }

    return -1;
}

int StartThread(int thid, void* arg)
{
    PcThread* t = &threads[thid];
    unsigned int stack = (t->stack_size < 0x40000) ? 0x40000 : (unsigned int)t->stack_size;

    kernel_init();

    if (!t->used || !t->dormant)
    {
        return -1;
    }

    if (t->fiber != NULL)
    {
        pcOsFiberDelete(t->fiber);
    }

    t->arg = arg;
    t->fiber = pcOsFiberCreate(stack, thread_entry, (void*)(size_t)thid);
    t->dormant = 0;
    t->waiting = WAIT_NONE;
    t->wakeup_count = 0;
    t->prio = t->init_prio;
    t->seq = ++seq_counter;

    check_preempt();

    return thid;
}

int DeleteThread(int thid)
{
    PcThread* t = &threads[thid];

    if (!t->used || !t->dormant || (thid == cur_tid))
    {
        return -1;
    }

    if (t->fiber != NULL)
    {
        pcOsFiberDelete(t->fiber);
    }

    memset(t, 0, sizeof(PcThread));

    return thid;
}

int TerminateThread(int thid)
{
    if ((thid == cur_tid) || !threads[thid].used)
    {
        return -1;
    }

    threads[thid].dormant = 1;

    return thid;
}

int GetThreadId(void)
{
    kernel_init();
    return cur_tid;
}

int ReferThreadStatus(int thid, struct ThreadParam* info)
{
    PcThread* t;

    kernel_init();

    if (thid == 0)
    {
        thid = cur_tid;
    }

    t = &threads[thid];

    if (!t->used)
    {
        return -1;
    }

    memset(info, 0, sizeof(*info));

    if (t->dormant)
    {
        info->status = THS_DORMANT;
    }
    else if (thid == cur_tid)
    {
        info->status = THS_RUN;
    }
    else if (t->waiting != WAIT_NONE)
    {
        info->status = t->suspended ? THS_WAITSUSPEND : THS_WAIT;
    }
    else
    {
        info->status = t->suspended ? THS_SUSPEND : THS_READY;
    }

    info->entry = t->entry;
    info->stackSize = t->stack_size;
    info->initPriority = t->init_prio;
    info->currentPriority = t->prio;
    info->waitType = t->waiting;
    info->waitId = t->wait_id;
    info->wakeupCount = t->wakeup_count;

    return info->status;
}

int ChangeThreadPriority(int thid, int prio)
{
    kernel_init();

    if (thid == 0)
    {
        thid = cur_tid;
    }

    if (!threads[thid].used)
    {
        return -1;
    }

    threads[thid].prio = prio;
    threads[thid].seq = ++seq_counter;

    if (!in_interrupt)
    {
        pcKernelReschedule();
    }

    return 0;
}

int RotateThreadReadyQueue(int prio)
{
    kernel_init();

    if (threads[cur_tid].prio == prio)
    {
        threads[cur_tid].seq = ++seq_counter;
    }

    if (!in_interrupt)
    {
        pcKernelReschedule();
    }

    return 0;
}

int SleepThread(void)
{
    PcThread* t;

    kernel_init();

    t = &threads[cur_tid];

    if (t->wakeup_count > 0)
    {
        t->wakeup_count--;
        return cur_tid;
    }

    t->waiting = WAIT_SLEEP;
    pcKernelReschedule();

    return cur_tid;
}

static int wakeup(int thid)
{
    PcThread* t = &threads[thid];

    if (!t->used || t->dormant)
    {
        return -1;
    }

    if (t->waiting == WAIT_SLEEP)
    {
        t->waiting = WAIT_NONE;
        t->seq = ++seq_counter;
    }
    else
    {
        t->wakeup_count++;
    }

    return thid;
}

int WakeupThread(int thid)
{
    int r;

    kernel_init();

    r = wakeup(thid);
    check_preempt();

    return r;
}

int iWakeupThread(int thid)
{
    return wakeup(thid);
}

int SuspendThread(int thid)
{
    kernel_init();

    if (!threads[thid].used || threads[thid].dormant)
    {
        return -1;
    }

    threads[thid].suspended = 1;

    if (thid == cur_tid)
    {
        pcKernelReschedule();
    }

    return thid;
}

int ResumeThread(int thid)
{
    kernel_init();

    if (!threads[thid].used || !threads[thid].suspended)
    {
        return -1;
    }

    threads[thid].suspended = 0;
    threads[thid].seq = ++seq_counter;

    check_preempt();

    return thid;
}

/* ------------------------------------------------------------------------------------------- */
/* semaphores */

int CreateSema(struct SemaParam* param)
{
    int i;

    for (i = 1; i < MAX_SEMAS; i++)
    {
        if (!semas[i].used)
        {
            semas[i].used = 1;
            semas[i].count = param->initCount;
            semas[i].init = param->initCount;
            semas[i].max = param->maxCount;
            semas[i].attr = param->attr;
            semas[i].option = param->option;

            return i;
        }
    }

    return -1;
}

int DeleteSema(int semid)
{
    int i;

    if (!semas[semid].used)
    {
        return -1;
    }

    /* release anything still waiting */
    for (i = 1; i < MAX_THREADS; i++)
    {
        if (threads[i].used && (threads[i].waiting == WAIT_SEMA) && (threads[i].wait_id == semid))
        {
            threads[i].waiting = WAIT_NONE;
        }
    }

    semas[semid].used = 0;

    return semid;
}

static int signal_sema(int semid)
{
    int i, best = 0;

    if (!semas[semid].used)
    {
        return -1;
    }

    for (i = 1; i < MAX_THREADS; i++)
    {
        PcThread* t = &threads[i];

        if (t->used && (t->waiting == WAIT_SEMA) && (t->wait_id == semid))
        {
            if ((best == 0) || (t->prio < threads[best].prio) ||
                ((t->prio == threads[best].prio) && (t->seq < threads[best].seq)))
            {
                best = i;
            }
        }
    }

    if (best != 0)
    {
        threads[best].waiting = WAIT_NONE;
        threads[best].seq = ++seq_counter;
    }
    else if (semas[semid].count < semas[semid].max)
    {
        semas[semid].count++;
    }

    return semid;
}

int SignalSema(int semid)
{
    int r;

    kernel_init();

    r = signal_sema(semid);
    check_preempt();

    return r;
}

int iSignalSema(int semid)
{
    return signal_sema(semid);
}

int WaitSema(int semid)
{
    kernel_init();

    if (!semas[semid].used)
    {
        return -1;
    }

    if (semas[semid].count > 0)
    {
        semas[semid].count--;
        return semid;
    }

    threads[cur_tid].waiting = WAIT_SEMA;
    threads[cur_tid].wait_id = semid;
    threads[cur_tid].seq = ++seq_counter;

    pcKernelReschedule();

    return semid;
}

int PollSema(int semid)
{
    kernel_init();

    if (!semas[semid].used || (semas[semid].count <= 0))
    {
        return -1;
    }

    semas[semid].count--;

    return semid;
}

int ReferSemaStatus(int semid, struct SemaParam* info)
{
    int i, waiting = 0;

    if (!semas[semid].used)
    {
        return -1;
    }

    for (i = 1; i < MAX_THREADS; i++)
    {
        if (threads[i].used && (threads[i].waiting == WAIT_SEMA) && (threads[i].wait_id == semid))
        {
            waiting++;
        }
    }

    info->currentCount = semas[semid].count;
    info->maxCount = semas[semid].max;
    info->initCount = semas[semid].init;
    info->numWaitThreads = waiting;
    info->attr = semas[semid].attr;
    info->option = semas[semid].option;

    return semid;
}

/* ------------------------------------------------------------------------------------------- */
/* alarms and interrupt handlers */

int iSetAlarm(unsigned short time, void (*handler)(int, unsigned short, void*), void* arg)
{
    int i;

    kernel_init();

    for (i = 0; i < MAX_ALARMS; i++)
    {
        if (!alarms[i].used)
        {
            alarms[i].used = 1;
            alarms[i].due = pcOsTime() + time / HSYNC_HZ;
            alarms[i].time = time;
            alarms[i].handler = handler;
            alarms[i].arg = arg;

            return i + 1;
        }
    }

    return -1;
}

int SetAlarm(unsigned short time, void (*handler)(int, unsigned short, void*), void* arg)
{
    return iSetAlarm(time, handler, arg);
}

int iReleaseAlarm(int alarm_id)
{
    if ((alarm_id < 1) || (alarm_id > MAX_ALARMS) || !alarms[alarm_id - 1].used)
    {
        return -1;
    }

    alarms[alarm_id - 1].used = 0;

    return alarm_id;
}

int ReleaseAlarm(int alarm_id)
{
    return iReleaseAlarm(alarm_id);
}

int AddIntcHandler(int cause, int (*handler)(int), int next)
{
    int k;

    kernel_init();

    for (k = 0; k < MAX_INTC; k++)
    {
        if (intc_handlers[cause & 15][k].handler == NULL)
        {
            intc_handlers[cause & 15][k].handler = handler;
            intc_handlers[cause & 15][k].next = next;

            return k + 1;
        }
    }

    return -1;
}

int RemoveIntcHandler(int cause, int hid)
{
    if ((hid < 1) || (hid > MAX_INTC))
    {
        return -1;
    }

    intc_handlers[cause & 15][hid - 1].handler = NULL;

    return 0;
}

int EnableIntc(int cause)
{
    int old = intc_enabled[cause & 15];

    intc_enabled[cause & 15] = 1;

    return !old;
}

int DisableIntc(int cause)
{
    int old = intc_enabled[cause & 15];

    intc_enabled[cause & 15] = 0;

    return old;
}

int ExitHandler(void)
{
    return 0;
}

int DIntr(void)
{
    return 1;
}

int EIntr(void)
{
    return 1;
}

int sceGsSyncVCallback(int (*func)(int))
{
    int (*old)(int) = gs_vsync_callback;

    gs_vsync_callback = func;

    return (int)(size_t)old;
}

/* Wait for the start of the next vertical blank (busy wait on the EE). */
int sceGsSyncV(int mode)
{
    unsigned int start;

    kernel_init();

    start = vblank_count;

    while (vblank_count == start)
    {
        pcKernelBusyWait();
    }

    return vblank_count & 1;
}

/* ------------------------------------------------------------------------------------------- */
/* caches and other no-ops on PC */

void FlushCache(int op)                     { (void)op; }
void SyncDCache(void* start, void* end)     { (void)start; (void)end; }
void InvalidDCache(void* start, void* end)  { (void)start; (void)end; }
void iSyncDCache(void* start, void* end)    { (void)start; (void)end; }
void iInvalidDCache(void* start, void* end) { (void)start; (void)end; }

char _gp[4];

/* EE timer 0 as the game configures it (bus clock / 256, ~576 kHz) */
unsigned int pcHwRcnt0(void)
{
    return (unsigned int)(pcOsTime() * (147456000.0 / 256.0));
}
