# 文件撤销设置（实验性，可选）

桌面右键菜单的「撤销重命名」「撤销删除」「撤销复制」属于资源管理器的文件操作撤销记录。ClipZero 的「取消文件复制」只清空当前文件剪贴板；清空剪贴板不会移除这些撤销菜单。

此功能集成在 ClipZero 主窗口和托盘右键菜单中，不需要输入命令或运行额外脚本。只启动软件不会修改这个设置，只有点击尝试关闭或恢复时才会操作。**保存配置不等于隐藏或禁用菜单；目前没有实现保证移除这两个菜单项的功能。**

## 在软件中操作

1. 打开 ClipZero 主窗口，找到 **「文件撤销设置（实验性）」**；托盘右键菜单也提供入口。
2. 点击 **「尝试关闭撤销」**，查看软件显示的操作结果。「关闭设置已保存，效果未确认」只说明 `MaxUndoItems` 已保存为 `DWORD 0`。
3. 若刚修改设置，可在保存工作、等待文件操作完成后，点击 **开始菜单 → 账户头像 → 注销**，再重新登录一次进行检查。已经重新登录过就不必反复注销。

想恢复时，在同一处点击 **「恢复原设置」**，按窗口中的结果提示决定是否需要重新登录。这里的「恢复」只把配置还原成修改前的设置，不会恢复删除或重命名过的文件；若只是清理失败操作留下的备份、没有写入配置，则无需为此重新登录。

此设置尝试影响桌面和文件资源管理器的**全部文件撤销／重做**，包括右键菜单及 **Ctrl+Z / Ctrl+Y**，并非只隐藏两个项目。记事本、Office 等软件里的文字撤销不受影响。由于使用 Windows 未公开的设置，不能保证任何具体 Windows 版本的实际效果。

软件会显示中文状态、结果或错误提示，不会自动注销、重启电脑或结束资源管理器。无需管理员权限，也无需 PowerShell。请保留 `%LOCALAPPDATA%\ClipZero` 中的备份，直到恢复完成；这份备份不在程序文件夹中。

当前工作目录中已有的发布 ZIP 是先前构建，不包含本次集成。请构建并运行当前源码进行验收；本次不会重新打包或发布 Release。

## 效果和限制

- 修改当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` 下的 `MaxUndoItems`，写入 `DWORD 0`。
- 该值用于尝试限制资源管理器和桌面文件操作的撤销／重做，包括 `Ctrl+Z` 和 `Ctrl+Y`；不能依据值为零就断言功能已禁用，更不能保证隐藏「撤销重命名」「撤销删除」两个项目。
- 不影响记事本、Office 等其他应用内部的文本编辑撤销；不删除原文件、不清空回收站，也不影响从回收站手动还原。
- 这是 **Windows 未公开文档化的设置**，没有微软的兼容性保证。不同 Windows 10／11 版本的菜单表现可能不同，后续系统更新也可能不再生效。
- 刚写入配置后，可在方便时注销并重新登录一次再检查。软件不会注销、重启电脑、结束 Explorer 或打断正在进行的文件操作。仅看到设置已写入或菜单为灰色，不能证明后续文件操作的撤销已禁用；重新登录后仍能撤销新操作，就应视为此方案无效并恢复原设置，反复注销没有意义。
- 没有找到 Windows 官方提供的「仅隐藏原生撤销菜单」设置。`NoViewContextMenu` 会禁用整个桌面和资源管理器右键菜单，因此本项目不使用它。

微软文档确认文件操作撤销状态由 Explorer 进程维护，和剪贴板不同：[IFileOperation::SetOperationFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)。微软 PowerToys 仓库中也有提出 `MaxUndoItems` 方案的用户需求，但它仍是待评估的建议，并非官方支持说明：[PowerToys #38164](https://github.com/microsoft/PowerToys/issues/38164)。整个右键菜单策略见 [NoViewContextMenu](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-windowsexplorer#noviewcontextmenu)。

## 当前机器的检查记录

2026-10-21 只读检查确认当前用户的 `MaxUndoItems` 为 `DWORD 0`；配置备份创建于 10 月 14 日，系统最近启动于 10 月 16 日、Explorer 进程启动于 10 月 17 日。用户反馈桌面撤销项仍显示，但为灰色、无法点击。检查没有修改系统设置。

这些信息说明当前菜单项无法执行，当前没有点击触发撤销的风险；**彻底隐藏菜单尚未实现**。灰色也可能只是当前没有可撤销操作，不能单凭这一状态认定永久禁用或认定方案无效。后续若需验证，应在临时目录做一次新的文件操作后再检查，不需要为这次排查反复注销。

Windows 菜单接口也区分「灰色不可选」和「隐藏」：[`MENUITEMINFO` 的 `MFS_DISABLED` / `MFS_GRAYED`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-menuiteminfoa) 表示项目灰色且无法选择；Shell 的 [`ECS_DISABLED` 与 `ECS_HIDDEN`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_expcmdstate) 分别表示不可用和隐藏。这些接口定义支持上述状态区分，并不证明 `MaxUndoItems` 导致了当前灰色状态。

## 高级手工工具（可选）

软件界面已能完成实验性配置写入和恢复。此前的 `Set-ExplorerUndo.ps1` 仍保留为独立的手工工具，方便查看配置和预览操作；应用本身不调用这个脚本。需要时可在项目根目录打开普通 PowerShell，无需管理员权限。以下命令仅为使用说明，项目构建与测试不会执行 `Disable` 或 `Restore`。

查看当前设置（默认也是只读 `Status`）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-ExplorerUndo.ps1 -Action Status
```

预览将执行的操作，不改注册表、不写备份文件：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-ExplorerUndo.ps1 -Action Disable -WhatIf
```

确认接受关闭全部 Explorer 文件操作撤销／重做后，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-ExplorerUndo.ps1 -Action Disable
```

恢复脚本运行前的设置：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-ExplorerUndo.ps1 -Action Restore
```

`ExecutionPolicy Bypass` 只作用于这次 PowerShell 进程，不修改机器执行策略。如果组织策略禁止运行脚本，请遵守该策略。

默认备份文件为 `%LOCALAPPDATA%\ClipZero\explorer-undo-backup.json`。可以用 `-BackupPath "D:\backup\clipzero-explorer-undo.json"` 指定其他 JSON 文件；查看、关闭和恢复时应使用同一个路径。请保留备份直到恢复完成，不要把它复制到其他 Windows 账户使用；脚本会校验备份中的账户标识。

## 恢复保护

应用与辅助脚本使用兼容的备份格式，先保存原始值，再写注册表。备份记录原先是否存在该值，以及它的准确类型和内容；字符串、未展开的环境变量字符串、多字符串、DWORD、QWORD、二进制和 REG_NONE 都能保留。原先不存在时，恢复会删除工具新增的 `MaxUndoItems` 值，其他注册表内容不受影响。

已有备份不会被覆盖：重复尝试关闭且当前仍是 `DWORD 0` 时，不重复修改。应用还处理未完成的操作：如果当前值与备份原值完全相同，允许重新尝试写入，或通过「恢复原设置」复核后仅删除备份、不重写注册表。如果当前值既不是备份原值、也不是 `DWORD 0`，则停止，以免覆盖后来的修改。工具通过写入前重读和写入后校验减少冲突；请勿同时用其他工具调整这个值。

高级 PowerShell 脚本的行为较严格：已有备份而当前值不是 `DWORD 0` 时会停止，尚未提供上述应用内的重试与备份清理路径。遇到未完成操作时优先使用应用的恢复按钮，不要手工删除备份或改值来绕过检查。

恢复成功且校验通过后才删除备份，随后可以开始下一次关闭／恢复周期。手工脚本可加 `-WhatIf` 预览。如果原本已经是 `DWORD 0` 且没有备份，工具不会虚构原始状态，也不会代为恢复为默认值。如果备份写入失败，注册表不会被改写；如果写备份后设置发生变化或注册表写入失败，备份保留以便检查。

## 手工验收

1. 打开主窗口的「文件撤销设置（实验性）」，检查当前状态，点击「尝试关闭撤销」，确认显示的操作结果。也可从托盘右键菜单操作。
2. 仅在刚写入配置且尚未重新登录时，保存工作、等待文件操作完成后，自行注销并重新登录一次。
3. 在专用临时目录中创建空文件并重命名，检查桌面和该目录的右键菜单，分别记录项目是否显示、能否点击，以及 `Ctrl+Z`／`Ctrl+Y` 能否撤销。需要测试删除时仅使用这些临时空文件。不要用点击真实文件的撤销项目来验证。新操作后仍能撤销，则此方案在该系统上无效；停止尝试并恢复原设置。
4. 在记事本输入文字并用 `Ctrl+Z` 撤销，确认应用自身的撤销仍然可用。
5. 点击「恢复原设置」，按结果提示决定是否需要重新登录，再在测试目录确认 Explorer 的原有行为。未隐藏菜单应单独记录，不能以配置写入成功替代此项验收。

当前仅确认菜单灰色、无法点击，后续新操作的撤销效果仍需手工验证；自动测试不会修改开发机器的 Explorer 设置。
