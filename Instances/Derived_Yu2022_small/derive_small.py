# -*- coding: utf-8 -*-
"""
Derive identical-SMALLER-machine instances from Yu et al. (2022).
Rule:
  - machine = the SMALLER platform (min L*W) of the source; take its V,U,S,L,W.
  - per part: orientation-1 if its footprint (l<=L, w<=W) fits; else the first
    listed orientation whose footprint fits (stand tall parts up). Volume only.
  - platform height H = max(small machine H, max chosen-orientation height) so
    every part fits; L,W (area capacity) unchanged -> batching tightness preserved.
  - due dates: KEEP as-is for TestInstances; for LargerInstances (no due dates)
    generate with Yu's own GenerateDueDate for the 4 (TF,RDD,seed) combos.
"""
import os, glob, importlib.util

YU = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Yu et al., 2022"
DST = "/sessions/tender-wonderful-meitner/mnt/1D-AM-Branch-Bound/Instances/Derived_Yu2022_small"
spec = importlib.util.spec_from_file_location("yuID", os.path.join(YU, "SupFiles", "InstanceData.py"))
yu = importlib.util.module_from_spec(spec); spec.loader.exec_module(yu)

def parse_full(path):
    t = open(path, encoding="latin-1").read().split(); i = 0
    def gi():
        nonlocal i; v=int(float(t[i])); i+=1; return v
    def gf():
        nonlocal i; v=float(t[i]); i+=1; return v
    tm=gi(); tp=gi(); nm=gi(); npart=gi()
    macs=[]
    for _ in range(tm):
        gi(); gi(); V=gf();U=gf();S=gf();L=gf();W=gf();H=gf()
        macs.append(dict(V=V,U=U,S=S,L=L,W=W,H=H,area=L*W))
    parts=[]   # each: list of (l,w,h,sup), and volume
    for _ in range(tp):
        gi(); num=gi(); no=gi(); vol=gf()
        ors=[(gf(),gf(),gf(),gf()) for _ in range(no)]
        for _c in range(num): parts.append((vol, ors))
    due=[]
    while len(due)<npart and i<len(t):
        try: due.append(float(t[i])); i+=1
        except ValueError: i+=1
    return macs, parts, due

def derive(macs, parts):
    sm = min(macs, key=lambda m:m["area"]); L,W = sm["L"], sm["W"]
    chosen=[]   # (vol,l,w,h,sup)
    for vol, ors in parts:
        pick=None
        if ors[0][0]<=L and ors[0][1]<=W: pick=ors[0]          # orientation-1 footprint fits
        else:
            for o in ors:
                if o[0]<=L and o[1]<=W: pick=o; break           # first footprint-fitting
        assert pick is not None, "no footprint-fitting orientation"
        l,w,h,s=pick; chosen.append((vol,l,w,h,s))
    H = max(sm["H"], max(c[3] for c in chosen))
    return sm, chosen, H

def write_ours(path, sm, chosen, H, due):
    N=len(chosen)
    out=[f"1 {N}", f"1 {N}", "",
         f"1 1 {sm['V']:g} {sm['U']:g} {sm['S']:g} {sm['L']:g} {sm['W']:g} {H:g}", ""]
    for j,(vol,l,w,h,s) in enumerate(chosen,1):
        out += [f"{j} 1 1 {vol:g}", f"{l:g} {w:g} {h:g} {s:g}", ""]
    out += ["DueDate", " ".join(f"{int(round(d)):g}" for d in due)]
    open(path,"w").write("\n".join(out)+"\n")

os.makedirs(DST, exist_ok=True)
COMBOS=[(0.3,0.3,1),(0.3,0.6,1),(0.6,0.3,3),(0.6,0.6,3)]
rows=[]

# 1) TestInstances: keep due dates
for fp in sorted(glob.glob(os.path.join(YU,"TestInstances","*.txt"))):
    if os.path.basename(fp)=="readme.txt": continue
    macs,parts,due = parse_full(fp)
    sm,chosen,H = derive(macs,parts)
    assert len(due)==len(chosen), f"{fp}: due {len(due)} != {len(chosen)}"
    name=os.path.basename(fp)
    write_ours(os.path.join(DST,name), sm, chosen, H, due)
    rows.append((name,len(chosen),sm['L'],sm['W'],H,sm['U'],sm['S']))

# 2) ht2_1 (n=50, no due dates): generate via Yu for 4 combos
fp=os.path.join(YU,"LargerInstances","ht2_1.txt")
macs,parts,_ = parse_full(fp)
sm,chosen,H = derive(macs,parts); N=len(chosen)
for TF,RDD,seed in COMBOS:
    D=yu.GenerateDueDate(fp,TF,RDD,seed)
    name=f"ht2_1-{N}_{TF}_{RDD}_{seed}.txt"
    write_ours(os.path.join(DST,name), sm, chosen, H, D)
    rows.append((name,N,sm['L'],sm['W'],H,sm['U'],sm['S']))

print(f"generated {len(rows)} instances -> Derived_Yu2022_small")
for name,N,L,W,H,U,S in rows:
    print(f"  {name:28s} n={N:>2} machine {L:g}x{W:g}xH{H:g} U={U:g} S={S:g}")
