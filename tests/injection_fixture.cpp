#include <Windows.h>
#include <filesystem>
#include <fstream>
// Harmless owned child for injector tests. No game, accounts or anti-cheat.
int wmain(int argc, wchar_t** argv) {
    auto deadline = GetTickCount64() + 30000;
    while (!GetModuleHandleW(L"ZMLRuntime.dll") && GetTickCount64() < deadline) Sleep(100);
    bool loaded = GetModuleHandleW(L"ZMLRuntime.dll") != nullptr;
    if (argc == 3 && std::wstring_view(argv[1]) == L"--marker")
        std::ofstream(std::filesystem::path(argv[2])) << (loaded ? "runtime_loaded" : "timeout");
    Sleep(3000); // leave time for the injector's module verification
    return loaded ? 0 : 1;
}
