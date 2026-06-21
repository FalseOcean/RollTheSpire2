# Changelog

## v2.1.0 Stable

Stable public baseline for **RollTheSpire2**.

- Promoted the verified `v2.1.0-preview2e` prediction baseline to public stable version `v2.1.0`.
- Kept RollCore prediction logic, WPF feature logic, `config.json`, and runtime data paths unchanged.
- Kept `data/legacy/sts2_runtime_legacy_v2.json` as the active RollCore runtime data file.
- Renamed the public project and executable to **RollTheSpire2**.
- Added portable Windows release workflow through `publish_windows_x64.bat`.
- Rewrote `README.md` and `README_EN.md` for GitHub publication.
- Updated release, data-layout, versioning, and public checklist documentation.

## v2.1.0-preview3b GitHub Launch Cleanup

- Public project name changed to **RollTheSpire2**.
- WPF assembly output changed from `RollWpf.exe` to `RollTheSpire2.exe`.
- Added `publish_windows_x64.bat` for portable Windows release builds.
- Added `run_rollthespire2.bat` as an optional release launcher.
- Added `docs/RELEASE_BUILD_GUIDE.md`.
- Kept RollCore prediction logic, WPF behavior, `config.json`, and runtime data paths unchanged from the verified v2.1.0-preview2e baseline.

## v2.1.0-preview2e Verified Baseline

The verified prediction baseline used for this stable release.

Important data rule:

- `data/legacy/sts2_runtime_legacy_v2.json` is the active runtime data file.
- `data/sts2_data.json` is not a replacement for it.
- Do not remove `data/legacy` during public cleanup.

## Known bad package

`v2.1.0-preview3_public_release_pack` should not be used as a prediction baseline because it changed the runtime data path and removed required runtime data.
