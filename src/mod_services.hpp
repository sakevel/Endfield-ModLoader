#pragma once
#include "manifest.hpp"
#include <map>
#include <mutex>
namespace zml {
// Public Lua modules and configuration data are loader services, not menu hooks.
struct ConfigField {
    std::string key, type, label, description, initial, format;
    std::vector<std::string> options;
    double minimum = 0, maximum = 100, step = 1;
    size_t max_length = 128;
    size_t min_length = 0;
    bool restart = false;
};
struct ConfigDefinition { std::string title; std::vector<ConfigField> fields; };
using ConfigValues = std::map<std::string, std::string>;
std::string lua_quote(std::string_view text);
ConfigDefinition read_config(const std::filesystem::path& file);
bool valid_config_value(const ConfigField& field, std::string_view value);
struct ModRecord {
    Manifest info;
    ConfigDefinition config;
    ConfigValues values;
    std::filesystem::path state;
    std::string icon_base64, entry_source;
};
// prepare before calling start(); publish only after successful initialization.
ModRecord prepare_record(const Manifest& manifest, const std::filesystem::path& state);
class ModServices {
    std::vector<ModRecord> records_;
    std::string api_source_;
    mutable std::mutex mutex_;
public:
    void set_api(std::string source);
    void publish(ModRecord record);
    std::string registry_lua() const;
    // ZML/Api, ZML/Get/<id>, ZML/Set/<id>/<key>/<hex>, ZML/Entry/<id>.
    // No arbitrary file access or native function invocation.
    std::string module(std::string_view path);
};
}
