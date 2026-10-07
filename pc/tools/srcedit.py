"""Helpers for porting edits to upstream sources.

Every edit keeps the original code for the PS2 build:

    #ifdef TARGET_PC
    <pc code>
    #else
    <original>
    #endif
"""
import re


class Source:
    def __init__(self, path):
        self.path = path
        text = open(path, encoding="latin-1", newline="").read()
        self.eol = "\r\n" if "\r\n" in text else "\n"
        self.lines = text.split(self.eol)

    def save(self):
        with open(self.path, "w", encoding="latin-1", newline="") as f:
            f.write(self.eol.join(self.lines))

    def _wrap(self, start, end, pc_code, indent=""):
        pc = [indent + l if l.strip() else l for l in pc_code.strip("\n").split("\n")]
        orig = self.lines[start:end + 1]
        self.lines[start:end + 1] = ["#ifdef TARGET_PC"] + pc + ["#else"] + orig + ["#endif"]

    def asm_blocks(self):
        """Return (start, end) line ranges of every `asm volatile` statement not already ported."""
        blocks, i, depth_pc = [], 0, 0
        while i < len(self.lines):
            line = self.lines[i]
            if line.startswith("#ifdef TARGET_PC"):
                # skip already-ported regions (up to the matching #endif)
                while not self.lines[i].startswith("#endif"):
                    i += 1
                i += 1
                continue
            if re.search(r"\basm\s+volatile\b|\basm\s*\(", line) and not line.lstrip().startswith("//"):
                j = i
                # brace form:  asm volatile { ... }     paren form:  asm volatile ( ... );
                while "{" not in self.lines[j] and "(" not in self.lines[j]:
                    j += 1
                if "{" in self.lines[j] and ("(" not in self.lines[j] or
                                             self.lines[j].index("{") < self.lines[j].index("(")):
                    depth, k = 0, j
                    while True:
                        depth += self.lines[k].count("{") - self.lines[k].count("}")
                        if depth == 0 and "}" in self.lines[k]:
                            break
                        k += 1
                else:
                    k = j
                    while not re.search(r"\)\s*;", self.lines[k]):
                        k += 1
                blocks.append((i, k))
                i = k + 1
                continue
            i += 1
        return blocks

    def port_asm(self, index, pc_code):
        """Replace the index-th unported asm block (0-based, in file order) with pc_code."""
        start, end = self.asm_blocks()[index]
        indent = re.match(r"\s*", self.lines[start]).group(0)
        self._wrap(start, end, pc_code, indent)

    def port_lines(self, old, pc_code):
        """Wrap the unique run of lines `old` (exact text, may span several lines)."""
        old_lines = old.strip("\n").split("\n")
        n = len(old_lines)
        hits = [i for i in range(len(self.lines) - n + 1) if self.lines[i:i + n] == old_lines]
        if len(hits) != 1:
            raise ValueError(f"{self.path}: expected 1 match, found {len(hits)} for {old_lines[0]!r}")
        self._wrap(hits[0], hits[0] + n - 1, pc_code)


def exclude_function(src, signature, note="reimplemented in pc/src"):
    """Compile the whole function starting at the line `signature` only for the PS2 build."""
    hits = [i for i, l in enumerate(src.lines) if l.rstrip() == signature.rstrip()]
    if len(hits) != 1:
        raise ValueError(f"{src.path}: expected 1 match for {signature!r}, found {len(hits)}")
    start = hits[0]
    # include a preceding "// xx% matching" comment
    if start > 0 and src.lines[start - 1].lstrip().startswith("//"):
        start -= 1
    k, depth, seen = hits[0], 0, False
    while True:
        depth += src.lines[k].count("{") - src.lines[k].count("}")
        seen = seen or "{" in src.lines[k]
        if seen and depth == 0:
            break
        k += 1
    src.lines[start:k + 1] = [f"#ifndef TARGET_PC /* {note} */"] + src.lines[start:k + 1] + ["#endif"]
