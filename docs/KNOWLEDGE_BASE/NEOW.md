# Neow Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable / evolving

## Event RNG

Neow 是 `AncientEventModel`：

- `IsShared = false`。
- event id = `NEOW`。
- Neow 三选使用 `EventModel.Rng`。
- Neow 三选不是 `RunRng.Niche`。
- Neow 三选不是 `RunRng.UpFront`。

EventModel.Rng 规则：

```text
uint((int)hash(seed) + (isShared ? 0 : player_slot_index)) + uint(hash(event_id))
```

单人模式 player slot index = 0。

## Neow relic effects

Neow relic effects 应按各自源码路径建模，不要合并成统一“给卡 / 给遗物”规则。

常见路径：

- `LeadPaperweight`：ColorlessCardPool + RegularEncounter card reward + PlayerRng.Rewards。
- `LostCoffer`：卡牌 / 药水 reward，主要使用 PlayerRng.Rewards。
- `Kaleidoscope`：角色池 shuffle 用 RunRng.Niche，卡牌 reward 用 PlayerRng.Rewards。
- `ScrollBoxes`：使用 PlayerRng.Rewards。
- `NewLeaf`：使用 RunRng.Niche。
- `LeafyPoultice`：使用 PlayerRng.Transformations。
- `LargeCapsule` / `SmallCapsule`：普通 relic reward，不应包含 shop / ancient / Neow relic。

## 维护注意

- 不要把某个 Neow relic 的 reward 模型套到其他 relic。
- Neow 阶段 `RunState.CurrentActIndex = 0`。
- 部分 card reward 虽然升级概率为 0%，仍可能消耗 upgrade roll。
- `ArcaneScroll` / `HeftyTablet` 属于 NoUpgradeRoll 路径，不消耗 upgrade RNG。
