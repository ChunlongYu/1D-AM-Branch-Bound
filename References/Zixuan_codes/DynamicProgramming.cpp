#include "DynamicProgramming.h" // 确保这里引用的是修改后的头文件
#include <iostream>
#include <numeric> // For std::accumulate
#include <limits>  // For std::numeric_limits
#include <iostream>
#include <sstream>

// =================== 全局统计量定义（新增） ======================
DPMemoStats dp_memo_stats = { 0, 0, 0, 0 };

void reset_dp_memo_stats() {
    dp_memo_stats.total_V_calls = 0;
    dp_memo_stats.local_memo_hits = 0;
    dp_memo_stats.global_memo_hits = 0;
    dp_memo_stats.computed_states = 0;
}

// 【新增】实现清空函数
void clear_global_dp_cache() {
    // 1. 强制释放 global_memo 的内存
    // 创建一个空的临时 map，然后和全局 map 交换。
    // 临时 map 销毁时，会带走原 global_memo 占用的巨大内存空间。
    std::unordered_map<GlobalSubsetKey, DPResult, GlobalSubsetKeyHash> empty_global;
    global_memo.swap(empty_global);

    // 2. 强制释放 memo 的内存
    std::map<SubsetKey, DPResult> empty_memo;
    memo.swap(empty_memo);

    // 3. 强制释放 all_jobs 的内存 (虽然它不大，但是个好习惯)
    std::vector<Job> empty_jobs;
    all_jobs.swap(empty_jobs);

    // 4. (可选) 如果在 Windows 上，可以请求 OS 清理工作集
    // #ifdef _WIN32
    //     SetProcessWorkingSetSize(GetCurrentProcess(), (SIZE_T)-1, (SIZE_T)-1);
    // #endif

    print_debug_info("Global DP Cache Completely Freed (Memory Released).");
}

// =================== 全局变量定义（和原来一致） ======================
std::vector<Job> all_jobs;
std::map<SubsetKey, DPResult> memo;

// 新增：跨多次 DP 调用共享的哈希备忘录
std::unordered_map<GlobalSubsetKey, DPResult, GlobalSubsetKeyHash> global_memo;

//// =================== Job 成员函数 ======================
//std::string Job::to_string() const {
//    std::ostringstream oss;
//    oss << "Job{id=" << id
//        << ", p=" << p
//        << ", d=" << d
//        << ", orig_idx=" << original_input_index
//        << "}";
//    return oss.str();
//}

// =================== SubsetKey 实现（和原来一致） ====================

// SubsetKey 构造函数实现
SubsetKey::SubsetKey(const std::vector<int>& indices, double time) : start_time(time) {
    job_indices_in_all_jobs = indices;
    std::sort(job_indices_in_all_jobs.begin(), job_indices_in_all_jobs.end()); // 确保顺序一致，便于map查找
}

// SubsetKey 比较运算符实现
bool SubsetKey::operator<(const SubsetKey& other) const {
    if (start_time != other.start_time) {
        return start_time < other.start_time;
    }
    return job_indices_in_all_jobs < other.job_indices_in_all_jobs;
}

// SubsetKey 调试输出实现
std::string SubsetKey::to_string() const {
    std::string s = "{";
    for (size_t i = 0; i < job_indices_in_all_jobs.size(); ++i) {
        // 输出作业的原始ID
        s += std::to_string(all_jobs[job_indices_in_all_jobs[i]].id);
        if (i < job_indices_in_all_jobs.size() - 1) {
            s += ", ";
        }
    }
    s += "}, t=" + std::to_string(start_time);
    return s;
}

// // =================== DPResult 实现（和原来一致） =====================
DPResult::DPResult(double tardiness, int delta) : min_tardiness(tardiness), best_delta(delta) {}

// =================== GlobalSubsetKey 实现（新增） ====================
GlobalSubsetKey::GlobalSubsetKey(const std::vector<int>& ids, double t)
    : job_ids(ids), start_time(t)
{
    std::sort(job_ids.begin(), job_ids.end());
}

bool GlobalSubsetKey::operator==(const GlobalSubsetKey& other) const {
    return start_time == other.start_time &&
        job_ids == other.job_ids;
}

std::size_t GlobalSubsetKeyHash::operator()(const GlobalSubsetKey& k) const noexcept {
    std::size_t seed = std::hash<double>()(k.start_time);
    for (int id : k.job_ids) {
        seed ^= std::hash<int>()(id) + 0x9e3779b9 + (seed << 6) + (seed >> 2);
    }
    return seed;
}

// =================== 调试输出（和原来一致） =================
void print_debug_info(const std::string& msg) {
     //std::cout << "[DEBUG] " << msg << std::endl; // 可以取消注释以查看调试信息
}

// ================ 辅助函数：总处理时间（和原来一致，只是去掉 static） ==================
double calculate_total_processing_time(
    const std::vector<int>& subset_indices_in_all_jobs)
{
    double total_p = 0.0;
    for (int job_idx : subset_indices_in_all_jobs) {
        total_p += all_jobs[job_idx].p;
    }
    return total_p;
}

// ================ 辅助函数：最长处理时间作业索引（和原来一致，只是去掉 static） =========
int get_longest_processing_time_job_index_in_all_jobs(
    const std::vector<int>& subset_indices_in_all_jobs)
{
    if (subset_indices_in_all_jobs.empty()) {
        return -1;
    }

    double max_p = -1.0;
    int longest_job_current_idx = -1;

    for (int job_idx : subset_indices_in_all_jobs) {
        if (all_jobs[job_idx].p > max_p) {
            max_p = all_jobs[job_idx].p;
            longest_job_current_idx = job_idx;
        }
    }
    return longest_job_current_idx;
}

// ================ 新增辅助：从 memo / global_memo 查状态 ============
static DPResult lookup_dp_result(
    const std::vector<int>& subset_indices_in_all_jobs,
    double t)
{
    SubsetKey key(subset_indices_in_all_jobs, t);
    auto it_local = memo.find(key);
    if (it_local != memo.end()) {
        return it_local->second;
    }

    // 本地没有，则构造全局 key（用 job 的全局 ID）
    std::vector<int> ids;
    ids.reserve(subset_indices_in_all_jobs.size());
    for (int idx : subset_indices_in_all_jobs) {
        ids.push_back(all_jobs[idx].id);
    }
    GlobalSubsetKey gkey(ids, t);

    auto it_global = global_memo.find(gkey);
    if (it_global != global_memo.end()) {
        // 顺便写回本地 memo，便于后续回溯
        memo[key] = it_global->second;
        return it_global->second;
    }

    // 正常不应走到这里（说明没有先调用 V）
    throw std::runtime_error(
        "DP state not found in memo/global_memo in reconstruct_optimal_sequence");
}

// =================== DP 核心递归函数 V ===================
// 动态规划核心函数实现 (V 函数签名不变)
DPResult V(const std::vector<int>& subset_indices_in_all_jobs, double t) {
    SubsetKey current_key(subset_indices_in_all_jobs, t);
    print_debug_info("进入 V(" + current_key.to_string() + ")");

    ++dp_memo_stats.total_V_calls;

    // ---------- 1. 构造全局 Key（job 的全局ID + t） ----------
    std::vector<int> ids;
    ids.reserve(subset_indices_in_all_jobs.size());
    for (int idx : subset_indices_in_all_jobs) {
        ids.push_back(all_jobs[idx].id);   // Job.id = 你的 pid
    }
    GlobalSubsetKey gkey(ids, t);

    // ---------- 2. 先查本地 memo ----------
    if (memo.count(current_key)) {
        // 统计：本地 memo 命中
        ++dp_memo_stats.local_memo_hits;
        print_debug_info("  -> 本地 memo 命中");
        return memo[current_key];
    }

    // ---------- 3. 再查全局 global_memo ----------
    auto it_global = global_memo.find(gkey);
    if (it_global != global_memo.end()) {
        // 统计：全局缓存命中（这是跨调用/跨节点复用）
        ++dp_memo_stats.global_memo_hits;
        print_debug_info("  -> 全局 global_memo 命中");
        memo[current_key] = it_global->second; // 为当前调用补一份
        return it_global->second;
    }

    // 统计：这是一个需要“新计算”的状态
    ++dp_memo_stats.computed_states;

    // ---------- 4. 以下为你原来的“基础计算”，递推逻辑未改 ----------

    // 初始条件：空集
    if (subset_indices_in_all_jobs.empty()) {
        print_debug_info("  -> 集合为空，返回 {Tardiness: 0.0, Delta: -1}");
        DPResult res(0.0, -1);
        memo[current_key] = res;
        global_memo[gkey] = res;
        return res;
    }
    if (subset_indices_in_all_jobs.size() == 1) {
        int job_idx = subset_indices_in_all_jobs[0];
        double completion_time = t + all_jobs[job_idx].p;
        double tardiness = std::max(0.0, completion_time - all_jobs[job_idx].d);

        DPResult res(tardiness, -1);
        memo[current_key] = res;
        global_memo[gkey] = res;   // 关键：补上全局精确缓存
        return res;
    }

    // 递归关系
    double min_total_tardiness = std::numeric_limits<double>::max();
    int best_delta_for_current_key = -1;

    // 获取当前子集中处理时间最长的作业的在 all_jobs 列表中的索引
    int k_prime_idx_in_all_jobs = get_longest_processing_time_job_index_in_all_jobs(subset_indices_in_all_jobs);
    if (k_prime_idx_in_all_jobs == -1) {
        return DPResult(-1.0, -1); // 异常情况
    }

    // 拆分作业集合 J 为 smaller_jobs 和 larger_jobs
    // smaller_jobs: 在 all_jobs 中的索引小于 k_prime_idx_in_all_jobs 且在当前子集中的作业
    // larger_jobs: 在 all_jobs 中的索引大于 k_prime_idx_in_all_jobs 且在当前子集中的作业
    std::vector<int> smaller_jobs_in_subset;
    std::vector<int> larger_jobs_in_subset;

    for (int idx : subset_indices_in_all_jobs) {
        if (idx < k_prime_idx_in_all_jobs) {
            smaller_jobs_in_subset.push_back(idx);
        }
        else if (idx > k_prime_idx_in_all_jobs) {
            larger_jobs_in_subset.push_back(idx);
        }
    }
    // 确保这些子集内部也是排序的，以保证 SubsetKey 的一致性
    std::sort(smaller_jobs_in_subset.begin(), smaller_jobs_in_subset.end());
    std::sort(larger_jobs_in_subset.begin(), larger_jobs_in_subset.end());

    // 遍历所有可能的 δ 值
    // δ 代表 larger_jobs_in_subset 中有多少个作业排在 k_prime_idx_in_all_jobs 之前
    for (int delta = 0; delta <= larger_jobs_in_subset.size(); ++delta) {
        std::vector<int> first_part_jobs_recurs = smaller_jobs_in_subset; // 这部分总是排在 k_prime 之前
        for (int i = 0; i < delta; ++i) { // 从 larger_jobs 中取 delta 个也排在 k_prime 之前
            first_part_jobs_recurs.push_back(larger_jobs_in_subset[i]);
        }
        std::sort(first_part_jobs_recurs.begin(), first_part_jobs_recurs.end()); // 确保排序

        std::vector<int> third_part_jobs_recurs; // 剩余的 larger_jobs 排在 k_prime 之后
        for (size_t i = delta; i < larger_jobs_in_subset.size(); ++i) {
            third_part_jobs_recurs.push_back(larger_jobs_in_subset[i]);
        }
        std::sort(third_part_jobs_recurs.begin(), third_part_jobs_recurs.end()); // 确保排序

        // 计算 k_prime_idx_in_all_jobs 的完成时间
        double completion_k_prime_delta = t + calculate_total_processing_time(first_part_jobs_recurs) + all_jobs[k_prime_idx_in_all_jobs].p;

        double current_tardiness_k_prime = std::max(0.0, completion_k_prime_delta - all_jobs[k_prime_idx_in_all_jobs].d);

        DPResult result_first_part = V(first_part_jobs_recurs, t);
        DPResult result_third_part = V(third_part_jobs_recurs, completion_k_prime_delta);

        double total_val = result_first_part.min_tardiness + current_tardiness_k_prime + result_third_part.min_tardiness;

        print_debug_info("  δ=" + std::to_string(delta) + ": Ck'(" + std::to_string(delta) + ")=" + std::to_string(completion_k_prime_delta) +
            ", T(k')=" + std::to_string(current_tardiness_k_prime) +
            ", V_first=" + std::to_string(result_first_part.min_tardiness) +
            ", V_third=" + std::to_string(result_third_part.min_tardiness) +
            ", Total=" + std::to_string(total_val));

        if (total_val < min_total_tardiness) {
            min_total_tardiness = total_val;
            best_delta_for_current_key = delta;
        }
    }

    print_debug_info("  -> V(" + current_key.to_string() + ") 计算完成，结果: {Tardiness: " + std::to_string(min_total_tardiness) + ", Delta: " + std::to_string(best_delta_for_current_key) + "}");
    DPResult res(min_total_tardiness, best_delta_for_current_key);
    memo[current_key] = res;
    global_memo[gkey] = res;
    return res;
}

// =================== 最小总延迟主函数 ===================
// 主函数：计算给定作业列表的最小总延迟
double minimize_total_tardiness(const std::vector<Job>& jobs_input, double initial_start_time, std::vector<int>& optimal_sequence) {
    // 预处理：将 jobs_input 复制到 all_jobs 并记录原始索引，然后按 due_date 排序
    all_jobs.clear();
    for (size_t i = 0; i < jobs_input.size(); ++i) {
        Job j = jobs_input[i];
        j.original_input_index = static_cast<int>(i); // 记录在原始输入中的位置
        all_jobs.push_back(j);
    }

    // 按照 due_date 升序排列 all_jobs，如果d相同无法保证顺序一致，因此复用会出现问题，用tie-breaker连接
    std::sort(all_jobs.begin(), all_jobs.end(), [](const Job& a, const Job& b) {
        if (a.d != b.d) return a.d < b.d;
        return a.id < b.id; // 或 return a.original_input_index < b.original_input_index;
        });

    // Debug: 打印排序后的 all_jobs
    // std::cout << "Sorted all_jobs by due_date:" << std::endl;
    // for(const auto& job : all_jobs) {
    //     std::cout << "  " << job.to_string() << std::endl;
    // }

    std::vector<int> initial_subset_indices_in_all_jobs(all_jobs.size());
    // 此时 initial_subset_indices_in_all_jobs 包含 0 到 N-1，
    // 这些索引现在指向 all_jobs 中已经按 due_date 排序后的作业。
    std::iota(initial_subset_indices_in_all_jobs.begin(), initial_subset_indices_in_all_jobs.end(), 0);

    memo.clear(); // 清空备忘录

    DPResult result = V(initial_subset_indices_in_all_jobs, initial_start_time);

    // 回溯重建最优序列
    optimal_sequence.clear();
    // reconstruct_optimal_sequence 将会使用 all_jobs[idx].id 来获取原始ID
    reconstruct_optimal_sequence(initial_subset_indices_in_all_jobs, initial_start_time, optimal_sequence);

    return result.min_tardiness;
}

// =================== 回溯最优序列 =======================
// 回溯函数：从备忘录中重建最优序列
void reconstruct_optimal_sequence(const std::vector<int>& current_subset_indices_in_all_jobs, double current_time, std::vector<int>& sequence) {
    if (current_subset_indices_in_all_jobs.empty()) {
        return;
    }
    if (current_subset_indices_in_all_jobs.size() == 1) {
        sequence.push_back(all_jobs[current_subset_indices_in_all_jobs[0]].id); // 添加作业原始ID
        return;
    }

    SubsetKey current_key(current_subset_indices_in_all_jobs, current_time);
    DPResult stored_result = lookup_dp_result(current_subset_indices_in_all_jobs, current_time);
    int best_delta = stored_result.best_delta;

    int k_prime_idx_in_all_jobs = get_longest_processing_time_job_index_in_all_jobs(current_subset_indices_in_all_jobs);

    // 重新构建 smaller_jobs_in_subset 和 larger_jobs_in_subset，与 V 函数中的逻辑一致
    std::vector<int> smaller_jobs_in_subset;
    std::vector<int> larger_jobs_in_subset;

    for (int idx : current_subset_indices_in_all_jobs) {
        if (idx < k_prime_idx_in_all_jobs) {
            smaller_jobs_in_subset.push_back(idx);
        }
        else if (idx > k_prime_idx_in_all_jobs) {
            larger_jobs_in_subset.push_back(idx);
        }
    }
    std::sort(smaller_jobs_in_subset.begin(), smaller_jobs_in_subset.end());
    std::sort(larger_jobs_in_subset.begin(), larger_jobs_in_subset.end());


    std::vector<int> first_part_jobs_recurs = smaller_jobs_in_subset;
    for (int i = 0; i < best_delta; ++i) {
        first_part_jobs_recurs.push_back(larger_jobs_in_subset[i]);
    }
    std::sort(first_part_jobs_recurs.begin(), first_part_jobs_recurs.end());

    std::vector<int> third_part_jobs_recurs;
    for (size_t i = best_delta; i < larger_jobs_in_subset.size(); ++i) {
        third_part_jobs_recurs.push_back(larger_jobs_in_subset[i]);
    }
    std::sort(third_part_jobs_recurs.begin(), third_part_jobs_recurs.end());

    // 递归重建第一部分
    reconstruct_optimal_sequence(first_part_jobs_recurs, current_time, sequence);

    // 添加 k_prime_idx_in_all_jobs
    sequence.push_back(all_jobs[k_prime_idx_in_all_jobs].id); // 添加作业原始ID

    // 计算 k_prime 的完成时间，作为第三部分的开始时间
    double completion_k_prime_delta = current_time + calculate_total_processing_time(first_part_jobs_recurs) + all_jobs[k_prime_idx_in_all_jobs].p;

    // 递归重建第三部分
    reconstruct_optimal_sequence(third_part_jobs_recurs, completion_k_prime_delta, sequence);
}