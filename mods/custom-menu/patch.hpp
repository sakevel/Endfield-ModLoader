#pragma once
#include <algorithm>
#include <string>
#include <string_view>
#include <vector>
namespace menu_mod {
inline bool edit(std::string_view input, std::string_view extension, std::string& output) {
    if (input.find("ZMLCustomMenu") != input.npos) return false;
    const std::vector<std::string_view> anchors{
        "table.sort(RIGHT_BTN_ORDER)", "self:BuildData()", "HL.Commit(WatchCtrl)"};
    struct Insert { size_t position; std::string bytes; };
    std::vector<Insert> changes;
    for (size_t i = 0; i < anchors.size(); ++i) {
        auto at = input.find(anchors[i]);
        if (at == input.npos || input.find(anchors[i], at + anchors[i].size()) != input.npos) return false;
        if (i == 0) changes.push_back({at, "RIGHT_BTN_ORDER[#RIGHT_BTN_ORDER + 1] = 93\n"});
        if (i == 1) changes.push_back({at + anchors[i].size(), "\n    _G.ZMLCustomMenu.mount(self)"});
        if (i == 2) changes.push_back({at, std::string(extension) + "\n"});
    }
    for (auto required : {"local RIGHT_BTN_ORDER = {}", "WatchCtrl._RelayoutRightList", "PhaseManager:OpenPhase(data.phaseId, data.openPhaseArg)"})
        if (input.find(required) == input.npos) return false;
    std::sort(changes.begin(), changes.end(), [](const auto& a, const auto& b) { return a.position > b.position; });
    auto result = std::string(input);
    for (auto& change : changes) result.insert(change.position, change.bytes);
    output = std::move(result); return true;
}
}
