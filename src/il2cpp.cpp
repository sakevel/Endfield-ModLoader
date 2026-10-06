#include "il2cpp.hpp"
#include "win_util.hpp"
#include <cstring>
namespace zml {
bool Il2Cpp::connect() {
    image_ = GetModuleHandleW(L"GameAssembly.dll");
    if (!image_) return false;
#define BIND(member, suffix) member = reinterpret_cast<decltype(member)>(GetProcAddress(image_, "il2cpp_" suffix)); if (!member) return false
    BIND(domain_, "domain_get"); BIND(assemblies_, "domain_get_assemblies");
    BIND(assembly_image_, "assembly_get_image"); BIND(image_name_, "image_get_name");
    BIND(class_named_, "class_from_name"); BIND(methods_, "class_get_methods");
    BIND(method_name_, "method_get_name"); BIND(parameter_count_, "method_get_param_count");
    BIND(method_flags_, "method_get_flags");
    BIND(parameter_, "method_get_param"); BIND(return_type_, "method_get_return_type");
    BIND(type_name_, "type_get_name"); BIND(free_, "free");
    BIND(string_length_, "string_length"); BIND(string_chars_, "string_chars");
    BIND(string_new_, "string_new"); BIND(invoke_, "runtime_invoke");
    BIND(root_, "gchandle_new"); BIND(unroot_, "gchandle_free");
    BIND(object_class_, "object_get_class"); BIND(field_named_, "class_get_field_from_name");
    BIND(field_type_, "field_get_type"); BIND(field_flags_, "field_get_flags"); BIND(field_value_, "field_get_value");
    BIND(attach, "thread_attach"); BIND(detach, "thread_detach");
#undef BIND
    return true;
}
void* Il2Cpp::domain() const { return domain_ ? domain_() : nullptr; }
std::string Il2Cpp::type(const void* value) {
    char* s = type_name_(value);
    if (!s) return {};
    std::string name(s); free_(s); return name;
}
ManagedMethod Il2Cpp::method(const char* image, const char* ns, const char* klass,
                           const char* name, const char* returns, std::vector<std::string_view> args) {
    size_t count = 0;
    auto all = assemblies_(domain(), &count);
    if (!all || count > 4096) return {};
    ManagedMethod found;
    unsigned matches = 0;
    for (size_t i = 0; i < count; ++i) {
        auto img = assembly_image_(all[i]);
        if (!img || std::strcmp(image_name_(img), image)) continue;
        auto cls = class_named_(img, ns, klass);
        if (!cls) continue;
        void* iterator = nullptr;
        while (auto m = methods_(cls, &iterator)) {
            if (std::strcmp(method_name_(m), name) || parameter_count_(m) != args.size() || type(return_type_(m)) != returns) continue;
            bool exact = true;
            for (uint32_t p = 0; p < args.size(); ++p) exact &= type(parameter_(m, p)) == args[p];
            if (!exact) continue;
            ++matches;
            void* entry = nullptr;
            std::memcpy(&entry, m, sizeof(entry)); // verified IL2CPP MethodInfo first slot
            MEMORY_BASIC_INFORMATION region{};
            if (!entry || !VirtualQuery(entry, &region, sizeof(region)) || region.AllocationBase != image_ ||
                !(region.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))) continue;
            found = {m, entry};
        }
    }
    return matches == 1 ? found : ManagedMethod{};
}
void* Il2Cpp::call(ManagedMethod method, void* object, void** args) {
    if (!method.metadata) throw std::runtime_error("Managed method unavailable");
    void* exception = nullptr;
    auto result = invoke_(method.metadata, object, args, &exception);
    if (exception) throw std::runtime_error(std::string("Managed exception in ") + method_name_(method.metadata));
    return result;
}
bool Il2Cpp::is_static(ManagedMethod method) const {return method.metadata && (method_flags_(method.metadata,nullptr)&0x10);}
std::string Il2Cpp::string(void* value, size_t limit) {
    if (!value) return {};
    auto n = string_length_(value);
    if (n < 0 || static_cast<size_t>(n) > limit) throw std::runtime_error("Managed string exceeds limit");
    auto out = utf8(std::wstring_view(string_chars_(value), n));
    if (out.size() > limit) throw std::runtime_error("UTF8 payload exceeds limit");
    return out;
}
void* Il2Cpp::make_string(const std::string& text) { return string_new_(text.c_str()); }
void* Il2Cpp::reference_field(void* object, const char* name, const char* expected_type) {
    if(!object) throw std::runtime_error("Null field owner");
    auto field=field_named_(object_class_(object),name);
    if(!field || (field_flags_(field)&0x10) || type(field_type_(field))!=expected_type)
        throw std::runtime_error("Native UI reference field contract unavailable");
    void* value=nullptr;field_value_(object,field,&value);return value;
}
uint32_t Il2Cpp::protect(void* object) {
    if (!object) return 0;
    auto handle = root_(object, true); // pin until this operation finishes
    if (!handle) throw std::runtime_error("Managed GC root allocation failed");
    return handle;
}
void Il2Cpp::release(uint32_t handle) { if (handle) unroot_(handle); }
}
