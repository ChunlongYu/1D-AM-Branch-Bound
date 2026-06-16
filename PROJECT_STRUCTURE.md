# 项目结构说明

一维增材制造(AM)**同构并行机**批调度的精确算法工程,目标:最小化总延误(total tardiness)。
上层并行机分支定界 + 下层单机 oracle(精确批调度)。

## 目录布局

```
1D-AM-Branch-Bound/
├── main.cpp                    # 程序入口:读实例 → 调并行 B&B → 输出
├── ParallelBranchBound.h/.cpp  # 【核心】上层并行机分支定界(本课题主算法)
├── BranchBound.h/.cpp          # 下层单机 oracle Φ(精确单机批调度 B&B)
├── InstanceData.h/.cpp         # 实例文件读取
├── test_parallel.cpp           # 独立穷举回归测试(校验最优性,不依赖 oracle)
├── Branch&Bound.sln/.vcxproj   # Visual Studio 工程
├── Instance/                   # 测试算例(5~15 件)
├── PythonCodes/
│   ├── CompLowerBounds/         # 下界对比实验(Python)
│   └── MILP/milp_gurobi.py      # 论文 MILP 模型(gurobipy),用于和 B&B 对比
├── Documents/                  # 论文与分析文档
│   ├── 1D-AM_Parallel/          # 并行机论文 LaTeX
│   ├── NodeGenerationNew/       # 节点生成相关文档
│   ├── LowerBounds/             # 下界相关
│   ├── ORACLE_BUG_REPORT.md     # 给原 oracle 作者的 bug 报告(批次顺序 bug)
│   └── BPC_Analysis_Yu_et_al.md # INFORMS Branch-Price-and-Cut 论文分析
└── References/                 # 外部参考代码(非本工程构建)
    ├── INFORMS_Yuetal/          # Yu et al. UPMSP Branch-Price-and-Cut(C#/CPLEX)
    └── Zixuan_codes/            # 单机 oracle 的另一份实现(含 DP serial 界、新分支)
```

## 核心算法(ParallelBranchBound.cpp)

上层在"零件→机器"指派上做分支定界,叶节点(完整指派)分解成 M 个独立单机子问题,
由 oracle Φ 精确求解。已实现的关键技术:

- **对称消除分支**(规范开机顺序)+ strong-branching 选件;
- **节点下界** = 各机器 `max(记忆池界, 并行界, position界)` + 未分配零件松弛;
- **均衡优先 DFS**:节点选择按"各机器件数的最大值"优先最均衡 → 快速收紧上界;
- **增量守门叶子评估**:机器按集合从小到大逐台精确求 Φ,混合界 ≥ UB 即剪枝,
  跳过偏斜叶子上昂贵的大集合 oracle 调用;
- oracle 结果记忆池缓存 + DFS/BFS 自适应节点选择。

## 下层 oracle(BranchBound.cpp)

单机批调度精确 B&B,按论文附录实现:逐件/子集分支、**支配规则**、自适应节点选择、
并行界 + position 界。**已修复**一处批次顺序 bug(原按 unordered_map 哈希序遍历,
详见 `Documents/ORACLE_BUG_REPORT.md`)。

## 构建与运行

Visual Studio:打开 `Branch&Bound.sln`,Release/x64 生成。

g++:
```
g++ -std=c++17 -O2 -o bb_par main.cpp BranchBound.cpp InstanceData.cpp ParallelBranchBound.cpp
./bb_par
```
机器数 M、实例、截止时间在 `main.cpp` 顶部设置。

回归测试:
```
g++ -std=c++17 -O2 -o tp test_parallel.cpp BranchBound.cpp InstanceData.cpp ParallelBranchBound.cpp
./tp     # 应输出 "18/18 cases passed"
```

## 性能(已实测,证到全局最优)

| 算例 | M=1 | M=2 | M=3 |
|---|---|---|---|
| 13 件 | ~5 s | ~1.2 s | <1 s |
| 14 件 | ~13 s | ~2.9 s | — |
| 15 件 | ~10.5 s | ~11.9 s | ~11.9 s |

对比 Gurobi MILP:13 件 M=2,MILP 55 s vs B&B 1.2 s(目标值一致 59.47,交叉验证)。

## 实例文件格式(含截止时间)

截止时间不再硬编码在 `main.cpp`,而是写在每个实例 `.txt` 文件**末尾**:用一行 `DueDate`
标记,后接 n 个截止时间(对应零件 1..n,可分行或同一行)。例:

```
15 1 1 103.68
8.0 8.0 2.0 0.0

DueDate
15 43 11 23 14 38 35 37 48 31 20 13 38 8 31
```

- `InstanceData.cpp` 读完所有零件后,若遇到 `DueDate` 标记则读取 n 个截止时间到
  `PartLists::due_dates`;没有该段则 `due_dates` 为空。
- `main.cpp` 直接使用 `part_lists.due_dates`;若文件缺该段或数量不符,报错退出。
- **所有实例都已含 `DueDate` 段**:`5/10/11/12/13/14/15part.txt` 用的是原 `main.cpp` 里的
  既有截止时间;`10part_2/_3`、`10parts_4`、`15part_2-S`、`20part_3-S/_4-S` 按论文方法
  **新生成**:`d_j ~ Unif[Cmax(1-TF-RDD/2), Cmax(1-TF+RDD/2)]`,其中 Cmax 由单机首次适应
  (按投影面积非增排序)估计,参数 **TF=0.6, RDD=0.6, seed=1**(与既有实例口径一致,
  生成脚本逻辑见 `PythonCodes/CompLowerBounds/InstanceData.py` 的 `GenerateDueDate`)。
