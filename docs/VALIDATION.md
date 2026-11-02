# Validation

The current local candidate is **0.1.2**, with optional diagnostic writes isolated from power-control faults. See [0.1.2 verification](VALIDATION-0.1.2.md) for file-contention coverage, build results and scope.

Historical local evidence: 60 core/native assertions passed, including disabled choices, direct-command rejection, original menu preservation and clean DLL unloading. A running OneDrive CLOUD_E package fixture was uninstalled through a parent junction; the fixture was recycled and the original installation, junction and active power plan were preserved. Production power writes were checked by writing existing values back without changing the configuration.

The build script reruns source tests against built binaries. `-Ci` excludes the hardware-specific, read-only policy probe and actual Shell recycling; file-lock, simulated-policy, hidden-window Hook and recycle-tag checks remain enabled. Hosted GitHub Actions has not yet been executed. The [earlier packaging record](LOCAL-RELEASE-VERIFICATION.md) describes 0.1.1 only.

On 2026-11-02 the user explicitly reported that physical lid-close acceptance had completed and passed on 0.1.1. This is user-reported hardware evidence, not an automated test or a repeat acceptance of 0.1.2. It does not establish an exhaustive unplug, sleep/resume or hardware compatibility matrix. Notification visibility, future PowerToys compatibility and recovery after simultaneous process termination remain subject to the README limitations. See [the Chinese verification record](VALIDATION.zh-CN.md) for chronological evidence.
