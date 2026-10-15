// parser_dump.cpp
#include "parser_dump.h"
#include <regex>
#include <string>

std::vector<StaticField> FindAllStaticFieldsInDump(const std::string& dump) {
    std::vector<StaticField> fields;
    std::regex patterns[] = {
        std::regex("static\\s+\\w+\\s+(\\w+)\\s*;\\s*//\\s*0x([0-9a-fA-F]+)"),
        std::regex("static\\s+\\w+\\s+(\\w+)\\s*=\\s*0x([0-9a-fA-F]+)"),
        std::regex("static\\s+\\w+\\s+(\\w+)\\s*;\\s*/\\*\\s*0x([0-9a-fA-F]+)\\s*\\*/")
    };
    std::smatch match;
    for (const auto& pattern : patterns) {
        std::string::const_iterator start = dump.cbegin();
        while (std::regex_search(start, dump.cend(), match, pattern)) {
            StaticField sf;
            sf.name = match[1].str();
            sf.offset = "0x" + match[2].str();
            fields.push_back(sf);
            start = match.suffix().first;
        }
    }
    return fields;
}

std::string FindStaticFieldInDump(const std::string& dump, const std::vector<std::string>& possibleNames) {
    auto allFields = FindAllStaticFieldsInDump(dump);
    for (const auto& name : possibleNames) {
        for (const auto& field : allFields) {
            if (field.name == name) {
                return field.offset;
            }
        }
    }
    return "0x0";
}

std::string FindFieldInDump(const std::string& dump, const std::vector<std::string>& possibleNames) {
    std::regex patterns[] = {
        std::regex("\\b(\\w+)\\b.*?//\\s*0x([0-9a-fA-F]+)"),
        std::regex("\\b(\\w+)\\s*=\\s*0x([0-9a-fA-F]+)")
    };
    std::smatch match;
    for (const auto& name : possibleNames) {
        for (const auto& pattern : patterns) {
            std::string::const_iterator start = dump.cbegin();
            while (std::regex_search(start, dump.cend(), match, pattern)) {
                if (match[1].str() == name) {
                    return "0x" + match[2].str();
                }
                start = match.suffix().first;
            }
        }
    }
    return "0x0";
}