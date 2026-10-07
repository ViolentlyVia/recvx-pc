"""Build the shareable package: the game program without any of the game's files.

usage: python pc/tools/make_share.py

Writes build_pc/share/recvx-cvx-pc/ and build_pc/share/recvx-cvx-pc.zip containing recvx.exe,
SDL2.dll, the setup scripts and a README. Each recipient extracts their own disc image with
setup\\setup_game.py. Deliberately left out: the ISO, extracted disc files (data/), converted
movies, HD texture packs, recvx.ini (holds your local paths), logs and input recordings.
"""
import os
import shutil
import sys
import zipfile

sys.path.insert(0, os.path.dirname(__file__))
import common

BUILD = common.BUILD
OUT = os.path.join(BUILD, "share", "recvx-cvx-pc")

PROGRAM = ["recvx.exe", "SDL2.dll"]
SETUP = ["setup_game.py", "extract_iso.py", "convert_movies.py", "fetch_deps.py"]

README = """Resident Evil Code: Veronica X - PC port (recvx-decomp)
=====================================================

This package contains only the PC program. It does NOT contain any of the game's files:
you need your own copy of the US PlayStation 2 disc (SLUS-20184) as an .iso image.

Setup (once)
  1. Install Python 3 (https://www.python.org/downloads/) if you do not have it.
  2. Run:   python setup\\setup_game.py "C:\\path\\to\\your\\game.iso"
     This copies the files from your disc image into a "data" folder here and converts the
     movies (it downloads FFmpeg into setup\\deps for that, about 100 MB).
  3. Start recvx.exe. The ISO is no longer needed after setup.

  Without step 2 you can also put your .iso next to recvx.exe (or set iso=C:\\path\\game.iso
  in recvx.ini); the game then runs straight from the image, but movies are skipped.

Controls (keyboard; game controllers work too)
  Arrows = move, Z = Cross, X = Square, C = Circle, V = Triangle, A/S = L1/R1, Q/W = L2/R2,
  Enter = Start, Backspace = Select. Hold Numpad 0 to fast-forward.

Optional: HD textures from a PCSX2 texture pack - add  hd_textures=C:\\path\\to\\pack  to
recvx.ini (create the file next to recvx.exe if needed).

Not finished yet: memory card saves and sound reverb.
"""


def main():
    missing = [f for f in PROGRAM if not os.path.exists(os.path.join(BUILD, f))]
    if missing:
        print("build first (python pc/tools/build.py); missing: " + ", ".join(missing))
        return 1
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(os.path.join(OUT, "setup"))
    for f in PROGRAM:
        shutil.copy2(os.path.join(BUILD, f), os.path.join(OUT, f))
    for f in SETUP:
        shutil.copy2(os.path.join(os.path.dirname(__file__), f), os.path.join(OUT, "setup", f))
    with open(os.path.join(OUT, "README.txt"), "w", newline="\r\n") as f:
        f.write(README)

    # safety net: nothing that looks like game data may end up in the package
    for dirpath, _, files in os.walk(OUT):
        for f in files:
            ext = os.path.splitext(f)[1].lower()
            if ext in (".iso", ".afs", ".pss", ".mpg", ".dds", ".ald", ".ini") or f.upper().startswith("SLUS"):
                print("refusing to package game data: " + os.path.join(dirpath, f))
                return 1

    zpath = OUT + ".zip"
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for dirpath, _, files in os.walk(OUT):
            for f in files:
                full = os.path.join(dirpath, f)
                z.write(full, os.path.relpath(full, os.path.dirname(OUT)))
    print(f"wrote {zpath} ({os.path.getsize(zpath) / 1e6:.1f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
