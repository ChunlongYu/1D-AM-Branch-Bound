#!/usr/bin/env python3
"""
Brute-force validator for the incremental (Type I / Type II) branching bounds
described in docs/Incremental_Branching_Bounds.md.

For random small instances and random reachable incremental nodes, compare the
analytical node bound LB(N) against the EXACT optimum of the node's subtree
(exhaustive enumeration of all completions extending the partial schedule).

Checks:
  (1) Soundness:  LB(N) <= opt(N)            (for each height variant)
  (2) Leaf-exact: R == {}  ->  LB(N) == opt  (== true objective)
  (3) Tightness:  distribution of LB/opt

Height-term variants for the positional bound:
  safe   = sum_{i<bk} h_[i]              + h_[k]                  (manuscript form, ignores open batch h_r)
  tight  = sum_{i<bk} h_[i]              + max(h_[k], h_r)        (naive add of h_r)
  iron   = max(tokens) + sum of (bk-1) smallest tokens,
           tokens = {h_r} U {k smallest R heights}               (partition-min, has a proof)
"""
import itertools, math, random, sys

# part = (area, vol, h, due)
def b_area(B, P): return sum(P[j][0] for j in B)
def b_proc(B, P, S, V, U):
    if not B: return 0.0
    return S + V*sum(P[j][1] for j in B) + U*max(P[j][2] for j in B)

# ---- exact optimum over all ordered, capacity-feasible batchings of `rem`, started at t ----
def min_tard_batching(rem, t, P, A, S, V, U):
    rem = frozenset(rem)
    memo = {}
    def rec(R, tt):
        if not R: return 0.0
        key = (R, round(tt, 6))
        if key in memo: return memo[key]
        best = math.inf
        Rl = sorted(R)
        for r in range(1, len(Rl)+1):
            for combo in itertools.combinations(Rl, r):
                if b_area(combo, P) > A + 1e-9: continue
                Cn = tt + b_proc(combo, P, S, V, U)
                tard = sum(max(0.0, Cn - P[j][3]) for j in combo)
                v = tard + rec(R - frozenset(combo), Cn)
                if v < best: best = v
        memo[key] = best
        return best
    return rec(rem, t)

# ---- exact optimum of a node's subtree (extends closed.. + open batch may grow) ----
def node_opt(closed, open_set, R, P, A, S, V, U):
    F = 0.0; closed_tard = 0.0
    for B in closed:
        F += b_proc(B, P, S, V, U)
        closed_tard += sum(max(0.0, F - P[j][3]) for j in B)
    best = math.inf
    Rl = sorted(R); a_open = b_area(open_set, P)
    for r in range(0, len(Rl)+1):
        for T in itertools.combinations(Rl, r):
            if a_open + b_area(T, P) > A + 1e-9: continue
            of = set(open_set) | set(T)
            if of:
                Cr = F + b_proc(of, P, S, V, U)
                otard = sum(max(0.0, Cr - P[j][3]) for j in of)
            else:
                Cr = F; otard = 0.0
            rest_tard = min_tard_batching(R - set(T), Cr, P, A, S, V, U)
            tot = closed_tard + otard + rest_tard
            if tot < best: best = tot
    return best

# ---- analytical node bound ----
def node_LB(closed, open_set, R, P, A, S, V, U, hvar):
    F = 0.0; closed_tard = 0.0
    for B in closed:
        F += b_proc(B, P, S, V, U)
        closed_tard += sum(max(0.0, F - P[j][3]) for j in B)
    if open_set:
        a_r = b_area(open_set, P); v_r = sum(P[j][1] for j in open_set); h_r = max(P[j][2] for j in open_set)
        c_r = F + S + V*v_r + U*h_r; rho = A - a_r
        open_floor = sum(max(0.0, c_r - P[j][3]) for j in open_set)
        has_open = True
    else:
        a_r = v_r = h_r = 0.0; c_r = F; rho = 0.0; open_floor = 0.0; has_open = False

    Rl = sorted(R)
    # LB_par
    lbpar = 0.0
    for j in Rl:
        aj, vj, hj, dj = P[j]
        lj = (c_r + V*vj) if aj <= rho + 1e-9 else (c_r + S + V*vj + U*hj)
        lbpar += max(0.0, lj - dj)
    # LB_pos
    lbpos = 0.0; q = len(Rl)
    if q > 0:
        areas = sorted(P[j][0] for j in Rl)
        vols  = sorted(P[j][1] for j in Rl)
        hs    = sorted(P[j][2] for j in Rl)
        ds    = sorted(P[j][3] for j in Rl)
        prefA = prefV = 0.0
        for k in range(1, q+1):
            prefA += areas[k-1]; prefV += vols[k-1]
            bk = max(1, math.ceil((a_r + prefA)/A - 1e-9))
            bk = min(bk, k + (1 if a_r > 0 else 0))
            small = sum(hs[0:bk-1])           # bk-1 smallest R heights
            if hvar == 'safe':
                Hk = small + hs[k-1]
            elif hvar == 'tight':
                Hk = small + max(hs[k-1], h_r)
            elif hvar == 'iron':
                tokens = sorted(([h_r] if has_open else []) + hs[0:k])
                m = len(tokens)
                bkc = min(bk, m)
                Hk = tokens[-1] + sum(tokens[0:bkc-1])
            else:
                raise ValueError(hvar)
            Ck = F + bk*S + V*(v_r + prefV) + U*Hk
            lbpos += max(0.0, Ck - ds[k-1])
    return closed_tard + open_floor + max(lbpar, lbpos)

def gen_instance(n, rng):
    P = []
    for _ in range(n):
        area = rng.randint(1, 6)
        vol  = round(rng.uniform(0.5, 5.0), 3)
        h    = round(rng.uniform(0.5, 5.0), 3)
        due  = round(rng.uniform(0.0, 25.0), 3)
        P.append((area, vol, h, due))
    A = max(rng.randint(5, 12), max(p[0] for p in P))
    S = round(rng.uniform(1.0, 5.0), 3)
    V = round(rng.uniform(0.1, 1.0), 3)
    U = round(rng.uniform(0.1, 1.0), 3)
    return P, A, S, V, U

def random_node(P, A, S, V, U, rng):
    n = len(P); order = list(range(n)); rng.shuffle(order)
    steps = rng.randint(0, n)
    closed = []; open_set = []
    for j in order[:steps]:
        if not open_set:
            open_set = [j]
        else:
            if b_area(open_set, P) + P[j][0] <= A + 1e-9 and rng.random() < 0.5:
                open_set.append(j)                 # Type II
            else:
                closed.append(set(open_set)); open_set = [j]   # Type I
    R = set(range(n)) - set(order[:steps])
    return closed, set(open_set), R

def main():
    rng = random.Random(int(sys.argv[2]) if len(sys.argv) > 2 else 20240617)
    TRIALS = int(sys.argv[1]) if len(sys.argv) > 1 else 4000
    NODES  = 3
    variants = ['safe', 'tight', 'iron']
    viol = {v: 0 for v in variants}
    worst = {v: (1.0, None) for v in variants}   # smallest opt-LB margin (most negative => violation)
    ratios = {v: [] for v in variants}
    leaf_bad = 0; leaf_n = 0; nodes = 0
    first_viol = {v: None for v in variants}

    for t in range(TRIALS):
        n = rng.randint(3, 7)
        P, A, S, V, U = gen_instance(n, rng)
        for _ in range(NODES):
            closed, open_set, R = random_node(P, A, S, V, U, rng)
            opt = node_opt(closed, open_set, R, P, A, S, V, U)
            nodes += 1
            for hv in variants:
                lb = node_LB(closed, open_set, R, P, A, S, V, U, hv)
                if lb > opt + 1e-6:
                    viol[hv] += 1
                    if first_viol[hv] is None:
                        first_viol[hv] = (P, A, S, V, U, [sorted(b) for b in closed], sorted(open_set), sorted(R), lb, opt)
                margin = opt - lb
                if margin < worst[hv][0]:
                    worst[hv] = (margin, None)
                if opt > 1e-9:
                    ratios[hv].append(lb/opt)
            if not R:
                leaf_n += 1
                lb_iron = node_LB(closed, open_set, R, P, A, S, V, U, 'iron')
                if abs(lb_iron - opt) > 1e-6:
                    leaf_bad += 1

    print(f"instances={TRIALS}  nodes_tested={nodes}  leaf_nodes={leaf_n}")
    print(f"leaf-exactness failures (iron): {leaf_bad}")
    print()
    for hv in variants:
        rs = ratios[hv]
        avg = sum(rs)/len(rs) if rs else float('nan')
        mn  = min(rs) if rs else float('nan')
        print(f"[{hv:5s}] soundness violations: {viol[hv]:5d} / {nodes}   "
              f"min margin(opt-LB)={worst[hv][0]:+.4f}   "
              f"tightness LB/opt: mean={avg:.3f} min={mn:.3f}")
    print()
    for hv in variants:
        if first_viol[hv] is not None:
            P, A, S, V, U, cl, op, R, lb, opt = first_viol[hv]
            print(f"--- first violation for '{hv}': LB={lb:.4f} > opt={opt:.4f}")
            print(f"    parts(area,vol,h,due)={P}")
            print(f"    A={A} S={S} V={V} U={U}")
            print(f"    closed={cl} open={op} R={R}")

if __name__ == "__main__":
    main()
