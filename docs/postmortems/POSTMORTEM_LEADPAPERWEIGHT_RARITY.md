# LeadPaperweight Rarity 校准复盘

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
适用工具版本：`v2.1.1`  
状态：Accepted / 已用于修正 RollCore

## 背景

`LeadPaperweight`（铅制镇纸）会在 Neow 开局生成无色卡牌。它的卡牌池、稀有度规则和 RNG 消耗会直接影响 Neow 路线预测、批量筛种结果和最终牌组筛选。

在 `v2.1.0` 及更早 RollCore 中，铅制镇纸被建模为 `NoRare`：只允许无色 Common / Uncommon，不允许 Rare 无色卡。后续通过源码链路和低进阶验证确认，这个结论是不完整的。

## 修正结论

铅制镇纸应按游戏的无色 `RegularEncounter` card reward 路径建模：

```text
LeadPaperweight
-> ColorlessCardPool
-> RegularEncounter rarity logic
-> PlayerRng.Rewards
```

因此：

```text
LeadPaperweight can generate Rare colorless cards where the current game rarity odds permit it.
```

Rare 是否实际出现，应由当前游戏版本、进阶等级、Scarcity / rarity odds 和解锁池共同决定，而不是由 `LeadPaperweight` 来源本身固定禁止。

## 旧 RollCore 错误

旧实现路径为：

```text
OpeningPredictor.SimulateLeadPaperweight(...)
-> CreateCardForRewardNoRare(ColorlessCardPool, 2, false)
```

这会在候选 rarity 层面排除 Rare：

```text
allowed rarity = Common / Uncommon
Rare 不进入最终候选池
```

这是历史校准错误。

## 错误归因原因

当时观察到的现象主要来自高进阶 / 稀缺性环境：铅制镇纸很难或没有观察到 Rare。我们错误地把这个现象归因成：

```text
LeadPaperweight 本身禁止 Rare。
```

更准确的解释应是：

```text
高进阶 / Scarcity / rarity odds 改变了 Rare 出率；
低进阶测试不足导致我们没有覆盖 Rare 可出的情况。
```

## 暴露的问题

1. 低进阶测试覆盖不足。
2. 没有严格区分“来源限制”和“概率限制”。
3. 对 Neow relic 卡牌奖励的回归样本过度集中在常用高进阶环境。
4. 在实测现象和源码链路冲突时，未及时追加交叉验证。

## v2.1.1 修正

`v2.1.1` 对该问题做出以下修正：

- `SimulateLeadPaperweight(...)` 改用 `CreateCardForReward(ColorlessCardPool, 2, "RegularEncounter", null, false)`。
- WPF 中 LeadPaperweight 来源卡牌筛选改为无色 Common / Uncommon / Rare。
- 相关提示文案改为说明 Rare 概率由当前进阶与游戏 rarity odds 决定。
- 保留 `CreateCardForRewardNoRare(...)` 供其他特殊来源未来可能使用，但铅制镇纸不再使用该路径。

## 后续测试建议

新增 LeadPaperweight 回归样本时，应覆盖：

```text
A0 / A1：确认 Rare 可命中
A7+：确认 Scarcity 后稀有度概率正确
A10：覆盖常用筛种环境
```

同时需要确认：

```text
PlayerRng.Rewards 消耗顺序不变
RollForUpgrade 消耗仍然发生
与 LostCoffer / Kaleidoscope / ScrollBoxes 的 reward RNG 不混淆
```

## 当前状态

截至 `v2.1.1`：

```text
LeadPaperweight 的旧 NoRare 建模已修正。
低进阶下 Rare 无色卡应可以被预测和筛选。
高进阶下 Rare 是否出现由当前 rarity odds 决定。
```
