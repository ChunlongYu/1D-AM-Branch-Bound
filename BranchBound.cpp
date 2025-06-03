#include "BranchBound.h"
#include <algorithm>
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
        children.emplace_back(std::move(new_S), 0.0, node.name + std::to_string(max_batch_id + 1));
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
    const std::vector<double>& v
) {
    double time_cursor = 0.0;
    double tard_assigned = 0.0;

    // 已分配
    std::unordered_map<int, double> comp;
    comp.reserve(parts.size());
    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
        it != node.S.end(); ++it) {
        double vol = 0.0, mh = 0.0;
        for (std::size_t j = 0; j < it->second.size(); ++j) {
            vol += v[it->second[j]];
            if (h[it->second[j]] > mh) mh = h[it->second[j]];
        }
        double PT = ST[0] + VT[0] * vol + UT[0] * mh;
        for (std::size_t j = 0; j < it->second.size(); ++j) {
            comp[it->second[j]] = time_cursor + PT;
        }
        time_cursor += PT;
    }
    for (typename std::unordered_map<int, double>::const_iterator it = comp.begin(); it != comp.end(); ++it) {
        tard_assigned += std::max(0.0, it->second - D[it->first]);
    }

    // 未分配并行下界
    double tard_unassigned = 0.0;
    std::unordered_set<int> assigned;
    assigned.reserve(parts.size());
    for (typename std::unordered_map<int, std::vector<int> >::const_iterator it = node.S.begin();
        it != node.S.end(); ++it) {
        for (std::size_t j = 0; j < it->second.size(); ++j) {
            assigned.insert(it->second[j]);
        }
    }
    for (std::size_t i = 0; i < parts.size(); ++i) {
        int p = parts[i];
        if (assigned.find(p) == assigned.end()) {
            double pt = ST[0] + VT[0] * v[p] + UT[0] * h[p];
            double c = time_cursor + pt;
            tard_unassigned += std::max(0.0, c - D[p]);
        }
    }

    return tard_assigned + tard_unassigned;
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
    root.LB = compute_total_lower_bound(root, parts, D, ST, VT, UT, h, v);

    std::deque<Node> stack;
    stack.push_back(root);

    auto t0 = std::chrono::steady_clock::now();

    while (!stack.empty()) {
        auto t1 = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(t1 - t0).count();
        if (time_limit_seconds > 0.0 && elapsed > time_limit_seconds) {
            break;
        }

        Node cur = stack.back();
        stack.pop_back();
        ++stats.total_nodes;

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
            kids[i].LB = compute_total_lower_bound(kids[i], parts, D, ST, VT, UT, h, v);
            if (kids[i].LB < UB) {
                stack.push_back(kids[i]);
                
            }
            else {
                ++stats.LB_pruned_nodes;
            }
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
