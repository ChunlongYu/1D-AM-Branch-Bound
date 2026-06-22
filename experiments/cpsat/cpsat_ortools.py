#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CP-SAT(OR-Tools)模型:identical parallel-machine 1D-AM batch scheduling, min total tardiness。
与 milp_gurobi.py 同口径:从实例文件末尾 DueDate 段读截止时间。

CP-SAT 只支持整数,故把所有"时间"量按 SCALE 缩放取整(面积为整数不缩放)。
目标值按 obj/SCALE 还原;缩放精度 = 1/SCALE(默认 1e4,可调)。

用法:
    pip install ortools
    python cpsat_ortools.py ../../data/15part.txt 2 1800 [--workers 8] [--scale 10000]
"""
import sys, os, time

def read_instance(path):
    toks = open(path, encoding='latin-1').read().split()
    it = iter(toks); nxt = lambda: next(it)
    int(nxt()); int(nxt()); int(nxt()); num_p = int(nxt())
    int(nxt()); int(nxt())
    Vc=float(nxt()); Uc=float(nxt()); Sc=float(nxt()); Lm=float(nxt()); Wm=float(nxt()); float(nxt())
    machine = dict(V=Vc, U=Uc, S=Sc, L=Lm, W=Wm)
    parts=[]
    for _ in range(num_p):
        int(nxt()); int(nxt()); orient=int(nxt()); vol=float(nxt())
        l=float(nxt()); w=float(nxt()); h=float(nxt()); float(nxt())
        for _o in range(orient-1):
            for _k in range(4): nxt()
        parts.append(dict(v=vol,l=l,w=w,h=h))
    due=[]
    try:
        if nxt()=="DueDate":
            for _ in range(num_p): due.append(float(nxt()))
    except StopIteration:
        pass
    if len(due)!=num_p: due=[]
    return machine, parts, due

def solve_cpsat(machine, parts, due, M, time_limit=1800.0, workers=8, scale=10000, log=False):
    from ortools.sat.python import cp_model
    n=len(parts); J=range(n); Ms=range(M); B=range(n)
    S,V,U = machine["S"],machine["V"],machine["U"]
    LW = int(round(machine["L"]*machine["W"]))
    a   = [int(round(parts[j]["l"]*parts[j]["w"])) for j in J]   # area (integer)
    Vv  = [int(round(V*parts[j]["v"]*scale)) for j in J]         # scaled volume-time
    Uh  = [int(round(U*parts[j]["h"]*scale)) for j in J]         # scaled height-time
    Sint= int(round(S*scale))
    d   = [int(round(due[j]*scale)) for j in J]
    horizon = Sint*n + sum(Vv) + n*(max(Uh) if Uh else 0)
    Pmax = Sint + sum(Vv) + (max(Uh) if Uh else 0)

    m = cp_model.CpModel()
    x = {(j,mm,b): m.NewBoolVar(f"x{j}_{mm}_{b}") for j in J for mm in Ms for b in B}
    z = {(mm,b): m.NewBoolVar(f"z{mm}_{b}") for mm in Ms for b in B}
    H = {(mm,b): m.NewIntVar(0, max(Uh) if Uh else 0, f"H{mm}_{b}") for mm in Ms for b in B}
    P = {(mm,b): m.NewIntVar(0, Pmax, f"P{mm}_{b}") for mm in Ms for b in B}
    C = {(mm,b): m.NewIntVar(0, horizon, f"C{mm}_{b}") for mm in Ms for b in B}
    c = {j: m.NewIntVar(0, horizon, f"c{j}") for j in J}
    T = {j: m.NewIntVar(0, horizon, f"T{j}") for j in J}

    for j in J:
        m.Add(sum(x[j,mm,b] for mm in Ms for b in B) == 1)
    for mm in Ms:
        for b in B:
            for j in J:
                m.Add(x[j,mm,b] <= z[mm,b])                          # link (tight)
                m.Add(H[mm,b] >= Uh[j]*x[j,mm,b])                    # batch height
            m.Add(sum(a[j]*x[j,mm,b] for j in J) <= LW)             # area
            m.Add(P[mm,b] == Sint*z[mm,b] + sum(Vv[j]*x[j,mm,b] for j in J) + H[mm,b])
        m.Add(C[mm,0] >= P[mm,0])
        for b in B:
            if b>=1: m.Add(C[mm,b] >= C[mm,b-1] + P[mm,b])
            if b+1<n: m.Add(z[mm,b+1] <= z[mm,b])                    # contiguity
    for j in J:
        for mm in Ms:
            for b in B:
                m.Add(c[j] >= C[mm,b]).OnlyEnforceIf(x[j,mm,b])     # reified (no big-M)
        m.Add(T[j] >= c[j] - d[j])
    if M>=2:
        for mm in range(M-1):
            m.Add(sum(z[mm,b] for b in B) >= sum(z[mm+1,b] for b in B))  # machine symmetry

    m.Minimize(sum(T[j] for j in J))

    solver = cp_model.CpSolver()
    solver.parameters.max_time_in_seconds = float(time_limit)
    solver.parameters.num_search_workers = int(workers)
    solver.parameters.log_search_progress = bool(log)
    t0=time.time(); st=solver.Solve(m); el=time.time()-t0

    stat={cp_model.OPTIMAL:"OPTIMAL", cp_model.FEASIBLE:"TIME_LIMIT",
          cp_model.INFEASIBLE:"INFEASIBLE", cp_model.UNKNOWN:"UNKNOWN"}.get(st,str(st))
    has = st in (cp_model.OPTIMAL, cp_model.FEASIBLE)
    obj   = solver.ObjectiveValue()/scale if has else float("nan")
    bound = solver.BestObjectiveBound()/scale if has else float("nan")
    gap   = (abs(obj-bound)/abs(obj)) if (has and abs(obj)>1e-9) else (0.0 if has else float("nan"))
    return dict(obj=obj, bound=bound, gap=gap, status=stat, time=el)

def main():
    if len(sys.argv)<3: print(__doc__); return
    ipath=sys.argv[1]; M=int(sys.argv[2])
    tl=float(sys.argv[3]) if len(sys.argv)>3 and not sys.argv[3].startswith("--") else 1800.0
    workers=8; scale=10000
    if "--workers" in sys.argv: workers=int(sys.argv[sys.argv.index("--workers")+1])
    if "--scale"   in sys.argv: scale  =int(sys.argv[sys.argv.index("--scale")+1])
    machine,parts,due=read_instance(ipath)
    if len(due)!=len(parts):
        print(f"Error: '{ipath}' missing/incomplete DueDate ({len(due)}/{len(parts)})."); return
    name=os.path.splitext(os.path.basename(ipath))[0]
    print(f"=== CP-SAT  instance={name} n={len(parts)} M={M} TL={tl}s workers={workers} scale={scale} ===")
    r=solve_cpsat(machine,parts,due,M,time_limit=tl,workers=workers,scale=scale)
    print(f"status={r['status']} obj={r['obj']:.4f} bound={r['bound']:.4f} gap={r['gap']*100:.2f}% time={r['time']:.2f}s")
    print(f"RESULT instance={name} n={len(parts)} M={M} obj={r['obj']:.4f} bound={r['bound']:.4f} "
          f"gap={r['gap']:.6f} status={r['status']} time={r['time']:.2f}")

if __name__=="__main__":
    main()
