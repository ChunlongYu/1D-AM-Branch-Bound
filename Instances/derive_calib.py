# -*- coding: utf-8 -*-
"""
Build a CALIBRATION instance set (separate from the Derived_Yu2022_small benchmark)
by sub-sampling parts from Yu et al. (2022) LargerInstances ht2_2 + ht2_3 (n=50 each)
-- these two are NOT used in the benchmark (which only used Larger ht2_1), so the
calibration set shares no instance with the test set.

For each target (n, RDD) we draw n parts (seeded), pick a footprint-fitting
orientation, and generate due dates with TF=0.6 using Yu's own formula
    D_j ~ U( Cmax*(1-TF-RDD/2),  Cmax*(1-TF+RDD/2) )
where Cmax is a makespan estimate computed FOR THAT INSTANCE'S ASSIGNED M
(FFD area-batching + LPT across M machines, mix-batch time S+V*sum(vol)+U*max(h)),
so due dates are self-consistent with the M each instance will be solved at.

Output: Instances/Calib_TF06/  +  manifest.csv (instance,n,RDD,M,seed,Cmax).
The assigned M is encoded in the filename so the DOE runner can read it.
"""
import os, glob, random, csv

YU = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Yu et al., 2022"
SRC = [os.path.join(YU, "LargerInstances", f) for f in ("ht2_2.txt", "ht2_3.txt")]
DST = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Calib_TF06"

TF = 0.6
# (n, RDD) -> M  (the agreed assignment table; spans M in {2,3,4})
PLAN = {
    (10, 0.3): 2, (10, 0.6): 3,
    (15, 0.3): 4, (15, 0.6): 2,
    (20, 0.3): 3, (20, 0.6): 4,
    (25, 0.3): 2, (25, 0.6): 3,
    (30, 0.3): 4, (30, 0.6): 2,
}

def parse_full(path):
    t = open(path, encoding="latin-1").read().split(); i = 0
    def gi():
        nonlocal i; v = int(float(t[i])); i += 1; return v
    def gf():
        nonlocal i; v = float(t[i]); i += 1; return v
    tm = gi(); tp = gi(); nm = gi(); npart = gi()
    macs = []
    for _ in range(tm):
        gi(); gi(); V = gf(); U = gf(); S = gf(); L = gf(); W = gf(); H = gf()
        macs.append(dict(V=V, U=U, S=S, L=L, W=W, H=H, area=L*W))
    parts = []
    for _ in range(tp):
        gi(); num = gi(); no = gi(); vol = gf()
        ors = [(gf(), gf(), gf(), gf()) for _ in range(no)]
        for _c in range(num):
            parts.append((vol, ors))
    return macs, parts

def fit_orientation(ors, L, W):
    if ors[0][0] <= L and ors[0][1] <= W:
        return ors[0]
    for o in ors:
        if o[0] <= L and o[1] <= W:
            return o
    return None

def cmax_estimate(chosen, S, V, U, A, M):
    """FFD area-batching then LPT across M machines; mix-batch time."""
    # First-Fit Decreasing by footprint area
    items = sorted(chosen, key=lambda c: c[1]*c[2], reverse=True)  # (vol,l,w,h,s)
    batches = []   # each: [used_area, sum_vol, max_h]
    for vol, l, w, h, _s in items:
        a = l*w
        placed = False
        for b in batches:
            if b[0] + a <= A + 1e-9:
                b[0] += a; b[1] += vol; b[2] = max(b[2], h); placed = True; break
        if not placed:
            batches.append([a, vol, h])
    ptimes = sorted((S + V*b[1] + U*b[2] for b in batches), reverse=True)
    load = [0.0]*M
    for p in ptimes:              # LPT
        k = min(range(M), key=lambda i: load[i]); load[k] += p
    return max(load) if load else 0.0

def write_ours(path, sm, chosen, H, due):
    N = len(chosen)
    out = [f"1 {N}", f"1 {N}", "",
           f"1 1 {sm['V']:g} {sm['U']:g} {sm['S']:g} {sm['L']:g} {sm['W']:g} {H:g}", ""]
    for j, (vol, l, w, h, s) in enumerate(chosen, 1):
        out += [f"{j} 1 1 {vol:g}", f"{l:g} {w:g} {h:g} {s:g}", ""]
    out += ["DueDate", " ".join(f"{int(round(d))}" for d in due)]
    open(path, "w").write("\n".join(out) + "\n")

def main():
    os.makedirs(DST, exist_ok=True)
    # pool parts + take the smaller machine across the two sources
    pool, macs_all = [], []
    for fp in SRC:
        macs, parts = parse_full(fp)
        macs_all += macs; pool += parts
    sm = min(macs_all, key=lambda m: m["area"])
    L, W = sm["L"], sm["W"]
    print(f"pool = {len(pool)} parts from ht2_2+ht2_3 ; machine {L:g}x{W:g} U={sm['U']:g} S={sm['S']:g}")

    rows = []
    for (n, RDD), M in PLAN.items():
        seed = 1000 + n*10 + int(RDD*10)
        rng = random.Random(seed)
        idx = rng.sample(range(len(pool)), n)
        chosen = []
        for k in idx:
            vol, ors = pool[k]
            o = fit_orientation(ors, L, W)
            assert o is not None, "no footprint-fitting orientation"
            chosen.append((vol, o[0], o[1], o[2], o[3]))
        H = max(sm["H"], max(c[3] for c in chosen))
        A = L*W
        Cmax = cmax_estimate(chosen, sm["S"], sm["V"], sm["U"], A, M)
        lo = max(1, Cmax*(1-TF-RDD/2)); hi = max(lo+1, Cmax*(1-TF+RDD/2))
        rng2 = random.Random(seed + 7)
        due = [rng2.randint(int(lo), int(hi)) for _ in range(n)]
        name = f"cal_n{n}_rdd{RDD}_M{M}_s{seed}.txt"
        write_ours(os.path.join(DST, name), sm, chosen, H, due)
        rows.append(dict(instance=name, n=n, RDD=RDD, M=M, seed=seed,
                         Cmax=round(Cmax, 2), due_min=min(due), due_max=max(due)))
        print(f"  {name:34s} n={n:>2} M={M} Cmax={Cmax:7.1f} due[{min(due)}..{max(due)}]")

    with open(os.path.join(DST, "manifest.csv"), "w", newline="") as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0].keys())); wr.writeheader()
        wr.writerows(rows)
    print(f"\nwrote {len(rows)} calibration instances + manifest.csv -> Calib_TF06")

if __name__ == "__main__":
    main()
