#include "manifest.hpp"
#include "win_util.hpp"
#include <TlHelp32.h>
#include <fstream>
#include <iostream>
#include <vector>
namespace {
using namespace zml;
std::wstring argument(std::wstring_view text) {
    std::wstring out = L"\"";
    size_t slashes = 0;
    for (wchar_t ch : text) {
        if (ch == '\\') { ++slashes; continue; }
        if (ch == '"') out.append(slashes * 2 + 1, '\\');
        else out.append(slashes, '\\');
        slashes = 0; out += ch;
    }
    out.append(slashes * 2, '\\'); out += L'"'; return out;
}
uintptr_t module_base(DWORD pid, const std::wstring& name) {
    Handle list(CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid));
    if (!list) return 0;
    MODULEENTRY32W entry{}; entry.dwSize = sizeof(entry);
    for (BOOL ok = Module32FirstW(list.h, &entry); ok; ok = Module32NextW(list.h, &entry))
        if (!_wcsicmp(entry.szModule, name.c_str())) return reinterpret_cast<uintptr_t>(entry.modBaseAddr);
    return 0;
}
bool already_running(const std::filesystem::path& game) {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    if (!snapshot) throw std::runtime_error("Cannot enumerate processes");
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    for (BOOL ok = Process32FirstW(snapshot.h, &entry); ok; ok = Process32NextW(snapshot.h, &entry))
        if (!_wcsicmp(entry.szExeFile, game.filename().c_str())) return true;
    return false;
}
void inject(const PROCESS_INFORMATION& child, const std::filesystem::path& runtime) {
    auto address = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW");
    HMODULE owner = nullptr;
    if (!address || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(address), &owner)) throw std::runtime_error("Local LoadLibraryW unavailable");
    auto owner_name = module_path(owner).filename().wstring();
    uintptr_t remote_owner = 0;
    const auto limit = GetTickCount64() + 15000;
    do {
        if (WaitForSingleObject(child.hProcess, 0) == WAIT_OBJECT_0) throw std::runtime_error("Client exited before injection");
        remote_owner = module_base(child.dwProcessId, owner_name);
        if (!remote_owner) Sleep(100);
    } while (!remote_owner && GetTickCount64() < limit);
    if (!remote_owner) throw std::runtime_error("Target loader module unavailable");
    auto remote_loader = reinterpret_cast<LPTHREAD_START_ROUTINE>(remote_owner +
        reinterpret_cast<uintptr_t>(address) - reinterpret_cast<uintptr_t>(owner));
    auto value = runtime.wstring();
    size_t size = (value.size() + 1) * sizeof(wchar_t);
    auto storage = VirtualAllocEx(child.hProcess, nullptr, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!storage) throw std::runtime_error("Remote path allocation failed");
    size_t copied = 0;
    if (!WriteProcessMemory(child.hProcess, storage, value.c_str(), size, &copied) || copied != size) {
        VirtualFreeEx(child.hProcess, storage, 0, MEM_RELEASE);
        throw std::runtime_error("Remote path write failed");
    }
    Handle thread(CreateRemoteThread(child.hProcess, nullptr, 0, remote_loader, storage, 0, nullptr));
    if (!thread) {
        VirtualFreeEx(child.hProcess, storage, 0, MEM_RELEASE);
        throw std::runtime_error("Remote LoadLibraryW denied: " + std::to_string(GetLastError()));
    }
    if (WaitForSingleObject(thread.h, 30000) != WAIT_OBJECT_0)
        throw std::runtime_error("Remote loader timeout");
    VirtualFreeEx(child.hProcess, storage, 0, MEM_RELEASE);
    if (!module_base(child.dwProcessId, runtime.filename().wstring()))
        throw std::runtime_error("Runtime DLL was not loaded");
}
void require_x64(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    IMAGE_DOS_HEADER dos{};
    file.read(reinterpret_cast<char*>(&dos), sizeof(dos));
    if (!file || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < static_cast<LONG>(sizeof(dos))) throw std::runtime_error("Invalid PE image");
    file.seekg(dos.e_lfanew);
    DWORD signature = 0;
    IMAGE_FILE_HEADER header{};
    file.read(reinterpret_cast<char*>(&signature), sizeof(signature));
    file.read(reinterpret_cast<char*>(&header), sizeof(header));
    if (!file || signature != IMAGE_NT_SIGNATURE || header.Machine != IMAGE_FILE_MACHINE_AMD64)
        throw std::runtime_error("Target and plugins must be x64 PE images");
}
}
int wmain(int argc, wchar_t** argv) {
    try {
        auto root = module_path().parent_path();
        wchar_t configured[32768]{};
        GetPrivateProfileStringW(L"loader", L"game", L"", configured, 32768, (root / L"loader.ini").c_str());
        std::filesystem::path game(configured);
        bool list = false, dry = false;
        std::vector<std::wstring> extra;
        for (int i = 1; i < argc; ++i) {
            std::wstring_view flag(argv[i]);
            if (flag == L"--game" && i + 1 < argc) game = argv[++i];
            else if (flag == L"--list") list = true;
            else if (flag == L"--dry-run") dry = true;
            else if (flag == L"--") { for (++i; i < argc; ++i) extra.emplace_back(argv[i]); break; }
            else if (flag == L"--help") {
                std::wcout << random_full_name_w() << L" [--game Endfield.exe] [--list|--dry-run] [-- game arguments]\n"; return 0;
            } else throw std::runtime_error("Unknown/incomplete option");
        }
        auto mods = discover(root / L"mods");
        for (auto& mod : mods)
            std::cout << (mod.enabled ? "[on]  " : "[off] ") << mod.id << " | " << mod.title << '\n';
        if (list) return 0;
        auto runtime = root / L"ZMLRuntime.dll";
        if (!std::filesystem::is_regular_file(game) || !std::filesystem::is_regular_file(runtime)) throw std::runtime_error("Game/runtime file missing");
        game = std::filesystem::canonical(game);
        require_x64(game); require_x64(runtime);
        for (auto& mod : mods) if (mod.enabled) require_x64(mod.library);
        if (dry) { std::wcout << L"Validation passed.\n"; return 0; }
        if (already_running(game)) throw std::runtime_error("The game is already running; please close it first.");
        std::wstring command = argument(game.wstring());
        for (auto& arg : extra) command += L" " + argument(arg);
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        PROCESS_INFORMATION child{};
        if (!CreateProcessW(game.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_SUSPENDED, nullptr,
            game.parent_path().c_str(), &startup, &child)) throw std::runtime_error("CreateProcessW failed: " + std::to_string(GetLastError()));
        Handle process(child.hProcess), primary(child.hThread);
        std::cout << "Created PID " << child.dwProcessId << '\n';
        try {
            if (ResumeThread(primary.h) == static_cast<DWORD>(-1)) throw std::runtime_error("Client resume failed");
            inject(child, runtime);
        } catch (...) {
            // This handle belongs ONLY to the child just created by us.
            TerminateProcess(process.h, 20);
            WaitForSingleObject(process.h, 5000);
            throw;
        }
        std::cout << "Runtime loaded into PID " << child.dwProcessId << ".\n";
        std::wcout << L"Log: " << (state_root() / L"runtime.log").wstring() << '\n';
        return 0;
    } catch (const std::exception& error) {
        std::cerr << random_full_name_a() << ": " << error.what() << '\n'; return 1;
    }
}
