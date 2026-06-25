# RNG Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable baseline

## 已确认事实

- STS2 v0.107.1+ 使用 MegaRandom / xoshiro256**。
- xoshiro state 使用 SplitMix64 初始化。
- 游戏可见 seed 仍为 10 位。
- seed charset 排除 `I` / `O`。
- seed normalization：`I -> 1`，`O -> 0`。
- seed string 使用 deterministic hash。
- RunRng stream seed = seed hash + stream hash。
- PlayerRngSet 使用 player slot index，不使用 NetId。
- 单人 player slot index = 0。
- EventModel.Rng 使用 run seed + player slot offset + event id hash。
- `NextInt(1)` 消耗 RNG。
- `NextItem(count == 1)` 消耗 RNG。
- `NextDouble()` 使用高 53 bits。
- Shuffle 是 tail-to-head Fisher-Yates。
- GrabBag predicate 不是 filter 后抽，而是重复从整个 bag 抽直到满足 predicate，可能消耗多次 RNG。

## 常见 stream

```text
RunRng.UpFront
RunRng.Niche
PlayerRng.Rewards
PlayerRng.Transformations
EventModel.Rng
```

## 维护注意

不要用旧 RNG Rework 前经验覆盖 v0.107.1+ 结论。若 STS2 更新 RNG 或 seed/hash 逻辑，必须重新审计本文件。
