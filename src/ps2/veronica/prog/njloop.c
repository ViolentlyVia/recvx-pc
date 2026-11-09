#include "../../../ps2/veronica/prog/njloop.h"

#ifdef TARGET_PC
void* pcOsAllocScratchpad(void);
#endif

// 100% matching!
int main(int argc, char *argv[])
{
#ifdef TARGET_PC
    /* before anything else can load a DLL onto its fixed address */
    pcOsAllocScratchpad();
#endif

    njUserInit();
    
    while (TRUE) 
    { 
        if (njUserMain() < NJD_USER_CONTINUE) 
        { 
            break;
        } 
        
        njWaitVSync();
    } 
    
    njUserExit(); 
} 
