"""One-off rewrite: MWCC accepts `*p->key++` on an array member (reads key[0], key[1], key[2]
in sequence). Make that explicit for the PC build, keeping the original under #else."""
import re, sys

PAT = re.compile(r"\*(\w+)->key\+\+")

for path in sys.argv[1:]:
    lines = open(path, encoding="latin-1").read().split("\n")
    out, i = [], 0
    while i < len(lines):
        line = lines[i]
        if PAT.search(line) and len(PAT.findall(line)) == 3:
            k = iter(range(3))
            pc = PAT.sub(lambda m: f"{m.group(1)}->key[{next(k)}]", line)
            out += ["#ifdef TARGET_PC", pc, "#else", line, "#endif"]
            i += 1
        elif PAT.search(line) and all(PAT.search(l) for l in lines[i:i + 3]):
            grp = lines[i:i + 3]
            pc = [PAT.sub(lambda m, n=n: f"{m.group(1)}->key[{n}]", l) for n, l in enumerate(grp)]
            out += ["#ifdef TARGET_PC"] + pc + ["#else"] + grp + ["#endif"]
            i += 3
        else:
            if PAT.search(line):
                sys.exit(f"{path}:{i+1}: unhandled pattern")
            out.append(line)
            i += 1
    open(path, "w", encoding="latin-1", newline="").write("\n".join(out))
