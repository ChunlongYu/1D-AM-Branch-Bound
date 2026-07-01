# Branch-and-Price for parallel-AM total tardiness

Everything related to the branch-and-price (column-generation) route, in one
place. The motivation: the assignment-based B&B is bound-limited beyond n~20;
a Dantzig-Wolfe master gives a much stronger convexification bound, and its
pricing subproblem reuses the existing single-machine oracle.

## Layout
```
branch_and_price/
  doc/branch_and_price.tex        methodology draft (master / pricing / branching
                                  / algorithm / oracle-retrofit), elsarticle template
  prototype/bp_prototype.py       VALIDATED Python reference implementation of the
                                  whole loop (RMP+simplex duals, column generation,
                                  prize-collecting pricer, Ryan-Foster B&P);
                                  matches brute force on small instances (8/8)
  cpp/pricer_oracle.h             DRAFT C++ interface for the prize-collecting
                                  pricer: signature + the exact change points vs
                                  solvePhi (objective, discard branch, LB_pc,
                                  dominance, termination, Ryan-Foster)
  cpp/phi_oracle_reference.h      snapshot of the base single-machine oracle
                                  (new_oracle/phi_oracle.h) the pricer is built on
```

## Status
- Framework validated end-to-end in Python (`prototype/bp_prototype.py`):
  `python bp_prototype.py` -> P1 regression tests + simplex self-test + demo;
  a driver loop checks B&P optimum == brute force.
- Formulation written up (`doc/branch_and_price.tex`).
- C++ pricer interface drafted (`cpp/pricer_oracle.h`); body is a skeleton.

## Fixed: proven flags, anytime global lower bound, RF pool filtering (2026-07-01)
Design-review findings P2/P3, plus one new finding surfaced while fixing them:
- **P2** -- `simplex_bigM()` and `column_generation()` could silently hit an
  iteration cap and return a not-actually-optimal LP value with no signal to
  the caller; `branch_and_price()` then used that value to prune, which is
  unsound the same way P1 was (an under-converged RMP value is only a valid
  *upper* bound on the true master LP value, never a valid lower bound).
  Fixed: both now return a `proven` flag.
- **P3** -- the design doc claims the global lower bound is "the minimum node
  bound over the open nodes," but nothing in the prototype computed it.
  Fixed: `branch_and_price(..., track_lb=True)` returns an anytime
  `(global_lb, best_obj)` trace. Each node's bound is the proven LP value
  when available, and otherwise safely falls back to the bound already
  certified at its parent (valid by LP monotonicity under added branching
  constraints) -- the same mechanism resolves P2 and P3 together.
- **New finding (folded into the P5 fix)** -- a node's full inherited column
  pool was handed to *both* Ryan-Foster children unfiltered, so a child's RMP
  could still select an old column violating the branching decision just
  added. This never invalidates a bound or an incumbent (see the fix-log
  entry in `bp_prototype.py` for the argument), so it is a
  search-effectiveness gap, not a soundness bug like P1/P2 -- but it defeats
  the purpose of Ryan-Foster branching. Fixed via `_filter_rf()`/
  `_child_pool()`, which also purges long-inactive columns (P5): each child
  keeps only the columns active in the parent's optimal solution plus a
  permanent per-part singleton safety net.

Regression tests: `run_p2_p3_p5_regression_tests()` in `bp_prototype.py`
(simplex/CG proven-flag behaviour, global-LB monotonicity + soundness +
gap-closure on a real search tree validated against brute force, and direct
unit tests of the RF pool filter). Re-validated end-to-end against brute
force across 60+ random instances (varied n, M, due-date tightness) with
zero mismatches and zero lower-bound-trace violations.

## Fixed: candidate reduction x `together` interaction (2026-07-01)
Design-review finding P1: the pricer's candidate reduction (drop parts with
`pi_j<=0`) was applied per-part, independent of Ryan-Foster `together`
decisions. A part forced `together` with an attractive partner can be
profitable only jointly, even if its own `pi_j<=0`; the per-part rule could
therefore make the pricer miss a genuine negative-reduced-cost column and
falsely certify master-LP optimality -- a soundness risk, not just a
performance one. Fixed in `prototype/bp_prototype.py` via union-find over
`together` pairs (transitive components); `doc/branch_and_price.tex`
(Section "Retrofitting...", CHANGE 0) and `cpp/pricer_oracle.h` (CHANGE 0)
are updated to match, so the C++ implementation (still a skeleton) does not
reintroduce the same bug. Regression tests: `run_p1_regression_tests()`
in `prototype/bp_prototype.py` (pairwise and transitive-chain cases, each
checked against a no-reduction ground truth and against the old per-part
rule to confirm the instances actually exercise the bug).

## Roadmap (in order)
1. Implement `solvePricer` in C++ by copying `solvePhi` and applying the six
   change points in `cpp/pricer_oracle.h`. Unit-test it against the Python
   `price(...)` on small instances (same pi, same Ryan-Foster constraints).
2. Wrap an LP solver (CLP/HiGHS/Gurobi) for the restricted master; check the
   master LP value against the Python prototype on small instances.
3. Add dual stabilisation, heuristic pricing + column pool, strong-branching
   Ryan-Foster selection, best-bound node order, time limits, primal heuristics.
4. Benchmark against the assignment-based B&B on Derived_Yu2022_small to test
   whether the stronger bound pushes the exact frontier past n=20.

## Provenance
Copied from `docs/Branch_and_Price/` (tex) and `experiments/branch_and_price/`
(python); the C++ files are new. Keep this folder as the working copy.
