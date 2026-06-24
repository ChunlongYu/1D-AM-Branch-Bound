# -*- coding: utf-8 -*-
"""
Derive identical-parallel-machine instances from Yu et al. (2022) heterogeneous
instances:
  - take the LARGER machine (max platform area L*W) as the single identical machine,
  - keep all parts unchanged, using orientation 1 (as listed), volume only
    (support ignored), one part per expanded copy,
  - keep the due dates unchanged.
Output is written in the same simplified field convention our solver reads
(== the data/ files): one machine type, N expanded single-orientation parts,
a 'DueDate' line.
"""
import os, glob

def tokens(path):
    return open(path, encoding="latin-1").read().split()

def parse_yu(path):
    t = tokens(path); i = 0
    def gi():
        nonlocal i; v = int(float(t[i])); i += 1; return v
    def gf():
        nonlocal i; v = float(t[i]); i += 1; return v
    tm = gi(); tp = gi()         # types_machine, types_parts
    nm = gi(); npart = gi()      # num_machine, num_parts
    machines = []
    for _ in range(tm):
        idx = gi(); cnt = gi()
        V = gf(); U = gf(); S = gf(); L = gf(); W = gf(); H = gf()
        machines.append(dict(cnt=cnt, V=V, U=U, S=S, L=L, W=W, H=H, area=L*W))
    parts = []   # each: (vol, l, w, h, sup) for orientation 1, expanded
    for _ in range(tp):
        idx = gi(); num_pt = gi(); num_ori = gi(); vol = gf()
        ors = []
        for _o in range(num_ori):
            l = gf(); w = gf(); h = gf(); s = gf(); ors.append((l, w, h, s))
        l0, w0, h0, s0 = ors[0]                    # orientation 1
        for _c in range(num_pt):
            parts.append((vol, l0, w0, h0, s0))
    assert len(parts) == npart, f"{path}: expanded {len(parts)} != num_parts {npart}"
    due = [gf() for _ in range(npart)]
    return machines, parts, due

def write_ours(path, mac, parts, due):
    N = len(parts)
    lines = []
    lines.append(f"1 {N}")
    lines.append(f"1 {N}")
    lines.append("")
    lines.append(f"1 1 {mac['V']:g} {mac['U']:g} {mac['S']:g} {mac['L']:g} {mac['W']:g} {mac['H']:g}")
    lines.append("")
    for j, (vol, l, w, h, s) in enumerate(parts, start=1):
        lines.append(f"{j} 1 1 {vol:g}")
        lines.append(f"{l:g} {w:g} {h:g} {s:g}")
        lines.append("")
    lines.append("DueDate")
    lines.append(" ".join(f"{d:g}" for d in due))
    open(path, "w").write("\n".join(lines) + "\n")

SRC = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Yu et al., 2022/TestInstances"
DST = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Derived_Yu2022_identical"
os.makedirs(DST, exist_ok=True)

rows = []
for fp in sorted(glob.glob(os.path.join(SRC, "*.txt"))):
    name = os.path.basename(fp)
    if name == "readme.txt":
        continue
    machines, parts, due = parse_yu(fp)
    big = max(machines, key=lambda m: m["area"])
    write_ours(os.path.join(DST, name), big, parts, due)
    rows.append((name, len(parts), big["L"], big["W"], big["U"], big["S"]))

print(f"generated {len(rows)} instances -> {DST}")
for name, N, L, W, U, S in rows:
    print(f"  {name:32s} n={N:2d}  machine {L:g}x{W:g}  U={U:g} S={S:g}")
