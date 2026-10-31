"""Build the PC executable for Linux (build_pc/linux/recvx) with 32-bit GCC.

usage: python3 pc/tools/build_linux.py [--debug] [--clean]
Needs gcc-multilib and the i386 SDL2 and OpenGL development packages (libsdl2-dev:i386,
libgl-dev:i386). Optimised by default; --debug builds without optimisation (separate object
directory). Compiles changed sources in parallel; failing compiles are listed, and the link step
only runs when everything compiled.
"""
import argparse, os, re, shutil, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(__file__))
import common

OUT_DIR = os.path.join(common.BUILD, "linux")
OBJ_DIR = os.path.join(OUT_DIR, "obj")
EXE = os.path.join(OUT_DIR, "recvx")

# the same code generation as the MSVC build: SSE2 floats (not x87), wrapping signed overflow,
# no type-based alias analysis, string literals and tentative definitions as MSVC treats them.
# -fpermissive: old C that MSVC only warns about (implicit declarations, pointer mismatches).
# Locals start at zero: some game code reads variables it never set (KazuSet), harmless with the
# PS2's and MSVC's leftover values, not with GCC's.
# Plain C17 with POSIX names: the BSD ones (u_long, u_int) would clash with the PS2 SDK's.
CFLAGS = ["-m32", "-std=c17", "-D_XOPEN_SOURCE=700", "-msse2", "-mfpmath=sse", "-fwrapv", "-fno-strict-aliasing", "-fcommon",
          "-fno-merge-constants", "-fno-pie", "-g", "-w", "-fpermissive", "-ftrivial-auto-var-init=zero", "-DTARGET_PC", "-D_FILE_OFFSET_BITS=64",
          "-include", "pc_prefix.h"]


def sdl_flags(what):
    """--cflags or --libs of the 32-bit SDL2."""
    env = dict(os.environ, PKG_CONFIG_PATH="/usr/lib/i386-linux-gnu/pkgconfig")
    r = subprocess.run(["pkg-config", what, "sdl2"], capture_output=True, text=True, env=env)
    if r.returncode != 0:
        sys.exit("SDL2 for i386 not found (sudo apt install libsdl2-dev:i386)")
    return r.stdout.split()


def obj_path(src):
    return os.path.join(OBJ_DIR, common.rel(src).replace("/", "_")[:-2] + ".o")


def extra_flags(path):
    flags = ["-DPC_NATIVE_LONG"] if common.extra_flags(path) else []
    if common.rel(path) == "pc/src/pc_os_posix.c":
        flags.append("-D_GNU_SOURCE")   # fibers, dl_iterate_phdr, MAP_ANONYMOUS
    return flags


def compile_one(args):
    src, flags = args
    obj = obj_path(src)
    r = subprocess.run(["gcc"] + flags + extra_flags(src) + ["-MMD", "-c", "-o", obj, src], capture_output=True,
                       text=True, errors="replace", cwd=common.ROOT)
    errs = [l for l in (r.stdout + r.stderr).splitlines() if re.search(r"\b(error|fatal error):", l)]
    return src, r.returncode == 0, errs


def stale(src):
    """No object yet, or the source or any header it used (from gcc's .d file) is newer."""
    obj = obj_path(src)
    dep = obj[:-2] + ".d"
    if not os.path.exists(obj) or not os.path.exists(dep):
        return True
    t = os.path.getmtime(obj)
    with open(dep, encoding="utf-8", errors="replace") as f:
        names = f.read().replace("\\\n", " ").split(":", 1)[-1].split()
    return any(not os.path.exists(n) or os.path.getmtime(n) > t for n in
               (os.path.join(common.ROOT, n) for n in names))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--debug", action="store_true")
    ap.add_argument("--clean", action="store_true")
    opts = ap.parse_args()

    global OBJ_DIR
    if opts.debug:
        OBJ_DIR = os.path.join(OUT_DIR, "obj_debug")

    if opts.clean and os.path.isdir(OBJ_DIR):
        shutil.rmtree(OBJ_DIR)
    os.makedirs(OBJ_DIR, exist_ok=True)
    common.sync_katana()

    flags = CFLAGS + ["-I" + os.path.join(common.ROOT, i) for i in common.INCLUDES] + sdl_flags("--cflags")
    flags += ["-O2"] if not opts.debug else ["-O0", "-D_DEBUG"]

    sources = common.game_sources() + common.pc_sources()
    todo = [s for s in sources if stale(s)]
    print(f"compiling {len(todo)}/{len(sources)} files")
    failed = []
    with ThreadPoolExecutor(os.cpu_count()) as ex:
        for src, ok, errs in ex.map(compile_one, [(s, flags) for s in todo]):
            if not ok:
                failed.append(src)
                print(f"FAILED {common.rel(src)}")
                for e in errs[:5]:
                    print("    " + e.split(common.ROOT)[-1])
    if failed:
        print(f"{len(failed)} files failed to compile")
        return 1

    objs = [obj_path(s) for s in sources]
    # -rdynamic: names in crash stack traces; no PIE, like the Windows exe (fixed addresses)
    link = ["gcc", "-m32", "-no-pie", "-rdynamic", "-o", EXE] + objs + sdl_flags("--libs") + ["-lGL", "-lm", "-lpthread"]
    r = subprocess.run(link, capture_output=True, text=True, errors="replace", cwd=common.ROOT)
    out = r.stdout + r.stderr
    with open(os.path.join(OUT_DIR, "link.log"), "w") as f:
        f.write(out)
    if r.returncode != 0:
        unresolved = sorted(set(re.findall(r"undefined reference to `(\w+)'", out)))
        if unresolved:
            print(f"{len(unresolved)} unresolved symbols (full list in build_pc/linux/link.log)")
            with open(os.path.join(OUT_DIR, "unresolved.txt"), "w") as f:
                f.write("\n".join(unresolved) + "\n")
        else:
            print(out[-3000:])
        return 1
    print("built " + EXE)
    return 0


if __name__ == "__main__":
    sys.exit(main())
