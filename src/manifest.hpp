#pragma once
#include <filesystem>
#include <string>
#include <vector>
namespace zml {
struct Manifest {
    std::string id, title;
    std::filesystem::path directory, library;
    bool enabled = true;
};
Manifest read_manifest(const std::filesystem::path& file);
std::vector<Manifest> discover(const std::filesystem::path& root);
}
