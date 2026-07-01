// =============================================================================
//  pricer_oracle.h  --  DRAFT interface for the prize-collecting single-machine
//  pricer used in branch-and-price.  It is the retrofit of new_oracle/phi_oracle.h
//  (solvePhi): same batch model  P(B)=S+V*sum(vol)+U*max(h)  and area cap A,
//  same Type-I/II batch-position branching, interchange dominance and adaptive
//  search -- with three additions:
//     (1) parts are OPTIONAL (a discard branch),
//     (2) the objective collects dual prizes pi_j,
//     (3) the free-part bound is replaced by the prize-collecting bound LB_pc.
//  See docs section "Retrofitting the single-machine oracle into a pricer".
//
//  This header gives the SIGNATURE and the exact CHANGE POINTS relative to
//  solvePhi; the body is a skeleton with the new lines spelled out and the
//  reused machinery marked /* reuse solvePhi ... */.
// =============================================================================
#pragma once
#include <vector>
#include <cstdint>
#include <utility>
#include <limits>
#include <algorithm>

// ---- result of one pricing call ------------------------------------------
struct PriceResult {
    double   value      = 0.0;   // min over columns of  sum_{j in S}(T_j - pi_j)
    uint32_t included   = 0u;    // bitmask of the priced (best) column's parts
    double   tardiness  = 0.0;   // sum T_j of that column  == master cost c_p
    bool     proven     = false; // solved to optimality within the budget?
    // master reduced cost = value - sigma  (sigma = convexity dual, caller-supplied)
};

// ---- Ryan-Foster branching decisions (relabeled indices 0..n-1) ----------
struct RFConstraints {
    std::vector<std::pair<int,int>> together;  // both-or-neither in one column
    std::vector<std::pair<int,int>> apart;     // never both in one column
};

// ---- prize-collecting single-machine pricer -------------------------------
//  vol/hh/ar/dd: per-part arrays for the relabeled 0..n-1 subset (as in solvePhi).
//  pi: dual prizes, size n  (CHANGE 0: caller should pass only parts with pi_j>0).
//  Returns the most-negative-objective column subject to rf.
//  If `pool` != nullptr, additionally append every leaf with value < -eps
//  (multi-column harvesting).
inline PriceResult solvePricer(
    int n,
    const std::vector<double>& vol, const std::vector<double>& hh,
    const std::vector<double>& ar,  const std::vector<double>& dd,
    const std::vector<double>& pi,                    // NEW
    double S, double V, double U, double A,
    const RFConstraints& rf = {},                     // NEW
    double TL = 0.0, long long NODE_CAP = 0,
    double eps = 1e-7,
    std::vector<PriceResult>* pool = nullptr);        // NEW (optional)

/* ===========================================================================
   IMPLEMENTATION SKELETON  (copy solvePhi and apply the marked changes)
   ===========================================================================

   Node carries the SAME fields as in solvePhi:
       uint32_t sched, open;            // scheduled / currently-open-batch masks
       double   tprev, TTcl;            // prev completion, closed-batch tardiness
       double   oVol, oH, oArea; int oMax;
   PLUS one new running field:
       double   pizSched = 0.0;         // CHANGE 1: sum of pi_j over scheduled parts

   ---------------------------------------------------------------------------
   CHANGE 0  (candidate reduction).  Parts with pi_j <= 0 never help ON THEIR
   OWN (contribution T_j - pi_j >= 0). Drop them before calling -- BUT only
   after accounting for Ryan-Foster `together` constraints (rf.together):
   a part tied to a partner by `together` cannot be discarded individually,
   so its own sign no longer determines discardability -- the joint
   contribution can still be negative if the partner's term is negative
   enough. Union `together` pairs into connected components (transitively,
   e.g. chains a-b-c, not just direct pairs) and keep EVERY member of a
   component as a candidate as soon as ANY member has pi_j > 0; a component
   with no positive-pi member at all may still be dropped whole. Applying the
   naive per-part filter while a `together` constraint is active can make the
   pricer miss a genuine negative-reduced-cost column and falsely report
   optimality of the master LP -- a SOUNDNESS bug, not just a performance
   one (see branch_and_price analysis report, finding P1, and the reference
   fix + regression tests in prototype/bp_prototype.py:
   _together_components / _prize_candidates / run_p1_regression_tests).
   So: `n` should contain candidates that are either pi_j > 0, or belong to a
   together-component containing some part with pi_j > 0 -- not simply
   pi_j > 0.  (Filter here and relabel, using rf.together for the union-find.)

   ---------------------------------------------------------------------------
   CHANGE 1  (node objective).  solvePhi minimises total tardiness; the pricer
   minimises tardiness MINUS collected prizes.  Whenever a batch with part-mask
   `m` closes at completion time C:
       TTcl    += sum_{j in m} max(0, C - dd[j]);     // unchanged
       pizSched += sum_{j in m} pi[j];                // NEW
   The objective of a *complete* column is  ( TTcl - pizSched ).

   ---------------------------------------------------------------------------
   CHANGE 2  (discard branch / stop-anywhere).  In solvePhi a node is a leaf
   only when sched == FULL.  Here EVERY node may stop: close the open batch,
   discard all still-unscheduled candidates (they contribute 0), and record the
   column = current `sched`.  Concretely, at each popped node:
       // (a) "stop here" candidate column:
       double objStop = TTcl_with_open_closed - pizSched;     // close open batch
       uint32_t colMask = sched | open;
       if (valid_together(colMask, rf) && objStop < incumbent.value) {
           incumbent = { objStop, colMask, tard_with_open_closed, ... };
       }
       // (b) normal expansion: place next candidate j into the open batch or a
       //     new batch -- the Type-I/II branching of solvePhi, UNCHANGED, except
       //     a part may also simply be left out (it stays unscheduled and is
       //     discarded if we later stop).

   ---------------------------------------------------------------------------
   CHANGE 3  (lower bound LB_pc -- the crucial change).  Let
       free = candidates not yet scheduled  ( = FULL & ~sched ).
   A VALID lower bound on the prize-collecting objective of any completion is
       LB_pc =  ( TTcl - pizSched )                       // committed part
              +  open-batch committed contribution        // as in solvePhi
              -  sum_{j in free} max(0, pi[j] - tlb_j);   // NEW free-part credit
   where tlb_j >= 0 is any tardiness floor for j if appended after the committed
   completion tau, e.g.
       tlb_j = max(0.0, tau + (S + U*hh[j]) - dd[j]);     // soonest j can finish
   The cheap valid version uses tlb_j = 0  ->  subtract sum_{j in free} pi[j].
   *** Do NOT add lbPos / lbPar tardiness for free parts: they are OPTIONAL, so
       only this optimistic credit is valid. ***  (lbPos/lbPar remain valid only
       for parts already committed to the open/closed batches.)

   ---------------------------------------------------------------------------
   CHANGE 4  (dominance / memo).  Interchange dominance and the (tprev, TTcl)
   frontier test are unchanged for the committed batches.  Extend the dominance
   key with pizSched (objective offset) so two states are compared on
   ( sched, tprev, TTcl - pizSched ).  Apart/together only FILTER children; they
   do not affect dominance among feasible states.

   ---------------------------------------------------------------------------
   CHANGE 5  (termination / pruning / Lagrangian).
       incumbent.value starts at 0 (the empty column).
       prune node if  LB_pc >= incumbent.value - eps.
       HEURISTIC mode: return as soon as a leaf with value < -eps is found.
       EXACT mode: run to completion (or NODE_CAP / TL); proven = (not truncated).
                   The exact minimum gives the Lagrangian bound used by the master
                   ( z_RMP + Mbar * (value - sigma) ).
       If `pool` != nullptr, push every leaf with value < -eps for multi-column
       harvesting.

   ---------------------------------------------------------------------------
   CHANGE 6  (Ryan-Foster filters).
       apart(j,k):  prune any node with (sched>>j&1) && (sched>>k&1).
       together(j,k): a stop/leaf column `colMask` is valid only if
                      ((colMask>>j&1) == (colMask>>k&1));  reject otherwise.
   Both are O(#pairs) checks at expansion (apart) and at the stop test (together).

   ---------------------------------------------------------------------------
   Everything else -- batch time P(B)=S+V*sum(vol)+U*max(h), area feasibility,
   Type-I/II symmetry breaking (j > oMax), the initial EDD upper bound (here:
   the empty column gives objective 0), adaptive BFS/DFS, frontier cap -- is
   reused verbatim from solvePhi.
   =========================================================================== */
