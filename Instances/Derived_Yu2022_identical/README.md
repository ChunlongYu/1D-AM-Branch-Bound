# Derived instances from Yu et al. (2022) — identical parallel machines

派生自 `Instances/Yu et al., 2022/TestInstances/`,用于本工作的**同构(identical)
平行机** 1D-AM 批调度(min 总延误)。

## 派生规则

Yu et al. (2022) 考虑的是**异构**平行机(每个实例给出多台不同平台的机器)。本工作
是同构机,故:

- **取每个源实例里平台面积 `L×W` 最大的那台机器**,作为**所有 M 台同构机**的规格
  (其 `V, U, S, L, W, H` 全部沿用)。
- **零件全部保留不变**:取**orientation 1**(文件所列第一个朝向)的 `l, w, h`;
  体积只用**零件体积**(忽略支撑 support);按类型 multiplicities 展开成单个零件。
- **交期(due dates)原样保留**。
- 机器数 **M ∈ {2,3,4}** 由运行时传入(`./pbb <inst> <M> <TL>`),不写进文件。

## 格式

沿用本仓库 `data/` 的简化字段格式(单机型 + N 个单朝向零件 + `DueDate` 行),
可被 `src/pbb`、`new_oracle/oracle`、`experiments/milp/milp_gurobi.py` 直接读取:

```
1 N
1 N

1 1 V U S L W H          # 较大机器(同构机规格)

j 1 1 vol                # 第 j 个零件(orientation 1)
l w h support
...
DueDate
d_1 ... d_N
```

## 清单(20 个,n ∈ {10,15,20,25,30})

文件名沿用源:`htX_Y-N_TF_RDD_seed.txt`,N=零件数,TF/RDD=交期紧度/范围,seed=随机种子。

| 组 | n | 较大机器 (L×W, U, S) | 个数 |
|---|---|---|---|
| ht1_1-10 | 10 | 60×40, U=0.16, S=1 | 4 |
| ht1_2-15 | 15 | 40×40, U=0.14, S=1 | 4 |
| ht1_3-20 | 20 | 80×40, U=0.25, S=1 | 4 |
| ht2_2-25 | 25 | 60×40, U=0.16, S=1 | 4 |
| ht2_1-30 | 30 | 80×40, U=0.25, S=1 | 4 |

## 复现

`python derive_yu.py`(脚本内的 SRC/DST 路径按本仓库设置)。

---

## LargerInstances 派生(n=50)

源 `Instances/Yu et al., 2022/LargerInstances/`(ht2_1/2/3,各 n=50)**不含交期**。
交期用 Yu 自己的 `GenerateDueDate`(从其 `InstanceData.py` import,精确复制其
makespan 估计 + `random.seed` + `randint`),按**四个 (TF,RDD) 组合**生成,种子沿用
TestInstances 约定(TF=0.3→seed 1,TF=0.6→seed 3):

| TF | RDD | seed |
|---|---|---|
| 0.3 | 0.3 | 1 |
| 0.3 | 0.6 | 1 |
| 0.6 | 0.3 | 3 |
| 0.6 | 0.6 | 3 |

> 注:makespan 估计沿用 Yu 的"最大高度朝向 + FFD on 原异构 2 机"(仅用于定 Cmax),
> 与实例实际用的 orientation-1 无关——这点与 Yu 生成 TestInstances 交期的口径一致。

共 **12 个**:`ht2_{1,2,3}-50_{TF}_{RDD}_{seed}.txt`(较大机器:ht2_1/ht2_3→80×40,
ht2_2→60×40)。复现:`python derive_larger.py`。

> ⚠️ 因取的是**较大平台**,n=50 实例容量充裕:TF=0.3(松交期)下不少实例最优总延误
> 可能为 0(无延误);M=2 时每台机器 ~25 件、单机 Φ 基本不可解。这些大算例主要用于
> **规模/极限演示**,主力实验仍是 n=10–30 的 TestInstances 派生集。
