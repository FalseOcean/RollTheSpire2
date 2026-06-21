# Public Release Checklist

Before publishing to GitHub:

- Build with `build_windows_wpf.bat`.
- Verify `config.json:data_file` is `data/legacy/sts2_runtime_legacy_v2.json`.
- Ensure `data/legacy/sts2_runtime_legacy_v2.json` exists.
- Run at least one known seed regression test.
- Confirm no personal local paths are present.
- Confirm `profiles/database/*.json` is not committed.
- Confirm docs do not describe obsolete preview behavior as current truth.
- Confirm README version and WPF title both match the intended stable version.

## Repository vs Release

Commit to the repository:

```text
RollCore/
RollWpf/
data/
docs/
profiles/.keep
config.json
README.md
README_EN.md
CHANGELOG.md
build_windows_wpf.bat
run_wpf.bat
publish_windows_x64.bat
run_rollthespire2.bat
.gitignore
```

Do not commit:

```text
publish/
bin/
obj/
profiles/unlock_profile.json
profiles/database/*.json
profiles/database/*.tsv
```

## Launch packaging

For portable Windows releases, use:

```bat
publish_windows_x64.bat
```

See `docs/RELEASE_BUILD_GUIDE.md` for the full release workflow.
