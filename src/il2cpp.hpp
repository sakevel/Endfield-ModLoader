#pragma once
#include <Windows.h>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>
namespace zml {
struct ManagedMethod { const void* metadata = nullptr; void* entry = nullptr; };
class Il2Cpp {
    HMODULE image_ = nullptr;
    void* (*domain_)() = nullptr;
    const void** (*assemblies_)(void*, size_t*) = nullptr;
    void* (*assembly_image_)(const void*) = nullptr;
    const char* (*image_name_)(void*) = nullptr;
    void* (*class_named_)(void*, const char*, const char*) = nullptr;
    const void* (*methods_)(void*, void**) = nullptr;
    const char* (*method_name_)(const void*) = nullptr;
    uint32_t (*parameter_count_)(const void*) = nullptr;
    const void* (*parameter_)(const void*, uint32_t) = nullptr;
    const void* (*return_type_)(const void*) = nullptr;
    char* (*type_name_)(const void*) = nullptr;
    void (*free_)(void*) = nullptr;
    int32_t (*string_length_)(void*) = nullptr;
    const wchar_t* (*string_chars_)(void*) = nullptr;
    void* (*string_new_)(const char*) = nullptr;
    void* (*invoke_)(const void*, void*, void**, void**) = nullptr;
    uint32_t (*root_)(void*, bool) = nullptr;
    void (*unroot_)(uint32_t) = nullptr;
    std::string type(const void* value);
public:
    void* (*attach)(void*) = nullptr;
    void (*detach)(void*) = nullptr;
    bool connect();
    void* domain() const;
    ManagedMethod method(const char* image, const char* ns, const char* klass,
                         const char* name, const char* returns, std::vector<std::string_view> args);
    void* call(ManagedMethod method, void* object = nullptr, void** args = nullptr);
    std::string string(void* value, size_t limit = 1024 * 1024);
    void* make_string(const std::string& text);
    uint32_t protect(void* object);
    void release(uint32_t handle);
};
class ManagedRoot {
    Il2Cpp& api_;
    uint32_t handle_;
public:
    ManagedRoot(Il2Cpp& api, void* object) : api_(api), handle_(api.protect(object)) {}
    ~ManagedRoot() { api_.release(handle_); }
    ManagedRoot(const ManagedRoot&) = delete;
};
}
