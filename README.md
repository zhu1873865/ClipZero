# ClipZero

**文件复制状态，一键归零。**

轻量的 Windows 文件剪贴板工具。复制或剪切文件后，单击托盘图标即可取消待粘贴状态，减少在桌面、文件夹里误粘贴产生的副本。

[下载候选版](https://github.com/zhu1873865/ClipZero/releases/tag/v1.0.0-rc.1) · [使用方法](#使用) · [反馈问题](https://github.com/zhu1873865/ClipZero/issues) · [开发文档](docs/development.md)

![Build](https://github.com/zhu1873865/ClipZero/actions/workflows/build.yml/badge.svg?branch=main)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 为什么做 ClipZero

文件已经复制完，剪贴板却还留着上一批文件。整理桌面时误点「粘贴」，或习惯性按下 Ctrl+V，就可能多出一堆副本。

ClipZero 把当前文件复制状态放到托盘里：**黄色表示有文件待粘贴，点一下即可取消；灰色表示当前没有待粘贴文件。** 取消剪切也只清空剪贴板，原位置的文件保持不变。

它适合希望手动收尾文件复制、剪切操作的人：不用打开终端，也不用等到误粘贴之后再清理。

## 下载

当前版本为 **v1.0.0-rc.1 候选版**。多数 Intel / AMD Windows 电脑选择 **x64 独立运行版**，解压后运行 `ClipZero.exe`，无需另装 .NET。

| 下载 | 适用情况 |
| --- | --- |
| [x64 独立运行版（推荐）](https://github.com/zhu1873865/ClipZero/releases/download/v1.0.0-rc.1/ClipZero-v1.0.0-rc.1-win-x64-self-contained.zip) | 常见 x64 Windows 电脑，内置运行时 |
| [ARM64 独立运行版](https://github.com/zhu1873865/ClipZero/releases/download/v1.0.0-rc.1/ClipZero-v1.0.0-rc.1-win-arm64-self-contained.zip) | ARM64 Windows 电脑，内置运行时；尚未实机验收 |
| [x64 框架依赖版](https://github.com/zhu1873865/ClipZero/releases/download/v1.0.0-rc.1/ClipZero-v1.0.0-rc.1-win-x64-framework-dependent.zip) | 包体较小，需要同架构 .NET Desktop Runtime |
| [ARM64 框架依赖版](https://github.com/zhu1873865/ClipZero/releases/download/v1.0.0-rc.1/ClipZero-v1.0.0-rc.1-win-arm64-framework-dependent.zip) | 包体较小，需要同架构 .NET Desktop Runtime；尚未实机验收 |

框架依赖版需要 **.NET Desktop Runtime 10.0.14 或更新的 10.0 补丁版**，并保留解压后的完整目录；仅安装普通 .NET Runtime 不够。[下载 .NET](https://dotnet.microsoft.com/download/dotnet/10.0)。

每个 ZIP 的 SHA-256 校验文件都在 [Release 附件](https://github.com/zhu1873865/ClipZero/releases/tag/v1.0.0-rc.1) 中。系统要求为支持 .NET 10 的 Windows 桌面系统，主要面向 Windows 11；Windows 10 LTSC / Enterprise 的具体范围以 [.NET 官方支持列表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) 为准。

## 使用

1. 解压发布包，运行 `ClipZero.exe`。首次启动只显示托盘图标，可在任务栏的隐藏图标区域找到它。
2. 在资源管理器中复制或剪切文件、文件夹，托盘图标会变黄。
3. 单击托盘图标，或按 **Ctrl+Alt+Backspace**，取消当前文件待粘贴状态。
4. 右键托盘图标可打开窗口、刷新状态或退出。关闭窗口后，程序会继续留在托盘。

主窗口也提供「取消文件复制 / 剪切」按钮。运行 `ClipZero.exe --show` 可直接打开窗口；没有待粘贴文件时单击灰色图标保持安静。快捷键被其他软件占用时会提示，托盘和窗口按钮仍可使用。

| 托盘状态 | 含义 |
| --- | --- |
| 灰色 | 当前没有标准文件待粘贴状态 |
| 黄色 | 检测到文件复制、剪切，或操作类型未知的文件列表 |
| 棕色 | 状态暂时无法读取，可稍后刷新 |

## 功能

- **托盘一键取消**：支持标准文件、文件夹的复制与剪切，窗口显示项目数量和文件名摘要。
- **快捷键操作**：Ctrl+Alt+Backspace；支持冲突提示和重复启动唤起已有窗口。
- **核对后再清空**：清空前重新检查剪贴板格式与版本，内容变化时放弃旧请求。
- **轻量常驻**：使用 Windows 原生变化通知；剪贴板占用时有限重试，读取放在独立 STA 线程。
- **本机使用**：无需管理员权限，程序运行时不联网、不收集信息、不保存剪贴板历史。

软件不设置开机启动，不自动定时清空，也不拦截 Ctrl+C / Ctrl+V；取消操作由你主动触发。

## 文件安全与功能边界

- **不会删除、移动、重命名或修改原文件**，也不读取原文件内容。取消剪切后，原位置的文件仍然存在。
- 纯文本、密码文本、图片等非文件项目不读取内容，也不会被清空。若同一个剪贴板项目同时包含标准文件列表和其他格式，取消时会清除该项目的全部格式。
- 不清理 **Win+V 剪贴板历史**或第三方历史记录。重新选取历史项目后，会按新的当前内容处理。
- 识别标准 `CF_HDROP` 文件列表，不支持邮件附件、部分云盘等第三方虚拟文件格式。只保留当前最多 8 个文件名摘要，不保存完整路径或历史。
- 超过 16 MiB 的文件列表、超过 32,767 字符的单项路径或畸形格式会被拒绝读取和清空。
- 已经完成的粘贴不会被撤销。退出 ClipZero 后，Windows 正常复制和粘贴继续工作。

### 可选：文件撤销设置（实验性）

主窗口与托盘菜单提供「尝试关闭撤销」和「恢复原设置」，只有你主动点击才会修改配置。修改前备份原值，恢复时检查冲突。

这项功能使用 Windows 未公开的配置，尝试影响 Explorer 的全部文件撤销 / 重做，包括 Ctrl+Z / Ctrl+Y。**配置保存成功不代表撤销已禁用，也不保证隐藏「撤销重命名 / 撤销删除」菜单。** 清空文件剪贴板本身不会移除 Explorer 的撤销记录。

软件不会自动重启 Explorer、注销或重启电脑。操作范围、备份恢复和效果检查见 [实验性设置说明](docs/explorer-undo.md)。

## 当前验证情况

v1.0.0-rc.1 已通过 39 项核心模拟测试、14 项安全原生/UI 检查，以及开发 EXE 和解压后 x64 便携 EXE 的启动、单实例与退出检查。四种发布包的内容、架构、运行时、源码提交和 SHA-256 已核验，GitHub 主分支及标签 CI 均通过。完整结果见 [验证记录](docs/verification-2026-10-29.md)。

当前仍是候选版：原生剪贴板写入 / 清空测试因隔离环境权限与名称冲突跳过；真实 Explorer 交互、物理快捷键、多屏 DPI、没有 .NET 的干净机器与 ARM64 运行仍需手工验收。

## 从源码构建

需要 Windows 桌面环境与 .NET 10 SDK。在仓库根目录执行：

```powershell
dotnet restore ClipZero.slnx
dotnet build ClipZero.slnx -c Release --no-restore
dotnet run --project src/ClipZero/ClipZero.csproj -c Release --no-build -- --show
```

运行默认测试，不更改真实剪贴板或 Explorer 设置：

```powershell
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build
```

生成本地 ZIP 与 SHA-256：

```powershell
.\scripts\package.ps1 -Runtime win-x64 -Mode self-contained
.\scripts\package.ps1 -Runtime win-arm64 -Mode self-contained
```

`-Mode framework-dependent` 可生成框架依赖包。输出位于 `artifacts/`；脚本默认使用运行时 10.0.14。独立运行版会将原生运行时解包到临时目录，首次启动可能稍慢。只需要应用输出目录时，使用 `scripts/publish.ps1`。

[开发环境与目录职责](docs/development.md) · [架构](docs/architecture.md) · [测试与手工验收](docs/testing.md) · [发布流程](docs/releasing.md)

## 反馈与贡献

欢迎通过 [Issues](https://github.com/zhu1873865/ClipZero/issues) 反馈问题或建议。报告问题时请附上 Windows 版本、处理器架构、ClipZero 版本，以及可复现的操作步骤；请使用临时测试文件，避免提交真实剪贴板内容或敏感文件名。

开发协作约定见 [AGENTS.md](AGENTS.md)，项目接续信息见 [HANDOFF.md](docs/HANDOFF.md)。

## 许可

[MIT License](LICENSE)，允许使用、修改和分发。
