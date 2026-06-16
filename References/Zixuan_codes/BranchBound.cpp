#include "BranchBound.h"
#include "DataRecord.h"
#include <algorithm>
#include <chrono>
#include <limits>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <string>
#include <functional>
#include <iomanip>
#include "DynamicProgramming.h"

//=========================生成初始解（无任何调试输出）=========================
std::pair<BatchMap, double> generateInitialSolution(
    const std::vector<int>& parts,
    const std::vector<double>& L,
    const std::vector<double>& W,
    const std::vector<double>& l,
    const std::vector<double>& w,
    const std::vector<double>& v,
    const std::vector<double>& h,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT
) {
    std::vector<int> sorted_parts = parts;
    std::sort(sorted_parts.begin(), sorted_parts.end(),
        [&D](int a, int b) { return D[a] < D[b]; });

    double machine_area = L[0] * W[0];
    BatchMap batches;
    std::vector<int> current_batch;
    current_batch.reserve(parts.size());
    double current_batch_area = 0.0;
    int batch_id = 0;

    for (std::size_t i = 0; i < sorted_parts.size(); ++i) {
        int p = sorted_parts[i];
        double area = l[p] * w[p];
        if (current_batch_area + area <= machine_area) {
            current_batch.push_back(p);
            current_batch_area += area;
        }
        else {
            batches[batch_id++] = std::set<int>(current_batch.begin(), current_batch.end());
            current_batch.clear();
            current_batch.push_back(p);
            current_batch_area = area;
        }
    }
    if (!current_batch.empty()) {
        batches[batch_id] = std::set<int>(current_batch.begin(), current_batch.end());
    }

    std::vector<double> completion(parts.size(), 0.0);
    double time_cursor = 0.0;
    double total_tardiness = 0.0;

    for (typename BatchMap::const_iterator it = batches.begin(); it != batches.end(); ++it) {
        double vol = 0.0, mh = 0.0;
        for (typename std::set<int>::const_iterator jt = it->second.begin(); jt != it->second.end(); ++jt) {
            vol += v[*jt];
            if (h[*jt] > mh) mh = h[*jt];
        }
        double PT = ST[0] + VT[0] * vol + UT[0] * mh;
        for (typename std::set<int>::const_iterator jt = it->second.begin(); jt != it->second.end(); ++jt) {
            completion[*jt] = time_cursor + PT;
        }
        time_cursor += PT;
    }

    for (std::size_t i = 0; i < parts.size(); ++i) {
        total_tardiness += std::max(0.0, completion[parts[i]] - D[parts[i]]);
    }

    return std::make_pair(batches, total_tardiness);
}

//===========================Node 定义===============================
Node::Node()
    : LB(0.0), completion_time(0.0), total_tardiness(0.0), name("N"), depth(0) {
}

Node::Node(const std::unordered_map<int, std::vector<int>>& S_,
    double LB_,
    const std::string& name_,
    double completion_time_,
    double total_tardiness_,
    int depth_)
    : S(S_), LB(LB_), name(name_),
    completion_time(completion_time_), total_tardiness(total_tardiness_), depth(depth_) {
}

bool Node::operator==(const Node& other) const {
    return S == other.S;
}

std::size_t Node::Hash::operator()(const Node& node) const {
    std::size_t seed = 0;
    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
        it != node.S.end(); ++it) {
        std::size_t h1 = std::hash<int>()(it->first);
        for (std::size_t i = 0; i < it->second.size(); ++i) {
            h1 ^= std::hash<int>()(it->second[i])
                + 0x9e3779b9 + (h1 << 6) + (h1 >> 2);
        }
        seed ^= h1 + 0x9e3779b9 + (seed << 6) + (seed >> 2);
    }
    return seed;
}

std::ostream& operator<<(std::ostream& os, const Node& node) {
    os << "Node(" << node.name << "):\n";
    os << "  LB = " << node.LB << "\n";
    os << "  S = {\n";
    for (const auto& pair : node.S) {
        os << "    Batch " << pair.first << ": [";
        for (size_t i = 0; i < pair.second.size(); ++i) {
            os << pair.second[i];
            if (i != pair.second.size() - 1)
                os << ", ";
        }
        os << "]\n";
    }
    os << "  }";
    return os;
}

//=======================子节点生成（Type I & Type II）========================
// 本函数实现 Azizoglu & Webster (2000) 的增量式分支策略：
// 在已固定若干批次（B_1, ..., B_r，按时间先后排列）的部分调度上，
// 通过两类“添加动作”逐个把未排零件接入调度，从而枚举出所有可行调度：
//   - Type I 添加：把一个未排零件放入一个【全新批次】 B_{r+1}（等价于“封口”当前批次 B_r）；
//   - Type II 添加：把一个未排零件放入【当前最后一个批次】 B_r。
//
// 与文献的对应关系（已针对本文 AM / 总延误 模型做了模型相关的取舍，详见论文方法节）：
//   * Type II 仅保留两条与目标函数无关、纯结构性的过滤条件：
//       (v)  加入后不得超出平台容量 L×W（容量可行性）；
//       (vi) 仅当新零件下标大于当前批次内所有零件下标时才允许加入（对称性消除，避免重复枚举同一集合）。
//   * 文献中针对 Type I 的支配过滤条件 (i)-(iv) 基于“批加工时间 = 批内最大 p_j”及
//     “按 p/w 升序排批次”等性质，仅对 总加权完成时间 目标成立；
//     在本文 P_b = S + V·Σv_j + U·max h_j 的 M-batch + 总延误 模型下这些性质不成立，
//     故此处【不施加】(i)-(iv)，对每个未排零件无条件生成其 Type I 子节点，
//     其作用由状态支配规则（Su 相同时比较 (TT, C)）在子节点评估阶段替代承担。
ChildGenerationResult generate_children(
    const Node& node,
    const std::vector<int>& parts,
    double machine_area,
    const std::vector<double>& part_areas
) {
    std::unordered_set<int> assigned;
    int max_batch_id = -1;                 // 当前最后一个批次 B_r 的下标（根节点为 -1）
    double current_batch_area = 0.0;       // 当前批次 B_r 已占用的投影面积 a(B_r)
    int max_pid_in_current_batch = -1;     // 当前批次 B_r 内零件的最大下标（用于条件 vi）

    // 1. 解析父节点状态：找出已排零件集合、定位当前最后批次 B_r 及其属性
    for (const auto& kv : node.S) {
        max_batch_id = std::max(max_batch_id, kv.first);
        for (int pid : kv.second) {
            assigned.insert(pid);
        }
    }
    if (max_batch_id >= 0) {
        for (int pid : node.S.at(max_batch_id)) {
            current_batch_area += part_areas[pid];
            max_pid_in_current_batch = std::max(max_pid_in_current_batch, pid);
        }
    }

    // 2. 按零件原始下标顺序筛选未排零件（固定枚举顺序，配合条件 vi 消除对称）
    std::vector<int> unassigned;
    for (int p : parts) {
        if (assigned.find(p) == assigned.end()) {
            unassigned.push_back(p);
        }
    }
    if (unassigned.empty()) return { {}, 0 };

    std::vector<Node> children;
    children.reserve(unassigned.size() * 2);
    int pruned_count = 0;
    int child_index = 0;

    // ============================================================
    // 第一步（Phase 1）：生成 Type I 子节点 —— 为每个未排零件开一个新批次
    // 文献：根节点处即由此步生成 n 个“首批次只含单个零件”的子节点；
    //       一般节点处则对应“封口当前批次、另起新批次”。本模型不施加 (i)-(iv)。
    // ============================================================
    for (int pid : unassigned) {
        auto S_type1 = node.S;
        S_type1[max_batch_id + 1] = { pid };   // 开新批次 B_{r+1}，仅含 pid
        std::string name1 = node.name + "_T1_" + std::to_string(child_index++);

        children.emplace_back(
            std::move(S_type1),
            0.0,                 // LB 由调用方稍后计算
            name1,
            0.0,                 // completion_time 由 update_node_metrics 统一重算
            0.0,                 // total_tardiness 同上
            node.depth + 1
        );
    }

    // ============================================================
    // 第二步（Phase 2）：生成 Type II 子节点 —— 把未排零件并入当前批次 B_r
    // 仅保留满足 (v) 容量可行 与 (vi) 下标递增（对称性消除）的子节点。
    // 根节点（max_batch_id < 0）没有“当前批次”，故此步跳过。
    // ============================================================
    if (max_batch_id >= 0) {
        for (int pid : unassigned) {
            // 条件 (v)：容量可行性 —— a(B_r) + area(pid) ≤ L×W
            bool area_ok = (current_batch_area + part_areas[pid] <= machine_area);
            // 条件 (vi)：对称性 —— 仅允许把下标更大的零件并入当前批次
            bool index_ok = (pid > max_pid_in_current_batch);

            if (area_ok && index_ok) {
                auto S_type2 = node.S;
                S_type2[max_batch_id].push_back(pid);   // 并入当前批次 B_r
                std::string name2 = node.name + "_T2_" + std::to_string(child_index++);

                children.emplace_back(
                    std::move(S_type2),
                    0.0,
                    name2,
                    0.0,
                    0.0,
                    node.depth + 1
                );
            }
            else if (!area_ok) {
                ++pruned_count;   // 记录因容量约束 (v) 被剪掉的 Type II 候选数量
            }
        }
    }

    return { children, pruned_count };
}

// [删除]：移除了原有的 compute_completion_times 函数
// [删除]：移除了原有的 compute_assigned_tardiness 函数

//====================== [新增] 更新节点的全局时间与延迟状态 =============================
void update_node_metrics(
    Node& node,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::vector<double>& D
) {
    double current_time = 0.0;
    double total_tardiness = 0.0;

    int max_batch_id = -1;
    for (const auto& kv : node.S) {
        max_batch_id = std::max(max_batch_id, kv.first);
    }

    // 按批次 ID 顺序严格累加加工时间，确保时序正确
    for (int i = 0; i <= max_batch_id; ++i) {
        if (node.S.find(i) == node.S.end()) continue;

        const auto& batch = node.S.at(i);
        double vol = 0.0, mh = 0.0;

        // 提取当前批次的总体积和最大高度
        for (int pid : batch) {
            vol += v[pid];
            mh = std::max(mh, h[pid]);
        }

        // 计算当前批次的加工时间 (PT = 准备时间 + 体积相关时间 + 高度相关时间)
        double PT = ST[0] + VT[0] * vol + UT[0] * mh;
        current_time += PT;

        // 累加当前批次所有零件的延迟
        for (int pid : batch) {
            total_tardiness += std::max(0.0, current_time - D[pid]);
        }
    }

    // 固化节点的最终状态
    node.completion_time = current_time;
    node.total_tardiness = total_tardiness;
}

//=======================未分配零件总延迟下界估计====================
//初始LB的计算，假设未分配零件在并行批次上进行
double compute_unassigned_lower_bound(
    const Node& node,
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v
) {
    // 找出已分配的零件
    std::unordered_set<int> assigned;
    for (const auto& [_, part_ids] : node.S) {
        for (int pid : part_ids) {
            assigned.insert(pid);
        }
    }

    // 初始化未分配部分的延迟估计
    double unassigned_tardiness = 0.0;

    for (int p : parts) {
        if (assigned.find(p) == assigned.end()) {
            // 对每个未分配零件，估算加工时间并独立批次处理
            double pt = ST[0] + VT[0] * v[p] + UT[0] * h[p];
            double c = node.completion_time + pt;  // 假设从当前时间并行开始
            unassigned_tardiness += std::max(0.0, c - D[p]);
        }
    }

    // 返回当前延迟 + 未来估计
    return node.total_tardiness + unassigned_tardiness;
}

//不使用DP算法的串行计算
double compute_unassigned_lower_bound3(
    const Node& node,
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v
) {
    // 找出已分配的零件
    std::unordered_set<int> assigned;
    for (const auto& [_, part_ids] : node.S) {
        for (int pid : part_ids) {
            assigned.insert(pid);
        }
    }

    // 找出未分配零件的最小高度
    double min_height = std::numeric_limits<double>::max();
    std::vector<int> unassigned_parts;
    for (int p : parts) {
        if (assigned.find(p) == assigned.end()) {
            unassigned_parts.push_back(p);
            min_height = std::min(min_height, h[p]); // 更新最小高度
        }
    }

    // 初始化未分配部分的延迟估计
    double unassigned_tardiness = 0.0;
    double vol_accumulated = 0.0;
    for (int p : parts) {
        if (assigned.find(p) == assigned.end()) {
            vol_accumulated += v[p];

            ////    // 计算该零件的加工时间
            double processing_time = ST[0] + VT[0] * vol_accumulated + UT[0] * min_height;
            // 对每个未分配零件，估算加工时间并独立批次处理
            double c = node.completion_time + processing_time;  // 假设从当前时间并行开始
            unassigned_tardiness += std::max(0.0, c - D[p]);
        }
    }

    // 返回当前延迟 + 未来估计
    return node.total_tardiness + unassigned_tardiness;
}

//提出的更加收敛的LB的计算：未分配零件串行计算
double compute_unassigned_lower_bound2(
    const Node& node,
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::vector<double>& individual_part_processing_times
) {
    // 找出已分配的零件
    std::unordered_set<int> assigned;
    for (const auto& [_, part_ids] : node.S) {
        for (int pid : part_ids) {
            assigned.insert(pid);
        }
    }

    // 找出未分配零件的最小高度
    double min_height = std::numeric_limits<double>::max();
    std::vector<int> unassigned_parts;
    for (int p : parts) {
        if (assigned.find(p) == assigned.end()) {
            unassigned_parts.push_back(p);
            min_height = std::min(min_height, h[p]); // 更新最小高度
        }
    }
    //===========================引入动态规划算法获得未分配零件的最优序列============================
    std::vector<Job> dp_jobs;
    std::vector<Job> dp_jobs_for_solver;
    for (int pid : unassigned_parts) {
        dp_jobs_for_solver.push_back(Job{  // 显式调用构造函数，或者可以省略 Job
            pid,                 // id
            v[pid] * VT[0],      // p (近似：只考虑体积相关的处理时间)
            D[pid]               // d
            });
    }
    double dp_initial_time = node.completion_time + ST[0] + UT[0] * min_height;
    std::vector<int> optimal_sequence_result;
    double min_tardiness = minimize_total_tardiness(dp_jobs_for_solver, dp_initial_time, optimal_sequence_result);


    //// 初始化延迟估计
    double unassigned_tardiness = 0.0;
    double completion_time_future = node.completion_time;
    double vol_accumulated = 0.0; // 当前已处理部分体积


    //// 假设从当前位置开始串行处理未分配的零件
    for (int p : optimal_sequence_result) {
        ////    // 累加当前零件的体积
        vol_accumulated += v[p];

        ////    // 计算该零件的加工时间
        double processing_time = ST[0] + VT[0] * vol_accumulated + UT[0] * min_height;
        double final_processing_time = std::max(processing_time, individual_part_processing_times[p]);

        double completion_time = completion_time_future + final_processing_time;
        unassigned_tardiness += std::max(0.0, completion_time - D[p]);
    }

    // 返回当前延迟 + 估计的未分配延迟下界
    return node.total_tardiness + min_tardiness;
}



//========================Branch and Bound（无任何调试输出）========================
std::pair<Node, Stats> branch_and_cut(
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<std::set<int> >& initial_infeasible,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& L,
    const std::vector<double>& W,
    const std::vector<double>& l,
    const std::vector<double>& w,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::unordered_map<int, std::vector<int> >& initial_S,
    double UB,
    double time_limit_seconds,
    const std::string& path
) {
    Stats stats;

    reset_dp_memo_stats();
    clear_global_dp_cache(); // 【新增】清空上一轮实验留下的哈希表

    // ----------------- 新增：支配规则映射表 -----------------
    // Key: 已分配的零件集合（排序后的 vector）
    // Value: 帕累托前沿列表（保存不同的 {TT, C} 组合）
    std::unordered_map<std::vector<int>, std::vector<StateMetric>, VectorHash> dominance_map;
    // -------------------------------------------------------


    double machine_area = L[0] * W[0];

    std::vector<double> individual_processing_times(parts.size());

    // 遍历 parts 列表的索引
    for (std::size_t i = 0; i < parts.size(); ++i) {
        int current_part_id = parts[i];
        individual_processing_times[i] = ST[0] + VT[0] * v[current_part_id] + UT[0] * h[current_part_id];
    }

    // ========= 新增：根据已分配/未分配数量选择 LB 的小函数 =========
    auto compute_node_LB = [&](const Node& nd) -> double {
        // 统计已分配零件数量
        std::size_t assigned_cnt = 0;
        for (const auto& kv : nd.S) {
            assigned_cnt += kv.second.size();
        }
        int unassigned_cnt = static_cast<int>(parts.size() - assigned_cnt);

        // 阈值：最多允许多少未分配零件时才用 DP 下界
        // 可以根据问题规模调，比如 8~12
        const int MAX_UNASSIGNED_FOR_DP = 10;

        if (unassigned_cnt <= MAX_UNASSIGNED_FOR_DP) {
            // 未分配数量很少，用便宜的简单下界v
            return compute_unassigned_lower_bound(nd, parts, D, ST, VT, UT, h, v);
        }
        else {
            // 未分配数量很多，用更精确的 DP 下界
            return compute_unassigned_lower_bound2(nd, parts, D, ST, VT, UT, h, v, individual_processing_times);
        }
        };

    std::vector<double> part_areas(parts.size(), 0.0);
    for (std::size_t i = 0; i < parts.size(); ++i) {
        part_areas[parts[i]] = l[parts[i]] * w[parts[i]];
    }

    auto all_assigned = [&](const Node& nd) -> bool {
        std::size_t cnt = 0;
        for (const auto& it : nd.S) {
            cnt += it.second.size();
        }
        return cnt == parts.size();
        };

    Node best(initial_S, 0.0, "Best", 0.0, 0.0, 0);
    Node root({}, 0.0, "Root", 0.0, 0.0, 0);
    root.LB = compute_unassigned_lower_bound(root, parts, D, ST, VT, UT, h, v);

    std::deque<Node> stack;
    stack.push_back(root);

    auto t0 = std::chrono::steady_clock::now();
    if (UB > 0 && UB < std::numeric_limits<double>::infinity()) {
        stats.UB_updates.emplace_back(0.0, UB);
        stats.LB_convergence.emplace_back(0.0, root.LB);
    }

    // ========== 新增：自适应出栈策略控制 ==========
    int max_capa = 5000;
    int min_capa = 2000;
    bool use_best_first = true;
    static constexpr double epsilon = 1e-10;
    static constexpr double dom_epsilon = 1e-6; // 支配比较的容差


    while (!stack.empty()) {
        auto t1 = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(t1 - t0).count();
        if (time_limit_seconds > 0.0 && elapsed > time_limit_seconds) break;

        // === 实时记录当前最小 LB（用于收敛曲线） ===
        if (!stack.empty()) {
            double timestamp = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
            double min_LB = std::numeric_limits<double>::infinity();
            for (const Node& nd : stack) {
                if (nd.LB < min_LB) min_LB = nd.LB;
            }
            if (min_LB >= UB) min_LB = UB;
            if (min_LB >= 0.0 && min_LB < std::numeric_limits<double>::infinity()) {
                if (stats.LB_convergence.empty() || std::abs(min_LB - stats.LB_convergence.back().second) > epsilon) {
                    stats.LB_convergence.emplace_back(timestamp, min_LB);
                }
            }
        }

        // === 动态选择出栈策略 ===
        if (stack.size() > static_cast<std::size_t>(max_capa)) {
            use_best_first = false;
        }
        else if (stack.size() < static_cast<std::size_t>(min_capa)) {
            use_best_first = true;
        }

        Node cur;
        if (use_best_first) {
            auto best_it = std::min_element(stack.begin(), stack.end(),
                [](const Node& a, const Node& b) {
                    return a.LB < b.LB;
                });
            cur = *best_it;
            stack.erase(best_it);
        }
        else {
            cur = stack.back();
            stack.pop_back();
        }

        ++stats.total_nodes;

        // 不可行剪枝
        bool bad = false;
        for (const auto& it : cur.S) {
            for (const auto& infeasible_set : initial_infeasible) {
                if (std::includes(it.second.begin(), it.second.end(),
                    infeasible_set.begin(), infeasible_set.end())) {
                    bad = true;
                    break;
                }
            }
            if (bad) break;
        }
        if (bad) {
            ++stats.U_pruned_nodes;
            ++stats.pruned_nodes_per_depth[cur.depth];
            continue;
        }

        // 下界剪枝
        if (cur.LB >= UB) {
            ++stats.LB_pruned_nodes;
            ++stats.pruned_nodes_per_depth[cur.depth];
            continue;
        }

        // 叶子节点
        if (all_assigned(cur)) {
            ++stats.leaf_nodes;
            if (cur.LB < UB) {
                UB = cur.LB;
                best = cur;
                ++stats.updated_solutions;
                double timestamp = std::chrono::duration<double>(t1 - t0).count();
                stats.UB_updates.emplace_back(timestamp, UB);
            }
            continue;
        }

        // 展开子节点
        // 只有当当前节点是根节点 (depth == 0) 时，才记录其子节点的名称和 LB
        bool is_root_node = (cur.depth == 0);
        auto [kids, pruned] = generate_children(cur, parts, machine_area, part_areas);
        stats.generated_nodes += kids.size();
        stats.area_pruned_nodes += pruned;

        for (auto& child : kids) {
            // [修改]：移除旧的两次增量计算，统一调用 update_node_metrics 重算完成时间和总延迟
            update_node_metrics(child, ST, VT, UT, h, v, D);

            // ====================== 支配规则检查开始 ======================

//// 1. 构建键值：已分配的零件集合（排序后）
//            std::vector<int> assigned_key;
//            assigned_key.reserve(parts.size());
//            for (const auto& kv : child.S) {
//                assigned_key.insert(assigned_key.end(), kv.second.begin(), kv.second.end());
//            }
//            std::sort(assigned_key.begin(), assigned_key.end());
//
//            // 2. 检查是否被支配
//            auto& pareto_front = dominance_map[assigned_key];
//            bool is_dominated = false;
//
//            for (const auto& metric : pareto_front) {
//                // 如果历史记录中存在 TT 和 C 都比当前节点小（或相等）的状态
//                if (metric.tt <= child.total_tardiness + dom_epsilon &&
//                    metric.c <= child.completion_time + dom_epsilon) {
//                    is_dominated = true;
//                    break;
//                }
//            }
//
//            if (is_dominated) {
//                // 当前节点被支配，剪枝（不放入栈中）
//                // 此时也可以统计到 pruned_nodes 中，这里复用 U_pruned_nodes 或 LB_pruned_nodes
//                ++stats.U_pruned_nodes;
//                ++stats.pruned_nodes_per_depth[child.depth];
//                continue;
//            }
//
//            // 3. 更新支配表（维护帕累托前沿）
//            // 如果当前节点没有被支配，则将其添加到前沿中，并移除那些被当前节点支配的历史状态
//            // 这样可以保持 vector 大小最小化
//            auto it = pareto_front.begin();
//            while (it != pareto_front.end()) {
//                // 如果当前节点比历史节点更优（TT更小且C更小），则删除历史节点
//                if (child.total_tardiness <= it->tt + dom_epsilon &&
//                    child.completion_time <= it->c + dom_epsilon) {
//                    it = pareto_front.erase(it);
//                }
//                else {
//                    ++it;
//                }
//            }
//            pareto_front.push_back({ child.total_tardiness, child.completion_time });

            // ====================== 支配规则检查结束 ======================

            //===============================下界计算=======================
                //-------------------------------1.串行下界------
            //child.LB = compute_unassigned_lower_bound2(child, parts, D, ST, VT, UT, h, v, individual_processing_times);
             //child.LB = compute_unassigned_lower_bound3(child, parts, D, ST, VT, UT, h, v);
                //-------------------------------2.并行下界----------------------------------------------------
            child.LB = compute_unassigned_lower_bound(child, parts, D, ST, VT, UT, h, v);
            //-------------------------------3.串并行比较-----------------------------------------------
        //double LB_parallel = compute_unassigned_lower_bound(child, parts, D, ST, VT, UT, h, v);
        //double LB_serial = compute_unassigned_lower_bound2(child, parts, D, ST, VT, UT, h, v, individual_processing_times);
        //if (LB_serial <= LB_parallel) {
        //   child.LB = LB_parallel;
        //}
        //else {
        //    child.LB = LB_serial;
        //}

        //-----------------------------------4. delta下界控制-------------------------------------

        //// 1. 先计算并行下界 (Cheap)
        //double lb_parallel = compute_unassigned_lower_bound(child, parts, D, ST, VT, UT, h, v);

        //// 2. 如果并行下界已经超过UB，直接剪枝
        //if (lb_parallel >= UB) {
        //    ++stats.LB_pruned_nodes;
        //    ++stats.pruned_nodes_per_depth[child.depth];
        //    continue;
        //}

        //child.LB = lb_parallel; // 暂时赋值为并行LB

        //// 3. 计算 Delta 判断是否需要启用精确下界
        //// 确保 UB 不为0防止除零风险 (虽然 UB=0 时前面 lb>=UB 大概率已剪枝)
        //if (UB > 1e-9) {
        //    double delta = (UB - lb_parallel) / UB;

        //    // 4. 如果差距 <= 5%，启用精确下界 (Expensive Serial LB via DP)
        //    if (delta <= 0.5) {

        //        ++stats.delta_trigger_count;
        //        // 记录Delta启用次数
        //        double lb_serial = compute_unassigned_lower_bound2(child, parts, D, ST, VT, UT, h, v, individual_processing_times);

        //        // 取两者的最大值作为最终 LB (理论上 Serial >= Parallel)
        //        if (lb_serial > lb_parallel) {
        //            child.LB = lb_serial;
        //        }
        //        else {
        //            ++stats.serial_missing_count;
        //        }

        //        // 5. 再次剪枝判断
        //        if (child.LB >= UB) {
        //            ++stats.serial_pruning_count;

        //            ++stats.LB_pruned_nodes;
        //            ++stats.pruned_nodes_per_depth[child.depth];
        //            continue;
        //            }
        //        }
        //    }

        //----------------------------------------5. delta下界控制与未分配比例策略-----------------------------

        //// 1. 先计算并行下界 (Cheap / Fast LB)
        //double lb_parallel = compute_unassigned_lower_bound(child, parts, D, ST, VT, UT, h, v);

        //// 2. 如果并行下界已经超过UB，直接剪枝
        //if (lb_parallel >= UB) {
        //    ++stats.LB_pruned_nodes;
        //    ++stats.pruned_nodes_per_depth[child.depth];
        //    continue;
        //}

        //child.LB = lb_parallel; // 暂时默认赋值为并行LB

        //// 3. 开始多级判断
        //if (UB > 1e-9) {
        //    double delta = (UB - lb_parallel) / UB;

        //    // 【优化点】第一层过滤：先看 Delta 是否足够小 (<= 10%)
        //    // 如果 delta 很大（说明当前解离 UB 很远），直接跳过后续所有计算，保留 parallel LB 即可
        //    if (delta <= 0.1) {

        //        // 【优化点】第二层过滤：只有通过了 Delta 检查，才计算未分配零件比例
        //        std::size_t child_assigned_count = 0;
        //        for (const auto& kv : child.S) {
        //            child_assigned_count += kv.second.size();
        //        }

        //        int child_unassigned_count = static_cast<int>(parts.size() - child_assigned_count);
        //        double unassigned_ratio = static_cast<double>(child_unassigned_count) / parts.size();

        //        // 【优化点】第三层过滤：未分配零件 > 60% 才启用昂贵的 DP 下界
        //        if ( unassigned_ratio  > 0.6) {

        //            ++stats.delta_trigger_count; // 记录昂贵下界的触发次数

        //            // 启用精确下界 (Expensive Serial LB via DP)
        //            double lb_serial = compute_unassigned_lower_bound2(child, parts, D, ST, VT, UT, h, v, individual_processing_times);

        //            // 取两者的最大值作为最终 LB
        //            if (lb_serial > lb_parallel) {
        //                child.LB = lb_serial;
        //            }
        //            else {
        //                ++stats.serial_missing_count;
        //            }

        //            // 再次剪枝判断（因为 LB 变大了，可能现在能剪掉了）
        //            if (child.LB >= UB) {
        //                ++stats.serial_pruning_count;
        //                ++stats.LB_pruned_nodes;
        //                ++stats.pruned_nodes_per_depth[child.depth];
        //                continue;
        //            }
        //        }
        //    }
        //}



        // ----------------- [修改开始] -----------------
         // 记录第一层子节点的详细信息
            if (is_root_node) {
                // 1. 找出已分配的零件 (第一层子节点肯定只有 1 个 batch)
                std::unordered_set<int> assigned_set;
                for (const auto& kv : child.S) {
                    for (int pid : kv.second) {
                        assigned_set.insert(pid);
                    }
                }

                // 2. 计算未分配的零件
                std::vector<int> unassigned_parts_list;
                unassigned_parts_list.reserve(parts.size() - assigned_set.size());
                for (int p : parts) {
                    if (assigned_set.find(p) == assigned_set.end()) {
                        unassigned_parts_list.push_back(p);
                    }
                }

                // 3. 存入 Stats (内存中快速存储)
                stats.first_level_details.push_back({
                    child.name,
                    child.LB,
                    child.completion_time,
                    static_cast<int>(unassigned_parts_list.size()),
                    std::move(unassigned_parts_list) // 使用 move 避免拷贝
                    });

                // 保留旧的记录以便兼容（如果不需要可以删除）
                stats.first_level_node_lbs.emplace_back(child.name, child.LB);
            }
            // ----------------- [修改结束] -----------------




            if (child.LB < UB) {
                stack.push_back(std::move(child));
            }
            else {
                ++stats.LB_pruned_nodes;
                ++stats.pruned_nodes_per_depth[child.depth];
            }
        }
    }

    stats.total_V_calls = dp_memo_stats.total_V_calls;       // V() 被调用的总次数
    stats.local_memo_hits = dp_memo_stats.local_memo_hits;     // 命中本次调用的 memo（SubsetKey）的次数
    stats.global_memo_hits = dp_memo_stats.global_memo_hits;    // 命中全局 global_memo 的次数（跨调用复用）
    stats.computed_states = dp_memo_stats.computed_states;     // 真正需要计算的新状态数（没命中任何缓存）

    return std::make_pair(best, stats);
}

//========================分支过程追踪（教学/调试用）========================
namespace {

// 把节点的批次构成格式化为可读字符串，例如：[B0:{0,2} | B1:{1}]
std::string batches_to_string(const Node& node) {
    if (node.S.empty()) return "[空 / 根节点]";

    std::vector<int> ids;
    ids.reserve(node.S.size());
    for (const auto& kv : node.S) ids.push_back(kv.first);
    std::sort(ids.begin(), ids.end());

    std::string s = "[";
    for (std::size_t i = 0; i < ids.size(); ++i) {
        std::vector<int> b = node.S.at(ids[i]);
        std::sort(b.begin(), b.end());
        s += "B" + std::to_string(ids[i]) + ":{";
        for (std::size_t j = 0; j < b.size(); ++j) {
            s += std::to_string(b[j]);
            if (j + 1 < b.size()) s += ",";
        }
        s += "}";
        if (i + 1 < ids.size()) s += " | ";
    }
    s += "]";
    return s;
}

// 对比父子节点，判断本次添加是 Type I（开新批次）还是 Type II（并入当前批次），
// 并指出加入的是哪个零件，返回类似 "Type I: 新批次 B1 装入零件 3" 的描述。
std::string describe_child(const Node& parent, const Node& child) {
    std::unordered_set<int> parent_parts;
    int parent_max_batch = -1;
    for (const auto& kv : parent.S) {
        parent_max_batch = std::max(parent_max_batch, kv.first);
        for (int pid : kv.second) parent_parts.insert(pid);
    }

    int added_part = -1;
    int added_batch = -1;
    for (const auto& kv : child.S) {
        for (int pid : kv.second) {
            if (parent_parts.find(pid) == parent_parts.end()) {
                added_part = pid;
                added_batch = kv.first;
            }
        }
    }

    if (added_part < 0) return "(无新增零件)";
    if (added_batch > parent_max_batch) {
        return "Type I : 开新批次 B" + std::to_string(added_batch) +
               " 装入零件 " + std::to_string(added_part);
    }
    return "Type II: 把零件 " + std::to_string(added_part) +
           " 并入当前批次 B" + std::to_string(added_batch);
}

} // namespace

void trace_branch_and_bound(
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& L,
    const std::vector<double>& W,
    const std::vector<double>& l,
    const std::vector<double>& w,
    const std::vector<double>& h,
    const std::vector<double>& v,
    double UB,
    std::ostream& os,
    long long max_nodes
) {
    const double machine_area = L[0] * W[0];

    std::vector<double> part_areas(parts.size(), 0.0);
    for (std::size_t i = 0; i < parts.size(); ++i) {
        part_areas[parts[i]] = l[parts[i]] * w[parts[i]];
    }

    os << "========================= 分支过程追踪 (Type I / Type II) =========================\n";
    os << "零件数 n = " << parts.size()
       << "，平台容量 L×W = " << machine_area
       << "，初始上界 UB = " << UB << "\n";
    os << "本追踪与 branch_and_cut 完全一致：最优优先(best-first)出栈 + 自适应DFS，UB 仅在叶子出栈时更新。\n";
    os << "因此打印顺序就是节点真实的【出栈顺序】，不是简单的树形遍历；用节点名(Root_T1_..._T2_...)可看出父子血缘。\n";
    os << "缩进按深度，仅为可读性。\n";
    os << "----------------------------------------------------------------------------------\n";

    // ===== 与 branch_and_cut 一致的计数器 =====
    long long total_nodes = 0;       // 出栈处理的节点数
    long long generated_nodes = 0;   // 生成的子节点数
    long long area_pruned_nodes = 0; // 因容量(v)被剪的 Type II 候选数
    long long LB_pruned_nodes = 0;   // 因下界被剪的节点数
    long long leaf_nodes = 0;        // 到达的叶子数
    long long updated_solutions = 0; // UB 被刷新的次数
    std::map<int, int> pruned_nodes_per_depth; // 每个深度被剪枝的节点数

    double best_ub = UB;

    auto count_assigned = [&](const Node& nd) -> std::size_t {
        std::size_t c = 0;
        for (const auto& kv : nd.S) c += kv.second.size();
        return c;
    };

    Node root({}, 0.0, "Root", 0.0, 0.0, 0);
    update_node_metrics(root, ST, VT, UT, h, v, D);
    root.LB = compute_unassigned_lower_bound(root, parts, D, ST, VT, UT, h, v);

    // ===== 与 branch_and_cut 完全相同的栈与出栈策略 =====
    std::deque<Node> stack;
    stack.push_back(root);

    const int max_capa = 5000;   // 与正式算法一致
    const int min_capa = 2000;   // 与正式算法一致
    bool use_best_first = true;

    while (!stack.empty()) {
        if (total_nodes >= max_nodes) {
            os << "（已达到 max_nodes=" << max_nodes << " 上限，提前停止；如需完整统计请调大该上限）\n";
            break;
        }

        // 动态选择出栈策略（与 branch_and_cut 一致）
        if (stack.size() > static_cast<std::size_t>(max_capa)) use_best_first = false;
        else if (stack.size() < static_cast<std::size_t>(min_capa)) use_best_first = true;

        Node cur;
        if (use_best_first) {
            auto best_it = std::min_element(stack.begin(), stack.end(),
                [](const Node& a, const Node& b) { return a.LB < b.LB; });
            cur = *best_it;
            stack.erase(best_it);
        }
        else {
            cur = stack.back();
            stack.pop_back();
        }

        ++total_nodes;

        const std::string indent(static_cast<std::size_t>(cur.depth) * 2, ' ');
        os << indent << "* [出栈#" << total_nodes << "] 节点[" << cur.name << "] 深度=" << cur.depth << " "
           << batches_to_string(cur)
           << "  LB=" << cur.LB
           << "  C=" << cur.completion_time
           << "  TT=" << cur.total_tardiness
           << "  | 当前UB=" << best_ub << " 栈内剩余=" << stack.size() << "\n";

        // 1) 出栈时下界剪枝（注意：入栈后 UB 可能已下降，这里会再次判断）
        if (cur.LB >= best_ub) {
            ++LB_pruned_nodes;
            ++pruned_nodes_per_depth[cur.depth];
            os << indent << "  -> 出栈时被下界剪枝 (LB=" << cur.LB << " >= UB=" << best_ub << ")\n";
            continue;
        }

        // 2) 叶子节点：完整调度。UB 仅在此处（叶子出栈）更新
        if (count_assigned(cur) == parts.size()) {
            ++leaf_nodes;
            os << indent << "  -> 叶子节点：完整调度，总延误 = " << cur.total_tardiness;
            if (cur.LB < best_ub) {
                best_ub = cur.LB;
                ++updated_solutions;
                os << "  (刷新 UB -> " << best_ub << ")";
            }
            os << "\n";
            continue;
        }

        // 3) 展开子节点（与 branch_and_cut 调用同一套 generate_children / 下界）
        ChildGenerationResult res = generate_children(cur, parts, machine_area, part_areas);
        generated_nodes += static_cast<long long>(res.children.size());
        area_pruned_nodes += res.pruned_count;

        os << indent << "  生成 " << res.children.size() << " 个子节点"
           << "（另有 " << res.pruned_count << " 个 Type II 候选因容量约束(v)被剪）:\n";

        for (Node& child : res.children) {
            update_node_metrics(child, ST, VT, UT, h, v, D);
            child.LB = compute_unassigned_lower_bound(child, parts, D, ST, VT, UT, h, v);

            os << indent << "    - " << describe_child(cur, child)
               << "  => " << batches_to_string(child)
               << "  LB=" << child.LB;

            // 与 branch_and_cut 一致：child.LB < UB 才入栈，否则在“生成阶段”即被剪
            if (child.LB < best_ub) {
                os << "  [入栈待展开]\n";
                stack.push_back(std::move(child));
            }
            else {
                ++LB_pruned_nodes;
                ++pruned_nodes_per_depth[child.depth];
                os << "  [生成时即被下界剪枝: LB>=UB]\n";
            }
        }
    }

    os << "----------------------------------------------------------------------------------\n";
    os << "追踪结束（统计口径与 branch_and_cut 完全一致）：\n";
    os << "  出栈处理节点数 total_nodes = " << total_nodes << "\n";
    os << "  生成子节点数 generated_nodes = " << generated_nodes << "\n";
    os << "  叶子节点数 leaf_nodes = " << leaf_nodes << "\n";
    os << "  UB 刷新次数 updated_solutions = " << updated_solutions << "\n";
    os << "  容量(v)剪枝 area_pruned_nodes = " << area_pruned_nodes << "\n";
    os << "  下界剪枝 LB_pruned_nodes = " << LB_pruned_nodes << "\n";
    os << "  各深度被剪枝节点数 (Depth : PrunedNodes):\n";
    for (const auto& kv : pruned_nodes_per_depth) {
        os << "    深度 " << kv.first << " : " << kv.second << "\n";
    }
    os << "  最终 UB = " << best_ub << "\n";
    os << "==================================================================================\n";
}

