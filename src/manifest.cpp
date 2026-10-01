#include "manifest.hpp"
#include "win_util.hpp"
#include <algorithm>
#include <fstream>
#include <map>
#include <set>
namespace zml {
namespace {
std::string trim(std::string s) {
    auto start = s.find_first_not_of(" \t\r\n"), end = s.find_last_not_of(" \t\r\n");
    return start == std::string::npos ? "" : s.substr(start, end - start + 1);
}
}
Manifest read_manifest(const std::filesystem::path& file) {
    std::ifstream stream(file, std::ios::binary);
    if (!stream) throw std::runtime_error("Cannot read manifest: " + utf8(file.wstring()));
    std::map<std::string, std::string> values;
    std::string section, line;
    size_t total = 0;
    while (std::getline(stream, line)) {
        if ((total += line.size()) > 16384) throw std::runtime_error("Manifest exceeds 16 KiB");
        line = trim(line);
        if (line.empty() || line.front() == '#' || line.front() == ';') continue;
        if (line.front() == '[' && line.back() == ']') { section = line.substr(1, line.size() - 2); continue; }
        if (section != "mod") throw std::runtime_error("Unknown manifest section");
        auto eq = line.find('=');
        if (eq == std::string::npos) throw std::runtime_error("Invalid manifest line");
        auto key = trim(line.substr(0, eq));
        if (key != "id" && key != "name" && key != "library" && key != "enabled" && key != "api")
            throw std::runtime_error("Unknown manifest key: " + key);
        if (!values.emplace(key, trim(line.substr(eq + 1))).second) throw std::runtime_error("Duplicate manifest key");
    }
    if (values["api"] != "1") throw std::runtime_error("Unsupported mod API");
    auto id = values["id"];
    if (id.empty() || id.size() > 96 || id == "." || id == ".." || id.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789.-_") != std::string::npos)
        throw std::runtime_error("Invalid mod id");
    auto library = path_utf8(values["library"]);
    if (library.empty() || library.has_parent_path() || library.extension() != L".dll")
        throw std::runtime_error("library must be a local .dll filename");
    if (values["enabled"] != "true" && values["enabled"] != "false") throw std::runtime_error("enabled must be true/false");
    Manifest m{id, values["name"], std::filesystem::canonical(file.parent_path()), {}, values["enabled"] == "true"};
    m.library = m.directory / library;
    if (!inside(m.directory, m.library)) throw std::runtime_error("Library escapes mod directory");
    return m;
}
std::vector<Manifest> discover(const std::filesystem::path& root) {
    std::vector<Manifest> mods;
    if (!std::filesystem::exists(root)) return mods;
    std::set<std::string> ids;
    for (auto& entry : std::filesystem::directory_iterator(root)) {
        if (!entry.is_directory()) continue;
        auto ini = entry.path() / L"mod.ini";
        if (!std::filesystem::exists(ini)) continue;
        if (!inside(root, ini)) throw std::runtime_error("Mod directory escapes package root");
        auto m = read_manifest(ini);
        if (!ids.insert(m.id).second) throw std::runtime_error("Duplicate mod id: " + m.id);
        mods.push_back(std::move(m));
    }
    std::sort(mods.begin(), mods.end(), [](const auto& a, const auto& b) { return a.id < b.id; });
    return mods;
}
}
