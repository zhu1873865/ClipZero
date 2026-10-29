# ClipZero v1.0.0-rc.1 · 文件复制状态，一键归零

复制完文件，桌面却又被误粘贴出一堆副本？ClipZero 把当前文件复制状态放到 Windows 托盘里，点一下即可取消待粘贴状态，原文件保持不变。

这是 ClipZero 的首个公开候选版，使用 C#、.NET 10 和 Windows Forms 开发。无需管理员权限，程序运行时不联网，也不保存剪贴板历史。

## 下载后怎么用

大多数 Intel / AMD Windows 电脑选择 [x64 独立运行版](https://github.com/zhu1873865/ClipZero/releases/download/v1.0.0-rc.1/ClipZero-v1.0.0-rc.1-win-x64-self-contained.zip)，解压运行 `ClipZero.exe`，无需另装 .NET。

1. 在任务栏隐藏图标区找到 ClipZero。
2. 复制或剪切文件后，托盘图标变黄。
3. 单击图标，或按 **Ctrl+Alt+Backspace**，取消当前文件待粘贴状态。

右键托盘图标可打开窗口、刷新状态或退出；关闭窗口后继续驻留托盘。

| 附件 | 适用情况 |
| --- | --- |
| `win-x64-self-contained.zip` | 常见 x64 Windows 电脑，推荐，内置运行时 |
| `win-arm64-self-contained.zip` | ARM64 Windows 电脑，内置运行时；尚未实机验收 |
| `win-x64-framework-dependent.zip` | x64 小体积包，需要同架构 Desktop Runtime |
| `win-arm64-framework-dependent.zip` | ARM64 小体积包，需要同架构 Desktop Runtime；尚未实机验收 |

附件完整名称均以 `ClipZero-v1.0.0-rc.1-` 开头。框架依赖包需要 .NET Desktop Runtime **10.0.14 或更新的 10.0 补丁版**，并保留解压后的完整目录。每个 ZIP 都附有同名 `.sha256`，包内 `BUILD-INFO.txt` 标识源码提交、架构、模式和运行时。

## 本版功能与修复

- 标准文件 / 文件夹的复制与剪切识别，托盘一键取消，窗口查看数量与名称摘要。
- Ctrl+Alt+Backspace 快捷键，冲突提示，以及重复启动唤起已有窗口。
- 清空前重新核对剪贴板版本，避免旧请求清除后来复制的新内容。
- 修复第 9 项及之后的超长路径漏检、撤销配置读取与保存争用锁，以及仅清理备份误提示注销。
- 统一干净发布目录、版本 ZIP 和 SHA-256；发布运行时为 .NET 10.0.14。

## 文件安全与候选版范围

取消操作只清空当前文件剪贴板，不删除、移动、重命名或修改原文件。纯文本 / 图片等非文件项目不会被清空；同一个项目含文件与其他格式时，会整项清空。不清理 Win+V 历史，不支持第三方虚拟文件格式。

可选的「文件撤销设置」仍是实验功能：只有主动点击才修改配置，修改前备份原值。它使用 Windows 未公开配置，保存成功不能证明 Explorer 撤销已禁用，也不保证隐藏菜单。详见 [功能边界与恢复说明](https://github.com/zhu1873865/ClipZero/blob/v1.0.0-rc.1/docs/explorer-undo.md)。

Release 构建 0 警告、0 错误；39 项核心模拟与 14 项安全原生/UI 检查通过。开发 EXE 和最终 ZIP 解压后的 x64 EXE 启动、单实例及正常退出检查通过，四种包的内容、架构、运行时、干净提交与 SHA-256 已核验。

原生剪贴板写入 / 清空测试因隔离环境权限与名称冲突跳过。真实 Explorer 交互、物理快捷键、多屏 DPI、没有 .NET 的干净机器和 ARM64 运行仍需手工验收，因此本版保留候选版标记。

发布源码提交：`5f53e2703153bc055b9756c8bed3edd2a346eb48`。本次只整理公开介绍，不替换下载包或移动版本标签。

[完整验证记录](https://github.com/zhu1873865/ClipZero/blob/v1.0.0-rc.1/docs/verification-2026-10-29.md) · [主分支发布构建](https://github.com/zhu1873865/ClipZero/actions/runs/40832752675) · [候选版标签构建](https://github.com/zhu1873865/ClipZero/actions/runs/40832936167)

欢迎在 [Issues](https://github.com/zhu1873865/ClipZero/issues) 告诉我们哪里好用、哪里不顺手。反馈时请附 Windows 版本、架构、ClipZero 版本和复现步骤，避免上传敏感文件名或剪贴板内容。
