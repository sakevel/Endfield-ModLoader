#pragma once
#include "zml_plugin.h"
#ifdef __cplusplus
extern "C" {
#endif
// Optional extension. ABI1 ZmlHost remains byte-for-byte unchanged.
// Only during start(); host owner is validated. Owned namespace:
// ZML/Mod/<manifest id>/<relative path>. Callback may run on any LoadLua thread.
typedef int (*ZmlLuaSource)(void* userdata, const char* relative_path, ZmlSink sink, void* writer);
typedef struct ZmlLuaServicesV1 {
    uint32_t size, abi;
    int (*register_source)(void* host_owner, ZmlLuaSource provider, void* userdata);
} ZmlLuaServicesV1;
typedef const ZmlLuaServicesV1* (*ZmlLuaServicesEntry)(void);
// Runtime export: ZML_GetLuaServicesV1; no cross-DLL allocations/STL ownership.
#ifdef __cplusplus
}
#endif
