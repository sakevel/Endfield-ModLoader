#include "manifest.hpp"
#include "lua_modules.hpp"
#include <Windows.h>
#include <filesystem>
#include <fstream>
#include <iostream>
void check(bool x){if(!x)throw std::runtime_error("Dependency/source test failed");}
template<class F>void reject(F f){bool no=false;try{f();}catch(...){no=true;}check(no);}
int provide(void* mode,const char*,ZmlSink sink,void* out) {
    auto n=reinterpret_cast<uintptr_t>(mode);
    if(n==1)return 0;
    if(n==2){sink(out,"return 1",8);sink(out,"return 2",8);return 1;}
    if(n==3){sink(out,"a\0b",3);return 1;}
    sink(out,"return 42",9);return 1;
}
int main(){try {
    using namespace zml;auto dir=std::filesystem::temp_directory_path()/std::to_string(GetCurrentProcessId());
    dir/="zml-dep-fixture";std::filesystem::create_directories(dir);
    struct Cleanup{std::filesystem::path p;~Cleanup(){std::error_code e;std::filesystem::remove_all(p,e);}} cleanup{dir};
    auto write=[&](std::string id,std::string more=""){
        std::filesystem::create_directories(dir/id);std::ofstream(dir/id/"mod.ini")<<"[mod]\nid="<<id<<"\napi=1\nlibrary=Fixture.dll\nenabled=true\n"<<more;
    };
    write("alpha","depends=library\n");write("library");write("third","depends=alpha,library\n");write("beta");
    auto mods=discover(dir);check(mods.size()==4 && mods[0].id=="beta" && mods[1].id=="library" && mods[2].id=="alpha" && mods[3].id=="third");
    check(!dependencies_loaded(mods[2],{}) && dependencies_loaded(mods[2],{"library"}));
    check(!dependencies_loaded(mods[3],{"library"})); // failed dependency blocks transitive consumer before DLL start
    for(auto bad:{"alpha","library,library","library,","", "../outside"}){write("alpha",std::string("depends=")+bad+"\n");reject([&]{discover(dir);});}
    write("alpha","depends=absent\n");reject([&]{discover(dir);});
    write("alpha","depends=library\n");write("library","depends=third\n");reject([&]{discover(dir);});
    write("library");write("disabled","depends=absent\n");
    {std::ofstream f(dir/"disabled/mod.ini");f<<"[mod]\nid=disabled\napi=1\nlibrary=Fixture.dll\nenabled=false\ndepends=absent\n";}
    check(discover(dir).size()==5);
    {std::ofstream f(dir/"library/mod.ini");f<<"[mod]\nid=library\napi=1\nlibrary=Fixture.dll\nenabled=false\n";}
    reject([&]{discover(dir);});
    LuaModules routes;check(routes.add("fixture",provide,nullptr));check(!routes.add("fixture",provide,nullptr));
    check(routes.module("ZML/Mod/fixture/Test")=="return 42");
    for(auto p:{"ZML/Mod/fixture/../secret","ZML/Mod/fixture//Test","ZML/Mod/fixture/Test/","ZML/Mod/absent/Test","ZML/Api","ZML/Mod/fixture/C:/x"})reject([&]{routes.module(p);});
    routes.remove("fixture");reject([&]{routes.module("ZML/Mod/fixture/Test");});
    for(uintptr_t mode:{1,2,3}){check(routes.add("fixture",provide,reinterpret_cast<void*>(mode)));reject([&]{routes.module("ZML/Mod/fixture/Test");});routes.remove("fixture");}
    check(sizeof(ZmlHost)==48); // frozen original x64 ABI1
    std::cout<<"PASS dependency preflight, stable order, failed chain, owned Lua sources and rollback\n";return 0;
}catch(std::exception& e){std::cerr<<e.what()<<'\n';return 1;}}
