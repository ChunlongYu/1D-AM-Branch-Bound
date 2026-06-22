# Lagrangian-松弛 B&B 升级设计草案

> 目标:在不推翻现有"指派 B&B + 单机 oracle"架构的前提下,用 **Lagrangian 松弛**
> 替换松弛的解析下界(实测覆盖率仅 ~14%),把可证最优规模从 ~15 件推到 ~20–25 件。
> 核心结论:**Lagrangian 子问题 = 你现有 oracle 的"带零件奖励"版本**,主要工作是把
> oracle 扩展成"带奖励 + 可选纳入"的变体,外层加对偶价格与次梯度更新。

---

## 1. 记号与集合划分模型

- 零件 $J=\{1,\dots,n\}$,同型机器 $\mathcal{M}=\{1,\dots,M\}$。
- $\Phi(P)$:把零件集合 $P$ 放在**一台机**上的最优总延误(= 现有 oracle 的返回值),$\Phi(\emptyset)=0$。
- 一个"单机调度列" $s$ 覆盖零件集 $P(s)$,成本 $c_s$ = 该调度的总延误;取 $c_s=\Phi(P(s))$。
- $a_{js}=1$ 当 $j\in P(s)$。$\lambda_s\in\{0,1\}$ 表示是否选用列 $s$。

集合划分(set-partitioning)模型:
$$
\min \sum_{s} c_s\lambda_s
\quad\text{s.t.}\quad
\underbrace{\sum_s a_{js}\lambda_s = 1\ \ \forall j}_{\text{每个零件被覆盖一次}\;[u_j]},
\quad
\underbrace{\sum_s \lambda_s \le M}_{\text{至多 }M\text{ 台机}\;[\sigma\le 0]},
\quad \lambda_s\in\{0,1\}.
$$

---

## 2. 对偶化哪组约束

**对偶化"每个零件被覆盖一次"约束**(乘子 $u_j$,等式约束 → $u_j$ 自由号)。机器约束
$\sum_s\lambda_s\le M$ 保留在子问题里。Lagrangian 函数:
$$
L(u)=\sum_j u_j + \min_{\lambda}\sum_s\Big(\underbrace{c_s-\sum_{j\in P(s)}u_j}_{\bar c_s(u)\ \text{(reduced cost)}}\Big)\lambda_s
\quad\text{s.t.}\quad \sum_s\lambda_s\le M,\ \lambda\in\{0,1\}.
$$

内层在"至多 $M$ 列"约束下选 reduced cost 最负的列。定义**定价/子问题**:
$$
\boxed{\ \bar c^\*(u)=\min_{P\subseteq J}\Big[\ \Phi(P)-\sum_{j\in P}u_j\ \Big]\ }\qquad(\text{空集给 }0,\ \text{故}\ \bar c^\*(u)\le 0)
$$
即:**选一个零件子集 $P$,在一台机上排它,最小化(总延误 − 已收集的奖励 $\sum_{j\in P}u_j$)**
——这是一个"**带奖励的单机批调度(prize-collecting single-machine batch scheduling)**"。

---

## 3. Lagrangian 下界

根节点(机器全同、全空),$M$ 个子问题相同,可取最简形式:
$$
L(u)=\sum_j u_j + M\cdot \bar c^\*(u)\ \le\ \mathrm{OPT}.
$$
对任意 $u$ 都是合法下界;再对 $u$ 取上确界得 **Lagrangian 对偶界**
$\max_u L(u)\le \mathrm{OPT}$。由 LP 对偶,$\max_u L(u)$ **等于集合划分模型的 LP 松弛界**
(= 列生成界),所以它**本质上是能拿到的最紧的那个界**——这正是补在你 ~14% 痛点上的东西。

> 关键:**子问题只要给出合法下界即可**(不必每次精确解)。即用
> $\underline\Phi(P)-\sum_{j\in P}u_j$ 的下界版本(例如把现有 positional 界减去奖励)也保持
> $L(u)$ 合法,只是稍松。→ 可以"先用便宜界、必要时才精确解",控制开销。

---

## 4. 在 B&B 节点上用(残量 Lagrangian)

B&B 仍按"零件→机器"指派分支。节点 $N$ 已把 $\mathcal{S}_m(N)$ 固定到机器 $m$,自由件
$\mathcal{U}(N)$ 待分配。对"每个自由件被覆盖一次"对偶化($j\in\mathcal{U}(N)$ 的 $u_j$),
子问题**按机器分解**(注意各机有不同的**强制基集** $\mathcal{S}_m(N)$):
$$
g_m(u)=\min_{A_m\subseteq\mathcal{U}(N)}\Big[\ \Phi\big(\mathcal{S}_m(N)\cup A_m\big)-\sum_{j\in A_m}u_j\ \Big],
$$
$$
LB_{\mathrm{LR}}(N,u)=\sum_{j\in\mathcal{U}(N)}u_j+\sum_{m\in\mathcal{M}} g_m(u)\ \le\ \mathrm{OPT}(N).
$$
即"**带强制基集 $\mathcal{S}_m(N)$ + 自由件带奖励**的单机子问题",每台机各解一次。
取 $\max_u$(次梯度,见 §5)得该节点的紧下界,替换现有 `machineLB` 之和。

> 因为机器同型但**已固定的基集不同**,$M$ 个子问题不再相同,需各算一次
> (根节点退化为相同 → §3 的 $M\cdot\bar c^\*$)。

---

## 5. 次梯度更新

给定当前对偶 $u^t$ 与各子问题最优子集 $A_m^t$,覆盖约束 $j$ 的**次梯度**:
$$
g_j^t = 1-\sum_{m}\big[\,j\in A_m^t\,\big]\qquad(j\in\mathcal{U}(N)),
$$
即"该自由件在子问题里被选了几次,与 1 的差"。更新
$$
u_j^{t+1}=u_j^{t}+\theta_t\,g_j^t,\qquad
\theta_t=\frac{\rho_t\,(UB-L(u^t))}{\sum_j (g_j^t)^2},
$$
$\rho_t\in(0,2]$ 递减(被卡住若干步就减半)。迭代少量步(如 20–50,父节点 warm-start
$u$)即可拿到不错的界;若某步 $L(u^t)\ge UB$ 立即剪枝。也可用 bundle 法更稳。

---

## 6. 需要改的代码 / 可复用的部分

| 模块 | 现状 | 升级 |
|---|---|---|
| 单机 oracle `branch_and_cut` | 解"固定集合 $P$ 的最优总延误 $\Phi(P)$" | **扩成 prize-collecting 变体**:强制基集 + 自由件带奖励 $u_j$,可不纳入;目标 = 总延误 − Σ纳入奖励。这是**主要工作量**。 |
| 节点下界 | `Σ machineLB + 未分配松弛`(~14%) | 换成 $LB_{\mathrm{LR}}(N,u)$(§4),含次梯度循环 |
| 上层 B&B `solveParallelMachine` | 指派分支 + 均衡 diving + 守门叶子评估 | **几乎不动**;只把节点下界函数替换,父子间 warm-start 对偶 $u$ |
| 记忆池缓存 | 缓存 $\Phi(Q)$ | 仍可缓存 $\Phi(\cdot)$;prize-collecting 结果与 $u$ 有关,不直接缓存 |
| 叶子精确评估 / 守门 | 已实现 | 不变(叶子仍用 $\Phi$ 精确求和) |

**最难的一块**:把 oracle 改成 prize-collecting + 强制基集。改法:在 oracle 的批调度
B&B 里,节点状态除"已排序列"外,标记每个自由件为"待定/已纳入/已排除";终止条件要求
强制基集全部排完,自由件可留空;目标累加 (批延误) 减 (纳入自由件的 $u_j$);其下界相应
减去"仍可纳入的正奖励上界"。规模与现有 oracle 同量级(≤ |S_m|+|U|)。

---

## 7. 工程量 / 风险 / 预期收益

- **工程量**:中。次梯度 + 残量 Lagrangian 是标准件(~1–2 天);prize-collecting oracle
  是主要风险点(~3–5 天,含正确性对拍)。
- **正确性验证**:沿用现有"独立穷举对拍";另外 $\max_u L(u)$ 应 ≤ 真最优且 ≥ 现有解析界,
  可单测核对。
- **预期收益**:界从 ~14% 提到接近 LP 界(文献中同型平行机总延误用此法可达 ~25 件,
  见 Şen 等 IJPE;Kacem & Souayah RAIRO)。配合你已有的 AM 专属 positional 界(作为
  子问题的便宜下界)与对称消除分支,有望把可证最优规模显著上推。
- **退路(更轻)**:只在**根节点**算 Lagrangian 界(§3),得到强根界 + 一组对偶 $u^\*$;
  节点界用 $u^\*$ 加权的简化式。实现快但界略松、且节点界的严格性要小心论证。建议优先做 §4
  的残量 Lagrangian(更紧、更正规)。

---

## 8. 论文叙事(升级后)

"针对一维面积约束的平行 AM 批调度总延误问题,提出 **Lagrangian 分解精确算法**:对偶化
指派约束后问题分解为带奖励的单机批调度子问题(由精确 oracle 求解),Lagrangian 对偶给出
逼近 LP 松弛的下界;上层以对称消除的指派分支 + 均衡 diving + 增量守门叶子评估求解。与
Gurobi MILP 及列生成对比……" —— 既保留你已完成的 oracle / 分支 / 叶子技巧 / positional 界,
又补上审稿人必问的"下界为何这么紧",规模与可发性都上一个台阶。
