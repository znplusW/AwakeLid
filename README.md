# AwakeLid

<a href="README.md"><kbd> English </kbd></a> <a href="README.zh-CN.md"><kbd> 简体中文 </kbd></a>

Keep your laptop awake with its lid closed, following PowerToys Awake sessions. Adds options to Awake's existing tray menu; no PowerToys fork or rebuild required. Independent community add-on; current UI is Chinese.

**Requires:** Windows x64, [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), and Awake managed by PowerToys. Tested with Awake **0.101.2362.0**.

## Use

1. Download the runtime ZIP from [Releases](https://github.com/znplusW/AwakeLid/releases/latest) and extract it into its own writable folder.
2. Enable Awake in PowerToys and run `AwakeLid.exe`.
3. Start an Awake session. Right-click its tray icon → **合盖保持唤醒** and select plugged in, battery, or both.

The choices are disabled while Awake is off. **跟随系统设置** clears both choices and restores settings. Session expiry or stopping Awake also restores the original lid-close actions; detected manual changes take priority.

## Stop and uninstall

- `Status.cmd` — view status.
- `Stop.cmd` — restore settings and stop the add-on.
- `Uninstall.cmd` — restore, stop and send the dedicated program folder to the Recycle Bin.
- `InstallStartup.cmd` — enable login startup; off by default.

To upgrade, run the old `Stop.cmd` first, then replace the program files, keeping `data/`. If recovery fails, keep that folder and its recovery records. Restoring a lid policy does not force immediate sleep.

[Build and release](docs/RELEASING.md) · [Validation and limitations](docs/VALIDATION.md) · [Changelog](CHANGELOG.md) · [MIT license](LICENSE)
