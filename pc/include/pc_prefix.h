/* Force-included into every translation unit of the PC build. */
#pragma once

#ifndef TARGET_PC
#define TARGET_PC 1
#endif

#define DEBUG

/* GCC/MWCC attributes (alignment, VU sections) are not needed on PC. */
#define __attribute__(x)

/* MWCC treats "inline" in C; MSVC spells it __inline in C mode before C11 headers. */
#ifndef inline
#define inline __inline
#endif
#include "pc_vu0.h"
