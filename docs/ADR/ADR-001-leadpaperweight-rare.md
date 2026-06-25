# ADR-001: LeadPaperweight 允许 Rare Colorless

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
适用工具版本：`v2.1.1`  
状态：Accepted

## 背景

`LeadPaperweight` / 铅制镇纸会在 Neow 开局生成无色卡。旧 RollCore 将其建模为 `NoRare`，导致只会从 Common / Uncommon 无色卡中抽取。

后续源码审计结论与低进阶实测表明，该模型不完整：LeadPaperweight 在游戏 odds 允许时可以生成 Rare colorless。

## 决策

RollCore 中 `LeadPaperweight` 不再使用：

```text
CreateCardForRewardNoRare(ColorlessCardPool, ...)
```

改为：

```text
CreateCardForReward(ColorlessCardPool, 2, "RegularEncounter", null, false)
```

即：

```text
LeadPaperweight
-> ColorlessCardPool
-> RegularEncounter rarity logic
-> PlayerRng.Rewards
```

## 理由

- 旧 NoRare 模型把“高进阶 Rare 很难出现”误判成“来源禁止 Rare”。
- 新模型能覆盖低进阶 Rare colorless 样本。
- 新模型与 `CardCreationSource.Other` / `RegularEncounter` 路径的当前审计结论一致。
- WPF 筛选也必须允许 LeadPaperweight 的无色 Common / Uncommon / Rare。

## 后果

正面：

- 低进阶 Rare colorless 种子不再漏筛。
- 过程导向和最终结果导向筛选都能命中 LeadPaperweight Rare。
- 旧错误归因被显式记录，避免回归。

风险：

- Scarcity / rarity odds 阈值仍需要继续源码级确认。
- 如果未来 STS2 改动 card reward odds，需要重新审计。

## 相关文档

- `../source_audits/LEAD_PAPERWEIGHT_RARE_2026-06-26.md`
- `../KNOWLEDGE_BASE/CARD_REWARDS_AND_SCARCITY.md`
- `../postmortems/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md`
