#include "../../../ps2/veronica/prog/ps2_sg_sytmr.h"

// 100% matching! 
Uint32 syTmrGetCount(void)
{

#ifdef TARGET_PC /* the PS2 build returned whatever was left in the return register */
    return 0;
#endif
}
