#pragma once
#include <stdint.h>
// Additive ABI1 service: DLLs remain resident. No native hook removal/reload.
typedef struct ZmlActivationV1 {
    uint32_t size, abi;
    int (*is_active)(void* owner); // lock-free; owner is the original ZmlHost.owner
    int (*is_mod_active)(const char* id); // bounded registry lookup for input owners
} ZmlActivationV1;
typedef const ZmlActivationV1* (*ZmlActivationEntry)(void);
