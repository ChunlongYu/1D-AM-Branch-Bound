#ifndef DADA_RECORD_H
#define DADA_RECORD_H

#include <string>
#include <utility>
#include <ostream>
#include <fstream>
#include <filesystem>
#include <algorithm>
#include <chrono>
#include <limits>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "BranchBound.h"

//=======================文档数据记录====================
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

//================================
void export_bound_statistics(const std::string& log_filename, const Stats& stats);

//===============
int export_pruned_depth_info(
    const std::string& log_filename,
    const std::unordered_map<int, int>& pruned_nodes_per_depth
);

void export_first_level_lbs(const std::string& log_filename, const std::vector<std::pair<std::string, double>>& first_level_node_lbs);
void export_first_level_detailed_info(
    const std::string& log_filename,
    const std::vector<FirstLevelNodeInfo>& details
);
#endif DATA_RECORD_H
