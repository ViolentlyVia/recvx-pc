"""Parse a VU microcode listing (vsm/*.vsm) into (labels, [(upper, lower, ebit)])."""
import re


def parse(path):
    labels, prog = {}, []
    for raw in open(path, encoding="latin-1"):
        line = re.sub(r"/\* [0-9A-F]{4} [0-9A-F]{16} \*/", "", raw).strip()
        m = re.match(r"^([A-Za-z_]\w*):", line)
        if m:
            labels[m.group(1)] = len(prog)
            continue
        if "/* | */" not in line or "PADDING" in line:
            continue
        up, lo = [p.strip() for p in line.split("/* | */", 1)]
        lo = re.sub(r"/\*.*?\*/", "", lo).strip()
        ebit = "[E]" in up
        up = up.replace("[E]", "")
        prog.append((up, lo, ebit))
    return labels, prog


if __name__ == "__main__":
    import sys, collections
    labels, prog = parse(sys.argv[1])
    ops = collections.Counter()
    for up, lo, e in prog:
        ops[up.split()[0]] += 1
        ops[lo.split()[0]] += 1
    print(len(prog), "instructions,", len(labels), "labels")
    print(" ".join(f"{k}:{v}" for k, v in sorted(ops.items())))
