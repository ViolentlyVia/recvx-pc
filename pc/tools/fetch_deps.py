"""Download third-party dependencies for the PC port into build_pc/deps."""
import io, os, urllib.request, zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEPS = os.path.join(ROOT, "build_pc", "deps")

SDL2_VERSION = "2.32.10"
SDL2_URL = (f"https://github.com/libsdl-org/SDL/releases/download/release-{SDL2_VERSION}/"
            f"SDL2-devel-{SDL2_VERSION}-VC.zip")


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
