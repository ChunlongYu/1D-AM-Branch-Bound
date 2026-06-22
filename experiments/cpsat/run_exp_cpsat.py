#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
run_exp_cpsat.py —— CP-SAT(OR-Tools)批量实验主程序。
遍历 INSTANCES × MACHINES,逐条即时写 experiments/results/cpsat_results.csv(每行 flush)。
截止时间从实例文件 DueDate 段读取(与 B&B / MILP 同口径)。

前置:pip install ortools
运行(本目录 experiments/cpsat/ 下):python run_exp_cpsat.py
"""
import os, csv, time, traceback
import cpsat_ortools as cs

# ---- 实验配置 ----
TIME_LIMIT = 1800.0
MACHINES   = [2, 3, 4]
WORKERS    = 8
SCALE      = 10000
INSTANCES  = [
    "5part","10part","10part_2","10part_3","10parts_4",
    "11part","12part","13part","14part","15part","15part_2-S",
    "20part_3-S","20part_4-S",
]
# ------------------

HERE=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(HERE,"..",".."))
INSTDIR=os.path.join(ROOT,"data")
OUTDIR =os.path.join(ROOT,"experiments","results")

def main():
    os.makedirs(OUTDIR,exist_ok=True)
    out=os.path.join(OUTDIR,"cpsat_results.csv")
    f=open(out,"w",newline=""); w=csv.writer(f)
    w.writerow(["instance","n","M","obj","bound","gap","status","time_sec"]); f.flush()
    print(f"CP-SAT batch  (TL={TIME_LIMIT}s, workers={WORKERS}, scale={SCALE})")
    print(f"{'instance':14s} {'n':>3s} {'M':>2s} {'obj':>10s} {'gap%':>7s} {'status':>11s} {'time(s)':>9s}")
    print("-"*64)
    for inst in INSTANCES:
        ip=os.path.join(INSTDIR,inst+".txt")
        if not os.path.exists(ip): print("skip(missing):",inst); continue
        machine,parts,due=cs.read_instance(ip)
        if len(due)!=len(parts): print("skip(no DueDate):",inst); continue
        n=len(parts)
        for M in MACHINES:
            try:
                r=cs.solve_cpsat(machine,parts,due,M,time_limit=TIME_LIMIT,workers=WORKERS,scale=SCALE)
                obj,bound,gap,status,tsec=r["obj"],r["bound"],r["gap"],r["status"],r["time"]
            except Exception as e:
                obj=bound=gap=float("nan"); status=f"ERROR:{type(e).__name__}"; tsec=0.0
                traceback.print_exc()
            w.writerow([inst,n,M,f"{obj:.4f}",f"{bound:.4f}",f"{gap:.6f}",status,f"{tsec:.2f}"]); f.flush()
            gp=gap*100 if gap==gap else float("nan")
            print(f"{inst:14s} {n:3d} {M:2d} {obj:10.3f} {gp:7.2f} {status:>11s} {tsec:9.2f}")
    f.close(); print(f"\nDone -> {out}")

if __name__=="__main__":
    main()
