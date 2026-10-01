#include "lua_codec.hpp"
#include <Windows.h>
#include <wincrypt.h>
#include <array>
#include <cstdint>
#include <stdexcept>
#include <vector>
namespace zml {
namespace {
constexpr uint32_t golden = 0x9e3779b9;
// Client resource encryption key
constexpr std::string_view key_bytes = "d41d8cd98f00b204";
uint32_t word(std::string_view s, size_t offset) {
    uint32_t value = 0;
    for (size_t k = 0; k != 4 && offset + k < s.size(); ++k)
        value |= static_cast<uint32_t>(static_cast<uint8_t>(s[offset + k])) << (8 * k);
    return value;
}
std::string crypt(std::string_view data, bool forward) {
    if (data.empty() || data.size() > 1024 * 1024 || (!forward && (data.size() % 4 || data.size() < 8)))
        throw std::runtime_error("Invalid XXTEA payload size");
    size_t blocks = forward ? (data.size() + 3) / 4 + 1 : data.size() / 4;
    std::vector<uint32_t> words(blocks);
    for (size_t i = 0; i < blocks; ++i) words[i] = word(data, i * 4);
    if (forward) words.back() = static_cast<uint32_t>(data.size());
    std::array<uint32_t, 4> key{};
    for (size_t i = 0; i < key.size(); ++i) key[i] = word(key_bytes, i * 4);
    auto mix = [&](uint32_t left, uint32_t right, uint32_t sum, size_t index) {
        return (((left >> 5) ^ (right << 2)) + ((right >> 3) ^ (left << 4))) ^
               ((sum ^ right) + (key[(index & 3) ^ ((sum >> 2) & 3)] ^ left));
    };
    unsigned cycles = 6 + 52 / static_cast<unsigned>(blocks);
    uint32_t sum = forward ? 0 : golden * cycles;
    uint32_t carry = forward ? words.back() : words.front();
    for (unsigned cycle = 0; cycle < cycles; ++cycle) {
        if (forward) {
            sum += golden;
            for (size_t i = 0; i < blocks; ++i) {
                words[i] += mix(carry, words[(i + 1) % blocks], sum, i);
                carry = words[i];
            }
        } else {
            for (size_t i = blocks; i-- > 0;) {
                words[i] -= mix(words[(i + blocks - 1) % blocks], carry, sum, i);
                carry = words[i];
            }
            sum -= golden;
        }
    }
    size_t size = forward ? blocks * 4 : words.back();
    if (!forward && (size > (blocks - 1) * 4 || size < blocks * 4 - 7))
        throw std::runtime_error("XXTEA length check failed");
    std::string result(size, '\0');
    for (size_t i = 0; i < size; ++i) result[i] = static_cast<char>(words[i / 4] >> (i % 4 * 8));
    return result;
}
std::string b64(std::string_view input, bool encode) {
    DWORD size = 0;
    if (encode) {
        if (!CryptBinaryToStringA(reinterpret_cast<const BYTE*>(input.data()), static_cast<DWORD>(input.size()),
            CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &size)) throw std::runtime_error("Base64 encoding failed");
        std::string output(size, '\0');
        CryptBinaryToStringA(reinterpret_cast<const BYTE*>(input.data()), static_cast<DWORD>(input.size()),
            CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, output.data(), &size);
        output.resize(size); return output;
    }
    if (input.empty() || input.size() % 4 || input.find_first_not_of("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=") != std::string_view::npos)
        throw std::runtime_error("Not canonical Base64");
    if (!CryptStringToBinaryA(input.data(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_STRICT,
        nullptr, &size, nullptr, nullptr)) throw std::runtime_error("Base64 decoding failed");
    std::string output(size, '\0');
    if (!CryptStringToBinaryA(input.data(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_STRICT,
        reinterpret_cast<BYTE*>(output.data()), &size, nullptr, nullptr)) throw std::runtime_error("Base64 decoding failed");
    output.resize(size);
    if (b64(output, true) != input) throw std::runtime_error("Noncanonical Base64 padding");
    return output;
}
}
LuaSource unpack_lua(std::string_view bytes) {
    if (bytes.empty() || bytes.size() > 1024 * 1024) throw std::runtime_error("Lua payload exceeds size limit");
    if (bytes.find_first_not_of("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=") == std::string_view::npos) {
        auto source = crypt(b64(bytes, false), false);
        if (source.empty() || source.find('\0') != std::string::npos) throw std::runtime_error("Decrypted Lua contains NUL");
        return {std::move(source), Envelope::xxtea_base64};
    }
    if (bytes.find('\0') != std::string_view::npos || bytes.starts_with("\x1bLua")) throw std::runtime_error("Lua binary format unsupported");
    return {std::string(bytes), Envelope::text};
}
std::string pack_lua(const LuaSource& source) {
    if (source.text.empty() || source.text.find('\0') != std::string::npos || source.text.size() > 768 * 1024)
        throw std::runtime_error("Invalid transformed Lua source");
    return source.envelope == Envelope::text ? source.text : b64(crypt(source.text, true), true);
}
}
