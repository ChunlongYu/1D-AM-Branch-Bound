#ifndef PARALLEL_BRANCH_BOUND_H
#define PARALLEL_BRANCH_BOUND_H

// =============================================================================
//  Parallel-machine AM batch scheduling : exact branch-and-bound (upper layer)
// -----------------------------------------------------------------------------
//  This implements the assignment-based branch-and-bound described in the
//  manuscript (Section "METHODOLOGY").  The upper-level tree branches on
//  part-to-machine assignments; each complete (terminal) assignment is
//  decomposed into M independent single-machine subproblems that are solved
//  EXACTLY by the existing single-machine oracle (branch_and_cut in
//  BranchBound.cpp), reused here as Phi(Q) without modification.
//
//  Faithful components (per the paper):
//    - symmetry-reduced (canonical machine-opening) branching
//    - strong-branching (LB-gap) branching-part selection
//    - node lower bound  =  assigned-part bound  +  unassigned relaxation
//        assigned-part bound per machine = max( memory-based bound,
//                                                fast analytical bound )
//        fast analytical bound = max( parallel bound , positional bound )
//    - memory pool caching of exact oracle values Phi(Q)
//    - hybrid depth-first / best-first node selection (N_max / N_min control)
//    - multi-rule constructive initial upper bound
// =============================================================================

#include <vector>
#include <string>
#include <cstdint>
#include <unordered_map>

// ---------------------------------------------------------------------------
//  Parameters controlling the parallel-machine search.
// ---------------------------------------------------------------------------
struct PBBParams {
    int    M            = 2;        // number of identical parallel machines
    double time_limit   = 1800.0;   // global wall-clock limit (seconds); <=0 => none

    // hybrid node-selection control
    int    N_max        = 200000;   // switch to depth-first when |active| > N_max
    int    N_min        = 50000;    // switch back to best-first when |active| < N_min
    int    dfs_warmup_improvements = 1; // initial DFS phase: run DFS until this many
                                        // incumbent improvements, then go best-first

    // strong-branching: number of earliest-due-date unassigned parts scored
    // (<=0 => score all unassigned parts)
    int    strong_branch_candidates = 8;
};

// ---------------------------------------------------------------------------
//  Search statistics.
// ---------------------------------------------------------------------------
struct PBBStats {
    long long total_nodes        = 0;  // nodes popped/processed
    long long generated_nodes    = 0;  // child nodes generated
    long long lb_pruned_nodes    = 0;  // pruned by LB >= UB
    long long leaf_nodes         = 0;  // complete assignments evaluated
    long long updated_solutions  = 0;  // incumbent improvements
    long long oracle_calls       = 0;  // distinct single-machine solves
    long long oracle_cache_hits  = 0;  // Phi(Q) served from memory pool
    long long leaf_guard_skips   = 0;  // leaves pruned by incremental guard (oracle calls avoided)
    bool      timed_out          = false;
};

// ---------------------------------------------------------------------------
//  Result : best complete assignment found.
//    assign[m] = sorted list of part indices placed on machine m
//    machine_tardiness[m] = Phi(assign[m])
//    total_tardiness = sum_m machine_tardiness[m]  (= UB, proven optimal if
//    the search terminated without hitting the time limit)
// ---------------------------------------------------------------------------
struct PBBSolution {
    std::vector<std::vector<int>> assign;
    std::vector<double>           machine_tardiness;
    double                        total_tardiness = 0.0;
    bool                          proven_optimal  = false;
};

// ---------------------------------------------------------------------------
//  Main entry point.
//
//  Global per-part arrays (D, l, w, h, v) are indexed by part id and have
//  size n.  The single-element machine vectors (L, W, ST, VT, UT) follow the
//  same convention as the existing single-machine code; element [0] is used
//  for every (identical) machine.
// ---------------------------------------------------------------------------
std::pair<PBBSolution, PBBStats> solveParallelMachine(
    const std::vector<int>&    parts,   // {0,...,n-1}
    const std::vector<double>& D,       // due dates
    const std::vector<double>& ST,      // setup time     S  (use [0])
    const std::vector<double>& VT,      // volume coeff   V  (use [0])
    const std::vector<double>& UT,      // height coeff   U  (use [0])
    const std::vector<double>& L,       // platform length   (use [0])
    const std::vector<double>& W,       // platform width    (use [0])
    const std::vector<double>& l,       // part lengths
    const std::vector<double>& w,       // part widths
    const std::vector<double>& h,       // part heights
    const std::vector<double>& v,       // part volumes
    const PBBParams&           params
);

#endif // PARALLEL_BRANCH_BOUND_H
