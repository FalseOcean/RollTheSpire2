# Profiles Directory

This directory stores user-generated local data at runtime.

Common files:

```text
profiles/unlock_profile.json
profiles/database/search_history.json
profiles/database/candidate_pools.json
```

These files are ignored by Git by default because they may contain local user settings, imported unlock information, favorites, notes, and candidate pools.

Only `profiles/.keep` and this README should normally be committed.
