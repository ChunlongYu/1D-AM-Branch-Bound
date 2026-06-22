// =============================================================================
//  run_exp_BB.cpp  —— B&B 批量实验主程序
//  遍历 INSTANCES × M(MACHINES),逐条把结果写入 results/bb_results.csv(即时 flush)。
//
//  编译(在仓库根目录):
//    g++ -std=c++17 -O2 -o run_exp_BB run_exp_BB.cpp BranchBound.cpp InstanceData.cpp ParallelBranchBound.cpp
//  运行:
//    ./run_exp_BB            (Windows: run_exp_BB.exe)
//
//  注意:这是独立 main,不要和 main.cpp 一起编译(两个 main 会冲突)。
// =============================================================================
#include "InstanceData.h"
#include "BranchBound.h"
#include "ParallelBranchBound.h"
#include <iostream>
#include <fstream>
#include <vector>
#include <string>
#include <chrono>
#include <filesystem>

// ---- 实验配置(按需修改)---------------------------------------------------
static const double      TIME_LIMIT = 1800.0;          // 每次运行的时间限制(秒)
static const std::vector<int> MACHINES = {2, 3, 4};    // 机器数 M
static const std::vector<std::string> INSTANCES = {    // 实例(Instance/ 下,不含 .txt)
    "5part",
    "10part", "10part_2", "10part_3", "10parts_4",
    "11part", "12part", "13part", "14part",
    "15part", "15part_2-S",
    "20part_3-S", "20part_4-S"
};
// ---------------------------------------------------------------------------

int main() {
    namespace fs = std::filesystem;
    fs::create_directories("experiments/results");
    std::ofstream csv("experiments/results/bb_results.csv", std::ios::out);
    csv << "instance,n,M,TT,optimal,time_sec,nodes,oracle_calls,oracle_hits\n";
    csv.flush();

    std::cout << "B&B batch experiment  (time limit " << TIME_LIMIT << "s)\n";
    std::cout << "instance        n   M        TT  opt     time(s)     nodes   oracle\n";
    std::cout << "------------------------------------------------------------------------\n";

    for (const std::string& inst : INSTANCES) {
        std::string path = "data/" + inst + ".txt";
        MachineInfo machine;
        std::vector<PartInfo> parts;
        PartLists pl;
        if (!readMachineAndParts(path, machine, parts, pl)) {
            std::cerr << "skip (cannot read): " << path << "\n";
            continue;
        }
        if (pl.due_dates.size() != parts.size()) {
            std::cerr << "skip (no/incomplete DueDate): " << path << "\n";
            continue;
        }
        std::vector<int> partIdx;
        for (size_t i = 0; i < parts.size(); ++i) partIdx.push_back((int)i);
        std::vector<double> L = {machine.length}, W = {machine.width};
        std::vector<double> ST = {machine.setup_time},
                            VT = {machine.scanning_speed},
                            UT = {machine.recoater_speed};

        for (int M : MACHINES) {
            PBBParams p;
            p.M = M;
            p.time_limit = TIME_LIMIT;
            p.strong_branch_candidates = 8;
            p.dfs_warmup_improvements  = 1;
            p.N_max = 200000;
            p.N_min = 50000;

            auto t0 = std::chrono::steady_clock::now();
            auto res = solveParallelMachine(partIdx, pl.due_dates, ST, VT, UT,
                                            L, W, pl.lengths, pl.widths,
                                            pl.heights, pl.volumes, p);
            double sec = std::chrono::duration<double>(
                std::chrono::steady_clock::now() - t0).count();

            const PBBSolution& s = res.first;
            const PBBStats&    st = res.second;
            int opt = s.proven_optimal ? 1 : 0;

            // CSV(即时写 + flush)
            csv << inst << "," << parts.size() << "," << M << ","
                << s.total_tardiness << "," << opt << ","
                << sec << "," << st.total_nodes << ","
                << st.oracle_calls << "," << st.oracle_cache_hits << "\n";
            csv.flush();

            // 控制台
            char line[256];
            std::snprintf(line, sizeof(line),
                "%-14s %3zu %3d %9.3f %4d %11.3f %9lld %8lld\n",
                inst.c_str(), parts.size(), M, s.total_tardiness, opt, sec,
                (long long)st.total_nodes, (long long)st.oracle_calls);
            std::cout << line << std::flush;
        }
    }
    csv.close();
    std::cout << "\nDone. Results -> results/bb_results.csv\n";
    return 0;
}
