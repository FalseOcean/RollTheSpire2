# Knowledge Management

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
项目线：RollTheSpire2 External Tool

## 1. 目标

本文件定义外部 WPF 工具的文档管理规则。目标是避免把源码审计、实测结论、版本历史和架构决策混在一起，确保长期维护时可以快速回答：

```text
现在项目状态是什么？
某个预测结论的源码证据是什么？
哪些事实已经稳定？
为什么最终这么设计？
哪个版本改了什么？
```

## 2. 文档职责分层

### CURRENT_STATUS.md

唯一当前开发基线。记录当前版本、P0/P1、稳定模块、不能破坏的行为、下一步目标。

不负责保存完整源码审计，也不负责写版本 changelog。

### CHANGELOG.md

只记录版本发生了什么：Added / Fixed / Changed / Prediction logic / Docs。

不写完整推导，不替代 ADR，不替代 Knowledge Base。

### docs/source_audits/

保存源码证据。优先保存 Codex 审计原文；如果迁移期没有原文，可以先保存审计摘要，但必须标注“原始审计原文未随包保存”。

### docs/KNOWLEDGE_BASE/

保存长期稳定事实，例如 RNG、Neow、Bones、Event Queue、Boss/Ancient、Card Rewards、Data Layout。

Knowledge Base 中的内容应该已经经过源码审计、用户实测或固定 seed 回归支持。

### docs/ADR/

保存架构决策。ADR 回答“为什么这样设计”，尤其适用于：

- 推翻旧 RollCore 模型。
- 改变 RNG / 预测逻辑。
- 改变数据布局。
- 引入新的长期流程或边界约束。

### docs/postmortems/

保存错误复盘。Postmortem 关注：

- 原错误是什么。
- 为什么会误判。
- 哪些测试没覆盖。
- 后续如何避免。

### PROJECT_SOURCE_CURRENT.md

用于 ChatGPT Project / 新对话迁移的知识快照。它不是开发源文件，不应每次小改都生成。

生成时机：

- 大版本发布。
- 文档体系大修。
- 准备开启新对话。
- Project Knowledge 需要整体刷新。

## 3. 信息冲突处理

如果不同来源冲突，优先级为：

```text
CURRENT_STATUS.md
> 当前源码包 / 最新 Git commit
> docs/KNOWLEDGE_BASE/
> docs/ADR/
> docs/source_audits/
> README / CHANGELOG
> 旧聊天记录 / 旧 Project Source
```

如果源码审计、用户实测和旧 RollCore 经验冲突，不允许直接压过任何一方。必须找出：

- 游戏版本差异。
- 进阶等级差异。
- unlock profile 差异。
- RNG stream 差异。
- 路径 / route state 差异。
- 样本覆盖不足。

## 4. 新机制处理流程

对于任何新的、不确定的游戏行为，采用以下流程：

```text
发现预测问题 / 新机制不确定
-> 写 Codex 源码审计 prompt
-> Codex 或人工审计源码
-> 保存到 docs/source_audits/
-> 用户游戏实测或固定 seed 回归
-> 结论稳定后提升到 docs/KNOWLEDGE_BASE/
-> 如果影响算法、架构或推翻旧结论，写 docs/ADR/
-> 再修改 RollCore / WPF
-> 做静态检查和本地 build
-> 更新 CURRENT_STATUS.md
-> 更新 CHANGELOG.md
-> 必要时生成 PROJECT_SOURCE_CURRENT.md
```

## 5. 什么变更必须写文档

必须写 source audit / knowledge base / ADR：

- RNG stream。
- seed hash / normalization。
- RunRng / PlayerRng / EventRng。
- Neow option 生成。
- Bones relic / curse 顺序。
- Boss / Ancient / Event Queue。
- Card reward / rarity / Scarcity。
- relic pool / capsule pool。
- transform target pool。
- unlock profile 影响。
- runtime data layout。

通常只需更新 CHANGELOG / CURRENT_STATUS：

- 纯 UI 文案。
- 布局 polish。
- 不影响预测逻辑的按钮 / tooltip。
- 发布脚本小修。

## 6. 文档元信息

新增或大幅修改知识文档时，尽量写明：

```text
记录日期
适用 STS2 游戏版本
适用工具版本 / 分支
信息来源：源码审计 / 用户实测 / 固定 seed 回归 / RollCore 校准
状态：Draft / Verified / Promoted / Deprecated
```

## 7. External Tool 特有红线

- `data/legacy/sts2_runtime_legacy_v2.json` 是当前主运行数据。
- `data/sts2_data.json` 是 source sidecar，不能替代主运行数据。
- `config.json:data_file` 是预测输入的一部分，不是普通文档路径。
- 文档清理不能修改 `config.json`、`data/`、`RollCore/`、`RollWpf/`。
- `profiles/unlock_profile.json` 和 `profiles/database/*.json` 是用户本地数据，不应提交。
- `publish/` 是构建产物，不应提交 GitHub。
