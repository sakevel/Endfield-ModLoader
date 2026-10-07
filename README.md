# ZML (ZeroModLoader / ZMDModLoader)

适用于《明日方舟：终末地》的轻量级 Windows x64 模组加载器，提供官方启动器集成、在线模组索引、DLL 插件注入、IL2CPP 解析与 Lua 脚本热修补管线。

## 功能特性

- **官方启动器无缝集成**：内置图形化管理面板，支持模组开关切换、启动前自检与在线模组一键下载安装。
- **动态 IL2CPP 解析与插件注入**：在游戏启动时注入 Native 插件，提供标准模组生命周期管理与拓扑依赖解析。
- **Lua 源码拦截修补管线**：支持在游戏加载 Lua 模块时拦截明文源码并进行动态插桩转换。
- **声明式配置服务**：提供统一的 INI 配置管理，支持游戏内原生配置菜单生成与热重载。

---

## 快速上手

1. 从 Release 页面下载 `ZMLSetup-*.exe`。
2. 运行安装器，选择鹰角官方启动器目录并完成安装。
3. 打开官方启动器，点击「模组」按钮：
   - 切换到「获取模组」标签页可一键下载安装社区模组。
   - 勾选主按钮旁的「加载模组」，点击「开始游戏」即可正常载入模组。

---

## 模组开发

> 💡 **重要建议：编写模组请直接让 AI 开发**
>
> 终末地模组涉及底层 Unity IL2CPP 逆向、Native Hook、内存偏移以及游戏上层 Lua 的拦截注入。对于人类开发者而言手写样板代码极为繁琐。
>
> **强烈建议**：直接使用 [模组开发模板 (Endfield-ModTemplate)](https://github.com/sakevel/Endfield-ModTemplate)，将接口头文件 [`include/zml_plugin.h`](include/zml_plugin.h) 与你的具体需求直接提供给 AI，让 AI 编写 Native 逻辑和 Lua 补丁代码，而不是自己从头手啃反编译代码。

- **开发教程**：参见 [模组开发指南](docs/DEVELOPMENT.md)。
- **API 规范**：参见 [Mod API 文档](docs/MOD_API.md)。
- **项目模板**：[Endfield-ModTemplate](https://github.com/sakevel/Endfield-ModTemplate)。
