# Branch-and-Price prototype (parallel-AM total tardiness)

`bp_prototype.py` — a **correct, self-contained reference implementation** of the
branch-and-price framework in `docs/Branch_and_Price/branch_and_price.tex`,
validated against brute-force partition enumeration.

## Status: validated, not production
- Full loop implemented: set-partitioning RMP (with a built-in Big-M simplex that
  returns duals), column generation, prize-collecting single-machine pricer
  (optional parts + dual rewards + Ryan-Foster filters), Ryan-Foster B&P tree.
- `python bp_prototype.py` runs a self-test (simplex duals) and one demo; a driver
  loop checks **B&P optimum == brute force** on random instances.
- Verified: 8/8 random instances (n=5-6, M=2) matched brute force exactly; the
  built-in n=6,M=2 demo matches (19.7762).
- Scales only to n ~ 8 (the pricer enumerates area-feasible subsets); it is a
  reference oracle to check a fast implementation against, not a solver.

## What is still missing (production checklist)
1. **Fast pricing = the crux.** Replace the brute subset-enumeration pricer with the
   retrofit of the existing C++ single-machine oracle (see the "Retrofitting the
   single-machine oracle into a pricer" section of the LaTeX draft): batch-position
   branching + optional-part discard branch + the prize-collecting bound
   LB_pc + dominance/memoization. This is the main engineering and the whole
   performance story.
2. **Real LP solver for the RMP.** The Big-M simplex here is for validation only.
   Use CLP / HiGHS / Gurobi with warm starts.
3. **Dual stabilisation.** Not implemented; needed for convergence at real sizes
   (set-partitioning masters are degenerate, duals oscillate).
4. **Heuristic pricing + column-pool management.** Prototype adds one exact column
   per iteration. Production: cheap heuristic pricer first, harvest several negative
   columns per call, purge long-inactive columns.
5. **Branching / search refinements.** Prototype uses DFS, first fractional pair, a
   simple bound prune. Production: strong-branching choice of the Ryan-Foster pair,
   best-bound node order, proper global-LB tracking, time limit, primal heuristics
   (rounding) for incumbents, and LP-bound rounding for integer-data instances.
6. **Language/performance.** Python prototype handles n<=8; a C++ port integrated
   with the oracle is required to reach and pass the n=20 exact frontier.

## Files
- `bp_prototype.py` — the prototype + self-test + brute-force validator.
