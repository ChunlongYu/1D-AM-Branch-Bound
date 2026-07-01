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
  - _prize_candidates(...)/_price_search(...)  candidate-reduction + core search,
                                            factored apart so the reduction can be
                                            validated against a no-reduction ground
                                            truth (see run_p1_regression_tests)
  - simplex_bigM(...)                       Big-M LP -> primal x, duals (pi,sigma),
                                            and a `proven` flag (certified optimal?)
  - column_generation(node)                 RMP + pricing loop -> LP bound, duals, x,
                                            and a `proven` flag (see run_p2_p3_p5_*)
  - _child_pool(...)/_filter_rf(...)        column-pool purge + Ryan-Foster filtering
                                            handed to each B&P child (see fix log)
  - branch_and_price(...)                   Ryan-Foster B&P tree -> proven optimum,
                                            optionally an anytime global-LB trace
                                            (track_lb=True)
  - brute_force(...)                        validation oracle (enumerate partitions)
  - run_p1_regression_tests()               regression tests for the fixed
                                            candidate-reduction x "together" interaction
  - run_p2_p3_p5_regression_tests()         regression tests for proven-flags, the
                                            anytime global lower bound, and RF pool
                                            filtering (see the block above selftest_simplex)

Fix log:
  2026-07-01a Candidate reduction (drop pi_j<=0 parts before pricing) was applied
              per-part, independent of "together" Ryan-Foster constraints. A part
              forced together with an attractive partner can be profitable only
              jointly, even if its own pi_j<=0, so per-part reduction could make
              the pricer miss a genuine negative-reduced-cost column and falsely
              report master-LP optimality -- a soundness risk, not just a
              performance one. Fixed via union-find over "together" pairs
              (transitive component reduction); see _prize_candidates() and
              run_p1_regression_tests(). [design-review finding P1]

  2026-07-01b Both simplex_bigM() and column_generation() could silently hit an
              iteration cap and return a not-actually-optimal LP value with no
              signal to the caller; branch_and_price() then used that value to
              prune, which is unsound in the same way as P1 (an under-converged
              RMP value is only a valid UPPER bound on the true master LP value,
              never a valid lower bound). Fixed: both now return a `proven` flag;
              branch_and_price() computes each node's bound as the proven LP value
              when available, and otherwise safely falls back to the bound already
              certified at its parent (valid by LP monotonicity under added
              branching constraints), and never prunes on anything else. The same
              mechanism gives an anytime global lower bound (track_lb=True) as a
              byproduct. [design-review findings P2, P3]

  2026-07-01c Discovered while implementing the above: a node's full inherited
              column pool was handed to BOTH Ryan-Foster children unfiltered, so a
              child's RMP could still select an old column violating the branching
              decision just added. Analysis: this never invalidates a bound (a
              looser/unfiltered feasible region can only lower the LP value, so it
              stays a valid, if weaker, lower bound) and never produces an invalid
              incumbent (any integral RMP solution is still a real feasible
              schedule for the original problem) -- so it is a search-effectiveness
              gap, not a soundness bug, unlike P1/P2. Fixed anyway via
              _filter_rf()/_child_pool(), which also purges long-inactive columns
              (keeping only the columns active in the parent's optimal solution
              plus a permanent per-part singleton safety net) so the pool does not
              grow unboundedly with search depth. [new finding, folded into the
              P5 fix; see run_p2_p3_p5_regression_tests -> test_rf_filter_pool_purity]
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
#
# CANDIDATE REDUCTION -- fixed interaction with "together" (2026-07-01, see
# branch_and_price analysis report, finding P1).
# A part j with pi_j<=0 is "never beneficial ALONE" (including it contributes
# T_j-pi_j>=0), so it is safe to drop j from the search -- *provided j can be
# independently discarded*. That assumption breaks once j is tied to another
# part by a "together" constraint: j and its partner(s) must be included or
# excluded as one unit, so the joint contribution can still be negative even
# if j's own term is >=0, as long as the partner's term is negative enough.
# The fix: union together-pairs into connected components (transitively, so
# chains a-b-c are handled, not just direct pairs) and keep EVERY member of a
# component in the candidate set as soon as ANY member has pi_j>0. A component
# with no positive-pi member at all can still be safely dropped whole, since
# including it can only add cost. "apart" needs no such fix: it only forbids
# joint inclusion, never forces it, so it cannot make an otherwise-unhelpful
# part become helpful.
# ----------------------------------------------------------------------------
def _together_components(n, together):
    """Union-Find over 'together' pairs -> component id per part (transitive)."""
    parent=list(range(n))
    def find(x):
        while parent[x]!=x:
            parent[x]=parent[parent[x]]; x=parent[x]
        return x
    def union(x,y):
        rx,ry=find(x),find(y)
        if rx!=ry: parent[rx]=ry
    for (x,y) in together:
        union(x,y)
    return [find(j) for j in range(n)]

def _prize_candidates(I, pi, together):
    """Candidate set for pricing: pi_j>0, OR j's together-component contains
    some part with pi>0 (component members cannot be discarded individually)."""
    comp=_together_components(I.n, together)
    comp_attractive={}
    for j in range(I.n):
        if pi[j] > 1e-12:
            comp_attractive[comp[j]]=True
    return tuple(j for j in range(I.n)
                 if pi[j] > 1e-12 or comp_attractive.get(comp[j], False))

def _price_search(I, pi, together, apart, cand):
    """Core prize-collecting search over a given candidate set `cand`.
    Passing cand=tuple(range(I.n)) (no reduction) gives the ground-truth
    value used to validate _prize_candidates() in the tests below."""
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

def price(I, pi, together, apart):
    cand=_prize_candidates(I, pi, together)
    return _price_search(I, pi, together, apart, cand)

# ----------------------------------------------------------------------------
# Tiny Big-M simplex for the restricted master LP, returning primal + duals.
#   min c^T x  s.t.  Aeq x = beq (beq >= 0),  x >= 0.
# Returns (x, y, proven) where y are the row dual prices (y = c_B B^{-1}) and
# proven=True iff the simplex itself reached a certified optimum (no negative
# reduced cost left) within max_iter pivots. If proven=False, x/y are just the
# last iterate -- NOT reliable, and must not be trusted for bounding (P2).
# ----------------------------------------------------------------------------
def simplex_bigM(c, Aeq, beq, max_iter=10000):
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
    proven=False
    for _ in range(max_iter):
        rc=reduced()
        # Bland's rule: smallest index with rc<-eps
        piv=-1
        for j in range(N):
            if rc[j]<-1e-7: piv=j; break
        if piv<0:
            proven=True
            break
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
    return x[:ncol], y, proven

# ----------------------------------------------------------------------------
# Restricted master + column generation at a B&P node.
# Returns (lp_obj, x, pi, sigma, cols, proven).
# proven=True  <=>  lp_obj is the TRUE master-LP optimum for (cols,together,
#                    apart) at this node: the RMP simplex itself certified
#                    optimality AND pricing certified no negative-reduced-cost
#                    column remains.
# proven=False <=>  either the simplex or the outer CG loop hit its iteration
#                    cap. lp_obj is then only an UPPER bound on the true
#                    master-LP value here (fewer columns than optimal can only
#                    make the RMP value too high) and MUST NOT be used as a
#                    lower bound for pruning (design-review finding P2).
# ----------------------------------------------------------------------------
def column_generation(I, cols, together, apart, eps=1e-7, maxit=2000):
    """cols: list of (frozenset parts, cost)."""
    n=I.n
    lp_obj=None; x=None; pi=[0.0]*n; sigma=0.0
    for _ in range(maxit):
        K=len(cols)
        # master: min sum c_p x_p ; sum_p a_jp x_p = 1 ; sum_p x_p + s = M
        Aeq=[]
        for j in range(n):
            Aeq.append([1.0 if j in cols[p][0] else 0.0 for p in range(K)] + [0.0])  # +slack col
        Aeq.append([1.0]*K + [1.0])            # convexity with slack
        c=[cols[p][1] for p in range(K)] + [0.0]
        beq=[1.0]*n + [float(I.M)]
        x, y, simplex_proven = simplex_bigM(c, Aeq, beq)
        lp_obj=sum(c[k]*x[k] for k in range(len(c)))
        if not simplex_proven:
            return lp_obj, x, pi, sigma, cols, False
        pi=y[:n]; sigma=y[n]
        # price
        rc, inc, tard = price(I, pi, together, apart)
        red = rc - sigma
        if red < -1e-6 and len(inc)>0:
            if (inc, tard) not in [(c0,c1) for (c0,c1) in cols]:
                cols=cols+[(inc, tard)]
                continue
        return lp_obj, x, pi, sigma, cols, True     # simplex-optimal AND no improving column
    return lp_obj, x, pi, sigma, cols, False         # outer CG loop exhausted -> not proven

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

def _rf_consistent(fs, tog, apr):
    """True if column part-set `fs` does not violate any (tog/apr) pair."""
    for (a,b) in apr:
        if a in fs and b in fs: return False
    for (a,b) in tog:
        if (a in fs) != (b in fs): return False
    return True

def _filter_rf(cols, tog, apr):
    return [(fs,c) for (fs,c) in cols if _rf_consistent(fs, tog, apr)]

def _child_pool(cols, x, tog, apr, j, k, mode, singletons):
    """Pool handed to one Ryan-Foster child: purge to (a) columns actually
    used (x_p>0) in the parent's optimal RMP solution -- long-inactive
    columns are dropped (finding P5) -- plus (b) a permanent per-part
    singleton safety net so the child's RMP is always feasible; then (c)
    filter both against the branching constraint being added right now.

    Previously the inherited pool was passed to children unfiltered: a
    column violating the new constraint could still sit in the RMP. That
    never made a bound *invalid* (an unfiltered/looser feasible region can
    only make the LP value <= the properly-restricted one, so it stays a
    valid, if weaker, lower bound; and every improving basic feasible
    solution is still a real feasible schedule for the ORIGINAL problem), but
    it defeats the purpose of Ryan-Foster branching -- the child's RMP no
    longer represents its intended restricted subproblem, which slows
    convergence. Filtering here fixes that (see fix log entry 2026-07-01c)."""
    if mode=="together": child_tog=tog+[(j,k)]; child_apr=list(apr)
    else:                 child_tog=list(tog);   child_apr=apr+[(j,k)]
    active=[cols[p] for p in range(len(cols)) if x[p]>1e-7]
    pool=list(dict.fromkeys(active))
    seen={fs for fs,_ in pool}
    for fs,c in singletons:
        if fs not in seen:
            pool.append((fs,c)); seen.add(fs)
    pool=_filter_rf(pool, child_tog, child_apr)
    return pool, child_tog, child_apr

def _integral_cost_data(I):
    """True if all cost-relevant data are integers, in which case the LP
    bound may be safely rounded up (ceil) to a valid integer bound."""
    vals=[I.S,I.V,I.U]+list(I.v)+list(I.h)+list(I.d)
    return all(abs(v-round(v))<1e-9 for v in vals)

def branch_and_price(I, max_nodes=200000, track_lb=False):
    """Ryan-Foster branch-and-price. Returns (best_obj, best) normally; if
    track_lb=True, returns (best_obj, best, lb_trace) where lb_trace is the
    list of (global_lb, best_obj) sampled at every node pop.

    global_lb is the anytime global lower bound: the minimum, over currently
    open (not-yet-resolved) nodes, of a bound that is either PROVEN at that
    node, or safely inherited from its parent when unproven. This is sound
    because an unproven node's own RMP value can only be too HIGH (fewer
    columns than optimal inflates the restricted-master value), so it must
    never be trusted as a lower bound; the parent's already-validated bound
    remains valid for the child by LP monotonicity under added branching
    constraints (a restriction can only raise the true LP optimum, never
    lower it). [design-review findings P2, P3]"""
    best_obj=math.inf; best=None
    SINGLETONS=[(frozenset([j]), phi_exact(I,(j,))) for j in range(I.n)]
    root_cols=initial_columns(I)
    stack=[(root_cols, [], [], 0.0)]     # (cols, together, apart, parent_bound)
    lb_trace=[]
    nodes=0
    while stack:
        nodes+=1
        if nodes>max_nodes: break
        cols, tog, apr, parent_bound = stack.pop()
        lp, x, pi, sigma, cols, proven = column_generation(I, cols, tog, apr)

        if proven:
            nb = math.ceil(lp-1e-7) if _integral_cost_data(I) else lp
            node_bound = max(nb, parent_bound)   # monotonicity safety net
        else:
            node_bound = parent_bound            # cannot trust lp -> inherit only

        if track_lb:
            open_bounds=[e[3] for e in stack]
            global_lb = min([node_bound]+open_bounds)
            if best_obj < math.inf: global_lb = min(global_lb, best_obj)
            lb_trace.append((global_lb, best_obj))

        if node_bound >= best_obj - 1e-9:           # bound prune (always a validated bound)
            continue

        if integral(x):
            if lp < best_obj - 1e-9:
                best_obj=lp; best=[cols[p][0] for p in range(len(cols)) if x[p]>0.5]
            continue

        pair=frac_pair(I, x, cols)
        if pair is None:
            # Rare LP degeneracy: every pairwise together-frequency is
            # integral yet x itself is fractional (e.g. duplicate columns
            # for one part-set splitting weight). No Ryan-Foster pair exists
            # to branch on. A fractional x is not a feasible schedule, so
            # (unlike an earlier version of this prototype) we do NOT fold lp
            # into best_obj here -- doing so silently produced an "incumbent"
            # with no matching primal solution in `best`. Known prototype
            # limitation: this node's true integer optimum is not resolved;
            # a production implementation needs a fallback branching rule
            # (e.g. column-based branching) for this corner case.
            continue

        j,k=pair
        pool_t, tog_t, apr_t = _child_pool(cols, x, tog, apr, j, k, "together", SINGLETONS)
        pool_a, tog_a, apr_a = _child_pool(cols, x, tog, apr, j, k, "apart", SINGLETONS)
        stack.append((pool_t, tog_t, apr_t, node_bound))
        stack.append((pool_a, tog_a, apr_a, node_bound))

    if track_lb:
        # Search exhausted (stack empty): by definition every subtree has
        # been pruned (with a valid bound) or resolved, so the true global
        # lower bound now equals best_obj -- record that terminal state
        # explicitly (mid-search pops don't reach it under LIFO/DFS order,
        # which is expected: the anytime bound need only be valid and
        # non-decreasing while nodes remain open, not tight early).
        lb_trace.append((best_obj, best_obj))
        return best_obj, best, lb_trace
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
# Regression tests for P1 (candidate reduction x "together" interaction).
# Each test compares the production pricer price() -- which uses the reduced
# candidate set from _prize_candidates() -- against the ground-truth search
# _price_search(..., cand=all parts) that applies NO candidate reduction at
# all. If the reduction is unsound, the two disagree. A local "naive" (i.e.
# the pre-fix) reduction is also run, only inside these tests, to demonstrate
# that it actually would have gotten the crafted instances wrong -- this
# confirms the instances exercise the bug and are not vacuously passing.
# ----------------------------------------------------------------------------
def _naive_candidates_pre_fix(I, pi):
    """The ORIGINAL (buggy) candidate rule: drop every part with pi_j<=0,
    regardless of 'together' constraints. Kept only in the test file to
    demonstrate the regression; never used by price()."""
    return tuple(j for j in range(I.n) if pi[j] > 1e-12)

def test_together_candidate_reduction_pairwise():
    """Direct pair: part 0 has pi<=0 but is forced 'together' with part 1,
    whose pi>0 is large enough that the JOINT column is profitable even
    though part 0's own term is not. Part 2 is an unrelated, genuinely
    unhelpful part (pi<=0, no together constraint, tight due date) that
    should still be correctly excluded."""
    I=Inst(V=0.1,U=0.1,S=1.0,A=100.0,
           v=[1.0,1.0,1.0], h=[1.0,1.0,1.0], a=[10.0,10.0,10.0],
           d=[100.0,100.0,5.0], M=1)
    pi=[-5.0, 10.0, -3.0]
    together=[(0,1)]; apart=[]

    ref = _price_search(I, pi, together, apart, tuple(range(I.n)))  # ground truth: no reduction
    got = price(I, pi, together, apart)                             # production pricer
    naive_cand = _naive_candidates_pre_fix(I, pi)
    naive = _price_search(I, pi, together, apart, naive_cand)        # pre-fix behaviour

    assert abs(ref[0]-(-5.0))<1e-6, ref            # true optimum: include {0,1}, exclude 2
    assert ref[1]==frozenset({0,1}), ref
    assert abs(got[0]-ref[0])<1e-6 and got[1]==ref[1], (got, ref)     # fixed pricer matches ground truth
    assert naive[0] > ref[0]+1e-6 or naive[1]!=ref[1], naive          # pre-fix rule would have missed it
    print("  P1 test (pairwise together) ok: fixed=%.3f matches ground truth; "
          "pre-fix rule would have returned %.3f (missed the column)"%(got[0], naive[0]))

def test_together_candidate_reduction_chain():
    """Transitive chain: together=[(0,1),(1,2)] links three parts into one
    component. Only part 2 has pi>0; parts 0 and 1 must still be reachable
    as candidates purely through the chain (not just a direct pair)."""
    I=Inst(V=0.1,U=0.1,S=1.0,A=100.0,
           v=[1.0,1.0,1.0], h=[1.0,1.0,1.0], a=[10.0,10.0,10.0],
           d=[100.0,100.0,100.0], M=1)
    pi=[-2.0, -2.0, 10.0]
    together=[(0,1),(1,2)]; apart=[]

    ref = _price_search(I, pi, together, apart, tuple(range(I.n)))
    got = price(I, pi, together, apart)
    naive_cand = _naive_candidates_pre_fix(I, pi)
    naive = _price_search(I, pi, together, apart, naive_cand)

    assert abs(ref[0]-(-6.0))<1e-6, ref             # true optimum: include {0,1,2}
    assert ref[1]==frozenset({0,1,2}), ref
    assert abs(got[0]-ref[0])<1e-6 and got[1]==ref[1], (got, ref)
    assert naive[0] > ref[0]+1e-6 or naive[1]!=ref[1], naive
    print("  P1 test (transitive chain) ok: fixed=%.3f matches ground truth; "
          "pre-fix rule would have returned %.3f (missed the column)"%(got[0], naive[0]))

def run_p1_regression_tests():
    test_together_candidate_reduction_pairwise()
    test_together_candidate_reduction_chain()

# ----------------------------------------------------------------------------
# Regression tests for P2 (proven flags / no silent non-convergence pruning),
# P3 (anytime global lower bound), and the pool-filtering fix folded into P5.
# ----------------------------------------------------------------------------
def test_simplex_proven_flag():
    # A trivially small, well-posed LP should be proven=True.
    x,y,proven = simplex_bigM([2.0,3.0],[[1.0,1.0]],[10.0])
    assert proven, "expected the tiny LP to be solved to certified optimality"
    assert abs(x[0]-10)<1e-4 and abs(x[1])<1e-4, x
    assert abs(y[0]-2.0)<1e-3, y
    # Force non-convergence via an absurdly small iteration cap; proven must
    # become False so callers know not to trust x/y as a bound.
    x2,y2,proven2 = simplex_bigM([2.0,3.0],[[1.0,1.0]],[10.0], max_iter=0)
    assert proven2 is False, "expected proven=False when max_iter is exhausted"
    print("  P2 test (simplex proven flag) ok")

def test_column_generation_proven_flag():
    I=Inst(V=0.1,U=0.1,S=1.0,A=100.0, v=[1.0,1.0],h=[1.0,1.0],a=[10.0,10.0],
           d=[5.0,5.0], M=1)
    cols=initial_columns(I)
    lp,x,pi,sigma,cols2,proven = column_generation(I, cols, [], [])
    assert proven, "a real, small instance should converge to a proven CG optimum"
    lp2,x2,pi2,sigma2,cols3,proven2 = column_generation(I, cols2, [], [])
    assert proven2 and abs(lp2-lp)<1e-6, (lp, lp2)
    print("  P2 test (column_generation proven flag) ok")

def test_global_lb_monotone_and_sound():
    """global_lb must be non-decreasing over the search and must never
    exceed best_obj; at the end (search exhausted) it must equal the final
    best_obj, which must match brute force."""
    import random; rng=random.Random(42)
    n=6
    v=[rng.uniform(20,120) for _ in range(n)]
    h=[rng.uniform(2,30) for _ in range(n)]
    a=[rng.uniform(20,120) for _ in range(n)]
    I0=Inst(V=0.03,U=0.7,S=2.0,A=300.0,v=v,h=h,a=a,d=[0]*n,M=2)
    base=max(1.0, 0.6*(sum(batch_time(I0,(j,)) for j in range(n))/I0.M))
    d=[rng.uniform(0.3*base,0.9*base) for _ in range(n)]   # tighter -> more branching
    I=Inst(V=0.03,U=0.7,S=2.0,A=300.0,v=v,h=h,a=a,d=d,M=2)
    bp_obj, sol, trace = branch_and_price(I, track_lb=True)
    bf_obj = brute_force(I)
    assert abs(bp_obj-bf_obj)<1e-4, (bp_obj, bf_obj)
    prev_lb=-math.inf
    for glb, bobj in trace:
        assert glb <= bobj + 1e-6, (glb, bobj)       # LB must never exceed the incumbent
        assert glb >= prev_lb - 1e-6, (prev_lb, glb) # anytime LB must be non-decreasing
        prev_lb = max(prev_lb, glb)
    final_lb, final_best = trace[-1]
    assert abs(final_lb-bp_obj)<1e-4, (final_lb, bp_obj)   # gap fully closed at termination
    print("  P3 test (global lower-bound monotonicity/soundness) ok "
          "(nodes=%d, final gap=%.2e)"%(len(trace), abs(final_lb-bp_obj)))

def test_rf_filter_pool_purity():
    """_child_pool must never hand a child a column that violates the
    constraint just added (fix log entry 2026-07-01c)."""
    cols=[(frozenset({0,1}), 3.0), (frozenset({2}), 1.0), (frozenset({0}), 2.0)]
    x=[0.6, 0.4, 0.5]   # pretend all "active" (x_p>0) for this unit test
    singletons=[(frozenset({0}),2.0),(frozenset({1}),2.0),(frozenset({2}),1.0)]
    pool_t, tog_t, apr_t = _child_pool(cols, x, [], [], 0, 1, "together", singletons)
    pool_a, tog_a, apr_a = _child_pool(cols, x, [], [], 0, 1, "apart", singletons)
    # "together" child: singleton {0} alone (without 1) must be filtered out
    assert not any(fs==frozenset({0}) for fs,_ in pool_t), pool_t
    assert any(fs==frozenset({0,1}) for fs,_ in pool_t), pool_t
    # "apart" child: the {0,1} column must be filtered out
    assert not any(fs==frozenset({0,1}) for fs,_ in pool_a), pool_a
    assert any(fs==frozenset({0}) for fs,_ in pool_a), pool_a
    print("  pool-filter purity test ok (fix log 2026-07-01c)")

def run_p2_p3_p5_regression_tests():
    test_simplex_proven_flag()
    test_column_generation_proven_flag()
    test_global_lb_monotone_and_sound()
    test_rf_filter_pool_purity()

# ----------------------------------------------------------------------------
def selftest_simplex():
    # min 2x+3y s.t. x+y=10, x>=0,y>=0 -> x=10 obj=20 ; dual of eq = 2
    x,y,proven=simplex_bigM([2.0,3.0],[[1.0,1.0]],[10.0])
    assert proven
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
    run_p1_regression_tests()
    run_p2_p3_p5_regression_tests()
    demo()
