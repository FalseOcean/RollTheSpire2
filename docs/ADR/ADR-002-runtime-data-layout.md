# ADR-002: 保留 data/legacy/sts2_runtime_legacy_v2.json 作为主运行数据

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
适用工具版本：`v2.1.0` / `v2.1.1`  
状态：Accepted

## 背景

项目同时包含：

```text
data/legacy/sts2_runtime_legacy_v2.json
data/sts2_data.json
```

前者是当前 RollCore 主运行数据，后者是源码抽取辅助数据。由于 `legacy` 名称容易误导，历史上曾在 public clean 包中错误删除 `data/legacy/` 并把 `config.json:data_file` 改为 `data/sts2_data.json`，导致预测结果错误。

## 决策

继续保留：

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

并明确：

```text
legacy 是历史命名，不代表过期。
data/sts2_data.json 不能替代 RollCore 主运行数据。
```

## 理由

- 当前 RollCore 预测基线依赖 `sts2_runtime_legacy_v2.json` 的结构和数据语义。
- `data/sts2_data.json` 是 source sidecar，字段结构和用途不同。
- 数据路径错误会直接污染预测结果。
- 文档清理和 public packaging 不应改变预测输入。

## 后果

- 短期内保持 `data/legacy/` 名称，即使名称不够理想。
- README、CURRENT_STATUS、Knowledge Base、Release Checklist 都必须重复强调该规则。
- 如果未来迁移到 `data/runtime/`，必须单独开迁移版本并做 regression。

## 相关文档

- `../KNOWLEDGE_BASE/DATA_LAYOUT.md`
- `../DATA_LAYOUT.md`
- `../PUBLIC_RELEASE_CHECKLIST.md`
