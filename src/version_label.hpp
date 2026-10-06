#pragma once
#include <string>
#include <string_view>
namespace zml {
inline std::string version_label(std::string_view native, std::string_view version) {
    if(native.empty() || native.size()>1024 || native.find("ZML ")!=std::string_view::npos)return std::string(native);
    return std::string(native)+"   |   ZML "+std::string(version);
}
}
