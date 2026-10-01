#pragma once
#include <string>
#include <string_view>
namespace zml {
enum class Envelope { text, xxtea_base64 };
struct LuaSource { std::string text; Envelope envelope; };
LuaSource unpack_lua(std::string_view bytes);
std::string pack_lua(const LuaSource& source);
}
