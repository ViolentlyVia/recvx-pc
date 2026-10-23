"""Run MSVC (x86) tools from any shell by capturing the vcvarsall environment once."""
import json, os, shutil, subprocess, sys

VCVARS = r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
VSWHERE = r"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
CACHE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".msvc_env.json")


def vcvars():
    """VCVARS, else the vcvarsall.bat of any Visual Studio with the C++ x86/x64 tools (Community, ...)."""
    if os.path.exists(VCVARS) or not os.path.exists(VSWHERE):
        return VCVARS
    out = subprocess.run([VSWHERE, "-latest", "-products", "*", "-requires",
                          "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
                         capture_output=True, text=True).stdout.strip()
    found = os.path.join(out, r"VC\Auxiliary\Build\vcvarsall.bat") if out else ""
    return found if os.path.exists(found) else VCVARS


def msvc_env():
    if os.path.exists(CACHE):
        with open(CACHE) as f:
            return json.load(f)
    path = vcvars()
    if not os.path.exists(path):
        sys.exit("Visual Studio 2022 with the C++ x86 tools was not found (" + path + ")")
    out = subprocess.run(f'"{path}" x86 >nul && set', shell=True,
                         capture_output=True, text=True).stdout
    env = dict(l.split("=", 1) for l in out.splitlines() if "=" in l)
    with open(CACHE, "w") as f:
        json.dump(env, f)
    return env


def run(args, **kw):
    env = msvc_env()
    exe = shutil.which(args[0], path=env["PATH"]) or args[0]
    return subprocess.run([exe] + list(args[1:]), env=env, **kw)


if __name__ == "__main__":
    sys.exit(run(sys.argv[1:]).returncode)
