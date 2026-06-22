#ifndef PRIZE_ORACLE_H
#define PRIZE_ORACLE_H
// =============================================================================
//  Prize-collecting single-machine batch scheduling oracle (Lagrangian subproblem)
//
//  Solve   min_{A}  [ Phi(A) - sum_{j in A} u[j] ]
//  over part subsets A that contain all `mandatory` parts, where Phi(A) is the
//  optimal single-machine total tardiness of A (area-feasible batches, processed
//  sequentially from time 0).  Free parts may be included or dropped; mandatory
//  parts must be scheduled.  This is the per-machine subproblem of the
//  Lagrangian-decomposition B&B (see docs/Lagrangian_BB_Design.md).
//
//  All part arrays are indexed by GLOBAL part id; `parts` lists the candidate ids;
//  `u` and the attribute arrays are indexed by global id.
// =============================================================================
#include <vector>

struct PrizeResult {
    double           value;   // min [ Phi(A) - sum u ]
    std::vector<int> chosen;  // the optimal A (global ids)
};

PrizeResult prizeCollectSingleMachine(
    const std::vector<int>&    parts,      // candidate part ids
    const std::vector<double>& l,
    const std::vector<double>& w,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::vector<double>& d,          // due dates
    double S, double Vc, double Uc, double area,
    const std::vector<double>& u,          // reward per part id
    const std::vector<int>&    mandatory   // parts that MUST be scheduled (may be empty)
);

#endif
