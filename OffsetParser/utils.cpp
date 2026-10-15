// utils.cpp
#include "utils.h"
#include <fstream>
#include <iostream>

std::string ReadFileToString(const std::string& filename) {
    std::ifstream file(filename, std::ios::binary);
    if (!file.is_open()) return "";
    file.seekg(0, std::ios::end);
    size_t size = file.tellg();
    file.seekg(0, std::ios::beg);
    std::string content(size, '\0');
    file.read(&content[0], size);
    file.close();
    return content;
}

void WriteOffsets(const std::string& localPlayer, const std::string& clientEntities,
    const std::string& viewMatrix, const std::string& health,
    const std::string& position, const std::string& bones) {
    std::ofstream file("offsets.ini");
    if (!file.is_open()) {
        std::cout << "[ERROR] Не вдалося створити offsets.ini" << std::endl;
        return;
    }
    file << "[Global]\n";
    file << "LocalPlayer = " << localPlayer << "\n";
    file << "ClientEntities = " << clientEntities << "\n";
    file << "ViewMatrix = " << viewMatrix << "\n";
    file << "\n[Field]\n";
    file << "Health = " << health << "\n";
    file << "Position = " << position << "\n";
    file << "Bones = " << bones << "\n";
    file.close();
    std::cout << "[SUCCESS] offsets.ini створено." << std::endl;
}