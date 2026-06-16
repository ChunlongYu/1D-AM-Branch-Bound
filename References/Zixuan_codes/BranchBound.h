#ifndef BRANCH_BOUND_H
#define BRANCH_BOUND_H

#include <iostream>
#include <vector>
#include <set>
#include <map>
#include <unordered_map>
#include <deque>
#include <string>
#include <utility>
#include <ostream>
#include <fstream>
#include <filesystem>

//===================节点定义=======================
class Node {
public:
    // 每个批次对应的零件列表
    std::unordered_map<int, std::vector<int>> S;
    double LB;            // 当前节点的下界
    double completion_time;     // 当前累计完成时间
    double total_tardiness;     // 当前已产生总延迟
    std::string name;     // 节点名称
    int depth;

    Node();
    Node(const std::unordered_map<int, std::vector<int>>& S_,
        double LB_,
        const std::string& name_ = "N",
        double completion_time_ = 0.0,
        double total_tardiness_ = 0.0,
        int depth_ = 0);

    bool operator==(const Node& other) const;

    friend std::ostream& operator<<(std::ostream& os, const Node& node);

    struct Hash {
        std::size_t operator()(const Node& node) const;
    };
};


//========================= 支配规则辅助结构 =========================
struct StateMetric {
    double tt; // 总延迟 (Total Tardiness)
    double c;  // 完成时间 (Completion Time)
};

// 用于让 std::vector<int> 能作为 std::unordered_map 的 key
struct VectorHash {
    std::size_t operator()(const std::vector<int>& v) const {
        std::size_t seed = 0;
        for (int i : v) {
            // boost::hash_combine 风格的哈希组合
            seed ^= std::hash<int>{}(i)+0x9e3779b9 + (seed << 6) + (seed >> 2);
        }
        return seed;
    }
};



//========================生成初始解==============================
typedef std::map<int, std::set<int>> BatchMap;

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
);

//=========================子节点生成==============================
struct ChildGenerationResult {
    std::vector<Node> children;
    int pruned_count;
};

// [修改]：取消原本的注释，并声明子节点生成函数
ChildGenerationResult generate_children(
    const Node& node,
    const std::vector<int>& parts,
    double machine_area,
    const std::vector<double>& part_areas
);


//=========================下界计算================================
// [删除]：删除了原本的 compute_completion_times 和 compute_assigned_tardiness 的声明
// [新增]：声明全新的全局状态重算函数 update_node_metrics
void update_node_metrics(
    Node& node,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::vector<double>& D
);

double compute_unassigned_lower_bound(
    const Node& node,
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v
);

//==========================Branch and Bound========================
struct FirstLevelNodeInfo {
    std::string name;
    double lb;
    double completion_time;
    int unassigned_count;
    std::vector<int> unassigned_parts; // 存储未分配零件的 ID
};

struct Stats {
    int updated_solutions = 0;
    int total_nodes = 0;
    int generated_nodes = 0;
    int area_pruned_nodes = 0;
    int LB_pruned_nodes = 0;
    int U_pruned_nodes = 0;
    int leaf_nodes = 0;
    std::unordered_map<int, int> pruned_nodes_per_depth;     //每个深度被剪枝的节点数
    std::vector<std::pair<double, double>> UB_updates;       // <时间戳, 新UB>
    std::vector<std::pair<double, double>> LB_convergence;   // <时间戳, 当前最小LB>
    // [新增] 替换原来的 pair vector，或者新增这个详细信息的 vector
    std::vector<FirstLevelNodeInfo> first_level_details;
    std::vector<std::pair<std::string, double>> first_level_node_lbs; // 新增：存储第一层子节点的名称和LB
    long long total_V_calls;       // V() 被调用的总次数
    long long local_memo_hits;     // 命中本次调用的 memo（SubsetKey）的次数
    long long global_memo_hits;    // 命中全局 global_memo 的次数（跨调用复用）
    long long computed_states;     // 真正需要计算的新状态数（没命中任何缓存）
    long long delta_trigger_count = 0;   // 记录Delta策略触发次数
    long long serial_pruning_count = 0;  // 记录并行无法剪枝但串行成功剪枝的次数
    long long serial_missing_count = 0;  // 记录串行比并行小或相等的次数
};

std::pair<Node, Stats> branch_and_cut(
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<std::set<int>>& initial_infeasible,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& L,
    const std::vector<double>& W,
    const std::vector<double>& l,
    const std::vector<double>& w,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::unordered_map<int, std::vector<int>>& initial_S,
    double UB,
    double time_limit_seconds,
    const std::string& path
);

//==========================分支过程追踪（教学/调试用）========================
// 在【小算例】上以深度优先方式打印 Type I / Type II 的分支过程：
// 对每个被访问的节点打印其批次构成、LB、完成时间 C 与已产生总延误 TT，
// 并逐一列出它生成的 Type I / Type II 子节点（标注加入了哪个零件、是否触发剪枝）。
// 该函数与 branch_and_cut 复用同一套 generate_children / update_node_metrics / 下界，
// 因此打印出的就是算法真实的分支行为，仅用于观察，不参与正式实验。
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
    std::ostream& os = std::cout,
    long long max_nodes = 100000
);

// 内置 4 零件小算例的一键追踪入口（实现在 BranchBound.cpp）。
// 在 main() 里加一行 run_branch_trace_example(); 即可打印整棵分支树。
void run_branch_trace_example();


#endif // BRANCH_BOUND_H