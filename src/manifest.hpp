#pragma once
#include <filesystem>
#include <string>
#include <vector>
namespace zml {
struct Manifest {
    std::string id, title;
    std::filesystem::path directory, library;
    bool enabled = true;
    std::string version, description, authors;
    std::vector<std::string> tags;
    std::vector<std::string> depends;
    std::filesystem::path icon, config, config_entry;
    // Schema is shared storage; only one menu renderer may be declared.
    std::string config_menu;
};
std::string config_menu(const Manifest& manifest);
Manifest read_manifest(const std::filesystem::path& file);
std::vector<Manifest> discover(const std::filesystem::path& root);
// Stable dependency-first order; disabled consumers impose no requirements.
std::vector<Manifest> dependency_order(std::vector<Manifest> mods);
bool dependencies_loaded(const Manifest&, const std::vector<std::string>& loaded);
}
