#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
论文 MILP 模型(identical parallel-machine 1D-AM batch scheduling, min total tardiness)
的 gurobipy 复现。截止时间从实例文件末尾的 DueDate 段读取(与 C++ B&B 完全同口径)。

用法:
    python milp_gurobi.py <instance_path> <M> [time_limit_sec] [--no-sym]
例:
    python milp_gurobi.py ../../Instance/15part.txt 2 1800
    python milp_gurobi.py ../../Instance/13part.txt 2
    python milp_gurobi.py ../../data/13part.txt 2 60 --pbatch   # idealized p-batch model
"""
import sys, os, time

def read_instance(path):
    """解析实例文件;返回 (machine_dict, parts_list, due_dates_list)。
       截止时间来自文件末尾的 'DueDate' 段(无则返回空列表)。"""
    toks = open(path, encoding='latin-1').read().split()
    it = iter(toks); nxt = lambda: next(it)
    _tm = int(nxt()); _tp = int(nxt())
    _nm = int(nxt()); num_p = int(nxt())
    mid = int(nxt()); mnum = int(nxt())
    Vc = float(nxt()); Uc = float(nxt()); Sc = float(nxt())
    Lm = float(nxt()); Wm = float(nxt()); Hm = float(nxt())
    machine = dict(V=Vc, U=Uc, S=Sc, L=Lm, W=Wm, H=Hm)
    parts = []
    for _ in range(num_p):
        pid = int(nxt()); pnum = int(nxt()); orient = int(nxt()); vol = float(nxt())
        l = float(nxt()); w = float(nxt()); h = float(nxt()); supp = float(nxt())
        for _o in range(orient - 1):
            for _k in range(4): nxt()
        parts.append(dict(v=vol, l=l, w=w, h=h))
    # optional DueDate section
    due = []
    try:
        tag = nxt()
        if tag == "DueDate":
            for _ in range(num_p):
                due.append(float(nxt()))
    except StopIteration:
        pass
    if len(due) != num_p:
        due = []
    return machine, parts, due

def solve_milp(machine, parts, due, M, time_limit=1800.0, sym_break=True, threads=0, output=True,
               mode="mix"):
    # mode='mix'   : P(B) = S + V*sum(vol) + U*max(h)   (our realistic AM batch model)
    # mode='pbatch': P(B) = S +              U*max(h)   (idealized parallel batch, time = max only)
    import gurobipy as gp
    from gurobipy import GRB
    n = len(parts); J = range(n); Ms = range(M); B = range(n)
    S, V, U = machine["S"], machine["V"], machine["U"]
    LW = machine["L"] * machine["W"]
    l=[p["l"] for p in parts]; w=[p["w"] for p in parts]
    h=[p["h"] for p in parts]; v=[p["v"] for p in parts]
    a=[l[j]*w[j] for j in J]; d=list(due)
    bigM_C = sum(S + V*v[j] + U*h[j] for j in J)   # makespan upper bound on one machine
    bigM_link = n

    m = gp.Model("AM_parallel_milp")
    m.Params.OutputFlag = 1 if output else 0
    if time_limit and time_limit > 0: m.Params.TimeLimit = time_limit
    if threads: m.Params.Threads = threads

    x = m.addVars(J, Ms, B, vtype=GRB.BINARY, name="x")
    z = m.addVars(Ms, B, vtype=GRB.BINARY, name="z")
    H = m.addVars(Ms, B, lb=0.0, name="H")
    P = m.addVars(Ms, B, lb=0.0, name="P")
    C = m.addVars(Ms, B, lb=0.0, name="C")
    c = m.addVars(J, lb=0.0, name="c")
    T = m.addVars(J, lb=0.0, name="T")

    m.setObjective(gp.quicksum(T[j] for j in J), GRB.MINIMIZE)
    m.addConstrs((gp.quicksum(x[j,mm,b] for mm in Ms for b in B) == 1 for j in J), "assign")
    m.addConstrs((gp.quicksum(x[j,mm,b] for j in J) <= bigM_link*z[mm,b] for mm in Ms for b in B), "linkxz")
    m.addConstrs((gp.quicksum(a[j]*x[j,mm,b] for j in J) <= LW for mm in Ms for b in B), "area")
    if mode == "pbatch":
        # p-batch (parallel batch): batch time = S + max over assigned parts of their
        # standalone processing time q_j = V*vol_j + U*h_j (longest single job governs the batch).
        q = [V*v[j] + U*h[j] for j in J]
        Q = m.addVars(Ms, B, lb=0.0, name="Q")
        m.addConstrs((Q[mm,b] >= q[j]*x[j,mm,b] for j in J for mm in Ms for b in B), "qmax")
        m.addConstrs((P[mm,b] == S*z[mm,b] + Q[mm,b] for mm in Ms for b in B), "proc")
    else:
        # mix-batch (our AM model): batch time = S + V*sum(vol) + U*max(h).
        m.addConstrs((H[mm,b] >= h[j]*x[j,mm,b] for j in J for mm in Ms for b in B), "height")
        m.addConstrs((P[mm,b] == S*z[mm,b] + V*gp.quicksum(v[j]*x[j,mm,b] for j in J) + U*H[mm,b]
                      for mm in Ms for b in B), "proc")
    m.addConstrs((C[mm,0] >= P[mm,0] for mm in Ms), "Cfirst")
    m.addConstrs((C[mm,b] >= C[mm,b-1] + P[mm,b] for mm in Ms for b in B if b >= 1), "Cseq")
    m.addConstrs((c[j] >= C[mm,b] - bigM_C*(1 - x[j,mm,b]) for j in J for mm in Ms for b in B), "cj")
    m.addConstrs((T[j] >= c[j] - d[j] for j in J), "tard")
    m.addConstrs((z[mm,b+1] <= z[mm,b] for mm in Ms for b in B if b+1 < n), "contig")
    if sym_break and M >= 2:
        m.addConstrs((gp.quicksum(z[mm,b] for b in B) >= gp.quicksum(z[mm+1,b] for b in B)
                      for mm in range(M-1)), "machsym")

    t0 = time.time(); m.optimize(); elapsed = time.time() - t0
    status = {GRB.OPTIMAL:"OPTIMAL", GRB.TIME_LIMIT:"TIME_LIMIT",
              GRB.INFEASIBLE:"INFEASIBLE"}.get(m.Status, str(m.Status))
    obj = m.ObjVal if m.SolCount > 0 else float("nan")
    bound = m.ObjBound if m.SolCount > 0 or m.Status==GRB.TIME_LIMIT else float("nan")
    gap = m.MIPGap if m.SolCount > 0 else float("nan")
    nodes = getattr(m, 'NodeCount', float('nan'))
    return dict(obj=obj, bound=bound, gap=gap, status=status, time=elapsed,
                nvars=m.NumVars, nconstr=m.NumConstrs, nodes=nodes, mode=mode)

def main():
    if len(sys.argv) < 3:
        print(__doc__); return
    ipath = sys.argv[1]; M = int(sys.argv[2])
    tl = float(sys.argv[3]) if len(sys.argv) > 3 and not sys.argv[3].startswith("--") else 1800.0
    sym = "--no-sym" not in sys.argv
    mode = "pbatch" if "--pbatch" in sys.argv else "mix"
    if not os.path.exists(ipath):
        print("instance not found:", ipath); return
    machine, parts, due = read_instance(ipath)
    if len(due) != len(parts):
        print(f"Error: '{ipath}' has no/incomplete DueDate section "
              f"({len(due)} found, {len(parts)} parts)."); return
    name = os.path.splitext(os.path.basename(ipath))[0]
    print(f"=== MILP  instance={name}  n={len(parts)}  M={M}  mode={mode}  sym_break={sym}  TL={tl}s ===")
    r = solve_milp(machine, parts, due, M, time_limit=tl, sym_break=sym, mode=mode)
    print("\n---- RESULT ----")
    print(f"status   : {r['status']}")
    print(f"objective: {r['obj']:.4f}")
    print(f"bound    : {r['bound']:.4f}   gap: {r['gap']*100:.2f}%")
    print(f"time     : {r['time']:.2f} s   model: {r['nvars']} vars, {r['nconstr']} constrs")
    # machine-parseable line for the comparison harness
    print(f"RESULT instance={name} n={len(parts)} M={M} mode={mode} obj={r['obj']:.4f} "
          f"bound={r['bound']:.4f} gap={r['gap']:.6f} status={r['status']} time={r['time']:.2f} nodes={r['nodes']:.0f}")

if __name__ == "__main__":
    main()
