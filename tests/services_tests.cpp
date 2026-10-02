#include "mod_services.hpp"
#include "lua_codec.hpp"
#include "win_util.hpp"
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <io.h>
#include <fcntl.h>
using namespace std::string_view_literals;
namespace {
void check(bool b, const char* label) { if (!b) throw std::runtime_error(label); }
template<class F> void reject(F fn, const char* label) { bool bad = false; try { fn(); } catch (...) { bad = true; } check(bad, label); }
std::string read(const std::filesystem::path& path) { std::ifstream f(path, std::ios::binary); return {std::istreambuf_iterator<char>(f), {}}; }
void write(const std::filesystem::path& path, std::string_view s) { std::ofstream(path, std::ios::binary) << s; }
std::string hex(std::string_view s) { const char* digits = "0123456789abcdef"; std::string out; for (unsigned char c : s) { out += digits[c >> 4]; out += digits[c & 15]; } return out; }
}
int main(int argc, char** argv) {
    try {
        using namespace zml;
        auto root = std::filesystem::path(ZML_SOURCE_DIR);
        auto temp = std::filesystem::temp_directory_path() / ("zml-services-" + std::to_string(GetCurrentProcessId()));
        std::filesystem::create_directories(temp / "demo");
        struct Cleanup { std::filesystem::path p; ~Cleanup() { std::error_code e; std::filesystem::remove_all(p, e); } } cleanup{temp};
        auto config = temp / "demo/schema.ini";
        std::string schema = "[menu]\ntitle=示例配置\n[field.enabled]\ntype=bool\nlabel=功能开关\ndefault=true\n"
            "[field.amount]\ntype=number\ndefault=2\nmin=0\nmax=10\nstep=2\nrestart=true\n"
            "[field.mode]\ntype=enum\ndefault=简单\noptions=简单|复杂\n"
            "[field.caption]\ntype=string\ndefault=演示\nmax_length=64\n";
        write(config, schema);
        Manifest demo; demo.id = "demo"; demo.title = "测试模组"; demo.directory = temp / "demo";
        demo.description = "包含声明式配置和复杂页面入口"; demo.version = "1.2.3"; demo.tags = {"测试", "工具"}; demo.config = config;
        Manifest custom = demo; custom.id = "custom-demo"; custom.title = "Custom Fixture";
        custom.config_menu = "custom"; custom.tags = {"自定义"};
        custom.config_entry = root / "examples/config-entry.lua";
        // Standalone generic fixture; optional external manifest is only for
        // black-box integration with a separately maintained Mod repository.
        Manifest fixture; fixture.id="fixture"; fixture.title="Fixture"; fixture.config=config;
        fixture.directory=temp; fixture.icon=temp/"fixture.png";
        std::string png(33,'\0'); png.replace(0,8,"\x89PNG\r\n\x1a\n",8); png.replace(12,4,"IHDR"); png[19]=2; png[23]=2;
        write(fixture.icon,png);
        if (argc >= 3 && std::string_view(argv[1]) == "--serve") fixture=read_manifest(argv[2]);
        auto fixture_record=prepare_record(fixture,temp/"state/fixture");
        check(!fixture_record.icon_base64.empty(), "Fixture icon encoded");
        auto legacy_state=temp/"legacy-fixture"; std::filesystem::create_directories(legacy_state);
        write(legacy_state/"config.ini","removed_field=legacy\namount=6\nenabled=false\n");
        auto migrated=prepare_record(demo,legacy_state);
        check(!migrated.values.contains("removed_field") && migrated.values.at("amount")=="6" &&
              migrated.values.at("enabled")=="false", "Removed config key ignored without resetting other values");
        ModServices service;
        service.set_api(read(root / "lua/zml.lua"));
        service.publish(fixture_record); service.publish(prepare_record(demo, temp / "state/demo"));
        service.publish(prepare_record(custom, temp / "state/custom-demo"));
        if (argc >= 2 && std::string_view(argv[1]) == "--serve") {
            for(int i=3;i<argc;++i) {
                auto extra=read_manifest(argv[i]);
                service.publish(prepare_record(extra,temp/"state"/extra.id));
            }
            // Test-only length-prefixed stdio protocol. No game process needed.
            _setmode(_fileno(stdout), _O_BINARY);
            std::string request;
            while (std::getline(std::cin, request)) {
                auto result = service.module(request);
                std::cout << result.size() << '\n' << result << std::flush;
            }
            return 0;
        }
        auto registry = service.registry_lua();
        check(registry.find(fixture.id) != registry.npos && registry.find("demo") != registry.npos && registry.find("failed-mod") == registry.npos, "Published loaded-only registry");
        reject([&] { service.publish(fixture_record); }, "Duplicate publish rejected");
        check(lua_quote("\"\\\n\r\0"sv) == "\"\\\"\\\\\\010\\013\\000\"", "Lua string injection escaped");
        auto request = [&](std::string_view key, std::string_view value) { return service.module("ZML/Set/demo/" + std::string(key) + "/" + hex(value)); };
        check(request("enabled", "false").find("ok=true") != std::string::npos, "Boolean applied");
        auto saved = temp / "state/demo/config.ini"; auto baseline = read(saved);
        for (auto& [key, value] : std::vector<std::pair<std::string, std::string>>{{"enabled", "yes"}, {"amount", "3"}, {"amount", "12"}, {"amount", "nan"}, {"amount", "2junk"}, {"mode", "missing"}, {"caption", std::string(65, 'a')}, {"caption", "bad\nvalue"}, {"unknown", "x"}}) {
            check(request(key, value).find("ok=false") != std::string::npos && read(saved) == baseline, "Invalid config preserves on-disk state");
        }
        check(request("amount", "4").find("restart=true") != std::string::npos, "Restart flag reported");
        check(request("caption", "中文 \" ]; error('injection') --").find("ok=true") != std::string::npos, "String data escaped instead of executed");
        auto recreated = prepare_record(demo, temp / "state/demo");
        check(recreated.values["enabled"] == "false" && recreated.values["amount"] == "4", "Config reload persistence");
        auto original = service.module("ZML/Get/demo");
        std::filesystem::create_directory(temp / "state/demo/config.ini.tmp"); // open/write must fail
        check(request("amount", "6").find("ok=false") != std::string::npos && service.module("ZML/Get/demo") == original, "Write failure rolls back in-memory values");
        std::filesystem::remove(temp / "state/demo/config.ini.tmp");
        auto committed = read(saved);
        std::filesystem::remove(saved); std::filesystem::create_directory(saved);
        check(request("amount", "6").find("ok=false") != std::string::npos && service.module("ZML/Get/demo") == original, "Atomic replacement failure preserves in-memory values");
        std::filesystem::remove(saved); write(saved, committed);
        for (auto path : {"ZML/Get/failed-mod", "ZML/Set/demo/amount/xx", "ZML/Set/demo/amount/0", "ZML/Set/../amount/32", "ZML/Entry/no-mod", "ZML/Report/demo/../../other"})
            check(service.module(path).find("ok=false") != std::string::npos, "Invalid service route rejected");
        check(service.module("ZML/Report/demo/page_open") == "return {ok=true}", "Bounded diagnostics supported");
        check(service.module("ZML/Entry/custom-demo").find("create") != std::string::npos, "Custom-only config entry provided");
        check(service.module("ZML/Entry/demo").find("ok=false") != std::string::npos, "Standard menu cannot offer custom entry");
        check(registry.find("config_menu=\"standard\"") != registry.npos && registry.find("config_menu=\"custom\"") != registry.npos, "Exclusive menu modes published");
        auto ambiguous = custom; ambiguous.config_menu.clear();
        reject([&]{prepare_record(ambiguous,temp/"ambiguous");}, "Implicit dual-menu declaration rejected");
        ambiguous.config_menu="standard";
        reject([&]{prepare_record(ambiguous,temp/"ambiguous");}, "Standard plus custom entry rejected");
        ambiguous.config_menu="both";
        reject([&]{prepare_record(ambiguous,temp/"ambiguous");}, "Unknown mode rejected");
        ambiguous=demo; ambiguous.config_menu="custom";
        reject([&]{prepare_record(ambiguous,temp/"ambiguous");}, "Custom without entry rejected");
        ConfigField digits; digits.type="string"; digits.format="digits"; digits.min_length=1; digits.max_length=20;
        check(valid_config_value(digits,"000123") && !valid_config_value(digits,"") && !valid_config_value(digits,"１２３") && !valid_config_value(digits,"1e9"), "Standard numeric-string validation");
        ConfigField plain=digits; plain.format="plain_text"; plain.max_length=96;
        check(valid_config_value(plain,"中文🙂") && !valid_config_value(plain,"<b>x</b>") && !valid_config_value(plain,"\tx") && !valid_config_value(plain,"\xff"), "Plain UTF8 validation rejects markup/controls/invalid encoding");
        auto source = service.module("ZML/Api");
        check(source.find("__ZML_REGISTRY__") == source.npos, "Registry snapshot injected");
        check(unpack_lua(pack_lua({source, Envelope::xxtea_base64})).text == source, "Virtual API current-game envelope roundtrip");
        for (auto bad : {schema + "[field.enabled]\ntype=bool\ndefault=true\n", std::string("[field.x]\ntype=execute\ndefault=x\n"),
             std::string("[field.x]\ntype=number\nmin=2\nmax=1\ndefault=2\n"), std::string("[field.x]\ntype=enum\noptions=a|a\ndefault=a\n"),
             std::string("[field.x]\ntype=bool\ndefault=true\nformat=digits\n"), std::string("[field.x]\ntype=string\ndefault=x\nformat=unknown\n"),
             std::string("[field.x]\ntype=string\ndefault=4\nmin_length=2\nmax_length=1\n"), std::string("[field.x]\ntype=string\ndefault=no\nmin_length=1\nformat=digits\n")}) {
            write(config, bad); reject([&] { read_config(config); }, "Invalid schema rejected");
        }
        write(config, schema);
        write(saved, "amount=9\nenabled=false\nmode=bad\ncaption=中文\n");
        auto recovered = prepare_record(demo, temp / "state/demo");
        check(recovered.values["amount"] == "2" && recovered.values["mode"] == "简单" && recovered.values["caption"] == "中文", "Invalid persisted fields fall back independently");
        auto ini = temp / "demo/mod.ini";
        write(ini, "[mod]\nid=demo\nname=Demo\napi=1\nlibrary=Demo.dll\nenabled=true\ntags=工具,测试,工具\nconfig=schema.ini\n");
        check(read_manifest(ini).tags.size() == 2, "Metadata tag deduplication");
        auto prefix = std::string("[mod]\nid=demo\napi=1\nlibrary=Demo.dll\nenabled=true\n");
        write(temp/"demo/entry.lua","return {api=1,create=function()end}");
        for(auto tail : {"config=schema.ini\nconfig_entry=entry.lua\n", "config_menu=standard\nconfig=schema.ini\nconfig_entry=entry.lua\n", "config_menu=custom\nconfig=schema.ini\n", "config_menu=both\n"}) {
            write(ini,prefix+tail); reject([&]{read_manifest(ini);},"Manifest exclusive-menu enforcement");
        }
        write(ini,prefix+"config_menu=custom\nconfig=schema.ini\nconfig_entry=entry.lua\n");
        check(read_manifest(ini).config_menu=="custom","Custom menu schema is storage, not another menu");
        for (auto tail : {"icon=../../outside.png\n", "config_entry=C:\\outside.lua\n", "[unknown]\n", "config_entry=missing.lua\n"}) {
            write(ini, "[mod]\nid=demo\napi=1\nlibrary=Demo.dll\nenabled=true\n" + std::string(tail));
            reject([&] { read_manifest(ini); }, "Unsafe metadata assets rejected");
        }
        std::cout << "PASS: loaded registry, metadata, schema, config persistence/rollback, virtual services\n"; return 0;
    } catch (const std::exception& e) { std::cerr << "FAIL: " << e.what() << '\n'; return 1; }
}
