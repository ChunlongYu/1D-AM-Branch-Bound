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
    std::string name;     // 节点名称

    Node();
    Node(const std::unordered_map<int, std::vector<int>>& S_,
        double LB_,
        const std::string& name_ = "N");

    bool operator==(const Node& other) const;

    friend std::ostream& operator<<(std::ostream& os, const Node& node);

    struct Hash {
        std::size_t operator()(const Node& node) const;
    };
};

//std::ostream& operator<<(std::ostream& os, const Node& node);

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
std::vector<Node> generate_children(
    const Node& node,
    const std::vector<int>& parts,
    double machine_area,
    const std::vector<double>& part_areas
);

//=========================下界计算================================
double compute_total_lower_bound(
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
struct Stats {
    int updated_solutions = 0;
    int total_nodes = 0;
    int generated_nodes = 0;
    int area_pruned_nodes = 0;
    int LB_pruned_nodes = 0;
    int U_pruned_nodes = 0;
    int leaf_nodes = 0;
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

//=======================数据记录====================
// 获取日志文件名（避免覆盖）
std::string get_log_filename(const std::string& input_filename);

// 全局日志输出对象
extern std::ofstream log_stream;

// 写入 UTF-8 BOM 到日志文件开头
void write_utf8_bom(std::ofstream& stream);

// 同时输出到控制台和日志文件
template <typename T>
void log_and_cout(const T& msg) {
    std::cout << msg;
    if (log_stream.is_open()) {
        log_stream << msg;
    }
}
#endif // BRANCH_BOUND_H
