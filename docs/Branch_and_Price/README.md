# Branch-and-Price draft

`branch_and_price.tex` — a standalone methodology draft (elsarticle, same template
as the main manuscript) sketching a branch-and-price / column-generation framework
for the parallel-AM total-tardiness problem:

- Dantzig-Wolfe reformulation; columns = single-machine schedules.
- Set-partitioning restricted master + reduced cost.
- Pricing = prize-collecting single-machine batch total-tardiness (reuses the
  existing single-machine oracle Phi with optional parts + dual rewards).
- Ryan-Foster (same-machine / different-machine) branching, pricing-compatible.
- Full algorithm + implementation notes (dual stabilisation, heuristic pricing,
  search order, relation to Benders / Nascimento 2024).

Compile on a machine with elsarticle:  `pdflatex branch_and_price` (x2).
Self-contained bibliography (manual thebibliography), no external .bib needed.
