# Candidate Pools Knowledge Base

记录日期：2026-06-26  
适用游戏版本：Slay the Spire 2 v0.107.1+ RNG Rework 主线（项目简称 0.107.1 rng+）  
状态：Stable workflow

## 定位

粗筛候选池是 External Tool 的核心工作流之一，不是附属功能。

典型流程：

```text
放宽条件粗筛
-> 保存候选池
-> 从候选池继续加条件精筛
-> 找极端稀有种子
```

## 数据边界

候选池属于用户本地数据，通常保存到：

```text
profiles/database/candidate_pools.json
```

该文件不应提交 GitHub。

## 设计原则

- 候选池只需要保存 seed 和轻量 summary，不保存完整大型详情。
- 从候选池继续筛选时，应重新调用当前 RollCore 分析，避免旧详情污染结果。
- 候选池 UI 应支持保存、选择、继续筛、查看、清理。
- 未来可增加导入、合并、去重、导出。
