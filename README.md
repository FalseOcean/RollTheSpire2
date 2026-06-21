# RollTheSpire2

`RollTheSpire2` 是一个面向 **Slay the Spire 2 v0.107.1+** 的外部种子分析与批量筛种工具。

它不需要启动游戏即可离线模拟和筛选开局结果，重点支持 Neow 开局、Neow's Bones / 骨骰路线、Neow 遗物效果、Boss、Ancient、事件 effective queue、收藏库和粗筛候选池。项目当前提供 WPF 图形界面，适合单种分析、批量 Roll 种、保存爽种、候选池精筛和 `progress.save` 解锁档案导入。

当前稳定版：`v2.1.0`。本版本基于已经实测正确的 `v2.1.0-preview2e` 预测基线整理而来；公开发布整理只更新项目名称、启动脚本、发布脚本和文档，**没有修改 RollCore 预测算法、WPF 功能逻辑、`config.json` 或主运行数据路径**。

> 重要说明：曾经的 `v2.1.0-preview3_public_release_pack` 是坏包，因为它错误地把 `config.json:data_file` 从 `data/legacy/sts2_runtime_legacy_v2.json` 改成了 `data/sts2_data.json`，并移除了 `data/legacy`，会导致预测结果错误。当前稳定版仍以 `data/legacy/sts2_runtime_legacy_v2.json` 作为 RollCore 主运行数据。

## 内容列表

- [背景](#背景)
- [安装](#安装)
- [使用说明](#使用说明)
- [功能概览](#功能概览)
- [数据与版本支持](#数据与版本支持)
- [项目结构](#项目结构)
- [开发](#开发)
- [GitHub 发布建议](#github-发布建议)
- [维护者](#维护者)
- [鸣谢](#鸣谢)
- [如何贡献](#如何贡献)
- [使用许可](#使用许可)

## 背景

这个项目最初是因为自己roll种手都roll断了都roll不到农种，一气之下让ai做的，试问谁看到农的开局会不笑呢。

这是功能：

- 可以对单个 seed 做详细分析，展示 Neow 开局、骨骰路线、Boss、Ancient、事件队列和相关遗物结果。
- 可以批量扫描随机 seed，按 Neow、骨骰、最终结果、Boss、Ancient、事件队列等条件筛选。
- 可以导入 `progress.save`，让预测尽量贴近玩家自己的解锁进度。
- 可以把命中的 seed 保存到收藏库，后续按标签、备注、角色、命中解释重新检索。
- 可以把宽松条件筛到的结果保存成粗筛候选池，再逐步追加条件做精筛。

本项目是外部 Companion Tool，不是游戏内 Workshop Mod。它不会注入游戏进程，也不会修改游戏存档。

## 安装

### 普通用户：下载发布版

前往 GitHub Releases 下载：

```text
RollTheSpire2_v2.1.0_win-x64.zip
```

解压后直接双击：

```text
RollTheSpire2.exe
```

发布版推荐使用 self-contained portable zip，通常会比较大，因为其中包含 .NET 运行时。这样普通用户不需要额外安装 .NET SDK 或 Runtime。

如果双击 exe 后窗口没有出现，可以尝试双击备用启动器：

```text
run_rollthespire2.bat
```

它可以保留控制台窗口，便于查看启动错误。

### 从源码运行 / 构建

源码构建需要：

- Windows 10/11 x64
- .NET 9 SDK
- 完整的 `data/` 目录

在仓库根目录执行：

```bat
build_windows_wpf.bat
run_wpf.bat
```

构建成功后，开发版程序位于：

```text
RollWpf/bin/Release/net9.0-windows/RollTheSpire2.exe
```

也可以手动执行：

```powershell
dotnet restore RollWpf/RollWpf.csproj
dotnet build RollWpf/RollWpf.csproj -c Release
```

## 使用说明

### 图形界面常见流程

1. 打开 `RollTheSpire2.exe`。
2. 在“配置”页选择角色、进阶、运行模式和解锁档案。
3. 如果希望贴近自己的账号解锁进度，可以导入 `progress.save`。
4. 在“单种分析”页输入 seed，查看该 seed 的开局预测和路线详情。
5. 在“批量筛种”页添加筛选条件，例如 Neow 遗物、骨骰遗物、骨骰诅咒黑名单、Boss、Ancient 或事件队列。
6. 点击“开始搜索”，结果会出现在“筛种结果”页。
7. 对有价值的结果，可以复制 seed、复制详情、填入单种分析、收藏，或者保存成粗筛候选池。
8. 若条件很稀有，可以先放宽条件筛一批候选池，再从候选池继续精筛。

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

- Neow 三选。
- 直接 Neow 遗物效果。
- Neow's Bones / 骨骰路线。
- ScrollBoxes、Kaleidoscope、NewLeaf、LeafyPoultice、LostCoffer 等路线相关结果。
- Act selection。
- Boss / Ancient。
- 事件 effective queue。
- 商店遗物 / 普通遗物相关信息。
- 原始详情与调试信息。

### 批量筛种

批量筛种支持随机搜索和候选池继续筛选。当前常用条件包括：

- Neow 三选必须包含 / 排除指定 Neow 遗物。
- 骨骰给出的两个 Neow relic 必须包含指定项。
- 骨骰诅咒白名单 / 黑名单。
- 最终牌组必须包含 / 排除指定牌。
- 最终普通遗物必须包含 / 排除指定遗物。
- 最终诅咒、药水条件。
- Act1 / Act2 / Act3 Boss 指定。
- Act1 / Act2 / Act3 Ancient 指定。
- Act3 A10 第二 Boss 指定。
- Act 前 N 个 effective event 必须包含 / 排除指定事件。
- 商店遗物和普通遗物序列相关筛选。

### 过程导向与最终结果导向

工具区分两种筛法：

- 过程导向：要求开局路线本身满足条件。例如 Neow 有骨骰，骨骰给 NewLeaf + LeafyPoultice，诅咒不要 Debt。
- 最终结果导向：不关心具体来源，只看最终牌组、普通遗物、诅咒、药水或 Neow 遗物是否满足条件。

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

收藏库用于保存有价值的 seed。记录可以包含：

- seed
- 角色
- 进阶
- 标签
- 备注
- 命中解释
- 详细预测文本
- RNG / 工具版本信息

可以按 seed、角色、标签、备注、命中解释搜索，也可以重新分析收藏记录。

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
| RNG 兼容目标 | MegaRandom / xoshiro256** 兼容实现 |
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
```

## 项目结构

```text
RollTheSpire2/
├─ RollCore/                 # 纯预测核心、RNG 兼容实现、数据读取、筛选逻辑
├─ RollWpf/                  # WPF 图形界面
├─ data/                     # 运行数据、源码抽取辅助数据、事件规则、实体索引
│  ├─ legacy/                # 当前 RollCore 主运行数据，不能删除
│  └─ README.md
├─ docs/                     # 当前项目文档
├─ profiles/                 # 用户本地 profile / 收藏 / 候选池，默认不提交个人数据
├─ config.json               # 默认配置
├─ build_windows_wpf.bat     # 源码构建脚本
├─ run_wpf.bat               # 源码运行脚本
├─ publish_windows_x64.bat   # 生成 portable Windows 发布包
└─ run_rollthespire2.bat     # 发布包备用启动器
```

当前公开包已经移除旧 Web / WinForms / FastCore / extractor 实验线，公开主线是 WPF + RollCore。

## 开发

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

生成普通用户发布包：

```bat
publish_windows_x64.bat
```

输出目录：

```text
publish/RollTheSpire2_v2.1.0_win-x64/
```

这个目录应压缩后上传到 GitHub Releases，不应直接提交进仓库。

## GitHub 发布建议

建议把仓库和 Release 分开管理：

- GitHub 仓库：提交源码、数据、文档和构建脚本。
- GitHub Releases：上传 `publish/` 目录压缩出来的普通用户发布包。

不要提交：

```text
publish/
bin/
obj/
profiles/unlock_profile.json
profiles/database/*.json
profiles/database/*.tsv
```

这些内容已经在 `.gitignore` 中忽略。

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

如果改动了预测逻辑、数据文件或 `config.json:data_file`，必须附带回归测试说明。尤其不要把主运行数据从 `data/legacy/sts2_runtime_legacy_v2.json` 改到 `data/sts2_data.json`，除非正在做专门的数据格式迁移并完成实测回归。

## 使用许可

当前仓库尚未附带正式 `LICENSE` 文件。公开发布前建议明确选择许可证；在未添加许可证前，默认按未授权 / 保留所有权利处理。
