# Capsule Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable with pool validation needed

## LargeCapsule

已确认：

- 生成 2 个普通 random relic。
- 固定加入 1 Strike + 1 Defend。

## SmallCapsule

已确认：

- 生成 1 个普通 random relic。

## Capsule relic pool

Capsule random relic reward 只应使用普通 relic rarity：

```text
Common
Uncommon
Rare
```

不应包含：

```text
Shop relic
Ancient / Neow relic
MassiveScroll
multiplayer-only relic
```

## 筛选语义

如果想筛：

```text
骨骰给 LargeCapsule + SmallCapsule
```

应使用 Neow relic 条件。

如果想筛：

```text
LargeCapsule 生成 GamblingChip
```

应使用最终普通 relic 条件。
