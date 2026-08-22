// utils.h
#pragma once
#include <string>
#include <vector>

struct StaticField {
    std::string name;
    std::string offset;
};

std::string ReadFileToString(const std::string& filename);
void WriteOffsets(const std::string& localPlayer, const std::string& clientEntities,
    const std::string& viewMatrix, const std::string& health,
    const std::string& position, const std::string& bones);