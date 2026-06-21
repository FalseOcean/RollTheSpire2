# Candidate Pool Workflow

The candidate pool is a lightweight coarse-filter database.

Purpose:

```text
relaxed filter -> save candidate seeds -> add stricter filters -> continue searching inside the pool
```

It is useful for extremely rare seeds, where a single strict search may take too long or give no feedback.

## Design principle

Candidate pools store only seed lists and minimal metadata. They do not store full prediction details, raw event queues, or long analysis text.

This keeps memory and disk usage low even for large pools.

## Files

```text
profiles/database/candidate_pools.json
```

This is user-generated local data and should not be committed to Git.
