# 子问题 ↔ B&P pricing ↔ labeling:对应关系与路线锚点

> 一页备忘:说明本项目 Lagrangian-B&B 的**子问题**与 Branch-and-Price(B&P)的
> **pricing 问题**、以及 **labeling 算法**之间的等价关系,作为论文写作与后续开发的锚点。
> 相关代码:`new_algorithm/prize_oracle.{h,cpp}`(子问题求解器)、`new_algorithm/lagrangian_bb.cpp`
> (上层)、`references/Zixuan_codes/DynamicProgramming.cpp`(子集 DP 雏形)。

## 1. 三者的等价

本项目的 Lagrangian 子问题(残量形式,机器 m):
$$g_m(u)=\min_{A_m\subseteq U}\Big[\ \Phi(S_m\cup A_m)-\sum_{j\in A_m}u_j\ \Big]$$
其中 `u_j` 是对偶价格,`Φ` 是单机最优(批)调度总延误,`S_m` 为强制基集。

它**就是 Branch-and-Price 的 pricing 问题**:

| B&P / labeling | 本项目子问题 |
|---|---|
| 列 = 一条路径 / 一个调度 | 列 = 一台机器的一个批调度 |
| reduced cost = 成本 − Σ 对偶 | `Φ(A) − Σ_{j∈A} u_j` |
| label = (已访问集合, 资源, 累计 reduced cost) | 节点 = (已排集合, 完工时间 C, 累计值 G) |
| 资源沿弧扩展(时间/容量) | 完工时间 C 沿"加一批"扩展 |
| **label dominance**:同状态下资源、成本占优则剪 | **支配规则**:同一已排集合下 (C, G) 占优则剪 |

## 2. 现有 prize oracle 已经是"DFS 形式的 labeling"

`prize_oracle.cpp` 加入支配规则(`domCheckInsert`:同已排集合掩码下,(C,G) 被占优则剪)后,
其状态/支配与 labeling 完全一致:**label = (scheduled-set 掩码, C, G),按 scheduled-set 分组做支配**。
差别仅在组织形式:
- 现状:**递归 DFS + 记忆**推进 label;
- 经典 labeling:在**状态图上按资源(C)顺序前向扩展**,常配 **bucket graph**(按 C 分桶)与更强 dominance。
即 dominance 思想相同,缺的是 labeling 的系统化扩展结构与桶加速。

## 3. 战略含义:Lagrangian-B&B ≈ "没有 LP 主问题的 B&P"

- 我们用次梯度求的 Lagrangian 对偶界 **= 集合划分的 LP 松弛界 = 列生成界**
  (这正是实测根界达 ~99–100% 最优的原因)。
- 我们的子问题 = pricing;我们用 DFS+dominance 解,B&P 用 labeling(DP)解。
- 因此从本方法到 B&P,差的只是"**用 labeling 解 pricing + 显式 LP 主问题(+割)**"。

`references/Zixuan_codes/DynamicProgramming.cpp` 的子集 DP `V(Q,t)`(状态=零件子集+起始时间,
`global_memo` 即 DP 表)正是 labeling/DP 的雏形,可复用为 pricing 的求解核。

## 4. 两条路线

1. **务实(提速现有 Lagrangian-B&B)**:把子问题从 DFS 改写为**显式 labeling/DP**
   (按 C 分桶、强化 dominance,或复用 `V(Q,t)`),降低每节点"子问题求解 × 迭代 × M"的成本。
2. **进阶(对标 SOTA)**:完整 **Branch-and-Price** —— LP 主问题 + labeling pricing + 割
   (参见 `docs/BPC_Analysis_Yu_et_al.md` 对 INFORMS 论文的拆解)。

## 5. 诚实的难点:tardiness 对 labeling 不友好

B&P 的 labeling 在 **TWCT(总加权完工时间)** 上很干净(Yu et al. 因此能上几十上百件)。
但**总延误**不是沿路径单调可加的"好资源":要让 label dominance 严格成立,状态必须同时锁住
"已排集合 + 完工时间",这会使状态数仍可能指数级。所以:
- 改写为显式 labeling 能实质提速,但**不会魔法般变多项式**;
- tardiness 版的 labeling pricing 是公认偏难的一块,是走向完整 B&P 时的主要技术风险点。

## 一句话

**子问题 = B&P 的 pricing;加了支配规则的 prize oracle 已是 DFS 形式的 labeling;
本方法本质是"用次梯度代替 LP 主问题"的 B&P。** 提速 → 显式 labeling/DP;上规模 → 完整 B&P。
