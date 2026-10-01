#pragma once
#include <stddef.h>
#include <stdint.h>

// C ABI; no STL objects or ownership crosses DLL boundaries.
// A successful transform must call the sink exactly once. The host copies
// the supplied UTF-8 source before the callback returns. No Lua execution is
// allowed during initialization (worker thread).
typedef void (*ZmlSink)(void* writer, const char* bytes, size_t length);
typedef int (*ZmlLuaTransform)(void* userdata, const char* source, size_t length,
                             ZmlSink sink, void* writer);
typedef struct ZmlHost {
    uint32_t size;
    uint32_t abi; // 1
    void* owner;
    const char* mod_directory; // UTF-8, process-lifetime
    const char* state_directory; // UTF-8; owned by this mod, not game data
    void (*log)(void* owner, const char* message);
    // Canonical module path without .lua; registration only inside start().
    int (*transform_lua)(void* owner, const char* module_path,
                         ZmlLuaTransform callback, void* userdata);
} ZmlHost;
typedef struct ZmlPlugin {
    uint32_t size;
    uint32_t abi;
    const char* id; // Must equal mod.ini id.
    int (*start)(const ZmlHost* host);
} ZmlPlugin;
typedef const ZmlPlugin* (*ZmlPluginEntry)(void);
#ifdef __cplusplus
extern "C" {
#endif
__declspec(dllexport) const ZmlPlugin* ZML_PluginV1(void);
#ifdef __cplusplus
}
#endif
