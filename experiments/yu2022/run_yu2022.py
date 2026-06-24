#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
批跑 Derived_Yu2022_identical 实例集(24 个) × M∈{2,3,4},每例 3600s。
每个实验写一个 .txt:头部(实例/配置)+ pbb 完整输出(含每 10s 的 UB/LB 轨迹与最终 RESULT)。
同时汇总到 master_results.csv 和 summary.md。

用法(本机,pbb 已编译):
    cd experiments/yu2022
    python run_yu2022.py                      # 全量,TL=3600
    python run_yu2022.py --tl 60 --quick      # 先小时限验证流程
    python run_yu2022.py --M 3                 # 只跑 M=3
    python run_yu2022.py --resume             # 跳过已完成(.txt 里已有 RESULT)的实验
构建 pbb:  cd ../../src && g++ -std=c++17 -O2 -o pbb main.cpp ParallelBranchBound.cpp BranchBound.cpp InstanceData.cpp
"""
import os, sys, glob, time, csv, argparse, subprocess, datetime, shutil, re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

def find_pbb():
    # On Windows accept ONLY pbb.exe (a Linux-built "pbb" file may also sit in src/).
    if os.name == "nt":
        c = os.path.join(ROOT, "src", "pbb.exe")
        return c if os.path.exists(c) else None
    for c in [os.path.join(ROOT, "src", "pbb"), os.path.join(ROOT, "src", "pbb.exe")]:
        if os.path.exists(c): return c
    return None

def parse_result(text):
    m = re.search(r"RESULT .*", text)
    if not m: return None
    d = {}
    for k, v in re.findall(r"(\w+)=([^\s]+)", m.group(0)):
        d[k] = v
    return d

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--instdir", default=os.path.join(ROOT, "Instances", "Derived_Yu2022_identical"))
    ap.add_argument("--outdir",  default=os.path.join(HERE, "runs"))
    ap.add_argument("--pbb",     default=find_pbb() or os.path.join(ROOT, "src", "pbb.exe" if os.name=="nt" else "pbb"))
    ap.add_argument("--tl",      type=float, default=600.0)
    ap.add_argument("--traceint",type=float, default=10.0)
    ap.add_argument("--M",       default="2,3,4")
    ap.add_argument("--heavy",   default="1")        # HEAVY on/off (set 0 for ablation)
    ap.add_argument("--resume",  action="store_true")
    ap.add_argument("--quick",   action="store_true")  # cosmetic flag; just use small --tl
    args = ap.parse_args()

    if not os.path.exists(args.pbb):
        srcdir = os.path.join(ROOT, "src")
        sys.exit(
            f"pbb executable not found at: {args.pbb}\n"
            f"Build it on THIS machine (a Linux-built 'src/pbb' from elsewhere will NOT run on Windows):\n"
            f"  cd \"{srcdir}\"\n"
            f"  # with g++ (MinGW):\n"
            f"  g++ -std=c++17 -O2 -o pbb.exe main.cpp ParallelBranchBound.cpp BranchBound.cpp InstanceData.cpp\n"
            f"  # or with MSVC (Developer Command Prompt):\n"
            f"  cl /utf-8 /O2 /EHsc /std:c++17 main.cpp ParallelBranchBound.cpp BranchBound.cpp InstanceData.cpp /Fe:pbb.exe\n"
            f"Then re-run, or pass --pbb <path-to-pbb.exe>.")
    os.makedirs(args.outdir, exist_ok=True)
    insts = sorted(glob.glob(os.path.join(args.instdir, "*.txt")))
    Ms = [int(x) for x in args.M.split(",")]
    csv_path = os.path.join(args.outdir, "master_results.csv")
    new_csv = not os.path.exists(csv_path)
    fcsv = open(csv_path, "a", newline="")
    wr = csv.writer(fcsv)
    if new_csv:
        wr.writerow(["instance","n","M","obj","proven","lb","gap_pct","time_s","nodes","oracle","heavy"]); fcsv.flush()

    total = len(insts) * len(Ms); done = 0
    for ipath in insts:
        name = os.path.splitext(os.path.basename(ipath))[0]
        for M in Ms:
            done += 1
            outfile = os.path.join(args.outdir, f"{name}_M{M}.txt")
            if args.resume and os.path.exists(outfile) and "RESULT " in open(outfile, errors="ignore").read():
                print(f"[{done}/{total}] skip {name} M={M} (done)"); continue
            print(f"[{done}/{total}] run  {name} M={M} TL={args.tl:g}s ...", flush=True)
            env = dict(os.environ, TRACE="1", TRACEINT=str(args.traceint), HEAVY=str(args.heavy))
            t0 = time.time()
            try:
                p = subprocess.run([args.pbb, ipath, str(M), str(args.tl)],
                                   env=env, capture_output=True, text=True,
                                   timeout=args.tl + 600)
                out = p.stdout + ("\n[stderr]\n" + p.stderr if p.stderr.strip() else "")
            except subprocess.TimeoutExpired:
                out = "[ERROR] subprocess timeout (pbb did not return)\n"
            wall = time.time() - t0
            # write the per-experiment .txt
            with open(outfile, "w") as f:
                f.write(f"# experiment: {name}  M={M}\n")
                f.write(f"# config: HEAVY={args.heavy} diving=depth LS=1 ; TL={args.tl:g}s traceint={args.traceint:g}s\n")
                f.write(f"# date: {datetime.datetime.now().isoformat(timespec='seconds')}  wall={wall:.1f}s\n")
                f.write("# columns of TRACE lines: t(s) ub lb gap(%) nodes\n")
                f.write("#" + "="*70 + "\n\n")
                f.write(out)
            r = parse_result(out)
            if r:
                wr.writerow([name, r.get("n",""), M, r.get("TT",""), r.get("optimal",""),
                             r.get("lb",""), r.get("gap",""), r.get("time",""),
                             r.get("nodes",""), r.get("oracle",""), args.heavy]); fcsv.flush()
                print(f"      -> obj={r.get('TT')} proven={r.get('optimal')} gap={r.get('gap')}% "
                      f"time={r.get('time')}s nodes={r.get('nodes')}")
            else:
                print("      -> [no RESULT parsed]")
    fcsv.close()
    write_summary(csv_path, os.path.join(args.outdir, "summary.md"))
    print(f"\nDone. CSV -> {csv_path}\nSummary -> {os.path.join(args.outdir,'summary.md')}")

def write_summary(csv_path, md_path):
    rows = list(csv.DictReader(open(csv_path)))
    with open(md_path, "w") as f:
        f.write("# Yu2022 派生集实验汇总\n\n")
        f.write("| 实例 | n | M | obj | proven | lb | gap% | time(s) | nodes |\n")
        f.write("|---|---|---|---|---|---|---|---|---|\n")
        for r in rows:
            pv = "✅" if r["proven"]=="1" else "—"
            f.write(f"| {r['instance']} | {r['n']} | {r['M']} | {r['obj']} | {pv} | "
                    f"{r['lb']} | {r['gap_pct']} | {r['time_s']} | {r['nodes']} |\n")
        provn = sum(1 for r in rows if r["proven"]=="1")
        f.write(f"\n**{provn}/{len(rows)} 可证最优。**\n")

if __name__ == "__main__":
    main()
