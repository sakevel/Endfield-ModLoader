# Endfield Mod Loader (ZML)

适用于《明日方舟：终末地》的轻量级 Windows x64 模组加载器，提供游戏启动器集成、DLL 插件加载、运行时 IL2CPP 解析、配置服务与 Lua 脚本热修补管线。

## 功能特性

- **官方启动器无缝集成**：提供图形化安装器，在官方启动器主界面无缝嵌入模组管理面板与一键加载开关。
- **动态 IL2CPP 导出解析**：运行时解析 Unity IL2CPP 符号，支持安全的 Native 插件与 Lua 源码拦截转换。
- **标准化模组服务**：提供统一的模组生命周期管理、依赖拓扑加载、声明式 INI 配置服务与事件订阅机制。
- **独立模组生态**：加载器仅提供核心运行时与通用 SDK，模组作为独立包进行构建、分发与安装。

---

## 快速上手

### 图形化安装器

1. 从 Release 页面下载 `ZMLSetup-*.exe`。
2. 运行安装器，选择鹰角启动器所在目录（通常可自动识别），点击「安装 / 修复」。
3. 打开鹰角启动器，主按钮旁会显示「模组」按钮及「加载模组」勾选项。
4. 勾选「加载模组」后点击「开始游戏」即可正常加载已安装的模组。

可在启动器的模组侧边栏中直接开启/关闭模组，或点击「模组文件夹」将新模组放入模组目录。

---

## 模组安装与管理

模组通过独立发行包进行分发，每个模组对应 `ZML\mods\<模组ID>` 目录下的一个独立文件夹。

如需手动安装或打包模组，可使用项目附带的 PowerShell 工具：

```powershell
# 将模组包安装到本地加载器
.\tools\install-mod.ps1 -ModPackage "..\Endfield-ModMenu\build\package\Release\mod-menu"

# 查看当前已安装的模组列表
.\build\package\Release\ZML.exe --list
```

模组的用户自定义配置保存在 `%LOCALAPPDATA%\ZML\mods\<模组ID>\config.ini`，更新或重新安装模组不会覆盖已有的个人配置。

---

## 源码构建

### 环境要求

- Visual Studio 2022 (C++ x64 工作负载)
- Windows 10/11 SDK
- CMake 3.25+
- Git

### 构建步骤

```powershell
# 编译核心加载器与运行时
.\tools\build.ps1

# 打包加载器发行物（输出至 dist/）
.\tools\package.ps1

# 编译图形化单文件安装器
.\tools\build-installer.ps1
```

构建产物位于 `build/package/Release/`。可通过命令行直接测试运行：

```powershell
# 预检游戏路径与环境
.\build\package\Release\ZML.exe --game "D:\Game\Hypergryph Launcher\games\Endfield Game\Endfield.exe" --dry-run
```

---

## 模组开发与 SDK

- **C 插件接口**：参见 [`include/zml_plugin.h`](include/zml_plugin.h) 与 [`include/zml_lua_service.h`](include/zml_lua_service.h)。
- **模组配置与 Lua API 规范**：参见 [Mod API 文档](docs/MOD_API.md)。
- **启动器集成机制**：参见 [安装器说明](docs/INSTALLER.md)。
- **测试与验证指南**：参见 [验证说明](docs/VALIDATION.md)。
