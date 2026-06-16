#include "DynamicProgramming.h"
#include <iostream>
#include <limits> // 用于 std::numeric_limits

//==================================================枚举函数实现===============================================

double calculate_total_lateness(const std::vector<Job>& schedule) {
    double current_time = 0.0;
    double total_lateness = 0.0;
    for (const auto& job : schedule) {
        current_time += job.p;
        total_lateness += std::max(0.0, current_time - job.d);
    }
    return total_lateness;
}

std::vector<Job> find_min_lateness_schedule(const std::vector<Job>& jobs) {
    std::vector<Job> current_permutation = jobs;
    std::vector<Job> best_schedule = jobs;
    double min_lateness = std::numeric_limits<double>::max();

    // 初始排序，确保 std::next_permutation 从第一个排列开始
    // 默认按ID排序，或者根据实际需要
    std::sort(current_permutation.begin(), current_permutation.end(), [](const Job& a, const Job& b) {
        return a.id < b.id;
        });

    do {
        double current_lateness = calculate_total_lateness(current_permutation);
        if (current_lateness < min_lateness) {
            min_lateness = current_lateness;
            best_schedule = current_permutation;
        }
    } while (std::next_permutation(current_permutation.begin(), current_permutation.end(), [](const Job& a, const Job& b) {
        // next_permutation 需要一个 strict weak ordering，这里仍然按ID比较
        return a.id < b.id;
        }));

    return best_schedule;
}


//==================================================随机算例生成实现================================================

std::vector<Job> generate_random_jobs(int num_jobs, double min_p, double max_p, double min_d, double max_d) {
    std::vector<Job> jobs;
    // 使用 std::default_random_engine 和系统时间作为种子
    std::default_random_engine generator(std::chrono::system_clock::now().time_since_epoch().count());

    // 使用 std::uniform_real_distribution 生成浮点数
    std::uniform_real_distribution<double> p_distribution(min_p, max_p);
    std::uniform_real_distribution<double> d_distribution(min_d, max_d);

    for (int i = 0; i < num_jobs; ++i) {
        jobs.push_back({ i + 1, p_distribution(generator), d_distribution(generator) });
    }
    return jobs;
}