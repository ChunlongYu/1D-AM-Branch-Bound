#ifndef SCHEDULER_H
#define SCHEDULER_H

#include <vector>
#include <string>
#include <map>

// 机器信息结构体
struct MachineInfo {
    int id;
    int num;
    double scanning_speed;
    double recoater_speed;
    double setup_time;
    double length, width, height;
};

// 零件信息结构体
struct PartInfo {
    int id;
    int num;
    double volume;
    double length, width, height, support;
};

// 拆分零件属性的结构体
struct PartLists {
    std::vector<double> volumes;
    std::vector<double> lengths;
    std::vector<double> widths;
    std::vector<double> heights;
    std::vector<double> supports;
};

// 读取机器和零件数据
bool readMachineAndParts(
    const std::string& filename,
    MachineInfo& machine,
    std::vector<PartInfo>& parts,
    PartLists& part_lists
);


#endif // SCHEDULER_H
