# Changelog

## v2.1.1 Stable

Prediction correctness patch for **RollTheSpire2**.

- Fixed `LeadPaperweight` / 铅制镇纸 card reward modeling.
- `LeadPaperweight` no longer uses the RollCore `CreateCardForRewardNoRare(...)` path.
- It now uses the normal colorless `RegularEncounter` card reward rarity path, allowing Rare colorless cards where the current game rarity odds permit them.
- Updated WPF process-filter dropdowns and validation so LeadPaperweight can select colorless Common / Uncommon / Rare cards instead of only Uncommon / NoRare candidates.
- Added `docs/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md` to record the historical misattribution and the testing gap around low ascension.
- Kept `config.json:data_file` unchanged: `data/legacy/sts2_runtime_legacy_v2.json`.
- Documentation system refresh:
  - Added `CURRENT_STATUS.md` as the current source of truth.
  - Added `docs/README.md` and `docs/KNOWLEDGE_MANAGEMENT.md`.
  - Added `docs/source_audits/`, `docs/KNOWLEDGE_BASE/`, `docs/ADR/`, and `docs/postmortems/`.
  - Added `PROJECT_SOURCE_CURRENT.md` for ChatGPT Project / new-session context migration.
  - Refreshed root `README.md` and `README_EN.md` so they describe `v2.1.1` as the current public stable patch release.

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
