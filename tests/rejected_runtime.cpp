#include <Windows.h>
BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    return reason != DLL_PROCESS_ATTACH; // intentional LoadLibrary failure
}
