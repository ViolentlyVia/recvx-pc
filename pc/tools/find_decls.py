"""Print the declaration found in headers for each symbol in build_pc/unresolved.txt."""
import os, re, sys
sys.path.insert(0, os.path.dirname(__file__))
import common

HDR_DIRS = ["include/ps2/veronica/prog", "include/cri", "build_pc/katana_inc", "pc/include",
            "include/recvx-decomp-cri/cri/mwlib/include", "include/recvx-decomp-cri/cri/mwlib/ee/include"]
headers = []
for d in HDR_DIRS:
    for dp, _, fs in os.walk(os.path.join(common.ROOT, d)):
        headers += [os.path.join(dp, f) for f in fs if f.lower().endswith(".h")]
texts = {h: open(h, encoding="latin-1").read() for h in headers}

syms = sys.argv[1:] or open(os.path.join(common.BUILD, "unresolved.txt")).read().split()
for s in syms:
    hit = None
    for h, t in texts.items():
        for m in re.finditer(r"^[^\n#/]*\b%s\b[^\n]*" % re.escape(s), t, re.M):
            line = m.group(0).strip()
            if line.endswith(";") and ("(" in line or line.startswith("extern")):
                hit = (os.path.basename(h), line)
                break
        if hit:
            break
    print(f"{s:36} {hit[0] + ': ' + hit[1] if hit else '-'}")
