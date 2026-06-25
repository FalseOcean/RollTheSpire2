# RollTheSpire2 External Tool — CURRENT_STATUS

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
当前公开稳定版：`v2.1.1`  
当前开发线：暂无；下一轮功能迭代待定  
当前项目线：External WPF Tool，不是 Workshop Mod  
公开仓库：`FalseOcean/RollTheSpire2`

## 0. Source of Truth 优先级

维护本项目时，若信息发生冲突，按以下优先级处理：

```text
CURRENT_STATUS.md
> 当前源码包 / 最新 Git commit
> docs/KNOWLEDGE_BASE/ 中已提升的稳定事实
> docs/ADR/ 中的架构决策
> docs/source_audits/ 中的源码证据
> README / CHANGELOG
> 旧聊天记录 / 旧 Project Source
```

`CURRENT_STATUS.md` 负责回答“现在项目处于什么状态、下一步应该做什么”。它不是知识库，不保存完整源码审计原文，也不替代 CHANGELOG。

## 1. 当前开发目标

当前阶段是 `v2.1.1` stable 发布准备完成后的维护基线。`v2.1.1` 已将 LeadPaperweight / 铅制镇纸 Rare colorless 预测修正与外部工具文档体系整理纳入稳定维护线。

当前维护目标：

1. 将 `v2.1.1` 作为新的公开稳定补丁线维护。
2. 保持 `CURRENT_STATUS.md` 作为唯一当前开发基线。
3. 保持 `docs/source_audits/`、`docs/KNOWLEDGE_BASE/`、`docs/ADR/`、`docs/postmortems/` 的分层文档体系。
4. 后续任何 RNG / 预测逻辑变更必须先补 source audit / knowledge base / ADR。
5. 保留并强调 `data/legacy/sts2_runtime_legacy_v2.json` 是当前 RollCore 主运行数据。

## 2. 当前版本状态

| 项目 | 状态 |
| --- | --- |
| 最新公开稳定版 | `v2.1.1` |
| 上一公开稳定版 | `v2.1.0` |
| 本版本核心变化 | 修正 `LeadPaperweight` / 铅制镇纸 Rare colorless 预测 |
| 当前主运行数据 | `data/legacy/sts2_runtime_legacy_v2.json` |
| 源码抽取辅助数据 | `data/sts2_data.json` |
| WPF 版本显示 | `v2.1.1` |
| 发布脚本版本 | `v2.1.1` |

`v2.1.1` 是基于 `v2.1.0` 的稳定维护补丁，重点修正 LeadPaperweight 从旧 `NoRare` 模型到无色 `RegularEncounter` 奖励路径的预测差异。

## 3. 当前稳定模块

以下模块已作为相对稳定基线维护，修改前必须有明确理由和回归样本：

- seed normalization。
- v0.107.1+ MegaRandom / xoshiro256** 基础兼容。
- Act selection。
- UpFront 主消耗顺序。
- Boss / Ancient 预测。
- A10 Act3 second boss 顺序。
- Event effective queue 起点 offset。
- 每幕起点 Ancient 造成的 `eventsVisited + 1`。
- Candidate pool 工作流。
- `progress.save` 基础导入。
- 角色中文名显示。
- GitHub public packaging 基线。
- `config.json:data_file = data/legacy/sts2_runtime_legacy_v2.json`。

## 4. 当前基本稳定但需谨慎模块

以下模块可以继续迭代，但任何 RNG / 预测逻辑变更必须先写入 source audit / knowledge base / ADR：

- Neow relic route prediction。
- NeowsBones / BoneDice route。
- NewLeaf / LeafyPoultice route-aware transform。
- LostCoffer。
- Kaleidoscope。
- ScrollBoxes。
- LeadPaperweight `v2.1.1` 修复后的低进阶 / 高进阶交叉验证。
- Final result filter。
- Duplicate card count。
- Event encyclopedia / tooltip。
- 搜索历史 / 收藏库。
- 粗筛候选池 UI。

## 5. 当前 P0

1. 用户本地运行 `build_windows_wpf.bat`，确认源码构建通过。
2. 用户本地运行 `publish_windows_x64.bat`，生成 `v2.1.1` Windows x64 发布包。
3. 上传 `v2.1.1` GitHub Release 包。
4. 保留 LeadPaperweight 低进阶 / 高进阶样本作为后续 regression 基线。
5. 下一轮功能迭代开始前，先确认是否需要新的 source audit。

## 6. 当前 P1

- 完整整理 `CARD_REWARDS_AND_SCARCITY.md`。
- Egg / Hook / route state 影响单独审计。
- Capsule / normal relic pool 再校准。
- 候选池导入、合并、去重。
- 收藏库导入导出。
- 批量筛种性能优化。
- 自动化 regression suite。
- CI release build。

## 7. 当前不能破坏的行为

- 不要混淆 External Tool 与 Workshop Mod。
- 不要在文档清理时修改 RollCore 预测逻辑。
- 不要修改 `config.json:data_file`。
- 不要删除 `data/legacy/`。
- 不要把 `data/sts2_data.json` 当成 RollCore 主运行数据。
- 不要恢复旧 LeadPaperweight `NoRare` 结论。
- 不要把高进阶没观测到 Rare 错归因成来源禁止 Rare。
- 不要提交 `publish/`、`bin/`、`obj/`、用户 profile 数据。

## 8. 当前维护边界

当前 `v2.1.1` stable 源码包允许进行版本文案、发布脚本版本号和文档状态同步。

后续功能迭代前必须重新确认边界：

- RNG / 预测逻辑变更：先 source audit，再 Knowledge Base / ADR，再改 RollCore。
- 纯 UI / 文档变更：可以直接修改，但版本结束时必须同步 CURRENT_STATUS / CHANGELOG。
- 数据布局变更：必须单独开迁移版本，不得顺手修改。

## 9. 每次版本结束必须同步

每次 preview / stable / patch 结束时至少同步：

```text
CURRENT_STATUS.md
CHANGELOG.md
docs/KNOWLEDGE_BASE/
docs/source_audits/
docs/ADR/
docs/postmortems/（如果产生复盘）
PROJECT_SOURCE_CURRENT.md（仅在迁移 Project / 开新对话 / 大版本发布时生成）
```

## 10. 下一步建议

当前下一步：

1. 用户本地运行 `build_windows_wpf.bat`。
2. 用户本地运行 `publish_windows_x64.bat`。
3. 将生成的 `RollTheSpire2_v2.1.1_win-x64.zip` 上传到 GitHub Releases。
4. 下一轮功能更新开始前，先更新本文件的“当前开发目标”和 P0。
