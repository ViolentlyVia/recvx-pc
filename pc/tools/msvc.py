"""Run MSVC (x86) tools from any shell by capturing the vcvarsall environment once."""
import json, os, shutil, subprocess, sys

VCVARS = r"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
CACHE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".msvc_env.json")


def msvc_env():
    if os.path.exists(CACHE):
        with open(CACHE) as f:
            return json.load(f)
    out = subprocess.run(f'"{VCVARS}" x86 >nul && set', shell=True,
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
