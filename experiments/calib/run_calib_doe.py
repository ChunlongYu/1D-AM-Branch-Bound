#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
B&B 超参数标定 DOE runner —— 全因子 81 配置 × 10 标定算例(Calib_TF06),每例 TL=300s。

  因子(全固定其余:DIVE=depth, LS=1, HEAVY=1, WARM=0, FREEPAR=0):
     SCORE       ∈ {spread, min, sum}
     CAND (k)    ∈ {4, 8, 16}
     MOVEBUDGET  ∈ {0.3, 0.5, 0.8}
     HEAVYMARGIN ∈ {2, 4, 8}
  => 3*3*3*3 = 81 configs

每个算例的 M 编码在文件名里(cal_n..._M<M>_...txt)。遍历顺序:算例按 n 升序(外层)、
配置(内层)——这样便宜算例先跑完,能尽早拿到"全配置×部分算例"做初步排名。

产出 runs/master_calib.csv(一行 = 一个 config×instance 运行)。断点续传:--resume 跳过
CSV 里已存在的 (cfg_id,instance) 组合。

用法(本机,pbb.exe 已编译):
    cd experiments/calib
    python run_calib_doe.py                 # 全量,TL=300
    python run_calib_doe.py --tl 30         # 先小时限验证流程
    python run_calib_doe.py --resume
"""
import os, sys, glob, csv, time, argparse, subprocess, re, itertools

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

SCORES   = ["spread", "min", "sum"]
CANDS    = [4, 8, 16]
MOVES    = [0.3, 0.5, 0.8]
HMARGINS = [2, 4, 8]

def find_pbb():
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

def m_of(instname):
    m = re.search(r"_M(\d+)_", instname)
    return int(m.group(1)) if m else 2

def n_of(instname):
    m = re.search(r"_n(\d+)_", instname)
    return int(m.group(1)) if m else 0

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--instdir", default=os.path.join(ROOT, "Instances", "Calib_TF06"))
    ap.add_argument("--outdir",  default=os.path.join(HERE, "runs"))
    ap.add_argument("--pbb",     default=find_pbb() or os.path.join(ROOT, "src", "pbb.exe"))
    ap.add_argument("--tl",      type=float, default=300.0)
    ap.add_argument("--resume",  action="store_true")
    args = ap.parse_args()

    if not os.path.exists(args.pbb):
        sys.exit(f"pbb not found at {args.pbb}; build src/pbb.exe first (src\\build_pbb.bat).")
    os.makedirs(args.outdir, exist_ok=True)
    insts = sorted(glob.glob(os.path.join(args.instdir, "*.txt")), key=lambda p: n_of(os.path.basename(p)))
    if not insts:
        sys.exit(f"no instances in {args.instdir} (run Instances/derive_calib.py first).")

    configs = []
    cid = 0
    for sc, k, mv, hm in itertools.product(SCORES, CANDS, MOVES, HMARGINS):
        configs.append(dict(cfg_id=cid, SCORE=sc, CAND=k, MOVE=mv, HMARGIN=hm)); cid += 1

    csv_path = os.path.join(args.outdir, "master_calib.csv")
    done = set()
    new_csv = not os.path.exists(csv_path)
    if not new_csv and args.resume:
        for r in csv.DictReader(open(csv_path)):
            done.add((int(r["cfg_id"]), r["instance"]))
    fcsv = open(csv_path, "a", newline="")
    wr = csv.writer(fcsv)
    if new_csv:
        wr.writerow(["cfg_id", "SCORE", "CAND", "MOVE", "HMARGIN", "instance", "n", "M",
                     "obj", "proven", "gap_pct", "time_s", "nodes", "status"]); fcsv.flush()

    total = len(insts) * len(configs); did = 0
    for ip in insts:                 # instance outer (n ascending)
        name = os.path.splitext(os.path.basename(ip))[0]
        M = m_of(name); n = n_of(name)
        for cfg in configs:
            did += 1
            key = (cfg["cfg_id"], name)
            if args.resume and key in done:
                continue
            env = dict(os.environ,
                       SCORE=cfg["SCORE"], CAND=str(cfg["CAND"]),
                       MOVEBUDGET=str(cfg["MOVE"]), HEAVYMARGIN=str(cfg["HMARGIN"]),
                       HEAVY="1")
            t0 = time.time()
            try:
                p = subprocess.run([args.pbb, ip, str(M), str(args.tl)],
                                   env=env, capture_output=True, text=True,
                                   timeout=args.tl + 120)
                out = p.stdout
            except subprocess.TimeoutExpired:
                out = ""
            r = parse_result(out) or {}
            proven = r.get("optimal", "")
            wr.writerow([cfg["cfg_id"], cfg["SCORE"], cfg["CAND"], cfg["MOVE"], cfg["HMARGIN"],
                         name, n, M, r.get("TT", ""), proven, r.get("gap", ""),
                         r.get("time", f"{time.time()-t0:.2f}"), r.get("nodes", ""),
                         "OPT" if proven == "1" else "TL"]); fcsv.flush()
            if did % 20 == 0 or cfg["cfg_id"] == 0:
                print(f"[{did}/{total}] {name} cfg{cfg['cfg_id']} "
                      f"{cfg['SCORE']}/k{cfg['CAND']}/mv{cfg['MOVE']}/hm{cfg['HMARGIN']} "
                      f"-> TT={r.get('TT','?')} proven={proven} t={r.get('time','?')}", flush=True)
    fcsv.close()
    print(f"\nDone. -> {csv_path}\nAnalyse: python analyze_calib.py")

if __name__ == "__main__":
    main()
