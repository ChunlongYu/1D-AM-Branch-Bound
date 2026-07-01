#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""S3 figure: head-to-head MILP(mix) vs B&B on the same instances, same 3600s TL.
Reads MILP master_milp.csv and B&B master_results.csv, merges on (instance,M).
Outputs fig_milp_vs_bnb.(png|pdf)."""
import os, argparse
import numpy as np, pandas as pd
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D

MM = 1/25.4
NCOL = {10:"#4C72B0",15:"#55A868",20:"#C44E52",25:"#8172B3",30:"#DD8452"}

def style():
    plt.rcParams.update({"font.family":"sans-serif","font.sans-serif":["Arial","DejaVu Sans"],
        "font.size":7,"axes.labelsize":8,"axes.titlesize":8,"xtick.labelsize":7,"ytick.labelsize":7,
        "legend.fontsize":6.3,"axes.linewidth":0.7,"xtick.major.width":0.7,"ytick.major.width":0.7,
        "axes.spines.top":False,"axes.spines.right":False,"legend.frameon":False,
        "savefig.dpi":360,"savefig.bbox":"tight","pdf.fonttype":42})

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--milp",default="runs_milp_v2_3600/master_milp.csv")
    ap.add_argument("--bnb", default="../yu2022/runs_bnb_v2/master_results.csv")
    ap.add_argument("--tl",type=float,default=3600.0)
    ap.add_argument("--out",default="runs_milp_v2_3600/fig_milp_vs_bnb")
    a=ap.parse_args(); TL=a.tl; style()

    mi=pd.read_csv(a.milp); mi=mi[mi["mode"]=="mix"].copy()
    bb=pd.read_csv(a.bnb).copy()
    for df,tc,sc in [(mi,"time_s","status"),(bb,"time_s","proven")]:
        df["time_s"]=pd.to_numeric(df["time_s"],errors="coerce")
        for k in ("n","M","gap_pct"): df[k]=pd.to_numeric(df[k],errors="coerce")
    mi["msolved"]=mi["status"]=="OPTIMAL"
    bb["bsolved"]=pd.to_numeric(bb["proven"],errors="coerce")==1
    for df in (mi,bb): df["obj"]=pd.to_numeric(df["obj"],errors="coerce")
    m=pd.merge(mi[["instance","n","M","time_s","msolved","gap_pct","obj"]],
               bb[["instance","M","time_s","bsolved","gap_pct","obj"]],
               on=["instance","M"],suffixes=("_mi","_bb"))
    m=m[m["n"]<=30]
    # drop truly-trivial cells (proven optimum = 0): a zero-tardiness instance is
    # trivial for the MILP (root LP) but not informative for the exact comparison.
    trivial=((m.msolved)&(m.obj_mi.abs()<1e-6))|((m.bsolved)&(m.obj_bb.abs()<1e-6))
    n_triv=int(trivial.sum()); m=m[~trivial]
    m["xt"]=np.where(m["msolved"], m["time_s_mi"].clip(lower=1e-3), TL)
    m["yt"]=np.where(m["bsolved"], m["time_s_bb"].clip(lower=1e-3), TL)

    fig,axes=plt.subplots(1,2,figsize=(183*MM,72*MM))

    # (a) head-to-head solve time
    ax=axes[0]; lo,hi=5e-3,TL*1.7
    ax.plot([lo,hi],[lo,hi],color="#9AA0A6",lw=0.7,ls="--",zorder=1)
    ax.axhline(TL,color="#9AA0A6",lw=0.5,ls=":"); ax.axvline(TL,color="#9AA0A6",lw=0.5,ls=":")
    for n,g in m.groupby("n"):
        ax.scatter(g["xt"],g["yt"],s=16,color=NCOL.get(int(n),"#999"),
                   edgecolor="white",linewidth=0.3,label="n=%d"%int(n),zorder=3)
    ax.set_xscale("log"); ax.set_yscale("log"); ax.set_xlim(lo,hi); ax.set_ylim(lo,hi)
    ax.set_xlabel("MILP (mix) solve time (s)"); ax.set_ylabel("B&B solve time (s)")
    ax.text(0.03,0.90,"B&B faster",transform=ax.transAxes,fontsize=6,color="#444",style="italic")
    ax.text(0.72,0.60,"MILP faster",transform=ax.transAxes,fontsize=6,color="#444",style="italic")
    ax.legend(loc="lower left",ncol=1,handletextpad=0.3,bbox_to_anchor=(0.0,0.02))
    ax.set_title("a  head-to-head (same 3600 s limit)",loc="left",fontweight="bold",fontsize=7.5)

    # (b) proven fraction by n
    ax=axes[1]
    ns=sorted(m["n"].unique()); x=np.arange(len(ns)); w=0.38
    milp_f=[m[(m.n==n)]["msolved"].mean() for n in ns]
    bnb_f =[m[(m.n==n)]["bsolved"].mean() for n in ns]
    ax.bar(x-w/2,milp_f,w,color="#C44E52",edgecolor="white",linewidth=0.5,label="MILP (mix)")
    ax.bar(x+w/2,bnb_f,w,color="#4C72B0",edgecolor="white",linewidth=0.5,label="B&B")
    for xi,f in zip(x-w/2,milp_f): ax.text(xi,f+0.02,"%d%%"%round(f*100),ha="center",va="bottom",fontsize=5.5)
    for xi,f in zip(x+w/2,bnb_f):  ax.text(xi,f+0.02,"%d%%"%round(f*100),ha="center",va="bottom",fontsize=5.5)
    ax.set_xticks(x); ax.set_xticklabels(["%d"%n for n in ns])
    ax.set_ylim(0,1.15); ax.set_xlabel("number of parts  n"); ax.set_ylabel("fraction proven optimal")
    ax.legend(loc="upper right"); ax.set_title("b  exact reach by size",loc="left",fontweight="bold",fontsize=7.5)

    fig.tight_layout()
    for e in ("png","pdf"): fig.savefig(a.out+"."+e)
    plt.close(fig)
    # headline stats
    both=m; bw=both[(~both.msolved)&(both.bsolved)]; mw=both[(both.msolved)&(~both.bsolved)]
    joint=both[both.msolved&both.bsolved]
    sp=(joint["time_s_mi"]/joint["time_s_bb"]).median()
    print("merged non-trivial pairs:",len(m),"(dropped %d trivial opt=0)"%n_triv)
    print("B&B solved & MILP not: %d ; MILP solved & B&B not: %d"%(len(bw),len(mw)))
    print("median speedup (B&B vs MILP) on jointly-solved: %.1fx"%sp)
    print("proven by n  MILP:",{int(n):round(f,2) for n,f in zip(ns,milp_f)})
    print("proven by n  B&B :",{int(n):round(f,2) for n,f in zip(ns,bnb_f)})

if __name__=="__main__": main()
