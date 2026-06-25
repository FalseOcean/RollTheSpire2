# Boss and Ancient Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable baseline

## UpFront 初始化顺序

已确认大顺序：

```text
InitializeNewRun
-> SharedRelicGrabBag.Populate(sharedRelics, UpFront)
-> each player PopulateRelicGrabBagIfNecessary(UpFront)
-> SharedAncients shuffle
-> Act2 shared ancient count
-> Act3 shared ancient count
-> Act1.GenerateRooms(UpFront)
-> Act2.GenerateRooms(UpFront)
-> Act3.GenerateRooms(UpFront)
-> if A10: Act3 second boss
```

## Act 内 GenerateRooms 顺序

```text
event queue shuffle
-> weak encounter queue
-> regular encounter queue
-> elite encounter queue
-> boss
-> ancient
```

## Boss

已确认：

- Boss pool 来自 `GenerateAllEncounters()` / `RoomType.Boss`。
- 不是 `BossDiscoveryOrder`。
- Boss 在 Ancient 前抽。
- A10 Act3 second boss 在 Act3 Ancient 后抽。
- A10 second boss 使用 `RunRng.UpFront`。
- second boss pool 排除 first boss。

## Ancient

已确认：

- Ancient 在 Boss 后抽。
- Ancient pool = local ancient pool + shared ancient subset。
- Act1 Ancient: Neow。
- Shared ancient 分配在所有 Act GenerateRooms 前完成。
- 即使 shared.Count == 0，Act2 / Act3 `NextInt(1)` 也消耗 RNG。

## 维护注意

Boss / Ancient 属于当前稳定模块。若出现 mismatch，优先检查 Boss/Ancient 之前的 UpFront 消耗链，而不是直接改 Boss pool。
