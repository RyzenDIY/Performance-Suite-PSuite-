// main.cpp
#include <windows.h>   // Для SetConsoleOutputCP, CP_UTF8
#include <iostream>
#include <string>
#include <vector>
#include "utils.h"
#include "parser_dump.h"
#include "parser_json.h"
#include "parser_memory.h"

int main(int argc, char* argv[]) {
    SetConsoleOutputCP(CP_UTF8);  // Теперь работает
    std::cout << "=== RustVision Offset Parser (модульный) ===" << std::endl;

    // ---- Параметры командной строки ----
    bool useDump = true, useJson = true, useMemory = true;
    if (argc > 1) {
        std::string mode = argv[1];
        if (mode == "--dump") { useJson = false; useMemory = false; }
        else if (mode == "--json") { useDump = false; useMemory = false; }
        else if (mode == "--memory") { useDump = false; useJson = false; }
        else if (mode == "--auto") { /* всё включено */ }
        else {
            std::cout << "Использование: OffsetParser.exe [--dump | --json | --memory | --auto]" << std::endl;
            return 1;
        }
    }

    // ---- Загрузка файлов ----
    std::string dump = useDump ? ReadFileToString("dump.cs") : "";
    std::string json = useJson ? ReadFileToString("script.json") : "";

    if (dump.empty() && json.empty() && !useMemory) {
        std::cout << "[ERROR] Нет dump.cs или script.json, режим памяти отключён." << std::endl;
        system("pause");
        return 1;
    }

    // ---- Переменные ----
    std::string localPlayer = "0x0";
    std::string clientEntities = "0x0";
    std::string viewMatrix = "0x0";
    std::string health = "0x0";
    std::string position = "0x0";
    std::string bones = "0x0";

    // ---- ЭТАП 1: dump.cs ----
    if (!dump.empty()) {
        std::cout << "[INFO] Анализ dump.cs..." << std::endl;
        std::vector<std::string> localNames = { "clientLocalPlayer", "LocalPlayer", "m_LocalPlayer", "_localPlayer" };
        std::vector<std::string> entityNames = { "clientEntities", "entityList", "m_entityList", "_entityList" };
        std::vector<std::string> viewNames = { "viewMatrix", "m_ViewMatrix", "ViewMatrix", "_viewMatrix" };

        localPlayer = FindStaticFieldInDump(dump, localNames);
        clientEntities = FindStaticFieldInDump(dump, entityNames);
        viewMatrix = FindStaticFieldInDump(dump, viewNames);
        health = FindFieldInDump(dump, { "_health", "health" });
        position = FindFieldInDump(dump, { "_position", "position" });
        bones = FindFieldInDump(dump, { "_bones", "bones" });
    }

    // ---- ЭТАП 2: script.json ----
    if ((localPlayer == "0x0" || clientEntities == "0x0" || viewMatrix == "0x0") && !json.empty()) {
        std::cout << "[INFO] Анализ script.json..." << std::endl;
        std::vector<std::string> localNames = { "clientLocalPlayer", "LocalPlayer", "m_LocalPlayer", "_localPlayer" };
        std::vector<std::string> entityNames = { "clientEntities", "entityList", "m_entityList", "_entityList" };
        std::vector<std::string> viewNames = { "viewMatrix", "m_ViewMatrix", "ViewMatrix", "_viewMatrix" };

        if (localPlayer == "0x0") localPlayer = FindStaticFieldInJSON(json, localNames);
        if (clientEntities == "0x0") clientEntities = FindStaticFieldInJSON(json, entityNames);
        if (viewMatrix == "0x0") viewMatrix = FindStaticFieldInJSON(json, viewNames);
        if (health == "0x0") health = FindStaticFieldInJSON(json, { "_health", "health" });
        if (position == "0x0") position = FindStaticFieldInJSON(json, { "_position", "position" });
        if (bones == "0x0") bones = FindStaticFieldInJSON(json, { "_bones", "bones" });
    }

    // ---- ЭТАП 3: память ----
    if ((localPlayer == "0x0" || clientEntities == "0x0" || viewMatrix == "0x0") && useMemory) {
        std::cout << "[INFO] Поиск в памяти..." << std::endl;
        std::string memResult = FindOffsetsInMemory();
        if (!memResult.empty()) {
            size_t pos1 = memResult.find(',');
            size_t pos2 = memResult.find(',', pos1 + 1);
            if (pos1 != std::string::npos && pos2 != std::string::npos) {
                localPlayer = memResult.substr(0, pos1);
                clientEntities = memResult.substr(pos1 + 1, pos2 - pos1 - 1);
                viewMatrix = memResult.substr(pos2 + 1);
            }
        }
    }

    // ---- Вывод результатов ----
    std::cout << "\n[РЕЗУЛЬТАТ]" << std::endl;
    std::cout << "LocalPlayer: " << localPlayer << (localPlayer == "0x0" ? " (НЕ НАЙДЕНО)" : "") << std::endl;
    std::cout << "ClientEntities: " << clientEntities << (clientEntities == "0x0" ? " (НЕ НАЙДЕНО)" : "") << std::endl;
    std::cout << "ViewMatrix: " << viewMatrix << (viewMatrix == "0x0" ? " (НЕ НАЙДЕНО)" : "") << std::endl;
    std::cout << "Health: " << health << std::endl;
    std::cout << "Position: " << position << std::endl;
    std::cout << "Bones: " << bones << std::endl;

    // ---- Проверка ----
    if (localPlayer == "0x0" || clientEntities == "0x0" || viewMatrix == "0x0") {
        std::cout << "\n[ERROR] Не удалось найти глобальные офсеты." << std::endl;
        std::cout << "Попробуйте:\n"
            << "1. Убедитесь, что у вас есть актуальный dump.cs или script.json.\n"
            << "2. Запустите игру и используйте параметр --memory.\n"
            << "3. Или найдите офсеты вручную через Cheat Engine." << std::endl;
        system("pause");
        return 1;
    }

    WriteOffsets(localPlayer, clientEntities, viewMatrix, health, position, bones);
    std::cout << "\n[SUCCESS] offsets.ini создан." << std::endl;
    system("pause");
    return 0;
}