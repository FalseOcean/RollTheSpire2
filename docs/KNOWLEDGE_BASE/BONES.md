# NeowsBones / BoneDice Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable / route edge cases evolving

## 已确认事实

- `NeowsBones` / BoneDice 会产生两个 Neow relic。
- BoneDice relic shuffle 使用 `PlayerRng.Rewards`。
- BoneDice curse 使用 `RunRng.Niche`。
- Bones curse 在两个 BoneDice relic 之后加入。
- NewLeaf 同轮不能 transform BoneDice curse。
- 两个 BoneDice relic 的 pickup order 可能影响结果。
- 必要时应模拟 A->B 和 B->A。
- 命中解释应标注实际满足条件的路线。

## 筛选语义

过程导向：

```text
Neow 三选有 NeowsBones
BoneDice 给 NewLeaf + LeafyPoultice
Bones curse 不要 Debt
```

最终结果导向：

```text
最终牌组 / relic / curse / potion 满足条件，不关心具体来源
```

两者不能混淆。

## 维护注意

若 Bones route 与 NewLeaf / LeafyPoultice / Capsule / LeadPaperweight 发生组合影响，优先检查 route order 和 RNG stream，不要只看最终列表。
