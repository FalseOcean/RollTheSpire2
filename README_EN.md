# RollTheSpire2

`RollTheSpire2` is an external seed analysis and filtering companion tool for **Slay the Spire 2 v0.107.1+**.

It can simulate and filter opening seeds offline without launching the game. The current stable release focuses on Neow choices, Neow's Bones routes, Neow relic effects, Boss / Ancient prediction, effective event queues, favorites, candidate pools, and `progress.save` unlock-profile import.

Current stable version: `v2.1.0`. This release is based on the verified `v2.1.0-preview2e` prediction baseline. Public release cleanup only updates naming, launch scripts, release scripts, and documentation. It does **not** change RollCore prediction logic, WPF behavior, `config.json`, or runtime data paths.

> Important: `v2.1.0-preview3_public_release_pack` is a known bad package. It changed `config.json:data_file` to `data/sts2_data.json` and removed `data/legacy`, which breaks prediction results. The current stable release still uses `data/legacy/sts2_runtime_legacy_v2.json` as the RollCore runtime data file.

## Contents

- [Background](#background)
- [Installation](#installation)
- [Usage](#usage)
- [Feature overview](#feature-overview)
- [Data and version support](#data-and-version-support)
- [Project layout](#project-layout)
- [Development](#development)
- [GitHub release workflow](#github-release-workflow)
- [Maintainer](#maintainer)
- [Acknowledgements](#acknowledgements)
- [Contributing](#contributing)
- [License](#license)

## Background

This project originally started because I had rolled seeds by hand until my hands were practically broken, and still couldn’t find a “Nong seed.” Out of sheer frustration, I asked AI to build this tool. Honestly, who wouldn’t laugh when they see a Nong-style opening?

Here, “Nong seed” is a meme term: “Nong” is short for a streamer’s nickname. His playstyle is intentionally goofy/troll-like, and seeds that even he can casually clear are jokingly called “Nong seeds.”

It has grown into an offline seed research tool:

- Analyze a single seed in detail.
- Batch-search random seeds with Neow, BoneDice, final-result, Boss, Ancient, and event-queue filters.
- Import `progress.save` to approximate the user's unlock profile.
- Save valuable seeds to a local favorites database.
- Save relaxed search results as candidate pools and refine them later.

This is an external companion tool, not an in-game Workshop mod. It does not inject into the game process and does not modify game saves.

## Installation

### End users

Download the portable Windows package from GitHub Releases:

```text
RollTheSpire2_v2.1.0_win-x64.zip
```

Extract it and run:

```text
RollTheSpire2.exe
```

The release package is self-contained and may be large because it includes the .NET runtime. If double-clicking the exe gives no visible error, run `run_rollthespire2.bat` to keep a console window open for troubleshooting.

### Source build

Requirements:

- Windows 10/11 x64
- .NET 9 SDK
- Complete `data/` directory

From the repository root:

```bat
build_windows_wpf.bat
run_wpf.bat
```

The built executable is:

```text
RollWpf/bin/Release/net9.0-windows/RollTheSpire2.exe
```

## Usage

Typical WPF workflow:

1. Open `RollTheSpire2.exe`.
2. Choose character, ascension, run mode, and unlock profile on the configuration page.
3. Optionally import `progress.save`.
4. Analyze a single seed on the single-seed page.
5. Add batch-search filters such as Neow relics, BoneDice relics, curse blacklist, Boss, Ancient, or event queue.
6. Start searching and review hits on the results page.
7. Copy seeds, copy details, re-analyze a hit, favorite it, or save hits as a candidate pool.
8. For rare goals, save a relaxed pool first and continue filtering from that pool.

User-local generated files:

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
```

These files are ignored by Git by default.

## Feature overview

- Single-seed analysis for Neow, BoneDice, Neow relic effects, Boss, Ancient, event queue, shop/relic-related previews, and raw details.
- Batch seed filtering with random search and candidate-pool refinement.
- Process-oriented filters for exact Neow / BoneDice route requirements.
- Final-result filters for final cards, relics, curses, potions, and Neow relics.
- Duplicate-card count semantics for conditions such as three copies of `Claw`.
- Effective event queue filtering that accounts for Ancient event offset, `IsAllowed`, visited shared events, and hook replacement.
- Favorites database with tags, notes, hit explanation, and re-analysis.
- Candidate pool workflow for rare and extreme seeds.
- `progress.save` unlock-profile import.

## Data and version support

| Item | Current status |
| --- | --- |
| Target game line | Slay the Spire 2 v0.107.1+ RNG Rework |
| RNG target | MegaRandom / xoshiro256** compatible behavior |
| Default visible seed length | 10 characters |
| RollCore runtime data | `data/legacy/sts2_runtime_legacy_v2.json` |
| Source-extracted sidecar | `data/sts2_data.json` |
| Event rules/text | `data/event_rules.json`, `data/event_texts.json` |
| Entity index | `data/entity_index.json` |
| Unlock profile | `profiles/unlock_profile.json` |
| Favorites | `profiles/database/search_history.json` |
| Candidate pools | `profiles/database/candidate_pools.json` |

### Critical data note

The active RollCore runtime data file is:

```text
data/legacy/sts2_runtime_legacy_v2.json
```

The `legacy` folder name is historical. This file is not obsolete and must not be deleted. `data/sts2_data.json` is source-extracted sidecar data and must not replace the runtime data file.

The correct `config.json` value is:

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

See `docs/DATA_LAYOUT.md` for details.

## Project layout

```text
RollTheSpire2/
├─ RollCore/                 # prediction core, RNG compatibility, data loading, filters
├─ RollWpf/                  # WPF UI
├─ data/                     # runtime data and sidecar data
│  ├─ legacy/                # active RollCore runtime data
│  └─ README.md
├─ docs/                     # documentation
├─ profiles/                 # user-local profile/favorites/candidate data
├─ config.json               # default configuration
├─ build_windows_wpf.bat     # source build script
├─ run_wpf.bat               # source run script
├─ publish_windows_x64.bat   # portable Windows release script
└─ run_rollthespire2.bat     # optional release fallback launcher
```

The public mainline is WPF + RollCore. Old Web / WinForms / FastCore / extractor experimental lines are not included in the public stable package.

## Development

Common commands:

```bat
build_windows_wpf.bat
run_wpf.bat
```

Manual commands:

```powershell
dotnet restore RollWpf/RollWpf.csproj
dotnet build RollWpf/RollWpf.csproj -c Release
dotnet run --project RollWpf/RollWpf.csproj -c Release
```

Build a portable release package:

```bat
publish_windows_x64.bat
```

Output:

```text
publish/RollTheSpire2_v2.1.0_win-x64/
```

Zip that folder and upload it to GitHub Releases. Do not commit `publish/` to the repository.

## GitHub release workflow

Use the repository for source, data, docs, and scripts. Use GitHub Releases for compiled portable zip packages.

Do not commit:

```text
publish/
bin/
obj/
profiles/unlock_profile.json
profiles/database/*.json
profiles/database/*.tsv
```

## Maintainer

- Ocean False / @falseocean8

## Acknowledgements

- This project was developed with assistance from ChatGPT throughout the process of design, code organization, documentation, RNG logic calibration, and release preparation.
- Thanks to the Slay the Spire 2 community for discussions and verification samples related to RNG, event queues, Neow, Ancients, and seed mechanics.
- Thanks to Mega Crit for creating Slay the Spire 2. This project is an unofficial external tool and is not affiliated with Mega Crit.


## Contributing

Issues and pull requests are welcome. When reporting a prediction mismatch, include the game version, tool version, character, ascension, seed, unlock-profile status, expected result, and in-game result.

Any change to prediction logic, data files, or `config.json:data_file` should include regression notes. Do not replace `data/legacy/sts2_runtime_legacy_v2.json` with `data/sts2_data.json` unless a dedicated data-layout migration is implemented and tested.

## License

No formal `LICENSE` file is included yet. Until one is added, treat the project as unlicensed / all rights reserved.
