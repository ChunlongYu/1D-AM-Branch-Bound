#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Branch-and-price PROTOTYPE for parallel-AM total-tardiness scheduling.

Demonstrates the full loop on small instances and is validated against
brute-force partition enumeration. NOT a production solver (see "what is missing"
in the README). Self-contained: includes a tiny Big-M simplex so it needs no
external LP solver.

Pieces:
  - batch_time / area feasibility           (mix-batch model S + V*sum v + U*max h)
  - phi_exact(S)                            exact single-machine total tardiness
  - price(...)                              prize-collecting single-machine pricer
                                            (optional parts + dual rewards + Ryan-Foster)
  - simplex(...)                            Big-M LP -> primal x and duals (pi, sigma)
  - column_generation(node)                 RMP + pricing loop -> LP bound, duals, x
  - branch_and_price(...)                   Ryan-Foster B&P tree -> proven optimum
  - brute_force(...)                        validation oracle (enumerate partitions)
"""
import itertools, math
from functools import lru_cache

# ----------------------------------------------------------------------------
# Instance + batch model
# ----------------------------------------------------------------------------
class Inst:
    def __init__(self, V,U,S,A, v,h,a,d, M):
        self.V,self.U,self.S,self.A = V,U,S,A
        self.v,self.h,self.a,self.d = v,h,a,d
        self.M=M; self.n=len(v)

def batch_time(I, B):
    return I.S + I.V*sum(I.v[j] for j in B) + I.U*max(I.h[j] for j in B)

def area_ok(I, B):
    return sum(I.a[j] for j in B) <= I.A + 1e-9

def feasible_subsets(I, parts):
    """all non-empty area-feasible subsets of `parts` (tuple)."""
    out=[]
    for r in range(1,len(parts)+1):
        for B in itertools.combinations(parts, r):
            if area_ok(I, B): out.append(B)
    return out

# ----------------------------------------------------------------------------
# Exact single-machine total tardiness  Phi(S)   (for validation + column cost)
# ----------------------------------------------------------------------------
def phi_exact(I, S):
    S=tuple(sorted(S))
    memo={}
    def rec(rem, t):
        if not rem: return 0.0
        key=(rem,round(t,6))
        if key in memo: return memo[key]
        best=math.inf
        for B in feasible_subsets(I, rem):
            t2=t+batch_time(I,B)
            tard=sum(max(0.0,t2-I.d[j]) for j in B)
            val=tard+rec(tuple(x for x in rem if x not in B), t2)
            if val<best: best=val
        memo[key]=best; return best
    return rec(S, 0.0)

# ----------------------------------------------------------------------------
# Pricing: prize-collecting single-machine schedule.
# Returns (min_value, included_frozenset, tardiness_cost) minimising
#   sum_{j in S} ( T_j - pi_j ),  parts optional, subject to Ryan-Foster.
# together/apart are sets of frozenset pairs constraining the COLUMN's part set.
# ----------------------------------------------------------------------------
def price(I, pi, together, apart):
    cand=tuple(j for j in range(I.n) if pi[j] > 1e-12)   # candidate reduction
    apart=[tuple(p) for p in apart]; together=[tuple(p) for p in together]
    best=[math.inf, frozenset(), 0.0]
    def violates_apart(inc):
        return any(x in inc and y in inc for (x,y) in apart)
    def violates_together(inc):           # checked only at a complete column
        return any((x in inc) != (y in inc) for (x,y) in together)
    def rec(rem, t, inc, val, tard):
        # option: stop here (discard the rest)
        if not violates_together(inc) and val < best[0]:
            best[0],best[1],best[2]=val, frozenset(inc), tard
        for B in feasible_subsets(I, rem):
            inc2=inc|set(B)
            if violates_apart(inc2): continue
            t2=t+batch_time(I,B)
            tj=sum(max(0.0,t2-I.d[j]) for j in B)
            add=tj-sum(pi[j] for j in B)
            rec(tuple(x for x in rem if x not in B), t2, inc2, val+add, tard+tj)
    rec(cand, 0.0, set(), 0.0, 0.0)
    return best[0], best[1], best[2]

# ----------------------------------------------------------------------------
# Tiny Big-M simplex for the restricted master LP, returning primal + duals.
#   min c^T x  s.t.  Aeq x = beq (beq >= 0),  x >= 0.
# Returns (x, y) where y are the row dual prices (y = c_B B^{-1}).
# ----------------------------------------------------------------------------
def simplex_bigM(c, Aeq, beq):
    m=len(Aeq); ncol=len(c)
    BIG=1e7*(1+max((abs(v) for v in c), default=1.0))
    # build tableau with one artificial per row
    A=[row[:] + [1.0 if i==r else 0.0 for r in range(m)] for i,row in enumerate(Aeq)]
    cost=list(c)+[BIG]*m
    basis=[ncol+i for i in range(m)]
    b=list(beq)
    N=ncol+m
    def reduced():
        # cB^T B^{-1} N  via current tableau (A,b already in basic form)
        cb=[cost[basis[i]] for i in range(m)]
        # y_j = cost_j - sum_i cb_i * A[i][j]
        return [cost[j]-sum(cb[i]*A[i][j] for i in range(m)) for j in range(N)]
    # Gaussian: ensure identity on artificial cols (already identity)
    for _ in range(10000):
        rc=reduced()
        # Bland's rule: smallest index with rc<-eps
        piv=-1
        for j in range(N):
            if rc[j]<-1e-7: piv=j; break
        if piv<0: break
        # ratio test
        best=-1; bestr=math.inf
        for i in range(m):
            if A[i][piv]>1e-9:
                ratio=b[i]/A[i][piv]
                if ratio<bestr-1e-12: bestr=ratio; best=i
        if best<0: raise RuntimeError("unbounded")
        # pivot on (best,piv)
        pv=A[best][piv]
        A[best]=[x/pv for x in A[best]]; b[best]/=pv
        for i in range(m):
            if i==best: continue
            f=A[i][piv]
            if abs(f)>1e-15:
                A[i]=[A[i][k]-f*A[best][k] for k in range(N)]
                b[i]-=f*b[best]
        basis[best]=piv
    # primal
    x=[0.0]*N
    for i in range(m): x[basis[i]]=b[i]
    # duals y_i: solve y^T B = c_B  ->  with tableau, y = c_B B^{-1};
    # equivalently y_i = cost_original_row dual = cost[j]-rc[j] relation:
    # use y such that rc_j = c_j - y . A0_j on ORIGINAL columns; recover y from
    # the artificial columns whose original A0 = e_i and cost = BIG:
    rc=reduced()
    y=[BIG-rc[ncol+i] for i in range(m)]
    return x[:ncol], y

# ----------------------------------------------------------------------------
# Restricted master + column generation at a B&P node
# ----------------------------------------------------------------------------
def column_generation(I, cols, together, apart, eps=1e-7, maxit=2000):
    """cols: list of (frozenset parts, cost). Returns (lp_obj, x, duals pi, sigma, cols)."""
    n=I.n
    for _ in range(maxit):
        K=len(cols)
        # master: min sum c_p x_p ; sum_p a_jp x_p = 1 ; sum_p x_p + s = M
        Aeq=[]; 
        for j in range(n):
            Aeq.append([1.0 if j in cols[p][0] else 0.0 for p in range(K)] + [0.0])  # +slack col
        Aeq.append([1.0]*K + [1.0])            # convexity with slack
        c=[cols[p][1] for p in range(K)] + [0.0]
        beq=[1.0]*n + [float(I.M)]
        x, y = simplex_bigM(c, Aeq, beq)
        pi=y[:n]; sigma=y[n]
        lp_obj=sum(c[k]*x[k] for k in range(len(c)))
        # price
        rc, inc, tard = price(I, pi, together, apart)
        red = rc - sigma
        if red < -1e-6 and len(inc)>0:
            if (inc, tard) not in [(c0,c1) for (c0,c1) in cols]:
                cols=cols+[(inc, tard)]
                continue
        return lp_obj, x, pi, sigma, cols
    return lp_obj, x, pi, sigma, cols

# ----------------------------------------------------------------------------
# Ryan-Foster branch-and-price
# ----------------------------------------------------------------------------
def initial_columns(I):
    # greedy M-partition (feasible) + singletons, as (frozenset, cost)
    order=sorted(range(I.n), key=lambda j: -I.a[j])
    groups=[[] for _ in range(I.M)]
    for j in order:
        k=min(range(I.M), key=lambda i: sum(I.a[x] for x in groups[i]))
        groups[k].append(j)
    cols=[]
    seen=set()
    for g in groups:
        if g:
            fs=frozenset(g); cols.append((fs, phi_exact(I,fs))); seen.add(fs)
    for j in range(I.n):
        fs=frozenset([j])
        if fs not in seen: cols.append((fs, phi_exact(I,fs))); seen.add(fs)
    return cols

def integral(x, tol=1e-6):
    return all(abs(v-round(v))<tol for v in x)

def frac_pair(I, x, cols):
    """find a Ryan-Foster pair (j,j') with fractional together-frequency."""
    n=I.n
    for j in range(n):
        for k in range(j+1,n):
            y=sum(x[p] for p in range(len(cols)) if j in cols[p][0] and k in cols[p][0])
            if 1e-6 < y < 1-1e-6:
                return (j,k)
    return None

def branch_and_price(I):
    best_obj=math.inf; best=None
    root=(initial_columns(I), [], [])     # (cols, together, apart)
    stack=[root]
    while stack:
        cols, tog, apr = stack.pop()
        lp, x, pi, sigma, cols = column_generation(I, cols, tog, apr)
        if lp >= best_obj - 1e-9:           # bound prune
            continue
        if integral(x):
            if lp < best_obj - 1e-9:
                best_obj=lp; best=[cols[p][0] for p in range(len(cols)) if x[p]>0.5]
            continue
        pair=frac_pair(I, x, cols)
        if pair is None:                    # fractional but no pair: take as feasible bound
            if lp < best_obj - 1e-9:
                best_obj=lp
            continue
        j,k=pair
        stack.append((list(cols), tog+[frozenset([j,k])], list(apr)))   # together
        stack.append((list(cols), list(tog), apr+[frozenset([j,k])]))   # apart
    return best_obj, best

# ----------------------------------------------------------------------------
# Brute-force validation: min over partitions into <= M groups
# ----------------------------------------------------------------------------
def brute_force(I):
    n=I.n; best=math.inf
    def parts_into(k, items):
        if not items:
            yield []; return
        first, rest = items[0], items[1:]
        for sub in parts_into(k, rest):
            # add to an existing group
            for i in range(len(sub)):
                yield sub[:i]+[sub[i]+[first]]+sub[i+1:]
            if len(sub)<k:
                yield sub+[[first]]
    for groups in parts_into(I.M, list(range(n))):
        tot=sum(phi_exact(I, g) for g in groups)
        if tot<best: best=tot
    return best

# ----------------------------------------------------------------------------
def selftest_simplex():
    # min 2x+3y s.t. x+y=10, x>=0,y>=0 -> x=10 obj=20 ; dual of eq = 2
    x,y=simplex_bigM([2.0,3.0],[[1.0,1.0]],[10.0])
    assert abs(x[0]-10)<1e-4 and abs(x[1])<1e-4, x
    assert abs(y[0]-2.0)<1e-3, y
    print("  simplex self-test ok (x=%.2f,%.2f dual=%.3f)"%(x[0],x[1],y[0]))

def demo():
    selftest_simplex()
    # small instance: n=6, M=2
    import random; rng=random.Random(7)
    n=6
    v=[rng.uniform(20,120) for _ in range(n)]
    h=[rng.uniform(2,30) for _ in range(n)]
    a=[rng.uniform(20,120) for _ in range(n)]
    I0=Inst(V=0.03, U=0.7, S=2.0, A=300.0, v=v,h=h,a=a,d=[0]*n, M=2)
    # due dates ~ tight around single-machine makespan estimate
    cmax=phi_exact(I0, tuple(range(n)))  # rough scale
    base=max(1.0, 0.6*(sum(batch_time(I0,(j,)) for j in range(n))/I0.M))
    d=[rng.uniform(0.3*base, 1.0*base) for _ in range(n)]
    I=Inst(V=0.03,U=0.7,S=2.0,A=300.0,v=v,h=h,a=a,d=d,M=2)
    bp_obj, sol = branch_and_price(I)
    bf_obj = brute_force(I)
    print("  B&P optimum   = %.4f  columns=%s"%(bp_obj, [sorted(s) for s in (sol or [])]))
    print("  brute-force   = %.4f"%bf_obj)
    print("  MATCH" if abs(bp_obj-bf_obj)<1e-4 else "  *** MISMATCH ***")

if __name__=="__main__":
    demo()
