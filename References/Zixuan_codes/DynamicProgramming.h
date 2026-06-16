#ifndef JOB_SCHEDULING_H
#define JOB_SCHEDULING_H

#include <vector>
#include <string>
#include <map>
#include <algorithm> // for std::sort, std::max
#include <numeric>   // for std::iota
#include <random>
#include <chrono>
#include <iostream>  // For std::cout
#include <limits>    // For std::numeric_limits
#include <unordered_map>

//==================================================动态规划算法===============================================
// ======================= 统计信息（新增） =========================
struct DPMemoStats {
    long long total_V_calls;       // V() 被调用的总次数
    long long local_memo_hits;     // 命中本次调用的 memo（SubsetKey）的次数
    long long global_memo_hits;    // 命中全局 global_memo 的次数（跨调用复用）
    long long computed_states;     // 真正需要计算的新状态数（没命中任何缓存）
};

// 在 .cpp 中定义
extern DPMemoStats dp_memo_stats;

// 重置统计量（例如在一次 branch_and_cut 开始前调用）
void reset_dp_memo_stats();

// 【新增】清空全局 DP 缓存（必须在每次新的实验 Run 开始前调用）
void clear_global_dp_cache();

// 获取当前全局缓存表的大小（global_memo.size()）
std::size_t get_global_memo_size();


// ==========================定义作业结构体====================
struct Job {
    int id; // 原始ID
    double p; // processing_time
    double d; // due_date
    int original_input_index; // 新增字段：在 jobs_input 中的原始索引

    // 用于调试输出
    std::string to_string() const {
        return "Job{" + std::to_string(id) + ", p=" + std::to_string(p) + ", d=" + std::to_string(d) + ", original_idx=" + std::to_string(original_input_index) + "}";
    }
};



// ==================局部备忘录Key，用于map的key：表示一个作业子集及其开始时间
struct SubsetKey {
    std::vector<int> job_indices_in_all_jobs; // 存储作业在当前 all_jobs 列表中的索引 (这些索引现在指向已排序的作业)
    double start_time;

    // 构造函数
    SubsetKey(const std::vector<int>& indices, double time);

    // 比较运算符，用于map的key
    bool operator<(const SubsetKey& other) const;

    // 用于调试输出
    std::string to_string() const;
};

// ===========DP结果，存储 V(J, t) 的结果和导致该结果的最佳 delta 值
struct DPResult {
    double min_tardiness;
    int best_delta; // 记录导致 min_tardiness 的 delta 值，-1表示初始条件或空集

    DPResult(double tardiness = 0.0, int delta = -1);
};

//=======================全局DP状态====================
// 备忘录存储 V(J, t) 的结果和最佳 delta
extern std::map<SubsetKey, DPResult> memo;
// 全局存储所有作业（可能已排序，但id和original_input_index会保留原始信息）
extern std::vector<Job> all_jobs;


//=========================辅助函数声明
// 函数声明
void print_debug_info(const std::string& msg);

// 辅助函数：计算子集中所有作业的总处理时间
double calculate_total_processing_time(const std::vector<int>& subset_indices_in_all_jobs);

// 辅助函数：在给定子集中找到处理时间最长的作业的索引 (在all_jobs中的索引)
int get_longest_processing_time_job_index_in_all_jobs(const std::vector<int>& subset_indices_in_all_jobs);


// ======================= DP 核心与接口 =======================
// 动态规划核心函数：计算给定子集 J 和开始时间 t 的最小总延迟
// subset_indices_in_all_jobs: 存储作业在当前全局 all_jobs 列表中的索引
DPResult V(const std::vector<int>& subset_indices_in_all_jobs, double t);

// 主函数：计算给定作业列表的最小总延迟
// optimal_sequence 将存储原始ID
double minimize_total_tardiness(const std::vector<Job>& jobs_input, double initial_start_time, std::vector<int>& optimal_sequence);

// 回溯函数：从备忘录中重建最优序列
void reconstruct_optimal_sequence(const std::vector<int>& current_subset_indices_in_all_jobs, double current_time, std::vector<int>& sequence);

// ======================= 新增：跨调用共享的哈希缓存 =======================

// 以 job 的全局 id 集合 + 起始时间 t 作为 key
struct GlobalSubsetKey {
    std::vector<int> job_ids; // 该状态涉及的作业ID集合（已排序）
    double start_time;        // 起始时间 t

    GlobalSubsetKey() = default;
    GlobalSubsetKey(const std::vector<int>& ids, double t);

    bool operator==(const GlobalSubsetKey& other) const;
};

// 自定义 hash，用于 unordered_map
struct GlobalSubsetKeyHash {
    std::size_t operator()(const GlobalSubsetKey& k) const noexcept;
};

// 在 .cpp 中定义：跨所有 DP 调用共享
extern std::unordered_map<GlobalSubsetKey, DPResult, GlobalSubsetKeyHash> global_memo;

//================================枚举函数，共用job结构体===================================

// 计算给定作业序列的总延迟时间
double calculate_total_lateness(const std::vector<Job>& schedule); // 返回值修改

// 使用枚举法找到最小化总延迟时间的调度方案
std::vector<Job> find_min_lateness_schedule(const std::vector<Job>& jobs);


//================================随机算例生成================================================

// 随机生成作业列表
std::vector<Job> generate_random_jobs(int num_jobs, double min_p, double max_p, double min_d, double max_d); // 参数修改

#endif // JOB_SCHEDULING_H