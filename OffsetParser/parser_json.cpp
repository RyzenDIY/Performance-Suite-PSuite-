// parser_json.cpp
#include "parser_json.h"
#include <regex>
#include <algorithm>
#include <sstream>   // для std::stringstream
#include <iomanip>   // для std::hex

std::string FindStaticFieldInJSON(const std::string& json, const std::vector<std::string>& possibleNames) {
    for (const auto& fieldName : possibleNames) {
        size_t fieldPos = json.find("\"" + fieldName + "\"");
        while (fieldPos != std::string::npos) {
            size_t staticPos = json.find("\"isStatic\"", fieldPos);
            if (staticPos != std::string::npos && staticPos - fieldPos < 500) {
                size_t truePos = json.find("true", staticPos);
                if (truePos != std::string::npos && truePos - staticPos < 50) {
                    size_t offsetPos = json.find("\"offset\"", fieldPos);
                    if (offsetPos != std::string::npos) {
                        size_t colonPos = json.find(":", offsetPos);
                        if (colonPos != std::string::npos) {
                            size_t start = json.find_first_not_of(" \t\n\r", colonPos + 1);
                            if (start != std::string::npos) {
                                size_t end = json.find_first_of(",}", start);
                                if (end != std::string::npos) {
                                    std::string offsetStr = json.substr(start, end - start);
                                    offsetStr.erase(remove(offsetStr.begin(), offsetStr.end(), ' '), offsetStr.end());
                                    if (offsetStr.find("0x") == 0 || offsetStr.find("0X") == 0)
                                        return offsetStr;
                                    else {
                                        try {
                                            uint64_t val = std::stoull(offsetStr);
                                            std::stringstream ss;
                                            ss << "0x" << std::hex << val;
                                            return ss.str();
                                        }
                                        catch (...) { return "0x0"; }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            fieldPos = json.find("\"" + fieldName + "\"", fieldPos + 1);
        }
    }
    return "0x0";
}