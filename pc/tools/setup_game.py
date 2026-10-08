"""Set up recvx.exe from your own copy of the game.

usage: python setup_game.py <your Resident Evil Code: Veronica X (USA, SLUS-20184) .iso>

Extracts the disc into a "data" folder next to recvx.exe and converts its movies. The game finds
that folder by itself, so after this the ISO is no longer needed. Nothing from the game is
downloaded or shipped: everything comes from the disc image you provide. (FFmpeg, used once to
convert the movies, is downloaded into a "deps" folder next to this script.)
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
os.environ.setdefault("RECVX_DEPS", os.path.join(HERE, "deps"))
sys.path.insert(0, HERE)

import convert_movies  # noqa: E402
import extract_iso     # noqa: E402

EXPECTED_ID = "SLUS_201.84"


def find_exe_dir():
    # shared package: setup/ next to recvx.exe; repository: pc/tools/ with the exe in build_pc/
    repo_build = os.path.join(os.path.dirname(os.path.dirname(HERE)), "build_pc")
    for d in (os.path.dirname(HERE), HERE, repo_build):
        if os.path.exists(os.path.join(d, "recvx.exe")):
            return d
    return os.path.dirname(HERE)


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 1
    iso = sys.argv[1]
    data = os.path.join(find_exe_dir(), "data")

    print(f"extracting {iso} -> {data}")
    sys.argv = ["extract_iso.py", iso, data]
    if extract_iso.main() != 0:
        return 1
    if not os.path.exists(os.path.join(data, EXPECTED_ID)):
        print(f"warning: {EXPECTED_ID} not found - this port is for the US release (SLUS-20184)")

    print("converting movies")
    sys.argv = ["convert_movies.py", data]
    if convert_movies.main() != 0:
        return 1

    print("\nAll set - run recvx.exe.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
