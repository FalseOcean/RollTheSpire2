# Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）

Knowledge Base 保存长期稳定事实。它不记录“今天改了什么”，也不保存完整源码审计原文。

## 当前主题索引

| 文件 | 主题 | 状态 |
| --- | --- | --- |
| `RNG.md` | v0.107.1+ RNG 基础、seed、stream、消耗规则 | Stable baseline |
| `NEOW.md` | Neow event RNG、三选、Neow relic effects 总规则 | Stable / evolving |
| `BONES.md` | NeowsBones / BoneDice relic 和 curse 顺序 | Stable / route edge cases evolving |
| `CARD_REWARDS_AND_SCARCITY.md` | Card reward、Scarcity、LeadPaperweight Rare 修正 | Active calibration |
| `CAPSULE.md` | LargeCapsule / SmallCapsule 普通 relic 池 | Stable with pool validation needed |
| `EVENT_QUEUE.md` | raw queue / effective queue / Ancient offset | Stable baseline |
| `BOSS_ANCIENT.md` | Boss / Ancient / UpFront 顺序 | Stable baseline |
| `DATA_LAYOUT.md` | runtime data、source sidecar、profiles、publish 边界 | Critical stable rule |
| `CANDIDATE_POOLS.md` | 粗筛候选池工作流和数据边界 | Stable workflow |

## 使用规则

- 新预测逻辑应先查本目录。
- 如果只是在 `source_audits/` 里出现但尚未提升到 Knowledge Base，应视为未稳定结论。
- 如果 Knowledge Base 与 CURRENT_STATUS 冲突，以 CURRENT_STATUS 为准，并尽快同步修正。
