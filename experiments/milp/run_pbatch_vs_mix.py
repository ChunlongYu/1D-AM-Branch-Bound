#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
对比实验:p-batch(理想并行批,P=S+U*max h) vs mix-batch(我们的真实 AM 模型,
P=S+V*Σvol+U*max h)对同一 MILP 用 Gurobi 求解的难度影响。

控制变量:除了"批处理时间是否含 V*Σvol 这一项"以外,实例 / 机器数 / 交期 / 面积容量 /
目标(总延误)/ Gurobi 设置 全部相同。所以两栏之差 = 只来自串行体积耦合项。

用法(在 experiments/milp/ 下,机器需有 gurobipy):
    python run_pbatch_vs_mix.py                         # 默认网格,每例 60s
    python run_pbatch_vs_mix.py --tl 120 --M 2,3
    python run_pbatch_vs_mix.py --instances 12part,13part,14part --tl 300
结果写到 results_pbatch_vs_mix.csv,并在终端打印对比表。

正确性参考(已用纯枚举核对,mix 与我们的 C++ oracle/pbb 逐位一致):
    5part  M=1  mix=40.5691  pbatch=20.7000
    5part  M=2  mix=19.1778  pbatch=16.4000
跑出来这几个值若对得上,说明模型与你机器上的 Gurobi 口径一致。
"""
import sys, os, csv, argparse, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from milp_gurobi import read_instance, solve_milp

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--datadir", default=os.path.join("..", "..", "data"))
    ap.add_argument("--instances", default="5part,10part,11part,12part,13part,14part")
    ap.add_argument("--M", default="2")
    ap.add_argument("--modes", default="pbatch,mix")
    ap.add_argument("--tl", type=float, default=60.0)
    ap.add_argument("--threads", type=int, default=0)
    ap.add_argument("--maxn", type=int, default=0, help="skip instances with n>maxn (0=no cap)")
    ap.add_argument("--out", default="results_pbatch_vs_mix.csv")
    args = ap.parse_args()

    import glob as _glob
    if args.instances.strip().lower() == "all":
        insts = sorted(os.path.splitext(os.path.basename(p))[0]
                       for p in _glob.glob(os.path.join(args.datadir, "*.txt")))
    else:
        insts = [x.strip() for x in args.instances.split(",") if x.strip()]
    Ms    = [int(x) for x in args.M.split(",")]
    modes = [x.strip() for x in args.modes.split(",")]

    rows = []
    fcsv = open(args.out, "w", newline="")
    wr = csv.writer(fcsv)
    wr.writerow(["instance","n","M","mode","status","obj","bound","gap_pct",
                 "time_s","nodes","nvars","nconstr"])
    fcsv.flush()

    for name in insts:
        path = os.path.join(args.datadir, name + ".txt")
        if not os.path.exists(path):
            print(f"[skip] {path} not found"); continue
        machine, parts, due = read_instance(path)
        if len(due) != len(parts):
            print(f"[skip] {name}: no DueDate section"); continue
        n = len(parts)
        if args.maxn and n > args.maxn:
            print(f"[skip] {name}: n={n} > maxn={args.maxn}"); continue
        for M in Ms:
            for mode in modes:
                print(f"\n>>> {name} n={n} M={M} mode={mode} (TL={args.tl}s) ...", flush=True)
                t0 = time.time()
                try:
                    r = solve_milp(machine, parts, due, M, time_limit=args.tl,
                                   sym_break=True, threads=args.threads, output=False, mode=mode)
                except Exception as e:
                    print(f"    ERROR: {e}")
                    r = dict(obj=float('nan'), bound=float('nan'), gap=float('nan'),
                             status="ERROR", time=time.time()-t0, nvars=0, nconstr=0, nodes=0)
                gp = r['gap']*100 if r['gap']==r['gap'] else float('nan')
                print(f"    {mode:7s} status={r['status']:10s} obj={r['obj']:.4f} "
                      f"gap={gp:.2f}% time={r['time']:.2f}s nodes={r.get('nodes',0):.0f}")
                wr.writerow([name,n,M,mode,r['status'],f"{r['obj']:.4f}",f"{r['bound']:.4f}",
                             f"{gp:.2f}",f"{r['time']:.2f}",f"{r.get('nodes',0):.0f}",
                             r['nvars'],r['nconstr']])
                fcsv.flush()
                rows.append(dict(name=name,n=n,M=M,**r,gap_pct=gp))
    fcsv.close()

    # side-by-side summary
    print("\n" + "="*92)
    print(f"{'instance':<11}{'n':>3}{'M':>3} | {'pbatch':>26} | {'mix':>26} | {'mix/pbatch':>10}")
    print(f"{'':11}{'':3}{'':3} | {'status   obj      t(s)':>26} | {'status   obj      t(s)':>26} |")
    print("-"*92)
    by = {}
    for r in rows: by[(r['name'],r['M'],r['mode'])] = r
    seen=set()
    for r in rows:
        key=(r['name'],r['M'])
        if key in seen: continue
        seen.add(key)
        pb = by.get((r['name'],r['M'],'pbatch')); mx = by.get((r['name'],r['M'],'mix'))
        def fmt(x):
            if x is None: return f"{'-':>26}"
            st = x['status'][:8]
            return f"{st:<9}{x['obj']:>8.2f}{x['time']:>9.1f}"
        ratio = "-"
        if pb and mx and pb['time']>1e-9: ratio = f"{mx['time']/pb['time']:.1f}x"
        print(f"{r['name']:<11}{r['n']:>3}{r['M']:>3} | {fmt(pb)} | {fmt(mx)} | {ratio:>10}")
    print("="*92)
    print(f"CSV -> {args.out}")
    print("解读:同一实例同一 M,两栏只差 V*Σvol 项。看 mix/pbatch 的时间比、以及谁先 TIME_LIMIT、谁 gap 更大。")

if __name__ == "__main__":
    main()
