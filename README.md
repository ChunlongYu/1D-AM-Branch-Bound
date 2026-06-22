# 1D-AM Parallel-Machine Branch-and-Bound

一维(面积约束)增材制造**同构平行机批调度**的精确算法,目标:最小化总延误(total tardiness)。
上层在"零件→机器"指派上做分支定界,叶节点分解为 M 个独立单机子问题,由精确单机 oracle 求解。

## 目录结构

```
1D-AM-Branch-Bound/
├── src/                  # C++ 源码(本文 B&B 方法)
│   ├── BranchBound.{h,cpp}          # 单机 oracle Φ(精确单机批调度 B&B)
│   ├── ParallelBranchBound.{h,cpp}  # 上层平行机分支定界(主算法)
│   ├── InstanceData.{h,cpp}         # 实例读取(含末尾 DueDate 段)
│   ├── main.cpp                     # 主程序:./bb [实例] [M] [时限]
│   ├── run_exp_BB.cpp               # 批量实验(遍历实例 × M)
│   └── test_parallel.cpp            # 独立穷举回归测试
├── data/                 # 算例(*.txt,末尾含 DueDate 段)
├── docs/                 # 论文与分析文档
│   ├── 1D-AM_Parallel/              # 论文 LaTeX
│   ├── Lagrangian_BB_Design.md      # 新算法(Lagrangian-B&B)设计草案
│   ├── BPC_Analysis_Yu_et_al.md     # INFORMS Branch-Price-and-Cut 分析
│   ├── ORACLE_BUG_REPORT.md         # 原 oracle 批次顺序 bug 报告
│   └── README_CN.md                 # 单机版旧说明
├── experiments/          # 实验脚本与结果
│   ├── milp/                        # milp_gurobi.py, run_exp_milp.py(Gurobi 对比)
│   ├── lower_bounds/                # 下界对比实验(Python)
│   └── results/                     # bb_results.csv, milp_results.csv, comparison_table.md
├── references/           # 外部参考代码(非本工程构建)
│   ├── INFORMS_Yuetal/              # Yu et al. UPMSP Branch-Price-and-Cut(C#/CPLEX)
│   └── Zixuan_codes/                # 单机 oracle 另一实现(含 DP serial 界、新分支)
├── new_algorithm/        # 新算法(Lagrangian-B&B)开发区(见 docs 设计草案)
├── Branch&Bound.sln / .vcxproj / .filters   # Visual Studio 工程(引用 src/)
└── README.md
```

## 构建与运行

**Visual Studio**:打开 `Branch&Bound.sln`,Release/x64 生成(工程已指向 `src/`)。
运行目录为工程根目录,故 `data/...` 路径可直接解析。

**g++(在仓库根目录运行)**:
```bash
# 主程序
g++ -std=c++17 -O2 -o bb src/main.cpp src/BranchBound.cpp src/InstanceData.cpp src/ParallelBranchBound.cpp
./bb data/15part.txt 2 1800          # 实例  机器数M  时限(秒);缺省 data/15part.txt 2 1800

# 批量实验(遍历 data/ 全部实例 × M={2,3,4});结果写 experiments/results/bb_results.csv
g++ -std=c++17 -O2 -o run_exp_BB src/run_exp_BB.cpp src/BranchBound.cpp src/InstanceData.cpp src/ParallelBranchBound.cpp
./run_exp_BB

# 回归测试(应输出 "18/18 cases passed")
g++ -std=c++17 -O2 -o tp src/test_parallel.cpp src/BranchBound.cpp src/InstanceData.cpp src/ParallelBranchBound.cpp
./tp
```
> 注:`run_exp_BB.cpp` / `test_parallel.cpp` 各自带 `main()`,不要和 `main.cpp` 一起编译。

**MILP 对比(需本机 Gurobi)**:
```bash
cd experiments/milp
python run_exp_milp.py               # 写 experiments/results/milp_results.csv
python milp_gurobi.py ../../data/15part.txt 2 1800   # 单实例
```

## 实例文件格式

末尾用一行 `DueDate` 标记 + n 个截止时间(详见 `docs/` 内说明)。`data/` 下全部实例均已含该段。

## 现状(基础版)

- 正确性:B&B 与 Gurobi MILP 在所有可证最优算例上目标值完全一致。
- 规模:M=2、n≤15 时 B&B 比 MILP 快 1–1.5 个数量级;n=20 当前解不动。
- 下一步:Lagrangian 分解下界(根界实测可达最优的 ~99–100%),见 `docs/Lagrangian_BB_Design.md`,
  开发放在 `new_algorithm/`。
