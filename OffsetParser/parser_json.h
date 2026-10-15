// parser_json.h
#pragma once
#include <string>
#include <vector>

std::string FindStaticFieldInJSON(const std::string& json, const std::vector<std::string>& possibleNames);