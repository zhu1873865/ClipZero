# 构建与 GitHub Release 发布

GitHub Actions 只构建并上传 artifact，不会创建标签或自动发布 Release。以下步骤用于维护者手工发布，首次发布前尤其需要完成 Windows 交互验收。首版功能和限制已整理在 [1.0.0 发布说明](release-notes.md)，发布时补充实际验证范围。

默认仅在本机开发、测试和提交 Git。本文中的本地输出命令不上传文件；后面的远端推送、GitHub 标签及 Release 仅在用户明确要求线上发布后执行。日常环境与本地 SDK 命令见 [开发文档](development.md)。

当前开发修改先完成软件内的「文件撤销设置（实验性）」和验收，不默认执行以下打包步骤。`artifacts/packages/` 中已有 ZIP 属于先前构建，不包含当前集成功能；不能作为当前源码的验证结果或交付包。

## 发布前

1. 确认源代码、README、LICENSE 和已知限制一致，填写下方发布说明。
2. 在 Windows 安装 .NET 10 SDK，执行 `dotnet --info` 并记录使用的 SDK 与运行时补丁版本。
3. 执行 `dotnet restore ClipZero.slnx`、`dotnet build ClipZero.slnx -c Release --no-restore` 和默认控制台测试。
4. 按 [验收清单](testing.md) 检查真实资源管理器、托盘、快捷键、单实例和高 DPI；记录尚未验证的系统与架构。
5. 分别发布目标架构和模式，测试解压后的包。独立运行包最好在没有 Desktop Runtime 10 的干净机器上验证；ARM64 包在 ARM64 Windows 上验证。

## 四种包

```powershell
.\scripts\publish.ps1 -Runtime win-x64 -Mode self-contained
.\scripts\publish.ps1 -Runtime win-x64 -Mode framework-dependent
.\scripts\publish.ps1 -Runtime win-arm64 -Mode self-contained
.\scripts\publish.ps1 -Runtime win-arm64 -Mode framework-dependent
```

脚本只构建本地目录，不上传，不创建 GitHub 标签。框架依赖版目录需要完整打包，独立运行版包含一个应用 EXE。打包时一并附上 LICENSE、README 和版本说明；MIT 要求分发时保留许可与版权声明。实验性文件撤销设置由 C# 直接实现，不依赖外部脚本。

推荐包名：

```text
ClipZero-v<版本>-win-x64-self-contained.zip
ClipZero-v<版本>-win-x64-framework-dependent.zip
ClipZero-v<版本>-win-arm64-self-contained.zip
ClipZero-v<版本>-win-arm64-framework-dependent.zip
```

生成 SHA-256 可校验下载完整性：

```powershell
Get-FileHash -Algorithm SHA256 .\artifacts\packages\*.zip
```

在 GitHub 创建指向已验证 commit 的版本标签，例如 `v1.0.0`；创建 Release，上传 ZIP 与哈希文本。使用 Actions artifact 时，确认 artifact 对应相同 commit，而不是其他分支的未验收构建。高级手工撤销脚本若随包保留，应附对应文档，不能只附脚本而遗漏边界和恢复说明。

独立运行包必须通过重新构建来更新其中的 .NET Runtime。发布前检查官方 [.NET 支持政策](https://dotnet.microsoft.com/platform/support/policy) 与 [系统兼容性](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)，避免沿用过期兼容声明。未做代码签名的 EXE 可能出现 Windows SmartScreen 下载提示；不能保证所有机器均无提示。

## 首版发布说明模板

```markdown
## ClipZero v1.0.0

Windows 文件粘贴保护工具：检测文件复制/剪切，单击托盘即可取消待粘贴状态。

- 标准文件/文件夹复制与剪切识别，展示数量与文件名。
- 灰色/黄色托盘状态、右键菜单、简洁窗口。
- Ctrl+Alt+Backspace 清空，支持快捷键冲突提示。
- 清空前重新核对格式与版本；不修改原文件，纯文本不清空。
- 单实例，无需管理员，不联网，不保存历史。
- 软件内集成可选「文件撤销设置（实验性）」，从主窗口或托盘菜单尝试调整配置、恢复原设置；不默认执行，也不保证隐藏菜单或禁用撤销。

下载：优先选择与处理器架构匹配的 self-contained 包，无需另装运行时；framework-dependent 包较小，需要 .NET Desktop Runtime 10，并且必须保留完整文件夹。

已知限制：不清理 Win+V 历史；清空剪贴板不会清除 Explorer 撤销记录。可选实验性设置使用未公开的 Windows 配置，尝试影响全部 Explorer 文件撤销/重做，写入成功不代表行为生效。刚修改配置后可自行重新登录一次检查；新操作仍能撤销则应恢复原设置，不需反复注销。彻底隐藏菜单尚未实现。不支持第三方虚拟文件格式；含文件的混合剪贴板项目清空时会清除其全部格式。独立运行单 EXE 会将原生运行时解包至临时目录。

验证记录：填写实际 Windows 版本、架构、自动测试结果和手工验收范围。未验证的平台请明确列出。
```
