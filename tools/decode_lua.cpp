// Offline Lua decoding utility.
#include "lua_codec.hpp"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
int main(int argc, char** argv) {
    try {
        if (argc != 3) throw std::runtime_error("usage: DecodeLua <resource> <output>");
        const auto input = std::filesystem::absolute(argv[1]);
        const auto output = std::filesystem::absolute(argv[2]);
        if (input == output || std::filesystem::exists(output)) throw std::runtime_error("Refusing overwrite");
        std::ifstream stream(input, std::ios::binary);
        if (!stream) throw std::runtime_error("Cannot read resource");
        std::string bytes{std::istreambuf_iterator<char>(stream), {}};
        auto source = zml::unpack_lua(bytes);
        if (zml::pack_lua(source) != bytes) throw std::runtime_error("Envelope roundtrip differs");
        std::filesystem::create_directories(output.parent_path());
        std::ofstream result(output, std::ios::binary);
        result.write(source.text.data(), source.text.size()); result.close();
        if (!result) throw std::runtime_error("Cannot write output");
        std::cout << "PASS: decode and byte-exact envelope roundtrip; " << source.text.size() << " bytes\n";
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
