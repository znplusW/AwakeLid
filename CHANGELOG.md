# Changelog / 更新记录

## 0.1.2 — 2026-11-02

- Isolate optional status/log I/O from policy faults. A busy diagnostic file no longer latches the guardian into a paused state or interrupts restoration.
- Publish only changed status snapshots through a same-directory staging file and replacement. Retry diagnostic I/O after five seconds, buffer up to 128 log entries, and show separate diagnostic warnings.
- Keep `state.json` persistence strict: an unavailable recovery journal still blocks new policy writes.
- Add regression coverage with independent file-locking processes, shared readers, access failures, bounded/concurrent logging, manual edits and session expiry.
- 修复状态或日志文件被占用后误暂停联动的问题；诊断写入失败不再阻断恢复。
- 状态变更才写盘，先写临时文件再替换；失败五秒后重试，日志最多缓冲 128 条，诊断问题单独显示。
- 保留恢复记录的严格写入要求，增加独立进程占用文件、并发日志、到期恢复及手动设置优先的回归测试。
- Record the user's report that physical lid-close acceptance passed on 0.1.1; do not present it as a new hardware test of 0.1.2.

## 0.1.1 — 2026-11-02

- Disable and reject AC/battery follow choices while Awake is off. Keep the system-settings choice available.
- Recognize OneDrive CLOUD reparse tags, distinguish actual junctions/symlinks, and compare canonical paths during uninstall.
- Validate uninstall using a running cloud-directory fixture through a parent junction.
- Awake 关闭时置灰并拦截两项跟随操作，系统选项继续可用。
- 修复 OneDrive 云目录误判及路径别名识别，验证父联接下运行中卸载。
- Prepare bilingual public documentation, repeatable build/packaging scripts, licenses and a build-only CI workflow.

## 0.1.0 — 2026-11-02

Initial local prototype: native Awake tray-menu extension, AC/DC session leases, manual-change preservation, durable recovery, optional login startup and recycle-only uninstall.

首个本地原型：托盘扩展、插电/电池会话联动、手动设置优先、持久恢复、可选自启动与回收站卸载。
