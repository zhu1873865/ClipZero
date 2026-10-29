# ClipZero 开发文档

本目录是独立的 C# / .NET 10 Windows Forms 项目，处理当前 Windows 文件剪贴板状态及用户主动启用的实验性 Explorer 撤销配置。它不引用 `LabEquipmentCenter`，也不需要该网页项目的 Node.js、页面、数据或服务。两个项目各自维护源码、依赖、测试、文档与 Git。

默认只做本机开发、验证和本地 Git 提交。`dotnet publish` 与 `scripts/publish.ps1` 是本地生成应用文件，不会上传；远端 Git 推送、上传源码、GitHub Release 或其他线上发布必须有用户明确要求。协作约定见 [AGENTS.md](../AGENTS.md)。

## 环境与首次构建

需要 Windows 桌面环境与 .NET 10 SDK。只装 Desktop Runtime 能运行框架依赖版，但不能开发构建。项目无第三方 NuGet 库；首次还原和发布所需运行时包可能需要联网，应用运行时不联网。

`global.json` 指定稳定版 `10.0.100`，允许滚动到已安装的较新 10.0 feature band。`Directory.Build.props` 启用确定性编译、可空检查相关项目配置和警告视为错误；不要通过关闭警告来掩盖构建问题。

在项目根目录打开 PowerShell：

```powershell
dotnet --info
dotnet --list-sdks
dotnet restore ClipZero.slnx
dotnet build ClipZero.slnx -c Release --no-restore
dotnet run --project src/ClipZero/ClipZero.csproj -c Release --no-build -- --show
```

截至 2026-10-29，本工作目录已有 `.tools/dotnet/dotnet.exe`，SDK 版本为 `10.0.402`。若系统 `dotnet` 只有 host、没有 SDK，可直接使用现有本地 SDK，不用再次安装：

```powershell
& .\.tools\dotnet\dotnet.exe --info
& .\.tools\dotnet\dotnet.exe restore ClipZero.slnx
& .\.tools\dotnet\dotnet.exe build ClipZero.slnx -c Release --no-restore
& .\.tools\dotnet\dotnet.exe run --project src/ClipZero/ClipZero.csproj -c Release --no-build -- --show
```

`.tools/` 是本机工具目录，受 Git 忽略，不随源码或发布包分发。别人取得仓库后应安装 SDK，或自行准备兼容的本地 SDK；不要假设每个环境都有此目录。

构建后可先从托盘退出旧实例，再双击根目录 `启动ClipZero.cmd`。该入口基于自身所在目录查找 `src/ClipZero/bin/Release/net10.0-windows/ClipZero.exe`，不会重新构建，并在发现 `.tools/dotnet` 时配置本地运行时。普通启动默认只有托盘，`--show` 会显示主窗口；关闭主窗口仍驻留，使用托盘菜单退出进程。

## 目录与职责

| 路径 | 职责 |
| --- | --- |
| `ClipZero.slnx` | Core、WinForms 应用、控制台测试三项目入口 |
| `src/ClipZero.Core/ClipboardService.cs` | 纯业务规则；基于快照版本安全清空，依赖 `IClipboardGateway` |
| `src/ClipZero.Core/ExplorerUndoService.cs` | 实验性设置备份、写入、恢复和冲突保护，依赖存储接口 |
| `src/ClipZero/Program.cs` | STA 入口、当前会话单实例及再次启动唤起 |
| `src/ClipZero/TrayApplicationContext.cs` | 生命周期、托盘、窗口与服务协调 |
| `src/ClipZero/ClipboardWorker.cs` | 专用 STA 工作线程及有限读取／清空重试 |
| `src/ClipZero/MessageWindow.cs` | 剪贴板通知、全局快捷键和实例激活消息 |
| `src/ClipZero/Interop/` | Windows API 与注册表／备份文件存储实现 |
| `src/ClipZero/UI/` | 主窗口、状态文案和程序图标 |
| `tests/ClipZero.Tests/` | 无第三方框架的控制台测试、可选原生／UI／EXE 冒烟 |
| `tests/ExplorerUndo.Tests.ps1` | 辅助脚本的只读／WhatIf 安全检查 |
| `scripts/publish.ps1` | 本地 x64／ARM64、框架依赖／独立运行输出 |
| `.github/workflows/build.yml` | 远端仓库中运行时的构建、默认测试与 artifact 流程；不自动发 Release |
| `docs/` | 架构、验收、实验性设置及发布文档 |
| `artifacts/`、`**/bin/`、`**/obj/` | 本地检查、构建与发布产物，不属于源码提交 |

## 修改时遵守的边界

剪贴板通知来自 `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`；不添加高频常驻轮询或键盘钩子。原生读取与清空放在 STA 工作线程，不在 UI 线程执行可能延迟提供数据的剪贴板调用。

界面状态只是快照。清空必须重新取得剪贴板锁，在锁内核对版本与 `CF_HDROP`，并在可能延迟渲染后复核版本；过期、非文件或读取失败时拒绝清空。重试继续使用点击时的同一版本，不能追着清除新复制的内容。任何分支都需释放锁；不要直接从按钮调用无条件 `EmptyClipboard`。

只保留当前文件名摘要，不记录文本、密码、图片或历史，不读取原文件内容，也不删除、移动、改名原文件。包含标准文件列表的混合项目会整项清空；不能把它描述为只删除其中的文件格式。Win+V 历史不属于工具清理范围。

Explorer 文件撤销与剪贴板是两套机制。实验性配置只尝试把当前用户 `MaxUndoItems` 设为 `DWORD 0`；写入成功不等于隐藏菜单或禁用撤销。启动与刷新只读，写入／恢复必须来自用户明确操作；保持原值备份、账户和目标校验、外部修改冲突保护，不重启 Explorer，不自动注销。现有现场记录只确认撤销项曾灰色不可点，未证明新文件操作无法撤销。完整边界见 [explorer-undo.md](explorer-undo.md)。

## 测试与调试

先构建，再运行默认测试；只用项目内 SDK 时，直接替换命令入口：

```powershell
& .\.tools\dotnet\dotnet.exe build ClipZero.slnx -c Release --no-restore
& .\.tools\dotnet\dotnet.exe run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build
```

默认套件用替身验证剪贴板和撤销设置，不修改用户剪贴板或真实注册表，失败返回非零退出码。正常安装 SDK 的环境将命令入口换回 `dotnet`。测试模式的详细说明、历史验证结果与手工步骤见 [testing.md](testing.md)。

| 可选参数 | 作用与限制 |
| --- | --- |
| `--native` | 在独立 window station 操作测试剪贴板；隔离失败输出 SKIP，不能计为通过，不回退到用户剪贴板 |
| `--ui` | 私有内存解析、消息窗口、工作线程、UI 与短暂测试快捷键注册；模拟 DPI 不代替真实缩放 |
| `--development` | 私有桌面上启动当前开发 EXE，验证启动、单实例与退出；不触发清空或设置修改 |
| `--portable` | 对已生成的 x64 独立运行 EXE 做启动冒烟；不证明干净机器或 ARM64 已验证 |

项目内 SDK 环境运行 `--development` 时，还应给子 EXE 指定运行时：

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.tools\dotnet).Path
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
& .\.tools\dotnet\dotnet.exe .\tests\ClipZero.Tests\bin\Release\net10.0-windows\ClipZero.Tests.dll --development
```

实际 Explorer 的复制／剪切后 Ctrl+V、托盘鼠标操作、物理快捷键、跨显示器 DPI、Win+V 与实验性设置重新登录后的效果，按验收文档在临时文件中检查。自动测试不能替代这些交互验证。不得为通过测试而修改日常文件、清空用户剪贴板或启用实验性配置。

## 本地输出与维护流程

日常修改只需 `build` 和相关测试，不必每轮打包。明确需要本地便携输出时，可运行 `scripts/publish.ps1`；脚本优先使用本项目 `.tools/dotnet/dotnet.exe`，否则查找系统 SDK。框架依赖版需要同架构 .NET Desktop Runtime 10 并保留全部文件；独立运行版体积较大且内含运行时，运行时更新需要重新生成。WinForms 不启用 trimming。命令、输出位置与正式发布验收见 [releasing.md](releasing.md)。

每次修改说明行为变化、相关测试与未验证项目，更新对应文档。只暂存源码和文档；`.tools/`、构建输出、测试截图、真实配置备份与剪贴板内容不进入 Git。核对当前仓库根目录后本地提交，避免把旁边的设备计量项目加入同一仓库。未经用户明确要求，不执行 `git push`，不创建线上站点或 GitHub Release。
