# RollTheSpire2 External Tool — Project Source Current

生成日期：2026-06-26  
用途：ChatGPT Project / 新对话迁移知识快照  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
当前公开稳定版：`v2.1.1`  
当前开发线：暂无；下一轮功能迭代待定  
项目线：External WPF Tool，不是 Workshop Mod

## 1. 必读优先级

新对话恢复项目时，按以下顺序读取：

```text
CURRENT_STATUS.md
README.md / README_EN.md
CHANGELOG.md
docs/README.md
docs/KNOWLEDGE_MANAGEMENT.md
docs/KNOWLEDGE_BASE/
docs/ADR/
docs/source_audits/
```

## 2. 项目定位

`RollTheSpire2` 是面向 `Slay the Spire 2` 的外部 WPF 种子分析与批量筛种工具。它不启动游戏、不注入游戏进程、不修改存档。它通过本地数据、源码审计结论、用户实测样本和 RollCore RNG 模型离线预测 seed。

核心结构：

```text
RollWpf/                         WPF UI
RollCore/                        预测核心
data/legacy/sts2_runtime_legacy_v2.json  当前主运行数据
data/sts2_data.json              源码抽取辅助数据
profiles/                        用户本地数据
publish_windows_x64.bat          Windows x64 发布脚本
```

## 3. 当前状态摘要

- 最新公开稳定版：`v2.1.1`。
- 上一公开稳定版：`v2.1.0`。
- 本版本核心：修正 LeadPaperweight Rare colorless 预测。
- 本轮维护重点：文档体系大修，建立 CURRENT_STATUS / source_audits / KNOWLEDGE_BASE / ADR / postmortems。
- 下一轮功能迭代待定。

## 4. 当前硬规则

1. 不要混淆 External Tool 和 Workshop Mod。
2. 不要修改 `config.json:data_file`。
3. 不要删除 `data/legacy/`。
4. 不要把 `data/sts2_data.json` 当作 RollCore 主运行数据。
5. `legacy` 是历史命名，不代表过期。
6. 改 RollCore RNG / 预测逻辑前，先查 source audits、Knowledge Base、ADR 和用户实测。
7. 重要预测逻辑变更必须写 source audit / Knowledge Base / ADR。
8. 不要恢复旧 LeadPaperweight `NoRare` 结论。
9. 修改代码后做静态检查和本地 build。
10. 当前环境不保证 dotnet build，交付后提醒用户本地运行 `build_windows_wpf.bat`。

## 5. 当前关键事实

### RNG

- v0.107.1+ 使用 MegaRandom / xoshiro256** / SplitMix64。
- visible seed 仍为 10 chars。
- seed normalization：`I -> 1`，`O -> 0`。
- RunRng stream seed = seed hash + stream hash。
- PlayerRng 使用 player slot index，不用 NetId。
- EventModel.Rng = run seed + player slot offset + event id hash。
- `NextInt(1)` 和 `NextItem(count==1)` 都消耗 RNG。

### Event / Boss / Ancient

- Act selection 用 `act_selection` 独立 stream。
- Act 内 GenerateRooms 顺序：event shuffle -> weak -> regular -> elite -> boss -> ancient。
- Boss 在 Ancient 前。
- A10 Act3 second boss 在 Act3 Ancient 后抽。
- Effective event queue 才是实际事件顺序。
- 每幕起点 Ancient 进入 EventRoom，使 eventsVisited +1，因此普通事件默认从 raw[1] 开始。

### Neow / relic effects

- Neow 使用 EventModel.Rng，不是 UpFront / Niche。
- NeowsBones relic shuffle 使用 PlayerRng.Rewards。
- BoneDice curse 使用 RunRng.Niche。
- LargeCapsule：2 random normal relic + Strike + Defend。
- SmallCapsule：1 random normal relic。
- Capsule 不应抽 shop / ancient / Neow / MassiveScroll。
- MassiveScroll 是 multiplayer-only Neow / Ancient relic。
- LeadPaperweight 当前模型：ColorlessCardPool + RegularEncounter + PlayerRng.Rewards，允许 Rare where game odds permit it。

## 6. 文档体系

```text
CURRENT_STATUS.md                 当前唯一开发基线
CHANGELOG.md                      版本变化
docs/README.md                    文档索引
docs/KNOWLEDGE_MANAGEMENT.md      文档流程
docs/source_audits/               源码审计证据
docs/KNOWLEDGE_BASE/              长期稳定事实
docs/ADR/                         架构决策
docs/postmortems/                 复盘
```

## 7. 下一步

1. 用户本地运行 `build_windows_wpf.bat`。
2. 用户本地运行 `publish_windows_x64.bat`。
3. 将生成的 `RollTheSpire2_v2.1.1_win-x64.zip` 上传到 GitHub Releases。
4. 下一轮功能迭代开始前，先更新 `CURRENT_STATUS.md` 的当前开发目标和 P0。
