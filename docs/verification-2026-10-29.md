# ClipZero 1.0.0-rc.1 验证记录

日期：2026-10-29。目标为 GitHub 候选版，不能据此断言软件不存在任何 bug。

## 修复

- 对整个文件列表校验路径长度，拒绝第 9 项及以后超过 32,767 UTF-16 字符的路径；保留有效 ANSI 多字节路径与最大长度边界。
- 读取撤销设置期间禁用窗口和托盘的配置操作，并阻止直接事件启动写入，避免自身只读会话与写操作争用备份锁。
- 仅清理保留备份而不改配置时，不再提示注销。
- 发布使用干净 staging，避免旧文件混入；统一生成含许可、说明、版本、构建信息与 SHA-256 的包。

## 环境与结果

Windows 11 `10.0.26200`、x64；项目本地 SDK `10.0.402`，开发测试进程运行时 `10.0.13`。发布包指定 NETCore/WindowsDesktop Runtime `10.0.14`；该补丁已按 [.NET 官方支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) 核对。

| 检查 | 实际结果 |
| --- | --- |
| Release 全方案构建 | 0 警告、0 错误 |
| 剪贴板模拟 | 15 / 15 通过 |
| 撤销设置模拟 | 24 / 24 通过；仅清理备份不再提示注销 |
| 安全原生/UI | 14 / 14 通过，0 跳过；含非预览项超长路径、最大长度、阻塞读取期间禁止写入 |
| UI 测试消息循环 | 私有桌面子进程使用真实 Application.Run，开启非法跨线程访问检查；修正仅 DoEvents 导致的测试回调线程错误 |
| 开发 EXE 冒烟 | 3 / 3 通过：显示窗口、再次启动唤起、正常退出/互斥体释放 |
| x64 独立运行包冒烟 | 3 / 3 通过，且从 ZIP 解压后的 EXE 同样通过；不触发清空或设置修改 |
| Explorer PowerShell 辅助脚本 | 33 通过、0 失败、1 跳过；当前真实配置已为 DWORD 0，冲突用例安全跳过 |
| 原生剪贴板写入/清空 | 跳过：命名 station 权限不足（5），登录派生 station 已存在（183） |
| 四种发布模式 | x64 / ARM64 × 独立运行 / 框架依赖均构建成功，指定运行时 10.0.14 |
| 四种 ZIP | 必需文件、EXE 架构、运行时配置和 SHA-256 检查通过；不含本机工具、bin/obj 或真实配置备份 |
| 窗口截图 | 查看复制、缩小滚动窗口和持久操作结果截图；不代替真实 DPI 验收 |

打包流程实测旧目录的 marker 不会进入新发布目录。ARM64 首次还原因本机 .NET 到 NuGet 的证书链错误失败；使用正常 TLS 从 NuGet 官方 HTTPS 下载相同依赖，并以临时本地 feed 完成还原，未关闭证书校验或修改系统证书。正式交付包在源码提交后重新生成，包内 BUILD-INFO 标识干净提交；GitHub Release 指向相同提交，并附各 ZIP 的 SHA-256。远端 CI 结果以 [GitHub Actions](https://github.com/zhu1873865/ClipZero/actions) 的对应提交为准。

可复现命令：

```powershell
dotnet build ClipZero.slnx -c Release
dotnet run --project tests/ClipZero.Tests/ClipZero.Tests.csproj -c Release --no-build -- --ui --native --development
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\ExplorerUndo.Tests.ps1
.\scripts\package.ps1 -Runtime win-x64 -Mode self-contained
.\scripts\package.ps1 -Runtime win-x64 -Mode framework-dependent
.\scripts\package.ps1 -Runtime win-arm64 -Mode self-contained
.\scripts\package.ps1 -Runtime win-arm64 -Mode framework-dependent
.\tests\Packages.Tests.ps1 -RequireClean
```

只安装项目内 SDK 时，将 `dotnet` 替换为 `& .\.tools\dotnet\dotnet.exe`；开发 EXE 冒烟还需按 [测试文档](testing.md) 设置 DOTNET_ROOT。

## 未验证范围

- 独立 window station 创建失败（命名 5、登录派生 183）；原生剪贴板写入/清空套件跳过，没有退回用户剪贴板。
- 真实 Explorer 复制/粘贴、托盘鼠标、物理快捷键、Win+V、真实多屏 DPI 与没有 .NET 的干净机器。
- ARM64 包仅交叉编译和检查包内容，未在 ARM64 Windows 运行。
- 未修改真实 Explorer 配置；实验性设置保存成功仍不证明隐藏菜单或禁用撤销。

真实剪贴板未清空，原文件未修改，未重启 Explorer 或注销系统。
