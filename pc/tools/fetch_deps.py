"""Download third-party dependencies for the PC port into build_pc/deps."""
import io, os, urllib.request, zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
# RECVX_DEPS overrides the location (the share package keeps its tools outside the repository)
DEPS = os.environ.get("RECVX_DEPS") or os.path.join(ROOT, "build_pc", "deps")

SDL2_VERSION = "2.32.10"
SDL2_URL = (f"https://github.com/libsdl-org/SDL/releases/download/release-{SDL2_VERSION}/"
            f"SDL2-devel-{SDL2_VERSION}-VC.zip")


# FFmpeg is only used by pc/tools/convert_movies.py (converting the disc's movies once at setup);
# it is not linked into or shipped with the game.
FFMPEG_URL = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"


def ffmpeg_exe():
    """Path of ffmpeg.exe, downloaded on first use."""
    for dirpath, _, files in os.walk(DEPS):
        if "ffmpeg.exe" in files:
            return os.path.join(dirpath, "ffmpeg.exe")
    os.makedirs(DEPS, exist_ok=True)
    print(f"downloading {FFMPEG_URL}")
    data = urllib.request.urlopen(FFMPEG_URL).read()
    zipfile.ZipFile(io.BytesIO(data)).extractall(os.path.join(DEPS, "ffmpeg"))
    return ffmpeg_exe()


def sdl2_dir():
    return os.path.join(DEPS, f"SDL2-{SDL2_VERSION}")


def fetch():
    if os.path.isdir(sdl2_dir()):
        return
    os.makedirs(DEPS, exist_ok=True)
    print(f"downloading {SDL2_URL}")
    data = urllib.request.urlopen(SDL2_URL).read()
    zipfile.ZipFile(io.BytesIO(data)).extractall(DEPS)


if __name__ == "__main__":
    fetch()
    print(sdl2_dir())
