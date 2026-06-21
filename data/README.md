# Data Directory

This directory contains all runtime and sidecar data used by **RollTheSpire2**.

## Critical runtime file

Do not delete or replace:

```text
data/legacy/sts2_runtime_legacy_v2.json
```

Despite the folder name, this file is the current RollCore runtime data file. The word `legacy` only refers to the historical runtime-data schema name.

The correct `config.json` value is:

```json
"data_file": "data/legacy/sts2_runtime_legacy_v2.json"
```

## Other data files

```text
data/sts2_data.json              # source-extracted sidecar data
data/event_rules.json            # event rule metadata
data/event_texts.json            # event display text / localization helper
data/entity_index.json           # entity name and ID index
data/neow_relic_effects.json     # Neow relic effect metadata
```

`data/sts2_data.json` cannot currently replace the runtime file. A data layout migration should be done as a separate version with regression testing.
