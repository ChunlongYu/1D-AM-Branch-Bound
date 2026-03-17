# 1D-AM-Branch-Bound 项目说明文档

## 目录

1. [项目背景与目标](#1-项目背景与目标)
2. [项目文件结构](#2-项目文件结构)
3. [问题模型描述](#3-问题模型描述)
4. [数据结构详解](#4-数据结构详解)
5. [算法逻辑详解](#5-算法逻辑详解)
6. [函数调用关系](#6-函数调用关系)
7. [实例数据格式](#7-实例数据格式)
8. [Python辅助代码说明](#8-python辅助代码说明)
9. [编译与运行](#9-编译与运行)
10. [开发扩展建议](#10-开发扩展建议)

---

## 1. 项目背景与目标

本项目针对**一维增材制造（1D AM）批调度问题**，求解最小化零件总延迟（Total Tardiness）的最优批次排序方案。

### 实际场景

在金属增材制造（如 SLM/SLS）中，若干零件需要在一台打印机上分批（Batch）打印。每个批次内可并行打印多个零件，受机器打印台面积限制。每个批次完成后才能开始下一批次。每个零件有各自的交货期（Due Date），目标是安排打印顺序与分组，使所有零件的完工延迟总和最小。

### 算法框架

- **分支定界法（Branch and Bound）** 精确求解最优解
- 以贪心法生成**初始可行上界（UB）**
- 通过**下界（LB）计算**和**剪枝策略**高效压缩搜索空间

---

## 2. 项目文件结构

```
1D-AM-Branch-Bound/
├── main.cpp                  # 程序入口：读取实例、调用算法、输出结果
├── BranchBound.h             # 算法头文件：节点类、算法函数声明
├── BranchBound.cpp           # 算法实现：初始解、子节点生成、下界、B&B主循环
├── InstanceData.h            # 数据头文件：机器与零件结构体声明
├── InstanceData.cpp          # 数据读取：从文本文件解析机器与零件信息
├── Instance/                 # 实例数据目录
│   ├── 5part.txt             # 5个零件的测试实例
│   ├── 10part.txt            # 10个零件的测试实例
│   ├── ...
│   └── 15part.txt            # 15个零件的测试实例（main.cpp默认使用）
├── PythonCodes/
│   └── CompLowerBounds/
│       ├── InstanceData.py   # Python版实例数据读取与截止时间生成工具
│       └── experiment_lb_compare.py  # 下界质量对比实验脚本
├── Branch&Bound.sln          # Visual Studio 解决方案文件
├── Branch&Bound.vcxproj      # Visual Studio 项目配置文件
└── logs/                     # 运行日志目录（运行时自动创建）
```

---

## 3. 问题模型描述

### 3.1 问题参数

| 符号 | 含义 |
|------|------|
| $n$ | 零件总数 |
| $L \times W$ | 机器打印台面积（长 × 宽） |
| $l_j \times w_j$ | 零件 $j$ 的底面积（长 × 宽） |
| $h_j$ | 零件 $j$ 的高度（影响铺粉时间） |
| $v_j$ | 零件 $j$ 的体积（影响扫描时间） |
| $d_j$ | 零件 $j$ 的交货期（Due Date） |
| $S$ | 每批次固定准备时间（Setup Time） |
| $V$ | 体积系数（Scanning Speed，单位体积扫描时间） |
| $U$ | 高度系数（Recoater Speed，单位高度铺粉时间） |

### 3.2 批次加工时间

对于一个批次 $b$，其加工时间（Processing Time）为：

$$PT_b = S + V \cdot \sum_{j \in b} v_j + U \cdot \max_{j \in b} h_j$$

其中：
- $S$：固定准备时间（激光预热、气体充填等）
- $V \cdot \sum v_j$：激光扫描时间，正比于批次内零件体积之和
- $U \cdot \max h_j$：铺粉时间，正比于批次内最高零件的高度

### 3.3 完工时间与目标函数

设 $K$ 个批次按顺序 $b_1, b_2, \ldots, b_K$ 依次打印，第 $k$ 个批次的完工时间：

$$C_{b_k} = \sum_{i=1}^{k} PT_{b_i}$$

批次内所有零件的完工时间相同。零件 $j$（属于第 $k$ 批次）的**延迟**：

$$T_j = \max(0,\ C_{b_k} - d_j)$$

**目标：** 最小化总延迟

$$\min \sum_{j=1}^{n} T_j$$

### 3.4 约束条件

- 每个批次内零件底面积之和不超过机器台面面积：$\sum_{j \in b} l_j \cdot w_j \leq L \cdot W$
- 每个零件恰好属于一个批次

---

## 4. 数据结构详解

### 4.1 `MachineInfo`（InstanceData.h）

描述打印机的物理参数：

```cpp
struct MachineInfo {
    int id;               // 机器编号
    int num;              // 机器数量（当前版本只处理单机）
    double scanning_speed; // 体积系数 V（激光扫描速度相关）
    double recoater_speed; // 高度系数 U（铺粉速度相关）
    double setup_time;    // 固定准备时间 S
    double length;        // 打印台长度 L
    double width;         // 打印台宽度 W
    double height;        // 打印台最大高度（当前未直接参与计算）
};
```

### 4.2 `PartInfo`（InstanceData.h）

描述单个零件的属性：

```cpp
struct PartInfo {
    int id;        // 零件编号
    int num;       // 数量（通常为1）
    double volume; // 体积 v_j
    double length; // 底面长 l_j
    double width;  // 底面宽 w_j
    double height; // 高度 h_j
    double support;// 支撑材料体积（当前实现中未直接用于加工时间计算）
};
```

### 4.3 `PartLists`（InstanceData.h）

将所有零件的同类属性拆分为独立向量，便于向量化计算：

```cpp
struct PartLists {
    std::vector<double> volumes;  // 各零件体积
    std::vector<double> lengths;  // 各零件底面长
    std::vector<double> widths;   // 各零件底面宽
    std::vector<double> heights;  // 各零件高度
    std::vector<double> supports; // 各零件支撑体积
};
```

### 4.4 `Node`（BranchBound.h）

分支定界树中的搜索节点，代表一个**部分批次分配方案**：

```cpp
class Node {
public:
    std::unordered_map<int, std::vector<int>> S; // 批次编号 -> 零件编号列表
    double LB;      // 当前节点的下界值（已分配批次的延迟 + 未分配零件的并行下界）
    std::string name; // 节点名称（用于调试追踪，格式如 "Root_0_2_1"）
};
```

`S` 中的键为批次序号（从 0 开始递增），值为该批次包含的零件编号列表。**节点的 `S` 只包含已确定安排的批次，未分配零件尚未出现在 `S` 中。**

### 4.5 `BatchMap`（BranchBound.h）

```cpp
typedef std::map<int, std::set<int>> BatchMap;
```

初始解生成函数的返回类型，键为批次序号，值为该批次内的零件编号集合（有序）。

### 4.6 `Stats`（BranchBound.h）

搜索统计信息结构体：

```cpp
struct Stats {
    int updated_solutions;    // 更新最优解的次数
    int total_nodes;          // 实际处理（弹出）的节点总数
    int generated_nodes;      // 生成的子节点总数
    int area_pruned_nodes;    // 面积约束剪枝次数（含在 generate_children 内）
    int LB_pruned_nodes;      // 下界剪枝次数（LB ≥ UB）
    int U_pruned_nodes;       // 不可行批次剪枝次数
    int leaf_nodes;           // 叶子节点数（完整分配方案数）
};
```

---

## 5. 算法逻辑详解

### 5.1 `readMachineAndParts`（InstanceData.cpp）

**功能：** 从文本文件读取机器与零件数据。

**处理流程：**
1. 读取头部：机器类型数、零件类型数、机器数量、零件数量
2. 跳过两行空行/注释行
3. 读取机器参数（单台机器模式）
4. 循环读取每种零件类型的参数：编号、数量、方向数、体积、尺寸（长宽高）、支撑体积
5. 将所有零件属性同时填入 `parts` 列表和 `part_lists` 各向量

**注意：** 当前实现仅使用零件的第一个方向（orientation 0），忽略多方向情况。

---

### 5.2 `generateInitialSolution`（BranchBound.cpp）

**功能：** 生成初始可行解，作为分支定界的初始上界（UB）。

**算法：EDD（Earliest Due Date）贪心分批**

```
1. 按截止时间 d_j 升序排列零件（EDD规则）
2. 贪心分批：顺序扫描排列后的零件，若当前零件面积加入批次不超过机器面积，则加入；
   否则关闭当前批次，新开批次
3. 顺序计算每个批次的加工时间 PT = S + V*Σv_j + U*max(h_j)
4. 累加得各零件完工时间，计算总延迟
```

**返回：** `std::pair<BatchMap, double>`——初始批次方案及其总延迟。

---

### 5.3 `compute_total_lower_bound`（BranchBound.cpp）

**功能：** 计算节点的下界（Lower Bound），用于剪枝判断。

**两部分之和：**

#### 已分配部分延迟（精确值）
- 按 `node.S` 中批次顺序（map键自然排序）逐批计算加工时间
- 累计时间轴 `time_cursor`
- 已分配零件的完工时间 = `time_cursor + PT`
- 计算延迟 `max(0, C_j - d_j)` 并累加

#### 未分配部分延迟（并行下界）
- 假设每个未分配零件**单独**构成一个批次（最乐观估计）
- 每个未分配零件 $p$ 的加工时间：$pt_p = S + V \cdot v_p + U \cdot h_p$
- 完工时间：$c_p = \text{time\_cursor} + pt_p$（从当前时间轴出发）
- 延迟：$\max(0, c_p - d_p)$

> **注：** 这是一个**乐观**的下界，因为实际中多个未分配零件会被合并到同一批次（增加加工时间），而这里假设每个零件单批并行执行，给出下界估计。

**返回：** 已分配延迟 + 未分配并行下界之和。

---

### 5.4 `generate_children`（BranchBound.cpp）

**功能：** 对当前节点展开，生成所有可行子节点。

**逻辑：**

1. 从 `node.S` 中提取**已分配零件**集合
2. 计算**未分配零件**列表 `unassigned`
3. 枚举未分配零件的所有**非空子集**（位掩码枚举，$2^n - 1$ 个子集）
4. 对每个子集检查**面积约束**（若面积累加超过机器面积则跳过）
5. 合法子集构成新批次，加入 `node.S` 的副本，形成子节点
6. 子节点名称格式：`父节点名_子节点序号`

**时间复杂度：** $O(2^{|\text{unassigned}|})$，随未分配零件数指数增长，因此本算法仅适用于规模较小（≤20个零件）的实例。

**注意：** 当前版本为全量枚举（不含批次顺序优化），保留了一段注释掉的旧版位掩码实现。

---

### 5.5 `branch_and_cut`（BranchBound.cpp）

**功能：** 主搜索函数，实现深度优先分支定界搜索。

**初始化：**
- 计算每个零件的面积 `part_areas`
- 构造根节点（空 `S`，LB = 初始下界）
- 将根节点压入栈（`std::deque` 用作栈，后进先出）
- 从 `generateInitialSolution` 的结果获取初始 UB

**主循环（深度优先搜索）：**

```
while 栈非空:
    弹出栈顶节点 cur

    1. 时间限制检查：超过 time_limit_seconds 则退出
    
    2. 不可行剪枝（U-pruned）：
       若 cur.S 中任一批次包含 initial_infeasible 中的某个不可行集合，剪掉
    
    3. LB剪枝：
       若 cur.LB >= UB，剪掉（不可能改善当前最优解）
    
    4. 叶子节点判断：
       若所有零件均已分配（|S中零件总数| == n），
       则更新最优解（若 cur.LB < UB）
    
    5. 展开子节点：
       调用 generate_children 生成所有子节点
       为每个子节点计算 LB
       若 LB < UB 则压入栈（否则 LB-pruned）
```

**返回：** `std::pair<Node, Stats>`——最优节点及搜索统计信息。

---

### 5.6 日志与输出函数（BranchBound.cpp）

| 函数 | 功能 |
|------|------|
| `get_log_filename(input_filename)` | 根据输入文件名生成日志文件名（避免覆盖，自动递增编号），日志存入 `logs/` 目录 |
| `write_utf8_bom(stream)` | 向输出流写入 UTF-8 BOM（`EF BB BF`），确保中文日志文件编码正确 |
| `log_and_cout(msg)` | 模板函数，同时向标准输出和日志文件输出消息 |

全局变量 `log_stream`（`std::ofstream`）在整个程序运行期间保持打开状态。

---

## 6. 函数调用关系

### 6.1 总体调用关系图

```
main()
├── readMachineAndParts()          # 读取机器和零件数据
├── generateInitialSolution()      # 贪心法生成初始解（UB）
│   └── [内部: EDD排序 + 贪心分批 + 延迟计算]
├── branch_and_cut()               # 分支定界主函数
│   ├── compute_total_lower_bound()  # 根节点下界计算
│   └── [循环中]:
│       ├── [不可行剪枝]
│       ├── [LB剪枝]
│       ├── [叶子节点处理]
│       └── generate_children()    # 子节点展开
│           └── compute_total_lower_bound()  # 子节点下界计算
├── log_and_cout()                 # 输出日志
├── get_log_filename()             # 获取日志文件名
└── write_utf8_bom()               # 写入BOM头
```

### 6.2 详细调用时序

```
main()
  │
  ├─► readMachineAndParts(filename, machine, parts, part_lists)
  │     └─► 文件I/O解析
  │
  ├─► generateInitialSolution(part_indices, L, W, l, w, v, h, due_dates, ST, VT, UT)
  │     返回 (BatchMap, total_tardiness)  → UB = total_tardiness
  │
  └─► branch_and_cut(parts, D, infeasible_batches, ST, VT, UT, L, W, l, w, h, v, init_S, UB, time_limit, path)
        │
        ├─► compute_total_lower_bound(root, ...)  # 根节点LB
        │
        └─► [主循环]
              ├─► generate_children(cur, parts, machine_area, part_areas)
              │     返回 vector<Node>
              │
              └─► compute_total_lower_bound(child, ...)  # 每个子节点的LB
```

---

## 7. 实例数据格式

实例文件为纯文本格式，位于 `Instance/` 目录，以 `15part.txt` 为例：

```
1 15          # 机器类型数 零件类型数
1 15          # 机器数量  零件数量
              # 空行
1 1 0.030864 0.7 2.0 20.0 20.0 32.0
# 格式：机器ID  数量  扫描速度V  铺粉速度U  准备时间S  长L  宽W  高HM
              # 空行
1 1 1 90.0    # 零件ID  数量  方向数  体积v
3.0 6.0 32.0 0.0  # 长l  宽w  高h  支撑体积s（方向0）

2 1 1 150.0
10.0 6.0 8.0 0.0
...
```

**各字段含义：**

- 第1行：机器类型数 `types_machine`，零件类型数 `types_parts`
- 第2行：机器总数 `num_machine`，零件总数 `num_parts`
- 机器段（每类机器一行）：编号、数量、$V$（扫描速度）、$U$（铺粉速度）、$S$（准备时间）、$L$（长）、$W$（宽）、$HM$（最大高度）
- 零件段（每种零件占两行）：
  - 行1：零件编号、数量、方向数、体积 $v_j$
  - 行2：$l_j$（长）、$w_j$（宽）、$h_j$（高）、$s_j$（支撑体积）

**截止时间说明：** 截止时间 `due_dates` 当前在 `main.cpp` 中**手动硬编码**，每个实例对应一组注释掉的截止时间向量，实验时手动切换。

---

## 8. Python辅助代码说明

位于 `PythonCodes/CompLowerBounds/` 目录，用于实验分析。

### 8.1 `InstanceData.py`

提供与 C++ 端对应的 Python 版数据读取功能：

- `readInstance(path)`：读取实例文件，返回机器参数（$V, U, S, L, W, HM$）和零件参数（$v, h, l, w, s, Kj$）。
- `GenerateDueDate(path, TF, RDD, RndSeed)`：基于实例自动生成截止时间
  - `TF`（Tightness Factor）：松紧程度，越大截止时间越紧
  - `RDD`（Range of Due Dates）：截止时间分散程度
  - `RndSeed`：随机种子，保证可重现
- `Instance` 类：面向对象封装，调用 `load()` 和 `GenerateDD()` 方法

### 8.2 `experiment_lb_compare.py`

下界质量对比实验，定义了两种下界：

#### `lb_par`（并行下界）
与 C++ 中 `compute_total_lower_bound` 的未分配部分相同：
- 每个未分配零件假设单独成批执行
- 完工时间 = 当前时间 + 单件加工时间

#### `lb_pos`（位置下界，论文方法）
更紧的下界：
- 按面积升序排列未分配零件，前缀和计算批次数 $\beta_k$
- 综合考虑体积累积和高度累积，推导每个零件的完工时间下界
- 与截止时间按序配对（二者均排序），得到更紧的总延迟下界

实验通过对比两种下界的质量验证 `lb_pos` 的改进效果。

---

## 9. 编译与运行

### 9.1 使用 Visual Studio

1. 打开 `Branch&Bound.sln`
2. 选择 Release 或 Debug 配置
3. 生成项目（Ctrl+Shift+B）
4. 运行（F5 或 Ctrl+F5）

### 9.2 使用 g++（命令行）

```bash
g++ -std=c++17 -O2 -o bb main.cpp BranchBound.cpp InstanceData.cpp
./bb
```

### 9.3 配置实例与截止时间

在 `main.cpp` 中修改：

```cpp
// 修改实例文件路径（默认为 15part.txt，以下示例切换为 10part.txt）
std::string filename = "Instance/10part.txt";

// 修改对应截止时间（取消注释对应行）
std::vector<double> due_dates = { 9, 23, 32, 30, 29, 7, 13, 8, 20, 29 }; // 10part

// 修改时间限制（秒）
double time_limit = 1800.0;
```

### 9.4 输出说明

程序会在控制台和 `logs/` 目录下的日志文件中同时输出：
- 机器和零件信息摘要
- 初始解总延迟
- 搜索过程统计（节点数、剪枝次数等）
- 最优批次方案
- 运行时间

---

## 10. 开发扩展建议

### 10.1 更紧的下界

当前下界（并行下界）相对较松，可引入 `experiment_lb_compare.py` 中验证的**位置下界（lb_pos）**到 C++ 实现中，以减少搜索节点数、加速求解。

### 10.2 截止时间自动加载

当前截止时间在 `main.cpp` 中硬编码。建议将截止时间写入实例文件最后一行（参考 `15part_2-S.txt` 格式），并在 `readMachineAndParts` 中统一读取。

### 10.3 多机扩展

当前代码仅支持单台机器（所有参数均取向量下标 `[0]`）。若需扩展到多机场景，需修改调度逻辑（工作负载分配、机器选择）。

### 10.4 搜索策略优化

当前采用深度优先搜索（DFS，栈实现）。可尝试：
- **最佳优先搜索（Best-First）**：优先展开 LB 最小的节点，改用优先队列
- **宽度优先搜索（BFS）**：改用队列（`deque` 的 `front()`）

### 10.5 子节点生成优化

当前枚举所有 $2^n - 1$ 个子集，随零件数指数增长。针对较大规模实例，可考虑：
- 增加启发式剪枝（如基于截止时间的批次组合优先策略）
- 限制每次展开的子集大小上限
- 引入对称性破除规则（避免等价排列）

### 10.6 支撑材料的影响

当前实现中，支撑材料体积（`support`）被读取但未用于加工时间计算。若需考虑支撑材料的扫描时间贡献，可将加工时间公式调整为：

$$PT_b = S + V \cdot \sum_{j \in b} (v_j + s_j) + U \cdot \max_{j \in b} h_j$$

---

## 附录：关键参数命名对照表

| C++ 变量 | Python 变量 | 含义 |
|----------|-------------|------|
| `machine.scanning_speed` / `VT[0]` | `V[i]` | 体积系数（扫描速度） |
| `machine.recoater_speed` / `UT[0]` | `U[i]` | 高度系数（铺粉速度） |
| `machine.setup_time` / `ST[0]` | `S[i]` | 批次固定准备时间 |
| `machine.length` / `L[0]` | `L[i]` | 机器台面长度 |
| `machine.width` / `W[0]` | `W[i]` | 机器台面宽度 |
| `part_lists.volumes[j]` / `v[j]` | `v[j]` | 零件体积 |
| `part_lists.heights[j]` / `h[j]` | `h[j,0]` | 零件高度 |
| `part_lists.lengths[j]` / `l[j]` | `l[j,0]` | 零件底面长 |
| `part_lists.widths[j]` / `w[j]` | `w[j,0]` | 零件底面宽 |
| `due_dates[j]` / `D[j]` | `d[j]` | 零件截止时间 |
| `node.S` | — | 部分批次分配方案（批次ID → 零件列表） |
| `node.LB` | — | 当前节点下界值 |
| `UB` | — | 当前已知最优解上界值 |
