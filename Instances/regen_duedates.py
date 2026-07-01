# -*- coding: utf-8 -*-
"""
Regenerate due dates for the Derived_Yu2022_small benchmark with a CONSISTENT,
machine-aware makespan (the same recipe as the calibration set), replacing the
transplanted Yu due dates that made large-n instances trivial.

Parts and machine are kept byte-for-byte; only the DueDate section is replaced.
Each instance file is solved at M in {2,3,4}; since optimal tardiness is
non-increasing in M (more machines -> earlier completions), we scale due dates to
the makespan at the LARGEST M (M=4). This makes the most-parallel case non-trivial
by construction, and M=2,3 only tighter. TF/RDD/seed are taken from the filename.

In : Instances/Derived_Yu2022_small/*.txt
Out: Instances/Derived_Yu2022_small_v2/*.txt  (+ manifest.csv)
"""
import os, glob, re, random, csv

SRC = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Derived_Yu2022_small"
DST = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Derived_Yu2022_small_v2"
MREF = 4   # reference machine count for due-date scaling (largest M used)

def parse(path):
    t = open(path, encoding="latin-1").read().split(); i = 0
    def gi():
        nonlocal i; v = int(float(t[i])); i += 1; return v
    def gf():
        nonlocal i; v = float(t[i]); i += 1; return v
    gi(); N = gi(); gi(); gi()
    mid = gi(); mnum = gi(); V = gf(); U = gf(); S = gf(); L = gf(); W = gf(); H = gf()
    parts = []
    for _ in range(N):
        pid = gi(); pnum = gi(); ori = gi(); vol = gf()
        l = gf(); w = gf(); h = gf(); s = gf()
        parts.append((vol, l, w, h, s))
    mac = dict(V=V, U=U, S=S, L=L, W=W, H=H)
    return mac, parts

def cmax_estimate(parts, S, V, U, A, M):
    """Makespan estimate following Yu et al. (2022) GenerateDueDate, adapted to our
    batch model. Parts are taken in non-increasing footprint area and each is
    assigned to the machine yielding the smallest resulting completion time;
    per-machine batches are formed by Yu's area FormBatch (strict <), and the
    machine completion uses OUR batch time S + V*sum(vol) + U*max(h) WITHOUT the
    support-volume term (our solver does not model support). Cmax = max machine
    completion."""
    def machine_time(plist):                  # FormBatch (area, strict <) + our batch time
        batches = []                          # each: [used_area, sum_vol, max_h]
        for (vol, l, w, h, _s) in plist:      # plist kept in area-desc order
            a = l*w; placed = False
            for b in batches:
                if b[0] + a < A:              # Yu's FormBatch uses strict '<'
                    b[0] += a; b[1] += vol; b[2] = max(b[2], h); placed = True; break
            if not placed:
                batches.append([a, vol, h])
        return sum(S + V*b[1] + U*b[2] for b in batches)
    idx = sorted(range(len(parts)), key=lambda j: parts[j][1]*parts[j][2], reverse=True)
    mach = [[] for _ in range(M)]; comp = [0.0]*M
    for j in idx:                             # assign to the min-completion machine
        part = parts[j]; best_i, best_t = 0, float("inf")
        for i in range(M):
            t = machine_time(mach[i] + [part])
            if t < best_t: best_t, best_i = t, i
        mach[best_i].append(part); comp[best_i] = best_t
    return max(comp) if comp else 0.0

def write_ours(path, mac, parts, due):
    N = len(parts)
    out = [f"1 {N}", f"1 {N}", "",
           f"1 1 {mac['V']:g} {mac['U']:g} {mac['S']:g} {mac['L']:g} {mac['W']:g} {mac['H']:g}", ""]
    for j, (vol, l, w, h, s) in enumerate(parts, 1):
        out += [f"{j} 1 1 {vol:g}", f"{l:g} {w:g} {h:g} {s:g}", ""]
    out += ["DueDate", " ".join(f"{int(round(d))}" for d in due)]
    open(path, "w").write("\n".join(out) + "\n")

def main():
    os.makedirs(DST, exist_ok=True)
    rows = []
    for fp in sorted(glob.glob(os.path.join(SRC, "*.txt"))):
        name = os.path.splitext(os.path.basename(fp))[0]
        m = re.search(r"_([\d.]+)_([\d.]+)_(\d+)$", name)
        if not m:
            print(f"[skip] {name}: no TF_RDD_seed suffix"); continue
        TF, RDD, seed = float(m.group(1)), float(m.group(2)), int(m.group(3))
        mac, parts = parse(fp)
        A = mac["L"]*mac["W"]
        Cmax = cmax_estimate(parts, mac["S"], mac["V"], mac["U"], A, MREF)
        lo = max(1.0, Cmax*(1 - TF - RDD/2)); hi = max(lo+1, Cmax*(1 - TF + RDD/2))
        rng = random.Random(7919*seed + int(TF*100)*31 + int(RDD*100))
        due = [rng.randint(int(lo), int(hi)) for _ in range(len(parts))]
        write_ours(os.path.join(DST, name + ".txt"), mac, parts, due)
        rows.append(dict(instance=name, n=len(parts), TF=TF, RDD=RDD, seed=seed,
                         Cmax_M4=round(Cmax, 2), due_min=min(due), due_max=max(due)))
    with open(os.path.join(DST, "manifest.csv"), "w", newline="") as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0].keys())); wr.writeheader(); wr.writerows(rows)
    print(f"regenerated {len(rows)} instances -> Derived_Yu2022_small_v2 (due dates scaled to M={MREF} makespan)")

if __name__ == "__main__":
    main()
