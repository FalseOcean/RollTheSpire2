# Event Queue Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable baseline

## Raw queue

Raw event queue：

```text
act.AllEvents + ModelDb.AllSharedEvents
```

生成 raw queue 时不按 `IsAllowed` 过滤，epoch 过滤后使用 `RunRng.UpFront` shuffle。

## Effective queue

实际玩家遇到的是 effective event queue，不是 raw queue。

已确认处理：

- 每幕起点 Ancient 进入 EventRoom，导致本 Act `eventsVisited + 1`。
- 因此普通事件默认从 raw[1] 开始。
- `IsAllowed=false` 的事件跳过。
- `VisitedEventIds` 是 run-global，用于 shared event duplicate avoidance。
- `Hook.ModifyNextEvent` 可替换事件。
- Hook 替换后不再次 IsAllowed。

## PullNextEvent 顺序

```text
EnsureNextEventIsValid
-> Hook.ModifyNextEvent
-> AddVisitedEvent(final)
-> MarkRoomVisited(Event)
```

## 维护注意

批量筛种应基于 effective queue。Raw queue 适合调试展示，不应直接作为玩家实际问号房顺序。
