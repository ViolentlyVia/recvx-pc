/* Force-included into every translation unit of the PC build. */
#pragma once

#ifndef TARGET_PC
#define TARGET_PC 1
#endif

#define DEBUG

/* C runtime headers first: they must see the host's own "long" */
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <limits.h>
#include <float.h>
#include <ctype.h>
#include <time.h>
#include <assert.h>
#include <errno.h>
#include <setjmp.h>

/* GCC/MWCC attributes (alignment, VU sections) are not needed on PC. */
#define __attribute__(x)

/* MWCC treats "inline" in C; MSVC spells it __inline in C mode before C11 headers. */
#ifndef inline
#define inline __inline
#endif
#include "pc_vu0.h"

/* The EE C library's rand() returns 0..0x7FFFFFFF (game code divides by 2^31); MSVC's stops
 * at 32767. Same generator as the EE newlib. */
static __inline int pc_rand_impl(int seed, int set)
{
    static unsigned long long next = 1;

    if (set)
    {
        next = (unsigned int)seed;
        return 0;
    }

    next = next * 6364136223846793005ULL + 1;
    return (int)((next >> 32) & 0x7FFFFFFF);
}
#define rand() pc_rand_impl(0, 0)
#define srand(s) ((void)pc_rand_impl((int)(s), 1))

/* GCC's va_list name used by some CRI sources */
#include <stdarg.h>
typedef va_list __gnuc_va_list;

/* The EE (and the MWCC compiler the game was built with) have a 64-bit "long"; game code and
 * structures depend on it (GS register values, DMA tags, file data). PC platform files that
 * talk to Windows/SDL are compiled with PC_NATIVE_LONG instead. */
#ifndef PC_NATIVE_LONG
#define long long long
#endif
#include "pc_asm2c.h"
