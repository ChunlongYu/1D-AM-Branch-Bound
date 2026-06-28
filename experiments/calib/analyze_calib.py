#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
分析 B&B 标定 DOE(run_calib_doe 的 master_calib.csv)。
指标:每个 (config,instance) 的 PAR2 分 = time(已证) 或 2*TL(超时);
按配置聚合成 shifted geometric mean(shift=10s,越小越好),辅以 #proved。
产出 Nature/Science 风格图 + 最优配置。

用法:
    python analyze_calib.py                  # 默认 runs/，TL=300
    python analyze_calib.py --rundir runs --tl 300
输出 <rundir>/analysis/:
    fig1_config_ranking.(png|pdf)   81 配置 PAR2 排名(按 SCORE 着色)
    fig2_main_effects.(png|pdf)     四因子主效应
    fig3_interactions.(png|pdf)     关键二因子交互
    best_config.md / per_config.csv
"""
import os, argparse
import numpy as np, pandas as pd
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt

FACTORS = ["SCORE", "CAND", "MOVE", "HMARGIN"]
SHIFT = 10.0
C = {"SCORE": "#4C72B0", "CAND": "#C44E52", "MOVE": "#55A868", "HMARGIN": "#8172B3"}
SCORE_COL = {"spread": "#4C72B0", "min": "#C44E52", "sum": "#55A868"}
MM = 1/25.4

def set_style():
    plt.rcParams.update({
        "font.family": "sans-serif", "font.sans-serif": ["Arial","Helvetica","DejaVu Sans"],
        "font.size": 7, "axes.labelsize": 8, "axes.titlesize": 8,
        "xtick.labelsize": 7, "ytick.labelsize": 7, "legend.fontsize": 6.5,
        "axes.linewidth": 0.7, "xtick.major.width": 0.7, "ytick.major.width": 0.7,
        "xtick.major.size": 2.6, "ytick.major.size": 2.6,
        "axes.spines.top": False, "axes.spines.right": False, "legend.frameon": False,
        "savefig.dpi": 360, "savefig.bbox": "tight", "savefig.pad_inches": 0.02,
        "pdf.fonttype": 42,
    })

def save(fig, base):
    for e in ("png","pdf"): fig.savefig(base+"."+e)
    plt.close(fig)

def sgm(x, shift=SHIFT):
    x = np.asarray(x, float)
    return np.exp(np.mean(np.log(x+shift))) - shift

def md_table(df):
    cols = list(df.columns)
    head = "| " + " | ".join(str(c) for c in cols) + " |"
    sep  = "|" + "|".join("---" for _ in cols) + "|"
    body = ["| " + " | ".join(str(v) for v in row) + " |" for row in df.itertuples(index=False)]
    return "\n".join([head, sep, *body])

def load(rundir, tl):
    df = pd.read_csv(os.path.join(rundir, "master_calib.csv"))
    for k in ("obj","gap_pct","time_s","nodes","n","M","CAND","HMARGIN","MOVE","cfg_id"):
        df[k] = pd.to_numeric(df[k], errors="coerce")
    df["proven"] = df["proven"].astype(str).str.strip() == "1"
    g = pd.to_numeric(df["gap_pct"], errors="coerce")
    df["gapfrac"] = (g / 100.0).clip(0, 1)
    # gap-weighted PAR2 (seconds-equivalent):
    #   solved   -> solve time
    #   unsolved -> 2*TL*(1+gapfrac)  (ranks unsolved runs by how close the bound got;
    #               gapfrac=0 -> 2*TL, gapfrac=1 -> 4*TL). missing gap -> worst.
    df["score"] = np.where(df["proven"], df["time_s"].clip(lower=0),
                           2*tl*(1 + df["gapfrac"].fillna(1.0)))
    df["score"] = df["score"].fillna(4*tl)
    return df

def per_config(df):
    g = df.groupby(["cfg_id","SCORE","CAND","MOVE","HMARGIN"])
    out = g.agg(score_sgm=("score", sgm), proved=("proven","sum"),
                runs=("proven","size"),
                mean_gap=("gap_pct", lambda s: np.nanmean(pd.to_numeric(s, errors="coerce")))).reset_index()
    return out.sort_values("score_sgm").reset_index(drop=True)

# ---- fig1: ranking of all configs ----
def fig_ranking(pc, outbase):
    fig, ax = plt.subplots(figsize=(89*MM, 95*MM))
    y = np.arange(len(pc))
    cols = [SCORE_COL.get(s, "#999") for s in pc["SCORE"]]
    ax.barh(y, pc["score_sgm"], color=cols, edgecolor="none", height=0.8)
    ax.set_ylim(-0.5, len(pc)-0.5); ax.invert_yaxis()
    ax.set_xlabel("gap-weighted PAR2 (sgm), seconds-equiv  ↓ better")
    ax.set_ylabel("configuration rank (1 = best)")
    best = pc.iloc[0]
    ax.text(0.98, 0.02,
            f"best: SCORE={best['SCORE']} k={int(best['CAND'])} "
            f"MOVE={best['MOVE']:g} HM={int(best['HMARGIN'])}\n"
            f"PAR2={best['score_sgm']:.1f}s, proved {int(best['proved'])}/{int(best['runs'])}",
            transform=ax.transAxes, ha="right", va="bottom", fontsize=6)
    import matplotlib.patches as mp
    ax.legend(handles=[mp.Patch(color=v, label=f"SCORE={k}") for k,v in SCORE_COL.items()],
              loc="lower right", bbox_to_anchor=(1.0, 0.10))
    ax.set_title("All 81 configurations, ranked", fontsize=7.5)
    save(fig, outbase)

# ---- fig2: main effects ----
def fig_main_effects(df, outbase):
    fig, axes = plt.subplots(1, 4, figsize=(183*MM, 52*MM))
    levels = {"SCORE":["spread","min","sum"], "CAND":[4,8,16],
              "MOVE":[0.3,0.5,0.8], "HMARGIN":[2,4,8]}
    for ax, fac in zip(axes, FACTORS):
        xs = levels[fac]
        ys = [sgm(df[df[fac]==lv]["score"]) for lv in xs]
        xpos = np.arange(len(xs))
        ax.plot(xpos, ys, "-o", color=C[fac], ms=5, lw=1.3)
        ax.set_xticks(xpos); ax.set_xticklabels([str(v) for v in xs])
        ax.set_xlabel(fac); ax.set_xlim(-0.3, len(xs)-0.7)
        if ax is axes[0]: ax.set_ylabel("gap-weighted PAR2 (sgm)  ↓ better")
        gmin = min(ys)
        ax.scatter([np.argmin(ys)], [gmin], s=60, facecolor="none",
                   edgecolor="k", linewidth=1.0, zorder=5)
    fig.suptitle("Main effects (lower = faster); circled = best level", y=1.04, fontsize=8)
    save(fig, outbase)

# ---- fig3: key interactions ----
def fig_interactions(df, outbase):
    pairs = [("SCORE","HMARGIN"), ("SCORE","CAND"), ("MOVE","HMARGIN")]
    levels = {"SCORE":["spread","min","sum"], "CAND":[4,8,16],
              "MOVE":[0.3,0.5,0.8], "HMARGIN":[2,4,8]}
    fig, axes = plt.subplots(1, 3, figsize=(183*MM, 56*MM))
    pal = ["#4C72B0","#C44E52","#55A868","#8172B3"]
    for ax, (A, B) in zip(axes, pairs):
        xs = levels[A]; xpos = np.arange(len(xs))
        for j, lvB in enumerate(levels[B]):
            ys = [sgm(df[(df[A]==lv)&(df[B]==lvB)]["score"]) for lv in xs]
            ax.plot(xpos, ys, "-o", color=pal[j], ms=4, lw=1.1, label=f"{B}={lvB}")
        ax.set_xticks(xpos); ax.set_xticklabels([str(v) for v in xs])
        ax.set_xlabel(A); ax.legend(title=B, fontsize=5.6, title_fontsize=6)
        if ax is axes[0]: ax.set_ylabel("gap-weighted PAR2 (sgm)")
    fig.suptitle("Two-factor interactions (non-parallel lines = interaction)", y=1.04, fontsize=8)
    save(fig, outbase)

def write_best(pc, df, outdir, tl):
    best = pc.iloc[0]
    lines = ["# 标定结果:最优 B&B 配置\n",
             f"\n**最优配置**:`SCORE={best['SCORE']}  CAND={int(best['CAND'])}  "
             f"MOVEBUDGET={best['MOVE']:g}  HEAVYMARGIN={int(best['HMARGIN'])}`\n",
             f"\n- gap-weighted PAR2 (sgm) = **{best['score_sgm']:.2f}s**(TL={tl:g}s)",
             f"\n- 证到最优 {int(best['proved'])}/{int(best['runs'])} 例\n",
             "\n## 主效应最优水平(各因子边际)\n"]
    for fac, levs in {"SCORE":["spread","min","sum"],"CAND":[4,8,16],
                      "MOVE":[0.3,0.5,0.8],"HMARGIN":[2,4,8]}.items():
        ys = {lv: sgm(df[df[fac]==lv]["score"]) for lv in levs}
        bestlv = min(ys, key=ys.get)
        lines.append(f"- **{fac}**: 最优 = `{bestlv}`  (" +
                     ", ".join(f"{lv}:{v:.1f}" for lv, v in ys.items()) + ")")
    pc.to_csv(os.path.join(outdir, "per_config.csv"), index=False)
    lines.append("\n\n## Top-10 配置\n")
    top = pc.head(10)[["SCORE","CAND","MOVE","HMARGIN","score_sgm","proved","runs"]].round(2)
    lines.append(md_table(top))
    open(os.path.join(outdir, "best_config.md"), "w", encoding="utf-8").write("\n".join(str(x) for x in lines))

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rundir", default="runs")
    ap.add_argument("--tl", type=float, default=300.0)
    args = ap.parse_args()
    set_style()
    outdir = os.path.join(args.rundir, "analysis"); os.makedirs(outdir, exist_ok=True)
    df = load(args.rundir, args.tl)
    pc = per_config(df)
    fig_ranking(pc, os.path.join(outdir, "fig1_config_ranking"))
    fig_main_effects(df, os.path.join(outdir, "fig2_main_effects"))
    fig_interactions(df, os.path.join(outdir, "fig3_interactions"))
    write_best(pc, df, outdir, args.tl)
    b = pc.iloc[0]
    print(f"runs: {len(df)}  configs: {len(pc)}  instances: {df['instance'].nunique()}")
    print(f"BEST: SCORE={b['SCORE']} CAND={int(b['CAND'])} MOVE={b['MOVE']:g} "
          f"HMARGIN={int(b['HMARGIN'])}  PAR2={b['score_sgm']:.2f}s  proved {int(b['proved'])}/{int(b['runs'])}")
    print(f"-> {outdir}")

if __name__ == "__main__":
    main()
