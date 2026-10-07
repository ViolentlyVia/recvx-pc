"""Build the PC executable (build_pc/recvx.exe) with 32-bit MSVC.

usage: python pc/tools/build.py [--release] [--clean]
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
    ap.add_argument("--release", action="store_true")
    ap.add_argument("--clean", action="store_true")
    opts = ap.parse_args()

    if opts.clean and os.path.isdir(OBJ_DIR):
        shutil.rmtree(OBJ_DIR)
    os.makedirs(OBJ_DIR, exist_ok=True)
    common.sync_katana()
    fetch_deps.fetch()
    sdl = fetch_deps.sdl2_dir()

    flags = common.CFLAGS + common.include_flags() + ["/I" + os.path.join(sdl, "include"),
                                                      "/Zi", "/FS", "/Fd" + os.path.join(common.BUILD, "recvx_c.pdb")]
    flags += ["/O2", "/MD"] if opts.release else ["/Od", "/MDd", "/D_DEBUG"]

    sources = common.game_sources() + common.pc_sources()
    hdr = header_mtime_cache()
    stale = [s for s in sources if not os.path.exists(obj_path(s)) or
             os.path.getmtime(obj_path(s)) < max(os.path.getmtime(s), hdr)]
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
    rsp = os.path.join(common.BUILD, "link.rsp")
    with open(rsp, "w") as f:
        f.write("\n".join('"%s"' % o for o in objs))
    link = ["link", "/nologo", "/DEBUG", "/SUBSYSTEM:CONSOLE", "/MACHINE:X86", "/OUT:" + EXE,
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
    shutil.copyfile(os.path.join(sdl, "lib", "x86", "SDL2.dll"), os.path.join(common.BUILD, "SDL2.dll"))
    print("built " + EXE)
    return 0


if __name__ == "__main__":
    sys.exit(main())
