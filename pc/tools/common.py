"""Shared configuration for the PC port build scripts."""
import glob, os, shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BUILD = os.path.join(ROOT, "build_pc")
KATANA_SRC = os.path.join(ROOT, "include/recvx-decomp-katana/KATANA/Include")
KATANA_DST = os.path.join(BUILD, "katana_inc")
# Dreamcast libc headers that must not shadow the host CRT
LIBC = {"assert.h", "ctype.h", "errno.h", "float.h", "limits.h", "math.h", "setjmp.h",
        "stdarg.h", "stddef.h", "stdio.h", "stdlib.h", "string.h"}

INCLUDES = [
    "pc/include",
    "build_pc/katana_inc",
    "include/ps2/veronica/prog",
    "include/recvx-decomp-cri/cri/mwlib/include",
    "include/recvx-decomp-cri/cri/mwlib/ee/include",
    "src/cri/mwlib/ee/lib/libadxe",
    "src/ps2/veronica/prog",
]

CFLAGS = ["/nologo", "/TC", "/std:c17", "/W3", "/wd4244", "/wd4305", "/wd4996", "/wd4828",
          "/wd4005", "/wd4068", "/wd4307", "/Zc:preprocessor", "/wd5105", "/DTARGET_PC", "/FIpc_prefix.h"]

# Upstream sources replaced by the PC platform layer (pc/src). Grows as pieces are ported.
EXCLUDE = {
    # PS2 graphics: GS/VIF packet building and VU1 microcode drivers -> PC renderer
    # MPEG movie playback through the IPU -> PC movie player
    "src/ps2/veronica/prog/ps2_MovieFunc.c",
    # CRI ADX output to the IOP -> pc/src/pc_adx_rna.c
    "src/cri/mwlib/ee/lib/libadxe/adx_rnap.c",
    "src/cri/mwlib/ee/lib/libadxe/ps2_rna.c",
    # CRI host (devkit PC) file system, not used on retail discs
    "src/cri/mwlib/ee/lib/libadxe/htci.c",
    "src/cri/mwlib/ee/lib/libadxe/htci_sub.c",
}


def sync_katana():
    os.makedirs(KATANA_DST, exist_ok=True)
    for f in os.listdir(KATANA_SRC):
        src = os.path.join(KATANA_SRC, f)
        if f.lower() in LIBC or not os.path.isfile(src):
            continue
        dst = os.path.join(KATANA_DST, f)
        if not os.path.exists(dst) or os.path.getmtime(dst) < os.path.getmtime(src):
            shutil.copyfile(src, dst)


def include_flags():
    flags = ["/I" + os.path.join(ROOT, i) for i in INCLUDES]
    return flags


def game_sources():
    """All upstream C sources that make up the US executable."""
    files = sorted(glob.glob(os.path.join(ROOT, "src/ps2/veronica/prog/*.c")))
    files += sorted(glob.glob(os.path.join(ROOT, "src/cri/mwlib/ee/lib/libadxe/*.c")))
    # ps2_Vu1Scissor2.c is #included by another file, gcc_wrapper.c holds EE soft-float helpers
    files = [f for f in files if os.path.basename(f) not in ("ps2_Vu1Scissor2.c", "gcc_wrapper.c")]
    return [f for f in files if rel(f) not in EXCLUDE]


def pc_sources():
    return sorted(glob.glob(os.path.join(ROOT, "pc/src/*.c")) + glob.glob(os.path.join(ROOT, "pc/src/gs/*.c")) + glob.glob(os.path.join(ROOT, "pc/src/vu/*.c")))


# PC platform files that never include game headers: they talk to Windows/SDL and keep the
# host's 32-bit "long" (game code is compiled with the EE's 64-bit one, see pc_prefix.h).
NATIVE_LONG = {
    "pc/src/pc_os_win32.c", "pc/src/pc_platform.c", "pc/src/pc_audio.c", "pc/src/pc_kernel.c",
    "pc/src/pc_cdvd.c", "pc/src/pc_iop.c", "pc/src/pc_iop_tsnddrv.c", "pc/src/pc_sce_misc.c",
}


def extra_flags(path):
    r = rel(path)
    if r in NATIVE_LONG or r.startswith("pc/src/gs/"):
        return ["/DPC_NATIVE_LONG"]
    return []


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")
