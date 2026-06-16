#include "DataRecord.h"

//==========================数据记录================================
namespace fs = std::filesystem;

// 定义全局日志流对象
std::ofstream log_stream;

std::string get_log_filename(const std::string& input_filename) {
    std::string base = fs::path(input_filename).stem().string();  // 提取文件名（不含路径与后缀）
    std::string log_dir = "NodeGene_stru1/";
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


//=================================bound数据记录============================
void export_bound_statistics(const std::string& log_filename, const Stats& stats) {
    std::string bound_csv = log_filename;
    size_t pos = bound_csv.find_last_of(".");
    if (pos != std::string::npos) {
        bound_csv = bound_csv.substr(0, pos);
    }
    bound_csv += "_bounds.csv";

    std::ofstream out(bound_csv);
    if (!out.is_open()) {
        std::cerr << "无法打开文件用于写入 Bound 统计信息: " << bound_csv << std::endl;
        return;
    }

    out << "Time,UB,LB\n";

    size_t i = 0, j = 0;
    double last_ub = -1.0;
    double last_lb = -1.0;

    while (i < stats.UB_updates.size() || j < stats.LB_convergence.size()) {
        double t_ub = (i < stats.UB_updates.size()) ? stats.UB_updates[i].first : std::numeric_limits<double>::infinity();
        double t_lb = (j < stats.LB_convergence.size()) ? stats.LB_convergence[j].first : std::numeric_limits<double>::infinity();

        if (t_ub < t_lb) {
            last_ub = stats.UB_updates[i].second;
            out << std::fixed << std::setprecision(6) << t_ub << "," << last_ub << "," << last_lb << "\n";
            ++i;
        }
        else if (t_lb < t_ub) {
            last_lb = stats.LB_convergence[j].second;
            out << std::fixed << std::setprecision(6) << t_lb << "," << last_ub << "," << last_lb << "\n";
            ++j;
        }
        else {
            last_ub = stats.UB_updates[i].second;
            last_lb = stats.LB_convergence[j].second;
            out << std::fixed << std::setprecision(6) << t_ub << "," << last_ub << "," << last_lb << "\n";
            ++i;
            ++j;
        }
    }

    out.close();
    log_and_cout("上下界记录已导出至: " + bound_csv + "\n");
}
//======================深度剪枝节点数据记录=====================
int export_pruned_depth_info(
    const std::string& log_filename,
    const std::unordered_map<int, int>& pruned_nodes_per_depth
) {
    std::string csv_filename = log_filename;
    size_t pos = csv_filename.find_last_of(".");
    if (pos != std::string::npos) {
        csv_filename = csv_filename.substr(0, pos);
    }
    csv_filename += "_pruned_per_depth.csv";

    std::ofstream csv_file(csv_filename);
    if (!csv_file.is_open()) {
        std::cerr << "无法打开文件用于写入剪枝信息: " << csv_filename << std::endl;
        return 1;
    }

    csv_file << "Depth,PrunedNodes\n";
    for (const auto& [depth, count] : pruned_nodes_per_depth) {
        csv_file << depth << "," << count << "\n";
    }

    csv_file.close();
    std::cout << "剪枝深度信息已导出至: " << csv_filename << std::endl;

    return 0;
}

//================第一层节点数据记录=======================
void export_first_level_lbs(
    const std::string& log_filename,
    const std::vector<std::pair<std::string, double>>& first_level_node_lbs
) {
    std::string csv_filename = log_filename;
    size_t pos = csv_filename.find_last_of(".");
    if (pos != std::string::npos) {
        csv_filename = csv_filename.substr(0, pos);
    }
    csv_filename += "_first_level_lbs.csv"; // 命名方式与其他文件类似

    std::ofstream csv_file(csv_filename);
    if (!csv_file.is_open()) {
        std::cerr << "无法打开文件用于写入第一层子节点 LB 信息: " << csv_filename << std::endl;
        return;
    }

    csv_file << "Node Name,Lower Bound\n"; // CSV 文件头
    for (const auto& entry : first_level_node_lbs) {
        // 使用 std::fixed 和 std::setprecision 保持数值精度
        csv_file << entry.first << "," << std::fixed << std::setprecision(6) << entry.second << "\n";
    }

    csv_file.close();
    log_and_cout("第一层子节点 LB 信息已导出至: " + csv_filename + "\n");
}

//===================第一层节点详细数据记录==================
void export_first_level_detailed_info(
    const std::string& log_filename,
    const std::vector<FirstLevelNodeInfo>& details
) {
    std::string csv_filename = log_filename;
    size_t pos = csv_filename.find_last_of(".");
    if (pos != std::string::npos) {
        csv_filename = csv_filename.substr(0, pos);
    }
    csv_filename += "_first_level_details.csv";

    std::ofstream csv_file(csv_filename);
    if (!csv_file.is_open()) {
        std::cerr << "无法打开文件用于写入第一层详细信息: " << csv_filename << std::endl;
        return;
    }

    // 写入 CSV 头
    // Name: 节点名称
    // LB: 下界
    // CompletionTime: 当前批次完成时间
    // UnassignedCount: 未分配零件数量
    // UnassignedParts: 未分配零件ID列表 (以分号分隔)
    csv_file << "Node Name,Lower Bound,Completion Time,Unassigned Count,Unassigned Parts\n";

    for (const auto& info : details) {
        csv_file << info.name << ","
            << std::fixed << std::setprecision(6) << info.lb << ","
            << info.completion_time << ","
            << info.unassigned_count << ",";

        // 构建未分配零件的字符串列表，例如 "1;5;9"
        csv_file << "\""; // 开始引号
        for (size_t i = 0; i < info.unassigned_parts.size(); ++i) {
            csv_file << info.unassigned_parts[i];
            if (i != info.unassigned_parts.size() - 1) {
                csv_file << ";";
            }
        }
        csv_file << "\""; // 结束引号

        csv_file << "\n";
    }

    csv_file.close();
    // 假设你有 log_and_cout 函数，或者直接用 std::cout
    std::cout << "第一层子节点详细信息已导出至: " << csv_filename << std::endl;
}