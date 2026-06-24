# Yu2022 派生集实验

对 `Instances/Derived_Yu2022_identical/` 的 **24 个算例**(20 个 n=10–30 + 4 个
`ht2_1-50`)× **M∈{2,3,4}** = **72 个实验**,每例 **3600s**,跑固化 `src/pbb`
(默认配置:depth 下潜、LS、HEAVY 重载机器界全开)。

## 跑法

```bash
# 1) 先编译 pbb
cd ../../src && g++ -std=c++17 -O2 -o pbb main.cpp ParallelBranchBound.cpp BranchBound.cpp InstanceData.cpp
# 2) 全量(72 个实验,挂着跑;可中断后 --resume 续)
cd ../experiments/yu2022 && python run_yu2022.py
# 先小时限验证流程:
python run_yu2022.py --tl 60
# 只跑某 M / 断点续传 / HEAVY 消融:
python run_yu2022.py --M 3
python run_yu2022.py --resume
python run_yu2022.py --heavy 0      # 关重载界做对照
```

## 产出(写到 `runs/`)

- **每个实验一个 `<instance>_M<M>.txt`**:头部(实例/配置/日期)+ pbb 完整输出
  ——机器/零件信息、最佳指派、搜索统计、**每 10s 的 `TRACE t ub lb gap nodes`
  上下界轨迹**、以及最终 `RESULT ... TT=.. optimal=.. lb=.. gap=.. nodes=.. oracle=..`。
- **`master_results.csv`**:一行一个实验,列 = instance,n,M,obj,proven,lb,gap%,time,nodes,oracle,heavy。
- **`summary.md`**:汇总表 + 可证最优计数。

## 记录的指标

`obj / proven / time / nodes / gap` 固定有;`lb`(已证全局下界)+ `gap=(obj−lb)/obj`
由新加的全局下界给出;**UB/LB 随时间收敛**由 `TRACE` 行(默认每 10s,`--traceint` 可调)给出。

## 备注

- 初始构造(贪心 + 局搜)是循环前的一次性开销;对 n 大 / M 小(单机零件多、Φ 贵)
  的实例,头几秒可能全在构造,`TRACE` 从主循环开始记录。
- 取的是源实例**较大平台**机器,故部分松交期(TF=0.3)实例最优总延误可能为 0。
- 估算:72 个实验最坏 ~72h;多数小算例秒级结束,真正吃满 3600s 的是 n≥20、M=2 的少数。
  建议挂后台分批 + `--resume`。
