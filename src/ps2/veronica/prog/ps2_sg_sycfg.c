#include "../../../ps2/veronica/prog/ps2_sg_sycfg.h"

int SoundMode;

// 100% matching! 
Sint32 syCfgExit( Void  )
{

#ifdef TARGET_PC /* the PS2 build returned whatever was left in the return register */
    return 0;
#endif
}

// 100% matching! 
Sint32 syCfgGetSoundMode( Sint32* pnMode )
{ 
    *pnMode = SoundMode; 
    
    return 0;
}

// 100% matching! 
#ifdef TARGET_PC
Sint32 syCfgInit( Void* p )
#else
Sint32 syCfgInit( Void* )
#endif
{

#ifdef TARGET_PC /* the PS2 build returned whatever was left in the return register */
    return 0;
#endif
}

// 100% matching! 
Sint32 syCfgSetSoundMode( Sint32 nMode )
{ 
    SoundMode = nMode; 
#ifdef TARGET_PC /* the PS2 build returned whatever was left in the return register */
    return 0;
#endif
}
