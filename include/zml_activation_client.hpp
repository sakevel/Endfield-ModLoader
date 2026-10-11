#pragma once
#include "zml_activation.h"
#include <Windows.h>
namespace zml_activation {
inline const ZmlActivationV1* service=nullptr;
inline void* owner=nullptr;
inline void init(void* value) {
    owner=value;
    auto dll=GetModuleHandleW(L"ZMLRuntime.dll");
    auto entry=dll?reinterpret_cast<ZmlActivationEntry>(GetProcAddress(dll,"ZML_GetActivationV1")):nullptr;
    auto result=entry?entry():nullptr;
    if(result && result->abi==1 && result->size==sizeof(ZmlActivationV1) && result->is_active && result->is_mod_active) service=result;
}
inline bool active() {return !service || service->is_active(owner)!=0;}
inline bool active(const char* id) {return !service || service->is_mod_active(id)!=0;}
}
