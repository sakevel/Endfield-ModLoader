#include "mod_services.hpp"
#include "win_util.hpp"
#include "runtime_bootstrap.hpp"
#include <fstream>
#include <iostream>
#include <io.h>
#include <fcntl.h>
using namespace zml;
namespace {
void check(bool value) {if(!value)throw std::runtime_error("Activation assertion failed");}
void write(const std::filesystem::path& file,const std::string& text) {std::ofstream(file,std::ios::binary)<<text;}
}
int main(int argc,char**) {try {
    for(auto pair : {std::pair{"Common/Utils/UIUtils","_G.UIUtils = UIUtils"},
        std::pair{"UI/Panels/BattleAction/BattleActionCtrl","HL.Commit(BattleActionCtrl)"},
        std::pair{"UI/Panels/Watch/WatchCtrl","HL.Commit(WatchCtrl)"}}) {
        std::string source=pair.second;
        check(runtime_bootstrap(pair.first,source) && source.find("LoadLua('ZML/Api')")!=source.npos);
        source=std::string(pair.second)+"\n"+pair.second;auto before=source;
        check(!runtime_bootstrap(pair.first,source) && source==before);
    }
    {
        Manifest active,lib,child,missing,cycleA,cycleB;
        active.id="active";
        lib.id="lib";lib.enabled=false;lib.runtime_toggle=true;
        child=lib;child.id="child";child.depends={"lib"};
        missing=lib;missing.id="missing";missing.depends={"absent"};
        cycleA=lib;cycleA.id="cycle-a";cycleA.depends={"cycle-b"};
        cycleB=lib;cycleB.id="cycle-b";cycleB.depends={"cycle-a"};
        auto ordered=resident_order({active,lib,child,missing,cycleA,cycleB});
        for(auto& m:ordered) check(m.enabled==(m.id=="active"||m.id=="lib"||m.id=="child"));
        lib.runtime_toggle=false;
        ordered=resident_order({active,lib,child});
        for(auto& m:ordered) check(m.enabled==(m.id=="active"));
    }
    auto root=std::filesystem::temp_directory_path()/(L"zml-activation-"+std::to_wstring(GetCurrentProcessId()));
    std::filesystem::create_directories(root);
    struct Cleanup {std::filesystem::path p;~Cleanup(){std::error_code e;std::filesystem::remove_all(p,e);}} cleanup{root};
    auto manifest=[&](std::string id,bool enabled,bool toggle=true,std::string version="1") {
        std::filesystem::create_directories(root/id);
        write(root/id/"mod.ini","[mod]\nid="+id+"\napi=1\nlibrary=Fixture.dll\nversion="+version+
            "\nenabled="+(enabled?"true":"false")+"\nruntime_toggle="+(toggle?"true":"false")+
            "\n"+(id=="consumer"?"depends=library\n":""));
    };
    manifest("library",true);manifest("consumer",true);manifest("legacy",true,false);
    ModServices services;
    std::ifstream input(std::filesystem::path(ZML_SOURCE_DIR)/"lua/zml.lua",std::ios::binary);
    services.set_api({std::istreambuf_iterator<char>(input),{}});
    std::vector<std::shared_ptr<std::atomic_bool>> flags;
    for(auto id:{"library","consumer","legacy"}) {
        auto record=prepare_record(read_manifest(root/id/"mod.ini"),root/"state"/id);
        flags.push_back(record.active);services.publish(std::move(record));
    }
    if(argc>1) {
        _setmode(_fileno(stdout),_O_BINARY);
        std::string request;
        while(std::getline(std::cin,request)) {
            std::string result;
            if(request.starts_with("test/")) {
                auto slash=request.find('/',5);auto id=request.substr(5,slash-5);auto value=request.substr(slash+1);
                if((id!="library" && id!="consumer" && id!="legacy") || (value!="0" && value!="1"))throw std::runtime_error("Bad fixture command");
                manifest(id,value=="1",id!="legacy");result="return {ok=true}";
            } else result=services.module(request);
            std::cout<<result.size()<<'\n'<<result<<std::flush;
        }
        return 0;
    }
    check(services.is_enabled("library") && !services.is_enabled("absent"));
    manifest("library",false);
    check(services.module("ZML/Activation/Desired").find("ok=false")!=std::string::npos);
    check(flags[0]->load() && flags[1]->load()); // invalid dependency never partially disables
    manifest("consumer",false);
    check(services.module("ZML/Activation/Desired").find("mask=\"001\"")!=std::string::npos);
    check(services.module("ZML/Activation/Commit/111").find("ok=false")!=std::string::npos);
    check(flags[0]->load()); // stale plan preserves current flags
    check(services.module("ZML/Activation/Commit/001").find("ok=true")!=std::string::npos);
    check(!flags[0]->load() && !flags[1]->load() && flags[2]->load());
    check(services.registry_lua().find("enabled=false")!=std::string::npos);
    manifest("library",true);manifest("consumer",true);
    check(services.module("ZML/Activation/Commit/111").find("ok=true")!=std::string::npos);
    check(flags[0]->load() && flags[1]->load());
    manifest("legacy",false,false);
    check(services.module("ZML/Activation/Commit/110").find("ok=false")!=std::string::npos && flags[2]->load());
    manifest("legacy",true,false);manifest("consumer",true,true,"2");
    check(services.module("ZML/Activation/Desired").find("restart required")!=std::string::npos);
    manifest("consumer",true);write(root/"consumer/mod.ini","invalid");
    check(services.module("ZML/Activation/Desired").find("ok=false")!=std::string::npos && flags[1]->load());
    std::cout<<"PASS activation atomic preflight, dependency, stale-plan, legacy, changed-package, malformed rollback\n";
    return 0;
}catch(std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
