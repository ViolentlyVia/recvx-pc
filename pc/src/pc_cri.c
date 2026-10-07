/* PC port: pieces of the CRI middleware that are PS2/devkit specific. */
#include "../../include/cri/mwlib/ee/lib/libadxe/cri_cvfs.h"

/* "host" file system of the CRI devkit (files on the developer's PC). Retail builds register it
 * but never open files through it, so an interface without functions is enough. */
static CVS_FS_IF htci_vtbl;

CVFS_IF htCiGetInterface(void)
{
    return &htci_vtbl;
}

void htCiSetOpenMode(Sint32 mode)
{
    (void)mode;
}

Sint32 htCiLoadFpCache(Sint8* fls_fname, Sint8* fpc_ptr, Sint32 fpc_size)
{
    (void)fls_fname; (void)fpc_ptr; (void)fpc_size;
    return 0;
}
