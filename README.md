# RollTheSpire2

`RollTheSpire2` 是一个面向 **Slay the Spire 2 v0.107.1+ RNG Rework 线** 的外部 WPF 种子分析与批量筛种工具。

它可以在不启动游戏的情况下离线模拟开局结果，并按条件批量筛选 seed。当前重点支持 Neow 开局、Neow's Bones / 骨骰路线、Neow 遗物效果、Boss、Ancient、事件 effective queue、收藏库、粗筛候选池和 `progress.save` 解锁档案导入。

> `RollTheSpire2` 是外部工具，不是 Workshop Mod。它不会注入游戏进程，不会修改游戏存档，也不依赖游戏正在运行。

## 当前版本

| 项目 | 状态 |
| --- | --- |
| 当前公开稳定版 | `v2.1.1` |
| 上一公开稳定版 | `v2.1.0` |
| 目标游戏版本 | `Slay the Spire 2 v0.107.1+` RNG Rework 后主线 |
| 主运行数据 | `data/legacy/sts2_runtime_legacy_v2.json` |

`v2.1.1` 是基于 `v2.1.0` 的预测正确性维护补丁，重点修正 `LeadPaperweight` / 铅制镇纸的无色卡稀有度预测：旧模型错误地将它视为 `NoRare`，只允许 Common / Uncommon；修正后按无色 `RegularEncounter` 奖励路径建模，在游戏 odds 允许时可以生成 Rare colorless card。

## 内容列表

- [背景](#背景)
- [安装](#安装)
- [使用说明](#使用说明)
- [功能概览](#功能概览)
- [数据与版本支持](#数据与版本支持)
- [文档与维护](#文档与维护)
- [项目结构](#项目结构)
- [从源码构建](#从源码构建)
- [维护者](#维护者)
- [鸣谢](#鸣谢)
- [如何贡献](#如何贡献)
- [使用许可](#使用许可)

## 背景

这个项目最初是因为自己 roll 种手都快 roll 断了还找不到想要的“农种”，一气之下让 AI 辅助做出来的。后来它逐渐从一个 Neow 开局预览器，发展成了完整的 STS2 外部种子研究工具。

它主要解决这些问题：

- 给定一个 seed，我开局会看到什么？
- Neow 三选里有没有目标遗物？
- Neow's Bones / 骨骰会给什么？诅咒能不能避开 Debt？
- 这个 seed 的 Boss、Ancient、事件队列是否符合目标？
- 能否批量扫描 seed，先粗筛，再保存候选池，再逐步精筛？

## 安装

### 普通用户

前往 GitHub Releases 下载最新的 Windows x64 发布包，例如：

```text
RollTheSpire2_v<version>_win-x64.zip
```

解压后直接双击：

```text
RollTheSpire2.exe
```

发布包通常是 self-contained portable zip，体积较大是正常的，因为它会附带 .NET 运行时。普通用户不需要额外安装 .NET SDK / Runtime。

如果双击 exe 后没有看到窗口，可以尝试运行备用启动器：

```text
run_rollthespire2.bat
```

它会保留控制台窗口，方便查看启动错误。

### 从源码运行

源码运行需要：

- Windows 10/11 x64
- .NET 9 SDK
- 完整的 `data/` 目录

在仓库根目录执行：

```bat
build_windows_wpf.bat
run_wpf.bat
```

## 使用说明

### 图形界面常见流程

1. 打开 `RollTheSpire2.exe`。
2. 在“配置”页选择角色、进阶、运行模式和解锁档案。
3. 如果希望预测贴近自己的账号解锁进度，可以导入 `progress.save`。
4. 在“单种分析”页输入 seed，查看该 seed 的开局预测和路线详情。
5. 在“批量筛种”页添加筛选条件，例如 Neow 遗物、骨骰遗物、骨骰诅咒黑名单、Boss、Ancient 或事件队列。
6. 点击“开始搜索”，结果会出现在“筛种结果”页。
7. 对有价值的结果，可以复制 seed、复制详情、填入单种分析、收藏，或者保存成粗筛候选池。
8. 若条件非常稀有，建议先放宽条件筛一批候选池，再从候选池继续精筛。

### 运行时生成的用户文件

程序可能在 `profiles/` 下生成这些本地文件：

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
```

这些文件属于用户本地数据，默认已被 `.gitignore` 忽略，不建议提交到 GitHub。

## 功能概览

### 单种分析

输入一个 seed 后，工具会输出该 seed 的预测结果，包括：

- seed 标准化和游戏可见 seed normalization。
- 角色、进阶、单人 / 多人参数。
- `progress.save` 解锁影响。
- Neow 三选。
- 直接 Neow 遗物效果。
- Neow's Bones / 骨骰路线。
- BoneDice curse。
- LargeCapsule / SmallCapsule 生成普通 relic。
- NewLeaf / LeafyPoultice transform。
- ScrollBoxes、Kaleidoscope、LostCoffer、LeadPaperweight、HeftyTablet、NeowsTorment 等路线相关结果。
- Act selection。
- Boss / Ancient。
- Event raw queue / effective queue。
- 商店专属遗物序列。
- 地图 / Boss block。
- 原始详情与调试信息。

### 批量筛种

批量筛种支持随机搜索、顺序枚举和候选池继续筛选。常用条件包括：

- Neow 三选必须包含 / 排除指定 Neow 遗物。
- 骨骰给出的两个 Neow relic 必须包含指定项。
- 骨骰诅咒白名单 / 黑名单。
- 指定 NewLeaf / LeafyPoultice / ScrollBoxes / Kaleidoscope / LostCoffer / LeadPaperweight 结果。
- 最终牌组必须包含 / 排除指定牌。
- 最终普通遗物必须包含 / 排除指定遗物。
- 最终 Neow relic、诅咒、药水条件。
- Act1 / Act2 / Act3 Boss 指定。
- Act1 / Act2 / Act3 Ancient 指定。
- Act3 A10 第二 Boss 指定。
- Act 前 N 个 effective event 必须包含 / 排除指定事件。
- 商店遗物和普通遗物序列相关筛选。

### 过程导向与最终结果导向

工具区分两种筛法：

- **过程导向**：要求开局路线本身满足条件。例如 Neow 有骨骰，骨骰给 NewLeaf + LeafyPoultice，诅咒不要 Debt。
- **最终结果导向**：不关心具体来源，只看最终牌组、普通遗物、诅咒、药水或 Neow 遗物是否满足条件。

注意区分：

```text
Neow 遗物：NeowsBones、LargeCapsule、SmallCapsule、NewLeaf、LeafyPoultice 等开局 Neow relic。
最终普通遗物：LargeCapsule / SmallCapsule 等效果生成出来的普通遗物。
```

如果想筛“骨骰给巨大扭蛋 + 小型扭蛋”，应使用 Neow 遗物条件；如果想筛“巨大扭蛋生成赌博筹码”，应使用最终普通遗物条件。

### 重复卡牌条件

部分卡牌条件支持数量语义。例如输入三次 `Claw`，语义是至少需要三张 `Claw`，而不是只要出现一张即可。这对 ScrollBoxes 三爪、NewLeaf / LeafyPoultice 极端变化结果很有用。

### 事件 effective queue

事件筛选基于 effective event queue，而不是单纯 raw queue。它会考虑：

- 每幕开头 Ancient 进入 EventRoom 后导致本 Act 的 `eventsVisited + 1`。
- `IsAllowed=false` 的事件跳过。
- `VisitedEventIds` 去重。
- `Hook.ModifyNextEvent` 替换。

因此它比直接查看 raw 事件列表更接近实际问号房遇到的顺序。

### 收藏库

收藏库用于保存有价值的 seed。记录可以包含 seed、角色、进阶、标签、备注、命中解释、详细预测文本和工具版本信息。保存后的记录可以重新搜索，也可以再次分析。

### 粗筛候选池

粗筛候选池用于处理极低概率目标。推荐流程是：

```text
先用宽松条件筛一批 seed
-> 保存为候选池
-> 从候选池继续加条件精筛
-> 保留最终命中
```

候选池当前使用本地 JSON 文件保存，不依赖 SQLite。

## 数据与版本支持

| 项目 | 当前状态 |
| --- | --- |
| 目标游戏版本 | Slay the Spire 2 v0.107.1+ RNG Rework 线 |
| RNG 兼容目标 | MegaRandom / xoshiro256\*\* 兼容实现 |
| 默认 seed 长度 | 10 位游戏可见 seed |
| 主运行数据 | `data/legacy/sts2_runtime_legacy_v2.json` |
| 源码抽取辅助数据 | `data/sts2_data.json` |
| 事件规则 / 文本 | `data/event_rules.json`、`data/event_texts.json` |
| 实体索引 | `data/entity_index.json` |
| 用户解锁档案 | `profiles/unlock_profile.json` |
| 收藏库 | `profiles/database/search_history.json` |
| 粗筛候选池 | `profiles/database/candidate_pools.json` |

### 重要数据说明

当前 RollCore 主运行数据是：

```text
data/legacy/sts2_runtime_legacy_v2.json
```

这里的 `legacy` 是历史命名遗留，意思是“RollCore 运行时主数据格式”，不是过期数据，也不能删除。

`data/sts2_data.json` 是源码抽取辅助数据，不能直接替代主运行数据。当前正确配置必须保持：

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

更多说明见：

```text
docs/DATA_LAYOUT.md
docs/KNOWLEDGE_BASE/DATA_LAYOUT.md
```

## 文档与维护

项目文档采用分层结构，避免把源码审计、长期事实、架构决策和版本历史混在 README 里。

```text
CURRENT_STATUS.md                 当前开发基线 / Source of Truth
docs/README.md                    文档入口
docs/KNOWLEDGE_MANAGEMENT.md      文档管理流程
docs/source_audits/               源码审计证据或迁移期审计摘要
docs/KNOWLEDGE_BASE/              长期稳定事实
docs/ADR/                         架构决策记录
docs/postmortems/                 错误归因和复盘
CHANGELOG.md                      版本变化记录
```

涉及 RNG、池子、顺序、进阶、unlock、route state 的预测逻辑变更，应同步 source audit / Knowledge Base / ADR。普通文档清理不应修改 `RollCore/`、`RollWpf/`、`data/` 或 `config.json`。

## 项目结构

```text
RollTheSpire2/
├─ RollCore/                 # 纯预测核心、RNG 兼容实现、数据读取、筛选逻辑
├─ RollWpf/                  # WPF 图形界面
├─ data/                     # 运行数据、源码抽取辅助数据、事件规则、实体索引
│  ├─ legacy/                # 当前 RollCore 主运行数据，不能删除
│  └─ README.md
├─ docs/                     # 文档体系、知识库、ADR、源码审计、复盘
├─ profiles/                 # 用户本地 profile / 收藏 / 候选池，默认不提交个人数据
├─ CURRENT_STATUS.md         # 当前开发基线
├─ config.json               # 默认配置
├─ build_windows_wpf.bat     # 源码构建脚本
├─ run_wpf.bat               # 源码运行脚本
├─ publish_windows_x64.bat   # 生成 portable Windows 发布包
└─ run_rollthespire2.bat     # 发布包备用启动器
```

当前公开包已经移除旧 Web / WinForms / FastCore / extractor 实验线，公开主线是 WPF + RollCore。

## 从源码构建

常用开发命令：

```bat
build_windows_wpf.bat
run_wpf.bat
```

手动命令：

```powershell
dotnet restore RollWpf/RollWpf.csproj
dotnet build RollWpf/RollWpf.csproj -c Release
dotnet run --project RollWpf/RollWpf.csproj -c Release
```

生成 portable Windows 发布包：

```bat
publish_windows_x64.bat
```

## 维护者

- Ocean False / @falseocean8

## 鸣谢

- 本项目在设计、代码整理、文档编写、RNG 逻辑校准和版本发布整理过程中使用了 ChatGPT 进行辅助开发。
- 感谢 Slay the Spire 2 社区中关于 RNG、事件队列、Neow、先古之民和种子机制的讨论与验证样本。
- 感谢 Mega Crit 制作 Slay the Spire 2。本项目是非官方外部工具，与 Mega Crit 没有关联。

## 如何贡献

欢迎通过 Issue 或 Pull Request 反馈问题。贡献时建议说明：

- 使用的游戏版本。
- 使用的工具版本。
- 角色、进阶、seed、是否导入 `progress.save`。
- 预期结果与实际游戏结果。
- 如果涉及事件队列、Boss、Ancient 或 Neow 路线，请尽量提供最小复现步骤。

如果改动了预测逻辑、数据文件或 `config.json:data_file`，需要附带说明，并同步相关文档：

- `docs/source_audits/`
- `docs/KNOWLEDGE_BASE/`
- `docs/ADR/`
- `CHANGELOG.md`
- `CURRENT_STATUS.md`

## 使用许可

当前仓库尚未附带正式 `LICENSE` 文件。公开发布前建议明确选择许可证；在未添加许可证前，默认按未授权 / 保留所有权利处理。
