# 单机 Total-Tardiness Oracle 的正确性 Bug 报告

## 一句话结论
`BranchBound.cpp` 中的单机分支定界（`branch_and_cut` / `compute_total_lower_bound`）
在计算批次顺序完工时间时，按 `std::unordered_map` 的**哈希顺序**遍历批次，而不是按
**批次编号（=加工先后顺序）**遍历。这会让下界失效，从而可能把真正最优的分支剪掉，
**返回一个偏大（次优）的“最优值”**。结果还依赖输入零件顺序与编译器，因此是不确定的。

## 触发位置（根因）
文件 `BranchBound.cpp`，函数 `compute_total_lower_bound(...)`，
计算“已分配部分”顺序完工时间的循环：

```cpp
// node.S 的类型是 std::unordered_map<int, std::vector<int>>  (批次编号 -> 零件列表)
double time_cursor = 0.0;
for (auto it = node.S.begin(); it != node.S.end(); ++it) {   // <-- 哈希顺序遍历！
    double vol = 0.0, mh = 0.0;
    for (int j : it->second) { vol += v[j]; mh = std::max(mh, h[j]); }
    double PT = ST[0] + VT[0]*vol + UT[0]*mh;
    for (int j : it->second) comp[j] = time_cursor + PT;       // 完工时间
    time_cursor += PT;                                         // 时间轴累加
}
```

批次是**顺序加工**的，批次编号 b=0,1,2,... 就是它们在时间轴上的先后位置。
`time_cursor` 必须**按批次编号升序**累加才能得到正确的完工时间。
但 `std::unordered_map` 的迭代顺序是**未定义**的、一般不是按 key 升序，
所以这里实际算的是“同一组批次、但被打乱成另一种加工顺序”的完工时间。

## 为什么会导致错误（而不仅仅是“顺序不同”）
1. **下界失效 → 剪掉最优解。** `compute_total_lower_bound` 既用于内部节点剪枝。
   按哈希顺序算出的“已分配延误”可能**高于**该节点按真实(编号)顺序的真实部分成本，
   于是这个被抬高的下界可能 ≥ UB，导致**包含真正最优解的分支被错误剪掉**。
   这是返回值偏大的根本原因。
2. **叶子目标值算错。** 叶节点（全部零件已分配）的目标值也由这个函数给出，
   它对应的是“被哈希打乱后的批次顺序”的延误，而不是分支所代表的那个顺序。

注意：被算出来的每个值都对应**某个真实可行排程**（同一组批次换个顺序仍然可行，
面积约束与顺序无关），所以返回值**始终 ≥ 真最优**，不会低于真最优。

## 最小复现
6 个零件、单机、台面 20×20，S=2.0, V=0.030864, U=0.7：

```
part:  l   w   h    v
 0:    3   6   32   90
 1:   10   6    8  150
 2:    7   3    3   30
 3:    5   4    7   62
 4:    4   5    5   72
 5:    8   3    4   50
due dates D = [8, 22, 31, 29, 28, 15]
```

- 独立穷举（枚举所有“有序可行批次序列”，不走该 oracle）得到的**真最优 = 41.8468**。
- 该 oracle 以输入顺序 `{0,1,2,3,4,5}` 调用 → 返回 **69.0245（错误，偏大）**。
- 该 oracle 以按 due date 排序的顺序 `{0,5,1,4,3,2}` 调用 → 返回 **41.8468（正确）**。

同一组数据、仅输入顺序不同，结果就不同 —— 印证了哈希顺序依赖。

## 修复建议（任选其一，逻辑不变）
1. **按批次编号升序遍历**（最小改动）：
   ```cpp
   std::vector<int> bids;
   for (auto& kv : node.S) bids.push_back(kv.first);
   std::sort(bids.begin(), bids.end());
   for (int b : bids) {
       const auto& batch = node.S.at(b);
       ... // 同原逻辑累加 time_cursor / comp
   }
   ```
2. 把 `Node::S` 的类型从 `std::unordered_map<int,...>` 换成 `std::map<int,...>`
   （有序），或换成按位置索引的 `std::vector<std::vector<int>>`。

> 验证：改为按批次编号升序遍历后，上面的例子在两种输入顺序下都返回 41.8468，
> 与独立穷举一致，结果不再依赖输入顺序。

## 次要提示（与本 bug 不同，但建议一并检查）
`branch_and_cut` 内部把 `part_areas`、`completion` 等数组**按零件 id 直接索引**，
并以 `parts.size()` 作为数组长度，隐含假设传入的零件集合是连续的 `{0,1,...,k-1}`。
如果以后用任意（非连续）的零件 id 子集调用该 oracle，会发生越界写。
若该函数只在“全体零件 0..n-1”上调用则不受影响。
