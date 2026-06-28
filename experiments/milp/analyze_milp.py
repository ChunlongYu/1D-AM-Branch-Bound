#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
分析 run_milp_small 的结果:pbatch(理想 p-batch)vs mix(真实 AM M-batch)MILP
在 Derived_Yu2022_small 上的求解难度。产出 Nature/Science 风格图 + 汇总表。

用法:
    python analyze_milp.py                       # 默认 runs_milp_small/
    python analyze_milp.py --rundir runs_milp_small --tl 3600
输出到 <rundir>/analysis/ :
    fig1_time_scatter.(png|pdf)      pbatch vs mix 求解时间 log-log 散点(头条)
    fig2_difficulty.(png|pdf)        难度面板:TF×mode 求解率 + mix 时间随 n/TF
    fig3_gap_convergence.(png|pdf)   mix 超时 gap 分布 + 代表性 UB/LB 收敛轨迹
    summary.md / summary.csv         聚合统计
"""
import os, re, glob, argparse
import numpy as np
import pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import LogLocator, NullFormatter

# ----------------------------------------------------------------------------
# Nature/Science-style rcParams: small sans-serif, thin spines, no top/right.
# ----------------------------------------------------------------------------
def set_style():
    plt.rcParams.update({
        "font.family": "sans-serif",
        "font.sans-serif": ["Arial", "Helvetica", "DejaVu Sans"],
        "font.size": 7, "axes.labelsize": 8, "axes.titlesize": 8,
        "xtick.labelsize": 7, "ytick.labelsize": 7, "legend.fontsize": 6.5,
        "axes.linewidth": 0.7, "xtick.major.width": 0.7, "ytick.major.width": 0.7,
        "xtick.major.size": 2.6, "ytick.major.size": 2.6,
        "xtick.direction": "out", "ytick.direction": "out",
        "axes.spines.top": False, "axes.spines.right": False,
        "legend.frameon": False, "figure.dpi": 150, "savefig.dpi": 360,
        "savefig.bbox": "tight", "savefig.pad_inches": 0.02,
        "lines.linewidth": 1.0, "pdf.fonttype": 42, "ps.fonttype": 42,
    })

# colour-blind-safe pair
C_PB  = "#4C72B0"   # pbatch  (blue)
C_MIX = "#C44E52"   # mix     (red)
C_GREY= "#9AA0A6"
MM = 1/25.4

def md_table(df, index=True):
    cols = ([df.index.name or ""] if index else []) + [str(c) for c in df.columns]
    head = "| " + " | ".join(cols) + " |"
    sep  = "|" + "|".join("---" for _ in cols) + "|"
    body=[]
    for idx, row in df.iterrows():
        cells = ([str(idx)] if index else []) + [str(v) for v in row]
        body.append("| " + " | ".join(cells) + " |")
    return "\n".join([head, sep, *body])         # mm -> inch

def save(fig, base):
    for ext in ("png", "pdf"):
        fig.savefig(base + "." + ext)
    plt.close(fig)

# ----------------------------------------------------------------------------
# Load + parse
# ----------------------------------------------------------------------------
def parse_name(name):
    # ht1_2-15_0.6_0.3_3  -> fam=ht1 grp=2 n=15 TF=0.6 RDD=0.3 seed=3
    m = re.match(r"(ht\d)_(\d+)-(\d+)_([\d.]+)_([\d.]+)_(\d+)", name)
    if not m: return dict(fam="?", n=np.nan, TF=np.nan, RDD=np.nan)
    return dict(fam=m.group(1), grp=int(m.group(2)), n=int(m.group(3)),
                TF=float(m.group(4)), RDD=float(m.group(5)), seed=int(m.group(6)))

def load(rundir, tl):
    df = pd.read_csv(os.path.join(rundir, "master_milp.csv"))
    for k in ("obj", "bound", "gap_pct", "time_s", "nodes", "n", "M"):
        df[k] = pd.to_numeric(df[k], errors="coerce")
    meta = df["instance"].apply(parse_name).apply(pd.Series)
    df = pd.concat([df, meta[["fam", "TF", "RDD"]]], axis=1)
    df["solved"] = df["status"] == "OPTIMAL"
    df["timeout"] = df["status"] == "TIME_LIMIT"
    # clamp plotted time to TL (a couple ran a hair over)
    df["t"] = df["time_s"].clip(upper=tl)
    df["nontrivial"] = df["obj"] > 1e-6
    return df

def read_trace(path):
    rows = []
    for ln in open(path, errors="ignore"):
        m = re.match(r"TRACE t=([\d.]+) ub=([\d.eE+-]+) lb=([\d.eE+-]+)", ln)
        if m: rows.append((float(m.group(1)), float(m.group(2)), float(m.group(3))))
    return rows

# ----------------------------------------------------------------------------
# Figure 1 — headline: pbatch vs mix solve time, log-log
# ----------------------------------------------------------------------------
def fig_time_scatter(df, tl, outbase):
    piv = df.pivot_table(index=["instance", "M"], columns="mode",
                         values=["t", "solved", "obj"], aggfunc="first")
    fig, ax = plt.subplots(figsize=(89*MM, 78*MM))
    lo, hi = 5e-3, tl*1.6
    ax.plot([lo, hi], [lo, hi], color=C_GREY, lw=0.7, ls="--", zorder=1)
    ax.axhline(tl, color=C_GREY, lw=0.6, ls=":", zorder=1)
    ax.text(lo*1.3, tl*1.05, "mix time limit", color=C_GREY, fontsize=6, va="bottom")

    xp = piv[("t", "pbatch")].values
    yp = piv[("t", "mix")].values
    solved = piv[("solved", "mix")].values.astype(bool)
    # solved mix (filled) vs timed-out mix (open, sit on ceiling)
    ax.scatter(xp[solved], yp[solved], s=16, facecolor=C_MIX, edgecolor="white",
               linewidth=0.3, label="mix solved", zorder=3)
    ax.scatter(xp[~solved], yp[~solved], s=16, facecolor="none", edgecolor=C_MIX,
               linewidth=0.8, label="mix hit time limit", zorder=3)
    ax.set_xscale("log"); ax.set_yscale("log")
    ax.set_xlim(lo, hi); ax.set_ylim(lo, hi)
    ax.set_xlabel("p-batch MILP solve time (s)")
    ax.set_ylabel("mix-batch (AM) MILP solve time (s)")
    ax.legend(loc="lower right", handletextpad=0.4)
    for axis in (ax.xaxis, ax.yaxis):
        axis.set_minor_formatter(NullFormatter())
    # annotate orders of magnitude
    med_ratio = np.nanmedian(yp / xp)
    ax.set_title(f"Volume coupling: mix is ~{med_ratio:.0f}× slower (median)", fontsize=7.5)
    save(fig, outbase)
    return med_ratio

# ----------------------------------------------------------------------------
# Figure 2 — difficulty drivers: solved-rate by TF×mode ; mix time vs n by TF
# ----------------------------------------------------------------------------
def fig_difficulty(df, tl, outbase):
    fig, axes = plt.subplots(1, 2, figsize=(183*MM, 72*MM))

    # (a) solved fraction within TL, grouped by TF, pbatch vs mix
    ax = axes[0]
    tfs = sorted(df["TF"].dropna().unique())
    width = 0.36; xpos = np.arange(len(tfs))
    for k, (mode, col) in enumerate([("pbatch", C_PB), ("mix", C_MIX)]):
        fr = [df[(df["mode"] == mode) & (df["TF"] == tf)]["solved"].mean() for tf in tfs]
        ax.bar(xpos + (k-0.5)*width, fr, width, color=col, edgecolor="white",
               linewidth=0.5, label=mode)
        for x, f in zip(xpos + (k-0.5)*width, fr):
            ax.text(x, f+0.02, f"{f*100:.0f}%", ha="center", va="bottom", fontsize=6)
    ax.set_xticks(xpos); ax.set_xticklabels([f"TF={t:g}\n({'loose' if t<0.5 else 'tight'} due dates)" for t in tfs])
    ax.set_ylabel("fraction solved within 1 h"); ax.set_ylim(0, 1.18)
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, 1.16), ncol=2, columnspacing=1.2)
    ax.set_title("a", loc="left", fontweight="bold")

    # (b) mix solve time vs n, coloured by TF (timeouts on ceiling, open)
    ax = axes[1]
    mix = df[df["mode"] == "mix"]
    cmap = {0.3: "#55A868", 0.6: "#C44E52"}
    for tf, g in mix.groupby("TF"):
        c = cmap.get(tf, C_GREY)
        sv, to = g[g["solved"]], g[~g["solved"]]
        jit = (np.random.RandomState(1).rand(len(sv))-0.5)*0.8
        ax.scatter(sv["n"]+jit, sv["t"], s=14, color=c, edgecolor="white",
                   linewidth=0.3, label=f"TF={tf:g} solved")
        jit2 = (np.random.RandomState(2).rand(len(to))-0.5)*0.8
        ax.scatter(to["n"]+jit2, to["t"], s=14, facecolor="none", edgecolor=c,
                   linewidth=0.8, label=f"TF={tf:g} timeout")
    ax.axhline(tl, color=C_GREY, lw=0.6, ls=":")
    ax.set_yscale("log"); ax.set_xlabel("number of parts  n")
    ax.set_ylabel("mix MILP solve time (s)")
    ax.set_xticks([10, 15, 20, 25, 30, 50])
    ax.legend(loc="lower right", ncol=2, columnspacing=0.8, handletextpad=0.3)
    ax.set_title("b", loc="left", fontweight="bold")
    save(fig, outbase)

# ----------------------------------------------------------------------------
# Figure 3 — mix gap distribution + representative convergence traces
# ----------------------------------------------------------------------------
def fig_gap_convergence(df, rundir, tl, outbase):
    fig, axes = plt.subplots(1, 2, figsize=(183*MM, 72*MM))

    # (a) ECDF of final optimality gap for nontrivial mix timeouts
    ax = axes[0]
    to = df[(df["mode"] == "mix") & (~df["solved"]) & (df["nontrivial"])]
    g = np.sort(to["gap_pct"].dropna().values)
    if len(g):
        y = np.arange(1, len(g)+1)/len(g)
        ax.step(g, y, where="post", color=C_MIX, lw=1.3)
        ax.fill_between(g, 0, y, step="post", color=C_MIX, alpha=0.12)
    ax.set_xlabel("final optimality gap (%)"); ax.set_ylabel("cumulative fraction of\nunsolved mix instances")
    ax.set_xlim(0, 100); ax.set_ylim(0, 1.02)
    ax.set_title("a", loc="left", fontweight="bold")
    ax.text(0.5, 0.06, f"n = {len(g)} unsolved, non-trivial mix runs", transform=ax.transAxes,
            ha="center", fontsize=6, color=C_GREY)

    # (b) optimality gap over time for hardest mix instances (scale-free)
    ax = axes[1]
    cand = to.copy()
    cand["base"] = cand["instance"] + "_M" + cand["M"].astype(int).astype(str) + "_mix"
    cand = cand.sort_values("obj", ascending=False)
    picks, seen_n = [], set()
    for _, r in cand.iterrows():     # one per n, hardest first
        if r["n"] in seen_n: continue
        picks.append(r); seen_n.add(r["n"])
        if len(picks) >= 5: break
    pal = ["#C44E52", "#DD8452", "#55A868", "#4C72B0", "#8172B3"]
    for i, r in enumerate(sorted(picks, key=lambda z: z["n"])):
        rows = read_trace(os.path.join(rundir, r["base"] + ".txt"))
        if len(rows) < 2: continue
        t  = np.array([x[0] for x in rows]); ub = np.array([x[1] for x in rows])
        lb = np.array([x[2] for x in rows])
        gap = np.where(np.abs(ub) > 1e-9, (ub - lb)/ub*100.0, 0.0)
        ax.plot(t, gap, color=pal[i], lw=1.2,
                label=f"{r['instance'].replace('_','‑')} · M{int(r['M'])} (n={int(r['n'])})")
    ax.set_xlabel("wall-clock time (s)"); ax.set_ylabel("optimality gap (%)")
    ax.set_xlim(0, tl); ax.set_ylim(0, 100)
    ax.legend(loc="lower right", fontsize=5.4, handlelength=1.4)
    ax.set_title("b   mix MILP: the gap plateaus high", loc="left", fontweight="bold", fontsize=7)
    save(fig, outbase)

# ----------------------------------------------------------------------------
# Summary tables
# ----------------------------------------------------------------------------
def write_summary(df, tl, outdir):
    lines = ["# MILP pbatch vs mix — 汇总\n"]
    def blk(sub, title):
        s = sub.groupby("mode").agg(
            n_runs=("solved", "size"), solved=("solved", "sum"),
            median_time=("t", "median"), max_time=("t", "max"))
        s["solved_pct"] = (s["solved"]/s["n_runs"]*100).round(1)
        lines.append(f"\n## {title}\n")
        lines.append(md_table(s[["n_runs", "solved", "solved_pct", "median_time", "max_time"]].round(2)))
    blk(df, "全部")
    blk(df[df["TF"] == 0.3], "TF=0.3(松交期)")
    blk(df[df["TF"] == 0.6], "TF=0.6(紧交期)")
    blk(df[df["nontrivial"]], "仅非平凡(obj>0)")
    # per-n mix solved
    mix = df[df["mode"] == "mix"]
    pern = mix.groupby("n").agg(runs=("solved","size"), solved=("solved","sum"),
                                med_t=("t","median")).round(1)
    lines.append("\n## mix 按 n\n"); lines.append(md_table(pern))
    open(os.path.join(outdir, "summary.md"), "w").write("\n".join(str(x) for x in lines))
    df.to_csv(os.path.join(outdir, "summary.csv"), index=False)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rundir", default="runs_milp_small")
    ap.add_argument("--tl", type=float, default=3600.0)
    args = ap.parse_args()
    set_style()
    outdir = os.path.join(args.rundir, "analysis"); os.makedirs(outdir, exist_ok=True)
    df = load(args.rundir, args.tl)
    ratio = fig_time_scatter(df, args.tl, os.path.join(outdir, "fig1_time_scatter"))
    fig_difficulty(df, args.tl, os.path.join(outdir, "fig2_difficulty"))
    fig_gap_convergence(df, args.rundir, args.tl, os.path.join(outdir, "fig3_gap_convergence"))
    write_summary(df, args.tl, outdir)
    # console headline numbers
    for mode in ("pbatch", "mix"):
        s = df[df["mode"] == mode]
        print(f"{mode:7s}: solved {s['solved'].sum():2d}/{len(s)}  "
              f"median {s['t'].median():.2f}s  max {s['t'].max():.1f}s")
    print(f"median mix/pbatch time ratio = {ratio:.0f}x")
    print(f"figures + summary -> {outdir}")

if __name__ == "__main__":
    main()
