# Data Layout

This project currently uses multiple data files. They are not interchangeable.

## Active RollCore runtime data

```text
data/legacy/sts2_runtime_legacy_v2.json
```

This is the main data file used by RollCore prediction logic.

The folder name `legacy` is a historical naming issue. It means the older RollCore runtime-data schema, not obsolete game data. This file is required and must not be deleted.

The correct `config.json` entry is:

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

## Source-extracted sidecar data

```text
data/sts2_data.json
```

This file is generated from source extraction and is useful for data status, auditing, and future migration work. It currently does not replace the runtime data file.

## Event and text data

```text
data/event_rules.json
data/event_texts.json
data/entity_index.json
data/neow_relic_effects.json
```

These support event encyclopedia, tooltips, display names, entity search, and Neow relic effect explanations.

## User-generated data

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
```

These files are created locally by the tool and are ignored by Git by default.

## Public cleanup rule

When preparing a public package, do not change these unless doing a dedicated tested data-layout migration:

```text
config.json
data/
RollCore/
RollWpf/
```

Documentation cleanup should not alter prediction inputs.
