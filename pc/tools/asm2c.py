"""Translate EE + COP2 (VU0 macro mode) inline assembly blocks to C.

Used by the porting scripts: translate(src, start, end) returns C code that replaces the asm
statement at lines [start, end] of a srcedit.Source. Semantics follow the EE: 128-bit GPRs,
branch delay slots (MWCC inline assembly is literal, so the instruction after a branch is always
in its delay slot), VU0 registers in pcVu0 (pc_vu.h), microprogram calls via pcVu0Micro().
"""
import os, re, sys

sys.path.insert(0, os.path.dirname(__file__))
from vsm_parse import parse as parse_vsm

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
VU_LABELS, _ = parse_vsm(os.path.join(ROOT, "vsm/ps2_vu0.vsm"))

GPR_NAMES = {"zero", "at", "v0", "v1", "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "t0", "t1", "t2", "t3", "t4",
             "t5", "t6", "t7", "t8", "t9", "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7", "k0", "k1", "gp", "sp",
             "fp", "s8", "ra"}
FIELDS = "xyzw"
BRANCHES = {"beq", "bne", "beqz", "bnez", "blez", "bgtz", "bltz", "bgez", "b", "j", "beql", "bnel", "beqzl",
            "bnezl", "bc1t", "bc1f", "bc0t", "bc0f", "jr"}


class TranslateError(Exception):
    pass


def split_top(s, sep=","):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "([":
            depth += 1
        elif ch in ")]":
            depth -= 1
        if ch == sep and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur)
    return [x.strip() for x in out]


def float_params(sig):
    """names of float parameters in a C function signature, in order"""
    m = re.search(r"\((.*)\)", sig)
    names = []
    if not m:
        return names
    for p in split_top(m.group(1)):
        if re.match(r"(const\s+)?(float|Float)\b", p) and "*" not in p and "[" not in p:
            names.append(re.findall(r"(\w+)\s*$", p)[0])
    return names


class Block:
    def __init__(self, text_lines, func_sig, prefix):
        self.prefix = prefix
        self.fparams = float_params(func_sig)
        self.outputs, self.inputs = [], []
        self.code_lines = []
        self.gprs, self.fprs = set(), set()
        self.local_labels = set()
        self.parse(text_lines)

    # ---------------------------------------------------------------------------------------
    def parse(self, lines):
        text = "\n".join(lines)
        m = re.search(r"\basm\b[^{(]*", text)
        rest = text[m.end():].strip()
        if rest.startswith("{"):
            body = rest[1:rest.rindex("}")]
            self.code_lines = body.split("\n")
            return
        inner = rest[1:rest.rindex(")")]
        if '"' not in inner:
            self.code_lines = [inner]
            return
        # string part, then constraints
        q1 = inner.index('"')
        q2 = inner.index('"', q1 + 1)
        strpart = inner[q1 + 1:q2]
        # adjacent literals "..." "..." -> join
        strpart = re.sub(r'"\s*\n\s*"', "\n", strpart)
        strpart = strpart.replace('"', "")
        self.code_lines = re.split(r"\n|;(?![^(]*\))", strpart.replace("\\n", "\n").replace("\\t", " "))
        cons = inner[q2 + 1:]
        parts = cons.split(":")
        outs = parts[1] if len(parts) > 1 else ""
        ins = parts[2] if len(parts) > 2 else ""
        for p in split_top(outs):
            mm = re.match(r'"([=+&]*)(\w+)"\s*\((.*)\)$', p, re.S)
            if mm:
                self.outputs.append((mm.group(2), mm.group(3).strip()))
        for p in split_top(ins):
            mm = re.match(r'"(\w+)"\s*\((.*)\)$', p, re.S)
            if mm:
                self.inputs.append((mm.group(1), mm.group(2).strip()))

    # ---------------------------------------------------------------------------------------
    def operand_kind(self, n):
        ops = self.outputs + self.inputs
        return ops[n][0], ops[n][1]

    def gpr(self, tok):
        """C lvalue (PcGpr) for a GPR operand, or None"""
        tok = tok.strip()
        m = re.match(r"^%(\d+)$", tok)
        if m:
            self.operand_kind(int(m.group(1)))
            self.gprs.add("op" + m.group(1))
            return "g_op" + m.group(1)
        t = tok.lstrip("$")
        if t in GPR_NAMES:
            if t == "zero":
                return "g_zero"
            self.gprs.add(t)
            return "g_" + t
        m = re.match(r"^\$(\d+)$", tok)
        if m:
            self.gprs.add("r" + m.group(1))
            return "g_r" + m.group(1)
        return None

    def gval(self, tok):
        """64-bit value of a GPR operand or C identifier"""
        r = self.gpr(tok)
        if r is not None:
            return r + ".d[0]"
        return "A2C_SX32((uint32_t)(uintptr_t)(%s))" % tok.strip()

    def gw(self, tok):
        r = self.gpr(tok)
        if r is not None:
            return r + ".w[0]"
        return "((uint32_t)(uintptr_t)(%s))" % tok.strip()

    def set_gpr(self, tok, expr64):
        r = self.gpr(tok)
        if r is not None:
            if r == "g_zero":
                return "(void)(%s);" % expr64
            return "%s.d[0] = %s;" % (r, expr64)
        return "*(uint32_t*)&(%s) = (uint32_t)(%s);" % (tok.strip(), expr64)

    def fpr(self, tok):
        tok = tok.strip()
        m = re.match(r"^%(\d+)$", tok)
        if m:
            self.operand_kind(int(m.group(1)))
            self.fprs.add("op" + m.group(1))
            return "fr_op" + m.group(1)
        m = re.match(r"^\$?f(\d+)$", tok)
        if m:
            self.fprs.add(m.group(1))
            return "fr_" + m.group(1)
        return None

    def addr(self, tok):
        """'off(base)' or a global symbol -> uint32 address expression"""
        tok = tok.strip()
        m = re.match(r"^(.*)\((.*)\)$", tok)
        if not m:
            return "((uint32_t)(uintptr_t)&(%s))" % tok
        off, base = m.group(1).strip(), m.group(2).strip()
        if off == "":
            o = "0"
        elif re.match(r"^-?(0x[0-9a-fA-F]+|\d+)$", off):
            o = off
        elif re.match(r"^[A-Za-z_]\w*\.[A-Za-z_][\w.\[\]]*$", off):
            t, f = off.split(".", 1)
            o = "offsetof(%s, %s)" % (t, f)
        else:
            o = off
        return "((uint32_t)(%s + (%s)))" % (self.gw(base), o)

    # ---------------------------------------------------------------------------------------
    @staticmethod
    def vfreg(tok):
        """'$vf5xyz' -> (5, 'xyz'), 'ACC' -> ('ACC', ''), 'Q'/'I' -> ..."""
        t = tok.strip().lstrip("$")
        if t.upper() in ("ACC", "ACCXYZW"):
            return "ACC", ""
        if t.upper() == "Q":
            return "Q", ""
        if t.upper() == "I":
            return "I", ""
        m = re.match(r"^vf(\d+)([xyzw]*)$", t)
        if not m:
            raise TranslateError("bad vf operand " + tok)
        return int(m.group(1)), m.group(2)

    @staticmethod
    def vireg(tok):
        t = tok.strip().lstrip("$")
        m = re.match(r"^vi(\d+)$", t)
        if not m:
            raise TranslateError("bad vi operand " + tok)
        return int(m.group(1))

    def vu_op(self, mn, ops):
        """COP2 macro instruction -> C"""
        V = "pcVu0"
        if mn in ("vnop", "vwaitq"):
            return "vu_waitq(&pcVu0);" if mn == "vwaitq" else ""
        if mn == "vcallms":
            t = ops[0].strip()
            if t in VU_LABELS:
                return "pcVu0Micro(%d); /* %s */" % (VU_LABELS[t], t)
            return "pcVu0Micro((%s) / 8);" % t
        if mn in ("vdiv", "vrsqrt"):
            s, sf = self.vfreg(ops[1]); t, tf = self.vfreg(ops[2])
            sf, tf = sf or "x", tf or "x"
            fn = "vu_div" if mn == "vdiv" else "vu_rsqrt"
            return "vu_set_q_now(&pcVu0, %s(%s.vf[%d].f[%d], %s.vf[%d].f[%d]));" % (
                fn, V, s, FIELDS.index(sf), V, t, FIELDS.index(tf))
        if mn == "vsqrt":
            t, tf = self.vfreg(ops[1])
            return "vu_set_q_now(&pcVu0, vu_f(sqrtf(fabsf(%s.vf[%d].f[%d]))));" % (V, t, FIELDS.index(tf))
        if mn in ("viaddi", "viadd", "vior", "viand", "visub"):
            d = self.vireg(ops[0]); s = self.vireg(ops[1])
            if mn == "viaddi":
                e = "%s.vi[%d] + (%s)" % (V, s, ops[2].strip())
            else:
                t = self.vireg(ops[2])
                o = {"viadd": "+", "vior": "|", "viand": "&", "visub": "-"}[mn]
                e = "%s.vi[%d] %s %s.vi[%d]" % (V, s, o, V, t)
            return "" if d == 0 else "%s.vi[%d] = vu_vi(%s);" % (V, d, e)
        if mn == "vmtir":
            d = self.vireg(ops[0]); s, sf = self.vfreg(ops[1])
            return "" if d == 0 else "%s.vi[%d] = vu_vi((int)(%s.vf[%d].u[%d] & 0xFFFF));" % (V, d, V, s, FIELDS.index(sf))

        m = re.match(r"^v([a-z0-9]+)\.([xyzw]+)$", mn)
        if not m:
            raise TranslateError("COP2 op " + mn)
        name, dest = m.group(1), m.group(2)
        known = ("add", "adda", "sub", "suba", "mul", "mula", "madd", "madda", "msub", "msuba", "max", "mini",
                 "move", "mr32", "abs", "mfir", "opmula", "opmsub", "ftoi0", "ftoi4", "ftoi12", "ftoi15",
                 "itof0", "itof4", "itof12", "itof15")
        if name in known:
            base, bc = name, None
        elif name[:-1] in known and name[-1] in "xyzwqi":
            base, bc = name[:-1], name[-1]
        elif name == "clipw":
            base, bc = "clip", "w"
        else:
            raise TranslateError("COP2 op " + mn)
        if base == "clip" and bc == "w":
            s, _ = self.vfreg(ops[0]); t, tf = self.vfreg(ops[1])
            return "vu_clip(&pcVu0, &pcVu0.vf[%d], pcVu0.vf[%d].f[3]);" % (s, t)
        mask = [k for k in range(4) if FIELDS[k] in dest]
        lines = ["{ VuVec r_ = %s.vf[0];" % V]

        def store(d, setmac):
            out = []
            if d == "ACC":
                out += ["%s.acc.f[%d] = r_.f[%d];" % (V, k, k) for k in mask]
            elif d != 0:
                out += ["%s.vf[%d].f[%d] = r_.f[%d];" % (V, d, k, k) for k in mask]
            if setmac:
                out.append("vu_set_mac(&pcVu0, &r_, %d);" % sum(8 >> k for k in mask))
            return out

        if base in ("move", "mr32", "abs", "ftoi0", "ftoi4", "ftoi12", "ftoi15", "itof0", "itof4", "itof12", "itof15", "mfir"):
            d, _ = self.vfreg(ops[0])
            if base == "mfir":
                s = self.vireg(ops[1])
                for k in mask:
                    lines.append("r_.i[%d] = %s.vi[%d];" % (k, V, s))
                lines += store(d, False)
                return " ".join(lines) + " }"
            s, _ = self.vfreg(ops[1])
            for k in mask:
                if base == "move":
                    e = "%s.vf[%d].u[%d]" % (V, s, k)
                    lines.append("r_.u[%d] = %s;" % (k, e))
                elif base == "mr32":
                    lines.append("r_.u[%d] = %s.vf[%d].u[%d];" % (k, V, s, (k + 1) % 4))
                elif base == "abs":
                    lines.append("r_.f[%d] = fabsf(%s.vf[%d].f[%d]);" % (k, V, s, k))
                elif base.startswith("ftoi"):
                    sc = {"0": "1.0f", "4": "16.0f", "12": "4096.0f", "15": "32768.0f"}[base[4:]]
                    lines.append("r_.i[%d] = vu_ftoi(%s.vf[%d].f[%d], %s);" % (k, V, s, k, sc))
                else:
                    sc = {"0": "1.0f", "4": "16.0f", "12": "4096.0f", "15": "32768.0f"}[base[4:]]
                    lines.append("r_.f[%d] = (float)%s.vf[%d].i[%d] / %s;" % (k, V, s, k, sc))
            lines += store(d, False)
            return " ".join(lines) + " }"

        if base in ("opmula", "opmsub"):
            d, _ = self.vfreg(ops[0]); s, _ = self.vfreg(ops[1]); t, _ = self.vfreg(ops[2])
            sel = [(1, 2), (2, 0), (0, 1)]
            for k in range(3):
                a, b = sel[k]
                if base == "opmula":
                    lines.append("r_.f[%d] = vu_f(%s.vf[%d].f[%d] * %s.vf[%d].f[%d]);" % (k, V, s, a, V, t, b))
                else:
                    lines.append("r_.f[%d] = vu_f(%s.acc.f[%d] - %s.vf[%d].f[%d] * %s.vf[%d].f[%d]);" % (k, V, k, V, s, a, V, t, b))
            mask = [0, 1, 2]
            lines += store("ACC" if base == "opmula" else d, True)
            return " ".join(lines) + " }"

        exprs = {
            "add": "{a} + {b}", "adda": "{a} + {b}", "sub": "{a} - {b}", "suba": "{a} - {b}",
            "mul": "{a} * {b}", "mula": "{a} * {b}",
            "madd": "{acc} + {a} * {b}", "madda": "{acc} + {a} * {b}",
            "msub": "{acc} - {a} * {b}", "msuba": "{acc} - {a} * {b}",
            "max": "fmaxf({a}, {b})", "mini": "fminf({a}, {b})",
        }
        if base not in exprs:
            raise TranslateError("COP2 op " + mn)
        d, _ = self.vfreg(ops[0])
        s, _ = self.vfreg(ops[1])
        for k in mask:
            a = "%s.vf[%d].f[%d]" % (V, s, k)
            if bc == "q":
                b = "vu_q(&pcVu0)"
            elif bc == "i":
                b = "%s.i" % V
            else:
                t, tf = self.vfreg(ops[2])
                if bc:
                    b = "%s.vf[%d].f[%d]" % (V, t, FIELDS.index(bc))
                else:
                    b = "%s.vf[%d].f[%d]" % (V, t, k)
            e = exprs[base].format(a=a, b=b, acc="%s.acc.f[%d]" % (V, k))
            lines.append("r_.f[%d] = vu_f(%s);" % (k, e))
        accdest = base in ("adda", "suba", "mula", "madda", "msuba")
        lines += store("ACC" if accdest else d, base not in ("max", "mini"))
        return " ".join(lines) + " }"

    # ---------------------------------------------------------------------------------------
    def cond(self, mn, ops):
        if mn in ("beq", "beql"):
            return "(%s == %s)" % (self.gval(ops[0]), self.gval(ops[1]))
        if mn in ("bne", "bnel"):
            return "(%s != %s)" % (self.gval(ops[0]), self.gval(ops[1]))
        if mn in ("beqz", "beqzl"):
            return "(%s == 0)" % self.gval(ops[0])
        if mn in ("bnez", "bnezl"):
            return "(%s != 0)" % self.gval(ops[0])
        if mn == "blez":
            return "((int64_t)%s <= 0)" % self.gval(ops[0])
        if mn == "bgtz":
            return "((int64_t)%s > 0)" % self.gval(ops[0])
        if mn == "bltz":
            return "((int64_t)%s < 0)" % self.gval(ops[0])
        if mn == "bgez":
            return "((int64_t)%s >= 0)" % self.gval(ops[0])
        if mn in ("b", "j"):
            return "1"
        if mn == "bc1t":
            return "(fcc_ != 0)"
        if mn == "bc1f":
            return "(fcc_ == 0)"
        if mn == "bc0t":
            return "1"
        if mn == "bc0f":
            return "0"
        raise TranslateError("branch " + mn)

    def instr(self, mn, ops):
        """non-branch instruction -> C"""
        g, gv, gw, S = self.gpr, self.gval, self.gw, self.set_gpr
        if mn in ("nop", "ssnop", "sync", "sync.l", "sync.p", "ei", "di"):
            return ""
        if mn in ("addu", "add", "addiu", "addi", "subu", "sub", "daddiu", "daddu", "and", "or", "andi", "ori") and len(ops) == 2:
            ops = [ops[0], ops[0], ops[1]]      # two-operand assembler shorthand
        if mn in ("addu", "add", "addiu", "addi"):
            return S(ops[0], "A2C_SX32(%s + (uint32_t)(%s))" % (gw(ops[1]), self.imm_or_w(ops[2])))
        if mn in ("daddu", "dadd", "daddiu", "daddi"):
            return S(ops[0], "(%s + (uint64_t)(int64_t)(%s))" % (gv(ops[1]), self.imm_or_d(ops[2])))
        if mn in ("subu", "sub"):
            return S(ops[0], "A2C_SX32(%s - %s)" % (gw(ops[1]), gw(ops[2])))
        if mn in ("dsubu", "dsub"):
            return S(ops[0], "(%s - %s)" % (gv(ops[1]), gv(ops[2])))
        if mn in ("and", "or", "xor", "nor"):
            o = {"and": "&", "or": "|", "xor": "^", "nor": "|"}[mn]
            e = "(%s %s %s)" % (gv(ops[1]), o, gv(ops[2]))
            return S(ops[0], ("~" + e) if mn == "nor" else e)
        if mn in ("andi", "ori", "xori"):
            o = {"andi": "&", "ori": "|", "xori": "^"}[mn]
            return S(ops[0], "(%s %s (uint64_t)(uint16_t)(%s))" % (gv(ops[1]), o, ops[2]))
        if mn == "lui":
            return S(ops[0], "A2C_SX32((uint32_t)(%s) << 16)" % ops[1])
        if mn == "li":
            return S(ops[0], "(uint64_t)(int64_t)(int32_t)(%s)" % ops[1])
        if mn == "move":
            return S(ops[0], gv(ops[1]))
        if mn == "neg":
            return S(ops[0], "A2C_SX32(0u - %s)" % gw(ops[1]))
        if mn in ("sll", "srl", "sra"):
            if mn == "sll":
                e = "A2C_SX32(%s << (%s))" % (gw(ops[1]), ops[2])
            elif mn == "srl":
                e = "A2C_SX32(%s >> (%s))" % (gw(ops[1]), ops[2])
            else:
                e = "A2C_SX32((uint32_t)((int32_t)%s >> (%s)))" % (gw(ops[1]), ops[2])
            return S(ops[0], e)
        if mn in ("dsll", "dsrl", "dsra", "dsll32", "dsrl32", "dsra32"):
            sh = ops[2] if not mn.endswith("32") else "(32 + (%s))" % ops[2]
            if mn.startswith("dsll"):
                e = "(%s << %s)" % (gv(ops[1]), sh)
            elif mn.startswith("dsrl"):
                e = "(%s >> %s)" % (gv(ops[1]), sh)
            else:
                e = "(uint64_t)((int64_t)%s >> %s)" % (gv(ops[1]), sh)
            return S(ops[0], e)
        if mn in ("slt", "sltu", "slti", "sltiu"):
            a = gv(ops[1])
            b = gv(ops[2]) if mn in ("slt", "sltu") else "(uint64_t)(int64_t)(%s)" % ops[2]
            if mn in ("slt", "slti"):
                return S(ops[0], "((int64_t)%s < (int64_t)%s)" % (a, b))
            return S(ops[0], "(%s < %s)" % (a, b))
        if mn in ("mult", "multu"):
            if len(ops) == 3:
                e = "A2C_SX32((uint32_t)((int64_t)(int32_t)%s * (int32_t)%s))" % (gw(ops[1]), gw(ops[2]))
                return "lo_ = %s; %s" % (e, S(ops[0], "lo_"))
            return "lo_ = A2C_SX32((uint32_t)((int64_t)(int32_t)%s * (int32_t)%s));" % (gw(ops[0]), gw(ops[1]))
        if mn == "muli":
            e = "A2C_SX32((uint32_t)((int32_t)%s * (int32_t)(%s)))" % (gw(ops[1]), ops[2])
            return "lo_ = %s; %s" % (e, S(ops[0], "lo_"))
        if mn == "mflo":
            return S(ops[0], "lo_")
        if mn in ("movz", "movn"):
            c = "==" if mn == "movz" else "!="
            r = g(ops[0])
            return "if (%s %s 0) { %s }" % (gv(ops[2]), c, S(ops[0], gv(ops[1])))
        # loads / stores
        if mn in ("lw", "lwu", "lh", "lhu", "lb", "lbu", "ld", "lq"):
            a = self.addr(ops[1])
            e = {"lw": "A2C_SX32(*(uint32_t*)A2C_PTR({a}))", "lwu": "(uint64_t)*(uint32_t*)A2C_PTR({a})",
                 "lh": "(uint64_t)(int64_t)*(int16_t*)A2C_PTR({a})", "lhu": "(uint64_t)*(uint16_t*)A2C_PTR({a})",
                 "lb": "(uint64_t)(int64_t)*(int8_t*)A2C_PTR({a})", "lbu": "(uint64_t)*(uint8_t*)A2C_PTR({a})",
                 "ld": "a2c_ld64({a})"}.get(mn)
            if mn == "lq":
                r = g(ops[0])
                # LQ/SQ ignore the low 4 address bits on the EE; the PS2 data is always 16-byte
                # aligned, but MSVC drops the alignment attributes, so use the exact address.
                return "memcpy(&%s, A2C_PTR(%s), 16);" % (r, a)
            return S(ops[0], e.format(a=a))
        if mn in ("sw", "sh", "sb", "sd", "sq"):
            a = self.addr(ops[1])
            if mn == "sq":
                return "memcpy(A2C_PTR(%s), &%s, 16);" % (a, g(ops[0]) or "g_zero")
            t = {"sw": "uint32_t", "sh": "uint16_t", "sb": "uint8_t", "sd": "uint64_t"}[mn]
            if mn == "sd":
                return "a2c_sd64(%s, %s);" % (a, gv(ops[0]))
            return "*(%s*)A2C_PTR(%s) = (%s)%s;" % (t, a, t, gv(ops[0]))
        if mn in ("ldl", "ldr"):
            r = g(ops[0])
            return "%s.d[0] = a2c_%s(%s.d[0], %s);" % (r, mn, r, self.addr(ops[1]))
        if mn in ("sdl", "sdr"):
            return "a2c_%s(%s, %s);" % (mn, gv(ops[0]), self.addr(ops[1]))
        # MMI
        if mn in ("pextlw", "pextuw", "pextlh", "pcpyld", "pcpyud", "paddub", "prot3w", "plzcw", "por", "pand", "pnor", "pxor"):
            d = g(ops[0])
            s = g(ops[1]) if len(ops) > 1 else None
            t = g(ops[2]) if len(ops) > 2 else None
            if mn == "pextlw":
                return "{ PcGpr r_; r_.w[0] = %s.w[0]; r_.w[1] = %s.w[0]; r_.w[2] = %s.w[1]; r_.w[3] = %s.w[1]; %s = r_; }" % (t, s, t, s, d)
            if mn == "pextuw":
                return "{ PcGpr r_; r_.w[0] = %s.w[2]; r_.w[1] = %s.w[2]; r_.w[2] = %s.w[3]; r_.w[3] = %s.w[3]; %s = r_; }" % (t, s, t, s, d)
            if mn == "pextlh":
                return "{ PcGpr r_; int k_; for (k_ = 0; k_ < 4; k_++) { r_.h[k_ * 2] = %s.h[k_]; r_.h[k_ * 2 + 1] = %s.h[k_]; } %s = r_; }" % (t, s, d)
            if mn == "pcpyld":
                return "{ PcGpr r_; r_.d[0] = %s.d[0]; r_.d[1] = %s.d[0]; %s = r_; }" % (t, s, d)
            if mn == "pcpyud":
                return "{ PcGpr r_; r_.d[0] = %s.d[1]; r_.d[1] = %s.d[1]; %s = r_; }" % (s, t, d)
            if mn == "paddub":
                return "{ PcGpr r_; int k_; for (k_ = 0; k_ < 16; k_++) { unsigned v_ = %s.b[k_] + %s.b[k_]; r_.b[k_] = (uint8_t)(v_ > 255 ? 255 : v_); } %s = r_; }" % (s, t, d)
            if mn == "prot3w":
                return "{ PcGpr r_ = %s; r_.w[0] = %s.w[1]; r_.w[1] = %s.w[2]; r_.w[2] = %s.w[0]; %s = r_; }" % (s, s, s, s, d)
            if mn == "plzcw":
                return "{ PcGpr r_ = %s; r_.w[0] = a2c_plzcw1(%s.w[0]); r_.w[1] = a2c_plzcw1(%s.w[1]); %s.d[0] = r_.d[0]; }" % (d, s, s, d)
            o = {"por": "|", "pand": "&", "pxor": "^", "pnor": "|"}[mn]
            n = "~" if mn == "pnor" else ""
            return "%s.d[0] = %s(%s.d[0] %s %s.d[0]); %s.d[1] = %s(%s.d[1] %s %s.d[1]);" % (d, n, s, o, t, d, n, s, o, t)
        # FPU
        if mn in ("lwc1", "swc1"):
            f = self.fpr(ops[0])
            a = self.addr(ops[1])
            if mn == "lwc1":
                return "%s.u = *(uint32_t*)A2C_PTR(%s);" % (f, a)
            return "*(uint32_t*)A2C_PTR(%s) = %s.u;" % (a, f)
        if mn == "mfc1":
            f = self.fpr(ops[1])
            if f is None:   # C float variable
                return S(ops[0], "A2C_SX32(a2c_f_bits(%s))" % ops[1].strip())
            return S(ops[0], "A2C_SX32(%s.u)" % f)
        if mn == "mtc1":
            f = self.fpr(ops[1])
            if f is None:
                return "*(uint32_t*)&(%s) = %s;" % (ops[1].strip(), gw(ops[0]))
            return "%s.u = %s;" % (f, gw(ops[0]))
        if mn in ("add.s", "sub.s", "mul.s", "div.s"):
            o = {"add.s": "+", "sub.s": "-", "mul.s": "*", "div.s": "/"}[mn]
            return "%s.f = %s.f %s %s.f;" % (self.fpr(ops[0]), self.fpr(ops[1]), o, self.fpr(ops[2]))
        if mn in ("neg.s", "abs.s", "mov.s", "sqrt.s"):
            e = {"neg.s": "-{s}.f", "abs.s": "fabsf({s}.f)", "mov.s": "{s}.f", "sqrt.s": "sqrtf(fabsf({s}.f))"}[mn]
            return "%s.f = %s;" % (self.fpr(ops[0]), e.format(s=self.fpr(ops[1])))
        if mn == "cvt.w.s":
            return "%s.i = (int32_t)%s.f;" % (self.fpr(ops[0]), self.fpr(ops[1]))
        if mn == "cvt.s.w":
            return "%s.f = (float)%s.i;" % (self.fpr(ops[0]), self.fpr(ops[1]))
        if mn in ("c.lt.s", "c.le.s", "c.eq.s"):
            o = {"c.lt.s": "<", "c.le.s": "<=", "c.eq.s": "=="}[mn]
            return "fcc_ = (%s.f %s %s.f);" % (self.fpr(ops[0]), o, self.fpr(ops[1]))
        # COP2 transfers
        if mn in ("qmtc2", "qmtc2.i", "qmtc2.ni"):
            v, _ = self.vfreg(ops[1])
            return "" if v == 0 else "memcpy(&pcVu0.vf[%d], &%s, 16);" % (v, g(ops[0]) or "g_zero")
        if mn in ("qmfc2", "qmfc2.i", "qmfc2.ni"):
            v, _ = self.vfreg(ops[1])
            return "memcpy(&%s, &pcVu0.vf[%d], 16);" % (g(ops[0]), v)
        if mn in ("ctc2", "ctc2.i", "ctc2.ni"):
            return "a2c_ctc2(%d, %s);" % (self.vireg(ops[1]), gw(ops[0]))
        if mn in ("cfc2", "cfc2.i", "cfc2.ni"):
            return S(ops[0], "A2C_SX32(a2c_cfc2(%d))" % self.vireg(ops[1]))
        if mn in ("lqc2", "sqc2"):
            v, _ = self.vfreg(ops[0])
            a = self.addr(ops[1])
            if mn == "lqc2":
                return "" if v == 0 else "memcpy(&pcVu0.vf[%d], A2C_PTR(%s), 16);" % (v, a)
            return "memcpy(A2C_PTR(%s), &pcVu0.vf[%d], 16);" % (a, v)
        if mn.startswith("v"):
            return self.vu_op(mn, ops)
        raise TranslateError("instruction " + mn)

    @staticmethod
    def cimm(tok):
        """immediate expression: TYPE.field -> offsetof(TYPE, field)"""
        return re.sub(r"\b([A-Za-z_]\w*)\.([A-Za-z_][\w.\[\]]*)", r"offsetof(\1, \2)", tok.strip())

    def imm_or_w(self, tok):
        tok = tok.strip()
        if self.gpr(tok) is not None:
            return self.gw(tok)
        return "(int32_t)(%s)" % self.cimm(tok)

    def imm_or_d(self, tok):
        tok = tok.strip()
        if self.gpr(tok) is not None:
            return self.gv(tok)
        return "(int64_t)(%s)" % self.cimm(tok)

    # ---------------------------------------------------------------------------------------
    def translate(self):
        insns = []      # (label or None, mnemonic, ops, noreorder)
        noreorder = False
        for raw in self.code_lines:
            line = re.sub(r"//.*|#.*", "", raw).strip()
            if line.startswith(".set"):
                if "noreorder" in line:
                    noreorder = True
                elif "reorder" in line:
                    noreorder = False
                continue
            if not line or line.startswith("."):
                continue
            while True:
                m = re.match(r"^([A-Za-z_]\w*):\s*(.*)$", line)
                if not m:
                    break
                insns.append((m.group(1), None, None, noreorder))
                self.local_labels.add(m.group(1))
                line = m.group(2).strip()
            if not line:
                continue
            mn, _, rest = line.partition(" ")
            insns.append((None, mn.strip().lower() if not mn.startswith("v") else mn.strip(), split_top(rest), noreorder))

        out = []
        i = 0
        while i < len(insns):
            lab, mn, ops, nr = insns[i]
            if lab is not None:
                out.append("%s_%s:;" % (self.prefix, lab))
                i += 1
                continue
            mnl = mn.lower()
            if mnl in BRANCHES:
                if mnl == "jr":
                    raise TranslateError("jr inside asm")
                target = ops[-1].strip()
                c = self.cond(mnl, ops)
                # delay slot: only literal in ".set noreorder" code; otherwise the assembler
                # filled it (nop or an earlier instruction), i.e. program order is kept
                delay = ""
                if nr and i + 1 < len(insns) and insns[i + 1][0] is None:
                    _, dmn, dops, _ = insns[i + 1]
                    delay = self.instr(dmn.lower() if not dmn.startswith("v") else dmn, dops)
                    i += 1
                likely = mnl in ("beql", "bnel", "beqzl", "bnezl")
                if target in self.local_labels or any(l == target for l, _, _, _ in insns if l):
                    jump = "goto %s_%s;" % (self.prefix, target)
                else:
                    jump = "{ __OUTPUTS__ goto %s; }" % target
                if likely:
                    out.append("if %s { %s %s }" % (c, delay, jump))
                else:
                    out.append("{ int c_ = %s; %s if (c_) %s }" % (c, delay, jump))
                i += 1
                continue
            out.append(self.instr(mnl if not mn.startswith("v") else mn, ops))
            i += 1

        # declarations and operand binding
        head, tail = ["{"], []
        for r in sorted(self.gprs):
            head.append("    PcGpr g_%s = { 0 };" % r)
        head.append("    PcGpr g_zero = { 0 };")
        for r in sorted(self.fprs):
            head.append("    union { float f; uint32_t u; int32_t i; } fr_%s = { 0 };" % r)
        head.append("    uint64_t lo_ = 0; int fcc_ = 0;")
        ops = self.outputs + self.inputs
        for n, (kind, expr) in enumerate(ops):
            if ("op%d" % n) in self.fprs:
                head.append("    fr_op%d.f = (float)(%s);" % (n, expr))
            if ("op%d" % n) in self.gprs:
                head.append("    g_op%d.d[0] = A2C_SX32((uint32_t)(uintptr_t)(%s));" % (n, expr))
        # f12.. carry the function's float arguments (MIPS EABI)
        for k, name in enumerate(self.fparams[:8]):
            if str(12 + k) in self.fprs:
                head.append("    fr_%d.f = %s;" % (12 + k, name))
        outs = []
        for n, (kind, expr) in enumerate(self.outputs):
            if ("op%d" % n) in self.fprs:
                outs.append("%s = fr_op%d.f;" % (expr, n))
            else:
                outs.append("*(uint32_t*)&(%s) = g_op%d.w[0];" % (expr, n))
        body = []
        for l in out:
            if l:
                body.append("    " + l.replace("__OUTPUTS__", " ".join(outs)))
        tail = ["    " + o for o in outs] + ["    (void)g_zero; (void)lo_; (void)fcc_;", "}"]
        return "\n".join(head + body + tail)


def translate(src, start, end, prefix):
    """C replacement for the asm statement at src.lines[start..end]."""
    sig = ""
    for i in range(start, -1, -1):
        if re.match(r"^[A-Za-z].*\(.*\)\s*$", src.lines[i]):
            sig = src.lines[i]
            break
    b = Block(src.lines[start:end + 1], sig, prefix)
    code = b.translate()
    # function ends right after the asm and returns a value: the MIPS return register v0
    nxt = next((l.strip() for l in src.lines[end + 1:] if l.strip()), "")
    rtype = sig.split("(")[0].rsplit(None, 1)[0].strip() if sig else "void"
    if nxt == "}" and rtype not in ("void", "static void") and "v0" in b.gprs:
        code = code[:code.rindex("}")] + "    return (%s)g_v0.w[0];" % rtype + chr(10) + "}"
    return code
