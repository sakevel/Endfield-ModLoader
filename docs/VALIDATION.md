# 测试与验证指南

本文档汇总 Endfield Mod Loader 的自动化测试套件与功能验证方法。

---

## 自动化测试体系

本项目包含单元测试、注入测试、服务集成测试以及启动器 Web UI 的端到端测试。

### 1. CTest 核心测试套件

构建完成后，在 `build` 目录下运行 CTest 即可执行所有本地自动化测试：

```powershell
ctest --test-dir build -C Release --output-on-failure
```

测试套件涵盖以下模块：

- **CoreTests (`tests/core_tests.cpp`)**：
  - XXTEA 加密 / 解密与 Base64 编解码 roundtrip 测试。
  - Lua 脚本补丁注入与锚点唯一性校验。
  - 模组 Manifest（`mod.ini`）解析、格式校验与非法路径隔离。
  
- **DependencyTests (`tests/dependency_tests.cpp`)**：
  - 模组依赖图分析与拓扑排序算法。
  - 循环依赖检测、缺失依赖拒绝与禁用依赖处理。

- **ServicesTests (`tests/services_tests.cpp`)**：
  - 模组配置注册表（Registry）与声明式 INI 配置存取校验。
  - Lua API 映射及数据类型转换测试。

- **InjectionFixture (`tests/injection_fixture.cpp` / `tests/injection_e2e.ps1`)**：
  - 独立 Win32 进程上的 DLL 远程注入与卸载回滚验证。
  - 插件初始化异常时的安全退出与子进程资源清理机制。

---

### 2. 启动器与安装器测试

#### 安装器单元测试 (`tests/installer_tests.cs`)

覆盖安装引擎、备份机制与卸载恢复：

```powershell
.\tools\build-installer.ps1
```

测试点包含：
- 启动器目录定位与文件哈希校验。
- 官方启动器文件的安全备份与冲突处理。
- 卸载流程中的状态还原与自定义模组数据保留。

#### 启动器 Web 界面测试 (`tests/installer_web_tests.py`)

使用 Playwright 测试嵌入官方启动器中的模组管理界面：

```powershell
python tests/installer_web_tests.py --start build/installer/InstallerTests.exe build/installer/payload.zip build/tests/Release/InjectionFixture.exe
```

测试点包含：
- 启动器胶囊按钮的嵌入与展开。
- 模组管理侧边栏的弹出、搜索、筛选与开关切换。
- DOM 变更与主题自适应。

---

## 手动与实机验证清单

在真机环境下部署时，可按照以下清单验证核心交互：

1. **安装器流程**：
   - 安装器能正确检测启动器路径。
   - 安装完成后启动器主界面出现「模组」按钮及「加载模组」复选框。

2. **启动与注入**：
   - 勾选「加载模组」并点击「开始游戏」，客户端能顺利加载已启用的模组。
   - 未勾选时按原生模式启动，不加载任何模组 DLL。

3. **模组热修补**：
   - 模组声明的 Lua 变换能在游戏加载阶段正常生效。
   - 修改模组配置后能在游戏内正常热重载。
