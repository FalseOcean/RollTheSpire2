# Data Layout Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Critical stable rule

## 当前主运行数据

当前 RollCore 主运行数据是：

```text
data/legacy/sts2_runtime_legacy_v2.json
```

`legacy` 是历史命名，不代表过期。

正确配置必须保持：

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

## source sidecar

```text
data/sts2_data.json
```

定位：

- 源码抽取结果。
- source sidecar。
- 数据状态。
- 辅助校验。
- 事件百科 / tooltip。
- entity / localization 辅助来源。

它不能直接替代 `data/legacy/sts2_runtime_legacy_v2.json`。

## 历史事故

曾有 public clean 包错误地：

```text
config.json:data_file = data/sts2_data.json
删除 data/legacy/
```

结果导致预测大量错误。

结论：

```text
文档清理不能动 config.json / data / RollCore / RollWpf。
```

除非正在做专门数据布局迁移，并完成固定 seed 回归。

## 用户数据

用户运行时可能生成：

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
profiles/database/*.tsv
```

这些是本地用户数据，不应提交。

应保留：

```text
profiles/.keep
profiles/database/.keep
```

## 发布产物

`publish/` 是构建产物，不应提交 GitHub。普通用户发布包应由 `publish_windows_x64.bat` 生成后上传 GitHub Releases。

## 迁移规则

如果未来要把 `data/legacy/` 迁移到 `data/runtime/`：

1. 单独开版本。
2. 写 ADR。
3. 保留兼容路径或明确迁移步骤。
4. 更新 config / docs / release script。
5. 做固定 seed 回归。
6. 确认 public release 包含所有必需数据。
