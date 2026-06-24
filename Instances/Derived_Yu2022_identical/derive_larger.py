# -*- coding: utf-8 -*-
"""
Derive identical-machine instances from Yu et al. (2022) LargerInstances (n=50),
which ship WITHOUT due dates. Due dates are generated with Yu's own
GenerateDueDate (imported from their InstanceData.py) for the four (TF,RDD)
combinations, reusing the TestInstances seed convention (TF=0.3->seed 1,
TF=0.6->seed 3). The machine = the larger platform of the source; parts use
orientation 1, volume only (support ignored) -- same rule as the TestInstances
derivation.
"""
import os, glob, importlib.util

YU = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Yu et al., 2022"
SRC = os.path.join(YU, "LargerInstances")
DST = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Derived_Yu2022_identical"

# import Yu's exact due-date generator
spec = importlib.util.spec_from_file_location("yuID", os.path.join(YU, "SupFiles", "InstanceData.py"))
yu = importlib.util.module_from_spec(spec); spec.loader.exec_module(yu)

def parse_yu(path):
    t = open(path, encoding="latin-1").read().split(); i = 0
    def gi():
        nonlocal i; v = int(float(t[i])); i += 1; return v
    def gf():
        nonlocal i; v = float(t[i]); i += 1; return v
    tm = gi(); tp = gi(); nm = gi(); npart = gi()
    machines = []
    for _ in range(tm):
        gi(); cnt = gi(); V = gf(); U = gf(); S = gf(); L = gf(); W = gf(); H = gf()
        machines.append(dict(V=V, U=U, S=S, L=L, W=W, H=H, area=L*W))
    parts = []
    for _ in range(tp):
        gi(); num_pt = gi(); num_ori = gi(); vol = gf()
        ors = [(gf(), gf(), gf(), gf()) for _o in range(num_ori)]
        l0, w0, h0, s0 = ors[0]
        for _c in range(num_pt):
            parts.append((vol, l0, w0, h0, s0))
    assert len(parts) == npart, f"{path}: {len(parts)} != {npart}"
    return machines, parts

def write_ours(path, mac, parts, due):
    N = len(parts); out = [f"1 {N}", f"1 {N}", "",
        f"1 1 {mac['V']:g} {mac['U']:g} {mac['S']:g} {mac['L']:g} {mac['W']:g} {mac['H']:g}", ""]
    for j, (vol, l, w, h, s) in enumerate(parts, 1):
        out += [f"{j} 1 1 {vol:g}", f"{l:g} {w:g} {h:g} {s:g}", ""]
    out += ["DueDate", " ".join(f"{int(d):g}" for d in due)]
    open(path, "w").write("\n".join(out) + "\n")

COMBOS = [(0.3, 0.3, 1), (0.3, 0.6, 1), (0.6, 0.3, 3), (0.6, 0.6, 3)]
rows = []
for fp in sorted(glob.glob(os.path.join(SRC, "*.txt"))):
    base = os.path.splitext(os.path.basename(fp))[0]   # e.g. ht2_1
    machines, parts = parse_yu(fp)
    big = max(machines, key=lambda m: m["area"]); N = len(parts)
    for TF, RDD, seed in COMBOS:
        D = yu.GenerateDueDate(fp, TF, RDD, seed)       # Yu's exact procedure
        name = f"{base}-{N}_{TF}_{RDD}_{seed}.txt"
        write_ours(os.path.join(DST, name), big, parts, D)
        rows.append((name, N, big['L'], big['W'], min(D), max(D)))

print(f"generated {len(rows)} larger instances")
for name, N, L, W, dmin, dmax in rows:
    print(f"  {name:28s} n={N} machine {L:g}x{W:g}  due[{dmin}..{dmax}]")
