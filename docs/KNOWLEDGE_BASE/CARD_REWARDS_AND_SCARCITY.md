# Card Rewards and Scarcity Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Active calibration

## 核心原则

卡牌奖励必须区分：

```text
来源限制
概率限制
可用池限制
升级 roll 消耗
unlock profile 影响
```

不能因为某进阶下没有观察到 Rare，就推断来源本身禁止 Rare。

## RegularEncounter

当前 RollCore `v2.1.1` 中，普通 card reward 路径通过 `CreateCardForReward(..., "RegularEncounter", ...)` 建模。

当前实现要点：

- 先用 `PlayerRng.Rewards.NextFloat()` roll rarity。
- 在 Common / Uncommon / Rare 中选择可用 rarity。
- 从对应 rarity 候选中用 `PlayerRng.Rewards.NextItem(...)` 抽卡。
- 若不是 NoUpgradeRoll，仍消耗 upgrade roll。

当前代码中 `RollRegularCardRarity()` 使用：

```text
A0-A6: Rare 约 3%
A7-A10 Scarcity: Rare 约 1.49%
Uncommon threshold: 0.37
otherwise Common
```

注意：这些阈值仍需要和源码 / 实测继续交叉验证，尤其是项目早期记忆中曾有 A0-A6 Rare 5% 的说法。若源码审计证明阈值不同，必须更新本文件、ADR 和 RollCore。

## LeadPaperweight

当前稳定结论：

```text
LeadPaperweight
-> ColorlessCardPool
-> RegularEncounter rarity logic
-> PlayerRng.Rewards
```

`LeadPaperweight` 不再使用 `CreateCardForRewardNoRare(...)`。

因此：

```text
LeadPaperweight can generate Rare colorless cards where game odds permit it.
```

旧 NoRare 模型是历史校准错误，见：

- `../source_audits/LEAD_PAPERWEIGHT_RARE_2026-06-26.md`
- `../ADR/ADR-001-leadpaperweight-rare.md`
- `../postmortems/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md`

## NoUpgradeRoll

已知：

- `ArcaneScroll`：NoUpgradeRoll。
- `HeftyTablet`：NoUpgradeRoll。
- `LeadPaperweight` / `LostCoffer` / `Kaleidoscope` 等在 Neow 阶段名义升级概率为 0%，但路径可能仍消耗 upgrade roll。

## 后续 P0/P1

- 补完整 Scarcity / rarity odds 源码审计。
- 固定 A0 / A1 / A7+ / A10 样本。
- 确认 WPF 命中解释展示的 rare odds 与 RollCore 阈值一致。
