# AwakeLid

[简体中文](README.zh-CN.md) · English

**Temporarily keep a laptop running with its lid closed, following PowerToys Awake sessions.**

AwakeLid adds a submenu to the existing Awake tray icon. It temporarily changes selected Windows lid-close actions to **Do nothing** while Awake is active, then restores the previous values when the session ends. PowerToys does not need to be modified or rebuilt, and Windhawk is not required.

Independent community add-on; not affiliated with or endorsed by Microsoft. The current application and menu labels are **Chinese**. This README provides English guidance, not an English UI.

## Requirements and status

- Windows x64 and [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- Awake launched and managed by PowerToys. Standalone Awake CLI instances are unsupported.
- Validated against **PowerToys Awake 0.101.2362.0**. Future versions may need adaptation.
- Current build: **0.1.2**. Automated checks cover file contention, policy restoration and hidden menus. The user reported physical lid-close acceptance passed on **0.1.1** on 2026-11-02; that is separate from this build's automated verification.

## Quick start

1. Extract `AwakeLid-v0.1.2-win-x64.zip` into a writable, dedicated folder. Keep the complete folder together.
2. Enable Awake in PowerToys and run `AwakeLid.exe`. Start with the same privilege level as PowerToys.
3. Right-click Awake's existing coffee-cup tray icon and open **合盖保持唤醒** (keep awake with lid closed).
4. Start an Awake session. Select **接通电源时** (plugged in), **使用电池时** (on battery), or both under **跟随 Awake** (follow Awake).

```text
合盖保持唤醒 >                  Keep awake with lid closed
    跟随 Awake                 Follow Awake (heading)
        □ 接通电源时           Plugged in
        □ 使用电池时           On battery
    ------------------
    ✓ 跟随系统设置             Follow system settings
```

Both follow-Awake choices are disabled when Awake is off. Existing preferences remain stored. **跟随系统设置** stays available: it clears both choices and restores settings still owned by the add-on. The default is to follow system settings; starting the helper alone changes no power policy.

## Behavior and recovery

- Lid-close policy is tracked separately for AC, battery, and each power plan. Timer expiry, manual Awake stop, or Awake process exit triggers restoration.
- Extending a session retains its original baseline. Switching plans restores the old plan before applying a new lease.
- Detected manual policy changes take priority and suspend that supply's override for the current session. Detection samples about every 0.5 seconds; changes away and back between samples cannot be distinguished.
- Original values are journaled before writing. An orphaned Hook can start a one-shot recovery helper after the guardian exits unexpectedly. On the next launch, outstanding journal entries are checked again.
- Status and log files are optional diagnostics. A locked `status.json` or log file shows a separate diagnostic warning and retries after five seconds; it does not suspend lid control or block restoration. Unchanged snapshots are not rewritten. Log buffering is limited to 128 entries, with dropped entries reported. The recovery journal `state.json` remains mandatory before changing policy.
- Only the lid-close action is written. Awake itself supplies the keep-awake request. Sleep timeouts, hibernation switches and wake timers are not changed; no forced sleep is issued.
- Restoration changes policy; it does not guarantee immediate sleep when the lid is already closed. Windows decides subsequent power transitions.

If both processes terminate, the machine loses power, or the target message thread hangs, immediate recovery is not guaranteed. Keep `data/state.json` until restoration succeeds. Errors appear in status/logs; Windows notification settings can suppress balloons. Unknown Awake versions require menu verification before applying overrides. These checks cannot guarantee detection of every possible future incompatibility.

## Startup, stop and uninstall

| Entry | Purpose |
| --- | --- |
| `AwakeLid.exe` | Start the background helper; no permanent second tray icon |
| `Status.cmd` | Open status |
| `InstallStartup.cmd` | Explicitly opt into current-user login startup via `AwakeLid.lnk` |
| `Stop.cmd` | Restore owned settings, detach the Hook, stop the helper |
| `Uninstall.cmd` | Restore, stop, verify DLL release, recycle owned startup link and package folder |

Startup is **not enabled by default**. Uninstall supports OneDrive cloud placeholders and installations accessed through a parent directory junction. It refuses to recycle a package root that is itself a junction/symlink or an unknown reparse type. A recycle-only Shell operation vetoes permanent deletion. If recovery, verification or recycling fails, uninstall stops and preserves remaining files. PowerToys is never forcibly terminated. The whole dedicated package folder is recycled, including files you add there.

To upgrade in place, first run the old folder's `Stop.cmd` and wait for successful restoration and exit. Copy the contents of the new runtime folder over the same installation folder, keeping `data/`, then start `AwakeLid.exe`. Keeping the folder preserves an existing startup shortcut's target. Stop clears the follow selections; select them again when Awake is active. Do not overwrite running binaries or discard an unresolved recovery journal.

## Build and test

From a source checkout (not the runtime ZIP), install .NET SDK **8.0.426** on Windows x64, then use PowerShell 7:

```powershell
.\build.ps1
.\pack.ps1
```

The build downloads Zig **0.14.1** from ziglang.org into `artifacts/toolchain`, verifies its pinned SHA-256, and builds this add-on only. Alternatively pass `-ZigPath C:\path\to\zig.exe` to use an existing toolchain. Runtime packages are framework-dependent and do not bundle .NET.

Local tests use hidden mock windows and simulated power values. The optional actual-policy probe only reads. Recycle tests create their own fixture and send a child folder to the Recycle Bin; they do not change real power settings. On CI use `-Ci`: skip the hardware-specific read probe and actual Shell recycling, but retain native message/Hook tests and recycle-tag classification. CI behavior is not a physical-laptop test.

`pack.ps1` produces `dist/AwakeLid-v0.1.2-win-x64.zip` and SHA-256 files. It stages an explicit file list, excluding runtime data, logs, personal configuration, test executables and compiler caches. Source is distributed separately. See [release instructions](docs/RELEASING.md), [validation notes](docs/VALIDATION.md), [Chinese validation record](docs/VALIDATION.zh-CN.md), and [third-party notices](THIRD_PARTY_NOTICES.md).

## Source layout

`src/AwakeLid/`: guardian, journal and Windows power API. `src/NativeHook/`: thread-scoped native Hook and window subclass. `tests/`: core, hidden cross-process and recycling tests. Root scripts: startup, stopping and recycle-only uninstall.

The Hook targets only the `Awake.MessageWindow` thread. It does not patch PowerToys files, install a service/driver, or monitor keyboard/mouse input. MIT licensed; native runtime notices are included separately.
