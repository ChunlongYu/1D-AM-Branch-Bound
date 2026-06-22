# 项目总结:一维 AM 平行机批调度(总延误)精确求解

> 面向论文写作与方向决策的汇总。涵盖:方法、正确性、与 MILP 对比、下界分析、
> Lagrangian 路线的可行性/瓶颈,以及结论与建议。数据来源:`experiments/results/`、
> `new_algorithm/`、各 `docs/*` 专题文档。

---

## 1. 问题与方法

**问题**:n 个零件分配到 M 台**同型** PBF 增材机;每台机器把所分零件成批、批次串行加工;
批加工时间 `S + V·Σv + U·max h`,一维面积约束 `Σ l·w ≤ L·W`;目标 **最小化总延误**。

**方法(本文 B&B,两层)**:
- **上层(指派 B&B)**:只决定"零件→机器"。分支 = 选一个未分配零件分到候选机器;
  **规范开机**对称消除(零件只能分到已用机器或第一台未用机器)。
- **下层(单机 oracle)**:给定一台机器的零件集,精确分批+排序求 Φ(自身又是一套
  分批 B&B,带支配规则、par/pos 下界、自适应节点选择)。
- **节点下界** = `Σ_m max(记忆池, par, pos)(S_m) + Σ_{未分配} 单件延误`。
- **节点选择**:均衡 diving(选各机器件数最大值最小者)warm-up + 最优优先(N_max/N_min)。
- **叶子**:增量守门评估——机器按集合从小到大逐台精确求 Φ,混合界 ≥ UB 即剪,跳过最贵调用。
- 修复了原单机 oracle 的一处 bug(批次按 unordered_map 哈希序遍历 → 结果偏大且依赖输入序;
  详见 `ORACLE_BUG_REPORT.md`)。

---

## 2. 正确性

在**所有 30 个"B&B 与 Gurobi MILP 都证到最优"的算例(n≤15)上,目标值完全一致,0 处不符**。
两条独立实现得到同一最优值,是两边都正确的强证据。另有独立穷举对拍(并行层 18/18、
prize 子问题 24/24)。

---

## 3. 与 MILP(Gurobi)对比

实验:全部 13 个算例 × M∈{2,3,4},时限 1800s(`run_exp_BB` / `run_exp_milp`)。

**(a) M=2(紧、延误大 —— 本文方法的甜区):B&B 比 MILP 快 14–46×**

| M=2 | B&B | MILP | 加速 |
|---|---|---|---|
| 10part | 0.10s | 4.41s | 46× |
| 12part | 0.70s | 20.0s | 29× |
| 13part | 2.97s | 72.1s | 24× |
| 14part | 6.90s | 108s | 16× |
| 15part | 27.6s | 475s | 17× |

**(b) M=3,4(机器多、延误趋零):MILP 反超**——延误低使 MILP 的 LP 松弛很紧、秒解,
而 B&B 的指派树随机器数增长。例:15part M=3 MILP 1.2s vs B&B 7.7s;15part_2-S M=4 MILP 0.7s vs B&B 19.6s。

**(c) n=20:B&B 全线超时;MILP 多数秒解**(M≥3 常 <4s)。20part_4-S M=2 两者都难(MILP gap 33.6%)。

**总计**:两边都证最优的格子里,**B&B 更快 27 个、MILP 更快 6 个**;n=20 B&B 0/6、MILP 5/6 解出。

**一句话叙事**:B&B 在**机器少、负载紧、中小规模(M=2, n≤15)**对 Gurobi 有 1–1.5 个数量级优势;
机器多或 n=20 的松弛情形 MILP 更具竞争力。

---

## 4. 下界分析(为什么解析界松,试过什么)

- **快速解析界(par/pos)对大/集中集合很松**:覆盖真值的比例随集合增大降到 **13–20%**;
  positional 在本数据上几乎不优于 parallel(par 几乎全胜)。
- **根节点界(单件和)= 最优的 38–88%**(M 越大越紧)。
- 试过但**对本数据无效**的更强界:
  - **serial DP 界**:覆盖率 ≈ 0(V 极小、把 setup/高度只摊一次,丢掉主成本);
  - **全局 M 机位置界**:M≥2 恒为 0。
  根因一致:这些松弛把"零件在同一台机上互相排队推迟"的主成本乐观掉了。

---

## 5. Lagrangian 路线(强界存在,但太贵)

对偶化"每个零件分一次"约束 → 问题分解为各机的 **prize-collecting 单机子问题**
`min_A[Φ(S_m∪A)−Σu_j]`(= Branch-and-Price 的 pricing;子问题求解器即"带奖励 + 强制基集"的批调度 B&B)。

- **界质量:根节点 Lagrangian 界 = 最优的 99.7–100%**(对比解析界 38–88%),实测:
  | 实例 | M=2 | M=3 |
  |---|---|---|
  | 10part | 44%→99.7% | 88%→98.9% |
  | 12part | 38%→**100%** | 66%→**100%** |
- **完整 Lagrangian-B&B**:节点数坍塌(12part M=2:**24** vs 基础版 2351),目标值对。
- **但 wall-time 不竞争**:每节点 = 次梯度 × 指数级 prize 子问题,贵约 1000×。
  调优后(节点级"便宜后精确"+ dedup + 贪心 + 收敛即停 + 降迭代步):
  12part M=2 4.75s(基础 0.70s)、13part M=2 24.5s(基础 3.0s)、**15part M=2 连根界都算不动**。
- **子问题无高效 labeling/DP 结构**:实测"EDD 排序+连续批次"≠ 最优(280 例 167 不符,差达 203%);
  tardiness + 面积批次不具备 TWCT 那种小状态,labeling 提速仅常数倍(详见
  `Labeling_Feasibility_Assessment.md`、`Pricing_Labeling_Connection.md`)。

**结论**:Lagrangian 界**紧且可达(≈最优)**,但**计算成本对本问题过高**,**不是 wall-time 更优的求解器**。
其价值在于:① 强对偶界(可作 gap 证书 / 论文"界质量"对比);② 通往 B&P 的方法学认识。

---

## 6. 给论文/决策的建议

1. **主线写基础 B&B**:两层分解 + 规范开机分支 + par/pos 节点界 + 均衡 diving + 增量守门叶子;
   AM 专属的 **positional 下界(附录 A)** 是独立贡献;与 MILP 对比是硬结果(M=2 中小规模数量级优势)。
2. **诚实划定适用域**:n≤15、M=2 为强项;n=20 / 大 M 列为当前上限与未来工作。
3. **把"更强的界"写成有数据支撑的取舍**:"集合划分式 Lagrangian 界可达最优(根界 99–100%),
   但其 pricing(tardiness + 面积批次)计算代价过高且无高效 labeling 结构,故本文采用更轻的组合 B&B。"
   这种"试过 SOTA 思路、用数据说明为何不采用"的论述,审稿很认可。
4. **未来工作**:完整 Branch-and-Price(需要更快的 tardiness pricing,高难度);或对更大 M/规模的专门加速。

---

## 7. 关键产物索引

- 基础算法:`src/`(`ParallelBranchBound`、`BranchBound`);论文:`docs/1D-AM_Parallel/`。
- 实验:`experiments/`(`milp/`、`run_exp_*`)、结果 `experiments/results/`(`bb_results.csv`、`milp_results.csv`、`comparison_table.md`)。
- 新算法与分析:`new_algorithm/`(`prize_oracle`、`lagrangian_bb`、`root_lagrangian`)、
  `docs/Lagrangian_BB_Design.md`、`docs/Labeling_Feasibility_Assessment.md`、`docs/Pricing_Labeling_Connection.md`、
  `docs/BPC_Analysis_Yu_et_al.md`。
