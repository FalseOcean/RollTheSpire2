# Release Build Guide

This project is published under the public name **RollTheSpire2**.

Current package version:

```text
v2.1.1
```

## Source/developer workflow

From the repository root:

```bat
build_windows_wpf.bat
run_wpf.bat
```

`build_windows_wpf.bat` builds the WPF application in Release mode.
`run_wpf.bat` starts the built executable:

```text
RollWpf/bin/Release/net9.0-windows/RollTheSpire2.exe
```

## Portable Windows release workflow

From the repository root:

```bat
publish_windows_x64.bat
```

The script creates:

```text
publish/RollTheSpire2_v2.1.1_win-x64/
```

That folder is intended to be zipped and attached to a GitHub Release.

End users should launch:

```text
RollTheSpire2.exe
```

`run_rollthespire2.bat` is included as a fallback launcher for troubleshooting startup errors. It is not required for normal use.

## Why the publish folder is large

The release script currently uses a self-contained `win-x64` publish. This bundles the .NET runtime with the app so end users can run it without installing .NET separately. The tradeoff is a larger zip size.

## Important data rule

Do not change the runtime data path during release packaging:

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

Despite the folder name, `data/legacy/sts2_runtime_legacy_v2.json` is the current runtime data file used by RollCore. `data/sts2_data.json` is a source-extraction sidecar and must not replace it unless a dedicated data-layout migration is implemented and regression-tested.

## Release checklist

Before uploading a release:

1. Build with `build_windows_wpf.bat`.
2. Run at least one known regression seed and compare against the `v2.1.0` stable baseline, plus specific LeadPaperweight low-ascension Rare checks for this patch release.
3. Run `publish_windows_x64.bat`.
4. Launch `publish/.../RollTheSpire2.exe` from the publish folder.
5. Confirm `data/legacy/sts2_runtime_legacy_v2.json` exists in the publish folder.
6. Confirm `config.json:data_file` still points to `data/legacy/sts2_runtime_legacy_v2.json`.
7. Confirm user files are not included:
   - `profiles/unlock_profile.json`
   - `profiles/database/search_history.json`
   - `profiles/database/candidate_pools.json`
8. Zip the publish folder and upload the zip to GitHub Releases.
