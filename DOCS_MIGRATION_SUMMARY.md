# Documentation Migration Summary

记录日期：2026-06-26  
包版本：v2.1.1 stable docs/source refresh  
项目线：RollTheSpire2 External Tool

## 本次目标

本次完成 v2.1.1 stable 文档体系整理，并同步 WPF / 发布脚本版本号；不修改 RollCore 预测逻辑或 config。`data/neow_relic_effects.json` 仅同步 LeadPaperweight notes 中的版本标记，不改变数据结构、pool 或预测语义。

## 新增核心文档

- `CURRENT_STATUS.md`
- `PROJECT_SOURCE_CURRENT.md`
- `docs/README.md`
- `docs/KNOWLEDGE_MANAGEMENT.md`
- `docs/source_audits/README.md`
- `docs/source_audits/LEAD_PAPERWEIGHT_RARE_2026-06-26.md`
- `docs/KNOWLEDGE_BASE/README.md`
- `docs/KNOWLEDGE_BASE/RNG.md`
- `docs/KNOWLEDGE_BASE/NEOW.md`
- `docs/KNOWLEDGE_BASE/BONES.md`
- `docs/KNOWLEDGE_BASE/CARD_REWARDS_AND_SCARCITY.md`
- `docs/KNOWLEDGE_BASE/CAPSULE.md`
- `docs/KNOWLEDGE_BASE/EVENT_QUEUE.md`
- `docs/KNOWLEDGE_BASE/BOSS_ANCIENT.md`
- `docs/KNOWLEDGE_BASE/DATA_LAYOUT.md`
- `docs/KNOWLEDGE_BASE/CANDIDATE_POOLS.md`
- `docs/ADR/README.md`
- `docs/ADR/ADR-001-leadpaperweight-rare.md`
- `docs/ADR/ADR-002-runtime-data-layout.md`
- `docs/postmortems/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md`

## 更新文档

- `README.md`
- `README_EN.md`
- `CHANGELOG.md`
- `RollWpf/RollWpf.csproj`（仅版本号）
- `RollWpf/MainWindow.xaml`（仅版本显示）
- `RollWpf/MainWindow.xaml.cs`（仅版本显示 / 日志文本）
- `publish_windows_x64.bat`（仅 APP_VERSION）
- `data/neow_relic_effects.json`（仅 LeadPaperweight notes 版本标记，无预测数据结构 / pool 改动）
- `docs/DATA_LAYOUT.md`
- `docs/PROJECT_CONTEXT_CURRENT.md`
- `docs/POSTMORTEM_LEADPAPERWEIGHT_RARITY.md`

## 保持不变的保护对象

- `config.json`
- `data/`
- `RollCore/`
- `build_windows_wpf.bat`
- `run_wpf.bat`

## 静态检查结果

已通过：

- JSON parse。
- XAML parse。
- x:Name 重复检查。
- WPF 事件处理器存在检查。
- C# braces 粗检查。
- `config.json:data_file` 检查。
- zip integrity。

当前环境没有 `dotnet`，未执行 `dotnet build`。请在 Windows 本地运行：

```bat
build_windows_wpf.bat
```
