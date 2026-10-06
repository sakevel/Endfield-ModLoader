#pragma once
#include <Windows.h>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <utility>

namespace zml {
inline const wchar_t* random_full_name_w() {
    static const wchar_t* const names[] = { L"ZMDModLoader", L"ZeroModLoader", L"ZMLModLoader" };
    return names[GetTickCount() % 3];
}
inline const char* random_full_name_a() {
    static const char* const names[] = { "ZMDModLoader", "ZeroModLoader", "ZMLModLoader" };
    return names[GetTickCount() % 3];
}
inline std::filesystem::path path_utf8(std::string_view s) {
    return std::filesystem::path(std::u8string_view(reinterpret_cast<const char8_t*>(s.data()), s.size()));
}
struct Handle {
    HANDLE h = nullptr;
    explicit Handle(HANDLE value = nullptr) : h(value) {}
    ~Handle() { if (h && h != INVALID_HANDLE_VALUE) CloseHandle(h); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    explicit operator bool() const { return h && h != INVALID_HANDLE_VALUE; }
};
inline std::string utf8(std::wstring_view s) {
    if (s.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), nullptr, 0, nullptr, nullptr);
    if (!n) throw std::runtime_error("Invalid Unicode path");
    std::string out(n, '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), out.data(), n, nullptr, nullptr);
    return out;
}
inline std::filesystem::path module_path(HMODULE module = nullptr) {
    std::wstring s(32768, '\0');
    DWORD n = GetModuleFileNameW(module, s.data(), static_cast<DWORD>(s.size()));
    if (!n || n >= s.size()) throw std::runtime_error("GetModuleFileNameW failed");
    s.resize(n); return s;
}
inline std::filesystem::path state_root() {
    wchar_t s[32768];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", s, 32768);
    if (!n || n >= 32768) throw std::runtime_error("LOCALAPPDATA unavailable");
    return std::filesystem::path(s) / L"ZML";
}
inline bool inside(const std::filesystem::path& base, const std::filesystem::path& file) {
    auto b = std::filesystem::weakly_canonical(base), f = std::filesystem::weakly_canonical(file);
    auto bi = b.begin(), fi = f.begin();
    for (; bi != b.end(); ++bi, ++fi)
        if (fi == f.end() || _wcsicmp(bi->c_str(), fi->c_str())) return false;
    return fi != f.end();
}
} // namespace zml
