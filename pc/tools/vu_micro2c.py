"""Compile VU microcode (vsm/ps2_vu0.vsm) into C (pc/src/vu/vu0_micro.c).

Each instruction pair becomes a block that
  1. evaluates the upper and lower instruction from the *old* register state,
  2. commits both results,
  3. handles branch delay slots and the E (end) bit.
Indirect jumps (JR/JALR) go through a dispatch switch over every instruction index.
"""
import os, re, sys

sys.path.insert(0, os.path.dirname(__file__))
from vsm_parse import parse

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FIELDS = "xyzw"


def mask_of(dest):
    return sum({"x": 8, "y": 4, "z": 2, "w": 1}[c] for c in dest)


def vf(tok):
    """'vf07' / 'vf24w' -> (index, field or None)"""
    m = re.match(r"vf(\d+)([xyzw]?)$", tok.strip())
    if not m:
        raise ValueError("bad vf operand " + tok)
    return int(m.group(1)), (m.group(2) or None)


def vi(tok):
    m = re.match(r"vi(\d+)$", tok.strip())
    if not m:
        raise ValueError("bad vi operand " + tok)
    return int(m.group(1))


class Gen:
    def __init__(self, labels):
        self.labels = labels

    def imm(self, tok):
        tok = tok.strip()
        if tok in self.labels:
            return str(self.labels[tok])
        return str(int(tok, 0))

    # ---------------------------------------------------------------- upper
    def upper(self, text):
        """returns (phase1 code, phase2 code)"""
        if text == "NOP":
            return "", ""
        m = re.match(r"([A-Z0-9]+?)(i|q|x|y|z|w)?\.([xyzw]+)\s+(.*)$", text)
        if not m:
            raise ValueError("upper: " + text)
        base, bc, dest, ops = m.group(1), m.group(2), m.group(3), [o.strip() for o in m.group(4).split(",")]
        # MADDA/MSUBA/MULA/ADDA/SUBA write ACC; names like "MULAx" parse as base "MULA" + bc "x"
        mask = mask_of(dest)
        if base in ("FTOI", "ITOF"):
            raise ValueError("unexpected")
        p1, p2 = [], []

        def src_b(k):
            if bc == "q":
                return "vu_q(v)"
            if bc == "i":
                return "v->i"
            if bc in ("x", "y", "z", "w"):
                n, _ = vf(ops[2])
                return "v->vf[%d].f[%d]" % (n, FIELDS.index(bc))
            n, _ = vf(ops[2])
            return "v->vf[%d].f[%d]" % (n, k)

        if base == "ABS":
            d, _ = vf(ops[0]); s, _ = vf(ops[1])
            for k in range(4):
                if mask & (8 >> k):
                    p1.append("ut.f[%d] = fabsf(v->vf[%d].f[%d]);" % (k, s, k))
            p2.append(self.commit_vf(d, mask))
            return "\n".join(p1), "\n".join(p2)

        if base == "CLIP":
            s, _ = vf(ops[0]); t, _ = vf(ops[1])
            p1.append("ut = v->vf[%d]; uw = v->vf[%d].f[3];" % (s, t))
            p2.append("vu_clip(v, &ut, uw);")
            return "\n".join(p1), "\n".join(p2)

        if base in ("FTOI0", "FTOI4", "FTOI12", "FTOI15", "ITOF0", "ITOF4", "ITOF12", "ITOF15"):
            pass  # handled below via regex on text

        acc_dest = ops[0] == "ACC"
        a_ops = ops[1:] if True else ops
        a, _ = vf(ops[1])

        expr = {
            "ADD": "v->vf[{a}].f[{k}] + {b}", "ADDA": "v->vf[{a}].f[{k}] + {b}",
            "SUB": "v->vf[{a}].f[{k}] - {b}", "SUBA": "v->vf[{a}].f[{k}] - {b}",
            "MUL": "v->vf[{a}].f[{k}] * {b}", "MULA": "v->vf[{a}].f[{k}] * {b}",
            "MADD": "v->acc.f[{k}] + v->vf[{a}].f[{k}] * {b}", "MADDA": "v->acc.f[{k}] + v->vf[{a}].f[{k}] * {b}",
            "MSUB": "v->acc.f[{k}] - v->vf[{a}].f[{k}] * {b}", "MSUBA": "v->acc.f[{k}] - v->vf[{a}].f[{k}] * {b}",
            "MAX": "fmaxf(v->vf[{a}].f[{k}], {b})", "MINI": "fminf(v->vf[{a}].f[{k}], {b})",
        }.get(base)
        if expr is None:
            raise ValueError("upper op " + text)
        for k in range(4):
            if mask & (8 >> k):
                p1.append("ut.f[%d] = vu_f(%s);" % (k, expr.format(a=a, k=k, b=src_b(k))))
        flags = base not in ("MAX", "MINI")
        if acc_dest or base in ("MADDA", "MSUBA", "MULA", "ADDA", "SUBA"):
            p2.append(self.commit_acc(mask))
        else:
            d, _ = vf(ops[0])
            p2.append(self.commit_vf(d, mask))
        if flags:
            p2.append("vu_set_mac(v, &ut, %d);" % mask)
        return "\n".join(p1), "\n".join(p2)

    def commit_vf(self, d, mask):
        if d == 0:
            return ""
        return " ".join("v->vf[%d].f[%d] = ut.f[%d];" % (d, k, k) for k in range(4) if mask & (8 >> k))

    def commit_acc(self, mask):
        return " ".join("v->acc.f[%d] = ut.f[%d];" % (k, k) for k in range(4) if mask & (8 >> k))

    # ---------------------------------------------------------------- lower
    def lower(self, text, idx):
        """returns (phase1, phase2, branch_code) - branch_code sets br/br_target"""
        if text in ("NOP", ""):
            return "", "", ""
        op, _, rest = text.partition(" ")
        ops = [o.strip() for o in rest.split(",")] if rest.strip() else []
        base, _, dest = op.partition(".")
        p1, p2, br = [], [], ""

        def mem(tok):
            m = re.match(r"(-?\w+)\((vi\d+)\)", tok.replace(" ", ""))
            return "((%s + v->vi[%d]) & 255)" % (self.imm(m.group(1)), vi(m.group(2)))

        if base == "LQ":
            d, _ = vf(ops[0])
            p1.append("lt = v->mem[%s];" % mem(ops[1]))
            if d:
                p2.append(" ".join("v->vf[%d].f[%d] = lt.f[%d];" % (d, k, k) for k in range(4) if mask_of(dest) & (8 >> k)))
        elif base == "SQ":
            s, _ = vf(ops[0])
            p1.append("la = %s; lt = v->vf[%d];" % (mem(ops[1]), s))
            p2.append(" ".join("v->mem[la].u[%d] = lt.u[%d];" % (k, k) for k in range(4) if mask_of(dest) & (8 >> k)))
        elif base in ("LQI", "SQI"):
            r = vi(re.search(r"vi\d+", ops[1]).group(0))
            n, _ = vf(ops[0])
            if base == "LQI":
                p1.append("lt = v->mem[v->vi[%d] & 255];" % r)
                p2.append(" ".join("v->vf[%d].f[%d] = lt.f[%d];" % (n, k, k) for k in range(4) if mask_of(dest) & (8 >> k) and n))
            else:
                p1.append("la = v->vi[%d] & 255; lt = v->vf[%d];" % (r, n))
                p2.append(" ".join("v->mem[la].u[%d] = lt.u[%d];" % (k, k) for k in range(4) if mask_of(dest) & (8 >> k)))
            p2.append("v->vi[%d] = vu_vi(v->vi[%d] + 1);" % (r, r))
        elif base in ("ILW", "ISW"):
            k = FIELDS.index(dest)
            r = vi(ops[0])
            if base == "ILW":
                p1.append("li = (int)(v->mem[%s].u[%d] & 0xFFFF);" % (mem(ops[1]), k))
                if r:
                    p2.append("v->vi[%d] = vu_vi(li);" % r)
            else:
                p1.append("la = %s; li = v->vi[%d] & 0xFFFF;" % (mem(ops[1]), r))
                p2.append("v->mem[la].u[%d] = (unsigned int)li;" % k)
        elif base in ("ILWR", "ISWR"):
            k = FIELDS.index(dest)
            r = vi(ops[0])
            b = vi(re.search(r"vi\d+", ops[1]).group(0))
            if base == "ILWR":
                p1.append("li = (int)(v->mem[v->vi[%d] & 255].u[%d] & 0xFFFF);" % (b, k))
                if r:
                    p2.append("v->vi[%d] = vu_vi(li);" % r)
            else:
                p1.append("la = v->vi[%d] & 255; li = v->vi[%d] & 0xFFFF;" % (b, r))
                p2.append("v->mem[la].u[%d] = (unsigned int)li;" % k)
        elif base in ("IADDIU", "IADDI", "ISUBIU"):
            d, s = vi(ops[0]), vi(ops[1])
            sign = "-" if base == "ISUBIU" else "+"
            p1.append("li = v->vi[%d] %s %s;" % (s, sign, self.imm(ops[2])))
            if d:
                p2.append("v->vi[%d] = vu_vi(li);" % d)
        elif base in ("IADD", "ISUB", "IAND", "IOR"):
            d, s, t = vi(ops[0]), vi(ops[1]), vi(ops[2])
            o = {"IADD": "+", "ISUB": "-", "IAND": "&", "IOR": "|"}[base]
            p1.append("li = v->vi[%d] %s v->vi[%d];" % (s, o, t))
            if d:
                p2.append("v->vi[%d] = vu_vi(li);" % d)
        elif base in ("MOVE", "MR32"):
            d, _ = vf(ops[0]); s, _ = vf(ops[1])
            if base == "MOVE":
                p1.append("lt = v->vf[%d];" % s)
            else:
                p1.append("lt.f[0] = v->vf[%d].f[1]; lt.f[1] = v->vf[%d].f[2]; lt.f[2] = v->vf[%d].f[3]; lt.f[3] = v->vf[%d].f[0];" % (s, s, s, s))
            if d:
                p2.append(" ".join("v->vf[%d].f[%d] = lt.f[%d];" % (d, k, k) for k in range(4) if mask_of(dest) & (8 >> k)))
        elif base in ("DIV", "SQRT", "RSQRT"):
            if base == "SQRT":
                t, tf = vf(ops[1])
                p1.append("lf = vu_f(sqrtf(fabsf(v->vf[%d].f[%d])));" % (t, FIELDS.index(tf)))
                p2.append("vu_set_q(v, lf, 7);")
            else:
                s, sf = vf(ops[1]); t, tf = vf(ops[2])
                fn = "vu_div" if base == "DIV" else "vu_rsqrt"
                p1.append("lf = %s(v->vf[%d].f[%d], v->vf[%d].f[%d]);" % (fn, s, FIELDS.index(sf), t, FIELDS.index(tf)))
                p2.append("vu_set_q(v, lf, %d);" % (7 if base == "DIV" else 13))
        elif base == "WAITQ":
            p2.append("vu_waitq(v);")
        elif base == "LOI":
            val = float(ops[0])
            p2.append("v->i = %r;" % val if False else "v->i = %sf;" % repr(val))
        elif base in ("FCAND", "FCOR", "FCEQ", "FCSET", "FCGET"):
            if base == "FCGET":
                d = vi(ops[0])
                p1.append("li = (int)(vu_clip_seen(v) & 0xFFF);")
                if d:
                    p2.append("v->vi[%d] = vu_vi(li);" % d)
            elif base == "FCSET":
                p2.append("v->clip = %s; vu_hist_push(v);" % self.imm(ops[0]))
            else:
                c = self.imm(ops[1])
                expr = {"FCAND": "((vu_clip_seen(v) & {c}) != 0)",
                        "FCOR": "(((vu_clip_seen(v) | {c}) & 0xFFFFFF) == 0xFFFFFF)",
                        "FCEQ": "((vu_clip_seen(v) & 0xFFFFFF) == {c})"}[base].format(c=c)
                p1.append("li = %s;" % expr)
                p2.append("v->vi[1] = li;")
        elif base in ("FMAND", "FMOR", "FMEQ"):
            d, s = vi(ops[0]), vi(ops[1])
            o = {"FMAND": "vu_mac_seen(v) & v->vi[%d]", "FMOR": "vu_mac_seen(v) | v->vi[%d]",
                 "FMEQ": "(vu_mac_seen(v) == (unsigned int)(v->vi[%d] & 0xFFFF))"}[base] % s
            p1.append("li = (int)(%s);" % o)
            if d:
                p2.append("v->vi[%d] = vu_vi(li);" % d)
        elif base in ("IBEQ", "IBNE"):
            a, b = vi(ops[0]), vi(ops[1])
            cmp = "==" if base == "IBEQ" else "!="
            br = "if (v->vi[%d] %s v->vi[%d]) { br = 1; br_target = %s; }" % (a, cmp, b, self.imm(ops[2]))
        elif base in ("IBLTZ", "IBGTZ", "IBLEZ", "IBGEZ"):
            a = vi(ops[0])
            cmp = {"IBLTZ": "<", "IBGTZ": ">", "IBLEZ": "<=", "IBGEZ": ">="}[base]
            br = "if (v->vi[%d] %s 0) { br = 1; br_target = %s; }" % (a, cmp, self.imm(ops[1]))
        elif base in ("B", "BAL"):
            if base == "BAL":
                r = vi(ops[0])
                p2.append("v->vi[%d] = %d;" % (r, idx + 2))
                tgt = ops[1]
            else:
                tgt = ops[0]
            br = "br = 1; br_target = %s;" % self.imm(tgt)
        elif base in ("JR", "JALR"):
            if base == "JALR":
                link, r = vi(ops[0]), vi(ops[1])
                p1.append("lj = v->vi[%d];" % r)
                p2.append("v->vi[%d] = %d;" % (link, idx + 2))
            else:
                r = vi(ops[0])
                p1.append("lj = v->vi[%d];" % r)
            br = "br = 1; br_target = lj;"
        else:
            raise ValueError("lower op " + text)
        return "\n".join(p1), "\n".join(p2), br


def main():
    labels, prog = parse(os.path.join(ROOT, "vsm/ps2_vu0.vsm"))
    g = Gen(labels)
    out = []
    out.append("/* GENERATED by pc/tools/vu_micro2c.py from vsm/ps2_vu0.vsm - do not edit. */")
    out.append('#include "pc_vu.h"\n')
    out.append("PcVu0 pcVu0 = { { { { 0, 0, 0, 1 } } } };\n")
    out.append("void pcVu0Micro(int addr)\n{")
    out.append("    PcVu0* v = &pcVu0;")
    out.append("    VuVec ut, lt;")
    out.append("    float uw = 0, lf = 0;")
    out.append("    int li = 0, la = 0, lj = 0;")
    out.append("    int br = 0, br_target = 0, end_after = 0;")
    out.append("    int guard = 0;\n")
    out.append("dispatch:")
    out.append("    if (++guard > 100000) return;")
    out.append("    switch (addr)\n    {")
    for i in range(len(prog)):
        out.append("    case %d: goto I%d;" % (i, i))
    out.append("    default: return;\n    }\n")
    for i, (up, lo, ebit) in enumerate(prog):
        u1, u2 = g.upper(up)
        l1, l2, brc = g.lower(lo, i)
        name = [k for k, vv in labels.items() if vv == i]
        out.append("I%d: /* %s | %s %s*/" % (i, up, lo, ("(" + ",".join(name) + ") ") if name else ""))
        out.append("    v->cycle++;")
        for part in (u1, l1, u2, l2):
            for line in part.split("\n"):
                if line.strip():
                    out.append("    " + line)
        out.append("    if (end_after) { v->vf[0].f[0] = 0; v->vf[0].f[1] = 0; v->vf[0].f[2] = 0; v->vf[0].f[3] = 1; v->vi[0] = 0; return; }")
        out.append("    if (br) { br = 0; addr = br_target; goto dispatch; }")
        if ebit:
            out.append("    end_after = 1;")
        if brc:
            out.append("    " + brc)
    out.append("    return;\n}")
    out.append("\n/* entry points (instruction index) */")
    for name, idx in sorted(labels.items(), key=lambda x: x[1]):
        out.append("/* %-36s %d */" % (name, idx))
    dst = os.path.join(ROOT, "pc/src/vu/vu0_micro.c")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "w") as f:
        f.write("\n".join(out) + "\n")
    print("wrote", dst, len(prog), "instructions")


if __name__ == "__main__":
    main()
