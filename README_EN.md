# RollTheSpire2

`RollTheSpire2` is an external seed analysis and batch seed-searching tool for **Slay the Spire 2 v0.107.1+**.

It can simulate and filter starting results offline without launching the game. The tool focuses on Neow starts, Neow's Bones / Bone Dice routes, Neow relic effects, Bosses, Ancients, the effective event queue, the seed library, and coarse candidate pools. The project currently provides a WPF graphical interface suitable for single-seed analysis, batch seed rolling, saving good seeds, refining candidate pools, and importing `progress.save` unlock profiles.

Current stable version: `v2.1.0`. This release is organized from the prediction baseline of `v2.1.0-preview2e`, which has already been tested and verified. The public release cleanup only updates the project name, startup scripts, release scripts, and documentation.

## Table of Contents

* [Background](#background)
* [Installation](#installation)
* [Usage](#usage)
* [Feature Overview](#feature-overview)
* [Data and Version Support](#data-and-version-support)
* [Project Structure](#project-structure)
* [Development](#development)
* [GitHub Release Recommendation](#github-release-recommendation)
* [Maintainer](#maintainer)
* [Acknowledgements](#acknowledgements)
* [Contributing](#contributing)
* [License](#license)

## Background

This project started because I was rolling seeds by hand so much that my hand was about to fall off, yet I still could not find a “Nong seed”. Out of frustration, I asked AI to help build this tool. After all, who would not laugh when seeing a start that even Nong could casually win with?

Here is what it can do:

* Analyze a single seed in detail, including Neow starts, Bone Dice routes, Bosses, Ancients, event queues, and related relic results.
* Batch scan random seeds and filter them by conditions such as Neow, Bone Dice, final results, Bosses, Ancients, and event queues.
* Import `progress.save` so predictions can better match the player's own unlock progress.
* Save matched seeds into a local library and later search them by tags, notes, character, or match explanations.
* Save results from relaxed filters into a coarse candidate pool, then gradually add stricter conditions for refinement.

## Installation

### For Regular Users: Download the Release Build

Go to GitHub Releases and download:

```text
RollTheSpire2_v2.1.0_win-x64.zip
```

Extract it, then double-click:

```text
RollTheSpire2.exe
```

### Run / Build from Source

Building from source requires:

* Windows 10/11 x64
* .NET 9 SDK
* A complete `data/` directory

Run the following commands from the repository root:

```bat
build_windows_wpf.bat
run_wpf.bat
```

After a successful build, the development executable will be located at:

```text
RollWpf/bin/Release/net9.0-windows/RollTheSpire2.exe
```

You can also build it manually:

```powershell
dotnet restore RollWpf/RollWpf.csproj
dotnet build RollWpf/RollWpf.csproj -c Release
```

## Usage

### Common GUI Workflow

1. Open `RollTheSpire2.exe`.
2. On the “Config” page, select the character, ascension level, run mode, and unlock profile.
3. If you want predictions to match your own account unlock progress, import `progress.save`.
4. On the “Single Seed Analysis” page, enter a seed and inspect its starting prediction and route details.
5. On the “Batch Seed Search” page, add filter conditions such as Neow relics, Bone Dice relics, Bone Dice curse blacklist, Bosses, Ancients, or event queue requirements.
6. Click “Start Search”. Results will appear on the “Search Results” page.
7. For useful results, you can copy the seed, copy details, send it to single-seed analysis, save it to the library, or save it as a coarse candidate pool.
8. For very rare conditions, it is recommended to first search with relaxed filters, save a candidate pool, and then continue refining from that pool.

### Runtime User Files

The program may generate the following local files under `profiles/`:

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
```

These files are local user data. They are ignored by `.gitignore` by default and should not be committed to GitHub.

## Feature Overview

### Single-Seed Analysis

After entering a seed, the tool outputs predicted results including:

* Neow three-choice start.
* Direct Neow relic effects.
* Neow's Bones / Bone Dice routes.
* Route-related results for ScrollBoxes, Kaleidoscope, NewLeaf, LeafyPoultice, LostCoffer, and more.
* Act selection.
* Bosses / Ancients.
* Effective event queue.
* Shop relic / regular relic related information.
* Raw details and debug information.

### Batch Seed Search

Batch seed search supports both random searching and continuing from candidate pools. Commonly used conditions include:

* Neow three-choice options must include / exclude specified Neow relics.
* The two Neow relics given by Bone Dice must include specified relics.
* Bone Dice curse whitelist / blacklist.
* Final deck must include / exclude specified cards.
* Final regular relics must include / exclude specified relics.
* Final curse and potion conditions.
* Specified Act 1 / Act 2 / Act 3 Bosses.
* Specified Act 1 / Act 2 / Act 3 Ancients.
* Specified second Boss for Act 3 A10.
* The first N effective events in an Act must include / exclude specified events.
* Shop relic and regular relic sequence filters.

### Process-Oriented and Final-Result-Oriented Filtering

The tool separates two filtering styles:

* Process-oriented filtering: requires the starting route itself to satisfy the conditions. For example, Neow has Bone Dice, Bone Dice gives NewLeaf + LeafyPoultice, and the curse must not be Debt.
* Final-result-oriented filtering: ignores the exact source and only checks whether the final deck, regular relics, curses, potions, or Neow relics satisfy the conditions.

Important distinction:

```text
Neow relics: starting Neow relics such as NeowsBones, LargeCapsule, SmallCapsule, NewLeaf, LeafyPoultice, etc.
Final regular relics: regular relics generated by effects such as LargeCapsule / SmallCapsule.
```

If you want to search for “Bone Dice gives LargeCapsule + SmallCapsule”, use Neow relic conditions. If you want to search for “LargeCapsule generates Gambling Chip”, use final regular relic conditions.

### Duplicate Card Conditions

Some card conditions support quantity semantics. For example, entering `Claw` three times means the final result must contain at least three copies of `Claw`, not merely one. This is useful for extreme results such as three Claws from ScrollBoxes, or unusual NewLeaf / LeafyPoultice transformations.

### Effective Event Queue

Event filtering is based on the effective event queue, not just the raw event queue. It takes the following into account:

* At the beginning of each Act, entering the Ancient through an EventRoom causes `eventsVisited + 1` for that Act.
* Events with `IsAllowed=false` are skipped.
* `VisitedEventIds` prevents repeated events.
* `Hook.ModifyNextEvent` may replace the next event.

Therefore, it is closer to the actual order of question mark room encounters than directly reading the raw event list.

### Seed Library

The seed library is used to save valuable seeds. Each record may include:

* Seed
* Character
* Ascension level
* Tags
* Notes
* Match explanation
* Detailed prediction text
* RNG / tool version information

Records can be searched by seed, character, tag, note, or match explanation. Saved records can also be analyzed again.

### Coarse Candidate Pool

The coarse candidate pool is designed for extremely low-probability targets. The recommended workflow is:

```text
Search a batch of seeds with relaxed conditions
-> Save them as a candidate pool
-> Continue refining from the candidate pool with stricter conditions
-> Keep the final matches
```

Candidate pools are currently saved as local JSON files and do not depend on SQLite.

## Data and Version Support

| Item                            | Current Status                                      |
| ------------------------------- | --------------------------------------------------- |
| Target game version             | Slay the Spire 2 v0.107.1+ RNG Rework line          |
| RNG compatibility target        | MegaRandom / xoshiro256** compatible implementation |
| Default seed length             | 10-character in-game visible seed                   |
| Main runtime data               | `data/legacy/sts2_runtime_legacy_v2.json`           |
| Source-extracted auxiliary data | `data/sts2_data.json`                               |
| Event rules / text              | `data/event_rules.json`, `data/event_texts.json`    |
| Entity index                    | `data/entity_index.json`                            |
| User unlock profile             | `profiles/unlock_profile.json`                      |
| Seed library                    | `profiles/database/search_history.json`             |
| Coarse candidate pools          | `profiles/database/candidate_pools.json`            |

### Important Data Notes

The current main runtime data used by RollCore is:

```text
data/legacy/sts2_runtime_legacy_v2.json
```

Here, `legacy` is a historical naming artifact. It means “the main RollCore runtime data format”. It does not mean the data is outdated, and this directory must not be deleted.

`data/sts2_data.json` is auxiliary data extracted from source code and cannot directly replace the main runtime data. The correct current configuration must remain:

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

For more details, see:

```text
docs/DATA_LAYOUT.md
```

## Project Structure

```text
RollTheSpire2/
├─ RollCore/                 # Pure prediction core, RNG compatibility implementation, data loading, filtering logic
├─ RollWpf/                  # WPF graphical interface
├─ data/                     # Runtime data, source-extracted auxiliary data, event rules, entity index
│  ├─ legacy/                # Current RollCore main runtime data; must not be deleted
│  └─ README.md
├─ docs/                     # Current project documentation
├─ profiles/                 # Local user profiles / library / candidate pools; personal data is not committed by default
├─ config.json               # Default configuration
├─ build_windows_wpf.bat     # Source build script
├─ run_wpf.bat               # Source run script
├─ publish_windows_x64.bat   # Generates a portable Windows release package
└─ run_rollthespire2.bat     # Backup launcher for release packages
```

The current public package has removed the old Web / WinForms / FastCore / extractor experimental lines. The public mainline is WPF + RollCore.

## Development

Common development commands:

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

Generate the release package for regular users:

```bat
publish_windows_x64.bat
```

Output directory:

```text
publish/RollTheSpire2_v2.1.0_win-x64/
```

This directory should be compressed and uploaded to GitHub Releases. It should not be committed directly into the repository.

## GitHub Release Recommendation

For GitHub Releases, upload the compressed portable package:

```text
RollTheSpire2_v2.1.0_win-x64.zip
```

The release title can be:

```text
RollTheSpire2 v2.1.0
```

Suggested release description:

```text
RollTheSpire2 v2.1.0 is the first stable public release of the WPF + RollCore mainline.

This version supports offline single-seed analysis and batch seed searching for Slay the Spire 2 v0.107.1+, with major support for Neow starts, Neow's Bones / Bone Dice routes, Neow relic effects, Bosses, Ancients, effective event queues, seed library records, candidate pools, and progress.save unlock profile import.

This release is based on the tested v2.1.0-preview2e prediction baseline. The public release cleanup only updates the project name, startup scripts, release scripts, and documentation.
```

## Maintainer

* Ocean False / @falseocean8

## Acknowledgements

* This project used ChatGPT as an assistant during design, code organization, documentation, RNG logic calibration, and release preparation.
* Thanks to the Slay the Spire 2 community for discussions and verification samples related to RNG, event queues, Neow, Ancients, and seed mechanics.
* Thanks to Mega Crit for creating Slay the Spire 2. This project is an unofficial external tool and is not affiliated with Mega Crit.

## Contributing

Issues and Pull Requests are welcome. When contributing or reporting bugs, please include:

* The game version used.
* The tool version used.
* Character, ascension level, seed, and whether `progress.save` was imported.
* Expected result and actual in-game result.
* If the issue involves event queues, Bosses, Ancients, or Neow routes, please provide minimal reproduction steps if possible.

If you modify prediction logic, data files, or `config.json:data_file`, please include an explanation.

## License

This repository does not currently include a formal `LICENSE` file. Before public release, it is recommended to choose a license explicitly. Until a license is added, the project should be treated as unlicensed / all rights reserved.
