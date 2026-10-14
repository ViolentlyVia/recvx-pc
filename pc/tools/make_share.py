"""Build the shareable package: the game program without any of the game's files.

usage: python pc/tools/make_share.py [--rando <path to cvx-rando.exe>]

Writes build_pc/share/recvx-cvx-pc/ and build_pc/share/recvx-cvx-pc.zip containing recvx.exe,
recvx-config.exe, SDL2.dll and a README. Nothing else is needed on the player's PC: the settings
app sets the game up from their own disc image (no Python). Deliberately left out: the ISO,
extracted disc files (data/), converted movies, HD texture packs, recvx.ini (holds your local
paths), saves, logs and input recordings.

The randomizer (cvx-rando.exe, from the separate CVXRandomizer project) is included when it is
given with --rando or found in build_pc/.
"""
import argparse
import os
import shutil
import sys
import zipfile

sys.path.insert(0, os.path.dirname(__file__))
import common

BUILD = common.BUILD
OUT = os.path.join(BUILD, "share", "recvx-cvx-pc")

PROGRAM = ["recvx.exe", "recvx-config.exe", "SDL2.dll"]

README = """Resident Evil Code: Veronica X - PC port (recvx-decomp)
=====================================================

This package contains only the PC program. It does NOT contain any of the game's files:
you need your own copy of the US PlayStation 2 disc (SLUS-20184) as an .iso image.

Getting started
  1. Unzip this folder anywhere you like (it needs about 2.5 GB more once set up).
  2. Run recvx-config.exe. The first time, it asks for your disc image and sets the game up:
     it copies the game's files into a "data" folder here and converts the movies (it
     downloads FFmpeg once for that, about 100 MB, into a "tools" folder here).
     You can also drop the .iso onto recvx-config.exe.
  3. Press "Save and play". After this you can start recvx.exe directly; the disc image is no
     longer needed.

Settings
  recvx-config.exe sets the resolution, widescreen, fullscreen, folders and the keyboard /
  controller buttons. In the game, F11 or Alt+Enter switches between window and fullscreen.

Controls (defaults; keyboard and game controllers)
  Arrows = move, Z = Cross, X = Square, C = Circle, V = Triangle, A/S = L1/R1,
  Enter = Start, Backspace = Select. Hold Numpad 0 to fast-forward.

Saving: use the typewriters as usual. Saves are kept in the "saves" folder next to
recvx.exe (copy that folder to back them up or move them to another PC).

Optional: HD textures from a PCSX2 texture pack - pick its folder in recvx-config.exe.

Problems: the game writes recvx.log next to recvx.exe, and last_run_input.txt records your
last session. Include both when you report a problem.
"""

README_RANDO = """
Randomizer
  cvx-rando.exe (or "Randomizer..." in recvx-config.exe) shuffles items, enemies, doors and
  music. It writes the randomized game to a "rando" folder here and never changes your game
  files; a randomized game keeps its own saves. "Play normal game" in either program switches
  back to the normal game.
"""

RANDO_EXE = "cvx-rando.exe"


def main():
    parser = argparse.ArgumentParser(description="Build the shareable package.")
    parser.add_argument("--rando", help="path to cvx-rando.exe (default: build_pc/cvx-rando.exe if present)")
    args = parser.parse_args()

    missing = [f for f in PROGRAM if not os.path.exists(os.path.join(BUILD, f))]
    if missing:
        print("build first (python pc/tools/build.py); missing: " + ", ".join(missing))
        return 1

    rando = args.rando or os.path.join(BUILD, RANDO_EXE)
    if args.rando and not os.path.isfile(rando):
        print("not found: " + rando)
        return 1
    if not os.path.isfile(rando):
        rando = None

    # a folder still open elsewhere (Explorer, OneDrive) may survive; the zip only holds files
    shutil.rmtree(OUT, ignore_errors=True)
    os.makedirs(OUT, exist_ok=True)
    for f in PROGRAM:
        shutil.copy2(os.path.join(BUILD, f), os.path.join(OUT, f))
    if rando:
        shutil.copy2(rando, os.path.join(OUT, RANDO_EXE))
        print("including the randomizer: " + rando)
    with open(os.path.join(OUT, "README.txt"), "w", newline="\r\n") as f:
        f.write(README + (README_RANDO if rando else ""))

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
