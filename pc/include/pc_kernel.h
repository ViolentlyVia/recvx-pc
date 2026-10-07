/* PC port: EE kernel emulation hooks for the PC platform layer (see pc_kernel.c). */
#pragma once

/* Switch to the highest-priority ready thread (waits for interrupts if none). */
void pcKernelReschedule(void);

/* Run pending interrupts and switch if they woke a higher-priority thread. */
void pcKernelPoll(void);

/* One iteration of a PS2 busy-wait loop: sleep until the next interrupt and handle it, without
 * letting lower-priority threads run (exactly like spinning on the EE). */
void pcKernelBusyWait(void);

/* Number of VBLANK interrupts so far. */
unsigned int pcKernelVblankCount(void);
