# Yu et al. (INFORMS JoC 2026) Branch-Price-and-Cut 方法分析

> 对项目内 `INFORMS_Yuetal/` 代码的分析，以及"如何把本课题的一维 AM 并行机批调度问题
> 重构成 Branch-Price-and-Cut(BPC)"的对应关系说明。

---

## 1. 论文与代码概览

- **论文**：*An Exact Branch-Price-and-Cut Algorithm for the Unrelated Parallel Machine
  Scheduling Problem*，Yang Yu, Xiaolong Li, Roberto Baldacci, Zhiqiao Wu, Wei Sun,
  Jiafu Tang, Han Zhu。INFORMS Journal on Computing, 2026。DOI: 10.1287/ijoc.2024.0704。
- **问题**：非相关并行机调度(UPMSP),目标 **总加权完工时间(TWCT)**(数据文件名
  `m_n_TWCT_*.xlsx`,m=机器类、n=作业数)。机器"非相关"指同一作业在不同机器上的加工
  时间不同 `p_{k,j}`。
- **实现**:C#(.NET Framework 4.7.2),LP/MIP 求解器为 **IBM CPLEX 12.8**。
- **方法**:现代 **Branch-Price-and-Cut**,本质是 VRPSolver
  (Pessoa/Sadykov/Uchoa/Vanderbeck, 2020)那一派方法论搬到 UPMSP 上。
- **代码定位**(`src/UPMSP - Branch-Cut-and-Price Algorithm/`):
  - `AlgorithmLibrary.cs`(4192 行)—— 算法主体:列生成、标号定价、割、变量固定、
    枚举、分支;
  - `ObjectLibrary.cs`(1772 行)—— 数据结构(`PartialSchedule`/`BucketGraph`/
    `ForwardLabel`/`LmSRCOfVertex` 等);
  - `Program.cs` / `Parameters.cs` / `Switcher.cs` —— 驱动与开关;
  - `results/` 里的消融变体目录名直接暴露了部件:`No Buck. Graph`、`No DSUB`、
    `No Lm-SRCs`、`No Stro. Bran`、`No Vari. Fixi`、`No Sche. Enum`。

---

## 2. 方法骨架

### 2.1 主问题:集合划分(Dantzig–Wolfe 分解)

- 每一**列 = 一台机器上的一个完整调度**(`PartialSchedule`,一串有序作业)。
- 列的成本 = 该调度的 TWCT 贡献。
- 约束:每个作业被恰好覆盖一次(set-partitioning)+ 机器数量约束。
- 受限主问题(RMP,代码里 103 处 `RMP`)用 CPLEX 解 **LP 松弛**。
- **关键**:这个 Dantzig–Wolfe / 集合划分 LP 下界**远紧于任何解析下界**,逼近最优。
  这是整套方法能扩展到大规模的根本原因。

### 2.2 定价子问题:bucket-graph 标号算法

为每台机器找"**最小 reduced cost 的调度**",本质是一个**资源约束最短路**
(代码里 `Bucket` 771 处、`Label` 883 处)。加速手段(标准 VRPSolver 套件):

- **双向标号**:`ForwardLabel` + `BackwardLabel`,在中点 `ConcatenateLabels` 拼接;
- **bucket graph**:按完工时间/资源把状态分桶,桶内与跨桶做**支配剪枝**
  (`DominatedByLabelsInBucket`、`DominatedInCompWiseSmallerBuckets`);
- **DSUB = `DynamicShrinkBound`**:头顶点的动态收缩界 +
  `BackwardLabelAlgorithmWithCompletionBound`,用完工界提前砍标号
  (对应消融项 `No DSUB`)。

### 2.3 切割:limited-memory Subset-Row Cuts(lm-SRC)

- 在主问题上加 rank-1 子集行割(`ProduceLmSRCs`、`CalculateCoefficientOfLmSRC`、
  `ProceduceMemorySet`,代码里 `SRC` 427 处)。
- "有限记忆(limited-memory)"用来**控制割对定价支配规则的破坏**——这是 SOTA BPC 的
  标志部件(对应 `No Lm-SRCs`)。

### 2.4 三大加速

- **对偶稳定化**:Wentges/角度平滑(`CalculateAngle`、`AdjustAlphaValue`、
  `CalculateLagrangianRelaxationBound`),抑制列生成的对偶振荡。
- **reduced-cost 变量固定**:`VariableFixingByReducedCosts`、
  `ObtainImprovingArcs` / `ObtainNonImprovingArcs`,用 reduced cost 与 gap 把不可能进
  最优的弧/列直接删掉(对应 `No Vari. Fixi`)。
- **调度枚举(route/schedule enumeration)**:`RouteEnumeration` /
  `ForwardRouteEnumeration`,当 gap 足够小,把所有 reduced cost < gap 的调度全部枚举,
  转成一个集合划分 MIP 直接交给 CPLEX 收尾(对应 `No Sche. Enum`)。

### 2.5 分支:指派/邻接变量上的 strong branching

- 分支变量为 `x_kij`(作业 j 在机器 k 上紧跟 i)或 `q_ij`(邻接),带 strong branching
  选候选(`ObtainXkij/QijCandidateBranchVariables`、`UpdateCandidateBranchVariablesPhase_0`,
  对应 `No Stro. Bran`)。
- 分支通过**修改 bucket graph(删弧)**实现,保持定价子问题结构不变(robust branching)。

### 2.6 总流程

```
根节点：构造初始列 → 列生成(解 RMP → 定价加列 → 重复) → 加 lm-SRC 割 → 行列再生成
      → 对偶稳定化 + reduced-cost 变量固定
若 gap 小：调度枚举 → 集合划分 MIP 收尾
否则：strong branching(改 bucket graph)→ 子节点重复以上
```

---

## 3. 为什么它能上大规模

唯一也是最重要的原因:**集合划分 LP 下界非常紧**。组合型 B&B(像本课题现在这套)依赖
解析下界,而解析下界对批/顺序结构刻画很松(本课题实测覆盖率仅 13–20%);列生成则用
"列=完整单机调度"把这部分结构**精确地**放进 LP,下界逼近最优,于是分支树极小。代价是需要
LP 求解器 + 定价标号 + 割 + 稳定化这一整套重型机器。

---

## 4. 映射到本课题的一维 AM 并行机批调度问题

本课题与该论文都是"**并行机指派 + 每台机器内部排序**",差别在于本课题多了**成批(batching)**
且目标是**总延误(total tardiness)**、机器**同构**。BPC 框架可以自然迁移:

| BPC 部件 | 本课题对应 |
|---|---|
| 列 = 一台机器的调度 | **列 = 一台机器的一个完整批调度**(批的划分 + 批序) |
| 列成本 | 该单机批调度的总延误 |
| 主问题 = 对作业的集合划分 | **对零件的集合划分**(每个零件恰好属于一台机器的一个批) |
| 定价子问题 = 带对偶的单机最短路 | **带对偶价格的单机批调度**(本课题的 oracle Φ 改造版) |
| 机器约束 | 同构机器 → 用一个"机器使用数"凸性约束即可,天然消对称 |

**核心洞察**:本课题**已经有** oracle Φ(给定一组零件,解单机最优批调度)。在列生成里,只要把
它改成"**给定零件对偶价格 λ_j,找最小 reduced cost = (批调度延误 − Σ_{j∈列} λ_j) 的单机批调度**",
它就是定价子问题。也就是说,**oracle ≈ 定价器**,迁移路径是现成的。

### 4.1 重构步骤(若决定走 BPC)

1. **主问题**:变量 `y_s ∈ {0,1}` 表示是否选用单机批调度列 s;约束
   `Σ_{s: j∈s} y_s = 1 ∀ 零件 j`(集合划分)+ `Σ_s y_s ≤ M`(最多 M 台机器);
   目标 `min Σ_s cost(s) y_s`。解 LP 松弛得对偶 λ_j、μ。
2. **定价**:对"一台机器",求 reduced cost 最小的批调度
   `min cost(s) − Σ_{j∈s} λ_j − μ`。这是带利润(λ_j)的单机批调度,可用本课题 oracle 的
   分支定界改造(节点 reduced cost = 批延误 − 已含零件利润),或 DP/标号。
3. **下界**:RMP 的 LP 值(加割后更紧)即节点下界,替代现在的解析界。
4. **分支**:在"零件 j 是否与零件 i 同机/同批"或"零件 j 是否在机器使用计数"等指派量上分支,
   分支后在定价里禁止相应组合(robust)。
5. **(可选,进阶)** lm-SRC 割、reduced-cost 变量固定、列枚举收尾——按收益逐步加。

### 4.2 代价与建议

- **代价**:重型工程——需要 LP 求解器(Gurobi/CPLEX,你本机已有 Gurobi);需要写带对偶的
  定价器(可复用现有 oracle/DP);robust branching、割、稳定化是可选的进阶项。
- **当前路线对比**:你们现在纯组合 B&B(均衡优先 + 守门评估 + 紧化 oracle)已能在 ~12s 内
  把论文算例(15 件,M=1/2/3)证到全局最优,**对当前论文规模足够**。
- **何时上 BPC**:当目标是把规模推到**几十~上百个零件**,或想在方法学上对标该 INFORMS 论文
  时,BPC(列=单机批调度,定价=带对偶 oracle)是正确的重武器,而不是继续抠解析下界
  (实测 serial/全局界对本课题数据均无效)。

---

## 5. 一句话总结

Yu et al. 用的是 **VRPSolver 式的 Branch-Price-and-Cut**:集合划分主问题 + bucket-graph
双向标号定价 + lm-SRC 割 + 对偶稳定化 + reduced-cost 变量固定 + 调度枚举 + strong branching。
它强在 **LP 下界紧**。本课题若要冲大规模,可把"列=单机批调度、定价=带对偶的 oracle"做成 BPC;
若只需当前论文规模,现有组合 B&B 已经够用。
