# -*- coding: utf-8 -*-
# Pure-python exact brute force for small instances, both batch models.
# Used to VALIDATE the P(B) semantics + parser against the C++ oracle/pbb optima.
import sys, itertools
from functools import lru_cache

def parse(path):
    t = open(path).read().split()
    i = 0
    def ni():
        nonlocal i; v=t[i]; i+=1; return v
    tm=int(ni()); tp=int(ni()); nm=int(ni()); npart=int(ni())
    mid=int(ni()); mnum=int(ni())
    scan=float(ni()); recoat=float(ni()); setup=float(ni())
    L=float(ni()); W=float(ni()); Hh=float(ni())
    vol=[0.0]*npart; area=[0.0]*npart; h=[0.0]*npart
    for k in range(npart):
        pid=int(ni()); num=int(ni()); ori=int(ni())
        v=float(ni()); l=float(ni()); w=float(ni()); hh=float(ni()); sup=float(ni())
        vol[k]=v; area[k]=l*w; h[k]=hh
    # due dates: optional non-numeric marker then npart numbers
    d=[]
    while len(d)<npart and i<len(t):
        try: d.append(float(t[i])); i+=1
        except ValueError: i+=1
    return dict(n=npart, S=setup, V=scan, U=recoat, A=L*W, vol=vol, area=area, h=h, d=d)

def Pbatch(inst, B, mode):
    if mode=='mix':
        sv=sum(inst['vol'][j] for j in B); mh=max(inst['h'][j] for j in B)
        return inst['S'] + inst['V']*sv + inst['U']*mh
    else:  # p-batch (parallel batch): S + max over parts of standalone time (V*vol_j + U*h_j)
        mq=max(inst['V']*inst['vol'][j] + inst['U']*inst['h'][j] for j in B)
        return inst['S'] + mq

def single_machine_opt(inst, parts, mode):
    parts=tuple(sorted(parts)); A=inst['A']; area=inst['area']; d=inst['d']
    from functools import lru_cache
    @lru_cache(maxsize=None)
    def f(remaining, t):
        if not remaining: return 0.0
        R=list(remaining); best=float('inf')
        m=len(R)
        # enumerate non-empty area-feasible subsets B of R as the next batch
        for r in range(1, m+1):
            for combo in itertools.combinations(R, r):
                if sum(area[j] for j in combo) > A + 1e-9: continue
                P=Pbatch(inst, combo, mode); c=t+P
                tard=sum(max(0.0, c-d[j]) for j in combo)
                rest=tuple(x for x in R if x not in combo)
                val=tard + f(rest, round(c,6))
                if val<best: best=val
        return best
    return f(parts, 0.0)

def parallel_opt(inst, M, mode):
    n=inst['n']; parts=list(range(n))
    best=float('inf')
    # enumerate assignments of parts to M machines (M^n); separable per machine
    memo={}
    def smo(ps):
        key=tuple(sorted(ps))
        if key not in memo: memo[key]=single_machine_opt(inst, key, mode)
        return memo[key]
    for assign in itertools.product(range(M), repeat=n):
        groups=[[] for _ in range(M)]
        for j,m in enumerate(assign): groups[m].append(j)
        # symmetry: only consider assignments where machine sizes are non-increasing (identical machines)
        sizes=[len(g) for g in groups]
        if sizes!=sorted(sizes, reverse=True): continue
        tot=sum(smo(g) for g in groups if g)
        if tot<best: best=tot
    return best

if __name__=='__main__':
    path=sys.argv[1]; M=int(sys.argv[2]) if len(sys.argv)>2 else 1
    inst=parse(path)
    print(f"instance n={inst['n']} S={inst['S']} V={inst['V']} U={inst['U']} A={inst['A']}")
    for mode in ['mix','pbatch']:
        opt=parallel_opt(inst, M, mode)
        print(f"  M={M} mode={mode:7s} optimum total tardiness = {opt:.4f}")
