# Source Audit Summary — LeadPaperweight Rare Colorless

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
适用工具版本：`v2.1.1`  
状态：迁移期审计摘要；原始 Codex 审计全文未随当前压缩包保存  
已提升到：`../KNOWLEDGE_BASE/CARD_REWARDS_AND_SCARCITY.md`  
关联 ADR：`../ADR/ADR-001-leadpaperweight-rare.md`  
关联复盘：`../postmortems/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md`

## 1. 审计问题

旧 RollCore 将 `LeadPaperweight` / 铅制镇纸建模为 `NoRare`：

```text
LeadPaperweight -> ColorlessCardPool -> Common / Uncommon only
```

需要确认：

```text
LeadPaperweight 是否真的禁止 Rare colorless？
还是只是在高进阶 / Scarcity 下 Rare 概率很低？
```

## 2. 当前整理结论

`LeadPaperweight` 不应被视为来源级 NoRare。当前模型应为：

```text
LeadPaperweight
-> ColorlessCardPool
-> CardCreationSource.Other / RegularEncounter rarity logic
-> PlayerRng.Rewards
```

因此：

```text
LeadPaperweight can generate Rare colorless cards where current game rarity odds permit it.
```

Rare 是否实际出现取决于：

- STS2 游戏版本。
- 进阶等级。
- Scarcity / card rarity odds。
- 当前 unlock profile。
- RollCore 是否正确复刻 reward RNG 消耗顺序。

## 3. 当前包内代码证据

`RollCore/OpeningPredictor.cs` 中当前 `v2.1.1` 已改为：

```csharp
private void SimulateLeadPaperweight(SimState st)
{
    var cards = st.CreateCardForReward(st.ColorlessCardPoolName, 2, "RegularEncounter", null, false);
    st.RecordCardChoiceGroup("LeadPaperweight", cards, st.ColorlessCardPoolName, "colorless", "card_reward");
    if (_plan.LeadPaperweightChoice >= 0 && _plan.LeadPaperweightChoice < cards.Count)
        st.AddCardsToDeck(new[] { cards[_plan.LeadPaperweightChoice] }, st.ColorlessCardPoolName, "LeadPaperweight");
}
```

`CreateCardForReward(...)` 会在 Common / Uncommon / Rare 中按 `RollRegularCardRarity()` 选择可用 rarity，然后从对应 rarity 的池子抽牌；旧 `CreateCardForRewardNoRare(...)` 仍存在，但它只允许 Common / Uncommon，当前不再用于 `LeadPaperweight`。

## 4. 旧错误模型

旧模型相当于：

```text
OpeningPredictor.SimulateLeadPaperweight(...)
-> CreateCardForRewardNoRare(ColorlessCardPool, 2, false)
-> allowed rarity = Common / Uncommon
-> Rare 永远不会进入候选池
```

该模型会导致：

- 低进阶 Rare colorless 种子被漏筛。
- WPF 下拉候选缺失 Rare colorless。
- 最终结果导向筛选无法命中 LeadPaperweight 产生的 Rare colorless。

## 5. 错误归因

旧结论很可能来自高进阶样本。高进阶 / Scarcity 环境中 Rare 出率显著降低，容易被误判成：

```text
LeadPaperweight 本身禁止 Rare
```

当前修正后的理解是：

```text
来源不禁止 Rare；概率系统可能让 Rare 很难出现。
```

## 6. 后续需要补强的原始证据

当前压缩包未包含完整 Codex 审计原文。后续如果重新审计，建议让 Codex 查：

```text
LeadPaperweight.AfterObtained / reward creation path
ColorlessCardPool
CardCreationSource.Other
CardRarityOddsType.RegularEncounter
CardRarityOdds.RollWithBaseOdds
Scarcity / ascension modifier
Card reward upgrade roll consumption
```

并把原文补充到本文件或新增更详细审计文件。

## 7. 验证建议

- A0 / A1：找一个 LeadPaperweight 命中 Rare colorless 的样本，确认工具可预测。
- A7+ / A10：确认 Scarcity 下 Rare odds 是否符合源码阈值。
- 与 LostCoffer / Kaleidoscope：确认 `PlayerRng.Rewards` 消耗顺序未混淆。
- WPF：确认下拉、校验、命中解释都允许 Rare colorless。
