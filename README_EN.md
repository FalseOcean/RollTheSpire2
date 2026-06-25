# RollTheSpire2

`RollTheSpire2` is an external WPF seed analysis and batch seed-searching tool for the **Slay the Spire 2 v0.107.1+ RNG Rework line**.

It can simulate opening results offline without launching the game, and it can batch-filter seeds by user-defined conditions. The current focus includes Neow starts, Neow's Bones / Bone Dice routes, Neow relic effects, Bosses, Ancients, the effective event queue, the seed library, coarse candidate pools, and `progress.save` unlock-profile import.

> `RollTheSpire2` is an external companion tool, not a Workshop Mod. It does not inject into the game process, does not modify save files, and does not require the game to be running.

## Current Version

| Item | Status |
| --- | --- |
| Current public stable | `v2.1.1` |
| Previous public stable | `v2.1.0` |
| Target game version | `Slay the Spire 2 v0.107.1+`, mainline after the RNG Rework |
| Main runtime data | `data/legacy/sts2_runtime_legacy_v2.json` |

`v2.1.1` is a prediction-correctness maintenance patch based on the `v2.1.0` stable baseline. Its main fix is the `LeadPaperweight` colorless-card rarity model: the old model incorrectly treated it as `NoRare`, allowing only Common / Uncommon cards. The corrected model follows the colorless `RegularEncounter` reward path and can generate Rare colorless cards where the game odds permit it.

## Table of Contents

- [Background](#background)
- [Installation](#installation)
- [Usage](#usage)
- [Feature Overview](#feature-overview)
- [Data and Version Support](#data-and-version-support)
- [Documentation and Maintenance](#documentation-and-maintenance)
- [Project Structure](#project-structure)
- [Build from Source](#build-from-source)
- [Maintainer](#maintainer)
- [Acknowledgements](#acknowledgements)
- [Contributing](#contributing)
- [License](#license)

## Background

This project started from a practical frustration: manually rolling seeds for so long that my hand was about to fall off, while still failing to find the desired “Nong seed”. With AI assistance, that frustration became a tool. Over time, it grew from a Neow-opening previewer into a full offline seed research tool for STS2.

It answers questions such as:

- Given a seed, what opening results will I see?
- Does the Neow three-choice offer contain the target relic?
- What does Neow's Bones / Bone Dice generate, and can the curse avoid Debt?
- Do the Bosses, Ancients, or event queue match my target?
- Can I batch-search seeds, save a coarse pool, and refine it later?

## Installation

### Regular Users

Download the latest Windows x64 release package from GitHub Releases, for example:

```text
RollTheSpire2_v<version>_win-x64.zip
```

Extract the zip and run:

```text
RollTheSpire2.exe
```

Release builds are usually self-contained portable zip packages. They can be large because they include the .NET runtime, allowing regular users to run the tool without installing the .NET SDK or Runtime.

If double-clicking the exe shows no visible window, try the fallback launcher:

```text
run_rollthespire2.bat
```

It keeps a console window open, which helps diagnose startup errors.

### Run from Source

Running from source requires:

- Windows 10/11 x64
- .NET 9 SDK
- A complete `data/` directory

Run the following commands from the repository root:

```bat
build_windows_wpf.bat
run_wpf.bat
```

## Usage

### Common GUI Workflow

1. Open `RollTheSpire2.exe`.
2. On the Config page, select the character, ascension level, run mode, and unlock profile.
3. If you want predictions to match your own account unlock progress, import `progress.save`.
4. On the Single Seed Analysis page, enter a seed and inspect its opening prediction and route details.
5. On the Batch Seed Search page, add filter conditions such as Neow relics, Bone Dice relics, Bone Dice curse blacklist, Bosses, Ancients, or event queue requirements.
6. Click Start Search. Results will appear on the Search Results page.
7. For useful hits, you can copy the seed, copy details, send it to single-seed analysis, save it to the library, or save it as a coarse candidate pool.
8. For very rare conditions, first search with relaxed filters, save a candidate pool, and continue refining from that pool.

### Runtime User Files

The program may generate the following local files under `profiles/`:

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
```

These are local user data files. They are ignored by `.gitignore` by default and should not be committed to GitHub.

## Feature Overview

### Single-Seed Analysis

After entering a seed, the tool outputs predicted results including:

- Seed normalization and in-game visible seed normalization.
- Character, ascension, single-player / multiplayer parameters.
- `progress.save` unlock effects.
- Neow three-choice start.
- Direct Neow relic effects.
- Neow's Bones / Bone Dice routes.
- BoneDice curse.
- LargeCapsule / SmallCapsule random relic generation.
- NewLeaf / LeafyPoultice transforms.
- Route-related results for ScrollBoxes, Kaleidoscope, LostCoffer, LeadPaperweight, HeftyTablet, NeowsTorment, and more.
- Act selection.
- Bosses / Ancients.
- Event raw queue / effective queue.
- Shop-exclusive relic sequence.
- Map / Boss block information.
- Raw details and debug information.

### Batch Seed Search

Batch seed search supports random search, sequential enumeration, and continuing from candidate pools. Commonly used conditions include:

- Neow three-choice options must include / exclude specified Neow relics.
- The two Neow relics given by Bone Dice must include specified relics.
- Bone Dice curse whitelist / blacklist.
- Specific NewLeaf / LeafyPoultice / ScrollBoxes / Kaleidoscope / LostCoffer / LeadPaperweight results.
- Final deck must include / exclude specified cards.
- Final regular relics must include / exclude specified relics.
- Final Neow relic, curse, and potion conditions.
- Specified Act 1 / Act 2 / Act 3 Bosses.
- Specified Act 1 / Act 2 / Act 3 Ancients.
- Specified second Boss for Act 3 A10.
- The first N effective events in an Act must include / exclude specified events.
- Shop relic and regular relic sequence filters.

### Process-Oriented and Final-Result-Oriented Filtering

The tool separates two filtering styles:

- **Process-oriented filtering**: requires the opening route itself to satisfy the conditions. For example, Neow has Bone Dice, Bone Dice gives NewLeaf + LeafyPoultice, and the curse must not be Debt.
- **Final-result-oriented filtering**: ignores the exact source and only checks whether the final deck, regular relics, curses, potions, or Neow relics satisfy the conditions.

Important distinction:

```text
Neow relics: starting Neow relics such as NeowsBones, LargeCapsule, SmallCapsule, NewLeaf, LeafyPoultice, etc.
Final regular relics: regular relics generated by effects such as LargeCapsule / SmallCapsule.
```

If you want to search for “Bone Dice gives LargeCapsule + SmallCapsule”, use Neow relic conditions. If you want to search for “LargeCapsule generates Gambling Chip”, use final regular relic conditions.

### Duplicate Card Conditions

Some card conditions support quantity semantics. For example, entering `Claw` three times means the final result must contain at least three copies of `Claw`, not merely one. This is useful for extreme results such as three Claws from ScrollBoxes or unusual NewLeaf / LeafyPoultice transformations.

### Effective Event Queue

Event filtering is based on the effective event queue, not just the raw event queue. It takes the following into account:

- At the beginning of each Act, entering the Ancient through an EventRoom causes `eventsVisited + 1` for that Act.
- Events with `IsAllowed=false` are skipped.
- `VisitedEventIds` prevents repeated events.
- `Hook.ModifyNextEvent` may replace the next event.

Therefore, it is closer to the actual order of question mark room encounters than directly reading the raw event list.

### Seed Library

The seed library is used to save valuable seeds. Each record may include a seed, character, ascension level, tags, notes, hit explanation, detailed prediction text, and tool version information. Saved records can be searched and analyzed again.

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

| Item | Current Status |
| --- | --- |
| Target game version | Slay the Spire 2 v0.107.1+ RNG Rework line |
| RNG compatibility target | MegaRandom / xoshiro256\*\* compatible implementation |
| Default seed length | 10-character in-game visible seed |
| Main runtime data | `data/legacy/sts2_runtime_legacy_v2.json` |
| Source-extracted auxiliary data | `data/sts2_data.json` |
| Event rules / text | `data/event_rules.json`, `data/event_texts.json` |
| Entity index | `data/entity_index.json` |
| User unlock profile | `profiles/unlock_profile.json` |
| Seed library | `profiles/database/search_history.json` |
| Coarse candidate pools | `profiles/database/candidate_pools.json` |

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
docs/KNOWLEDGE_BASE/DATA_LAYOUT.md
```

## Documentation and Maintenance

The project uses a layered documentation structure so that source audits, long-term facts, architecture decisions, and version history do not get mixed into the README.

```text
CURRENT_STATUS.md                 current development baseline / source of truth
docs/README.md                    documentation index
docs/KNOWLEDGE_MANAGEMENT.md      documentation workflow
docs/source_audits/               source-audit evidence or migration-period audit summaries
docs/KNOWLEDGE_BASE/              long-term stable facts
docs/ADR/                         architecture decision records
docs/postmortems/                 bug attribution and postmortems
CHANGELOG.md                      version history
```

Prediction-logic changes involving RNG, pools, order, ascension, unlocks, or route state should update source audits, Knowledge Base, and ADRs. Documentation cleanup must not modify `RollCore/`, `RollWpf/`, `data/`, or `config.json`.

## Project Structure

```text
RollTheSpire2/
├─ RollCore/                 # Pure prediction core, RNG compatibility implementation, data loading, filtering logic
├─ RollWpf/                  # WPF graphical interface
├─ data/                     # Runtime data, source-extracted auxiliary data, event rules, entity index
│  ├─ legacy/                # Current RollCore main runtime data; must not be deleted
│  └─ README.md
├─ docs/                     # Documentation, Knowledge Base, ADRs, source audits, postmortems
├─ profiles/                 # Local user profiles / library / candidate pools; personal data is not committed by default
├─ CURRENT_STATUS.md         # Current development baseline
├─ config.json               # Default configuration
├─ build_windows_wpf.bat     # Source build script
├─ run_wpf.bat               # Source run script
├─ publish_windows_x64.bat   # Generates a portable Windows release package
└─ run_rollthespire2.bat     # Backup launcher for release packages
```

The current public package has removed the old Web / WinForms / FastCore / extractor experimental lines. The public mainline is WPF + RollCore.

## Build from Source

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

Generate a portable Windows package:

```bat
publish_windows_x64.bat
```

## Maintainer

* Ocean False / @falseocean8

## Acknowledgements

- This project used ChatGPT as an assistant during design, code organization, documentation, RNG logic calibration, and release preparation.
- Thanks to the Slay the Spire 2 community for discussions and verification samples related to RNG, event queues, Neow, Ancients, and seed mechanics.
- Thanks to Mega Crit for creating Slay the Spire 2. This project is an unofficial external tool and is not affiliated with Mega Crit.

## Contributing

Issues and Pull Requests are welcome. When contributing or reporting bugs, please include:

- The game version used.
- The tool version used.
- Character, ascension level, seed, and whether `progress.save` was imported.
- Expected result and actual in-game result.
- If the issue involves event queues, Bosses, Ancients, or Neow routes, please provide minimal reproduction steps if possible.

If you modify prediction logic, data files, or `config.json:data_file`, please include an explanation and update the relevant documentation:

- `docs/source_audits/`
- `docs/KNOWLEDGE_BASE/`
- `docs/ADR/`
- `CHANGELOG.md`
- `CURRENT_STATUS.md`

## License

This repository does not currently include a formal `LICENSE` file. Before public release, it is recommended to choose a license explicitly. Until a license is added, the project should be treated as unlicensed / all rights reserved.
