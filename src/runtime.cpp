#include "zml_plugin.h"
#include "il2cpp.hpp"
#include "lua_codec.hpp"
#include "manifest.hpp"
#include "mod_services.hpp"
#include "lua_modules.hpp"
#include "win_util.hpp"
#include "version_label.hpp"
#include <MinHook.h>
#include <chrono>
#include <fstream>
#include <memory>
#include <mutex>
#include <vector>
namespace {
using namespace zml;
HMODULE instance;
struct Mod {
    Manifest info;
    HMODULE dll = nullptr;
    std::string directory, state;
    ZmlHost host{};
    bool initializing = false;
};
struct Transform { Mod* owner; std::string path; ZmlLuaTransform callback; void* userdata; };
struct Runtime {
    Il2Cpp api;
    ManagedMethod load, utf8_encoding, decode, encode;
    std::vector<std::unique_ptr<Mod>> mods;
    std::vector<Transform> transforms;
    ModServices services;
    LuaModules modules;
    Mod* starting = nullptr;
    std::mutex log_mutex;
    std::filesystem::path log_path;
    void* (*original)(void*, void*, const void*) = nullptr;
    void log(std::string_view who, std::string_view message) {
        std::lock_guard lock(log_mutex);
        std::ofstream stream(log_path, std::ios::app | std::ios::binary);
        SYSTEMTIME time{}; GetLocalTime(&time);
        char stamp[40];
        sprintf_s(stamp, "%04u-%02u-%02u %02u:%02u:%02u", time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond);
        stream << stamp << " [" << who << "] " << message << '\n';
    }
};
// Process-lifetime runtime instance
Runtime* rt = nullptr;
void (*version_start_original)(void*,const void*)=nullptr;
ManagedMethod version_text_get,version_text_set;
void version_start(void* panel,const void* method) {
    version_start_original(panel,method);
    try {
        auto text=rt->api.reference_field(panel,"_textVersion","Beyond.UI.UIText");
        if(!text)return;
        ManagedRoot text_root(rt->api,text);
        auto original=rt->api.call(version_text_get,text);
        ManagedRoot original_root(rt->api,original);
        auto current=rt->api.string(original,1024);
        auto display=version_label(current,ZML_VERSION);
        if(current==display)return;
        auto replacement=rt->api.make_string(display);ManagedRoot replacement_root(rt->api,replacement);
        void* args[]{replacement};rt->api.call(version_text_set,text,args);
        rt->log("runtime","Native login version label updated");
    } catch(const std::exception&) {rt->log("runtime","Login version label unavailable");}
}
int register_source(void* owner,ZmlLuaSource source,void* data) {
    if (!rt || !rt->starting || owner!=rt->starting || !rt->starting->initializing) return 0;
    try {return rt->modules.add(rt->starting->info.id,source,data)?1:0;} catch(...) {return 0;}
}
const ZmlLuaServicesV1 lua_services{sizeof(ZmlLuaServicesV1),1,register_source};
void mod_log(void* owner, const char* message) {
    auto mod = static_cast<Mod*>(owner);
    rt->log(mod->info.id, message ? message : "");
}
int register_transform(void* owner, const char* path, ZmlLuaTransform callback, void* userdata) {
    auto mod = static_cast<Mod*>(owner);
    if (!mod->initializing || !path || !callback) return 0;
    std::string name(path);
    if (name.empty() || name.size() > 256 || name.find("..") != std::string::npos || name.front() == '/' || name.ends_with(".lua")) return 0;
    rt->transforms.push_back({mod, std::move(name), callback, userdata});
    return 1;
}
struct Output { std::string bytes; unsigned calls = 0; bool valid = true; };
void write_source(void* writer, const char* bytes, size_t length) {
    auto out = static_cast<Output*>(writer);
    ++out->calls;
    if (!bytes || length == 0 || length > 768 * 1024 || out->calls != 1) { out->valid = false; return; }
    out->bytes.assign(bytes, length);
    if (out->bytes.find('\0') != std::string::npos) out->valid = false;
}
void* patched_load(void* object, void* path, const void* method) {
    // Reserved virtual modules must be handled BEFORE the game's VFS lookup.
    // This is a public loader service, independent of any menu mod.
    try {
        auto name = rt->api.string(path, 4096);
        if (name.ends_with(".lua")) name.resize(name.size() - 4);
        if (name.starts_with("ZML/")) {
            auto source = name.starts_with("ZML/Mod/") ? rt->modules.module(name) : rt->services.module(name);
            if (name.starts_with("ZML/Report/") && source == "return {ok=true}") rt->log("lua-event", name.substr(11));
            auto text = rt->api.make_string(pack_lua({source, Envelope::xxtea_base64}));
            ManagedRoot text_root(rt->api, text);
            auto encoding = rt->api.call(rt->utf8_encoding);
            ManagedRoot encoding_root(rt->api, encoding);
            void* args[]{text};
            return rt->api.call(rt->encode, encoding, args);
        }
    } catch (const std::exception& error) {
        rt->log("runtime", std::string("Loader service failed: ") + error.what());
        // Handle virtual path lookup error
        try { if (rt->api.string(path, 4096).starts_with("ZML/")) return nullptr; } catch (...) {}
    }
    void* bytes = rt->original(object, path, method);
    if (!bytes) return bytes;
    try {
        auto name = rt->api.string(path, 512);
        if (name.ends_with(".lua")) name.resize(name.size() - 4);
        bool interested = false;
        for (auto& t : rt->transforms) interested |= t.path == name;
        if (!interested) return bytes;
        ManagedRoot bytes_root(rt->api, bytes);
        auto encoding = rt->api.call(rt->utf8_encoding);
        ManagedRoot encoding_root(rt->api, encoding);
        void* decode_args[]{bytes};
        auto managed_text = rt->api.call(rt->decode, encoding, decode_args);
        ManagedRoot text_root(rt->api, managed_text);
        auto source = unpack_lua(rt->api.string(managed_text));
        bool changed = false;
        // Stable manifest-id order. Each callback is atomic; a rejection
        // retains all earlier successful transforms without partial output.
        for (auto& t : rt->transforms) {
            if (t.path != name) continue;
            Output output;
            if (!t.callback(t.userdata, source.text.data(), source.text.size(), &write_source, &output)) continue;
            if (!output.valid || output.calls != 1) { rt->log(t.owner->info.id, "Invalid transform output ignored"); continue; }
            source.text = std::move(output.bytes);
            changed = true;
            rt->log(t.owner->info.id, "Transformed " + name);
        }
        if (!changed) return bytes;
        auto payload = pack_lua(source);
        auto replacement_text = rt->api.make_string(payload);
        ManagedRoot replacement_root(rt->api, replacement_text);
        void* encode_args[]{replacement_text};
        auto replacement = rt->api.call(rt->encode, encoding, encode_args);
        if (!replacement) throw std::runtime_error("Encoding returned null");
        rt->log("runtime", "Patched " + name);
        return replacement;
    } catch (const std::exception& error) {
        rt->log("runtime", std::string("Lua transform failed: ") + error.what());
        return bytes;
    } catch (...) {
        rt->log("runtime", "Lua transform failed: unknown error");
        return bytes;
    }
}
void load_mods(const std::filesystem::path& root) {
    std::vector<std::string> loaded;
    for (auto& manifest : discover(root / L"mods")) {
        if (!manifest.enabled) { rt->log(manifest.id, "Disabled by mod.ini"); continue; }
        if (!dependencies_loaded(manifest,loaded)) {rt->log(manifest.id,"Skipped: required dependency failed initialization");continue;}
        auto mod = std::make_unique<Mod>();
        mod->info = std::move(manifest);
        mod->directory = utf8(mod->info.directory.wstring());
        auto state = state_root() / L"mods" / path_utf8(mod->info.id);
        if (!inside(state_root(), state)) throw std::runtime_error("Mod state escapes loader directory");
        std::filesystem::create_directories(state);
        mod->state = utf8(state.wstring());
        mod->host = {sizeof(ZmlHost), 1, mod.get(), mod->directory.c_str(), mod->state.c_str(), &mod_log, &register_transform};
        size_t checkpoint = rt->transforms.size();
        try {
            auto record = prepare_record(mod->info, state);
            mod->dll = LoadLibraryExW(mod->info.library.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            if (!mod->dll) throw std::runtime_error("LoadLibraryExW failed: " + std::to_string(GetLastError()));
            auto entry = reinterpret_cast<ZmlPluginEntry>(GetProcAddress(mod->dll, "ZML_PluginV1"));
            auto plugin = entry ? entry() : nullptr;
            if (!plugin || plugin->size != sizeof(ZmlPlugin) || plugin->abi != 1 || !plugin->id || plugin->id != mod->info.id || !plugin->start)
                throw std::runtime_error("Plugin ABI/id mismatch");
            mod->initializing = true;
            rt->starting = mod.get();
            if (!plugin->start(&mod->host)) throw std::runtime_error("Plugin initialization rejected");
            mod->initializing = false;
            rt->starting = nullptr;
            rt->services.publish(std::move(record));
            loaded.push_back(mod->info.id);
            rt->log(mod->info.id, "Loaded " + utf8(mod->info.library.filename().wstring()));
        } catch (const std::exception& error) {
            mod->initializing = false;
            rt->starting = nullptr;
            rt->modules.remove(mod->info.id);
            rt->transforms.resize(checkpoint);
            rt->log(mod->info.id, error.what());
        }
        // Keep loaded DLL handle in list
        rt->mods.push_back(std::move(mod));
    }
}
DWORD WINAPI initialize(void*) {
    try {
        HMODULE pin = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, reinterpret_cast<LPCWSTR>(instance), &pin);
        rt = new Runtime;
        std::filesystem::create_directories(state_root());
        rt->log_path = state_root() / L"runtime.log";
        rt->log("runtime", "ZML " ZML_VERSION " loaded; waiting for GameAssembly");
        auto deadline = GetTickCount64() + 120000;
        while (!GetModuleHandleW(L"GameAssembly.dll")) {
            if (GetTickCount64() >= deadline) throw std::runtime_error("GameAssembly wait timed out");
            Sleep(250);
        }
        // Wait for runtime initialization before attaching thread
        Sleep(3000);
        if (!rt->api.connect()) throw std::runtime_error("IL2CPP export contract unavailable");
        while (!rt->api.domain()) {
            if (GetTickCount64() >= deadline) throw std::runtime_error("IL2CPP domain wait timed out");
            Sleep(250);
        }
        auto thread = rt->api.attach(rt->api.domain());
        if (!thread) throw std::runtime_error("IL2CPP thread attach failed");
        struct Detach { Il2Cpp& api; void* thread; ~Detach() { api.detach(thread); } } detach{rt->api, thread};
        do {
            rt->load = rt->api.method("Lua.Beyond.dll", "Beyond.Lua", "LuaManager", "LoadLua", "System.Byte[]", {"System.String"});
            rt->utf8_encoding = rt->api.method("mscorlib.dll", "System.Text", "Encoding", "get_UTF8", "System.Text.Encoding", {});
            rt->decode = rt->api.method("mscorlib.dll", "System.Text", "Encoding", "GetString", "System.String", {"System.Byte[]"});
            rt->encode = rt->api.method("mscorlib.dll", "System.Text", "Encoding", "GetBytes", "System.Byte[]", {"System.String"});
            if (rt->load.entry && rt->utf8_encoding.entry && rt->decode.entry && rt->encode.entry) break;
            if (GetTickCount64() >= deadline) throw std::runtime_error("Lua/Encoding metadata contract timed out");
            Sleep(250);
        } while (true);
        auto root = module_path(instance).parent_path();
        auto api_file = root / L"lua" / L"zml.lua";
        if (!inside(root, api_file) || std::filesystem::file_size(api_file) > 128 * 1024) throw std::runtime_error("Loader Lua API asset missing/invalid");
        std::ifstream api_stream(api_file, std::ios::binary);
        if (!api_stream) throw std::runtime_error("Cannot read loader Lua API");
        rt->services.set_api({std::istreambuf_iterator<char>(api_stream), {}});
        load_mods(root);
        if (MH_Initialize() != MH_OK) throw std::runtime_error("MinHook initialization failed (another loader may be present)");
        auto created = MH_CreateHook(rt->load.entry, reinterpret_cast<void*>(&patched_load), reinterpret_cast<void**>(&rt->original));
        if (created != MH_OK) throw std::runtime_error("LoadLua hook creation failed");
        if (MH_EnableHook(rt->load.entry) != MH_OK) { MH_RemoveHook(rt->load.entry); throw std::runtime_error("LoadLua hook enable failed"); }
        auto version_start_method=rt->api.method("Entry.Beyond.dll","Beyond.Login","LoginVersionPanel","Start","System.Void",{});
        version_text_get=rt->api.method("UI.Beyond.dll","Beyond.UI","UIText","get_text","System.String",{});
        version_text_set=rt->api.method("UI.Beyond.dll","Beyond.UI","UIText","set_text","System.Void",{"System.String"});
        if(version_start_method.entry && version_text_get.entry && version_text_set.entry &&
           !rt->api.is_static(version_start_method) && !rt->api.is_static(version_text_get) && !rt->api.is_static(version_text_set) &&
           MH_CreateHook(version_start_method.entry,reinterpret_cast<void*>(&version_start),reinterpret_cast<void**>(&version_start_original))==MH_OK) {
            if(MH_EnableHook(version_start_method.entry)!=MH_OK) {MH_RemoveHook(version_start_method.entry);rt->log("runtime","Login version hook unavailable");}
        } else rt->log("runtime","Login version metadata contract unavailable");
        rt->log("runtime", "Ready: " + std::to_string(rt->transforms.size()) + " Lua transform(s)");
    } catch (const std::exception& error) {
        if (rt && !rt->log_path.empty()) rt->log("runtime", std::string("Startup failed: ") + error.what());
    }
    return 0;
}
}
extern "C" __declspec(dllexport) const ZmlLuaServicesV1* ZML_GetLuaServicesV1() {return &lua_services;}
BOOL WINAPI DllMain(HINSTANCE module, DWORD event, LPVOID) {
    if (event == DLL_PROCESS_ATTACH) {
        instance = module;
        DisableThreadLibraryCalls(module);
        auto thread = CreateThread(nullptr, 0, &initialize, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
