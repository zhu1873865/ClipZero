# 技术方案与范围

ClipZero 检测并清空当前剪贴板里的标准文件待粘贴状态，还提供由用户主动操作的实验性文件撤销设置。剪贴板功能不删除、移动、重命名或写入原文件；读取路径用于展示文件名，不枚举文件夹内容，不保存历史。实验性功能仅修改当前用户的一项 Explorer 配置，并保存用于恢复的配置备份，不保证菜单隐藏或撤销功能禁用。

## 剪贴板检测

WinForms 消息循环提供原生窗口句柄。窗口注册 `AddClipboardFormatListener`，收到 `WM_CLIPBOARDUPDATE` 后读取剪贴板；不进行高频常驻轮询。读取与清空由专用 STA 工作线程执行，避免其他程序延迟提供数据时阻塞主窗口。退出时注销监听器和快捷键，释放托盘对象。

使用 `OpenClipboard` 取得短期互斥访问权，确认 `CF_HDROP`，再用 `GetClipboardData` 和 `DragQueryFileW` 读取文件列表。`CF_HDROP` 中的目录与普通文件同样属于文件列表，无需扫描磁盘判断。注册格式 `Preferred DropEffect` 中的 `DROPEFFECT_MOVE` 表示剪切，`DROPEFFECT_COPY` 表示复制；缺失或不明确时显示操作类型未知。第一版不解析 `FILEDESCRIPTOR` / `FILECONTENTS` 等虚拟文件格式。

当前状态为无文件、复制、剪切、操作类型未知或暂时无法读取。一次变化通知的状态读取最多尝试 5 次，并逐次延长间隔；持续占用后展示无法读取状态，后续变化通知或用户刷新可以恢复。完整路径不保留；内存快照只保留当前最多 8 个显示名，每名最多 512 字符，以及完整项目数量。路径列表在有界内存扫描中逐项检查长度，包括未显示的第 9 项及之后项目；ANSI 长路径按转换后的 UTF-16 长度校验。

窗口「刷新」和托盘「刷新状态」共同触发状态读取，显式刷新会重置读取失败计数，重新给予有限重试预算；读取仍按原规则合并，不形成常驻轮询。意外清空异常先将旧快照标为不可用，再安排有限刷新，避免重新启用过期的清空按钮。

官方接口参考：[剪贴板格式](https://learn.microsoft.com/windows/win32/dataxchg/standard-clipboard-formats)、[监听剪贴板](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)、[Shell 剪贴板格式](https://learn.microsoft.com/windows/win32/shell/clipboard)。

## 清空与并发

点击清空时依据当前已展示的文件快照发起请求，而不是无条件清空剪贴板。取得剪贴板锁后，再核对当前版本与文件格式；版本变化、非文件内容或读取失败时，都不会调用 `EmptyClipboard`。确认目标仍是同一文件项目后，在锁内清空，再更新界面。

`GetClipboardSequenceNumber` 用于判断快照是否过期；`OpenClipboard` 则防止核对和 `EmptyClipboard` 之间被另一个进程改变内容。`GetClipboardData` 可能触发延迟渲染，因此读取格式后、清空前再次核对版本。锁会在所有成功和失败路径中释放。其他程序在锁释放后复制的新内容是新的有效项目，工具不会追着清空它。

清空遇到锁占用时最多追加 3 次短暂重试，始终使用点击时捕获的同一个版本号；不会在重试中改为清除后来复制的新项目。状态通知被合并处理，重试次数有限，避免阻塞 UI 或产生忙循环。

清空一个项目会清除其所有剪贴板格式。只要包含文件列表，原生接口就不能可靠地只取消文件粘贴并同时保留同项目的所有其他格式。本工具因此明确采用整项目清空；纯文字等项目完全不清空。

此过程与 Win+V 历史不同。ClipZero 不调用 Windows 剪贴板历史清理 API，也不处理第三方历史数据库。已经执行的粘贴不受影响。

参考：[OpenClipboard](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openclipboard)、[EmptyClipboard](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-emptyclipboard)、[GetClipboardSequenceNumber](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber)。

## 托盘、快捷键与单实例

应用使用 `ApplicationContext` 管理生命周期，默认不显示主窗口。原生消息窗口负责监听和全局快捷键，`NotifyIcon` 显示状态；主窗口按需打开，关闭主窗口不结束进程。工具提示长度受 Windows 限制，因此显示摘要，完整名称在窗口中查看。

无文件时单击灰色托盘只刷新，不重复弹出空状态提示；相同提醒短时间内去重。清空发现剪贴板已改变时，主窗口展示新状态，并放弃旧请求。主窗口内容可滚动，首次显示和 DPI 变化时约束至显示器工作区；几何和字体的模拟缩放测试仍不能代替真实多显示器验收。

`RegisterHotKey` 注册 Ctrl+Alt+Backspace，失败时保留其他清空入口并告知用户。退出时使用 `UnregisterHotKey`，不安装键盘钩子。

命名互斥体限制当前会话中的实例数量。不把应用注册为服务，不要求管理员权限，不设置自启动。

## 文件撤销设置（实验性）

复制/剪切状态在 Windows 剪贴板里，重命名、删除、移动后的「撤销」属于 Shell / Explorer 文件操作历史。两者是不同机制，`EmptyClipboard` 不会清除 Explorer 的撤销记录。

Windows 对 `IFileOperation` 等接口提供是否记录某次文件操作的选项，但这不等于可以安全地清除其他 Explorer 窗口此前积累的撤销记录。本项目不重启或终止 Explorer，不模拟大批文件操作覆盖历史，也不调用未文档化接口修改 Shell 内部状态。

主窗口和托盘菜单均提供「尝试关闭撤销」与「恢复原设置」，连接到同一个 C# `ExplorerUndoService`。启动、打开窗口和更新状态只读配置；只有用户明确点击操作后才写设置。操作在后台执行，读取前与保存期间禁用两个按钮；读取失败也禁用旧的可操作状态，版本检查避免过期读取覆盖更新结果。结果持续显示在主窗口中，从托盘操作也会打开窗口展示，避免依赖系统通知送达。

`ExplorerUndoService` 将当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` 下的 `MaxUndoItems` 设为 `DWORD 0`，尝试限制整个 Explorer 文件操作撤销/重做，包括 Ctrl+Z / Ctrl+Y。这是未公开配置，不是微软保证的稳定接口。软件显示「关闭设置已保存，效果未确认」，不能据此断言菜单已隐藏或撤销已禁用。刚写入后可保存工作并自行重新登录一次；后续新操作仍可撤销即应判断方案无效并恢复配置，无需反复注销。不会自动执行注销，也不影响记事本等应用自己的文本撤销。当前机器菜单为灰色不可点击，尚不能仅凭此判断后续撤销行为；彻底隐藏仍未实现。

模型字段 `HasDisableSetting` 仅表示存储值满足 `DWORD 0`，不表示已经探测到 Explorer 行为变化。应用没有用注册表状态推断菜单是否可点、是否显示，或 `Ctrl+Z` 是否有效。

`WindowsExplorerUndoStore` 使用 .NET 自带的 `Microsoft.Win32.Registry` 和文件接口实现当前用户存储，无额外依赖，不启动 PowerShell。备份写入 `%LOCALAPPDATA%\ClipZero\explorer-undo-backup.json`，格式兼容此前 `Set-ExplorerUndo.ps1`：记录版本、账户 SID、目标注册表项和原值的准确存在状态、类型、内容。支持未展开的字符串、多字符串、DWORD、QWORD、二进制等类型；备份最大 16 MiB。

操作先以独占文件访问打开已有备份，或通过 `FileMode.CreateNew` 创建并落盘备份，防止覆盖原配置。写入前重读当前注册表值，写入后校验目标值；检测到后续修改、损坏备份或账户不匹配时停止，不覆盖其他工具的改动。注册表核对与写入之间仍存在极短并发窗口，不应同时使用其他工具调整此值。恢复时原值不存在则仅删除新增的 `MaxUndoItems`；成功并校验通过后才删除备份。无备份时不推测原值，也不提供自动恢复。

如果已有有效备份且当前值与备份原值完全一致，服务允许重试未完成的设置写入，保持原备份不变；也允许复核当前值后仅删除备份，以恢复可操作状态。恢复已完成而删除备份失败时，同样可安全重试清理，不会重复改写注册表。与原值、`DWORD 0` 均不匹配的外部修改仍视为冲突。结果模型单独标识是否实际写入配置，只有需要时提示重新登录；重复点击或仅清理备份不产生无用的注销提示。

服务通过 `IExplorerUndoStore` / `IExplorerUndoSession` 隔离平台存储，默认测试使用内存替身验证备份与失败边界，UI 测试同样不改写用户设置。原 PowerShell 脚本仅保留为高级手工工具，应用无需调用它；脚本的已有备份冲突判断较严格，不具备服务新增的原值匹配重试/清理路径。操作范围、现场记录与恢复步骤见 [实验性设置说明](explorer-undo.md)。
