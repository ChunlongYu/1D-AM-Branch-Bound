#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
长任务 runner:对 Derived_Yu2022_small 的全部算例 × M∈{2,3,4} × {pbatch, mix}
用 Gurobi 求解,每例 TL=3600s。带断点续传 + 逐实验落盘 + UB/LB 收敛轨迹。

每个实验写一个 .txt:头部(实例/配置)+ 每 ~10s 的 TRACE(incumbent/bound) + 最终 RESULT。
轨迹与 RESULT 行采用与 C++ B&B 相同的格式,可直接用 plot_trace.py 画图:
    python plot_trace.py --rundir runs_milp_small

同时汇总 master_milp.csv(每行一个实验)。

用法(在 experiments/milp/ 下,机器需有 gurobipy):
    python run_milp_small.py                                  # 全量 24×3×2,TL=3600
    python run_milp_small.py --tl 60                          # 先小时限验证流程
    python run_milp_small.py --M 2 --modes mix                # 只跑某子集
    python run_milp_small.py --resume                         # 跳过已完成(.txt 里已有 RESULT)
    python run_milp_small.py --maxn 20                        # 只跑 n<=20(mix 大 n 必爆,可先限)
"""
import os, sys, glob, csv, time, argparse, datetime, math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
from milp_gurobi import read_instance, solve_milp


def make_trace_cb(traceint):
    """返回 (callback, trace_lines)。callback 每 traceint 秒记一条
    'TRACE t ub lb gap nodes'(只在已有 incumbent 时记)。"""
    import gurobipy as gp
    from gurobipy import GRB
    trace_lines = []
    state = {"last": -1e9}

    def cb(model, where):
        if where != GRB.Callback.MIP:
            return
        t = model.cbGet(GRB.Callback.RUNTIME)
        if t - state["last"] < traceint:
            return
        ub = model.cbGet(GRB.Callback.MIP_OBJBST)   # incumbent
        lb = model.cbGet(GRB.Callback.MIP_OBJBND)   # best bound
        nodes = model.cbGet(GRB.Callback.MIP_NODCNT)
        if ub >= GRB.INFINITY:                       # no incumbent yet
            return
        gap = (ub - lb) / ub * 100.0 if abs(ub) > 1e-9 else 0.0
        trace_lines.append(f"TRACE t={t:.1f} ub={ub:.6g} lb={lb:.6g} gap={gap:.2f} nodes={int(nodes)}")
        state["last"] = t

    return cb, trace_lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--datadir", default=os.path.join(ROOT, "Instances", "Derived_Yu2022_small"))
    ap.add_argument("--outdir",  default=os.path.join(HERE, "runs_milp_small"))
    ap.add_argument("--tl",       type=float, default=3600.0)
    ap.add_argument("--traceint", type=float, default=10.0)
    ap.add_argument("--M",        default="2,3,4")
    ap.add_argument("--modes",    default="pbatch,mix")
    ap.add_argument("--threads",  type=int, default=0)
    ap.add_argument("--maxn",     type=int, default=0, help="skip instances with n>maxn (0=no cap)")
    ap.add_argument("--resume",   action="store_true")
    args = ap.parse_args()

    os.makedirs(args.outdir, exist_ok=True)
    insts = sorted(glob.glob(os.path.join(args.datadir, "*.txt")))
    Ms    = [int(x) for x in args.M.split(",")]
    modes = [x.strip() for x in args.modes.split(",") if x.strip()]

    # parse + sort by n ascending (tractable instances finish first)
    parsed = []
    for ip in insts:
        machine, parts, due = read_instance(ip)
        name = os.path.splitext(os.path.basename(ip))[0]
        if len(due) != len(parts):
            print(f"[skip] {name}: no/incomplete DueDate"); continue
        if args.maxn and len(parts) > args.maxn:
            continue
        parsed.append((len(parts), name, machine, parts, due))
    parsed.sort(key=lambda r: r[0])

    csv_path = os.path.join(args.outdir, "master_milp.csv")
    new_csv = not os.path.exists(csv_path)
    fcsv = open(csv_path, "a", newline="")
    wr = csv.writer(fcsv)
    if new_csv:
        wr.writerow(["instance", "n", "M", "mode", "status", "obj", "bound",
                     "gap_pct", "time_s", "nodes", "nvars", "nconstr"]); fcsv.flush()

    total = len(parsed) * len(Ms) * len(modes); done = 0
    for n, name, machine, parts, due in parsed:
        for M in Ms:
            for mode in modes:
                done += 1
                tag = f"{name}_M{M}_{mode}"
                outfile = os.path.join(args.outdir, tag + ".txt")
                if args.resume and os.path.exists(outfile) and \
                        "RESULT " in open(outfile, errors="ignore").read():
                    print(f"[{done}/{total}] skip {tag} (done)"); continue
                print(f"[{done}/{total}] run  {tag}  n={n} TL={args.tl:g}s ...", flush=True)

                cb, trace_lines = make_trace_cb(args.traceint)
                t0 = time.time()
                try:
                    r = solve_milp(machine, parts, due, M, time_limit=args.tl,
                                   sym_break=True, threads=args.threads, output=False,
                                   mode=mode, callback=cb)
                except Exception as e:
                    r = dict(obj=float('nan'), bound=float('nan'), gap=float('nan'),
                             status=f"ERROR:{e}", time=time.time() - t0,
                             nvars=0, nconstr=0, nodes=0)
                wall = time.time() - t0

                proven = 1 if r["status"] == "OPTIMAL" else 0
                obj  = r["obj"];  bnd = r["bound"]
                gpct = (r["gap"] * 100 if r["gap"] == r["gap"] else float("nan"))
                # write per-experiment .txt (B&B-compatible TRACE / RESULT for plot_trace.py)
                with open(outfile, "w") as fo:
                    fo.write(f"# experiment: {name}  M={M}  mode={mode}  (MILP / Gurobi)\n")
                    fo.write(f"# config: sym_break=1 threads={args.threads} ; TL={args.tl:g}s "
                             f"traceint={args.traceint:g}s\n")
                    fo.write(f"# date: {datetime.datetime.now().isoformat(timespec='seconds')}  "
                             f"wall={wall:.1f}s\n")
                    fo.write(f"# model: {r['nvars']} vars, {r['nconstr']} constrs\n")
                    fo.write("# columns of TRACE lines: t(s) ub lb gap(%) nodes\n")
                    fo.write("#" + "=" * 70 + "\n\n")
                    fo.write("\n".join(trace_lines))
                    if trace_lines: fo.write("\n")
                    obj_s = f"{obj:.6g}" if obj == obj else "nan"
                    lb_s  = f"{bnd:.6g}" if bnd == bnd else "nan"
                    gp_s  = f"{gpct:.4f}" if gpct == gpct else "nan"
                    fo.write(f"\nRESULT instance={name} n={n} M={M} mode={mode} "
                             f"TT={obj_s} optimal={proven} lb={lb_s} gap={gp_s} "
                             f"time={r['time']:.4f} nodes={r.get('nodes', 0):.0f} "
                             f"status={r['status']}\n")

                wr.writerow([name, n, M, mode, r["status"],
                             f"{obj:.4f}" if obj == obj else "nan",
                             f"{bnd:.4f}" if bnd == bnd else "nan",
                             f"{gpct:.2f}" if gpct == gpct else "nan",
                             f"{r['time']:.2f}", f"{r.get('nodes', 0):.0f}",
                             r["nvars"], r["nconstr"]]); fcsv.flush()
                print(f"      -> {mode:6s} status={r['status']:10s} "
                      f"obj={obj:.4f} gap={gpct:.2f}% time={r['time']:.1f}s")
    fcsv.close()
    print(f"\nDone. CSV -> {csv_path}")
    print(f"画收敛图:  python plot_trace.py --rundir {os.path.relpath(args.outdir, HERE)}")


if __name__ == "__main__":
    main()
