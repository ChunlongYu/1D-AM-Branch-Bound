#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""读 runs 目录下每个 <inst>_M<M>.txt 的 TRACE 行,画 UB/LB 随时间收敛图。
用法: python plot_trace.py [--rundir runs_small] [--out plots] [--only ht1_3-20]
每个实验一张 png;--grid 把同一规模拼一张。"""
import os, re, glob, argparse
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

TR = re.compile(r"^TRACE t=([\d.]+) ub=([\d.eE+-]+) lb=([\d.eE+-]+) gap=([\d.]+) nodes=(\d+)", re.M)
RES = re.compile(r"RESULT .*?TT=([\d.eE+-]+).*?optimal=(\d).*?lb=([\d.eE+-]+).*?time=([\d.eE+-]+)")

def load(path):
    t = open(path, errors="ignore").read()
    rows = [(float(a),float(b),float(c),float(d),int(e)) for a,b,c,d,e in TR.findall(t)]
    m = RES.search(t)
    fin = None
    if m: fin = dict(ub=float(m.group(1)), proven=m.group(2)=="1",
                     lb=float(m.group(3)), time=float(m.group(4)))
    return rows, fin

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rundir", default="runs_small")
    ap.add_argument("--out", default=None)
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    out = args.out or os.path.join(args.rundir, "plots")
    os.makedirs(out, exist_ok=True)
    n=0
    for path in sorted(glob.glob(os.path.join(args.rundir, "*.txt"))):
        name = os.path.splitext(os.path.basename(path))[0]
        if args.only and args.only not in name: continue
        rows, fin = load(path)
        # append final RESULT point so the line reaches the end
        if fin and rows:
            rows = rows + [(fin["time"], fin["ub"], fin["lb"],
                            (fin["ub"]-fin["lb"])/fin["ub"]*100 if fin["ub"]>1e-9 else 0,
                            rows[-1][4])]
        if len(rows) < 2:   # nothing to draw (solved instantly or stuck pre-loop)
            continue
        t  = [r[0] for r in rows]; ub=[r[1] for r in rows]; lb=[r[2] for r in rows]
        fig, ax = plt.subplots(figsize=(6,3.6))
        ax.step(t, ub, where="post", color="#c0392b", lw=1.8, label="UB (incumbent)")
        ax.step(t, lb, where="post", color="#2471a3", lw=1.8, label="LB (global)")
        ax.fill_between(t, lb, ub, step="post", color="#bbbbbb", alpha=0.25)
        tag = "proven optimal" if (fin and fin["proven"]) else "open (time limit)"
        ax.set_title(f"{name}   [{tag}]", fontsize=9)
        ax.set_xlabel("time (s)"); ax.set_ylabel("total tardiness")
        ax.legend(fontsize=8, loc="best"); ax.grid(alpha=0.3)
        fig.tight_layout(); fig.savefig(os.path.join(out, name+".png"), dpi=110)
        plt.close(fig); n+=1
    print(f"wrote {n} figures -> {out}")

if __name__ == "__main__":
    main()
