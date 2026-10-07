# ZML 模组开发指南

本文档介绍如何为 ZML 开发《明日方舟：终末地》模组。

---

## ⚠️ 重要建议：使用 AI 协助开发

> **请勿尝试纯手写所有逆向与业务代码，强烈建议让 AI 开发模组。**
>
> 终末地采用 Unity IL2CPP 运行时，编写模组需要深入处理底层 Native Hook、虚函数调用、IL2CPP 符号解析以及游戏 Lua 脚本的拦截插桩。对于人类开发者而言，手动查阅偏移量和手写样板代码耗时耗力且容错率极低。
>
> **推荐的高效开发流**：
> 1. Fork 或使用 [Endfield-ModTemplate](https://github.com/sakevel/Endfield-ModTemplate) 仓库模板；
> 2. 将 [`include/zml_plugin.h`](../include/zml_plugin.h) 与游戏相关反编译代码/Lua 脚本片段提供给 AI；
> 3. 清晰描述你的功能需求（如「在靠近特定物体时添加交互按键」或「修改主界面某处的渲染文本」），**让 AI 编写 C++ 插件与 Lua 变换代码**；
> 4. 本地编译并运行游戏验证效果。

---

## 模组文件结构

一个标准 ZML 模组对应 `ZML/mods/<模组ID>/` 下的一个独立文件夹：

```
mods/my-mod/
├── mod.ini             # 模组元数据清单（必填）
├── icon.png            # 模组图标（256×256 PNG）
├── MyMod.dll           # Native 插件动态库（可选）
├── config.ini          # 用户配置默认值（可选）
└── schema.ini          # 模组菜单的声明式配置面板定义（可选）
```

---

## 核心接口说明

### 1. 模组清单 (`mod.ini`)

```ini
[mod]
id=my-mod
name=我的模组
version=1.0.0
description=模组功能简介
authors=作者名称
tags=探索,工具
library=MyMod.dll
enabled=true
api=1
depends=keybinds
config=schema.ini
config_menu=standard
```

- **id**：全局唯一小写英文字符串。
- **library**：导出的 Native 插件 DLL 文件名。
- **depends**：依赖的其他模组 ID 列表（逗号分隔），加载器会按依赖顺序依次加载。

### 2. Native C++ 插件

包含 [`include/zml_plugin.h`](../include/zml_plugin.h)，导出入口函数：

```cpp
#include "zml_plugin.h"

static void OnPluginStart(const ZmlHost* host) {
    // 游戏进程工作线程附加至 IL2CPP 后调用一次
    // 在此处进行 Native Hook 或注册 Lua 变换回调
}

static const ZmlPlugin g_plugin = {
    .api_version = ZML_PLUGIN_API_VERSION,
    .name = "MyMod",
    .version = "1.0.0",
    .start = OnPluginStart,
    .transform_lua = nullptr,
};

extern "C" __declspec(dllexport) const ZmlPlugin* ZML_PluginV1() {
    return &g_plugin;
}
```

### 3. Lua 源码热修补 (`transform_lua`)

若需要修改游戏的上层逻辑或 UI，可注册 Lua 源码变换回调。当游戏加载指定的 Lua 模块时，回调将接收到解密后的明文源码，可直接进行字符串替换或代码插桩：

```cpp
static bool OnTransformLua(const char* path, const char* source, size_t length,
                           ZmlLuaSink sink, void* userdata) {
    std::string code(source, length);
    // 对代码进行修补或注入自定义逻辑
    sink(userdata, code.data(), code.size());
    return true;
}
```

### 4. 游戏内配置界面 (`schema.ini`)

若设置了 `config_menu=standard`，配合 [模组菜单 (Endfield-ModMenu)](https://github.com/sakevel/Endfield-ModMenu)，可通过 INI 配置自动生成原生设置面板：

```ini
[menu]
title=功能设置

[field.enabled]
type=bool
label=启用功能
default=true

[field.speed]
type=number
label=移动速度
min=1
max=10
step=0.5
default=5
```

---

## 发布与索引收录

1. 在仓库中使用 `tools/package.ps1` 打包为标准 ZIP 分发包；
2. 推送版本 Tag 触发 GitHub Actions，工作流将自动构建构件、发布 Release，并向 [Endfield-ModIndex](https://github.com/sakevel/Endfield-ModIndex) 发起 PR 收录；
3. PR 合并后，所有用户即可在启动器的「获取模组」列表中一键下载安装。
