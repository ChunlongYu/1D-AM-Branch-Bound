#include <iostream>
#include <iomanip>
#include <ctime>
#include <chrono>
#include <cstdlib>
#include <fstream>
#include <locale>
#include <codecvt>
#include "InstanceData.h"
#include "BranchBound.h"
#include "ParallelBranchBound.h"

//-------------含初始解的调用-------------------------
int main(int argc, char** argv) {
    std::clock_t start_time = std::clock();
    MachineInfo machine;
    std::vector<PartInfo> parts;
    PartLists part_lists;

    // CLI: <program> [instance_file] [M] [time_limit_sec]
    std::string filename = (argc > 1) ? argv[1] : "data/15part.txt";
    std::string log_filename = get_log_filename(filename);
    log_stream.open(log_filename, std::ios::out | std::ios::binary);

    if (!log_stream.is_open()) {
        std::cerr << "Can't open the log file:" << log_filename << std::endl;
        return 1;
    }
    write_utf8_bom(log_stream);
    if (!readMachineAndParts(filename, machine, parts, part_lists)) {
        log_and_cout("fail to read the Instance.\n");
        return 1;
    }


    // 构建零件索引
    std::vector<int> part_indices;
    for (size_t i = 0; i < parts.size(); ++i) {
        part_indices.push_back(static_cast<int>(i));
    }

    // 截止时间：从实例文件末尾的 DueDate 段读取
    std::vector<double> due_dates = part_lists.due_dates;
    if (due_dates.size() != parts.size()) {
        log_and_cout("Error: instance file has no/incomplete DueDate section (expected "
            + std::to_string(parts.size()) + " due dates).\n");
        return 1;
    }

    // 机器尺寸
    std::vector<double> L = { machine.length };
    std::vector<double> W = { machine.width };

    // 时间参数
    std::vector<double> ST = { machine.setup_time };
    std::vector<double> VT = { machine.scanning_speed }; // 体积系数，请替换为实际值
    std::vector<double> UT = { machine.recoater_speed }; // 高度系数，请替换为实际值

    // 输出机器信息
    log_and_cout("Machine Info:\n");
    log_and_cout("  ID: " + std::to_string(machine.id) + ", Num: " + std::to_string(machine.num) + "\n");
    log_and_cout("  Scanning speed: " + std::to_string(machine.scanning_speed) + "\n");
    log_and_cout("  Recoater speed: " + std::to_string(machine.recoater_speed) + "\n");
    log_and_cout("  Setup time: " + std::to_string(machine.setup_time) + "\n");
    log_and_cout("  Size: " + std::to_string(machine.length) + " x "
        + std::to_string(machine.width) + " x "
        + std::to_string(machine.height) + "\n\n");

    log_and_cout("Parts Summary:\n");
    for (size_t i = 0; i < part_lists.volumes.size(); ++i) {
        log_and_cout("  Part " + std::to_string(i) + ": "
            + std::to_string(part_lists.lengths[i]) + " x "
            + std::to_string(part_lists.widths[i]) + " x "
            + std::to_string(part_lists.heights[i]) + ", Vol = "
            + std::to_string(part_lists.volumes[i]) + ", Support = "
            + std::to_string(part_lists.supports[i]) + "\n");
    }

    // 调用初始解生成函数
    std::pair<BatchMap, double> result = generateInitialSolution(
        part_indices, L, W,
        part_lists.lengths,
        part_lists.widths,
        part_lists.volumes,
        part_lists.heights,
        due_dates,
        ST, VT, UT
    );

    log_and_cout("\nSingle-machine EDD initial Total Tardiness (info only): "
        + std::to_string(result.second) + "\n");

    // ================== 并行机分支定界（上层算法）==================
    // 机器数量 M 与时间限制：命令行可传，缺省 M=2、1800s
    int M = (argc > 2) ? std::atoi(argv[2]) : 2;
    double time_limit = (argc > 3) ? std::atof(argv[3]) : 1800.0;

    PBBParams pbb;
    pbb.M = M;
    pbb.time_limit = time_limit;             // 全局时间限制（秒），<=0 表示不限
    pbb.strong_branch_candidates = 8;        // strong-branching 候选件数（<=0 全评）
    pbb.dfs_warmup_improvements = 1;          // 初始深度优先阶段：改进次数阈值
    pbb.N_max = 200000;                       // 活动节点超过则转深度优先
    pbb.N_min = 50000;                        // 活动节点低于则转最优优先

    log_and_cout("\n============== Parallel-Machine Branch and Bound ==============\n");
    log_and_cout("Number of machines M = " + std::to_string(M) + "\n\n");

    auto wall0 = std::chrono::steady_clock::now();
    std::pair<PBBSolution, PBBStats> pres = solveParallelMachine(
        part_indices,
        due_dates,
        ST, VT, UT,
        L, W,
        part_lists.lengths,
        part_lists.widths,
        part_lists.heights,
        part_lists.volumes,
        pbb
    );

    const PBBSolution& psol = pres.first;
    const PBBStats& pstats = pres.second;
    double wall_seconds = std::chrono::duration<double>(std::chrono::steady_clock::now() - wall0).count();

    log_and_cout("\n=============== Best Parallel Assignment ===============\n");
    for (int m = 0; m < M; ++m) {
        std::string line = "  Machine " + std::to_string(m) + " : [";
        if (m < (int)psol.assign.size()) {
            for (size_t i = 0; i < psol.assign[m].size(); ++i) {
                line += std::to_string(psol.assign[m][i]);
                if (i + 1 != psol.assign[m].size()) line += ", ";
            }
        }
        line += "]  -> Phi = ";
        line += (m < (int)psol.machine_tardiness.size())
                    ? std::to_string(psol.machine_tardiness[m]) : "0";
        log_and_cout(line + "\n");
    }
    log_and_cout("\nTotal Tardiness (UB): " + std::to_string(psol.total_tardiness) + "\n");
    log_and_cout(std::string("Proven optimal: ")
                 + (psol.proven_optimal ? "yes" : "no (time limit reached)") + "\n");

    log_and_cout("\n============= Parallel Search Info =============\n");
    log_and_cout("nodes processed:      " + std::to_string(pstats.total_nodes) + "\n");
    log_and_cout("nodes generated:      " + std::to_string(pstats.generated_nodes) + "\n");
    log_and_cout("leaf nodes evaluated: " + std::to_string(pstats.leaf_nodes) + "\n");
    log_and_cout("incumbent updates:    " + std::to_string(pstats.updated_solutions) + "\n");
    log_and_cout("LB-pruned nodes:      " + std::to_string(pstats.lb_pruned_nodes) + "\n");
    log_and_cout("oracle calls:         " + std::to_string(pstats.oracle_calls) + "\n");
    log_and_cout("oracle cache hits:    " + std::to_string(pstats.oracle_cache_hits) + "\n");

    double elapsed_seconds = double(std::clock() - start_time) / CLOCKS_PER_SEC;
    log_and_cout("time:" + std::to_string(elapsed_seconds) + "s\n");

    // machine-parseable result line for batch comparison
    double _gap = (psol.total_tardiness>1e-9)?(psol.total_tardiness-pstats.global_lb)/psol.total_tardiness*100.0:0.0;
    std::cout << "RESULT instance=" << filename
              << " n=" << parts.size()
              << " M=" << M
              << " TT=" << psol.total_tardiness
              << " optimal=" << (psol.proven_optimal ? 1 : 0)
              << " time=" << wall_seconds
              << " lb=" << pstats.global_lb
              << " gap=" << _gap
              << " nodes=" << pstats.total_nodes
              << " oracle=" << pstats.oracle_calls
              << std::endl;

    log_stream.close();
    return 0;
}

