# Local release verification — 2026-11-02

This is the historical **0.1.1** public-repository packaging run, independently of earlier installed builds. See [0.1.2 verification](VALIDATION-0.1.2.md) for the later file-contention fix.

- Built the guardian and native Hook from this repository using .NET SDK 8.0.426 and Zig 0.14.1. Release C# builds reported zero warnings/errors; native sources compiled with warnings treated as errors.
- Full local test mode: **60 assertions passed**, plus recycle classification, junction alias resolution, redirect rejection, parent-target preservation and actual fixture recycling.
- Rebuilt after adding deterministic/path-mapped, symbol-free Release settings. CI mode: **59 assertions passed**; the real-policy read probe and Shell recycling were explicitly skipped, while native cross-process Hook tests and tag classification remained enabled.
- Packaged a fresh staging directory using an explicit file list. The runtime archive contained **24 file entries**. Audited entries and bytes for developer-only/user-state files, local home paths and the local-only commit email: none found.
- Audited public source files for the same personal path/identity strings: none found. Git author metadata is retained only in the local repository history; the public source archive excludes `.git`.
- No public repository was created, no remote push or hosted Actions run was performed, and no GitHub Release was published during this preparation.
- This run did not modify or restart the installed application, change real power policies, manipulate the foreground UI, or perform physical lid-close/sleep testing.

本记录对应 0.1.1 打包时的历史结果：完整模式 60 项、CI 子集 59 项通过。运行包不包含数据记录、日志、调试符号或个人路径；公开源码不包含本地提交身份。当时未执行 GitHub Actions 或真人合盖测试。用户随后确认 0.1.1 真人合盖验收完成并通过；这不代表 0.1.2 已重新进行硬件验收。
