# ZML Mod API 规范

本文档定义 ZML 的模组开发接口、元数据清单格式与配置服务。

---

## 1. 模组清单 (mod.ini)

每个模组必须拥有唯一的模组目录，并在根目录下包含 `mod.ini` 清单文件：

```ini
[mod]
id=my-mod
name=我的模组
version=1.0.0
description=模组简介
authors=作者
tags=工具,界面
icon=assets/icon.png
config=schema.ini
config_menu=standard
library=MyMod.dll
enabled=true
api=1
depends=keybinds
```

### 字段说明

- **id**（必填）：小写 ASCII 字母、数字及 `.-_`，全局唯一。
- **name**（必填）：模组显示名称。
- **library**（可选）：Native 插件 DLL 文件名，位于当前模组目录下。
- **enabled**（必填）：是否启用模组（`true` / `false`）。
- **api**（必填）：当前接口版本，设为 `1`。
- **version**（可选）：语义化版本号，如 `1.0.0`。
- **description**（可选）：模组功能简介。
- **authors**（可选）：模组作者。
- **tags**（可选）：逗号分隔的标签列表。
- **icon**（可选）：PNG 格式图标（推荐 256×256，≤64 KiB）。
- **config**（可选）：声明式配置模式定义文件（INI 格式）。
- **config_menu**（可选）：配置界面模式：`standard`（标准配置）、`custom`（自定义配置）或 `none`（无配置界面）。
- **config_entry**（可选）：自定义配置页面的入口 Lua 脚本路径。
- **depends**（可选）：依赖的其他模组 ID 列表（逗号分隔），加载器将按拓扑依赖顺序加载。

---

## 2. Native C 插件接口

包含头文件 [`include/zml_plugin.h`](../include/zml_plugin.h)，并导出唯一的入口函数：

```c
extern "C" __declspec(dllexport) const ZmlPlugin* ZML_PluginV1();
```

### 插件结构

- `start(host)`：在进程工作线程附加至 IL2CPP 后执行一次，用于初始化核心逻辑与 Hook。
- `transform_lua(owner, path, callback, userdata)`：注册游戏 Lua 源码变换回调。当游戏加载指定的 Lua 模块时，回调会被触发。
  - 回调接收解密后的明文 Lua 源码。
  - 调用 `sink(userdata, new_content, length)` 交付修补后的完整脚本。

---

## 3. 公共 Lua API

在游戏主线程中，通过原生加载机制获取公共 API 模块：

```lua
local chunk, err = loadstring(LuaManagerInst:LoadLua("ZML/Api"), "@ZML/Api")
assert(chunk, err)
local ZML = chunk()

-- 获取所有已加载的模组列表
local mods = ZML.mods()

-- 读取或修改指定模组的配置项
local values = ZML.get("my-mod")
ZML.set("my-mod", "enabled", "true")

-- 订阅配置项变更
local unsubscribe = ZML.subscribe("my-mod", function(key, value, values, restart_required)
    print("配置更新:", key, value)
end)
```

---

## 4. 声明式配置菜单 (schema.ini)

若模组设置了 `config_menu=standard`，可在 `schema.ini` 中使用简单的 INI 结构声明设置项，由模组菜单自动生成原生风格界面：

```ini
[menu]
title=模组设置

[field.enabled]
type=bool
label=启用功能
description=即时生效
default=true

[field.speed]
type=number
label=速度调节
min=1
max=10
step=0.5
default=5

[field.mode]
type=enum
label=工作模式
options=普通|增强|自动
default=普通

[field.alias]
type=string
label=自定义名称
max_length=32
default=管理员
```

支持字段类型：
- **bool**：开关切换。
- **number**：滑块控件，支持 `min`, `max`, `step`。
- **enum**：下拉菜单，`options` 以 `|` 分隔。
- **string**：单行文本输入，支持 `max_length`, `format=digits|plain_text`。

用户的修改会自动保存至 `%LOCALAPPDATA%\ZML\mods\<id>\config.ini`。

---

## 5. 自定义配置页面 (config_entry)

若模组设置了 `config_menu=custom`，可提供一个 Lua 脚本返回页面构造工厂：

```lua
return {
    api = 1,
    create = function(ctx)
        local values = ctx.get()
        ctx.text(ctx.parent, "自定义设置", 20, 20, 300, 40, 24)
        ctx.button(ctx.parent, "切换开关", 20, 80, 200, function()
            ctx.set("enabled", values.enabled ~= "true")
        end)

        return function()
            -- 页面关闭或销毁时的清理函数
        end
    end
}
```

高级模组亦可声明 `presentation="full"` 获取全屏画布容器，使用 Unity 原生 UI 组件自由绘制复杂的拖拽或交互界面。
