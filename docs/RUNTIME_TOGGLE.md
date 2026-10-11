# 运行时模组启停规范

ZML 0.5.0 起支持模组在游戏运行期间进行动态启停，玩家在启动器中切换模组开关后即可在游戏内自动应用。

## 机制说明

- **常驻代码控制**：声明支持运行时切换的模组在游戏启动时完成加载与类型绑定，并在启用状态发生变化时通过生命周期接口撤销或重新激活功能。
- **状态与依赖安全**：
  - 启停顺序遵循依赖拓扑：先停用依赖方，后停用被依赖库；启用时反之。
  - 模组可在关键业务交互（如编辑界面打开中）时推迟启停，待安全时机再行切换。
  - 本地存档与配置文件在停用期间保持完好。
- **新模组安装**：首次安装新模组或替换 DLL 文件仍需重启游戏生效。

---

## 模组接入契约

### 1. Manifest 声明
在模组 `mod.ini` 中声明：
```ini
runtime_toggle=true
```

### 2. Native 接口 (`include/zml_activation.h`)
Native 插件通过 `ZMLRuntime.dll!ZML_GetActivationV1` 获取状态查询服务：

```c
typedef struct ZmlActivationV1 {
    uint32_t size, abi;
    int (*is_active)(void* owner);        // 查询自身是否处于激活状态（无锁）
    int (*is_mod_active)(const char* id); // 查询指定模组是否处于激活状态
} ZmlActivationV1;
```

在拦截钩子与事件回调中，模组应先检查 `is_active`，非激活状态下直接放行原版函数逻辑。

### 3. Lua 生命周期接口
通过 `ZML/Api` 注册启停回调：

```lua
-- 检查模组是否已启用
local active = ZML.is_enabled('my-mod')

-- 注册生命周期处理器
ZML.register_lifecycle('my-mod', {
    -- 安全点检查（返回 true 允许切换，返回 false + 原因推迟切换）
    prepare = function(enabled)
        return not is_editing, '请先关闭编辑界面'
    end,
    -- 状态变更回调
    changed = function(enabled)
        if enabled then
            enable_features()
        else
            restore_clean_state()
        end
    end,
})

-- 监听任意模组运行时状态变更
local unsubscribe = ZML.subscribe_runtime(function(id, enabled)
    refresh_ui()
end)
```
