#include "zml_plugin.h"
#include "patch.hpp"
#include <Windows.h>
#include <filesystem>
#include <fstream>
#include <iterator>
namespace {
const ZmlHost* services = nullptr;
std::string extension;
std::string lua_literal(std::string_view input) {
    std::string result = "\"";
    for (char c : input) {
        if (c == '\\' || c == '"') result += '\\';
        if (c == '\r' || c == '\n') throw std::runtime_error("Invalid status path");
        result += c;
    }
    return result + '"';
}
int transform(void*, const char* source, size_t length, ZmlSink sink, void* writer) {
    try {
        std::string result;
        if (!menu_mod::edit(std::string_view(source, length), extension, result)) {
            services->log(services->owner, "WatchCtrl contract rejected/already patched; no partial changes"); return 0;
        }
        sink(writer, result.data(), result.size()); return 1;
    } catch (const std::exception& e) { services->log(services->owner, e.what()); return 0; }
}
int start(const ZmlHost* host) {
    if (!host || host->size != sizeof(ZmlHost) || host->abi != 1) return 0;
    services = host;
    try {
        auto file = std::filesystem::path(std::u8string(reinterpret_cast<const char8_t*>(host->mod_directory))) / L"custom-menu.lua";
        if (std::filesystem::file_size(file) > 128 * 1024) return 0;
        std::ifstream stream(file, std::ios::binary);
        extension.assign(std::istreambuf_iterator<char>(stream), {});
        if (extension.empty()) return 0;
        const std::string token = "__ZML_STATUS_FILE__";
        auto at = extension.find(token);
        if (at == extension.npos || extension.find(token, at + token.size()) != extension.npos) return 0;
        std::string path(host->state_directory); path += "/status.log";
        extension.replace(at, token.size(), lua_literal(path));
        return host->transform_lua(host->owner, "UI/Panels/Watch/WatchCtrl", &transform, nullptr);
    } catch (const std::exception& e) { host->log(host->owner, e.what()); return 0; }
}
const ZmlPlugin descriptor{sizeof(ZmlPlugin), 1, "custom-menu", &start};
}
extern "C" __declspec(dllexport) const ZmlPlugin* ZML_PluginV1() { return &descriptor; }
