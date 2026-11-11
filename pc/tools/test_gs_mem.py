"""Test the GS image transfers' block paths against the pixel by pixel reference (no game needed).

usage: python pc/tools/test_gs_mem.py

Builds pc/tests/gs_mem_blockpath_test.c twice with MSVC (GS_BLOCK_FASTPATH=1 and =0), runs both
and compares what they print: GS memory, page generations and transfer-done calls after the
same random transfers.
"""
import os, subprocess, sys

sys.path.insert(0, os.path.dirname(__file__))
import common, msvc

SRC = os.path.join(common.ROOT, "pc", "tests", "gs_mem_blockpath_test.c")
OUT = os.path.join(common.BUILD, "obj", "test_gs_mem")


def build(fast):
    name = "fast" if fast else "ref"
    exe = os.path.join(OUT, name + ".exe")
    r = msvc.run(["cl", "/nologo", "/W3", "/wd4996", "/O2", "/DGS_BLOCK_FASTPATH=%d" % fast,
                  "/Fo" + os.path.join(OUT, name + ".obj"), "/Fe" + exe, SRC],
                 capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("FAILED compiling the " + name + " build\n" + r.stdout + r.stderr)
    return subprocess.run([exe], capture_output=True, text=True, check=True).stdout


def main():
    os.makedirs(OUT, exist_ok=True)
    fast, ref = build(1), build(0)
    print(fast, end="")
    if fast != ref:
        print("DIFFERENT from the reference:\n" + ref, end="")
        return 1
    print("same as the reference")
    return 0


if __name__ == "__main__":
    sys.exit(main())
