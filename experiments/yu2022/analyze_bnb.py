#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Analyse parallel B&B results (runs_bnb_v2/master_results.csv) -> 3-panel figure."""
import os, argparse
import numpy as np, pandas as pd
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D

MM = 1/25.4
MCOL = {2: "#4C72B0", 3: "#DD8452", 4: "#55A868"}
NVALS = [10, 15, 20, 25, 30, 50]

def set_style():
    plt.rcParams.update({
        "font.family": "sans-serif", "font.sans-serif": ["Arial","Helvetica","DejaVu Sans"],
        "font.size": 7, "axes.labelsize": 8, "axes.titlesize": 8,
        "xtick.labelsize": 7, "ytick.labelsize": 7, "legend.fontsize": 6.3,
        "axes.linewidth": 0.7, "xtick.major.width": 0.7, "ytick.major.width": 0.7,
        "xtick.major.size": 2.6, "ytick.major.size": 2.6,
        "axes.spines.top": False, "axes.spines.right": False, "legend.frameon": False,
        "savefig.dpi": 360, "savefig.bbox": "tight", "savefig.pad_inches": 0.03, "pdf.fonttype": 42,
    })

def load(rundir):
    df = pd.read_csv(os.path.join(rundir, "master_results.csv"))
    for k in ("obj","lb","gap_pct","time_s","nodes","n","M","proven"):
        df[k] = pd.to_numeric(df[k], errors="coerce")
    df["solved"] = df["proven"] == 1
    df["xi"] = df["n"].map({n: i for i, n in enumerate(NVALS)})
    return df

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rundir", default="runs_bnb_v2")
    ap.add_argument("--tl", type=float, default=3600.0)
    args = ap.parse_args()
    set_style()
    outdir = os.path.join(args.rundir, "analysis"); os.makedirs(outdir, exist_ok=True)
    df = load(args.rundir); TL = args.tl
    dx = {2: -0.18, 3: 0.0, 4: 0.18}
    fig, axes = plt.subplots(1, 3, figsize=(183*MM, 60*MM))

    ax = axes[0]
    for M in (2, 3, 4):
        g = df[df.M == M]; sv, to = g[g.solved], g[~g.solved]
        ax.scatter(sv.xi+dx[M], sv.time_s.clip(lower=1e-3), s=15, color=MCOL[M],
                   edgecolor="white", linewidth=0.3, zorder=3)
        ax.scatter(to.xi+dx[M], np.full(len(to), TL), s=15, facecolor="none",
                   edgecolor=MCOL[M], linewidth=0.8, zorder=3)
    ax.axhline(TL, color="#9AA0A6", lw=0.6, ls=":")
    ax.set_yscale("log"); ax.set_ylim(5e-3, TL*3)
    ax.set_xticks(range(len(NVALS))); ax.set_xticklabels(NVALS)
    ax.set_xlabel("number of parts  n"); ax.set_ylabel("solve time (s)")
    h = [Line2D([],[],marker='o',ls='',mfc=MCOL[M],mec='white',mew=0.3,ms=4,label="M=%d"%M) for M in (2,3,4)]
    h += [Line2D([],[],marker='o',ls='',mfc='#777',mec='white',mew=0.3,ms=4,label="proven"),
          Line2D([],[],marker='o',ls='',mfc='none',mec='#555',mew=0.8,ms=4,label="timeout")]
    ax.legend(handles=h, loc="lower right", handletextpad=0.3, labelspacing=0.25)
    ax.set_title("a  exact-solvability frontier", loc="left", fontweight="bold", fontsize=7.5)

    ax = axes[1]
    for M in (2, 3, 4):
        g = df[df.M == M]
        ax.scatter(g.xi+dx[M], g.gap_pct, s=15, color=MCOL[M], edgecolor="white",
                   linewidth=0.3, label="M=%d"%M)
    ax.set_xticks(range(len(NVALS))); ax.set_xticklabels(NVALS)
    ax.set_ylim(-4, 104)
    ax.set_xlabel("number of parts  n"); ax.set_ylabel("final optimality gap (%)")
    ax.legend(loc="upper left", handletextpad=0.3)
    ax.set_title("b  bound-limited beyond the frontier", loc="left", fontweight="bold", fontsize=7.5)

    ax = axes[2]
    grid = np.full((len(NVALS), 3), np.nan)
    for i, n in enumerate(NVALS):
        for j, M in enumerate((2, 3, 4)):
            cell = df[(df.n == n) & (df.M == M)]
            if len(cell): grid[i, j] = cell.solved.mean()
    im = ax.imshow(grid, cmap="YlGnBu", vmin=0, vmax=1, aspect="auto", origin="upper")
    ax.set_xticks(range(3)); ax.set_xticklabels([2, 3, 4])
    ax.set_yticks(range(len(NVALS))); ax.set_yticklabels(NVALS)
    ax.set_xlabel("machines  M"); ax.set_ylabel("number of parts  n")
    for i in range(len(NVALS)):
        for j in range(3):
            v = grid[i, j]
            if v == v:
                ax.text(j, i, "%d/4"%int(round(v*4)), ha="center", va="center",
                        fontsize=6, color="white" if v > 0.5 else "#222")
    cb = fig.colorbar(im, ax=ax, fraction=0.046, pad=0.04)
    cb.set_label("fraction proven optimal", fontsize=6); cb.ax.tick_params(labelsize=6)
    ax.set_title("c  proven optimal (per 4 instances)", loc="left", fontweight="bold", fontsize=7.5)

    fig.tight_layout()
    for e in ("png", "pdf"):
        fig.savefig(os.path.join(outdir, "fig_bnb_frontier." + e))
    plt.close(fig)
    print("runs=%d  proven=%d/%d  min_obj=%.3f" % (len(df), df.solved.sum(), len(df), df.obj.min()))

if __name__ == "__main__":
    main()
