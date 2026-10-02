# 加载器架构与运行时原理

本文档记录 Endfield Mod Loader 的内部架构设计与底层游戏运行时机制。

---

## 1. 核心模块结构

- **`src/launcher.cpp`**：
  命令行预检、x64 环境校验、目标游戏进程启动与 Windows 远线程 DLL 注入。
  动态定位目标进程中的 `LoadLibraryW` 地址，完成运行时装载。

- **`src/runtime.cpp`**：
  注入生命周期管理。等待 Unity IL2CPP 运行时与主 Domain 就绪后，执行元数据读取与通用 Lua 源码拦截管线初始化。

- **`src/il2cpp.cpp` / `src/il2cpp.hpp`**：
  IL2CPP 运行时导出函数解析与动态方法签名比对。避免固定 RVA 偏移，确保在客户端小版本更新时具备兼容性与稳定性。

- **`src/lua_codec.cpp` / `src/lua_codec.hpp`**：
  游戏 Lua 脚本的解密与重封装。游戏资源使用 Base64 编码与 XXTEA 对称加密。加载器在内存中拦截解密后的明文脚本进行补丁注入，再按原格式封装交付。

- **`src/mod_services.cpp` / `lua/zml.lua`**：
  模组元数据注册表、声明式 INI 配置服务、公共 Lua API 及自定义配置入口工厂。

---

## 2. 运行时机制与游戏契约

### IL2CPP 与线程模型

- `GameAssembly.dll` 加载初期，GC 与 Domain 尚未完全初始化。运行时需等待主 Domain 就绪后，再执行线程 Attach 与符号解析。
- Unity UI 操作与 Lua 模块加载受限于主线程，因此界面补丁拦截在游戏主线程回调中执行。

### Lua 资源加载管线

1. 游戏内部通过 `Beyond.Lua.LuaManager.LoadLua(System.String)` 请求 Lua 模块。
2. 返回的内容为 Base64 + XXTEA 封装的加密数据。
3. 加载器 Hook 该方法：
   - 识别并解密数据为 UTF-8 明文 Lua 源码；
   - 依次触发已注册模组的 `transform_lua` 变换回调；
   - 校验修补结果，并使用原格式重新封装返回给游戏。

---

## 3. ESC 主菜单 (WatchCtrl) 适配

游戏主菜单控制器 `UI/Panels/Watch/WatchCtrl` 负责 ESC 导航面板的按钮生成与排布：

- `RIGHT_BTN_ORDER` 表收集右侧功能按钮列表。
- `InitWatchNodes` 的标准执行序列为：
  `BuildData()` → `_SnapshotRightList()` → `_RelayoutRightList()` → `_RefreshBtnLockState()`。
- 模组菜单等扩展入口在 `BuildData()` 与 `_SnapshotRightList()` 之间挂载，确保新增按钮纳入原生导航、布局与高度计算。
