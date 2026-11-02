# AwakeLid 0.1.2 — local verification / 本地验证

Date: 2026-11-02. Platform: Windows x64, .NET SDK 8.0.426.

## Confirmed defect and fix

In 0.1.1, `File.WriteAllText(status.json, ...)` ran in the same exception boundary as policy control. A sharing violation incorrectly triggered restoration and latched the guardian into its faulted state. The supplied screenshot and installed event log agree: the battery policy was restored, and no pending restore remained. The identity of the process holding the file was not established.

In 0.1.2, diagnostic writes run outside that fault boundary. Identical status snapshots are skipped; changed snapshots use a same-directory staging file and `File.Replace` for an existing target. Expected file-access errors become separate status warnings and retry after five seconds. Failed log writes queue up to 128 entries, safely across callers; dropped entries are counted. Buffered diagnostics are not durable across process exit. `state.json` continues to use the strict durable journal, and a journal failure still prevents a new policy override.

已确认旧版将状态文件占用误判为电源控制故障，触发恢复并持续暂停联动。现有日志显示异常发生后已恢复电池合盖策略，待恢复记录为零；未确认具体占用进程。新版隔离诊断写入、减少重复写盘、使用完整快照替换及自动重试；恢复日志的严格要求保持不变。

## Verification completed

- C# application and tests built in Release: **0 warnings, 0 errors**. Application version is **0.1.2**; UI/status use the assembly version.
- **88 assertions passed**: 29 new diagnostic/policy checks plus the existing 59 CI-safe core/native checks. No real Windows policy probe or write was performed in this run.
- An independent child process held exclusive locks on `status.json` and `events.log`. The old direct-write failure reproduced. The fixed code kept simulated policies active, respected a manual AC edit, restored DC at expiry and flushed the latest status/queued events after unlock.
- Shared-reader replacement preserved complete old/new JSON snapshots. Staging-file locks, access errors, bounded buffering and concurrent log callers were covered. Locking the mandatory recovery journal still blocked policy writes.
- Hidden native tests covered menu enable/disable, original commands, Hook detach/unload, crash-triggered recovery and incompatible-window rejection. These tests did not operate the user's tray or installed helper.
- Recycle-tag classification passed. Actual Shell recycling and cloud-directory uninstall were not repeated; the unchanged uninstall code retains its documented 0.1.1 evidence.
- Native Hook source is unchanged. A stalled native rebuild was canceled; the final package reuses the previously verified DLL, checked byte-for-byte against the 0.1.1 runtime ZIP. SHA-256: `a38c8199570ed1c11b4158af53203804c567d0fe1bd5248ce5a2f10298e17c80`.

本轮 88 项后台断言通过，包含 29 项新增文件占用/电源模拟检查和原有 59 项核心/隐藏 Hook 检查。未写真实电源策略、未切换前台窗口、未重启现有安装。未修改的卸载逻辑沿用此前实测证据，本轮重新检查回收标签分类。

## Hardware evidence and publication

The user explicitly reported physical lid-close acceptance completed and passed on **0.1.1**. This report is preserved separately; it does not claim physical testing of **0.1.2**, every AC/DC sleep/resume combination, or additional laptop models.

This record describes local verification before publication. Hosted Actions results are available separately on GitHub. The user's existing installation was not replaced by this verification.

用户已确认 0.1.1 真人合盖验收通过；0.1.2 本轮为发布前自动回归验证，未覆盖现有安装。GitHub Actions 结果在仓库中另行查看。
