# Architecture Decision Records

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）

ADR 保存“为什么这么设计”。它不是源码审计，也不是版本日志。

## 当前 ADR

| ADR | Topic | Status |
| --- | --- | --- |
| `ADR-001-leadpaperweight-rare.md` | LeadPaperweight 允许 Rare colorless | Accepted |
| `ADR-002-runtime-data-layout.md` | 保留 `data/legacy/sts2_runtime_legacy_v2.json` 作为主运行数据 | Accepted |

## 什么时候写 ADR

- 改变 RNG / 预测模型。
- 推翻旧 RollCore 经验。
- 改变 runtime data layout。
- 引入长期架构边界。
- 多种方案都可行，需要记录取舍。
