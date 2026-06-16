#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
论文 MILP 模型（identical parallel-machine 1D-AM batch scheduling, min total tardiness）
的 gurobipy 复现，用于和 C++ branch-and-bound 对比求解时间。

用法:
    python milp_gurobi.py <instance> <M> [time_limit_sec] [--no-sym]
例:
    python milp_gurobi.py 15part 2 1800
    python milp_gurobi.py 13part 2

<instance> 是 Instance/ 目录下文件名（不含 .txt），例如 10part / 13part / 15part。
截止时间与 C++ 端 main.cpp 中硬编码的一致（见下方 DUE_DATES），保证可直接对比。
"""
import sys, os, time

# 与 C++ main.cpp 完全一致的截止时间
DUE_DATES = {
    "5part":  [8,22,31,29,28],
    "10part": [9,23,32,30,29,7,13,8,20,29],
    "11part": [13,9,21,12,36,33,35,29,18,11,36],
    "12part": [13,9,21,12,36,33,35,29,18,11,36,6],
    "13part": [13,9,21,12,36,33,35,29,18,11,36,6,29],
    "14part": [14,42,10,22,13,37,34,36,30,19,12,37,7,30],
    "15part": [15,43,11,23,14,38,35,37,48,31,20,13,38,8,31],
}

def read_instance(path):
    """解析实例文件，返回 (machine_dict, parts_list)。格式见 README_CN.md 第7节。"""
    toks = open(path).read().split()
    it = iter(toks)
    nxt = lambda: next(it)
    _types_m = int(nxt()); _types_p = int(nxt())
    _num_m   = int(nxt()); num_p   = int(nxt())
    mid = int(nxt()); mnum = int(nxt())
    Vc = float(nxt()); Uc = float(nxt()); Sc = float(nxt())
    Lm = float(nxt()); Wm = float(nxt()); Hm = float(nxt())
    machine = dict(V=Vc, U=Uc, S=Sc, L=Lm, W=Wm, H=Hm)
    parts = []
    for _ in range(num_p):
        pid = int(nxt()); pnum = int(nxt()); orient = int(nxt()); vol = float(nxt())
        l = float(nxt()); w = float(nxt()); h = float(nxt()); supp = float(nxt())
        # 多方向只用第一个，跳过其余
        for _o in range(orient-1):
            for _k in range(4): nxt()
        parts.append(dict(v=vol, l=l, w=w, h=h))
    return machine, parts

def solve_milp(machine, parts, due, M, time_limit=1800.0, sym_break=True, threads=0):
    import gurobipy as gp
    from gurobipy import GRB

    n = len(parts)
    J = range(n)
    Ms = range(M)
    B = range(n)                      # 每台机器 n 个批次位置
    S, V, U = machine["S"], machine["V"], machine["U"]
    LW = machine["L"] * machine["W"]
    l = [p["l"] for p in parts]; w = [p["w"] for p in parts]
    h = [p["h"] for p in parts]; v = [p["v"] for p in parts]
    a = [l[j]*w[j] for j in J]        # 投影面积
    d = list(due)

    # big-M：单机完工时间上界（所有零件各自单批）
    bigM_C = sum(S + V*v[j] + U*h[j] for j in J)
    bigM_link = n                     # 一个批次至多 n 个零件

    m = gp.Model("AM_parallel_milp")
    m.Params.OutputFlag = 1
    if time_limit and time_limit > 0: m.Params.TimeLimit = time_limit
    if threads: m.Params.Threads = threads

    x = m.addVars(J, Ms, B, vtype=GRB.BINARY, name="x")     # 零件 j 在机器 mm 的批次位 b
    z = m.addVars(Ms, B, vtype=GRB.BINARY, name="z")         # 批次位是否启用
    H = m.addVars(Ms, B, lb=0.0, name="H")                   # 批次高度
    P = m.addVars(Ms, B, lb=0.0, name="P")                   # 批次加工时间
    C = m.addVars(Ms, B, lb=0.0, name="C")                   # 批次完工时间
    c = m.addVars(J, lb=0.0, name="c")                       # 零件完工时间
    T = m.addVars(J, lb=0.0, name="T")                       # 零件延误

    m.setObjective(gp.quicksum(T[j] for j in J), GRB.MINIMIZE)

    # 每个零件恰好分到一个机器的一个批次位
    m.addConstrs((gp.quicksum(x[j,mm,b] for mm in Ms for b in B) == 1 for j in J), "assign")
    # 批次位启用
    m.addConstrs((gp.quicksum(x[j,mm,b] for j in J) <= bigM_link * z[mm,b] for mm in Ms for b in B), "linkxz")
    # 面积容量
    m.addConstrs((gp.quicksum(a[j]*x[j,mm,b] for j in J) <= LW for mm in Ms for b in B), "area")
    # 批次高度 = 批内最大高度
    m.addConstrs((H[mm,b] >= h[j]*x[j,mm,b] for j in J for mm in Ms for b in B), "height")
    # 批次加工时间
    m.addConstrs((P[mm,b] == S*z[mm,b] + V*gp.quicksum(v[j]*x[j,mm,b] for j in J) + U*H[mm,b]
                  for mm in Ms for b in B), "proc")
    # 批次顺序完工时间
    m.addConstrs((C[mm,0] >= P[mm,0] for mm in Ms), "Cfirst")
    m.addConstrs((C[mm,b] >= C[mm,b-1] + P[mm,b] for mm in Ms for b in B if b >= 1), "Cseq")
    # 零件完工时间
    m.addConstrs((c[j] >= C[mm,b] - bigM_C*(1 - x[j,mm,b]) for j in J for mm in Ms for b in B), "cj")
    # 延误
    m.addConstrs((T[j] >= c[j] - d[j] for j in J), "tard")
    # 批次连续使用（对称消除）
    m.addConstrs((z[mm,b+1] <= z[mm,b] for mm in Ms for b in B if b+1 < n), "contig")
    # 机器对称消除（可选）
    if sym_break and M >= 2:
        m.addConstrs((gp.quicksum(z[mm,b] for b in B) >= gp.quicksum(z[mm+1,b] for b in B)
                      for mm in range(M-1)), "machsym")

    t0 = time.time()
    m.optimize()
    elapsed = time.time() - t0

    status = {GRB.OPTIMAL:"OPTIMAL", GRB.TIME_LIMIT:"TIME_LIMIT",
              GRB.INFEASIBLE:"INFEASIBLE"}.get(m.Status, str(m.Status))
    obj = m.ObjVal if m.SolCount > 0 else float("nan")
    bound = m.ObjBound if hasattr(m, "ObjBound") else float("nan")
    gap = m.MIPGap if m.SolCount > 0 else float("nan")
    return dict(obj=obj, bound=bound, gap=gap, status=status, time=elapsed,
                nvars=m.NumVars, nconstr=m.NumConstrs)

def main():
    if len(sys.argv) < 3:
        print(__doc__); return
    inst = sys.argv[1]; M = int(sys.argv[2])
    tl = float(sys.argv[3]) if len(sys.argv) > 3 and not sys.argv[3].startswith("--") else 1800.0
    sym = "--no-sym" not in sys.argv

    here = os.path.dirname(os.path.abspath(__file__))
    # Instance/ 在仓库根目录；脚本在 PythonCodes/MILP/ 下，回退两级
    root = os.path.abspath(os.path.join(here, "..", ".."))
    ipath = os.path.join(root, "Instance", inst + ".txt")
    if not os.path.exists(ipath):
        print("instance not found:", ipath); return
    if inst not in DUE_DATES:
        print("no hardcoded due dates for", inst, "- add them to DUE_DATES"); return

    machine, parts = read_instance(ipath)
    due = DUE_DATES[inst]
    assert len(due) == len(parts), f"due dates ({len(due)}) != parts ({len(parts)})"

    print(f"=== MILP  instance={inst}  n={len(parts)}  M={M}  sym_break={sym}  TL={tl}s ===")
    r = solve_milp(machine, parts, due, M, time_limit=tl, sym_break=sym)
    print("\n---- RESULT ----")
    print(f"status   : {r['status']}")
    print(f"objective: {r['obj']:.4f}")
    print(f"bound    : {r['bound']:.4f}   gap: {r['gap']*100:.2f}%")
    print(f"time     : {r['time']:.2f} s")
    print(f"model    : {r['nvars']} vars, {r['nconstr']} constrs")

if __name__ == "__main__":
    main()
