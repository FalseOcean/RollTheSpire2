# Project Context Current

Current stable baseline: `v2.1.0`.

This stable package is promoted from the verified `v2.1.0-preview2e` prediction baseline. Public cleanup and version promotion preserve RollCore prediction code, WPF feature logic, `config.json`, and runtime data paths.

## Correct runtime baseline

- STS2 target: v0.107.1+ RNG rework.
- RNG: game-compatible xoshiro256** / MegaRandom behavior in RollCore.
- Visible seed length: default 10 characters.
- Main runtime data file: `data/legacy/sts2_runtime_legacy_v2.json`.
- Do not replace the runtime data with `data/sts2_data.json`.

## Verified prediction chains

- Neow choices
- Neow's Bones route
- NewLeaf / LeafyPoultice / Kaleidoscope / ScrollBoxes
- Relic predictions
- Act selection
- Boss and Ancient
- Effective event queue

## Main workflows

- Single seed analysis
- Batch seed search
- Favorites database
- Candidate pool / coarse filter workflow
- progress.save unlock profile import

## Character display names

- Ironclad = 铁甲战士
- Defect = 故障机器人
- Silent = 静默猎手
- Regent = 储君
- Necrobinder = 亡灵契约师
