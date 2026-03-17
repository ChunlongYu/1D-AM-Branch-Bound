import os
import math
import time
import random
import itertools
from dataclasses import dataclass
from functools import lru_cache
from typing import List, Dict, Tuple, Iterable

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

import re
import matplotlib as mpl
from matplotlib.ticker import MaxNLocator

import InstanceData



def parse_tf_rdd_from_instance_name(name: str):
    """
    Parse names like 'tf0.2_rdd0.3_seed1' -> (0.2, 0.3).
    Return None if parsing fails.
    """
    m = re.search(r"tf([0-9.]+)_rdd([0-9.]+)", name)
    if m is None:
        return None
    return float(m.group(1)), float(m.group(2))

def pretty_instance_label(name: str) -> str:
    """
    Convert 'tf0.2_rdd0.3_seed1' -> '(tf=0.2,\nrdd=0.3)'
    """
    parsed = parse_tf_rdd_from_instance_name(name)
    if parsed is None:
        return name
    tf, rdd = parsed
    return f"(tf={tf:.1f},\nrdd={rdd:.1f})"


@dataclass
class SimpleInstance:
    name: str
    num_parts: int
    machine_area: float
    setup_time: float
    vol_coeff: float
    height_coeff: float
    max_height: float
    area: np.ndarray
    height: np.ndarray
    volume: np.ndarray
    support: np.ndarray
    due: np.ndarray


@dataclass
class Node:
    instance_name: str
    node_id: int
    current_time: float
    completed: Tuple[int, ...]
    remaining: Tuple[int, ...]
    depth: int
    remaining_size: int


def load_instance_with_due_dates(path: str, name: str | None = None) -> SimpleInstance:
    inst = InstanceData.Instance(name or os.path.basename(path))
    inst.load(path)

    # Base parser in InstanceData.py does not load the last due-date line,
    # so we read it manually when present.
    with open(path, 'r', encoding='utf-8') as f:
        lines = [ln.strip() for ln in f if ln.strip()]

    last_numbers = [float(x) for x in lines[-1].split()]
    if len(last_numbers) == inst.num_parts:
        due = np.array(last_numbers, dtype=float)
    else:
        due = np.array(InstanceData.GenerateDueDate(path, TF=0.3, RDD=0.6, RndSeed=1), dtype=float)

    area = np.array([inst.l[j, 0] * inst.w[j, 0] for j in range(inst.num_parts)], dtype=float)
    height = np.array([inst.h[j, 0] for j in range(inst.num_parts)], dtype=float)
    support = np.array([inst.s[j, 0] for j in range(inst.num_parts)], dtype=float)
    volume = np.array(inst.v, dtype=float)

    return SimpleInstance(
        name=name or os.path.splitext(os.path.basename(path))[0],
        num_parts=inst.num_parts,
        machine_area=float(inst.L[0] * inst.W[0]),
        setup_time=float(inst.S[0]),
        vol_coeff=float(inst.V[0]),
        height_coeff=float(inst.U[0]),
        max_height=float(inst.HM[0]),
        area=area,
        height=height,
        volume=volume,
        support=support,
        due=due,
    )

def generate_due_date_instances(base_path: str) -> List[SimpleInstance]:
    """
    Generate due-date scenarios using a clean factorial design:
        tf  in {0.2, 0.4, 0.6}
        rdd in {0.3, 0.6}

    The uncertain base_due_file scenario is NOT included in the returned list.
    """
    base = load_instance_with_due_dates(base_path, name='base_due_file')

    scenarios = []
    tf_list = [0.2, 0.4, 0.6]
    rdd_list = [0.3, 0.6]

    # use deterministic seeds so each (tf, rdd) has a unique reproducible scenario
    seed = 1
    for rdd in rdd_list:
        for tf in tf_list:
            due = np.array(
                InstanceData.GenerateDueDate(base_path, TF=tf, RDD=rdd, RndSeed=seed),
                dtype=float
            )

            scenarios.append(
                SimpleInstance(
                    name=f'tf{tf}_rdd{rdd}_seed{seed}',
                    num_parts=base.num_parts,
                    machine_area=base.machine_area,
                    setup_time=base.setup_time,
                    vol_coeff=base.vol_coeff,
                    height_coeff=base.height_coeff,
                    max_height=base.max_height,
                    area=base.area.copy(),
                    height=base.height.copy(),
                    volume=base.volume.copy(),
                    support=base.support.copy(),
                    due=due,
                )
            )
            seed += 1

    return scenarios

def batch_processing_time(ins: SimpleInstance, batch: Iterable[int]) -> float:
    batch = list(batch)
    if not batch:
        return 0.0
    return (
        ins.setup_time
        + ins.vol_coeff * float(np.sum(ins.volume[batch]))
        + ins.height_coeff * float(np.max(ins.height[batch]))
    )


def make_random_schedule_and_nodes(ins: SimpleInstance, n_schedules: int = 200, seed: int = 0) -> List[Node]:
    rng = random.Random(seed)
    nodes: List[Node] = []
    node_id = 0
    jobs = list(range(ins.num_parts))

    for _ in range(n_schedules):
        perm = jobs[:]
        rng.shuffle(perm)

        batches: List[List[int]] = []
        cur_batch: List[int] = []
        cur_area = 0.0
        for j in perm:
            a = ins.area[j]
            # If a single part exceeds capacity, skip it; not expected here.
            if a > ins.machine_area + 1e-9:
                continue
            if cur_batch and cur_area + a > ins.machine_area + 1e-9:
                batches.append(cur_batch)
                cur_batch = [j]
                cur_area = a
            else:
                cur_batch.append(j)
                cur_area += a
        if cur_batch:
            batches.append(cur_batch)

        t = 0.0
        completed: List[int] = []
        # Root node
        nodes.append(Node(ins.name, node_id, 0.0, tuple(), tuple(jobs), 0, ins.num_parts))
        node_id += 1
        for b in batches:
            t += batch_processing_time(ins, b)
            completed.extend(b)
            rem = tuple(sorted(set(jobs) - set(completed)))
            nodes.append(Node(ins.name, node_id, t, tuple(sorted(completed)), rem, len(completed), len(rem)))
            node_id += 1
    return nodes


def lb_par(ins: SimpleInstance, remaining: Iterable[int], current_time: float = 0.0) -> Tuple[float, List[float]]:
    U = list(remaining)
    if not U:
        return 0.0, []

    c_each = (
        current_time
        + ins.setup_time
        + ins.vol_coeff * ins.volume[U]
        + ins.height_coeff * ins.height[U]
    )
    lb = float(np.sum(np.maximum(c_each - ins.due[U], 0.0)))
    return lb, list(np.sort(c_each))


def lb_pos(ins: SimpleInstance, remaining: Iterable[int], current_time: float = 0.0) -> Tuple[float, List[float]]:
    """
    Positional lower bound defined in the paper.

    For k = 1,...,m,
        A_k = sum_{q=1}^k a_(q)
        beta_k = ceil(A_k / (L*W))
        underline{V}_k = sum_{q=1}^k v_<q>
        underline{H}_k = sum_{r=1}^{beta_k-1} h_[r] + h_[k]
        underline{C}_k = C_now + beta_k*S + U*underline{H}_k + V*underline{V}_k

    Then LB_pos = sum_k max(0, underline{C}_k - d^[k]), where d^[k] are due dates
    sorted in nondecreasing order.
    """
    U = list(remaining)
    if not U:
        return 0.0, []

    m = len(U)
    area_sorted = np.sort(ins.area[U])
    height_sorted = np.sort(ins.height[U])
    gamma_sorted = np.sort(ins.volume[U])
    due_sorted = np.sort(ins.due[U])

    area_prefix = np.cumsum(area_sorted)
    gamma_prefix = np.cumsum(gamma_sorted)
    height_prefix = np.cumsum(height_sorted)

    c_lb: List[float] = []
    for k in range(1, m + 1):
        beta_k = int(math.ceil(area_prefix[k - 1] / ins.machine_area - 1e-12))
        vol_term = ins.vol_coeff * float(gamma_prefix[k - 1])
        if beta_k <= 1:
            h_lb = float(height_sorted[k - 1])
        else:
            h_lb = float(height_prefix[beta_k - 2] + height_sorted[k - 1])
        c_k = current_time + beta_k * ins.setup_time + ins.height_coeff * h_lb + vol_term
        c_lb.append(c_k)

    lb = float(np.sum(np.maximum(np.array(c_lb) - due_sorted, 0.0)))
    return lb, c_lb


def all_feasible_batches(ins: SimpleInstance, jobs: Tuple[int, ...]) -> List[Tuple[int, ...]]:
    out = []
    n = len(jobs)
    for r in range(1, n + 1):
        for subset in itertools.combinations(jobs, r):
            if np.sum(ins.area[list(subset)]) <= ins.machine_area + 1e-9:
                out.append(subset)
    return out


def exact_opt_remaining_tardiness(ins: SimpleInstance, remaining: Tuple[int, ...], current_time: float) -> float:
    jobs = tuple(sorted(remaining))
    if not jobs:
        return 0.0

    feasible_cache: Dict[Tuple[int, ...], List[Tuple[int, ...]]] = {}

    @lru_cache(maxsize=None)
    def solve(rem: Tuple[int, ...], t_int: int) -> float:
        # t_int is a rounded representation to keep the cache key stable.
        t = t_int / 1000.0
        if not rem:
            return 0.0
        if rem not in feasible_cache:
            feasible_cache[rem] = all_feasible_batches(ins, rem)
        best = float('inf')
        for batch in feasible_cache[rem]:
            p = batch_processing_time(ins, batch)
            c = t + p
            batch_tard = float(np.sum(np.maximum(c - ins.due[list(batch)], 0.0)))
            nxt = tuple(j for j in rem if j not in batch)
            val = batch_tard + solve(nxt, int(round(c * 1000)))
            if val < best:
                best = val
        return best

    return solve(jobs, int(round(current_time * 1000)))



def parse_tf_rdd_from_instance_name(name: str):
    m = re.search(r"tf([0-9.]+)_rdd([0-9.]+)", str(name))
    if m is None:
        return None
    tf = float(m.group(1))
    rdd = float(m.group(2))
    return (rdd, tf)

def pretty_instance_label(name: str) -> str:
    m = re.search(r"tf([0-9.]+)_rdd([0-9.]+)", str(name))
    if m is None:
        return str(name)
    tf = float(m.group(1))
    rdd = float(m.group(2))
    return f"(tf={tf:.1f},\nrdd={rdd:.1f})"

def _style_ax(ax):
    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)
    ax.grid(False)
    ax.set_facecolor("white")
    ax.tick_params(direction="out", length=4, width=0.8)
    return ax

def _add_panel_label(ax, label):
    ax.text(
        -0.12, 1.03, label,
        transform=ax.transAxes,
        fontsize=13,
        fontweight="bold",
        ha="left",
        va="bottom"
    )

def set_publication_style():
    mpl.rcParams.update({
        "font.family": "DejaVu Sans",
        "font.size": 10,
        "axes.titlesize": 13,
        "axes.labelsize": 12,
        "xtick.labelsize": 10,
        "ytick.labelsize": 10,
        "legend.fontsize": 9,
        "figure.dpi": 160,
        "savefig.dpi": 400,
        "axes.linewidth": 0.8,
        "xtick.major.width": 0.8,
        "ytick.major.width": 0.8,
        "xtick.major.size": 4,
        "ytick.major.size": 4,
        "xtick.minor.size": 2.5,
        "ytick.minor.size": 2.5,
        "xtick.direction": "out",
        "ytick.direction": "out",
        "pdf.fonttype": 42,
        "ps.fonttype": 42,
    })

def run_experiment2(base_path: str, out_dir: str, n_schedules: int = 200, small_exact_limit: int = 4) -> Dict[str, str]:
    os.makedirs(out_dir, exist_ok=True)
    instances = generate_due_date_instances(base_path)

    all_rows = []
    exact_rows = []

    for idx, ins in enumerate(instances):
        print(f"\n[Instance {idx+1}/{len(instances)}] {ins.name}", flush=True)

        nodes = make_random_schedule_and_nodes(ins, n_schedules=n_schedules, seed=100 + idx)

        seen = set()
        filtered_nodes = []
        for nd in nodes:
            key = (round(nd.current_time, 6), nd.remaining)
            if key not in seen:
                seen.add(key)
                filtered_nodes.append(nd)

        print(f"  Raw nodes    : {len(nodes)}", flush=True)
        print(f"  Unique nodes : {len(filtered_nodes)}", flush=True)

        for nd in filtered_nodes:
            lbp, cpar = lb_par(ins, nd.remaining, nd.current_time)
            lbo, cpos = lb_pos(ins, nd.remaining, nd.current_time)

            all_rows.append({
                'instance': ins.name,
                'node_id': nd.node_id,
                'current_time': nd.current_time,
                'depth': nd.depth,
                'remaining_size': nd.remaining_size,
                'lb_par': lbp,
                'lb_pos': lbo,
                'improvement_abs': lbo - lbp,
                'improvement_pct_over_par': (100.0 * (lbo - lbp) / lbp) if lbp > 1e-6 else np.nan,
                'cpar_last': cpar[-1] if cpar else nd.current_time,
                'cpos_last': cpos[-1] if cpos else nd.current_time,
            })

            if 1 <= nd.remaining_size <= small_exact_limit:
                opt = exact_opt_remaining_tardiness(ins, nd.remaining, nd.current_time)
                exact_rows.append({
                    'instance': ins.name,
                    'node_id': nd.node_id,
                    'current_time': nd.current_time,
                    'depth': nd.depth,
                    'remaining_size': nd.remaining_size,
                    'lb_par': lbp,
                    'lb_pos': lbo,
                    'opt_remaining': opt,
                    'tightness_par': lbp / opt if opt > 1e-9 else 1.0,
                    'tightness_pos': lbo / opt if opt > 1e-9 else 1.0,
                })

    df = pd.DataFrame(all_rows)
    df_exact = pd.DataFrame(exact_rows)

    csv_all = os.path.join(out_dir, 'lb_comparison_all_nodes.csv')
    csv_exact = os.path.join(out_dir, 'lb_comparison_exact_nodes.csv')
    df.to_csv(csv_all, index=False)
    df_exact.to_csv(csv_exact, index=False)

    summary = df.groupby('instance').agg(
        mean_lb_par=('lb_par', 'mean'),
        mean_lb_pos=('lb_pos', 'mean'),
        mean_improvement_abs=('improvement_abs', 'mean'),
        mean_improvement_pct=('improvement_pct_over_par', lambda s: float(np.nanmean(s.values)) if np.any(~np.isnan(s.values)) else np.nan),
        dominance_rate=('improvement_abs', lambda s: float(np.mean(s >= -1e-9))),
        strict_better_rate=('improvement_abs', lambda s: float(np.mean(s > 1e-9))),
        n_nodes=('node_id', 'count'),
    ).reset_index()

    summary_path = os.path.join(out_dir, 'lb_comparison_summary.csv')
    summary.to_csv(summary_path, index=False)

    if not df_exact.empty:
        summary_exact = df_exact.groupby('instance').agg(
            mean_tightness_par=('tightness_par', 'mean'),
            mean_tightness_pos=('tightness_pos', 'mean'),
            n_exact_nodes=('node_id', 'count'),
        ).reset_index()

        better_map = (
            df_exact.assign(
                pos_better=(df_exact['tightness_pos'] >= df_exact['tightness_par'] - 1e-9).astype(float)
            )
            .groupby('instance')['pos_better']
            .mean()
        )
        summary_exact['better_rate'] = summary_exact['instance'].map(better_map)

        summary_exact_path = os.path.join(out_dir, 'lb_exact_summary.csv')
        summary_exact.to_csv(summary_exact_path, index=False)
    else:
        summary_exact_path = None

    print("\nExperiment finished. CSV files saved.", flush=True)

    return {
        'csv_all': csv_all,
        'csv_exact': csv_exact,
        'summary': summary_path,
        'summary_exact': summary_exact_path,
    }


# def run_experiment2(
#     base_path: str,
#     out_dir: str,
#     n_schedules: int = 200,
#     small_exact_limit: int = 4,
#     verbose: bool = True,
#     node_log_interval: int = 20,
# ) -> Dict[str, str]:
#     os.makedirs(out_dir, exist_ok=True)
#     instances = generate_due_date_instances(base_path)

#     all_rows = []
#     exact_rows = []

#     global_start = time.time()

#     if verbose:
#         print("=" * 80)
#         print(f"Start experiment")
#         print(f"base_path = {base_path}")
#         print(f"out_dir = {out_dir}")
#         print(f"#instances = {len(instances)}")
#         print(f"n_schedules = {n_schedules}")
#         print(f"small_exact_limit = {small_exact_limit}")
#         print("=" * 80, flush=True)

#     total_nodes_processed = 0
#     total_exact_nodes = 0

#     for idx, ins in enumerate(instances):
#         ins_start = time.time()

#         if verbose:
#             print(f"\n[Instance {idx+1}/{len(instances)}] {ins.name}", flush=True)
#             print("  Generating random schedules and nodes ...", flush=True)

#         nodes = make_random_schedule_and_nodes(ins, n_schedules=n_schedules, seed=100 + idx)

#         # Deduplicate by (current_time, remaining) to avoid too many repeated nodes.
#         seen = set()
#         filtered_nodes = []
#         for nd in nodes:
#             key = (round(nd.current_time, 6), nd.remaining)
#             if key not in seen:
#                 seen.add(key)
#                 filtered_nodes.append(nd)

#         n_nodes = len(filtered_nodes)
#         n_exact_candidates = sum(1 for nd in filtered_nodes if 1 <= nd.remaining_size <= small_exact_limit)

#         if verbose:
#             print(f"  Raw nodes      : {len(nodes)}", flush=True)
#             print(f"  Unique nodes   : {n_nodes}", flush=True)
#             print(f"  Exact candidates (|U| <= {small_exact_limit}): {n_exact_candidates}", flush=True)

#         for node_idx, nd in enumerate(filtered_nodes, start=1):
#             lbp, cpar = lb_par(ins, nd.remaining, nd.current_time)
#             lbo, cpos = lb_pos(ins, nd.remaining, nd.current_time)

#             all_rows.append({
#                 'instance': ins.name,
#                 'node_id': nd.node_id,
#                 'current_time': nd.current_time,
#                 'depth': nd.depth,
#                 'remaining_size': nd.remaining_size,
#                 'lb_par': lbp,
#                 'lb_pos': lbo,
#                 'improvement_abs': lbo - lbp,
#                 'improvement_pct_over_par': (100.0 * (lbo - lbp) / lbp) if lbp > 1e-6 else np.nan,
#                 'cpar_last': cpar[-1] if cpar else nd.current_time,
#                 'cpos_last': cpos[-1] if cpos else nd.current_time,
#             })

#             total_nodes_processed += 1

#             if verbose and (node_idx == 1 or node_idx % node_log_interval == 0 or node_idx == n_nodes):
#                 elapsed_ins = time.time() - ins_start
#                 elapsed_all = time.time() - global_start
#                 print(
#                     f"  Node {node_idx:>4}/{n_nodes} | "
#                     f"depth={nd.depth:>2} | |U|={nd.remaining_size:>2} | "
#                     f"LB_par={lbp:>10.3f} | LB_pos={lbo:>10.3f} | "
#                     f"elapsed(ins)={elapsed_ins:>7.1f}s | elapsed(all)={elapsed_all:>7.1f}s",
#                     flush=True
#                 )

#             if 1 <= nd.remaining_size <= small_exact_limit:
#                 if verbose:
#                     print(
#                         f"    -> exact solve for node {node_idx}/{n_nodes} "
#                         f"(node_id={nd.node_id}, |U|={nd.remaining_size}, current_time={nd.current_time:.3f})",
#                         flush=True
#                     )

#                 exact_start = time.time()
#                 opt = exact_opt_remaining_tardiness(ins, nd.remaining, nd.current_time)
#                 exact_elapsed = time.time() - exact_start
#                 total_exact_nodes += 1

#                 if verbose:
#                     print(
#                         f"       exact done: opt_remaining={opt:.3f}, "
#                         f"time={exact_elapsed:.2f}s, "
#                         f"tightness_par={(lbp / opt if opt > 1e-9 else 1.0):.4f}, "
#                         f"tightness_pos={(lbo / opt if opt > 1e-9 else 1.0):.4f}",
#                         flush=True
#                     )

#                 exact_rows.append({
#                     'instance': ins.name,
#                     'node_id': nd.node_id,
#                     'current_time': nd.current_time,
#                     'depth': nd.depth,
#                     'remaining_size': nd.remaining_size,
#                     'lb_par': lbp,
#                     'lb_pos': lbo,
#                     'opt_remaining': opt,
#                     'tightness_par': lbp / opt if opt > 1e-9 else 1.0,
#                     'tightness_pos': lbo / opt if opt > 1e-9 else 1.0,
#                 })

#         ins_elapsed = time.time() - ins_start
#         if verbose:
#             print(
#                 f"[Finished instance {ins.name}] "
#                 f"nodes={n_nodes}, exact_nodes={n_exact_candidates}, time={ins_elapsed:.1f}s",
#                 flush=True
#             )

#     df = pd.DataFrame(all_rows)
#     df_exact = pd.DataFrame(exact_rows)

#     if verbose:
#         print("\nSaving csv files ...", flush=True)

#     csv_all = os.path.join(out_dir, 'lb_comparison_all_nodes.csv')
#     csv_exact = os.path.join(out_dir, 'lb_comparison_exact_nodes.csv')
#     df.to_csv(csv_all, index=False)
#     df_exact.to_csv(csv_exact, index=False)

#     summary = df.groupby('instance').agg(
#         mean_lb_par=('lb_par', 'mean'),
#         mean_lb_pos=('lb_pos', 'mean'),
#         mean_improvement_abs=('improvement_abs', 'mean'),
#         mean_improvement_pct=('improvement_pct_over_par', lambda s: float(np.nanmean(s.values)) if np.any(~np.isnan(s.values)) else np.nan),
#         dominance_rate=('improvement_abs', lambda s: float(np.mean(s >= -1e-9))),
#         strict_better_rate=('improvement_abs', lambda s: float(np.mean(s > 1e-9))),
#         n_nodes=('node_id', 'count'),
#     ).reset_index()
#     summary_path = os.path.join(out_dir, 'lb_comparison_summary.csv')
#     summary.to_csv(summary_path, index=False)

#     if not df_exact.empty:
#         summary_exact = df_exact.groupby('instance').agg(
#             mean_tightness_par=('tightness_par', 'mean'),
#             mean_tightness_pos=('tightness_pos', 'mean'),
#             better_rate=('tightness_pos', lambda s: np.nan),
#             n_exact_nodes=('node_id', 'count'),
#         ).reset_index()
#         better_map = df_exact.groupby('instance').apply(
#             lambda g: float(np.mean(g['tightness_pos'] >= g['tightness_par'] - 1e-9))
#         )
#         summary_exact['better_rate'] = summary_exact['instance'].map(better_map)
#         summary_exact_path = os.path.join(out_dir, 'lb_exact_summary.csv')
#         summary_exact.to_csv(summary_exact_path, index=False)
#     else:
#         summary_exact_path = None

#     if verbose:
#         print("Drawing figures ...", flush=True)


#     # =========================
#     # Publication-style plots
#     # =========================
#     import matplotlib as mpl
#     from matplotlib.ticker import MaxNLocator

#     # Only keep orthogonal-design instances in plots
#     plot_df = df[df["instance"].str.contains(r"^tf", regex=True)].copy()
#     plot_df_exact = df_exact[df_exact["instance"].str.contains(r"^tf", regex=True)].copy() if not df_exact.empty else df_exact.copy()

#     # Sort instances by rdd first, then tf
#     instance_order = sorted(
#         plot_df["instance"].drop_duplicates().tolist(),
#         key=lambda nm: parse_tf_rdd_from_instance_name(nm)
#     )

#     instance_label_map = {nm: pretty_instance_label(nm) for nm in instance_order}

#     mpl.rcParams.update({
#         "font.family": "DejaVu Sans",
#         "font.size": 10,
#         "axes.titlesize": 13,
#         "axes.labelsize": 12,
#         "xtick.labelsize": 10,
#         "ytick.labelsize": 10,
#         "legend.fontsize": 9,
#         "figure.dpi": 160,
#         "savefig.dpi": 400,
#         "axes.linewidth": 0.8,
#         "xtick.major.width": 0.8,
#         "ytick.major.width": 0.8,
#         "xtick.major.size": 4,
#         "ytick.major.size": 4,
#         "xtick.minor.size": 2.5,
#         "ytick.minor.size": 2.5,
#         "xtick.direction": "out",
#         "ytick.direction": "out",
#         "pdf.fonttype": 42,
#         "ps.fonttype": 42,
#     })

#     def _style_ax(ax):
#         ax.spines["top"].set_visible(False)
#         ax.spines["right"].set_visible(False)
#         ax.grid(False)
#         ax.set_facecolor("white")
#         ax.tick_params(direction="out", length=4, width=0.8)
#         return ax

#     def _add_panel_label(ax, label):
#         ax.text(
#             -0.12, 1.03, label,
#             transform=ax.transAxes,
#             fontsize=13,
#             fontweight="bold",
#             ha="left",
#             va="bottom"
#         )

#     # ---------- Figure 1: Node-wise comparison of LB_par and LB_pos ----------
#     fig, ax = plt.subplots(figsize=(5.4, 4.4))
#     _style_ax(ax)

#     x = plot_df["lb_par"].to_numpy()
#     y = plot_df["lb_pos"].to_numpy()

#     lo = float(min(x.min(), y.min()))
#     hi = float(max(x.max(), y.max()))

#     ax.scatter(
#         x, y,
#         s=14,
#         alpha=0.18,
#         edgecolors="none",
#         rasterized=True,
#         clip_on=True
#     )

#     ax.plot([lo, hi], [lo, hi], "--", linewidth=1.1, color="black")

#     if len(x) >= 2:
#         coef = np.polyfit(x, y, 1)
#         xx = np.linspace(lo, hi, 200)
#         yy = coef[0] * xx + coef[1]
#         ax.plot(xx, yy, linewidth=1.2, color="black", alpha=0.75)

#     dominance_rate = float(np.mean(y >= x - 1e-9))
#     strict_better_rate = float(np.mean(y > x + 1e-9))

#     ax.text(
#         0.03, 0.97,
#         f"Dominance rate = {dominance_rate:.1%}\n"
#         f"Strictly better = {strict_better_rate:.1%}",
#         transform=ax.transAxes,
#         ha="left",
#         va="top",
#         bbox=dict(boxstyle="round,pad=0.25", facecolor="white", edgecolor="0.7", linewidth=0.7)
#     )

#     ax.set_xlabel(r"$LB_{par}$")
#     ax.set_ylabel(r"$LB_{pos}$")
#     ax.set_title("Node-wise lower-bound comparison", pad=10)
#     ax.xaxis.set_major_locator(MaxNLocator(6))
#     ax.yaxis.set_major_locator(MaxNLocator(6))
#     _add_panel_label(ax, "a")

#     fig.tight_layout()
#     fig1 = os.path.join(out_dir, "fig1_lb_scatter_publication.png")
#     fig1_pdf = os.path.join(out_dir, "fig1_lb_scatter_publication.pdf")
#     plt.savefig(fig1, bbox_inches="tight")
#     plt.savefig(fig1_pdf, bbox_inches="tight")
#     plt.close()

#     # ---------- Figure 2: Improvement distribution by instance ----------
#     fig, ax = plt.subplots(figsize=(7.2, 4.8))
#     _style_ax(ax)

#     box_data = [
#         plot_df.loc[plot_df["instance"] == nm, "improvement_abs"].dropna().to_numpy()
#         for nm in instance_order
#     ]
#     positions = np.arange(1, len(instance_order) + 1)

#     vp = ax.violinplot(
#         box_data,
#         positions=positions,
#         widths=0.76,
#         showmeans=False,
#         showmedians=False,
#         showextrema=False
#     )
#     for body in vp["bodies"]:
#         body.set_facecolor("#BDBDBD")
#         body.set_edgecolor("none")
#         body.set_alpha(0.25)

#     bp = ax.boxplot(
#         box_data,
#         positions=positions,
#         widths=0.32,
#         patch_artist=True,
#         showfliers=False,
#         medianprops=dict(color="black", linewidth=1.1),
#         boxprops=dict(facecolor="white", edgecolor="black", linewidth=0.8),
#         whiskerprops=dict(color="black", linewidth=0.8),
#         capprops=dict(color="black", linewidth=0.8),
#     )

#     # mean dots
#     for pos, arr in zip(positions, box_data):
#         if len(arr) > 0:
#             ax.scatter(pos, np.mean(arr), s=24, color="black", zorder=3)

#     ax.axhline(0, linestyle="--", linewidth=0.9, color="black", alpha=0.75)

#     ax.set_xticks(positions)
#     ax.set_xticklabels([instance_label_map[nm] for nm in instance_order])
#     ax.set_xlabel("Due-date setting")
#     ax.set_ylabel(r"$LB_{pos} - LB_{par}$")
#     ax.set_title("Improvement distribution across instances", pad=10)
#     ax.yaxis.set_major_locator(MaxNLocator(6))
#     _add_panel_label(ax, "b")

#     fig.tight_layout()
#     fig2 = os.path.join(out_dir, "fig2_improvement_distribution_publication.png")
#     fig2_pdf = os.path.join(out_dir, "fig2_improvement_distribution_publication.pdf")
#     plt.savefig(fig2, bbox_inches="tight")
#     plt.savefig(fig2_pdf, bbox_inches="tight")
#     plt.close()

#     # ---------- Figure 3: Exact-node tightness comparison ----------
#     if not plot_df_exact.empty:
#         fig, ax = plt.subplots(figsize=(5.4, 4.4))
#         _style_ax(ax)

#         x_raw = plot_df_exact["tightness_par"].to_numpy()
#         y_raw = plot_df_exact["tightness_pos"].to_numpy()

#         # clamp to [0, 1] for clean visualization
#         x = np.clip(x_raw, 0.0, 1.0)
#         y = np.clip(y_raw, 0.0, 1.0)

#         ax.scatter(
#             x, y,
#             s=16,
#             alpha=0.22,
#             edgecolors="none",
#             rasterized=True,
#             clip_on=True
#         )
#         ax.plot([0, 1], [0, 1], "--", linewidth=1.1, color="black")

#         better_rate = float(np.mean(y_raw >= x_raw - 1e-9))
#         mean_x = float(np.mean(x_raw))
#         mean_y = float(np.mean(y_raw))

#         ax.text(
#             0.03, 0.97,
#             f"$LB_{{pos}}$ better on {better_rate:.1%} of exact nodes\n"
#             f"Mean tightness: {mean_x:.3f} vs {mean_y:.3f}",
#             transform=ax.transAxes,
#             ha="left",
#             va="top",
#             bbox=dict(boxstyle="round,pad=0.25", facecolor="white", edgecolor="0.7", linewidth=0.7)
#         )

#         ax.set_xlim(0.0, 1.05)
#         ax.set_ylim(0.0, 1.05)
#         ax.set_xlabel(r"Tightness of $LB_{par}$")
#         ax.set_ylabel(r"Tightness of $LB_{pos}$")
#         ax.set_title("Exact-node tightness comparison", pad=10)
#         ax.xaxis.set_major_locator(MaxNLocator(6))
#         ax.yaxis.set_major_locator(MaxNLocator(6))
#         _add_panel_label(ax, "c")

#         fig.tight_layout()
#         fig3 = os.path.join(out_dir, "fig3_tightness_scatter_publication.png")
#         fig3_pdf = os.path.join(out_dir, "fig3_tightness_scatter_publication.pdf")
#         plt.savefig(fig3, bbox_inches="tight")
#         plt.savefig(fig3_pdf, bbox_inches="tight")
#         plt.close()
#     else:
#         fig3 = None
#         fig3_pdf = None

#     # ---------- Figure 4: Combined 3-panel figure ----------
#     fig, axes = plt.subplots(1, 3, figsize=(13.8, 4.5))

#     # panel a
#     ax = axes[0]
#     _style_ax(ax)
#     x = plot_df["lb_par"].to_numpy()
#     y = plot_df["lb_pos"].to_numpy()
#     lo = float(min(x.min(), y.min()))
#     hi = float(max(x.max(), y.max()))

#     ax.scatter(
#         x, y,
#         s=9,
#         alpha=0.16,
#         edgecolors="none",
#         rasterized=True,
#         clip_on=True
#     )
#     ax.plot([lo, hi], [lo, hi], "--", linewidth=1.0, color="black")

#     if len(x) >= 2:
#         coef = np.polyfit(x, y, 1)
#         xx = np.linspace(lo, hi, 200)
#         ax.plot(xx, coef[0] * xx + coef[1], linewidth=1.1, color="black", alpha=0.75)

#     ax.set_xlabel(r"$LB_{par}$")
#     ax.set_ylabel(r"$LB_{pos}$")
#     ax.set_title("All sampled nodes", pad=8)
#     ax.xaxis.set_major_locator(MaxNLocator(6))
#     ax.yaxis.set_major_locator(MaxNLocator(6))
#     _add_panel_label(ax, "a")

#     # panel b
#     ax = axes[1]
#     _style_ax(ax)

#     box_data = [
#         plot_df.loc[plot_df["instance"] == nm, "improvement_abs"].dropna().to_numpy()
#         for nm in instance_order
#     ]
#     positions = np.arange(1, len(instance_order) + 1)

#     vp = ax.violinplot(
#         box_data,
#         positions=positions,
#         widths=0.76,
#         showmeans=False,
#         showmedians=False,
#         showextrema=False
#     )
#     for body in vp["bodies"]:
#         body.set_facecolor("#BDBDBD")
#         body.set_edgecolor("none")
#         body.set_alpha(0.25)

#     bp = ax.boxplot(
#         box_data,
#         positions=positions,
#         widths=0.32,
#         patch_artist=True,
#         showfliers=False,
#         medianprops=dict(color="black", linewidth=1.1),
#         boxprops=dict(facecolor="white", edgecolor="black", linewidth=0.8),
#         whiskerprops=dict(color="black", linewidth=0.8),
#         capprops=dict(color="black", linewidth=0.8),
#     )

#     for pos, arr in zip(positions, box_data):
#         if len(arr) > 0:
#             ax.scatter(pos, np.mean(arr), s=20, color="black", zorder=3)

#     ax.axhline(0, linestyle="--", linewidth=0.9, color="black", alpha=0.75)

#     ax.set_xticks(positions)
#     ax.set_xticklabels([instance_label_map[nm] for nm in instance_order])
#     ax.set_xlabel("Due-date setting")
#     ax.set_ylabel(r"$LB_{pos} - LB_{par}$")
#     ax.set_title("Improvement by instance", pad=8)
#     ax.yaxis.set_major_locator(MaxNLocator(6))
#     _add_panel_label(ax, "b")

#     # panel c
#     ax = axes[2]
#     _style_ax(ax)

#     if not plot_df_exact.empty:
#         x_raw = plot_df_exact["tightness_par"].to_numpy()
#         y_raw = plot_df_exact["tightness_pos"].to_numpy()
#         x = np.clip(x_raw, 0.0, 1.0)
#         y = np.clip(y_raw, 0.0, 1.0)

#         ax.scatter(
#             x, y,
#             s=10,
#             alpha=0.18,
#             edgecolors="none",
#             rasterized=True,
#             clip_on=True
#         )
#         ax.plot([0, 1.05], [0, 1.05], "--", linewidth=1.0, color="black")
#         ax.set_xlim(0.0, 1.05)
#         ax.set_ylim(0.0, 1.05)

#     ax.set_xlabel(r"Tightness of $LB_{par}$")
#     ax.set_ylabel(r"Tightness of $LB_{pos}$")
#     ax.set_title("Exact nodes only", pad=8)
#     ax.xaxis.set_major_locator(MaxNLocator(6))
#     ax.yaxis.set_major_locator(MaxNLocator(6))
#     _add_panel_label(ax, "c")

#     fig.tight_layout()
#     fig_triptych = os.path.join(out_dir, "fig4_lb_comparison_triptych_publication.png")
#     fig_triptych_pdf = os.path.join(out_dir, "fig4_lb_comparison_triptych_publication.pdf")
#     plt.savefig(fig_triptych, bbox_inches="tight")
#     plt.savefig(fig_triptych_pdf, bbox_inches="tight")
#     plt.close()

def plot_results(out_dir: str) -> Dict[str, str]:
    """
    Read saved csv files from out_dir and generate publication-style figures.
    This function does NOT rerun the experiment.
    """
    set_publication_style()

    csv_all = os.path.join(out_dir, 'lb_comparison_all_nodes.csv')
    csv_exact = os.path.join(out_dir, 'lb_comparison_exact_nodes.csv')

    if not os.path.exists(csv_all):
        raise FileNotFoundError(f"Cannot find: {csv_all}")

    df = pd.read_csv(csv_all)
    if os.path.exists(csv_exact):
        df_exact = pd.read_csv(csv_exact)
    else:
        df_exact = pd.DataFrame()

    # only keep orthogonal-design scenarios: tf*_rdd*
    plot_df = df[df["instance"].astype(str).str.contains(r"^tf", regex=True)].copy()
    plot_df_exact = (
        df_exact[df_exact["instance"].astype(str).str.contains(r"^tf", regex=True)].copy()
        if not df_exact.empty else pd.DataFrame()
    )

    instance_names = plot_df["instance"].drop_duplicates().tolist()
    instance_names = [nm for nm in instance_names if parse_tf_rdd_from_instance_name(nm) is not None]
    instance_order = sorted(instance_names, key=parse_tf_rdd_from_instance_name)
    instance_label_map = {nm: pretty_instance_label(nm) for nm in instance_order}

    # ---------- Figure 1 ----------
    fig, ax = plt.subplots(figsize=(5.4, 4.4))
    _style_ax(ax)

    x = plot_df["lb_par"].to_numpy()
    y = plot_df["lb_pos"].to_numpy()
    lo = float(min(x.min(), y.min()))
    hi = float(max(x.max(), y.max()))

    ax.scatter(
        x, y,
        s=14,
        alpha=0.18,
        edgecolors="none",
        rasterized=True,
        clip_on=True
    )
    ax.plot([lo, hi], [lo, hi], "--", linewidth=1.1, color="black")

    if len(x) >= 2:
        coef = np.polyfit(x, y, 1)
        xx = np.linspace(lo, hi, 200)
        yy = coef[0] * xx + coef[1]
        ax.plot(xx, yy, linewidth=1.2, color="black", alpha=0.75)

    dominance_rate = float(np.mean(y >= x - 1e-9))
    strict_better_rate = float(np.mean(y > x + 1e-9))

    ax.text(
        0.03, 0.97,
        f"Dominance rate = {dominance_rate:.1%}\n"
        f"Strictly better = {strict_better_rate:.1%}",
        transform=ax.transAxes,
        ha="left",
        va="top",
        bbox=dict(boxstyle="round,pad=0.25", facecolor="white", edgecolor="0.7", linewidth=0.7)
    )

    ax.set_xlabel(r"$LB_{par}$")
    ax.set_ylabel(r"$LB_{pos}$")
    ax.set_title("Node-wise lower-bound comparison", pad=10)
    ax.xaxis.set_major_locator(MaxNLocator(6))
    ax.yaxis.set_major_locator(MaxNLocator(6))
    _add_panel_label(ax, "a")

    fig.tight_layout()
    fig1 = os.path.join(out_dir, "fig1_lb_scatter_publication.png")
    fig1_pdf = os.path.join(out_dir, "fig1_lb_scatter_publication.pdf")
    plt.savefig(fig1, bbox_inches="tight")
    plt.savefig(fig1_pdf, bbox_inches="tight")
    plt.close()

    # ---------- Figure 2 ----------
    fig, ax = plt.subplots(figsize=(7.2, 4.8))
    _style_ax(ax)

    box_data = [
        plot_df.loc[plot_df["instance"] == nm, "improvement_abs"].dropna().to_numpy()
        for nm in instance_order
    ]
    positions = np.arange(1, len(instance_order) + 1)

    vp = ax.violinplot(
        box_data,
        positions=positions,
        widths=0.76,
        showmeans=False,
        showmedians=False,
        showextrema=False
    )
    for body in vp["bodies"]:
        body.set_facecolor("#BDBDBD")
        body.set_edgecolor("none")
        body.set_alpha(0.25)

    ax.boxplot(
        box_data,
        positions=positions,
        widths=0.32,
        patch_artist=True,
        showfliers=False,
        medianprops=dict(color="black", linewidth=1.1),
        boxprops=dict(facecolor="white", edgecolor="black", linewidth=0.8),
        whiskerprops=dict(color="black", linewidth=0.8),
        capprops=dict(color="black", linewidth=0.8),
    )

    for pos, arr in zip(positions, box_data):
        if len(arr) > 0:
            ax.scatter(pos, np.mean(arr), s=24, color="black", zorder=3)

    ax.axhline(0, linestyle="--", linewidth=0.9, color="black", alpha=0.75)

    ax.set_xticks(positions)
    ax.set_xticklabels([instance_label_map[nm] for nm in instance_order])
    ax.set_xlabel("Due-date setting")
    ax.set_ylabel(r"$LB_{pos} - LB_{par}$")
    ax.set_title("Improvement distribution across instances", pad=10)
    ax.yaxis.set_major_locator(MaxNLocator(6))
    _add_panel_label(ax, "b")

    fig.tight_layout()
    fig2 = os.path.join(out_dir, "fig2_improvement_distribution_publication.png")
    fig2_pdf = os.path.join(out_dir, "fig2_improvement_distribution_publication.pdf")
    plt.savefig(fig2, bbox_inches="tight")
    plt.savefig(fig2_pdf, bbox_inches="tight")
    plt.close()

    # ---------- Figure 3 ----------
    if not plot_df_exact.empty:
        fig, ax = plt.subplots(figsize=(5.4, 4.4))
        _style_ax(ax)

        x_raw = plot_df_exact["tightness_par"].to_numpy()
        y_raw = plot_df_exact["tightness_pos"].to_numpy()

        x = np.clip(x_raw, 0.0, 1.05)
        y = np.clip(y_raw, 0.0, 1.05)

        ax.scatter(
            x, y,
            s=16,
            alpha=0.22,
            edgecolors="none",
            rasterized=True,
            clip_on=True
        )
        ax.plot([0, 1.05], [0, 1.05], "--", linewidth=1.1, color="black")

        better_rate = float(np.mean(y_raw >= x_raw - 1e-9))
        mean_x = float(np.mean(x_raw))
        mean_y = float(np.mean(y_raw))

        ax.text(
            0.03, 0.97,
            f"$LB_{{pos}}$ better on {better_rate:.1%} of exact nodes\n"
            f"Mean tightness: {mean_x:.3f} vs {mean_y:.3f}",
            transform=ax.transAxes,
            ha="left",
            va="top",
            bbox=dict(boxstyle="round,pad=0.25", facecolor="white", edgecolor="0.7", linewidth=0.7)
        )

        ax.set_xlim(0.0, 1.0)
        ax.set_ylim(0.0, 1.0)
        ax.set_xlabel(r"Tightness of $LB_{par}$")
        ax.set_ylabel(r"Tightness of $LB_{pos}$")
        ax.set_title("Exact-node tightness comparison", pad=10)
        ax.xaxis.set_major_locator(MaxNLocator(6))
        ax.yaxis.set_major_locator(MaxNLocator(6))
        _add_panel_label(ax, "c")

        fig.tight_layout()
        fig3 = os.path.join(out_dir, "fig3_tightness_scatter_publication.png")
        fig3_pdf = os.path.join(out_dir, "fig3_tightness_scatter_publication.pdf")
        plt.savefig(fig3, bbox_inches="tight")
        plt.savefig(fig3_pdf, bbox_inches="tight")
        plt.close()
    else:
        fig3 = None
        fig3_pdf = None

    # ---------- Figure 4 ----------
    fig, axes = plt.subplots(1, 3, figsize=(13.8, 4.5))

    # panel a
    ax = axes[0]
    _style_ax(ax)
    x = plot_df["lb_par"].to_numpy()
    y = plot_df["lb_pos"].to_numpy()
    lo = float(min(x.min(), y.min()))
    hi = float(max(x.max(), y.max()))
    ax.scatter(x, y, s=9, alpha=0.16, edgecolors="none", rasterized=True, clip_on=True)
    ax.plot([lo, hi], [lo, hi], "--", linewidth=1.0, color="black")
    if len(x) >= 2:
        coef = np.polyfit(x, y, 1)
        xx = np.linspace(lo, hi, 200)
        ax.plot(xx, coef[0] * xx + coef[1], linewidth=1.1, color="black", alpha=0.75)
    ax.set_xlabel(r"$LB_{par}$")
    ax.set_ylabel(r"$LB_{pos}$")
    ax.set_title("All sampled nodes", pad=8)
    ax.xaxis.set_major_locator(MaxNLocator(6))
    ax.yaxis.set_major_locator(MaxNLocator(6))
    _add_panel_label(ax, "a")

    # panel b
    ax = axes[1]
    _style_ax(ax)
    box_data = [
        plot_df.loc[plot_df["instance"] == nm, "improvement_abs"].dropna().to_numpy()
        for nm in instance_order
    ]
    positions = np.arange(1, len(instance_order) + 1)
    vp = ax.violinplot(
        box_data,
        positions=positions,
        widths=0.76,
        showmeans=False,
        showmedians=False,
        showextrema=False
    )
    for body in vp["bodies"]:
        body.set_facecolor("#BDBDBD")
        body.set_edgecolor("none")
        body.set_alpha(0.25)

    ax.boxplot(
        box_data,
        positions=positions,
        widths=0.32,
        patch_artist=True,
        showfliers=False,
        medianprops=dict(color="black", linewidth=1.1),
        boxprops=dict(facecolor="white", edgecolor="black", linewidth=0.8),
        whiskerprops=dict(color="black", linewidth=0.8),
        capprops=dict(color="black", linewidth=0.8),
    )

    for pos, arr in zip(positions, box_data):
        if len(arr) > 0:
            ax.scatter(pos, np.mean(arr), s=20, color="black", zorder=3)

    ax.axhline(0, linestyle="--", linewidth=0.9, color="black", alpha=0.75)
    ax.set_xticks(positions)
    ax.set_xticklabels([instance_label_map[nm] for nm in instance_order])
    ax.set_xlabel("Due-date setting")
    ax.set_ylabel(r"$LB_{pos} - LB_{par}$")
    ax.set_title("Improvement by instance", pad=8)
    ax.yaxis.set_major_locator(MaxNLocator(6))
    _add_panel_label(ax, "b")

    # panel c
    ax = axes[2]
    _style_ax(ax)
    if not plot_df_exact.empty:
        x_raw = plot_df_exact["tightness_par"].to_numpy()
        y_raw = plot_df_exact["tightness_pos"].to_numpy()
        x = np.clip(x_raw, 0.0, 1.05)
        y = np.clip(y_raw, 0.0, 1.05)
        ax.scatter(x, y, s=10, alpha=0.18, edgecolors="none", rasterized=True, clip_on=True)
        ax.plot([0, 1], [0, 1], "--", linewidth=1.0, color="black")
        ax.set_xlim(0.0, 1.05)
        ax.set_ylim(0.0, 1.05)

    ax.set_xlabel(r"Tightness of $LB_{par}$")
    ax.set_ylabel(r"Tightness of $LB_{pos}$")
    ax.set_title("Exact nodes only", pad=8)
    ax.xaxis.set_major_locator(MaxNLocator(6))
    ax.yaxis.set_major_locator(MaxNLocator(6))
    _add_panel_label(ax, "c")

    fig.tight_layout()
    fig_triptych = os.path.join(out_dir, "fig4_lb_comparison_triptych_publication.png")
    fig_triptych_pdf = os.path.join(out_dir, "fig4_lb_comparison_triptych_publication.pdf")
    plt.savefig(fig_triptych, bbox_inches="tight")
    plt.savefig(fig_triptych_pdf, bbox_inches="tight")
    plt.close()

    print("Figures generated from saved CSV files.", flush=True)

    return {
        "fig1": fig1,
        "fig1_pdf": fig1_pdf,
        "fig2": fig2,
        "fig2_pdf": fig2_pdf,
        "fig3": fig3,
        "fig3_pdf": fig3_pdf,
        "fig_triptych": fig_triptych,
        "fig_triptych_pdf": fig_triptych_pdf,
    }

if __name__ == '__main__':
    base_path = '/Users/chunlongyu/Desktop/Single AM TT/CompLowerBounds/20part_s.txt'
    out_dir = '/Users/chunlongyu/Desktop/Single AM TT/CompLowerBounds/lb_compare_results'

    # Step 1: run experiment and save csv
    result_exp = run_experiment2(base_path, out_dir, n_schedules=180, small_exact_limit=7)
    print(result_exp)

    # Step 2: plot from saved csv
    result_fig = plot_results(out_dir)
    print(result_fig)