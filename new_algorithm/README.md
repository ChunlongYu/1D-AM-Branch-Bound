# 新算法开发区:Lagrangian 分解 B&B

目标:用 Lagrangian 松弛(= 集合划分 LP / 列生成界)替换现有解析下界,把可证最优规模
从 ~15 件推到 ~20–25 件。设计草案:`../docs/Lagrangian_BB_Design.md`。

## 进度

- [x] **第 1 步:prize-collecting 单机 oracle**(`prize_oracle.{h,cpp}`)
  求解子问题 `min_A [ Φ(A) − Σ_{j∈A} u_j ]`(A 含强制基集;自由件可纳可弃)。
  正确性 `test_prize.cpp` 对独立穷举 **24/24 通过**(含 mandatory)。

- [x] **第 2 步:根节点 Lagrangian 界 via 真 oracle**(`root_lagrangian.cpp`)
  `L(u)=Σu_j + M·c̄*(u)`,子问题用 prize oracle(不枚举全集)+ 次梯度。
  实测根界达最优(300 步):
  | 实例 | M=2 | M=3 | M=4 |
  |---|---|---|---|
  | 10part | 99.7% | 98.9% | 100% |
  | 12part | **100.0%** | **100.0%** | 99.9% |
  对比现有解析根界(38–88%)——Lagrangian 根界 ≈ 最优,验证了整条路线。

- [x] **第 3 步:加速 prize oracle**(已并入 `prize_oracle.cpp`)
  - 带奖励的并行下界(剩余件从 C 出发的最小完工延误 − 可收集奖励)替换原乐观界;
  - 支配规则(同已排集合,(C,G) 占优则剪);
  - 贪心初始上界。
  效果:10 件 M=2 单次调用 **300ms → 3.8ms(~80×)**;正确性仍 24/24。
  (12 件 M=2 ~61ms/次,与基础 oracle 同量级的指数墙;实际用早停只需数十步即到界。)

- [~] **第 4 步:完整 Lagrangian-B&B**(`lagrangian_bb.cpp`,自包含)
  节点下界 = 残量 Lagrangian(各机 `cand=S_m∪free`、`mandatory=S_m`,prize oracle 求 g_m);
  完整分配节点 LB = Σ_m Φ(S_m) = 真目标(叶子/内部统一);最优优先 + 规范开机分支;次梯度 warm-start。
  - **正确 + 节点数坍塌**:12part M=2 → obj=51.522(对),**24 节点**(基础 B&B 2351);M=3 → 29.32(对),170 节点。
  - **瓶颈(待解)**:每节点次梯度要 M×iters 次 prize-oracle 精确调用,n≥13、M=2 时单次贵;
    且纯最优优先(无 diving)上界收得慢。13part M=2 在 40s 内未证完(增量解 62.8,真 59.47)。

- [~] **第 5 步:提速(已做一轮)+ 诚实结论**
  已实现:① **去重相同机器子问题**(根/浅层 M 台空机器 → 1 次求解);② **多规则贪心初始上界**;
  ③ **次梯度收敛即停**。效果:12part M=2 11s→**6.4s**(24 节点);M=3 6.1s→**4.6s**(119 节点);均无回归、目标值对。
  **但 13part M=2 仍未跑完**(5 节点 / 42s,每节点 ~8s = 50 步 × 2 机 × 13件 prize 解 ~80ms)。
  - **关键结论(诚实)**:Lagrangian-B&B 节点数极少(5–24,基础版要数千),但**每个节点贵 ~1000×**
    (几十~上百次 prize 子问题精确求解 vs 基础版 1 次解析界)。在基础版本就很强的 **M=2 中等规模**,
    Lagrangian-B&B 目前 **wall-time 反而更慢**(13part M=2:基础版 3s,Lagrangian 版 >40s)。
    "界对、树小,但节点太贵";"便宜界代替精确子问题"不可行(会塌回单件界,见正文分析)。
  - **下个该试的方向(节点级"便宜后精确")**:每个节点先用基础版的廉价解析界 + 紧 UB 剪枝,
    **只在通过廉价界、且接近 UB 的"难节点"上才调昂贵的 Lagrangian 界**——把基础版的速度与
    Lagrangian 的紧界按需结合。配合子问题 labeling(2–10×)。
  - 备注:Lagrangian 的真正价值预计在**更大 n**(基础版节点爆炸更快),但子问题成本也指数增长,
    交叉点未在可解规模内出现。

- [x] **第 5 步补充:节点级"便宜后精确"(方案 A)+ 迭代步调优**
  每节点先算基础版廉价解析界(par/pos + 自由件),`≥UB` 直接剪;仅 `cheapLB≥γ·UB` 的难节点
  才调 Lagrangian;根节点始终 Lagrangian;叶子精确求 ΣΦ。再把次梯度步数从 60 降到 20。
  **效果(都证到最优、目标值对)**:
  | 算例 (M=2) | Lagrangian-B&B | 节点 | 基础 B&B |
  |---|---|---|---|
  | 12part | 4.75s | 42 | 0.70s |
  | 13part | 24.5s | 43 | 3.0s |
  | 15part | **超时** | — | 27.6s |
  - **最终诚实结论**:Lagrangian 节点界让**节点数极少**(几十,基础版数千),但**每节点的 Lagrangian
    成本(次梯度 × 指数级 prize 子问题)** 使 wall-time 在 M=2 区间**比基础 B&B 慢 5–8×**,且 15part M=2
    根界都算不动(prize 解 ~370ms/次)。瓶颈是**子问题成本**,且其无高效 labeling 结构(见
    `../docs/Labeling_Feasibility_Assessment.md`)。
  - **定位**:Lagrangian 的"界≈最优、树坍塌"已被证实,但**当前不是 wall-time 上更优的求解器**;
    要竞争需要本质上更快的 pricing(labeling/DP 受限于 tardiness 结构,提速有限),属高难度长期方向。
    **基础 B&B 仍是实用主力。** Lagrangian 路线的主要价值:强对偶界(可作 gap 证书 / 论文中的界对比)、
    以及通往 Branch-and-Price 的认识(见 `../docs/Pricing_Labeling_Connection.md`)。

## 文件

- `prize_oracle.{h,cpp}` —— 子问题求解器(带奖励并行界 + 支配规则 + 贪心 UB,独立于 `../src/`)。
- `test_prize.cpp` —— 独立穷举对拍。
- `root_lagrangian.cpp` —— 根节点 Lagrangian 界 + 次梯度驱动(复用 `../src/InstanceData` 读实例)。
  编译/运行:
  ```
  g++ -std=c++17 -O2 -I../src -o rootlag root_lagrangian.cpp prize_oracle.cpp ../src/InstanceData.cpp
  ./rootlag 12part 300        # 实例  迭代步数
  ```
