# Lower Bounds for the Single-Machine Oracle under Incremental (Type I / Type II) Branching

**Scope.** This note re-derives the analytical lower bounds used *inside* the
single-machine oracle when its internal search is changed from
"choose a whole next batch" (submask enumeration) to the **incremental
Azizoglu–Webster branching**:

- **Type I** — the next unassigned part opens a *new* last batch (i.e. the
  current last batch is closed);
- **Type II** — the next unassigned part is appended to the *current* last
  batch (capacity- and symmetry-feasible only).

The key structural change is that, at a generic node, the last batch is
**open**: it may still receive more parts via Type II, so its processing time
and completion are **not yet final**. The manuscript's analytical bounds
(Appendix A) are written for a *fresh start at time 0 with no open batch*, so
they must be generalized. We do **not** need a separate bound per child type:
Type I and Type II merely produce different node states, and one
open-batch-aware bound covers both.

> **Status.** All bounds below were verified by the brute-force validator
> `new_algorithm/validate_incremental_bounds.py` (42,000 random nodes, 3 seeds,
> n up to 7): the recommended forms have **zero soundness violations** and exact
> leaves. See Section 7.

---

## 1. Model and notation

Single machine, batches processed sequentially from time 0. For part $j$:
projected area $\alpha_j=l_jw_j$, volume $v_j$, height $h_j$, due date $d_j$.
Platform capacity $A=LW$. A batch $B$ has

$$
P(B)=S+V\sum_{j\in B}v_j+U\max_{j\in B}h_j ,\qquad
\sum_{j\in B}\alpha_j\le A .
$$

If batches are processed in order $B_1,B_2,\dots$, the completion of $B_t$ is
$C^{(t)}=\sum_{s\le t}P(B_s)$, and every $j\in B_t$ has $C_j=C^{(t)}$,
tardiness $T_j=\max\{0,C_j-d_j\}$. The oracle minimizes $\sum_j T_j$ over all
batchings of its input set (all parts mandatory).

### 1.1 Incremental node state

A node $N$ of the incremental search fixes a partial schedule:

- **closed batches** $B_1,\dots,B_{r-1}$ — frozen (a batch stops changing once
  it is no longer the last one);
- **open batch** $B_r$ — the current last batch, with aggregates
  $a_r=\sum_{j\in B_r}\alpha_j$, $v_r=\sum_{j\in B_r}v_j$,
  $h_r=\max_{j\in B_r}h_j$;
- **unassigned set** $R=\mathcal U(N)$ — parts not yet placed.

Define the **prefix completion** (start time of the open batch)

$$
F=\sum_{t<r}P(B_t)\quad(F=0\text{ if no closed batch}),
$$

the open batch's current processing time and completion

$$
p_r=S+Vv_r+Uh_r,\qquad c_r=F+p_r,
$$

and the open batch's **residual capacity** $\rho=A-a_r$.

The two children update exactly these quantities:

| child | effect on state |
|------|------------------|
| **Type I** (part $j$ opens new batch) | $F\leftarrow c_r$; new open batch $\{j\}$; $R\leftarrow R\setminus\{j\}$ |
| **Type II** (part $j$ joins $B_r$) | $F$ unchanged; $a_r,v_r,h_r$ updated; $c_r$ grows; $R\leftarrow R\setminus\{j\}$ |

The **root** is the degenerate case with no batch at all ($F=0$, no open
batch); there the bounds below reduce to the manuscript's Appendix-A form
(Section 5).

### 1.2 Objective decomposition

The parts split into three disjoint groups, so their tardiness lower bounds
add:

$$
\sum_j T_j
=\underbrace{\sum_{t<r}\sum_{j\in B_t}T_j}_{\text{closed: exact}}
+\underbrace{\sum_{j\in B_r}T_j}_{\text{open batch}}
+\underbrace{\sum_{j\in R}T_j}_{\text{unassigned}} .
$$

- **Closed batches:** $C^{(t)}$ ($t<r$) are final, so $\sum_{t<r}\sum_{j\in B_t}\max\{0,C^{(t)}-d_j\}$ is **exact**.
- **Open batch:** $C_j\ge c_r$ for $j\in B_r$ (its completion can only grow as
  Type II adds parts), hence $\sum_{j\in B_r}\max\{0,c_r-d_j\}$ is a valid lower
  bound — call it the *open-batch floor*.
- **Unassigned:** bounded by $LB^{\mathrm{par}}$ / $LB^{\mathrm{pos}}$ below.

---

## 2. Per-part completion lemma

This is the engine for everything that follows.

> **Lemma 1.** Fix any feasible completion (leaf) of $N$'s subtree and any
> $j\in R$. Then
>
> 1. $C_j\ \ge\ c_r+Vv_j$ (always);
> 2. if $\alpha_j>\rho$ (part cannot fit in the open batch), then
>    $C_j\ \ge\ c_r+S+Vv_j+Uh_j$.

**Proof.** In the leaf, $j$ lies in some batch $B_q$ with $q\ge r$ (the open
batch as it is finally closed, or a later batch).

*Case $j\in$ final $B_r$.* The final open batch contains the current content
plus $\{j\}$ (and possibly more), so its volume is $\ge v_r+v_j$ and its max
height is $\ge h_r$. Thus
$P(\text{final }B_r)\ge S+V(v_r+v_j)+Uh_r=p_r+Vv_j$, and it still starts at
$F$. Hence $C_j=F+P(\text{final }B_r)\ge F+p_r+Vv_j=c_r+Vv_j$.

*Case $j\in B_q$, $q>r$.* The final open batch completes at
$C^{(r)}_{\text{fin}}\ge c_r$ (it only grows), and
$C_j=C^{(q)}\ge C^{(r)}_{\text{fin}}+P(B_q)\ge c_r+\bigl(S+Vv_j+Uh_j\bigr)
\ge c_r+Vv_j$.

This proves (1). For (2): if $\alpha_j>\rho=A-a_r$, then $a_r+\alpha_j>A$, so
$j$ cannot be in the final open batch (capacity); only the second case
applies, giving $C_j\ge c_r+S+Vv_j+Uh_j$. $\qquad\blacksquare$

---

## 3. Parallel (singleton) bound $LB^{\mathrm{par}}$

For each $j\in R$ define the **per-part completion floor**

$$
\ell_j=
\begin{cases}
c_r+Vv_j, & \alpha_j\le\rho\quad(\text{can join the open batch}),\\[2pt]
c_r+S+Vv_j+Uh_j, & \alpha_j>\rho\quad(\text{must open a new batch}).
\end{cases}
$$

> **Theorem 2.** $\displaystyle \sum_{j\in R}T_j\ \ge\
> LB^{\mathrm{par}}(R):=\sum_{j\in R}\max\{0,\ \ell_j-d_j\}.$

**Proof.** By Lemma 1, $C_j\ge\ell_j$ for every $j\in R$ in every leaf. Since
$t\mapsto\max\{0,t-d_j\}$ is nondecreasing,
$T_j=\max\{0,C_j-d_j\}\ge\max\{0,\ell_j-d_j\}$; sum over $R$. $\qquad\blacksquare$

This is strictly stronger than ignoring the open batch: it credits the work
already committed in $B_r$ (via $c_r$) and uses the residual capacity $\rho$
to decide whether a fresh $S+Uh_j$ must be charged. (Validated: 0 violations.)

---

## 4. Positional bound $LB^{\mathrm{pos}}$ (open-batch-aware)

We generalize the Appendix-A surrogate. Sort $R$ ($|R|=q$) **independently**
by area, volume, height, due date:

$$
\alpha_{(1)}\le\cdots\le\alpha_{(q)},\quad
v_{\langle1\rangle}\le\cdots\le v_{\langle q\rangle},\quad
h_{[1]}\le\cdots\le h_{[q]},\quad
d^{\uparrow}_{[1]}\le\cdots\le d^{\uparrow}_{[q]} .
$$

For position $k=1,\dots,q$ define cumulative area / volume **including the
open-batch pre-load**:

$$
A_k=\sum_{r=1}^{k}\alpha_{(r)},\qquad
\beta_k=\min\Bigl\{\,k+\mathbb{1}[a_r>0],\ \max\bigl\{1,\ \lceil (a_r+A_k)/A\rceil\bigr\}\Bigr\},
$$
$$
\underline V_k=\sum_{r=1}^{k}v_{\langle r\rangle}.
$$

(The clamp $k+\mathbb 1[a_r>0]$ caps the batch count at the maximum number of
nonempty batches: the open batch plus $k$ singletons.)

### 4.1 Height term — the partition-minimum form (proven)

The height-time over the $\beta_k$ batches covering the open batch and the
first $k$ parts of $R$ is a *sum of batch maxima*. Treat $h_r$ as one extra
"height token" forced into the open batch, and the $k$ smallest $R$-heights as
the other tokens:

$$
\mathcal T_k=\bigl\{\,h_r\ \text{if an open batch exists}\,\bigr\}\cup\{h_{[1]},\dots,h_{[k]}\},
\qquad m_k=|\mathcal T_k| .
$$

Let $g_{(1)}\le\cdots\le g_{(m_k)}$ be $\mathcal T_k$ sorted, and
$\tilde\beta_k=\min\{\beta_k,m_k\}$. Define

$$
\boxed{\ \underline H_k=\;g_{(m_k)}\;+\;\sum_{i=1}^{\tilde\beta_k-1} g_{(i)}\ }
\qquad(\text{largest token}+\text{the }\tilde\beta_k-1\text{ smallest tokens}).
$$

The **surrogate completion** of the $k$-th finishing part of $R$ is

$$
\underline C_k=F+\beta_k S+V\bigl(v_r+\underline V_k\bigr)+U\,\underline H_k ,
\qquad
LB^{\mathrm{pos}}(R)=\sum_{k=1}^{q}\max\bigl\{0,\ \underline C_k-d^{\uparrow}_{[k]}\bigr\}.
$$

> **Proposition 3.** $\underline C_k$ lower-bounds the completion time of the
> $k$-th-to-finish part of $R$ in any leaf; hence
> $LB^{\mathrm{pos}}(R)\le\sum_{j\in R}T_j$.

**Proof.** In any leaf, the batches $\mathcal B$ that hold the open-batch
content and the $k$ earliest-finishing $R$-parts number
$|\mathcal B|\ge\beta_k$ (area argument: total area $\ge a_r+A_k$, each batch
$\le A$), each charging $S$, so the $S$-time is $\ge\beta_k S$; the volume-time
is $\ge V(v_r+\underline V_k)$.

For the height-time $U\sum_{B\in\mathcal B}\max_{j\in B}h_j$: the heights
actually present dominate $\mathcal T_k$ (the $k$ real $R$-heights are
$\ge h_{[1]},\dots,h_{[k]}$, and $h_r$ is present). Distributing
$|\mathcal B|\ge\tilde\beta_k$ tokens into the batches, the minimum possible
sum of batch maxima over **any** partition of a token multiset into
$g$ groups equals *(largest token) + (sum of the $g-1$ smallest tokens)*
— cluster the large tokens into one group and make the rest singletons of the
smallest tokens. Hence $\sum_{B\in\mathcal B}\max h\ge\underline H_k$. (The
constraint that $h_r$ sits in the open group only *raises* the partition
minimum, so the unconstrained minimum $\underline H_k$ remains a valid lower
bound.) Adding the start $F$ gives $\underline C_k\le$ actual completion;
pairing sorted surrogate completions with sorted due dates is a valid lower
bound on $\sum\max\{0,C-d\}$ (rearrangement, as in Appendix A). $\qquad\blacksquare$

### 4.2 Why the naive height terms are unsound (settled empirically)

Two tempting forms were tested and **refuted**:

- $\underline H_k^{\text{safe}}=\sum_{r=1}^{\beta_k-1}h_{[r]}+h_{[k]}$
  (the manuscript form, $R$-heights only);
- $\underline H_k^{\text{tight}}=\sum_{r=1}^{\beta_k-1}h_{[r]}+\max\{h_{[k]},h_r\}$.

Both **over-count** when the open batch ends up holding no $R$-part and $h_r$
is small: they credit a small $R$-height to the open-batch slot that the real
schedule does not pay. The validator found hundreds of violations for each
(Section 7). The partition-minimum form in §4.1 fixes this by entering $h_r$
as a genuine token. At the root (no open batch) all three coincide with
Appendix A.

---

## 5. Combined node bound, leaf-exactness, root reduction

$$
\boxed{\,
LB(N)=
\underbrace{\sum_{t<r}\sum_{j\in B_t}\max\{0,C^{(t)}-d_j\}}_{\text{closed, exact}}
+\underbrace{\sum_{j\in B_r}\max\{0,c_r-d_j\}}_{\text{open-batch floor}}
+\max\bigl\{LB^{\mathrm{par}}(R),\,LB^{\mathrm{pos}}(R)\bigr\}\, }
$$

- **Validity.** The three groups are disjoint; each term is a valid lower
  bound on its group's tardiness (Section 1.2, Theorem 2, Proposition 3), so
  their sum bounds $\sum_j T_j$.
- **Leaf-exactness.** At a leaf $R=\varnothing$ and the open batch is final, so
  $c_r$ is exact and the open-batch floor becomes the exact open-batch
  tardiness; $LB(N)=\sum_j T_j$. No optimum is ever pruned and leaves report
  the true objective. (Validated: 0 leaf failures.)
- **Root reduction.** At the root (no batch): $F=0$, no open batch, $\rho=A$,
  and every part "must open a new batch", so
  $\ell_j=S+Vv_j+Uh_j$ and $\beta_k=\lceil A_k/A\rceil$,
  $\underline C_k=\beta_k S+V\underline V_k+U(\sum_{r\le\beta_k-1}h_{[r]}+h_{[k]})$
  — **exactly** the manuscript's $LB^{\mathrm{par}}$ and $LB^{\mathrm{pos}}$
  (Appendix A). The new bounds are a strict generalization.

---

## 6. Preview: prize-collecting extension (Type III), for later

When this branching is reused for the **Lagrangian pricing** subproblem, each
free part $j$ carries a reward $u_j$ and may be **dropped** (Type III). The
machinery above transfers with two changes:

1. The objective becomes $\sum_{j\in\text{chosen}}(T_j-u_j)$; in every
   completion floor replace $\max\{0,\ell_j-d_j\}$ by
   $\max\{0,\ell_j-d_j\}-u_j$ for *mandatory* parts, and by
   $\min\{0,\ \max\{0,\ell_j-d_j\}-u_j\}$ for *free* parts (include only if it
   helps).
2. A node is a candidate solution once no mandatory part remains in $R$.

The completion floors $\ell_j$, $\underline C_k$ are unchanged (they bound
completion regardless of reward). The full reward-aware derivation is deferred
to a separate note; the present note targets the **base oracle** swap.

---

## 7. Validation results (`new_algorithm/validate_incremental_bounds.py`)

Random instances ($n=3\text{–}7$), random reachable incremental nodes, each
node's bound compared against the exhaustive subtree optimum. Three height
variants tested. Representative run (≈15,000 nodes/seed):

| height term | soundness violations | leaf-exact | mean $LB/\text{opt}$ |
|-------------|----------------------|-----------|----------------------|
| `safe`  (manuscript, ignores $h_r$)            | **545 / 15000** ✗ | — | 0.801 |
| `tight` ($+\max(h_{[k]},h_r)$, naive)          | **579 / 15000** ✗ | — | 0.812 |
| **`iron`** (partition-minimum, §4.1)           | **0 / 15000** ✓   | 0 failures ✓ | 0.804 |

Conclusions:

1. **$LB^{\mathrm{par}}$ and the partition-minimum $LB^{\mathrm{pos}}$ are
   sound** (0 violations over 42,000 nodes across 3 seeds) and **leaf-exact**.
2. The naive height terms `safe`/`tight` are **unsound** with an open batch —
   do not use them; the manuscript surrogate must be replaced by §4.1 inside
   the incremental oracle.
3. The sound form is **as tight** as the naive ones (mean ratio 0.804 vs
   0.801/0.812), so soundness costs nothing in practice.

**Next (Step 3):** wire the incremental Type I/II branching + the §1.2 + §3 +
§4.1 node bound into `branch_and_cut`, and re-validate end-to-end against the
current submask oracle on the 13 instances (node counts, time, identical
optima).

---

## 8. Step-3 empirical verdict: do NOT replace the submask oracle

Before porting to C++, the incremental scheme was prototyped against the
existing submask oracle and exhaustive optima
(`new_algorithm/inc_oracle_proto.py`). Three findings, all decisive:

**(a) Dominance soundness.** With an open batch, the natural scheduled-set
`(C,TT)` dominance (Zixuan's `Su`-only key) is **unsound**: it returned wrong
optima on 24/800 random instances (e.g. 45.23 vs true 40.60). Only the finer
key `(scheduled-set, open-batch)` is sound (0 mismatches). *Implication: any
oracle using a scheduled-set-only dominance together with an open last batch
can report incorrect optima — worth flagging for the single-machine oracle
work.*

**(b) Head-to-head work.** With the sound dominance and the validated bounds,
the incremental scheme does **more** total work than the submask oracle, and
the gap **grows** with size:

| n (single machine) | submask / incremental bound-evals |
|--------------------|-----------------------------------|
| 8                  | 0.58× |
| 10                 | 0.37× |
| 12                 | 0.34× |
| 11, large capacity A=20 | 0.43× |

(submask consistently ~2–3× less work; same optima throughout).

**(c) Why (structural, not implementation).** In this problem the build-area
capacity keeps batches small, so the submask "next batch = any feasible
subset" is a *polynomial* number of children, not $2^{|R|}$. More importantly,
submask's batches are all **closed**, so the scheduled set is a *complete*
state and the scheduled-set dominance is both **sound and maximally
collapsing**. The incremental scheme enlarges the state to
(scheduled-set × open-batch); its only sound dominance is correspondingly
weaker, and that loss outweighs the smaller branching factor. Total work is
governed by dominance strength, not branching factor.

**Conclusion.** Keep the existing submask oracle. The derivation and validator
in Sections 1–7 remain valuable: they give *correct* open-batch-aware bounds,
which are exactly what the **prize-collecting pricing oracle** (Section 6)
needs — there, parts can be *dropped*, the scheduled set is not a complete
state regardless of scheme, and incremental branching may still pay off. The
base oracle, however, is best left as is.
