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
          "/wd4005", "/wd4068", "/wd4307", "/DTARGET_PC", "/FIpc_prefix.h"]

# Upstream sources replaced by the PC platform layer (pc/src). Grows as pieces are ported.
EXCLUDE = {
    # PS2 graphics: GS/VIF packet building and VU1 microcode drivers -> PC renderer
    "src/ps2/veronica/prog/ps2_NaDraw2D.c",
    "src/ps2/veronica/prog/ps2_NaSprite.c",
    "src/ps2/veronica/prog/ps2_NaSystem.c",
    "src/ps2/veronica/prog/ps2_NinjaCnk.c",
    "src/ps2/veronica/prog/ps2_Vu1Strip.c",
    "src/ps2/veronica/prog/ps2_dummy.c",
    "src/ps2/veronica/prog/ps2_loadtim2.c",
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
    return sorted(glob.glob(os.path.join(ROOT, "pc/src/*.c")))


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")
