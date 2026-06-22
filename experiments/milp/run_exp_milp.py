#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
run_exp_milp.py  —— MILP(Gurobi)批量实验主程序
遍历 INSTANCES × MACHINES,逐条把结果即时写入 results/milp_results.csv(每行 flush)。
截止时间从实例文件末尾的 DueDate 段读取(与 C++ B&B 完全同口径)。

前置:本机已安装并激活 Gurobi(gurobipy)。
运行(在本目录 PythonCodes/MILP/ 下):
    python run_exp_milp.py
"""
import os, csv, time, traceback
import milp_gurobi as mg   # 复用 read_instance / solve_milp

# ---- 实验配置(按需修改)---------------------------------------------------
TIME_LIMIT = 1800.0                 # 每次运行时间限制(秒)
MACHINES   = [2, 3, 4]              # 机器数 M
SYM_BREAK  = True                   # 是否加机器对称消除约束
INSTANCES  = [                      # 实例(Instance/ 下,不含 .txt)
    "5part",
    "10part", "10part_2", "10part_3", "10parts_4",
    "11part", "12part", "13part", "14part",
    "15part", "15part_2-S",
    "20part_3-S", "20part_4-S",
]
# ---------------------------------------------------------------------------

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
INSTDIR = os.path.join(ROOT, "data")
OUTDIR  = os.path.join(ROOT, "experiments", "results")

def main():
    os.makedirs(OUTDIR, exist_ok=True)
    out = os.path.join(OUTDIR, "milp_results.csv")
    f = open(out, "w", newline="")
    wcsv = csv.writer(f)
    wcsv.writerow(["instance","n","M","obj","bound","gap","status","time_sec","nvars","nconstr"])
    f.flush()

    print(f"MILP (Gurobi) batch experiment  (time limit {TIME_LIMIT}s, sym_break={SYM_BREAK})")
    print(f"{'instance':14s} {'n':>3s} {'M':>2s} {'obj':>10s} {'gap%':>7s} {'status':>11s} {'time(s)':>9s}")
    print("-"*64)

    for inst in INSTANCES:
        ipath = os.path.join(INSTDIR, inst + ".txt")
        if not os.path.exists(ipath):
            print(f"skip (missing): {inst}"); continue
        machine, parts, due = mg.read_instance(ipath)
        if len(due) != len(parts):
            print(f"skip (no/incomplete DueDate): {inst}"); continue
        n = len(parts)
        for M in MACHINES:
            try:
                r = mg.solve_milp(machine, parts, due, M,
                                  time_limit=TIME_LIMIT, sym_break=SYM_BREAK, output=False)
                obj, bound, gap = r["obj"], r["bound"], r["gap"]
                status, tsec = r["status"], r["time"]
                nv, nc = r["nvars"], r["nconstr"]
            except Exception as e:
                obj=bound=gap=float("nan"); status=f"ERROR:{type(e).__name__}"; tsec=0.0; nv=nc=0
                traceback.print_exc()
            wcsv.writerow([inst, n, M,
                           f"{obj:.4f}", f"{bound:.4f}", f"{gap:.6f}",
                           status, f"{tsec:.2f}", nv, nc])
            f.flush()
            gp = (gap*100) if gap==gap else float("nan")   # nan-safe
            print(f"{inst:14s} {n:3d} {M:2d} {obj:10.3f} {gp:7.2f} {status:>11s} {tsec:9.2f}")
    f.close()
    print(f"\nDone. Results -> {out}")

if __name__ == "__main__":
    main()
