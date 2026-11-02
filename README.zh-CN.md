# AwakeLid：Awake 合盖唤醒扩展

简体中文 · [English](README.md)

在 PowerToys Awake 原托盘菜单中添加合盖唤醒选项，随唤醒会话生效，到期恢复原设置。无需 fork 或重新构建 PowerToys，属于独立社区扩展，当前界面为中文。

**使用要求：** Windows x64、[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)，以及由 PowerToys 管理的 Awake。已验证 Awake **0.101.2362.0**。

## 使用

1. 从 [Releases](https://github.com/znplusW/AwakeLid/releases/latest) 下载运行包，解压到独立、可写的目录。
2. 在 PowerToys 中启用 Awake，运行 `AwakeLid.exe`。
3. 开始唤醒会话，右键 Awake 托盘图标 → **合盖保持唤醒**，选择“接通电源时”“使用电池时”，也可同时选择。

Awake 关闭时，两项置灰。选择“跟随系统设置”会取消两项并恢复设置；计时结束或关闭 Awake 也会恢复原合盖动作，检测到的系统手动修改优先。

## 停止与卸载

- `Status.cmd`：查看状态。
- `Stop.cmd`：恢复设置并停止外挂。
- `Uninstall.cmd`：恢复、退出，将独立程序目录送入回收站。
- `InstallStartup.cmd`：设置登录自启动，默认不开启。

升级前先运行旧版 `Stop.cmd`，再覆盖程序文件，保留 `data/`。恢复失败时请保留该目录及恢复记录。还原合盖设置不保证电脑立即睡眠。

[构建与发布](docs/RELEASING.md) · [验证与限制](docs/VALIDATION.md) · [更新记录](CHANGELOG.md) · [MIT 许可](LICENSE)
