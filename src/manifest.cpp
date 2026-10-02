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
std::string config_menu(const Manifest& m) {
    auto mode = m.config_menu;
    if (mode.empty()) {
        if (!m.config.empty() && !m.config_entry.empty())
            throw std::runtime_error("Ambiguous config menus: select config_menu=custom (schema is storage only)");
        mode = !m.config_entry.empty() ? "custom" : !m.config.empty() ? "standard" : "none";
    }
    if (mode == "standard" && !m.config.empty() && m.config_entry.empty()) return mode;
    if (mode == "custom" && !m.config_entry.empty()) return mode;
    if (mode == "none" && m.config.empty() && m.config_entry.empty()) return mode;
    throw std::runtime_error("Invalid/conflicting config menu declaration");
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
        if (line.front() == '[' && line.back() == ']') {
            section = line.substr(1, line.size() - 2);
            if (section != "mod") throw std::runtime_error("Unknown manifest section");
            continue;
        }
        if (section != "mod") throw std::runtime_error("Unknown manifest section");
        auto eq = line.find('=');
        if (eq == std::string::npos) throw std::runtime_error("Invalid manifest line");
        auto key = trim(line.substr(0, eq));
        if (key != "id" && key != "name" && key != "library" && key != "enabled" && key != "api" &&
            key != "version" && key != "description" && key != "authors" && key != "tags" &&
            key != "icon" && key != "config" && key != "config_entry" && key != "config_menu" && key != "depends")
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
    Manifest m;
    m.id = id; m.title = values["name"].empty() ? id : values["name"];
    m.directory = std::filesystem::canonical(file.parent_path()); m.enabled = values["enabled"] == "true";
    m.library = m.directory / library;
    if (!inside(m.directory, m.library)) throw std::runtime_error("Library escapes mod directory");
    m.version = values["version"]; m.description = values["description"]; m.authors = values["authors"];
    auto depIt = values.find("depends");
    auto deps = depIt==values.end()?std::string{}:depIt->second;
    if (depIt!=values.end() && deps.empty()) throw std::runtime_error("Empty dependency declaration");
    for (size_t at = 0; at < deps.size();) {
        auto end = deps.find(',', at); if (end == deps.npos) end = deps.size();
        auto dep = trim(deps.substr(at, end-at));
        if (dep.empty() || dep.size()>96 || dep=="." || dep==".." || dep==id ||
            dep.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789.-_") != dep.npos ||
            std::find(m.depends.begin(),m.depends.end(),dep)!=m.depends.end() || m.depends.size()>=32)
            throw std::runtime_error("Invalid/duplicate/self dependency: " + id);
        m.depends.push_back(dep);
        if (end+1==deps.size()) throw std::runtime_error("Empty trailing dependency");
        at=end+1;
    }
    auto tags = values["tags"];
    for (size_t at = 0; at < tags.size();) {
        auto end = tags.find(',', at); if (end == tags.npos) end = tags.size();
        auto tag = trim(tags.substr(at, end - at));
        if (!tag.empty() && std::find(m.tags.begin(), m.tags.end(), tag) == m.tags.end()) m.tags.push_back(tag);
        at = end + 1;
    }
    if (m.tags.size() > 16) throw std::runtime_error("Too many tags");
    auto asset = [&](const char* key, const wchar_t* suffix) {
        auto p = path_utf8(values[key]);
        if (p.empty()) return std::filesystem::path{};
        if (p.has_root_path() || p.string().find(':') != std::string::npos || p.extension() != suffix ||
            !inside(m.directory, m.directory / p) || !std::filesystem::is_regular_file(m.directory / p))
            throw std::runtime_error(std::string("Invalid local asset: ") + key);
        for (auto& part : p) if (part == L"..") throw std::runtime_error("Asset traversal rejected");
        return m.directory / p;
    };
    m.icon = asset("icon", L".png"); m.config = asset("config", L".ini"); m.config_entry = asset("config_entry", L".lua");
    m.config_menu = values["config_menu"];
    m.config_menu = config_menu(m);
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
    return dependency_order(std::move(mods));
}
bool dependencies_loaded(const Manifest& m, const std::vector<std::string>& loaded) {
    return std::all_of(m.depends.begin(),m.depends.end(),[&](auto& id){return std::find(loaded.begin(),loaded.end(),id)!=loaded.end();});
}
std::vector<Manifest> dependency_order(std::vector<Manifest> mods) {
    std::sort(mods.begin(),mods.end(),[](auto& a,auto& b){return a.id<b.id;});
    std::map<std::string,size_t> index;
    for (size_t i=0;i<mods.size();++i)
        if (!index.emplace(mods[i].id,i).second) throw std::runtime_error("Duplicate mod id: "+mods[i].id);
    for (auto& m:mods) if (m.enabled) for (auto& dep:m.depends) {
        auto it=index.find(dep);
        if (it==index.end() || !mods[it->second].enabled)
            throw std::runtime_error("Required dependency missing/disabled: "+m.id+" -> "+dep);
    }
    std::vector<Manifest> result; std::vector<bool> done(mods.size()); std::vector<std::string> loaded;
    while (result.size()<mods.size()) {
        bool progress=false;
        for (size_t i=0;i<mods.size();++i) {
            if (done[i] || (mods[i].enabled && !dependencies_loaded(mods[i],loaded))) continue;
            done[i]=true; loaded.push_back(mods[i].id); result.push_back(mods[i]); progress=true;
            break; // Lexical tie break is recalculated after every ready node.
        }
        if (!progress) throw std::runtime_error("Mod dependency cycle");
    }
    return result;
}
}
