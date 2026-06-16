#include "ParallelBranchBound.h"
#include "BranchBound.h"     // generateInitialSolution(), branch_and_cut() -> oracle Phi

#include <algorithm>
#include <chrono>
#include <cmath>
#include <limits>
#include <set>
#include <vector>

namespace {

constexpr double INF = std::numeric_limits<double>::infinity();

// ---------------------------------------------------------------------------
//  Shared context: all instance data, oracle-callable vectors, memory pool.
// ---------------------------------------------------------------------------
struct Ctx {
    int    n = 0;
    int    M = 1;
    double S = 0.0, V = 0.0, U = 0.0;     // machine time coefficients
    double area = 0.0;                    // L * W (platform capacity)

    // global per-part arrays (indexed by part id), size n
    const std::vector<double>* D = nullptr;
    const std::vector<double>* l = nullptr;
    const std::vector<double>* w = nullptr;
    const std::vector<double>* h = nullptr;
    const std::vector<double>* v = nullptr;

    // single-element machine vectors, forwarded to the existing oracle
    const std::vector<double>* L = nullptr;
    const std::vector<double>* Wv = nullptr;
    const std::vector<double>* ST = nullptr;
    const std::vector<double>* VT = nullptr;
    const std::vector<double>* UT = nullptr;

    std::vector<double> partArea;         // l_j * w_j
    std::vector<double> singleTard;       // max(0, S + V v_j + U h_j - d_j)

    // memory pool of exact single-machine optima: bitmask(Q) -> Phi(Q)
    std::unordered_map<uint64_t, double> phiCache;

    PBBParams params;
    PBBStats* stats = nullptr;

    std::chrono::steady_clock::time_point t0;

    double elapsed() const {
        return std::chrono::duration<double>(
            std::chrono::steady_clock::now() - t0).count();
    }
    bool outOfTime() const {
        return params.time_limit > 0.0 && elapsed() > params.time_limit;
    }
};

// ---------------------------------------------------------------------------
//  Internal search node : a partial part-to-machine assignment.
// ---------------------------------------------------------------------------
struct PNode {
    std::vector<std::vector<int>> assign; // assign[m] = parts on machine m
    std::vector<uint64_t>         mask;    // mask[m]   = bitmask of assign[m]
    double                        LB = 0.0;
    int                           depth = 0; // number of assigned parts
};

inline uint64_t bit(int j) { return uint64_t(1) << j; }

// ---------------------------------------------------------------------------
//  The existing single-machine oracle (generateInitialSolution / branch_and_cut)
//  indexes a parts.size()-sized vector by part id, i.e. it assumes the part set
//  is {0,...,k-1}.  To reuse it for an arbitrary subset Q of global ids without
//  modifying the oracle, we remap Q to compact local indices 0..|Q|-1 and build
//  local attribute arrays.  Phi is invariant under this relabeling.
// ---------------------------------------------------------------------------
struct LocalInstance {
    std::vector<int>    parts;          // 0..k-1
    std::vector<double> D, l, w, h, v;  // local attribute arrays
};

LocalInstance makeLocal(const Ctx& ctx, const std::vector<int>& Q) {
    LocalInstance li;
    int k = (int)Q.size();
    li.parts.resize(k);
    li.D.resize(k); li.l.resize(k); li.w.resize(k); li.h.resize(k); li.v.resize(k);
    for (int i = 0; i < k; ++i) {
        int id = Q[i];
        li.parts[i] = i;
        li.D[i] = (*ctx.D)[id];
        li.l[i] = (*ctx.l)[id];
        li.w[i] = (*ctx.w)[id];
        li.h[i] = (*ctx.h)[id];
        li.v[i] = (*ctx.v)[id];
    }
    return li;
}

// ===========================================================================
//  Exact single-machine oracle wrapper  Phi(Q)  (reuses branch_and_cut).
//  Results are memoized in the global memory pool.
// ===========================================================================
double Phi(Ctx& ctx, const std::vector<int>& Q, uint64_t qmask) {
    if (Q.empty()) return 0.0;

    auto it = ctx.phiCache.find(qmask);
    if (it != ctx.phiCache.end()) {
        ++ctx.stats->oracle_cache_hits;
        return it->second;
    }

    LocalInstance li = makeLocal(ctx, Q);

    // fast EDD-greedy feasible schedule -> initial UB for the oracle
    std::pair<BatchMap, double> init = generateInitialSolution(
        li.parts, *ctx.L, *ctx.Wv, li.l, li.w, li.v, li.h, li.D,
        *ctx.ST, *ctx.VT, *ctx.UT);
    double UB0 = init.second;

    // remaining time budget for this oracle call
    double tl;
    if (ctx.params.time_limit <= 0.0) {
        tl = 0.0; // unlimited (oracle treats <=0 as no limit)
    } else {
        double remaining = ctx.params.time_limit - ctx.elapsed();
        if (remaining <= 0.0) { ctx.stats->timed_out = true; remaining = 0.001; }
        tl = remaining;
    }

    std::unordered_map<int, std::vector<int>> emptyS;
    std::vector<std::set<int>> noInfeasible;

    std::pair<Node, Stats> res = branch_and_cut(
        li.parts, li.D, noInfeasible, *ctx.ST, *ctx.VT, *ctx.UT,
        *ctx.L, *ctx.Wv, li.l, li.w, li.h, li.v,
        emptyS, UB0, tl, "");

    // If no leaf strictly improved on the heuristic, the heuristic was optimal.
    double phi = res.first.S.empty() ? UB0 : res.first.LB;

    ctx.phiCache.emplace(qmask, phi);
    ++ctx.stats->oracle_calls;
    return phi;
}

// ===========================================================================
//  Lower-bound building blocks for a single machine's assigned set.
// ===========================================================================

// Parallel bound: every part is its own singleton batch from time zero.
double lbPar(const Ctx& ctx, const std::vector<int>& Q) {
    double s = 0.0;
    for (int j : Q) s += ctx.singleTard[j];
    return s;
}

// Positional bound (manuscript Appendix A).
double lbPos(const Ctx& ctx, const std::vector<int>& Q) {
    const int q = (int)Q.size();
    if (q == 0) return 0.0;

    std::vector<double> a(q), hh(q), vv(q), dd(q);
    for (int k = 0; k < q; ++k) {
        int j = Q[k];
        a[k]  = ctx.partArea[j];
        hh[k] = (*ctx.h)[j];
        vv[k] = (*ctx.v)[j];
        dd[k] = (*ctx.D)[j];
    }
    std::sort(a.begin(),  a.end());
    std::sort(hh.begin(), hh.end());
    std::sort(vv.begin(), vv.end());
    std::sort(dd.begin(), dd.end());

    // prefix sums of the smallest heights: hpre[t] = sum of t smallest heights
    std::vector<double> hpre(q + 1, 0.0);
    for (int t = 1; t <= q; ++t) hpre[t] = hpre[t - 1] + hh[t - 1];

    double Asum = 0.0, Vsum = 0.0, total = 0.0;
    for (int k = 1; k <= q; ++k) {
        Asum += a[k - 1];
        Vsum += vv[k - 1];
        int beta = (int)std::ceil(Asum / ctx.area - 1e-9);
        if (beta < 1) beta = 1;
        if (beta > k) beta = k;                 // at most k nonempty batches
        double Hk = hpre[beta - 1] + hh[k - 1]; // (beta-1) smallest heights + h_[k]
        double Ck = beta * ctx.S + ctx.V * Vsum + ctx.U * Hk;
        double tard = Ck - dd[k - 1];
        if (tard > 0.0) total += tard;
    }
    return total;
}

// Memory-based bound: max Phi(C) over cached subsets C subseteq Q.
double lbMem(const Ctx& ctx, uint64_t qmask) {
    double best = 0.0;
    for (const auto& kv : ctx.phiCache) {
        uint64_t cmask = kv.first;
        if ((cmask & qmask) == cmask && kv.second > best) best = kv.second;
    }
    return best;
}

// Combined machine-wise lower bound = max(memory, fast{par,pos}).
double machineLB(const Ctx& ctx, const std::vector<int>& Q, uint64_t qmask) {
    if (Q.empty()) return 0.0;
    double fast = std::max(lbPar(ctx, Q), lbPos(ctx, Q));
    double mem  = lbMem(ctx, qmask);
    return std::max(fast, mem);
}

// ===========================================================================
//  Node helpers.
// ===========================================================================
uint64_t fullMask(int n) {
    return (n >= 64) ? ~uint64_t(0) : ((uint64_t(1) << n) - 1);
}

// number of opened (nonempty) machines
int usedMachines(const PNode& nd) {
    int q = 0;
    for (uint64_t m : nd.mask) if (m) ++q;
    return q;
}

// canonical machine-opening candidate set K(N)
std::vector<int> candidateMachines(const PNode& nd, int M) {
    int q = usedMachines(nd);
    std::vector<int> K;
    if (q == 0) { K.push_back(0); return K; }
    int upto = std::min(q + 1, M);
    for (int r = 0; r < upto; ++r) K.push_back(r);
    return K;
}

// unassigned parts (bit order)
std::vector<int> unassignedParts(const Ctx& ctx, const PNode& nd) {
    uint64_t assigned = 0;
    for (uint64_t m : nd.mask) assigned |= m;
    uint64_t free = fullMask(ctx.n) & ~assigned;
    std::vector<int> U;
    for (int j = 0; j < ctx.n; ++j) if (free & bit(j)) U.push_back(j);
    return U;
}

// Full node lower bound, returning also the per-machine LBs and unassigned sum
// so that children can be evaluated incrementally.
double nodeLB(const Ctx& ctx, const PNode& nd,
              std::vector<double>& mLB, double& unassignedSum) {
    mLB.assign(ctx.M, 0.0);
    double total = 0.0;
    for (int m = 0; m < ctx.M; ++m) {
        mLB[m] = machineLB(ctx, nd.assign[m], nd.mask[m]);
        total += mLB[m];
    }
    unassignedSum = 0.0;
    uint64_t assigned = 0;
    for (uint64_t mm : nd.mask) assigned |= mm;
    uint64_t free = fullMask(ctx.n) & ~assigned;
    for (int j = 0; j < ctx.n; ++j)
        if (free & bit(j)) unassignedSum += ctx.singleTard[j];
    return total + unassignedSum;
}

// ===========================================================================
//  Strong-branching part selection (LB-gap score).
// ===========================================================================
int selectBranchingPart(const Ctx& ctx, const PNode& nd,
                        const std::vector<int>& U,
                        const std::vector<int>& K,
                        const std::vector<double>& curMLB) {
    // restrict candidate parts to the earliest-due-date subset for efficiency
    std::vector<int> cand = U;
    if (ctx.params.strong_branch_candidates > 0 &&
        (int)cand.size() > ctx.params.strong_branch_candidates) {
        std::sort(cand.begin(), cand.end(),
                  [&](int a, int b) { return (*ctx.D)[a] < (*ctx.D)[b]; });
        cand.resize(ctx.params.strong_branch_candidates);
    }

    int    bestPart  = U.front();
    double bestScore = -1.0;
    for (int j : cand) {
        double vmax = -INF, vmin = INF;
        for (int r : K) {
            std::vector<int> Qr = nd.assign[r];
            Qr.push_back(j);
            double g   = machineLB(ctx, Qr, nd.mask[r] | bit(j));
            double val = g - curMLB[r];          // marginal increase on machine r
            if (val > vmax) vmax = val;
            if (val < vmin) vmin = val;
        }
        double score = vmax - vmin;
        // tie-break: prefer earlier due date
        if (score > bestScore + 1e-12 ||
            (std::abs(score - bestScore) <= 1e-12 &&
             (*ctx.D)[j] < (*ctx.D)[bestPart])) {
            bestScore = score;
            bestPart  = j;
        }
    }
    return bestPart;
}

// ===========================================================================
//  Constructive initial upper bound (multi-rule).
// ===========================================================================
double evaluateAssignmentExact(Ctx& ctx,
                               const std::vector<std::vector<int>>& assign,
                               std::vector<double>& perMachine) {
    perMachine.assign(ctx.M, 0.0);
    double total = 0.0;
    for (int m = 0; m < ctx.M; ++m) {
        uint64_t msk = 0;
        for (int j : assign[m]) msk |= bit(j);
        double phi = Phi(ctx, assign[m], msk);
        perMachine[m] = phi;
        total += phi;
    }
    return total;
}

// 增量式、下界守门的叶子评估（论文外的工程加速，完全保最优）。
// 机器按集合大小升序逐台精确求 Φ；在算每台（尤其最大那台）oracle 之前，
// 检查 (已精确之和) + (剩余机器 machineLB 之和) >= UB，若成立则该叶子赢不了
// 当前 UB，直接剪枝，不再调用剩余（往往最大）机器的 oracle。
// 返回 true 表示完整评估（Z、perMachine 有效）；false 表示被守门剪枝。
bool evaluateLeafGuarded(Ctx& ctx,
                         const std::vector<std::vector<int>>& assign,
                         double UB,
                         std::vector<double>& perMachine,
                         double& Z) {
    const int M = ctx.M;
    std::vector<int>      order(M);
    std::vector<uint64_t> msk(M);
    std::vector<double>   mlb(M);
    double remainingLB = 0.0;
    for (int m = 0; m < M; ++m) {
        order[m] = m;
        uint64_t b = 0;
        for (int j : assign[m]) b |= bit(j);
        msk[m] = b;
        mlb[m] = machineLB(ctx, assign[m], b);   // 用最新缓存，界尽量紧
        remainingLB += mlb[m];
    }
    std::sort(order.begin(), order.end(),
              [&](int a, int b){ return assign[a].size() < assign[b].size(); });

    perMachine.assign(M, 0.0);
    double runningExact = 0.0;
    for (int idx = 0; idx < M; ++idx) {
        int m = order[idx];
        // 守门：已精确 + 剩余（含本台）下界 已 >= UB -> 该叶子无望，剪枝
        if (runningExact + remainingLB >= UB - 1e-9) return false;
        double phi = Phi(ctx, assign[m], msk[m]);
        perMachine[m] = phi;
        runningExact += phi;
        remainingLB  -= mlb[m];
    }
    Z = runningExact;
    return true;
}

// fast EDD-greedy estimate of single-machine tardiness for a part set
double phiHat(const Ctx& ctx, const std::vector<int>& Q) {
    if (Q.empty()) return 0.0;
    LocalInstance li = makeLocal(ctx, Q);
    std::pair<BatchMap, double> init = generateInitialSolution(
        li.parts, *ctx.L, *ctx.Wv, li.l, li.w, li.v, li.h, li.D,
        *ctx.ST, *ctx.VT, *ctx.UT);
    return init.second;
}

std::vector<std::vector<int>> constructByOrder(const Ctx& ctx,
                                               const std::vector<int>& order) {
    std::vector<std::vector<int>> assign(ctx.M);
    for (int j : order) {
        int    bestR   = 0;
        double bestInc = INF;
        for (int r = 0; r < ctx.M; ++r) {
            double before = phiHat(ctx, assign[r]);
            std::vector<int> tmp = assign[r];
            tmp.push_back(j);
            double after = phiHat(ctx, tmp);
            double inc = after - before;
            // prefer the smaller marginal increase; break near-ties toward the
            // least-loaded machine to keep the assignment balanced (this keeps
            // every single-machine oracle call small and avoids piling all parts
            // onto one machine when due dates are loose / increments are zero)
            bool better = inc < bestInc - 1e-9 ||
                (std::abs(inc - bestInc) <= 1e-9 &&
                 assign[r].size() < assign[bestR].size());
            if (better) { bestInc = inc; bestR = r; }
        }
        assign[bestR].push_back(j);
    }
    return assign;
}

void buildInitialIncumbent(Ctx& ctx, const std::vector<int>& parts,
                           std::vector<std::vector<int>>& bestAssign,
                           std::vector<double>& bestPerMachine,
                           double& UB) {
    auto makeOrder = [&](int rule) {
        std::vector<int> o = parts;
        std::sort(o.begin(), o.end(), [&](int a, int b) {
            switch (rule) {
                case 0:  return (*ctx.D)[a] < (*ctx.D)[b];         // EDD
                case 1:  return ctx.partArea[a] > ctx.partArea[b]; // area desc
                case 2:  return (*ctx.v)[a] > (*ctx.v)[b];         // volume desc
                default: return (*ctx.h)[a] > (*ctx.h)[b];         // height desc
            }
        });
        return o;
    };

    UB = INF;
    for (int rule = 0; rule < 4; ++rule) {
        std::vector<std::vector<int>> assign = constructByOrder(ctx, makeOrder(rule));
        std::vector<double> perMachine;
        double val = evaluateAssignmentExact(ctx, assign, perMachine);
        if (val < UB) {
            UB = val;
            bestAssign = std::move(assign);
            bestPerMachine = std::move(perMachine);
        }
    }
}

} // namespace

// ===========================================================================
//  Main solver.
// ===========================================================================
std::pair<PBBSolution, PBBStats> solveParallelMachine(
    const std::vector<int>&    parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& L,
    const std::vector<double>& W,
    const std::vector<double>& l,
    const std::vector<double>& w,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const PBBParams&           params)
{
    PBBStats stats;
    PBBSolution sol;

    Ctx ctx;
    ctx.n  = (int)parts.size();
    ctx.M  = std::max(1, params.M);
    ctx.S  = ST[0]; ctx.V = VT[0]; ctx.U = UT[0];
    ctx.area = L[0] * W[0];
    ctx.D = &D; ctx.l = &l; ctx.w = &w; ctx.h = &h; ctx.v = &v;
    ctx.L = &L; ctx.Wv = &W; ctx.ST = &ST; ctx.VT = &VT; ctx.UT = &UT;
    ctx.params = params;
    ctx.stats  = &stats;
    ctx.t0 = std::chrono::steady_clock::now();

    // precompute per-part area and singleton tardiness
    ctx.partArea.assign(ctx.n, 0.0);
    ctx.singleTard.assign(ctx.n, 0.0);
    for (int j = 0; j < ctx.n; ++j) {
        ctx.partArea[j]  = l[j] * w[j];
        double pj = ctx.S + ctx.V * v[j] + ctx.U * h[j];
        ctx.singleTard[j] = std::max(0.0, pj - D[j]);
    }

    if (ctx.n == 0) {
        sol.assign.assign(ctx.M, {});
        sol.machine_tardiness.assign(ctx.M, 0.0);
        sol.proven_optimal = true;
        return { sol, stats };
    }
    if (ctx.n >= 64) {
        // bitmask representation supports up to 63 parts
        sol.assign.assign(ctx.M, {});
        sol.machine_tardiness.assign(ctx.M, 0.0);
        sol.total_tardiness = INF;
        return { sol, stats };
    }

    // ---- initial incumbent (UB) -------------------------------------------
    std::vector<std::vector<int>> bestAssign;
    std::vector<double>           bestPerMachine;
    double UB;
    buildInitialIncumbent(ctx, parts, bestAssign, bestPerMachine, UB);

    // ---- root node ---------------------------------------------------------
    PNode root;
    root.assign.assign(ctx.M, {});
    root.mask.assign(ctx.M, 0);
    root.depth = 0;
    {
        std::vector<double> mLB; double us;
        root.LB = nodeLB(ctx, root, mLB, us);
    }

    std::vector<PNode> active;
    active.push_back(root);

    // ---- hybrid node-selection state --------------------------------------
    bool warmupDone   = (params.dfs_warmup_improvements <= 0);
    bool persistentDFS = !warmupDone;   // warmup uses depth-first
    long long improvements = 0;

    // ---- main loop ---------------------------------------------------------
    while (!active.empty()) {
        if (ctx.outOfTime()) { stats.timed_out = true; break; }

        // decide selection mode
        if (!warmupDone && improvements >= params.dfs_warmup_improvements) {
            warmupDone   = true;
            persistentDFS = false;        // switch to best-first after warmup
        }
        bool dfsMode;
        if (!warmupDone) {
            dfsMode = true;
        } else {
            if ((int)active.size() > params.N_max)      persistentDFS = true;
            else if ((int)active.size() < params.N_min) persistentDFS = false;
            dfsMode = persistentDFS;
        }

        // select node index
        int sel = 0;
        if (dfsMode) {
            // 实验：均衡优先 —— 负载 = 各机器件数的最大值，越小越均衡；
            // 主键最均衡，平手取更深，引导搜索先到均衡叶子。
            auto maxLoad = [&](const PNode& nd){ size_t mx=0; for (auto& a : nd.assign) mx=std::max(mx,a.size()); return (int)mx; };
            int bestLoad = maxLoad(active[0]); int bestDepth = active[0].depth;
            for (int i = 1; i < (int)active.size(); ++i) {
                int ld = maxLoad(active[i]);
                if (ld < bestLoad || (ld==bestLoad && active[i].depth > bestDepth)) {
                    bestLoad = ld; bestDepth = active[i].depth; sel = i;
                }
            }
        } else {
            double bestLB = active[0].LB; int bestDepth = active[0].depth;
            for (int i = 1; i < (int)active.size(); ++i) {
                if (active[i].LB < bestLB - 1e-12 ||
                    (std::abs(active[i].LB - bestLB) <= 1e-12 &&
                     active[i].depth > bestDepth)) {
                    bestLB = active[i].LB; bestDepth = active[i].depth; sel = i;
                }
            }
        }

        PNode cur = std::move(active[sel]);
        active[sel] = std::move(active.back());
        active.pop_back();
        ++stats.total_nodes;

        // bound-based pruning
        if (cur.LB >= UB - 1e-9) { ++stats.lb_pruned_nodes; continue; }

        std::vector<int> U = unassignedParts(ctx, cur);

        // terminal node -> exact evaluation by oracle
        if (U.empty()) {
            ++stats.leaf_nodes;
            std::vector<double> perMachine; double Z = 0.0;
            if (!evaluateLeafGuarded(ctx, cur.assign, UB, perMachine, Z)) {
                ++stats.leaf_guard_skips;     // 守门剪枝，跳过昂贵 oracle
                continue;
            }
            if (Z < UB - 1e-9) {
                UB = Z;
                bestAssign = cur.assign;
                bestPerMachine = perMachine;
                ++stats.updated_solutions;
                ++improvements;
            }
            continue;
        }

        // per-machine LBs of current node (for incremental child bounds + scoring)
        std::vector<double> curMLB; double curUnassigned;
        double baseLB = nodeLB(ctx, cur, curMLB, curUnassigned);

        // candidate machines (symmetry-reduced) and branching part
        std::vector<int> K = candidateMachines(cur, ctx.M);
        int j = selectBranchingPart(ctx, cur, U, K, curMLB);

        // generate children: assign j to each candidate machine r
        for (int r : K) {
            PNode child = cur;                         // copy
            child.assign[r].push_back(j);
            child.mask[r] |= bit(j);
            child.depth = cur.depth + 1;

            // incremental LB: only machine r and the unassigned set change
            double gr = machineLB(ctx, child.assign[r], child.mask[r]);
            child.LB = baseLB - ctx.singleTard[j] + (gr - curMLB[r]);

            ++stats.generated_nodes;
            if (child.LB < UB - 1e-9) active.push_back(std::move(child));
            else ++stats.lb_pruned_nodes;
        }
    }

    // ---- assemble result ---------------------------------------------------
    sol.assign            = bestAssign;
    sol.machine_tardiness = bestPerMachine;
    sol.total_tardiness   = UB;
    sol.proven_optimal    = !stats.timed_out;

    // sort each machine's part list for stable, readable output
    for (auto& m : sol.assign) std::sort(m.begin(), m.end());
    return { sol, stats };
}
