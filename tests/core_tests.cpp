#include "lua_codec.hpp"
#include "manifest.hpp"
#include "patch.hpp"
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
        std::string base = "local RIGHT_BTN_ORDER = {}\ntable.sort(RIGHT_BTN_ORDER)\nself:BuildData()\nWatchCtrl._RelayoutRightList\nPhaseManager:OpenPhase(data.phaseId, data.openPhaseArg)\nHL.Commit(WatchCtrl)";
        std::string result = "untouched";
        check(menu_mod::edit(base, "local ZMLCustomMenu = {}", result), "Current contract patched");
        check(result.find("RIGHT_BTN_ORDER[#RIGHT_BTN_ORDER + 1] = 93") != result.npos, "Slot registered");
        check(result.find("_G.ZMLCustomMenu.mount(self)") != result.npos, "Attachment inserted before snapshot");
        auto patched = result;
        check(!menu_mod::edit(patched, "extension", result) && result == patched, "No duplicate patch");
        for (auto anchor : {"self:BuildData()", "HL.Commit(WatchCtrl)", "table.sort(RIGHT_BTN_ORDER)"}) {
            auto broken = base; broken.erase(broken.find(anchor), std::string_view(anchor).size());
            check(!menu_mod::edit(broken, "extension", result) && result == patched, "Missing anchor is atomic");
            check(!menu_mod::edit(base + anchor, "extension", result) && result == patched, "Ambiguous anchor is atomic");
        }
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
            auto extension = argc > 3 ? read(argv[3]) : "local ZMLCustomMenu = {}";
            check(menu_mod::edit(decoded.text, extension, result), "Real current WatchCtrl accepted");
            decoded.text = result;
            check(unpack_lua(pack_lua(decoded)).text == result, "Real patched source roundtrip");
            if (argc > 4) std::ofstream(argv[4], std::ios::binary) << result;
        }
        std::cout << "PASS: codec, patch atomicity, manifests and optional real-client fixtures\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << "FAIL: " << e.what() << '\n'; return 1; }
}
