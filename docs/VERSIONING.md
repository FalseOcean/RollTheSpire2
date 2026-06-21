# Versioning

## Current stable version

```text
v2.1.0
```

`v2.1.0` is the first public stable baseline under the **RollTheSpire2** name. It is promoted from the verified `v2.1.0-preview2e` prediction baseline.

## Version line meanings

```text
v1.x = historical / legacy RNG line
v2.x = STS2 v0.107.1+ RNG rework line
preview = experimental or pre-release package
stable = public baseline intended for GitHub Releases
```

## Important warning

`v2.1.0-preview3_public_release_pack` should not be used as a prediction baseline. It changed the runtime data path and removed required runtime data.

For future public cleanup work, use the latest tested stable package as the base unless a newer preview has been explicitly confirmed in game.

## Safe cleanup rule

Documentation cleanup may remove old notes and archives, but should not modify:

```text
RollCore/
RollWpf/
config.json
data/
```

The active runtime data path must remain:

```text
data/legacy/sts2_runtime_legacy_v2.json
```

Changing data layout or runtime data paths requires a dedicated migration version and fixed-seed regression tests.
