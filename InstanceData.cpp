#include "InstanceData.h"
#include <fstream>
#include <iostream>
#include <sstream>
#include <algorithm>
#include <numeric>
#include <random>

// ----------- 读取数据函数实现 -----------

bool readMachineAndParts(
    const std::string& filename,
    MachineInfo& machine,
    std::vector<PartInfo>& parts,
    PartLists& part_lists
) {
    std::ifstream file(filename);
    if (!file.is_open()) {
        std::cerr << "Cannot open file: " << filename << std::endl;
        return false;
    }

    int types_machine, types_parts;
    int num_machine, num_parts;
    file >> types_machine >> types_parts;
    file >> num_machine >> num_parts;

    std::string dummy;
    std::getline(file, dummy);
    std::getline(file, dummy);

    // 读取机器信息
    file >> machine.id >> machine.num >> machine.scanning_speed >> machine.recoater_speed
        >> machine.setup_time >> machine.length >> machine.width >> machine.height;

    std::getline(file, dummy);
    std::getline(file, dummy);

    parts.clear();
    part_lists = {}; // 清空所有 vector

    for (int i = 0; i < num_parts; ++i) {
        PartInfo part;
        int orientations;
        file >> part.id >> part.num >> orientations >> part.volume;

        if (orientations != 1) {
            std::cerr << "Warning: Part " << part.id << " has " << orientations << " orientations, only the first is used.\n";
        }

        file >> part.length >> part.width >> part.height >> part.support;

        // 加入主列表
        parts.push_back(part);

        // 填入每个属性向量
        part_lists.volumes.push_back(part.volume);
        part_lists.lengths.push_back(part.length);
        part_lists.widths.push_back(part.width);
        part_lists.heights.push_back(part.height);
        part_lists.supports.push_back(part.support);
    }

    return true;
}

