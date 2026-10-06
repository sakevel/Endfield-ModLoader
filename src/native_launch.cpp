// Launcher process creation adapter.
#include "win_util.hpp"
#include <TlHelp32.h>
#include <mutex>
#include <vector>
namespace {
using CreateFn = decltype(&CreateProcessW);
CreateFn original = nullptr;
std::mutex gate;
std::wstring target, runtime;
ULONGLONG expires = 0;
DWORD status = 0; // 0 idle, 1 armed, 2 injected, 3 injecting, 4 expired; >=1000 Win32 error
uintptr_t module(DWORD pid, const wchar_t* name) {
    zml::Handle list(CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,pid));
    if (!list) return 0;
    MODULEENTRY32W e{}; e.dwSize=sizeof(e);
    for (BOOL ok=Module32FirstW(list.h,&e);ok;ok=Module32NextW(list.h,&e))
        if (!_wcsicmp(e.szModule,name)) return reinterpret_cast<uintptr_t>(e.modBaseAddr);
    return 0;
}
bool inject(const PROCESS_INFORMATION& child, const std::wstring& dll) {
    auto loader=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW");
    HMODULE owner=nullptr;
    if (!loader || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(loader),&owner)) return false;
    auto ownerName=zml::module_path(owner).filename().wstring(); uintptr_t remote=0;
    auto end=GetTickCount64()+15000;
    do { remote=module(child.dwProcessId,ownerName.c_str()); if (!remote) Sleep(100); }
    while (!remote && GetTickCount64()<end && WaitForSingleObject(child.hProcess,0)==WAIT_TIMEOUT);
    if (!remote) {SetLastError(ERROR_MOD_NOT_FOUND);return false;}
    auto address=reinterpret_cast<LPTHREAD_START_ROUTINE>(remote+reinterpret_cast<uintptr_t>(loader)-reinterpret_cast<uintptr_t>(owner));
    auto size=(dll.size()+1)*sizeof(wchar_t);
    auto memory=VirtualAllocEx(child.hProcess,nullptr,size,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);
    if (!memory) return false;
    SIZE_T written=0;
    if (!WriteProcessMemory(child.hProcess,memory,dll.c_str(),size,&written) || written!=size) {VirtualFreeEx(child.hProcess,memory,0,MEM_RELEASE);return false;}
    zml::Handle thread(CreateRemoteThread(child.hProcess,nullptr,0,address,memory,0,nullptr));
    if (!thread) {VirtualFreeEx(child.hProcess,memory,0,MEM_RELEASE);return false;}
    if (WaitForSingleObject(thread.h,30000)!=WAIT_OBJECT_0) {SetLastError(ERROR_TIMEOUT);return false;} // wait for remote thread completion
    VirtualFreeEx(child.hProcess,memory,0,MEM_RELEASE);
    if (!module(child.dwProcessId,std::filesystem::path(dll).filename().c_str())) {SetLastError(ERROR_DLL_INIT_FAILED);return false;}
    return true;
}
std::wstring executable(LPCWSTR app, LPCWSTR cmd, LPCWSTR cwd) {
    std::wstring p;
    if (app && *app) p=app;
    else if (cmd && *cmd) {
        // Extract executable path
        if (*cmd==L'"') {auto end=wcschr(cmd+1,L'"');if (!end) return {};p.assign(cmd+1,end);}
        else {auto end=wcschr(cmd,L' ');p.assign(cmd,end?end:cmd+wcslen(cmd));}
    }
    if (p.empty()) return {};
    std::filesystem::path path(p);
    if (!path.is_absolute()) { if (!cwd || !*cwd) return {};path=std::filesystem::path(cwd)/path; }
    return std::filesystem::weakly_canonical(path).wstring();
}
BOOL WINAPI create(LPCWSTR app, LPWSTR cmd, LPSECURITY_ATTRIBUTES pa, LPSECURITY_ATTRIBUTES ta,
    BOOL inherit, DWORD flags, LPVOID env, LPCWSTR cwd, LPSTARTUPINFOW si, LPPROCESS_INFORMATION pi) {
    bool owned=false; std::wstring dll, expected;
    try {
        std::lock_guard lock(gate);
        if (status==1 && GetTickCount64()>expires) status=4;
        if (status==1 && !_wcsicmp(executable(app,cmd,cwd).c_str(),target.c_str())) {
            // Ensure non-suspended process creation
            if ((flags&(CREATE_SUSPENDED|DEBUG_PROCESS|DEBUG_ONLY_THIS_PROCESS)) || !pi) {
                status=1000+ERROR_NOT_SUPPORTED;SetLastError(ERROR_NOT_SUPPORTED);return FALSE;
            }
            status=3;owned=true;dll=runtime;expected=target;
        }
    } catch (...) {return original(app,cmd,pa,ta,inherit,flags,env,cwd,si,pi);}
    BOOL ok=original(app,cmd,pa,ta,inherit,flags,env,cwd,si,pi);
    if (!owned) return ok;
    DWORD error=ok?ERROR_SUCCESS:GetLastError(); bool verified=false;
    if (ok) try {
        wchar_t actual[32768]{};DWORD size=32768;
        verified=QueryFullProcessImageNameW(pi->hProcess,0,actual,&size) && !_wcsicmp(actual,expected.c_str());
        if (!verified) error=ERROR_BAD_PATHNAME;
        else if (!inject(*pi,dll)) error=GetLastError()?GetLastError():ERROR_DLL_INIT_FAILED;
    } catch (...) {error=ERROR_DLL_INIT_FAILED;}
    if (error!=ERROR_SUCCESS && ok) {
        // Terminate child on injection failure
        if (verified) {TerminateProcess(pi->hProcess,20);WaitForSingleObject(pi->hProcess,5000);}
        CloseHandle(pi->hThread);CloseHandle(pi->hProcess);*pi={};ok=FALSE;
    }
    {std::lock_guard lock(gate);status=error==ERROR_SUCCESS?2:1000+error;}
    SetLastError(error);return ok;
}
bool install() {
    if (original) return true;
    auto base=reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
    if (dos->e_magic!=IMAGE_DOS_SIGNATURE || nt->Signature!=IMAGE_NT_SIGNATURE) return false;
    auto dir=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT]; if (!dir.VirtualAddress) return false;
    for (auto d=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base+dir.VirtualAddress);d->Name;++d) {
        if (!d->OriginalFirstThunk) continue;
        auto name=reinterpret_cast<IMAGE_THUNK_DATA64*>(base+d->OriginalFirstThunk);
        auto slot=reinterpret_cast<IMAGE_THUNK_DATA64*>(base+d->FirstThunk);
        for (;name->u1.AddressOfData;++name,++slot) {
            if (IMAGE_SNAP_BY_ORDINAL64(name->u1.Ordinal)) continue;
            auto symbol=reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base+name->u1.AddressOfData);
            if (strcmp(reinterpret_cast<char*>(symbol->Name),"CreateProcessW")) continue;
            auto native=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"CreateProcessW");
            if (slot->u1.Function!=reinterpret_cast<ULONGLONG>(native)) return false;
            DWORD protection=0;
            if (!VirtualProtect(&slot->u1.Function,sizeof(void*),PAGE_READWRITE,&protection)) return false;
            original=reinterpret_cast<CreateFn>(native);
            auto previous=InterlockedCompareExchangePointer(reinterpret_cast<PVOID volatile*>(&slot->u1.Function),reinterpret_cast<void*>(create),reinterpret_cast<void*>(native));
            DWORD ignored=0;BOOL restored=VirtualProtect(&slot->u1.Function,sizeof(void*),protection,&ignored);
            if (previous!=reinterpret_cast<void*>(native)) {original=nullptr;return false;}
            if (!restored) {
                InterlockedCompareExchangePointer(reinterpret_cast<PVOID volatile*>(&slot->u1.Function),reinterpret_cast<void*>(native),reinterpret_cast<void*>(create));
                original=nullptr;return false;
            }
            return true;
        }
    }
    return false;
}
struct Request {DWORD version,ttl;wchar_t game[32768],dll[32768];};
}
extern "C" __declspec(dllexport) DWORD WINAPI ZML_ArmNativeLaunch(void* data) {
    if (!data) return ERROR_INVALID_PARAMETER;
    auto r=static_cast<const Request*>(data);
    if (r->version!=1 || r->ttl<5 || r->ttl>120 || !wmemchr(r->game,0,32768) || !wmemchr(r->dll,0,32768)) return ERROR_INVALID_PARAMETER;
    try {
        std::lock_guard lock(gate);
        if (status==3 || (status==1 && GetTickCount64()<=expires)) return ERROR_BUSY;
        auto g=std::filesystem::canonical(r->game),d=std::filesystem::canonical(r->dll);
        if (!g.is_absolute() || !d.is_absolute() || !std::filesystem::is_regular_file(g) || !std::filesystem::is_regular_file(d)) return ERROR_BAD_PATHNAME;
        if (!install()) return ERROR_NOT_SUPPORTED;
        target=g.wstring();runtime=d.wstring();expires=GetTickCount64()+r->ttl*1000ULL;status=1;
        return ERROR_SUCCESS;
    } catch (...) {return ERROR_INVALID_DATA;}
}
extern "C" __declspec(dllexport) DWORD WINAPI ZML_NativeLaunchStatus(void*) {
    std::lock_guard lock(gate);if (status==1 && GetTickCount64()>expires) status=4;return status;
}
extern "C" __declspec(dllexport) DWORD WINAPI ZML_CancelNativeLaunch(void*) {
    std::lock_guard lock(gate);if (status==1) status=0;return status;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if (reason==DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
