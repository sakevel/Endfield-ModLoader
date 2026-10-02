#include "mod_services.hpp"
#include "win_util.hpp"
#include <wincrypt.h>
#include <algorithm>
#include <cmath>
#include <charconv>
#include <fstream>
#include <iterator>
#include <set>
#include <sstream>
namespace zml {
namespace {
std::string trim(std::string s) {
    auto a = s.find_first_not_of(" \t\r\n"), b = s.find_last_not_of(" \t\r\n");
    return a == s.npos ? "" : s.substr(a, b - a + 1);
}
std::string read(const std::filesystem::path& file, size_t limit) {
    if (std::filesystem::file_size(file) > limit) throw std::runtime_error("Mod asset exceeds size limit");
    std::ifstream stream(file, std::ios::binary);
    if (!stream) throw std::runtime_error("Cannot read mod asset");
    std::string out{std::istreambuf_iterator<char>(stream), {}};
    if (out.size() > limit) throw std::runtime_error("Mod asset exceeds size limit");
    return out;
}
double number(std::string_view value) {
    double n = 0;
    auto parsed = std::from_chars(value.data(), value.data() + value.size(), n);
    if (parsed.ec != std::errc{} || parsed.ptr != value.data() + value.size() || !std::isfinite(n))
        throw std::runtime_error("Invalid finite number");
    return n;
}
bool key_valid(std::string_view key) {
    return !key.empty() && key.size() <= 64 && key.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789_-") == key.npos;
}
std::vector<std::string> split(std::string_view s, char sep) {
    std::vector<std::string> result;
    for (size_t at = 0; at <= s.size();) {
        auto end = s.find(sep, at); if (end == s.npos) end = s.size();
        result.emplace_back(s.substr(at, end - at)); at = end + 1;
    }
    return result;
}
std::string values_lua(const ConfigValues& values) {
    std::string out = "{";
    for (auto& [key, value] : values) out += "[" + lua_quote(key) + "]=" + lua_quote(value) + ",";
    return out + "}";
}
std::string registry(const std::vector<ModRecord>& records) {
    std::string out = "{";
    for (auto& r : records) {
        auto& m = r.info;
        out += "{id=" + lua_quote(m.id) + ",name=" + lua_quote(m.title) + ",version=" + lua_quote(m.version) +
            ",description=" + lua_quote(m.description) + ",authors=" + lua_quote(m.authors) +
            ",icon=" + lua_quote(r.icon_base64.empty() ? "" : "png") + ",config_menu=" + lua_quote(m.config_menu) +
            ",has_entry=" + (m.config_menu == "custom" ? "true" : "false") + ",tags={";
        for (auto& tag : m.tags) out += lua_quote(tag) + ",";
        out += "},depends={";
        for (auto& dep : m.depends) out += lua_quote(dep) + ",";
        out += "},config={title=" + lua_quote(r.config.title) + ",fields={";
        for (auto& f : r.config.fields) {
            out += "{key=" + lua_quote(f.key) + ",type=" + lua_quote(f.type) + ",label=" + lua_quote(f.label) +
                ",description=" + lua_quote(f.description) + ",default=" + lua_quote(f.initial) +
                ",min=" + std::to_string(f.minimum) + ",max=" + std::to_string(f.maximum) + ",step=" + std::to_string(f.step) +
                ",min_length=" + std::to_string(f.min_length) + ",max_length=" + std::to_string(f.max_length) +
                ",format=" + lua_quote(f.format) + ",restart=" + (f.restart ? "true" : "false") + ",options={";
            for (auto& option : f.options) out += lua_quote(option) + ",";
            out += "}},";
        }
        out += "}},values=" + values_lua(r.values) + "},";
    }
    if (out.size() > 600 * 1024) throw std::runtime_error("Registry exceeds 600 KiB");
    return out + "}";
}
std::string unhex(std::string_view s) {
    if (s.size() % 2 || s.size() > 2048) throw std::runtime_error("Invalid configuration request");
    std::string result;
    auto digit = [](char c) -> int {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return -1;
    };
    for (size_t i = 0; i < s.size(); i += 2) {
        auto a = digit(s[i]), b = digit(s[i + 1]);
        if (a < 0 || b < 0) throw std::runtime_error("Invalid hex value");
        result += static_cast<char>(a * 16 + b);
    }
    return result;
}
}
std::string lua_quote(std::string_view text) {
    std::string out = "\"";
    for (unsigned char c : text) {
        if (c == '"' || c == '\\') { out += '\\'; out += c; }
        else if (c < 32 || c == 127) {
            char escaped[5]; sprintf_s(escaped, "\\%03u", c); out += escaped;
        } else out += c;
    }
    return out + '"';
}
bool valid_config_value(const ConfigField& f, std::string_view value) {
    if (value.find_first_of("\r\n\0", 0, 3) != value.npos || value.size() > 1024) return false;
    try {
        if (f.type == "bool") return value == "true" || value == "false";
        if (f.type == "enum") return std::find(f.options.begin(), f.options.end(), value) != f.options.end();
        if (f.type == "number") {
            auto n = number(value), steps = (n - f.minimum) / f.step;
            return n >= f.minimum && n <= f.maximum && std::abs(steps - std::round(steps)) <= 1e-6;
        }
        if (f.type == "string") {
            if (value.size() < f.min_length || value.size() > f.max_length) return false;
            if (f.format == "digits" && value.find_first_not_of("0123456789") != value.npos) return false;
            if (f.format == "plain_text") {
                for (unsigned char c : value) if (c < 32 || c == 127 || c == '<' || c == '>') return false;
            }
            return value.empty() || MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0) != 0;
        }
    } catch (...) {}
    return false;
}
ConfigDefinition read_config(const std::filesystem::path& file) {
    ConfigDefinition definition;
    if (file.empty()) return definition;
    std::istringstream stream(read(file, 64 * 1024));
    std::string line, section;
    std::map<std::string, std::map<std::string, std::string>> blocks;
    std::vector<std::string> order;
    while (std::getline(stream, line)) {
        line = trim(line);
        if (line.empty() || line[0] == ';' || line[0] == '#') continue;
        if (line.front() == '[' && line.back() == ']') {
            section = line.substr(1, line.size() - 2);
            if (section != "menu" && (!section.starts_with("field.") || !key_valid(section.substr(6)))) throw std::runtime_error("Invalid config section");
            if (blocks.contains(section)) throw std::runtime_error("Duplicate config section");
            blocks[section]; order.push_back(section); continue;
        }
        auto eq = line.find('=');
        if (section.empty() || eq == line.npos || !blocks[section].emplace(trim(line.substr(0, eq)), trim(line.substr(eq + 1))).second)
            throw std::runtime_error("Invalid/duplicate config property");
    }
    for (auto& name : order) {
        auto& v = blocks[name];
        if (name == "menu") {
            for (auto& [key, value] : v) { if (key != "title") throw std::runtime_error("Unknown menu property"); definition.title = value; }
            continue;
        }
        for (auto& [key, value] : v)
            if (key != "type" && key != "label" && key != "description" && key != "default" && key != "min" && key != "max" &&
                key != "step" && key != "options" && key != "max_length" && key != "min_length" && key != "format" && key != "restart") throw std::runtime_error("Unknown config property");
        ConfigField f; f.key = name.substr(6); f.type = v["type"]; f.label = v["label"].empty() ? f.key : v["label"];
        f.description = v["description"]; f.initial = v["default"];
        if (v.contains("restart")) {
            if (v["restart"] != "true" && v["restart"] != "false") throw std::runtime_error("Invalid restart flag");
            f.restart = v["restart"] == "true";
        }
        if (f.type == "number") {
            if (v.contains("min")) f.minimum = number(v["min"]);
            if (v.contains("max")) f.maximum = number(v["max"]);
            if (v.contains("step")) f.step = number(v["step"]);
            if (f.minimum > f.maximum || f.step <= 0) throw std::runtime_error("Invalid numeric bounds");
        }
        if (f.type == "enum") {
            f.options = split(v["options"], '|'); std::set<std::string> unique;
            if (f.options.size() > 32) throw std::runtime_error("Too many enum options");
            for (auto& option : f.options) { option = trim(option); if (option.empty() || !unique.insert(option).second) throw std::runtime_error("Invalid enum options"); }
        }
        if (v.contains("max_length")) {
            auto n = number(v["max_length"]);
            if (n < 1 || n > 1024 || n != std::floor(n)) throw std::runtime_error("Invalid max_length");
            f.max_length = static_cast<size_t>(n);
        }
        if (v.contains("min_length")) {
            auto n = number(v["min_length"]);
            if (n < 0 || n > 1024 || n != std::floor(n)) throw std::runtime_error("Invalid min_length");
            f.min_length = static_cast<size_t>(n);
        }
        if ((v.contains("min_length") || v.contains("format")) && f.type != "string") throw std::runtime_error("String constraints require string type");
        if (v.contains("format")) f.format = v.at("format");
        if (f.min_length > f.max_length || (!f.format.empty() && f.format != "digits" && f.format != "plain_text")) throw std::runtime_error("Invalid string constraints");
        if (!valid_config_value(f, f.initial)) throw std::runtime_error("Invalid config type/default: " + f.key);
        definition.fields.push_back(std::move(f));
        if (definition.fields.size() > 64) throw std::runtime_error("Too many config fields");
    }
    return definition;
}
ModRecord prepare_record(const Manifest& m, const std::filesystem::path& state) {
    ModRecord r; r.info = m; r.info.config_menu = config_menu(m); r.state = state; r.config = read_config(m.config);
    for (auto& f : r.config.fields) r.values[f.key] = f.initial;
    if (!m.config_entry.empty()) {
        r.entry_source = read(m.config_entry, 128 * 1024);
        if (r.entry_source.empty() || r.entry_source.find('\0') != std::string::npos) throw std::runtime_error("Invalid config entry source");
    }
    if (!m.icon.empty()) {
        auto icon = read(m.icon, 64 * 1024);
        // Constrain dimensions before Unity allocates/decompresses the image.
        auto be32 = [&](size_t at) { uint32_t n = 0; for (size_t i = at; i < at + 4; ++i) n = n * 256 + static_cast<unsigned char>(icon[i]); return n; };
        if (icon.size() < 33 || icon.substr(0, 8) != std::string("\x89PNG\r\n\x1a\n", 8) || icon.substr(12, 4) != "IHDR" ||
            !be32(16) || !be32(20) || be32(16) > 512 || be32(20) > 512) throw std::runtime_error("Icon must be a PNG up to 512x512 / 64 KiB");
        DWORD count = 0;
        if (!CryptBinaryToStringA(reinterpret_cast<const BYTE*>(icon.data()), static_cast<DWORD>(icon.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &count)) throw std::runtime_error("Icon encoding failed");
        r.icon_base64.resize(count);
        CryptBinaryToStringA(reinterpret_cast<const BYTE*>(icon.data()), static_cast<DWORD>(icon.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, r.icon_base64.data(), &count);
        r.icon_base64.resize(count);
        if (!r.icon_base64.empty() && r.icon_base64.back() == '\0') r.icon_base64.pop_back();
    }
    auto saved = state / L"config.ini";
    if (!inside(state, saved)) throw std::runtime_error("Config state escapes directory");
    if (std::filesystem::exists(saved)) {
        std::istringstream stream(read(saved, 64 * 1024)); std::string line;
        std::set<std::string> seen;
        while (std::getline(stream, line)) {
            auto eq = line.find('='); if (eq == line.npos) continue;
            auto key = line.substr(0, eq), value = line.substr(eq + 1);
            if (!value.empty() && value.back() == '\r') value.pop_back();
            for (auto& f : r.config.fields)
                if (f.key == key && valid_config_value(f, value) && seen.insert(key).second) r.values[key] = value;
        }
    }
    return r;
}
void ModServices::set_api(std::string source) {
    if (source.size() > 128 * 1024 || source.find('\0') != source.npos) throw std::runtime_error("Invalid Lua API asset");
    const std::string token = "__ZML_REGISTRY__"; auto at = source.find(token);
    if (at == source.npos || source.find(token, at + token.size()) != source.npos) throw std::runtime_error("Lua API registry token mismatch");
    std::lock_guard lock(mutex_); api_source_ = std::move(source);
}
void ModServices::publish(ModRecord record) {
    record.info.config_menu = config_menu(record.info);
    if ((record.info.config_menu == "custom") != !record.entry_source.empty()) throw std::runtime_error("Config entry/menu mismatch");
    std::lock_guard lock(mutex_);
    for (auto& r : records_) if (r.info.id == record.info.id) throw std::runtime_error("Duplicate published mod id");
    records_.push_back(std::move(record));
    try { registry(records_); } catch (...) { records_.pop_back(); throw; }
}
std::string ModServices::registry_lua() const { std::lock_guard lock(mutex_); return registry(records_); }
std::string ModServices::module(std::string_view path) {
    std::lock_guard lock(mutex_);
    try {
        if (path.size() > 4096) throw std::runtime_error("Request too large");
        if (path == "ZML/Api") {
            if (api_source_.empty()) throw std::runtime_error("Loader Lua API unavailable");
            auto source = api_source_; const std::string token = "__ZML_REGISTRY__";
            source.replace(source.find(token), token.size(), registry(records_)); return source;
        }
        auto args = split(path, '/');
        if (args.size() < 3 || args[0] != "ZML") throw std::runtime_error("Unknown loader service");
        auto found = std::find_if(records_.begin(), records_.end(), [&](auto& r) { return r.info.id == args[2]; });
        if (found == records_.end()) throw std::runtime_error("Mod is not loaded");
        auto& r = *found;
        if (args[1] == "Icon" && args.size() == 3) return "return {ok=true,data=" + lua_quote(r.icon_base64) + "}";
        if (args[1] == "Report" && args.size() == 4 && !args[3].empty() && args[3].size() <= 64 &&
            args[3].find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789_:-") == args[3].npos)
            return "return {ok=true}";
        if (args[1] == "Entry" && args.size() == 3) {
            if (r.entry_source.empty()) throw std::runtime_error("No custom config entry");
            return r.entry_source;
        }
        if (args[1] == "Get" && args.size() == 3) return "return {ok=true,values=" + values_lua(r.values) + "}";
        if (args[1] != "Set" || args.size() != 5) throw std::runtime_error("Unknown config service");
        auto f = std::find_if(r.config.fields.begin(), r.config.fields.end(), [&](auto& field) { return field.key == args[3]; });
        auto value = unhex(args[4]);
        if (f == r.config.fields.end() || !valid_config_value(*f, value)) throw std::runtime_error("Config key/value rejected");
        auto next = r.values; next[f->key] = value;
        auto file = r.state / L"config.ini", temp = r.state / L"config.ini.tmp";
        if (!inside(r.state, file) || !inside(r.state, temp)) throw std::runtime_error("Config state escapes directory");
        std::filesystem::create_directories(r.state);
        {
            std::ofstream stream(temp, std::ios::binary | std::ios::trunc);
            for (auto& [key, v] : next) stream << key << '=' << v << '\n';
            stream.flush(); if (!stream) throw std::runtime_error("Config write failed");
        }
        if (!MoveFileExW(temp.c_str(), file.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) throw std::runtime_error("Config commit failed");
        r.values.swap(next);
        return "return {ok=true,values=" + values_lua(r.values) + ",restart=" + (f->restart ? "true" : "false") + "}";
    } catch (const std::exception& error) {
        return "return {ok=false,error=" + lua_quote(error.what()) + "}";
    }
}
}
