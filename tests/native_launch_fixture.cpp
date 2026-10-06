#include <Windows.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
// Owned native launcher flow fixture; contains an actual CreateProcessW IAT import.
int wmain(int argc,wchar_t** argv) {
    if (argc!=4) return 2;
    std::wstring game=argv[1],marker=argv[2],unrelated=argv[3];
    std::string mode;
    while (std::getline(std::cin,mode)) {
        if(mode=="q") break;
        auto chosen=mode=="u"?unrelated:game;
        auto out=marker+(mode=="u"?L".unrelated":L".child");
        std::wstring cmd=L"\""+chosen+L"\" --marker \""+out+L"\""+(mode=="d"?L" -force-d3d11":L"");
        STARTUPINFOW si{};si.cb=sizeof(si);PROCESS_INFORMATION pi{};
        BOOL ok=CreateProcessW(chosen.c_str(),cmd.data(),nullptr,nullptr,FALSE,0,nullptr,std::filesystem::path(chosen).parent_path().c_str(),&si,&pi);
        DWORD error=GetLastError();
        // Native post-start callback simulation
        std::ofstream(std::filesystem::path(marker))<<(ok?"native_started ":"native_failed ")<<pi.dwProcessId<<" "<<error;
        std::cout<<"START "<<pi.dwProcessId<<" "<<ok<<" "<<error<<std::endl;
        if(ok){CloseHandle(pi.hThread);CloseHandle(pi.hProcess);}
    }
    return 0;
}
