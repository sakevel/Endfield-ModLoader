#include "lua_codec.hpp"
#include "manifest.hpp"
#include "version_label.hpp"
#include <Windows.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
namespace {
void check(bool result, const char* name) { if (!result) throw std::runtime_error(name); }
template<class F> void rejected(F&& f, const char* name) { bool failed = false; try { f(); } catch (...) { failed = true; } check(failed, name); }
std::string read(const char* path) {
    std::ifstream stream(path, std::ios::binary); check(bool(stream), "fixture exists");
    return {std::istreambuf_iterator<char>(stream), {}};
}
}
int main(int argc, char** argv) {
    try {
        using namespace zml;
        check(version_label("CN_WIN_REL_1.0.14_C1_E2","0.4.0")=="CN_WIN_REL_1.0.14_C1_E2   |   ZML 0.4.0","Native version preserved beside loader version");
        check(version_label(version_label("native","0.4.0"),"0.4.0")==version_label("native","0.4.0"),"Version label is idempotent");
        check(version_label("","0.4.0").empty() && version_label(std::string(1025,'x'),"0.4.0")==std::string(1025,'x'),"Invalid version label is not changed");
        for (auto text : {std::string("return 1"), std::string("-- 测试\nreturn '✓'"), std::string("--") + std::string(8191, 'x')}) {
            LuaSource source{text, Envelope::xxtea_base64};
            auto packed = pack_lua(source);
            auto unpacked = unpack_lua(packed);
            check(unpacked.text == text && unpacked.envelope == source.envelope, "XXTEA envelope roundtrip");
            check(pack_lua(unpacked) == packed, "Canonical envelope reproduced");
        }
        check(unpack_lua("return 42\n").envelope == Envelope::text, "Plain Lua supported");
        for (const char* bad : {"AB==", "a===", "AAA="}) rejected([&] { unpack_lua(bad); }, "Malformed data rejected");
        rejected([&] { unpack_lua(std::string("return 1\0", 9)); }, "Binary NUL rejected");
        rejected([&] { unpack_lua(std::string(1024 * 1024 + 1, 'A')); }, "Oversized payload rejected");
        auto folder = std::filesystem::temp_directory_path() / ("zml-tests-" + std::to_string(GetCurrentProcessId()));
        std::filesystem::create_directories(folder / "example");
        struct Cleanup { std::filesystem::path p; ~Cleanup() { std::error_code e; std::filesystem::remove_all(p, e); } } clean{folder};
        auto file = folder / "example/mod.ini";
        auto manifest = [&](const std::string& library, const std::string& enabled = "true", const std::string& more = "") {
            std::ofstream(file) << "[mod]\nid=example\nname=Example\napi=1\nlibrary=" << library << "\nenabled=" << enabled << '\n' << more;
        };
        manifest("Example.dll"); check(discover(folder).size() == 1, "Mod discovered");
        manifest("Example.dll", "false"); check(!read_manifest(file).enabled, "Disabled mod preserved");
        for (auto path : {"../escape.dll", "C:\\escape.dll", "code.exe"}) {
            manifest(path); rejected([&] { read_manifest(file); }, "Unsafe plugin path rejected");
        }
        manifest("Example.dll", "yes"); rejected([&] { read_manifest(file); }, "Invalid boolean rejected");
        manifest("Example.dll", "true", "api=1\n"); rejected([&] { read_manifest(file); }, "Duplicate keys rejected");
        manifest("Example.dll");
        std::filesystem::create_directories(folder / "duplicate");
        std::filesystem::copy_file(file, folder / "duplicate/mod.ini");
        rejected([&] { discover(folder); }, "Duplicate ids rejected");
        if (argc > 2) {
            auto raw = read(argv[1]), current = read(argv[2]);
            auto decoded = unpack_lua(raw);
            check(decoded.text == current, "Current installed client's resource decoded exactly");
            check(pack_lua(decoded) == raw, "Current installed client's bytes reproduced exactly");

        }
        std::cout << "PASS: codec, manifests and optional real-client fixtures\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << "FAIL: " << e.what() << '\n'; return 1; }
}
