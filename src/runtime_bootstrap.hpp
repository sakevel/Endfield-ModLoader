#pragma once
#include <string>
#include <string_view>
namespace zml {
inline bool runtime_bootstrap(std::string_view name,std::string& source) {
    std::string anchor;
    if(name=="Common/Utils/UIUtils")anchor="_G.UIUtils = UIUtils";
    else if(name=="UI/Panels/BattleAction/BattleActionCtrl")anchor="HL.Commit(BattleActionCtrl)";
    else if(name=="UI/Panels/Watch/WatchCtrl")anchor="HL.Commit(WatchCtrl)";
    else return false;
    auto at=source.find(anchor);
    if(at==source.npos || source.find(anchor,at+anchor.size())!=source.npos)return false;
    const std::string boot="do local ok,e=pcall(function() local api=assert(loadstring(LuaManagerInst:LoadLua('ZML/Api'),'@ZML/Api'))();api.attach_runtime() end);if not ok then logger.error('ZML lifecycle bootstrap failed',e) end end\n";
    if(source.size()+boot.size()>768*1024)return false;
    source.insert(at,boot);return true;
}
}
