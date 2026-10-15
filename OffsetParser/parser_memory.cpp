// parser_memory.cpp
#include "parser_memory.h"
#include <windows.h>
#include <tlhelp32.h>
#include <psapi.h>
#include <iostream>
#include <vector>
#include <algorithm>
#include <sstream>   // для std::stringstream (если понадобится)

#pragma comment(lib, "psapi.lib")

DWORD GetProcessIdByName(const std::wstring& name) {
    DWORD pid = 0;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe = { sizeof(pe) };
    if (Process32FirstW(snap, &pe)) {
        do {
            if (_wcsicmp(pe.szExeFile, name.c_str()) == 0) {
                pid = pe.th32ProcessID;
                break;
            }
        } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return pid;
}

uintptr_t GetModuleBase(HANDLE process, const std::wstring& moduleName) {
    HMODULE hMods[1024];
    DWORD cbNeeded;
    if (!EnumProcessModules(process, hMods, sizeof(hMods), &cbNeeded)) return 0;
    for (size_t i = 0; i < (cbNeeded / sizeof(HMODULE)); ++i) {
        wchar_t modName[MAX_PATH];
        if (GetModuleBaseNameW(process, hMods[i], modName, MAX_PATH)) {
            if (_wcsicmp(modName, moduleName.c_str()) == 0) {
                return (uintptr_t)hMods[i];
            }
        }
    }
    return 0;
}

bool ReadMemorySafe(HANDLE process, uintptr_t address, void* buffer, size_t size) {
    SIZE_T bytesRead;
    return ReadProcessMemory(process, (LPCVOID)address, buffer, size, &bytesRead) && bytesRead == size;
}

uintptr_t AOBScanMemory(HANDLE process, uintptr_t moduleBase, const std::string& pattern, const std::string& mask = "") {
    if (!process || !moduleBase) return 0;
    MODULEINFO modInfo;
    if (!GetModuleInformation(process, (HMODULE)moduleBase, &modInfo, sizeof(modInfo))) {
        modInfo.SizeOfImage = 0x4000000;
    }
    std::vector<uint8_t> patternBytes;
    std::string maskStr = mask.empty() ? std::string(pattern.length() / 2 - pattern.length() / 4, 'x') : mask;
    std::string hex = pattern;
    hex.erase(std::remove(hex.begin(), hex.end(), ' '), hex.end());
    for (size_t i = 0; i < hex.length(); i += 2) {
        if (hex[i] == '?' && hex[i + 1] == '?') {
            patternBytes.push_back(0);
        }
        else {
            patternBytes.push_back((uint8_t)std::stoi(hex.substr(i, 2), nullptr, 16));
        }
    }
    size_t patternLen = patternBytes.size();
    if (patternLen == 0) return 0;

    const size_t blockSize = 0x100000;
    uintptr_t currentAddr = moduleBase;
    uintptr_t endAddr = moduleBase + modInfo.SizeOfImage;

    while (currentAddr < endAddr) {
        size_t readSize = (std::min)(blockSize, (size_t)(endAddr - currentAddr));
        if (readSize == 0) break;
        std::vector<uint8_t> buffer(readSize);
        SIZE_T bytesRead;
        if (!ReadProcessMemory(process, (LPCVOID)currentAddr, buffer.data(), readSize, &bytesRead) || bytesRead == 0) {
            currentAddr += readSize;
            continue;
        }
        for (size_t i = 0; i < bytesRead - patternLen + 1; ++i) {
            bool found = true;
            for (size_t j = 0; j < patternLen; ++j) {
                if (maskStr[j] == 'x' && buffer[i + j] != patternBytes[j]) {
                    found = false;
                    break;
                }
            }
            if (found) {
                return currentAddr + i;
            }
        }
        currentAddr += readSize;
    }
    return 0;
}

std::string FindOffsetsInMemory() {
    DWORD pid = GetProcessIdByName(L"RustClient.exe");
    if (!pid) return "";

    HANDLE process = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, FALSE, pid);
    if (!process) return "";

    uintptr_t gameAssemblyBase = GetModuleBase(process, L"GameAssembly.dll");
    if (!gameAssemblyBase) {
        CloseHandle(process);
        return "";
    }

    std::cout << "[MEM] База GameAssembly.dll: 0x" << std::hex << gameAssemblyBase << std::dec << std::endl;

    uintptr_t localPtr = AOBScanMemory(process, gameAssemblyBase, "48 8B 0D ?? ?? ?? ?? 48 89 01", "xxx????xxx");
    std::string localPlayer = "0x0";
    if (localPtr) {
        int32_t offset = 0;
        if (ReadMemorySafe(process, localPtr + 3, &offset, 4)) {
            uintptr_t addr = localPtr + 7 + offset;
            localPlayer = "0x" + std::to_string(addr - gameAssemblyBase);
            std::cout << "[MEM] LocalPlayer найден: " << localPlayer << std::endl;
        }
    }

    uintptr_t entPtr = AOBScanMemory(process, gameAssemblyBase, "48 8B 0D ?? ?? ?? ?? 48 8B 01 FF 50 28", "xxx????xxx");
    std::string clientEntities = "0x0";
    if (entPtr) {
        int32_t offset = 0;
        if (ReadMemorySafe(process, entPtr + 3, &offset, 4)) {
            uintptr_t addr = entPtr + 7 + offset;
            clientEntities = "0x" + std::to_string(addr - gameAssemblyBase);
            std::cout << "[MEM] ClientEntities найден: " << clientEntities << std::endl;
        }
    }

    uintptr_t viewPtr = AOBScanMemory(process, gameAssemblyBase, "48 8B 0D ?? ?? ?? ?? 48 8B 01 FF 50 20", "xxx????xxx");
    std::string viewMatrix = "0x0";
    if (viewPtr) {
        int32_t offset = 0;
        if (ReadMemorySafe(process, viewPtr + 3, &offset, 4)) {
            uintptr_t addr = viewPtr + 7 + offset;
            viewMatrix = "0x" + std::to_string(addr - gameAssemblyBase);
            std::cout << "[MEM] ViewMatrix найден: " << viewMatrix << std::endl;
        }
    }

    CloseHandle(process);

    if (localPlayer != "0x0" && clientEntities != "0x0" && viewMatrix != "0x0") {
        return localPlayer + "," + clientEntities + "," + viewMatrix;
    }
    return "";
}