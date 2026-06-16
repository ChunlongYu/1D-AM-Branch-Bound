#include "BranchBound.h"
#include <algorithm>
#include <cstdint>
#include <cmath>
#include <chrono>
#include <limits>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <string>

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
Node::Node() : LB(0.0), name("N") {}

Node::Node(const std::unordered_map<int, std::vector<int> >& S_,
    double LB_,
    const std::string& name_)
    : S(S_), LB(LB_), name(name_) {
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

//=======================子节点生成（位掩码 + 面积即时剪枝）========================
//std::vector<Node> generate_children(
//    const Node& node,
//    const std::vector<int>& parts,
//    double machine_area,
//    const std::vector<double>& part_areas
//) {
//    // 已分配集合
//    std::unordered_set<int> assigned;
//    assigned.reserve(parts.size());
//    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
//        it != node.S.end(); ++it) {
//        for (std::size_t j = 0; j < it->second.size(); ++j) {
//            assigned.insert(it->second[j]);
//        }
//    }
//
//    // 未分配列表
//    std::vector<int> unassigned;
//    unassigned.reserve(parts.size());
//    for (std::size_t i = 0; i < parts.size(); ++i) {
//        if (assigned.find(parts[i]) == assigned.end()) {
//            unassigned.push_back(parts[i]);
//        }
//    }
//
//    int n = static_cast<int>(unassigned.size());
//    if (n == 0) return std::vector<Node>();
//
//    std::vector<Node> children;
//    if (n < 20) children.reserve((1u << n) - 1);
//
//    // 枚举所有非空子集
//    for (unsigned mask = 1; mask < (1u << n); ++mask) {
//        double a_sum = 0.0;
//        std::vector<int> subset;
//        for (int b = 0; b < n; ++b) {
//            if (mask & (1u << b)) {
//                a_sum += part_areas[unassigned[b]];
//                if (a_sum > machine_area) break;
//                subset.push_back(unassigned[b]);
//            }
//        }
//        if (a_sum > machine_area) continue;
//
//        // 构造新 S
//        std::unordered_map<int, std::vector<int> > newS = node.S;
//        int max_id = -1;
//        for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = newS.begin(); it != newS.end(); ++it) {
//            if (it->first > max_id) max_id = it->first;
//        }
//        newS[max_id + 1] = subset;
//
//        children.push_back(Node(newS, 0.0, node.name + std::to_string(max_id + 1)));
//    }
//    return children;
//}

std::vector<Node> generate_children(
    const Node& node,
    const std::vector<int>& parts,
    double machine_area,
    const std::vector<double>& part_areas
) {
    // 提取已分配零件
    std::unordered_set<int> assigned;
    assigned.reserve(parts.size());
    for (const auto& [batch_id, batch_parts] : node.S) {
        for (int pid : batch_parts) {
            assigned.insert(pid);
        }
    }

    // 收集未分配零件
    std::vector<int> unassigned;
    unassigned.reserve(parts.size());
    for (int p : parts) {
        if (assigned.find(p) == assigned.end()) {
            unassigned.push_back(p);
        }
    }

    const int n = static_cast<int>(unassigned.size());
    if (n == 0) return {};

    std::vector<Node> children;
    children.reserve((1u << n) - 1);  // 最多 2^n - 1 个子集

    // 获取当前最大批次编号
    int max_batch_id = -1;
    for (const auto& [batch_id, _] : node.S) {
        if (batch_id > max_batch_id) max_batch_id = batch_id;
    }

    int child_index = 0;

    // 枚举所有非空子集（不剪枝，保持全遍历）
    for (unsigned mask = 1; mask < (1u << n); ++mask) {
        double total_area = 0.0;
        std::vector<int> subset;
        subset.reserve(n);

        for (int i = 0; i < n; ++i) {
            if (mask & (1u << i)) {
                int part_id = unassigned[i];
                double area = part_areas[part_id];
                total_area += area;
                if (total_area > machine_area) break;
                subset.push_back(part_id);
            }
        }

        if (total_area > machine_area) continue;

        auto new_S = node.S;
        new_S[max_batch_id + 1] = std::move(subset);
        std::string child_name = node.name + "_" + std::to_string(child_index++);
        children.emplace_back(std::move(new_S), 0.0, child_name);
    }

    return children;
}



//===========================下界计算（完全无输出）==============================
double compute_total_lower_bound(
    const Node& node,
    const std::vector<int>& parts,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v,
    const std::vector<double>& part_areas,
    double machine_area
) {
    double time_cursor = 0.0;
    double tard_assigned = 0.0;

    // 已分配（修复：批次顺序加工，必须按批次编号顺序计算完工时间，
    // 原实现按 unordered_map 的哈希顺序遍历，会得到与预期不同的加工序列，
    // 导致下界/叶子目标值错误且依赖输入顺序）
    std::unordered_map<int, double> comp;
    comp.reserve(parts.size());
    std::vector<int> _bids;
    _bids.reserve(node.S.size());
    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
        it != node.S.end(); ++it) _bids.push_back(it->first);
    std::sort(_bids.begin(), _bids.end());
    for (std::size_t bi = 0; bi < _bids.size(); ++bi) {
        const std::vector<int>& batch = node.S.at(_bids[bi]);
        double vol = 0.0, mh = 0.0;
        for (std::size_t j = 0; j < batch.size(); ++j) {
            vol += v[batch[j]];
            if (h[batch[j]] > mh) mh = h[batch[j]];
        }
        double PT = ST[0] + VT[0] * vol + UT[0] * mh;
        for (std::size_t j = 0; j < batch.size(); ++j) {
            comp[batch[j]] = time_cursor + PT;
        }
        time_cursor += PT;
    }
    for (typename std::unordered_map<int, double>::const_iterator it = comp.begin(); it != comp.end(); ++it) {
        tard_assigned += std::max(0.0, it->second - D[it->first]);
    }

    // 收集未排零件（U(N)）
    std::unordered_set<int> assigned;
    assigned.reserve(parts.size());
    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
        it != node.S.end(); ++it) {
        for (std::size_t j = 0; j < it->second.size(); ++j) {
            assigned.insert(it->second[j]);
        }
    }
    std::vector<int> U;
    U.reserve(parts.size());
    for (std::size_t i = 0; i < parts.size(); ++i) {
        int p = parts[i];
        if (assigned.find(p) == assigned.end()) U.push_back(p);
    }

    // 未排零件下界 1：并行下界（每件单独成批，从 time_cursor 开始）
    double lb_par = 0.0;
    for (std::size_t i = 0; i < U.size(); ++i) {
        int p = U[i];
        double c = time_cursor + ST[0] + VT[0] * v[p] + UT[0] * h[p];
        lb_par += std::max(0.0, c - D[p]);
    }

    // 未排零件下界 2：position 下界（论文附录 A，基准时间平移到 time_cursor）
    double lb_pos = 0.0;
    const int q = static_cast<int>(U.size());
    if (q > 0) {
        std::vector<double> a(q), hh(q), vv(q), dd(q);
        for (int k = 0; k < q; ++k) {
            int p = U[k];
            a[k]  = part_areas[p];
            hh[k] = h[p];
            vv[k] = v[p];
            dd[k] = D[p];
        }
        std::sort(a.begin(),  a.end());
        std::sort(hh.begin(), hh.end());
        std::sort(vv.begin(), vv.end());
        std::sort(dd.begin(), dd.end());
        std::vector<double> hpre(q + 1, 0.0);
        for (int t = 1; t <= q; ++t) hpre[t] = hpre[t - 1] + hh[t - 1];
        double Asum = 0.0, Vsum = 0.0;
        for (int k = 1; k <= q; ++k) {
            Asum += a[k - 1];
            Vsum += vv[k - 1];
            int beta = static_cast<int>(std::ceil(Asum / machine_area - 1e-9));
            if (beta < 1) beta = 1;
            if (beta > k) beta = k;
            double Hk = hpre[beta - 1] + hh[k - 1];
            double Ck = time_cursor + beta * ST[0] + VT[0] * Vsum + UT[0] * Hk;
            double td = Ck - dd[k - 1];
            if (td > 0.0) lb_pos += td;
        }
    }

    // 综合未排零件下界：取并行界与 position 界的最大值
    return tard_assigned + std::max(lb_par, lb_pos);
}

//==================== 节点状态 (C_N, TT_N, 已排集合掩码) ====================
// 批次按编号顺序加工，与下界函数保持一致。
static void compute_node_state(
    const Node& node,
    const std::vector<double>& D,
    const std::vector<double>& ST,
    const std::vector<double>& VT,
    const std::vector<double>& UT,
    const std::vector<double>& h,
    const std::vector<double>& v,
    double& C_N, double& TT_N, std::uint64_t& sched_mask)
{
    double time_cursor = 0.0, tard = 0.0;
    std::uint64_t mask = 0;
    std::vector<int> bids;
    bids.reserve(node.S.size());
    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
        it != node.S.end(); ++it) bids.push_back(it->first);
    std::sort(bids.begin(), bids.end());
    for (std::size_t bi = 0; bi < bids.size(); ++bi) {
        const std::vector<int>& batch = node.S.at(bids[bi]);
        double vol = 0.0, mh = 0.0;
        for (std::size_t j = 0; j < batch.size(); ++j) {
            vol += v[batch[j]];
            if (h[batch[j]] > mh) mh = h[batch[j]];
        }
        double PT = ST[0] + VT[0] * vol + UT[0] * mh;
        time_cursor += PT;
        for (std::size_t j = 0; j < batch.size(); ++j) {
            tard += std::max(0.0, time_cursor - D[batch[j]]);
            mask |= (std::uint64_t(1) << batch[j]);
        }
    }
    C_N = time_cursor; TT_N = tard; sched_mask = mask;
}

//==================== 自适应节点选择的比较器 ====================
// best_first=true：最优优先，LB 最小者位于堆顶；
// best_first=false：深度优先，批次数 |B(N)|=S.size() 最大者位于堆顶。
struct OracleNodeCmp {
    const bool* best_first;
    bool operator()(const Node& a, const Node& b) const {
        if (*best_first) {
            if (a.LB != b.LB) return a.LB > b.LB;     // 小 LB 在堆顶
            return a.S.size() < b.S.size();           // 平手：更深者在堆顶
        } else {
            if (a.S.size() != b.S.size())
                return a.S.size() < b.S.size();        // 大深度在堆顶
            return a.LB > b.LB;                        // 平手：小 LB 在堆顶
        }
    }
};


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
    const std::string& path,
    long long node_Nmax,
    long long node_Nmin
) {
    Stats stats;
    double machine_area = L[0] * W[0];

    // 预计算面积
    std::vector<double> part_areas(parts.size(), 0.0);
    for (std::size_t i = 0; i < parts.size(); ++i) {
        part_areas[parts[i]] = l[parts[i]] * w[parts[i]];
    }

    // 判断是否全分配
    auto all_assigned = [&](const Node& nd)->bool {
        std::size_t cnt = 0;
        for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = nd.S.begin();
            it != nd.S.end(); ++it) {
            cnt += it->second.size();
        }
        return cnt == parts.size();
        };

    Node best(initial_S, 0.0, "Best");
    Node root(std::unordered_map<int, std::vector<int> >(), 0.0, "Root");
    root.LB = compute_total_lower_bound(root, parts, D, ST, VT, UT, h, v, part_areas, machine_area);

    // ===== 支配规则：每个“已排零件集合掩码”维护非支配的 (C_N, TT_N) 标签 =====
    // 论文附录 B：相同已排集合下，若存在 C、TT 都不劣（且至少一个更优）的标签，则被支配可剪。
    std::unordered_map<std::uint64_t, std::vector<std::pair<double, double> > > dom_labels;
    const double DEPS = 1e-9;
    auto dom_dominates = [&](const std::pair<double, double>& a,
                             const std::pair<double, double>& b) -> bool {
        // a 支配 b：a.C<=b.C, a.TT<=b.TT，且至少一个严格更小
        return a.first <= b.first + DEPS && a.second <= b.second + DEPS &&
               (a.first < b.first - DEPS || a.second < b.second - DEPS);
    };
    auto dom_equal = [&](const std::pair<double, double>& a,
                         const std::pair<double, double>& b) -> bool {
        return std::abs(a.first - b.first) <= DEPS && std::abs(a.second - b.second) <= DEPS;
    };
    // 入栈登记：被支配/重复 -> 返回 false（丢弃）；否则删掉被它支配的旧标签并登记
    auto dom_register = [&](std::uint64_t m, const std::pair<double, double>& lab) -> bool {
        std::vector<std::pair<double, double> >& vec = dom_labels[m];
        for (std::size_t i = 0; i < vec.size(); ++i)
            if (dom_dominates(vec[i], lab) || dom_equal(vec[i], lab)) return false;
        vec.erase(std::remove_if(vec.begin(), vec.end(),
                  [&](const std::pair<double, double>& L){ return dom_dominates(lab, L); }),
                  vec.end());
        vec.push_back(lab);
        return true;
    };
    // 出栈复查：是否已被某个（入栈后新增的）更优标签严格支配
    auto dom_is_dominated = [&](std::uint64_t m, const std::pair<double, double>& lab) -> bool {
        std::unordered_map<std::uint64_t, std::vector<std::pair<double, double> > >::iterator it = dom_labels.find(m);
        if (it == dom_labels.end()) return false;
        for (std::size_t i = 0; i < it->second.size(); ++i)
            if (dom_dominates(it->second[i], lab)) return true;
        return false;
    };
    dom_register(0ULL, std::make_pair(0.0, 0.0));   // 根节点：空集，(C,TT)=(0,0)

    // 自适应活动列表（二叉堆）：默认最优优先；> N_max 转深度优先，< N_min 转回。
    bool best_first = true;
    OracleNodeCmp node_cmp{ &best_first };
    std::vector<Node> active;
    active.push_back(root);
    std::push_heap(active.begin(), active.end(), node_cmp);

    auto t0 = std::chrono::steady_clock::now();

    while (!active.empty()) {
        auto t1 = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(t1 - t0).count();
        if (time_limit_seconds > 0.0 && elapsed > time_limit_seconds) {
            break;
        }

        // 自适应切换（滞回：N_max 进 DFS，N_min 回 best-first）
        long long asz = static_cast<long long>(active.size());
        bool want_bf = best_first;
        if (asz > node_Nmax)      want_bf = false;
        else if (asz < node_Nmin) want_bf = true;
        if (want_bf != best_first) {
            best_first = want_bf;
            std::make_heap(active.begin(), active.end(), node_cmp);
        }

        std::pop_heap(active.begin(), active.end(), node_cmp);
        Node cur = std::move(active.back());
        active.pop_back();
        ++stats.total_nodes;

        // 出栈时再做一次支配检查（入栈后可能被更优标签支配）
        {
            double cC, cTT; std::uint64_t cMask;
            compute_node_state(cur, D, ST, VT, UT, h, v, cC, cTT, cMask);
            if (dom_is_dominated(cMask, std::make_pair(cC, cTT))) {
                ++stats.dom_pruned_nodes;
                continue;
            }
        }

        // 初步不可行剪枝
        bool bad = false;
        for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = cur.S.begin();
            it != cur.S.end() && !bad; ++it) {
            for (std::size_t ui = 0; ui < initial_infeasible.size(); ++ui) {
                if (std::includes(
                    it->second.begin(),
                    it->second.end(),
                    initial_infeasible[ui].begin(),
                    initial_infeasible[ui].end()))
                {
                    bad = true;
                    break;
                }
            }
        }
        if (bad) {
            ++stats.U_pruned_nodes;
            continue;
        }

        // LB 剪枝
        if (cur.LB >= UB) {
            ++stats.LB_pruned_nodes;
            continue;
        }

        // 叶子节点
        if (all_assigned(cur)) {
            ++stats.leaf_nodes;
            if (cur.LB < UB) {
                UB = cur.LB;
                best = cur;
                ++stats.updated_solutions;
            }
            continue;
        }

        // 展开子节点
        std::vector<Node> kids = generate_children(cur, parts, machine_area, part_areas);
        stats.generated_nodes += kids.size();
        for (std::size_t i = 0; i < kids.size(); ++i) {
            kids[i].LB = compute_total_lower_bound(kids[i], parts, D, ST, VT, UT, h, v, part_areas, machine_area);
            if (kids[i].LB >= UB) {
                ++stats.LB_pruned_nodes;
                continue;
            }
            // 支配规则：被已有标签支配/重复则丢弃，否则登记并入栈
            double kC, kTT; std::uint64_t kMask;
            compute_node_state(kids[i], D, ST, VT, UT, h, v, kC, kTT, kMask);
            if (!dom_register(kMask, std::make_pair(kC, kTT))) {
                ++stats.dom_pruned_nodes;
                continue;
            }
            active.push_back(std::move(kids[i]));
            std::push_heap(active.begin(), active.end(), node_cmp);
        }
    }

    return std::make_pair(best, stats);
}


//==========================数据记录================================
namespace fs = std::filesystem;

// 定义全局日志流对象
std::ofstream log_stream;

std::string get_log_filename(const std::string& input_filename) {
    std::string base = fs::path(input_filename).stem().string();  // 提取文件名（不含路径与后缀）
    std::string log_dir = "logs/";
    fs::create_directories(log_dir);  // 创建 logs 目录（若不存在）

    int count = 1;
    std::string log_filename;
    do {
        log_filename = log_dir + base + "_log_" + std::to_string(count) + ".txt";
        count++;
    } while (fs::exists(log_filename));

    return log_filename;
}

void write_utf8_bom(std::ofstream& stream) {
    // 写入 UTF-8 BOM: EF BB BF
    stream << "\xEF\xBB\xBF";
}
