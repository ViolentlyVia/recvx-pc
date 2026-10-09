"""Build the PC executable (build_pc/recvx.exe) with 32-bit MSVC.

usage: python pc/tools/build.py [--debug] [--clean]
Optimised by default; --debug builds without optimisation (separate object directory).
Compiles changed sources in parallel, links against SDL2 and copies SDL2.dll next to the exe.
Failing compiles are listed; the link step still runs so missing symbols show up together.
"""
import argparse, os, re, shutil, sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(__file__))
import common, fetch_deps, msvc

OBJ_DIR = os.path.join(common.BUILD, "obj")
EXE = os.path.join(common.BUILD, "recvx.exe")


def header_mtime_cache():
    """Newest header mtime in the include dirs: any header change rebuilds everything (simple and safe)."""
    newest = 0
    for inc in ["include", "pc/include", "pc/src", "src"]:
        for dirpath, _, files in os.walk(os.path.join(common.ROOT, inc)):
            for f in files:
                if f.endswith(".h"):
                    newest = max(newest, os.path.getmtime(os.path.join(dirpath, f)))
    return newest


def source_mtime(src):
    """mtime of a source including any .c files it #includes (ps2_Vu1Strip.c includes ps2_Vu1Scissor2.c)."""
    t = os.path.getmtime(src)
    with open(src, encoding="utf-8", errors="replace") as f:
        for name in re.findall(r'^\s*#\s*include\s+"([^"]+\.c)"', f.read(), re.M):
            inc = os.path.join(os.path.dirname(src), name)
            if os.path.exists(inc):
                t = max(t, os.path.getmtime(inc))
    return t


def obj_path(src):
    return os.path.join(OBJ_DIR, common.rel(src).replace("/", "_")[:-2] + ".obj")


def compile_one(args):
    src, flags = args
    obj = obj_path(src)
    r = msvc.run(["cl"] + flags + common.extra_flags(src) + ["/c", "/Fo" + obj, src], capture_output=True, text=True,
                 errors="replace", cwd=common.ROOT)
    out = (r.stdout + r.stderr).splitlines()
    errs = [l for l in out if re.search(r"\b(error|fatal error) [CD]\d+", l)]
    return src, r.returncode == 0, errs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--debug", action="store_true")
    ap.add_argument("--clean", action="store_true")
    opts = ap.parse_args()

    global OBJ_DIR
    if opts.debug:
        OBJ_DIR = os.path.join(common.BUILD, "obj_debug")

    if opts.clean and os.path.isdir(OBJ_DIR):
        shutil.rmtree(OBJ_DIR)
    os.makedirs(OBJ_DIR, exist_ok=True)
    common.sync_katana()
    fetch_deps.fetch()
    sdl = fetch_deps.sdl2_dir()

    flags = common.CFLAGS + common.include_flags() + ["/I" + os.path.join(sdl, "include"),
                                                      "/Zi", "/FS", "/Fd" + os.path.join(common.BUILD, "recvx_c.pdb")]
    # /GF-: the game writes into string literals (gdFsInit uppercases file names in place)
    flags += ["/O2", "/GF-", "/MD"] if not opts.debug else ["/Od", "/MDd", "/D_DEBUG"]

    sources = common.game_sources() + common.pc_sources()
    hdr = header_mtime_cache()
    stale = [s for s in sources if not os.path.exists(obj_path(s)) or
             os.path.getmtime(obj_path(s)) < max(source_mtime(s), hdr)]
    print(f"compiling {len(stale)}/{len(sources)} files")
    failed = []
    with ThreadPoolExecutor(os.cpu_count()) as ex:
        for src, ok, errs in ex.map(compile_one, [(s, flags) for s in stale]):
            if not ok:
                failed.append(src)
                print(f"FAILED {common.rel(src)}")
                for e in errs[:5]:
                    print("    " + e.split(common.ROOT)[-1])
    if failed:
        print(f"{len(failed)} files failed to compile")
        return 1

    objs = [obj_path(s) for s in sources]
    res = compile_res("recvx")
    if res is None:
        return 1
    objs.append(res)
    rsp = os.path.join(common.BUILD, "link.rsp")
    with open(rsp, "w") as f:
        f.write("\n".join('"%s"' % o for o in objs))
    # a Windows program (no console window) that still starts at main(); RECVX_CONSOLE=1 opens one
    link = ["link", "/nologo", "/DEBUG", "/SUBSYSTEM:WINDOWS", "/ENTRY:mainCRTStartup", "/MACHINE:X86", "/OUT:" + EXE,
            "/LIBPATH:" + os.path.join(sdl, "lib", "x86"), "SDL2.lib", "@" + rsp]
    r = msvc.run(link, capture_output=True, text=True, errors="replace", cwd=common.ROOT)
    out = r.stdout + r.stderr
    with open(os.path.join(common.BUILD, "link.log"), "w") as f:
        f.write(out)
    unresolved = sorted(set(re.findall(r"unresolved external symbol _?(\w+)", out)))
    if r.returncode != 0:
        print(out[-3000:] if not unresolved else "")
        if unresolved:
            print(f"{len(unresolved)} unresolved symbols (full list in build_pc/link.log)")
            with open(os.path.join(common.BUILD, "unresolved.txt"), "w") as f:
                f.write("\n".join(unresolved) + "\n")
        return 1
    dll_src = os.path.join(sdl, "lib", "x86", "SDL2.dll")
    dll_dst = os.path.join(common.BUILD, "SDL2.dll")
    # only when it changed: a running recvx.exe or recvx-config.exe keeps it open
    if not os.path.exists(dll_dst) or os.path.getsize(dll_dst) != os.path.getsize(dll_src):
        shutil.copyfile(dll_src, dll_dst)
    print("built " + EXE)
    return build_config(sdl, opts.debug)


def compile_res(name):
    """pc/res/<name>.rc (icon, version information) -> .res for the linker; None on failure."""
    rc = os.path.join(common.ROOT, "pc", "res", name + ".rc")
    out = os.path.join(OBJ_DIR, name + ".res")
    r = msvc.run(["rc", "/nologo", "/fo", out, rc], capture_output=True, text=True, errors="replace",
                 cwd=os.path.dirname(rc))
    if r.returncode != 0:
        print("FAILED pc/res/" + name + ".rc")
        print(r.stdout + r.stderr)
        return None
    return out


def build_config(sdl, debug):
    """recvx-config.exe: the settings app (pc/config), a small Win32 program next to recvx.exe."""
    cfg = os.path.join(common.ROOT, "pc/config")
    srcs = [os.path.join(cfg, f) for f in ("recvx_config.c", "setup_game.c")]
    exe = os.path.join(common.BUILD, "recvx-config.exe")
    res_dir = os.path.join(common.ROOT, "pc", "res")
    deps = srcs + [os.path.join(cfg, "setup_game.h"), os.path.join(common.ROOT, "pc/include/pc_bindings.h"),
                   os.path.join(res_dir, "recvx-config.rc"), os.path.join(res_dir, "recvx-config.ico"),
                   os.path.join(res_dir, "version.h")]
    if os.path.exists(exe) and os.path.getmtime(exe) >= max(os.path.getmtime(d) for d in deps):
        return 0
    objs = [os.path.join(OBJ_DIR, os.path.basename(s)[:-2] + ".obj") for s in srcs]
    cmd = ["cl", "/nologo", "/W3", "/wd4996", "/O2" if not debug else "/Od", "/MD", "/c",
           "/I" + os.path.join(common.ROOT, "pc/include"), "/I" + os.path.join(sdl, "include"),
           "/Fo" + OBJ_DIR + os.sep] + srcs
    r = msvc.run(cmd, capture_output=True, text=True, errors="replace", cwd=common.ROOT)
    if r.returncode != 0:
        print("FAILED compiling recvx-config.exe (pc/config)")
        print(r.stdout + r.stderr)
        return 1
    res = compile_res("recvx-config")
    if res is None:
        return 1
    link = ["link", "/nologo", "/SUBSYSTEM:WINDOWS", "/MACHINE:X86", "/MANIFEST:EMBED", "/OUT:" + exe] + objs + [res,
            "/LIBPATH:" + os.path.join(sdl, "lib", "x86"), "SDL2.lib", "user32.lib", "gdi32.lib"]
    r = msvc.run(link, capture_output=True, text=True, errors="replace", cwd=common.ROOT)
    if r.returncode != 0:
        print("FAILED linking recvx-config.exe")
        print(r.stdout + r.stderr)
        return 1
    print("built " + exe)
    return 0


if __name__ == "__main__":
    sys.exit(main())
