# ClipZero

轻量的 Windows 文件粘贴保护工具。复制或剪切文件后，托盘图标会变黄；单击图标即可取消当前文件复制状态，减少在桌面误粘贴产生的副本。

使用 C#、.NET 10 和 Windows Forms，采用 Windows 原生剪贴板通知。无需管理员权限；程序运行时不联网、不收集信息、不保存剪贴板历史。

这是独立的 ClipZero 项目，当前目录仅维护 Windows 文件剪贴板工具。旁边的 `LabEquipmentCenter` 是独立的设备计量网页项目；两者没有构建、运行或数据依赖，迁移本目录时无需带上另一个项目。开发入口见 [开发文档](docs/development.md)，协作范围见 [AGENTS.md](AGENTS.md)。默认只在本机开发、测试和提交 Git；推送远端、上传源码或发布软件需用户另行明确要求。

在新的独立聊天中接续本项目时，先读取 [项目交接说明](docs/HANDOFF.md)，其中保留当前实现、用户要求和未验证边界。

当前版本为 **1.0.0-rc.1 候选版**。源码和下载包见 [GitHub 仓库](https://github.com/zhu1873865/ClipZero) 与 [Release](https://github.com/zhu1873865/ClipZero/releases/tag/v1.0.0-rc.1)。实际验证范围见 [本轮验证记录](docs/verification-2026-10-29.md)。

软件提供可选的「文件撤销设置（实验性）」。它只能确认配置是否保存，**不能保证隐藏或禁用 Explorer 撤销菜单**；彻底隐藏尚未实现。

在当前开发目录中，先从托盘退出旧版 ClipZero，再双击 **`启动ClipZero.cmd`**，即可打开最新构建的主窗口。该入口运行 `src/ClipZero/bin/Release/net10.0-windows/ClipZero.exe`，会使用目录中的本地 .NET 运行时；首次从源码取得项目时，需先按下方说明构建。

## 使用

1. 下载适合处理器架构的发布包并解压，运行 `ClipZero.exe`。首次启动默认只显示托盘图标，可在任务栏的隐藏图标区域找到它。
2. 在资源管理器中复制或剪切文件、文件夹，托盘会显示待粘贴状态。
3. 单击托盘图标、点击窗口中的「取消文件复制」，或按 **Ctrl+Alt+Backspace** 清空当前文件剪贴板。
4. 右键托盘图标可查看状态、打开窗口、刷新、清空、操作实验性文件撤销设置和退出。关闭窗口会回到托盘；彻底退出请使用托盘菜单。

窗口中的「刷新」和托盘菜单的「刷新状态」可主动重新读取剪贴板与撤销配置；持续占用解除后也可再次尝试。没有待粘贴文件时单击灰色托盘保持安静。窗口支持滚动，缩小高度后仍可到达按钮；撤销设置的操作结果保留在窗口中，便于查看。

运行 `ClipZero.exe --show` 可在启动时打开窗口。软件不设置开机启动，不拦截 Ctrl+C、Ctrl+V，也不会自动定时清空。

| 状态 | 含义 |
| --- | --- |
| 当前无待粘贴文件 | 剪贴板中没有标准文件列表，托盘为灰色 |
| 已复制文件 / 已剪切文件 | 检测到文件列表，托盘为黄色；窗口显示数量及名称 |
| 状态暂时无法读取 | 剪贴板被占用或数据无法安全读取，稍后重试 |

快捷键若被其他程序占用，会提示注册失败；托盘和窗口按钮仍可使用。重复启动会打开已有实例的窗口，不会创建第二个托盘实例。检测到文件但复制/剪切格式缺失或不明确时，显示「待粘贴文件（操作类型未知）」，仍可安全取消；读取失败时显示棕色状态图标。

## 安全边界与已知限制

- 清空只调用 Windows 剪贴板接口，**不会删除、移动或修改原文件**。取消剪切后，原位置的文件仍然存在。
- 清空前会重新检查文件格式与剪贴板版本；内容已经变化时，放弃本次操作并更新状态，避免根据过期界面清除新内容。
- 仅有文字、密码文本、图片等非文件内容时不清空。如果剪贴板同时含标准文件列表和文字等格式，一次清空会清除该剪贴板项目的全部格式。
- 不清理 Windows 的 **Win+V 剪贴板历史**。从历史或其他剪贴板管理器中重新选择文件项目，可能再次产生待粘贴状态。
- 识别标准 `CF_HDROP` 文件列表；通过 `Preferred DropEffect` 判断复制/剪切。预览最多 8 个文件名，每个名称最多 512 字符；数量仍显示全部项目。第一版不支持邮件附件、部分云盘等第三方虚拟文件格式。
- 为安全读取，文件列表内存块超过 16 MiB、单项路径超过 32,767 字符或格式畸形时，会显示无法读取并拒绝清空。
- 清空剪贴板不会清除资源管理器的「撤销重命名 / 撤销删除」记录。软件内的 [实验性文件撤销设置](docs/explorer-undo.md) 可尝试调整或恢复相关配置，但不保证菜单消失或停止撤销；只有主动点击操作才会修改设置，不会撤销已发生的文件操作。
- 退出后不提供保护；Windows 的正常复制和粘贴继续工作。托盘图标更新依赖当前用户会话，不支持 Windows 服务模式。

## 可选：文件撤销设置（实验性）

不需要输入命令。直接在 ClipZero 中操作：

1. 在主窗口的 **「文件撤销设置（实验性）」** 中点击 **「尝试关闭撤销」**；托盘右键菜单也可进入。
2. 「关闭设置已保存，效果未确认」只表示配置写入成功。若刚修改设置，可在保存工作后注销、重新登录一次，再用临时文件操作检查效果；**已经重新登录过就不必反复注销**。
3. 灰色撤销项当前不能执行，但不代表以后一直禁用，也不等于隐藏。如果重新登录后，新产生的文件操作仍能撤销，说明此方案在该系统上无效；点击 **「恢复原设置」** 即可还原配置，按操作结果决定是否需要重新登录。

此方案使用 Windows 未公开的设置，尝试影响桌面和文件资源管理器的**全部文件撤销与重做**，包括右键菜单和 Ctrl+Z / Ctrl+Y；并非只处理两个菜单项。记事本等软件里的文字撤销不受影响。软件不会自动注销或重启电脑。更多说明见 [实验性文件撤销设置](docs/explorer-undo.md)。

配置先备份至 `%LOCALAPPDATA%\ClipZero`；恢复会还原关闭前的设置。读取和保存期间暂时禁用配置按钮，防止与自身读取争用备份锁。写入失败时保留备份：若当前值仍是原值，可安全重试或仅清理备份；仅清理备份不提示注销。若被其他程序改成不同值，则停止以保护后续修改。此功能由 C# 直接实现，运行应用无需调用 PowerShell。旧的辅助脚本仍保留为高级手工工具，命令及其较严格的恢复限制见 [独立说明](docs/explorer-undo.md)。

## 系统要求

- Windows 桌面系统，支持发布 `win-x64` 和 `win-arm64`；不支持 Linux、macOS 或 Windows 7/8.1。
- Windows 11，以及符合 .NET 10 官方支持范围的 Windows 10 LTSC / Enterprise。普通 Windows 10 版本的运行表现需要自行验证，不能据此承诺微软的系统支持。最新范围见 [.NET 10 支持的系统](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)。
- 框架依赖版需要相同架构的 **.NET Desktop Runtime 10**，仅安装普通 .NET Runtime 不够；独立运行版无需另装运行时。运行时下载：[.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)。

## 构建与运行

在 Windows 安装 .NET 10 SDK，并在仓库根目录使用 PowerShell：

```powershell
dotnet --info
dotnet restore ClipZero.slnx
dotnet build ClipZero.slnx -c Release --no-restore
dotnet run --project src/ClipZero/ClipZero.csproj -c Release --no-build -- --show
```

`global.json` 要求 .NET 10 稳定版 SDK，允许滚动到已安装的较新 10.0 feature band。不需要额外第三方 NuGet 库。首次还原、下载运行时包需要网络；这属于构建过程，应用本身不使用网络。如果此工作目录使用本地 SDK，可将上述 `dotnet` 替换为 `& .\.tools\dotnet\dotnet.exe`；`.tools` 不属于源码或发布内容。

依赖注入后的剪贴板状态逻辑使用无第三方测试框架的控制台测试：

```powershell
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release
```

默认测试不写入当前用户剪贴板；测试失败会以非零退出码退出。真实资源管理器交互、托盘、快捷键和高 DPI 的手工步骤见 [验收清单](docs/testing.md)。

可选 Windows 原生集成测试：`dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release -- --native`。测试在独立的非交互 window station 中操作测试剪贴板，不接触当前用户剪贴板；隔离创建失败时输出 `SKIP`，必须与通过区分。

可选安全原生/UI 测试：`dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release -- --ui`。测试私有内存中的文件列表解析、应用消息窗口、STA 工作线程和界面，不读取或改写用户剪贴板；会短暂注册测试快捷键，需要 Windows 桌面能力。测试截图输出到 `artifacts/screenshots/`，详见验收文档。

无需打包即可测试当前开发 EXE：`dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build -- --development`，检查私有桌面上的启动、单实例和正常退出。实际应用会只读查看文件剪贴板元数据与撤销配置，测试不触发清空或设置修改。

发布 x64 独立运行版后，可执行 `dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release -- --portable`，验证真实压缩 EXE 的启动、第二次启动和正常退出。该测试使用私有非输入桌面；实际应用会正常读取当前文件剪贴板元数据，测试不触发清空或快捷键。

Explorer 撤销辅助脚本的可复现安全测试：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\ExplorerUndo.Tests.ps1
```

该测试只调用只读 `Status` 和 `-WhatIf`，使用临时 JSON 验证备份与恢复边界，不改写真实 Explorer 配置。

## 发布便携版

只生成应用文件可使用 `scripts/publish.ps1`。生成含文档、许可、构建信息及 SHA-256 的 ZIP，使用：

```powershell
# x64 独立运行、单个应用 EXE
.\scripts\package.ps1

# ARM64 独立运行版
.\scripts\package.ps1 -Runtime win-arm64 -Mode self-contained

# 已安装 .NET Desktop Runtime 10 的电脑使用较小的框架依赖包
.\scripts\package.ps1 -Runtime win-x64 -Mode framework-dependent
```

输出目录为 `artifacts/publish/<架构>/<模式>/`。框架依赖包需要保留整个文件夹，不能只复制 EXE。独立运行包较大，可直接携带应用 EXE；启用压缩以减小分发体积，启动时需要解压，首次启动可能稍慢。.NET 在运行时将内置的原生运行时文件解包至 `%TEMP%/.net`，所以「单文件」并不表示运行时完全不写临时文件。见 [.NET 单文件发布说明](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)。

等价的可复现命令（将 `win-x64` 换成 `win-arm64` 即可发布 ARM64）：

```powershell
# 框架依赖、多文件
dotnet publish src/ClipZero/ClipZero.csproj -c Release -r win-x64 --self-contained false -p:RuntimeFrameworkVersion=10.0.14 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -p:UseSharedCompilation=false -o artifacts/publish/win-x64/framework-dependent

# 独立运行、单文件；WinForms 不启用 trimming
dotnet publish src/ClipZero/ClipZero.csproj -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -p:UseSharedCompilation=false -o artifacts/publish/win-x64/self-contained
```

ZIP 和校验文件位于 `artifacts/packages/`，包名包含版本、架构和模式。发布脚本每次使用干净输出，避免混入旧文件；默认运行时为 10.0.14，可通过 `-RuntimeFrameworkVersion` 显式指定。构建信息记录于包内 `BUILD-INFO.txt`。框架依赖版至少需要同架构 Desktop Runtime 10.0.14。独立运行版更新运行时需要重新发布应用。GitHub Actions 会构建、运行默认自动测试，并保存两种架构、两种发布模式的 ZIP artifact；不会自动发布 GitHub Release。发布步骤见 [发布指南](docs/releasing.md)。

## 项目结构

```text
src/
  ClipZero/             WinForms 窗口、托盘、原生 API、消息窗口与快捷键
  ClipZero.Core/        剪贴板模型、安全清空规则与撤销设置备份/恢复
tests/
  ClipZero.Tests/       无第三方依赖的控制台测试
scripts/publish.ps1     应用发布输出
scripts/package.ps1     版本 ZIP、校验值与构建信息
scripts/Set-ExplorerUndo.ps1  高级手工撤销设置工具
启动ClipZero.cmd         当前开发目录的主窗口启动入口
docs/                  技术说明、手工验收、发布指南
.github/workflows/     Windows 构建与发布 artifact
```

开发环境、源码职责与修改流程见 [开发文档](docs/development.md)；技术实现和并发保护见 [架构说明](docs/architecture.md)，Explorer 撤销设置的边界见 [独立说明](docs/explorer-undo.md)。欢迎通过 Issue 提交操作步骤、Windows 版本及架构；请避免上传真实剪贴板内容或敏感文件名。

## 许可

[MIT License](LICENSE)，允许使用、修改和分发。
