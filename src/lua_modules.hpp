#pragma once
#include "zml_lua_service.h"
#include <map>
#include <stdexcept>
#include <string>
#include <string_view>
namespace zml {
class LuaModules {
    struct Provider {ZmlLuaSource fn; void* data;};
    std::map<std::string,Provider> providers;
public:
    bool add(const std::string& id, ZmlLuaSource fn, void* data) {
        if (id.empty() || id.size()>96 || id=="." || id==".." ||
            id.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789.-_")!=id.npos || !fn) return false;
        return providers.emplace(id,Provider{fn,data}).second;
    }
    void remove(const std::string& id) { providers.erase(id); }
    std::string module(std::string_view name) const {
        if (!name.starts_with("ZML/Mod/") || name.size()>512) throw std::runtime_error("Invalid Mod module namespace");
        name.remove_prefix(8); auto slash=name.find('/');
        if (slash==name.npos) throw std::runtime_error("Missing Mod module path");
        auto owner=std::string(name.substr(0,slash)), path=std::string(name.substr(slash+1));
        if (path.empty() || path.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_/")!=path.npos)
            throw std::runtime_error("Invalid Mod module path");
        for (size_t at=0;at<=path.size();) {
            auto end=path.find('/',at);if(end==path.npos)end=path.size();auto part=path.substr(at,end-at);
            if(part.empty() || part=="." || part=="..")throw std::runtime_error("Mod module traversal rejected");
            at=end+1;
        }
        auto it=providers.find(owner);if(it==providers.end())throw std::runtime_error("Mod source provider not loaded");
        struct Output {std::string text; unsigned calls=0; bool valid=true;} out;
        auto sink=+[](void* data,const char* text,size_t size) {
            auto& o=*static_cast<Output*>(data);
            if(++o.calls!=1 || !text || !size || size>768*1024){o.valid=false;return;}
            o.text.assign(text,size);if(o.text.find('\0')!=o.text.npos)o.valid=false;
        };
        int accepted=0;
        try {accepted=it->second.fn(it->second.data,path.c_str(),sink,&out);}catch(...){throw std::runtime_error("Mod source provider threw");}
        if(!accepted || !out.valid || out.calls!=1)
            throw std::runtime_error("Mod source provider rejected request");
        return out.text;
    }
};
}
