# Source Audits Index

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）

本目录保存源码审计材料。它的职责是保存“证据”，不是写最终知识结论。

## 文件命名建议

```text
TOPIC_YYYY-MM-DD.md
```

示例：

```text
LEAD_PAPERWEIGHT_RARE_2026-06-26.md
REGULAR_ENCOUNTER_SCARCITY_2026-06-26.md
NEOW_EVENT_RNG_2026-06-26.md
```

## 当前索引

| Topic | File | Status | Promoted To | ADR |
| --- | --- | --- | --- | --- |
| LeadPaperweight Rare colorless | `LEAD_PAPERWEIGHT_RARE_2026-06-26.md` | Audit summary, original Codex transcript not packaged | `../KNOWLEDGE_BASE/CARD_REWARDS_AND_SCARCITY.md` | `../ADR/ADR-001-leadpaperweight-rare.md` |

## 维护规则

- 优先保存 Codex / 人工源码审计原文。
- 如果只有整理后的结论，必须标记为 audit summary。
- Source Audit 可以包含不稳定线索；稳定事实应另行提升到 `KNOWLEDGE_BASE/`。
- 如果审计结论推翻旧模型，必须写 ADR 或 postmortem。
