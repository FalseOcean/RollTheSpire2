# Versioning

## Current package

```text
v2.1.1
```

`v2.1.1` is a stable maintenance patch based on the public `v2.1.0` stable package. It intentionally changes RollCore prediction behavior for `LeadPaperweight` / 铅制镇纸 so that the relic follows the colorless `RegularEncounter` reward rarity path and can include Rare colorless cards where game rarity odds permit them.

Previous public stable baseline before this fix:

```text
v2.1.0
```

## Version line meanings

```text
v1.x = historical / legacy RNG line
v2.x = STS2 v0.107.1+ RNG rework line
preview = experimental or pre-release package
stable = public baseline intended for GitHub Releases
```

## Important warning

`v2.1.0-preview3_public_release_pack` should not be used as a prediction baseline. It changed the runtime data path and removed required runtime data.

## Safe cleanup rule

Documentation cleanup may remove old notes and archives, but should not modify prediction code, `config.json`, or runtime data paths unless the version is explicitly a logic or data migration release.

The active runtime data path must remain:

```text
data/legacy/sts2_runtime_legacy_v2.json
```

Changing data layout or runtime data paths requires a dedicated migration version and fixed-seed regression tests.
