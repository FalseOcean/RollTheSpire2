# Project Context Current

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）

> 当前项目已经引入 `CURRENT_STATUS.md` 作为唯一开发基线。新对话、新模型或新 Project 应优先读取仓库根目录的 `CURRENT_STATUS.md`，再读取本文件和知识库。

Current stable patch: `v2.1.1`.

Previous public stable baseline: `v2.1.0`.

This stable patch is based on `v2.1.0` and fixes `LeadPaperweight` / 铅制镇纸 card rarity modeling. The old RollCore path treated LeadPaperweight as `NoRare`; `v2.1.1` changes it to use the colorless `RegularEncounter` reward path, allowing Common / Uncommon / Rare where current game rarity odds permit them.

The package still preserves the core public-release data rule:

```text
config.json:data_file = data/legacy/sts2_runtime_legacy_v2.json
```

Do not replace this runtime data file with `data/sts2_data.json` during documentation or public-release cleanup.

Key current mainline:

```text
RollCore/    prediction core
RollWpf/     WPF UI
config.json  active runtime data path
data/        required runtime/source/metadata files
docs/        public and maintenance documentation
```

Canonical maintenance documents:

```text
CURRENT_STATUS.md
docs/README.md
docs/KNOWLEDGE_MANAGEMENT.md
docs/source_audits/
docs/KNOWLEDGE_BASE/
docs/ADR/
docs/postmortems/
```

Known recent fix:

```text
LeadPaperweight can generate Rare colorless cards at low ascension / when rarity odds permit it.
Old NoRare modeling was a historical calibration error caused by insufficient low-ascension coverage.
```

See:

```text
docs/source_audits/LEAD_PAPERWEIGHT_RARE_2026-06-26.md
docs/KNOWLEDGE_BASE/CARD_REWARDS_AND_SCARCITY.md
docs/ADR/ADR-001-leadpaperweight-rare.md
docs/postmortems/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md
```
