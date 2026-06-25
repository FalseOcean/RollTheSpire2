# RollTheSpire2 Documentation Index

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）

本目录保存 `RollTheSpire2 External Tool` 的长期维护文档。本文档体系的目标不是只写 README，而是把：

```text
源码审计 -> 知识沉淀 -> 架构决策 -> 代码实现 -> 回归验证
```

串成可追溯流程。

## 文档层级

| 层级 | 路径 | 作用 |
| --- | --- | --- |
| 当前状态 | `../CURRENT_STATUS.md` | 当前唯一开发基线，决定现在做什么 |
| 版本历史 | `../CHANGELOG.md` | 记录每个版本发生了什么，不解释完整原因 |
| 文档规则 | `KNOWLEDGE_MANAGEMENT.md` | 说明文档如何升级、如何同步、如何处理冲突 |
| 源码审计 | `source_audits/` | 保存 Codex / 人工源码审计证据或迁移期审计摘要 |
| 长期事实库 | `KNOWLEDGE_BASE/` | 保存已稳定、可被 RollCore 引用的事实 |
| 架构决策 | `ADR/` | 保存为什么选择某种设计或模型 |
| 复盘 | `postmortems/` | 保存错误归因、测试覆盖不足和事故复盘 |
| 发布文档 | `RELEASE_BUILD_GUIDE.md`、`PUBLIC_RELEASE_CHECKLIST.md` | 发布和打包流程 |

## 维护原则

- `CURRENT_STATUS.md` 的优先级最高。
- `CHANGELOG.md` 只写版本变化，不承载完整源码推导。
- `source_audits/` 保存证据，`KNOWLEDGE_BASE/` 保存稳定事实，`ADR/` 保存设计理由。
- 重要 RNG / 预测逻辑变更必须先有源码审计和知识库记录，再改 RollCore。
- 文档必须尽量写明记录日期和适用 STS2 版本。
- External Tool 与 Workshop Mod 是兄弟项目，不要把两者设计约束混用。

## 当前关键入口

- 当前开发状态：`../CURRENT_STATUS.md`
- 文档管理规范：`KNOWLEDGE_MANAGEMENT.md`
- 主运行数据说明：`KNOWLEDGE_BASE/DATA_LAYOUT.md`
- LeadPaperweight ADR：`ADR/ADR-001-leadpaperweight-rare.md`
- LeadPaperweight 复盘：`postmortems/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md`
- LeadPaperweight 审计摘要：`source_audits/LEAD_PAPERWEIGHT_RARE_2026-06-26.md`
