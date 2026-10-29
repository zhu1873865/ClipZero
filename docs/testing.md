# 验收测试

本文提供可重复的手工步骤，不代表所有步骤已经在每个系统版本上执行。发布前记录 Windows 版本、处理器架构、SDK、运行时版本及实际通过项目。

## 自动测试

在仓库根目录执行：

```powershell
dotnet build ClipZero.slnx -c Release
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build
```

默认控制台测试针对剪贴板服务与受控 API 替身，验证格式识别、快照版本变化、锁与清空失败、忙状态后的恢复和资源释放；另用撤销设置存储替身验证只读启动、准确备份与恢复、数据类型、损坏备份、账户与目标校验、冲突、写入失败及校验失败。还验证当前值仍为备份原值时的安全重试，以及恢复配置后备份删除失败的重试路径；只清理备份时不重写配置、不要求重新登录。不更改用户当前剪贴板或真实 Explorer 设置。退出码 `0` 表示全部通过，非 `0` 表示存在失败；具体覆盖项以测试程序输出为准。GitHub Actions 执行同一默认套件。UI 重试调度、系统消息和真实资源管理器操作仍需独立验证。

Windows 上可追加原生集成测试：

```powershell
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build -- --native
```

测试父进程创建独立的非交互 **window station** 和桌面，再启动子进程；子进程先核对所在 station，成功后才调用剪贴板 API。仅另建桌面不能隔离用户剪贴板，因此测试不是在当前 window station 中开一个隐藏桌面。创建隔离失败时输出 `SKIP`，不退回用户剪贴板，也不能将跳过记为原生测试通过。

原生套件使用临时测试文件并清理它们，验证单文件、多文件、文件夹、剪切、纯文本、过期请求、未知 DropEffect、混合格式、畸形数据、真实锁占用、`WM_CLIPBOARDUPDATE` 通知，以及操作后正常复制粘贴。它不启动交互式 Explorer，也不代替托盘、快捷键、Ctrl+V 或 DPI 手工验证。

独立的安全原生/UI 测试可以不写用户剪贴板地运行：

```powershell
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build -- --ui
```

该模式用私有 `HGLOBAL` 内存验证真正的 `DragQueryFileW` 路径解析及畸形数据拒绝；使用合成窗口消息验证消息窗口和激活，检查 STA 工作线程、快捷键冲突/注销以及主窗口状态。实验性文件撤销设置使用内存替身，检查读取前禁用按钮、关闭/恢复事件、保存中防止重复操作、读取失败、无备份时不能恢复；还创建真实托盘控制器，验证启动只读及托盘、窗口按钮连接到相同服务。测试停止剪贴板刷新计时器，不读取或改写用户剪贴板、真实注册表或备份；界面渲染在私有桌面中进行，截图写入 `artifacts/screenshots/`。测试前先退出已经运行的 ClipZero，避免同名消息窗口干扰查找。快捷键会短暂注册；已被占用时部分成功/冲突分支会输出 `SKIP`，应单独记录。CI 只运行默认 Core 和以下安全脚本套件，避免依赖托管 runner 的桌面与快捷键条件。

UI 检查还覆盖显式刷新重置有限重试预算、失败后禁用过期撤销按钮、操作结果在刷新后继续保留、仅实际配置写入提示重新登录，以及将窗口压缩到 320 逻辑像素高度后滚动到全部操作按钮。96/144/192 DPI 用例只模拟布局几何和字体大小，并非切换 Windows 的实际显示缩放，也不证明跨屏行为正确；物理显示器仍需按下方步骤手工验收。

验证当前开发构建的实际 EXE，无需重新打包：

```powershell
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build -- --development
```

该模式在私有桌面启动 `src/ClipZero/bin/Release/net10.0-windows/ClipZero.exe`，检查窗口启动、重复启动唤起以及正常退出。只安装项目内 SDK 时，需要将 `DOTNET_ROOT` 和 `DOTNET_ROOT_X64` 指向 `.tools/dotnet`，否则即使测试 DLL 能运行，子进程 EXE 也可能找不到 .NET 10 运行时：

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.tools\dotnet).Path
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
& .\.tools\dotnet\dotnet.exe .\tests\ClipZero.Tests\bin\Release\net10.0-windows\ClipZero.Tests.dll --development
```

上述本地 x64 SDK 命令仅适用于已在该目录安装 SDK 的开发环境。已有实例持有单实例锁时会跳过。实际应用会读取文件剪贴板元数据与撤销配置，但测试不触发清空、关闭或恢复设置。

实际 x64 便携 EXE 的冒烟测试：

```powershell
.\scripts\publish.ps1 -Runtime win-x64 -Mode self-contained
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release -- --portable
```

该模式在私有、非输入桌面启动 `artifacts/publish/win-x64/self-contained/ClipZero.exe`，检查默认窗口隐藏的托盘启动、第二次启动退出并唤起既有窗口、只有一个应用消息窗口，以及正常退出和互斥体释放。可以与 `--ui` 合用。测试不触发清空或快捷键；真实应用会按正常逻辑读取当前文件剪贴板元数据。它不会切换用户桌面，也不等同于在没有安装 .NET 的干净机器上验证独立运行。

Explorer 撤销辅助脚本的可复现测试：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\ExplorerUndo.Tests.ps1
# PowerShell 7 也可执行
pwsh -NoProfile -File .\tests\ExplorerUndo.Tests.ps1
```

测试只调用只读 `Status` 和 `-WhatIf`，不会改写真实 Explorer 注册表值；比较前后值确认未变。34 项检查包括 14 项类型/JSON 往返，以及临时 JSON 的备份不可覆盖、账户与目标项校验、冲突拒绝、WhatIf 等逻辑。当前值恰好为 `DWORD 0` 时，无法构造的恢复冲突用例会明确输出 `SKIP`，不会为了测试改动真实设置。这些测试不证明注销后的 Explorer 菜单效果。

## 此前版本验证记录

记录日期：2026-10-14。下表与产物体积来自软件内集成文件撤销保护之前的构建，不是新功能的验收结果。`--portable` 测试针对此前发布产物，本次没有重新打包。

| 项目 | 实际结果 |
| --- | --- |
| 环境 | Windows 11 `10.0.26200`，x64；.NET SDK `10.0.402`，运行时 `10.0.13` |
| Release 构建 | 成功，0 警告、0 错误 |
| Core 默认套件 | 15 / 15 通过 |
| `--ui` 安全原生/UI 套件 | 12 / 12 通过，0 跳过：8 项私有 HGLOBAL 解析与 4 项应用检查；复制、剪切、空状态、无法读取四种截图均已生成并查看 |
| `--portable` 实际压缩 EXE 冒烟 | x64 独立运行包 3 / 3 通过：托盘启动、重复启动/窗口唤起、正常退出/互斥体释放；不执行剪贴板清空 |
| `--native` 原生剪贴板套件 | **跳过**：无法创建独立 window station（错误 183）；没有退回用户剪贴板，未验证真实剪贴板写入/清空 |
| 发布 | x64、ARM64 的框架依赖版与压缩独立运行单文件版均编译成功 |
| Explorer 辅助脚本套件 | Windows PowerShell 5.1 与 PowerShell 7.6 均为 34 通过、0 失败、0 跳过；未执行真实设置关闭或恢复 |

此前编译得到的应用产物体积（不含文档、脚本与 ZIP 外壳）：

| 架构 | 独立运行压缩单 EXE | 框架依赖 EXE |
| --- | ---: | ---: |
| x64 | 51,697,490 字节，约 49.30 MiB | 174,592 字节 |
| ARM64 | 50,442,578 字节，约 48.11 MiB | 152,064 字节 |

框架依赖版必须分发整个目录，表中的 EXE 体积不是整个包的大小。ARM64 编译成功不代表已在 ARM64 机器运行验证。交互式 Explorer 复制/粘贴、托盘点击、物理快捷键、高 DPI、Win+V 以及 Explorer 撤销设置的实际效果尚需按下面清单验证。

## 当前集成版本验证记录

记录日期：2026-10-21。以下为当前开发目录构建及实验性设置文案修正后的回归结果，未重新发布 ZIP。

| 项目 | 实际结果 |
| --- | --- |
| Release 全方案构建 | 成功，0 警告、0 错误 |
| 默认控制台套件 | 39 项通过：剪贴板 15 项，文件撤销服务替身 24 项；不操作真实注册表或用户剪贴板 |
| `--ui` 安全原生/UI 套件 | 12 项通过，0 跳过；含撤销状态、托盘与窗口按钮服务连接、只读启动、显式刷新、持久反馈、压缩窗口与模拟 DPI，以及真实快捷键冲突与注销分支 |
| `--development` 当前 EXE 冒烟 | 3 / 3 通过：使用项目本地运行时启动窗口、重复启动唤起原窗口、正常退出并释放单实例锁；未触发剪贴板清空或设置修改 |
| 窗口截图 | 输出于 `artifacts/screenshots/`；含复制、剪切、无文件、无法读取、撤销状态与结果；新增 `undo-feedback.png`、`compact-scrolled.png` 和 `compact-simulated-{96,144,192}dpi.png`，模拟图不能替代实际 DPI 验收 |
| 当前程序入口 | 先退出旧托盘实例，再双击根目录 `启动ClipZero.cmd`，运行最新开发构建的主窗口 |
| 真实 Explorer 设置与菜单 | 自动测试未执行配置写入/恢复；10 月 21 日只读检查确认当前值为 `DWORD 0`。用户报告桌面撤销项仍为灰色、无法点击；未隐藏，尚不能判断后续新操作能否撤销，详见 [现场记录](explorer-undo.md#当前机器的检查记录) |
| 发布产物 | 未重新打包；已有 ZIP 属于此前版本，不能用于验收本次集成 |

以上自动检查共 54 项。自动测试证明按钮行为、服务逻辑及受控失败路径，不能证明 Windows 在注销后的菜单效果。实际鼠标点击、物理快捷键、系统 DPI 与 ARM64 仍需手工验证。配置为零或菜单灰色都不能作为已永久禁用撤销的证据。

本轮开发 EXE 冒烟初次因未给子进程配置本地运行时而失败；按上面的 `DOTNET_ROOT` 配置重跑后，三项启动检查全部通过。这是开发环境运行时查找问题，不作为 Explorer 行为验证结果。

## 手工准备

使用临时测试目录中的无关紧要文件，不使用工作文档。准备 `one.txt`、`two.txt`、`three.txt`、一个文件夹及一个新的粘贴目标文件夹。记录文件内容和位置，便于确认清空没有改变它们。启动 ClipZero；如果托盘默认隐藏，将图标拖到任务栏可见区域。

清空前可以先在窗口中确认状态。复制内容后等待状态更新，再执行清空；不要把某次复制之后再次复制的结果误认为同一项目。测试过程中不会要求删除真实文件。

## 必测场景

| 编号 | 操作 | 预期 |
| --- | --- | --- |
| 1 | 资源管理器选中 `one.txt`，Ctrl+C；单击托盘 | 显示已复制 1 个文件，名称正确；清空后无待粘贴文件，源文件仍在原位且内容不变 |
| 2 | 选中三个测试文件，Ctrl+C；从窗口清空 | 数量为 3、预览名称正确，清空成功，三个源文件不变 |
| 3 | 选中测试文件夹，Ctrl+C；从菜单清空 | 文件夹作为 1 个项目识别，清空不修改文件夹及其内容 |
| 4 | 选中测试文件，Ctrl+X；按 Ctrl+Alt+Backspace | 显示剪切状态；清空后源文件没有移动或删除，不能再粘贴该剪切项目 |
| 5 | 记事本复制普通文本；尝试清空 | 显示无待粘贴文件，不删除文本；粘贴到记事本仍得到该文本 |
| 6 | 复制测试文件并清空，进入空目标文件夹或桌面按 Ctrl+V | 不产生此前文件副本；从 Win+V 历史重新选取属于新操作，不计入该步骤 |
| 7 | 依次复制文件、文本、文件夹 | 托盘在黄色和灰色之间随通知更新，数量与窗口一致 |
| 8 | 让测试程序暂时持有 `OpenClipboard`，期间尝试刷新或清空 | UI 不崩溃；占用持续时显示无法读取/失败提示；释放后刷新可恢复 |
| 9 | 连续启动两次 ClipZero；也测试 `--show` | 只有一个进程提供托盘交互，不出现两个托盘图标；已存在实例行为以实际提示为准 |
| 10 | 从托盘退出，随后正常复制和粘贴文本、测试文件 | Windows 复制粘贴正常，Ctrl+Alt+Backspace 不再触发 ClipZero |

第 8 项可先复制临时测试文件，让 ClipZero 显示已复制状态；然后另开一个 PowerShell 窗口运行以下代码，利用 5 秒占用窗口点击 ClipZero 清空或刷新。代码只取得并释放剪贴板锁，不读取内容、不清空、不改写格式：

```powershell
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ClipZeroClipboardLockTest {
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();
}
"@

if (-not [ClipZeroClipboardLockTest]::OpenClipboard([IntPtr]::Zero)) {
    throw '未取得剪贴板锁，请稍后重试。'
}
try {
    Write-Host '剪贴板保持占用 5 秒；现在尝试 ClipZero 操作。'
    Start-Sleep -Seconds 5
}
finally {
    [ClipZeroClipboardLockTest]::CloseClipboard() | Out-Null
}
```

锁释放后在 ClipZero 窗口刷新状态，确认仍能识别此前的文件；再次主动清空应成功。占用本身没有改变剪贴板，不一定产生新的变化通知，所以释放后需要用户刷新。

## 边界与 UI

| 场景 | 步骤与预期 |
| --- | --- |
| 快照变更 | 复制文件 A，迅速复制文本 B 或文件 C，再触发旧状态清空；过期请求放弃，最新内容不能被旧快照清掉。并发极短窗口主要由自动替身测试验证 |
| 持续复制 | 连续多次复制文件和文本；状态最终与最新项目一致，无异常或死循环 |
| 混合格式 | 测试程序同时提供 `CF_HDROP` 和文本格式；执行清空会清空整个项目；纯文本不会清空 |
| 快捷键冲突 | 另一个测试程序先注册相同快捷键，再启动 ClipZero；看到适度提示，托盘和按钮继续工作 |
| 高 DPI | 在 100%、150%、200% 缩放下检查文本、列表、按钮；跨不同缩放显示器移动窗口，不出现遮挡或失焦 |
| 小窗口与持久反馈 | 缩小窗口高度，滚动到所有按钮；操作实验性配置后刷新剪贴板，结果提示继续可见；仅备份清理不要求重新登录 |
| 显式刷新与安静托盘 | 文件状态读取失败后解除占用，从窗口「刷新」或托盘「刷新状态」恢复；无待粘贴文件时单击灰色托盘不反复弹提示 |
| 长文件名和大量项目 | 准备长名称、多个文件；数量准确，预览受限时显示说明，窗口尺寸与托盘工具提示仍正常 |
| 关闭与重开 | 关闭窗口后托盘继续响应；菜单重开窗口；隐藏/显示多次后只有一个窗口 |
| 无网络 / 普通权限 | 普通用户、断网时运行已发布包；无需 UAC，检测和清空仍可工作 |
| Explorer 撤销菜单 | 重命名临时文件夹，清空文件剪贴板；撤销重命名菜单可能仍存在，这是已知范围，不属于失败 |
| Windows 历史 | 开启 Win+V 历史，清空当前文件剪贴板；历史条目保持，这是预期；重新选取后按新内容处理 |

软件内可选「文件撤销设置（实验性）」的验证见 [操作说明](explorer-undo.md)。只测试剪贴板功能无需修改该设置；不得仅依据界面「关闭设置已保存，效果未确认」、脚本 `Status`、`-WhatIf` 或菜单灰色，将实际撤销禁用/恢复效果标为通过。已经重新登录后不再要求反复注销；若临时目录的新操作仍能撤销，应记录方案无效并恢复原配置。菜单是否隐藏、是否能点击、能否撤销新操作应分别记录。

## 发布验证记录

每次发布复制以下模板并填写，不能将未执行项目标记为通过：

```text
版本 / commit：
Windows 版本与 build：
架构：
SDK / Runtime：
发布模式：
自动测试命令与结果：
手工场景通过编号：
未验证项目：
发现问题 / 已知限制：
```
