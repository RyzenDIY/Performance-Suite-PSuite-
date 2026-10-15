// parser_dump.h
#pragma once
#include <string>
#include <vector>
#include "utils.h"

std::vector<StaticField> FindAllStaticFieldsInDump(const std::string& dump);
std::string FindStaticFieldInDump(const std::string& dump, const std::vector<std::string>& possibleNames);
std::string FindFieldInDump(const std::string& dump, const std::vector<std::string>& possibleNames);