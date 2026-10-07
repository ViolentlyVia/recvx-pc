"""Compile-check sources for the PC port with 32-bit MSVC.

usage: python pc/tools/check.py [files...]      (default: all game sources)
Writes per-file logs to build_pc/logs and prints an error summary.
"""
import glob, os, re, shutil, sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(__file__))
import msvc

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BUILD = os.path.join(ROOT, "build_pc")
KATANA_SRC = os.path.join(ROOT, "include/recvx-decomp-katana/KATANA/Include")
KATANA_DST = os.path.join(BUILD, "katana_inc")
# Dreamcast libc headers that must not shadow the host CRT
LIBC = {"assert.h", "ctype.h", "errno.h", "float.h", "limits.h", "math.h", "setjmp.h",
        "stdarg.h", "stddef.h", "stdio.h", "stdlib.h", "string.h"}


def sync_katana():
    os.makedirs(KATANA_DST, exist_ok=True)
    for f in os.listdir(KATANA_SRC):
        src = os.path.join(KATANA_SRC, f)
        if f.lower() in LIBC or not os.path.isfile(src):
            continue
        shutil.copyfile(src, os.path.join(KATANA_DST, f))


INCLUDES = [
    "pc/include",
    "build_pc/katana_inc",
    "include/ps2/veronica/prog",
    "include/recvx-decomp-cri/cri/mwlib/include",
    "include/recvx-decomp-cri/cri/mwlib/ee/include",
    "src/cri/mwlib/ee/lib/libadxe",
    "src/ps2/veronica/prog",
]
CFLAGS = ["/nologo", "/c", "/TC", "/std:clatest", "/W3", "/wd4244", "/wd4305", "/wd4996",
          "/wd4828", "/Zs", "/DTARGET_PC", "/FIpc_prefix.h"]


def check(src):
    rel = os.path.relpath(src, ROOT).replace("\\", "/")
    log = os.path.join(BUILD, "logs", rel.replace("/", "_") + ".log")
    args = ["cl"] + CFLAGS + ["/I" + os.path.join(ROOT, i) for i in INCLUDES] + [src]
    r = msvc.run(args, capture_output=True, text=True, errors="replace", cwd=ROOT)
    out = r.stdout + r.stderr
    with open(log, "w", encoding="utf-8") as f:
        f.write(out)
    errs = [l for l in out.splitlines() if re.search(r"\berror [CD]\d+", l)]
    return rel, errs


def main():
    os.makedirs(os.path.join(BUILD, "logs"), exist_ok=True)
    sync_katana()
    files = sys.argv[1:] or sorted(glob.glob(os.path.join(ROOT, "src/ps2/veronica/prog/*.c")))
    files = [os.path.abspath(f) for f in files]
    with ThreadPoolExecutor(os.cpu_count()) as ex:
        results = list(ex.map(check, files))
    ok = 0
    codes = {}
    for rel, errs in results:
        if not errs:
            ok += 1
        else:
            print(f"{len(errs):4d}  {rel}   {errs[0].split(': ', 1)[-1][:110]}")
        for e in errs:
            m = re.search(r"error ([CD]\d+): (.*)", e)
            key = m.group(1) + ": " + m.group(2)[:90]
            codes[key] = codes.get(key, 0) + 1
    print(f"\n{ok}/{len(results)} files clean")
    for k, v in sorted(codes.items(), key=lambda x: -x[1])[:25]:
        print(f"{v:5d}  {k}")


if __name__ == "__main__":
    main()
