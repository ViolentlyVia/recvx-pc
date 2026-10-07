"""Compile-check sources for the PC port with 32-bit MSVC.

usage: python pc/tools/check.py [files...]      (default: all game sources)
Writes per-file logs to build_pc/logs and prints an error summary.
"""
import os, re, sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(__file__))
import common, msvc

ROOT, BUILD = common.ROOT, common.BUILD
CFLAGS = ["/c", "/Zs"] + common.CFLAGS


def check(src):
    rel = os.path.relpath(src, ROOT).replace("\\", "/")
    log = os.path.join(BUILD, "logs", rel.replace("/", "_") + ".log")
    args = ["cl"] + CFLAGS + common.include_flags() + [src]
    r = msvc.run(args, capture_output=True, text=True, errors="replace", cwd=ROOT)
    out = r.stdout + r.stderr
    with open(log, "w", encoding="utf-8") as f:
        f.write(out)
    errs = [l for l in out.splitlines() if re.search(r"\berror [CD]\d+", l)]
    return rel, errs


def main():
    os.makedirs(os.path.join(BUILD, "logs"), exist_ok=True)
    common.sync_katana()
    files = sys.argv[1:] or common.game_sources()
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
