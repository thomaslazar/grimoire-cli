# `models` per-item layer

**Status:** approved
**Issue:** [#39](https://github.com/thomaslazar/grimoire-cli/issues/39)
**Verified against:** `hunterreadca/grimoire:1.7.1`, the pinned stack

## Problem

Second of the four collections to get a per-item layer, after
[maps](2026-09-16-maps-per-item-layer-design.md) settled the shape. The same
symptom applies: the cross-cutting commands all reach models — `duplicates`,
`tags items`, `search`, `files`, `library rescan --scope` — but nothing lists
them, so **the only way to set a tag on a model is
`duplicates merge-metadata --resource-type model --fields tags`**, copying it
off another model that already carries it.

Models ranks above tokens and audio because 1.6.2 shipped four model-specific
variant kinds — `presupported`, `unsupported`, `split`, `merged` — which
`duplicates link --resource-type model` already accepts and which are unusable
end to end, there being no way to list candidates. 3D-print libraries are also
the most duplicate-prone content a Grimoire instance holds: the same mini in
supported and unsupported cuts is the normal case.

## Verified server behaviour

Read from `backend/routers/models/core.py`, `_schemas.py` and `__init__.py` at
tag `v1.7.1`. Line cites are that tag.

### Endpoints and roles

| Method | Path | Role | Source |
|---|---|---|---|
| GET | `/api/models` | `require_not_guest` | `__init__.py:38` |
| GET | `/api/models/{model_id}` | `get_current_user` | `core.py:96` |
| PATCH | `/api/models/{model_id}` | `require_gm_or_admin` | `core.py:189` |
| POST | `/api/models/bulk` | `require_gm_or_admin` | `core.py:202` |
| POST | `/api/models/bulk/tags` | `require_gm_or_admin` | `core.py:217` |
| GET | `/api/models/{model_id}/thumbnail` | `get_current_user` | `core.py:164` |
| GET | `/api/model-folders` | `require_not_guest` | `__init__.py:47` |
| PATCH | `/api/model-folders` | `require_gm_or_admin` | `core.py:84` |
| POST | `/api/model-folders/bulk` | `require_gm_or_admin` | `core.py:228` |

Neither read carries a tag, per the same rule as maps.

### `Model3DUpdate` — 4 fields

`description`, `tags`, `is_explicit`, `is_supported` (`_schemas.py:11-20`).
`tags` validates via `dedupe_tags(v, validate=True)` (`_schemas.py:25`), so a
tag containing `/` or `\` is a 422, as everywhere else since 1.7.0.

### Measured facts

- **`is_supported` is a one-way trip.** The column is tri-state — true,
  false, or null meaning "the scanner could not tell" — but `update_model`
  applies `data.model_dump(exclude_none=True)` with **no** `model_fields_set`
  re-application (`core.py:195`). So `{"is_supported": null}` is dropped and the
  write answers `{"status": "ok"}` having changed nothing. A model can be moved
  from unknown to true or false and **never back**. This is maps' grid-clear
  asymmetry inverted: there `update_map` rescues a sent `0` deliberately
  (`maps/core.py:630-633`); here nothing rescues a sent `null`.
- **Read and write disagree about the same fact.** `Model3DOut` exposes the
  derived pair `is_presupported` / `is_unsupported` rather than the column
  (`_schemas.py:43-52, 66-67`), both false when unknown, because a single
  tri-state field renders as a badge for one case and nothing for the other two.
  The write path takes the single `is_supported`. So the field a caller reads is
  never the field it writes.
- **`GET /api/models` takes `limit` and `offset` only** (`core.py:32-33`), with
  `Query(100000)` and no `le=` — the same no-ceiling default as maps, tokens and
  audio. There is no folder or type filter, so the dual-paging split that makes
  a negative limit ambiguous on maps does not exist here: this endpoint only ever
  pages in SQL.
- **Explicit content is filtered server-side per account.** `list_models` drops
  `is_explicit` rows unless the account allows them (`core.py:37-40`), as
  `books list` does.
- **Variants never reach the list.** `variants.parents_only` is applied first
  (`core.py:38`).
- **`bulk_update_models` passes no `validate` hook** (`core.py:200-212`), like
  maps and unlike books. Only `"Model not found"` can reach `errors`; a
  schema-invalid item fails Pydantic on the envelope and 422s the whole batch
  with nothing written.
- **`thumbnail` 404s when no thumbnail was rendered** (`core.py:183`) — the file
  is looked up by a path derived from the model's name and hash, and a miss is a
  404 rather than a placeholder. `.stl` is the one format that renders one
  (`indexer/models3d.py:67`).
- **Folder tags read and write differently**, as on maps: `list_model_folders`
  resolves display casing via `folder_display_tags` (`core.py:76`), while the
  PATCH and the bulk both echo the stored internal keys (`core.py:91`,
  `core.py:237`).
- **`model-folders` has no delete**, and `upsert_folder_tags` inserts a row for
  any path string — so a folder-tag write to a path that was never on disk is
  unreachable afterwards. 1.7.1's `_purge_folders` only runs when a real
  directory is deleted.
- **Supported/unsupported is inferred folder-level**, not per file —
  `Goblins/Presupported/goblin_a.stl` (`indexer/media.py:349`). That is what the
  fixture tree has to mirror for the flag to be observable at all.

## Design

### 1. Command surface

```
models list [--limit <n>] [--offset <n>]
models get --id <id>
models update --id <id> {--input <f> | --stdin}          gm or admin
models batch-update {--input <f> | --stdin}              gm or admin
models batch-tag {--input <f> | --stdin}                 gm or admin
models thumbnail --id <id> --output <path|->
models folders list
models folders set {--input <f> | --stdin}               gm or admin
models folders batch-set {--input <f> | --stdin}         gm or admin
```

New `Commands/ModelsCommand.cs`, `Commands/ModelFolderCommands.cs` and
`Services/ModelsService.cs` — the maps layout exactly, per the one-file-per-
command-group rule.

Writes take JSON via `--input`/`--stdin`, validated by `JsonBodyInput.Validate`
against the generated model's own `GetFieldDeserializers()` keys. That is the
shape maps settled and CLAUDE.md now records; nothing here reopens it.

`--limit` uses `OptionHelpers.Range("--limit", …, 1)` with
`DefaultValueFactory = _ => 100`, matching what maps ended at: a floor of 1, no
ceiling (the server declares none), and the default rendered by `--help` rather
than described in prose.

**Builder access is `_client.Api.Api.Models`**, but the generated namespace is
`Api.ModelsRequests` — Kiota renamed it to avoid colliding with the
`Generated/Models/` DTO namespace. The call site is unaffected; only someone
looking for an `Api/Models` folder is.

`models thumbnail` mirrors `books thumbnail`: `--output <path|->`, writing bytes
to stdout for `-` and `{path, bytes}` for a path.

Exit 0 everywhere except `batch-update` and `batch-tag`, which return
`BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"))`.
`folders batch-set` returns 0 — the server loops and commits with no per-item
error list.

### 2. Help text

Five caveats port from the corrected maps text and are not re-derived here: the
`batch-update` 422 rule, explicit-null-is-a-no-op, the permanent folder row, the
folder-tag inheritance gap on `get`, and hidden variants. The 422 rule ports
without maps' numeric ranges — `Model3DUpdate` carries no bounded field, so what
trips the whole-batch 422 here is a malformed tag or a wrong-typed bool rather
than an out-of-range number. Two caveats are specific to models.

`models update`:

```
Notes:
  tags replace the set. To add without removing, use batch-tag.

  Clear description with ""; an explicit null does nothing.

  is_supported is one-way: a model whose support state is unknown can be set
  true or false, but nothing sets it back to unknown. null is dropped.

  Responds {"status": "ok"} and echoes nothing — read back with:
  grimoire-cli models get --id <id>
```

`models get`:

```
Notes:
  is_presupported and is_unsupported are derived from is_supported, which
  models update writes. Both false means unknown, not unsupported.
```

`models list` carries the paging note, the server-side explicit filter, and
hidden variants. `models thumbnail` carries the 404 and that `.stl` is the only
format that renders one.

### 3. Testing

Unit tests in `tests/GrimoireCli.Tests/Commands/ModelsCommandTests.cs`,
`ModelFolderCommandTests.cs` and `Services/ModelsServiceTests.cs`, mirroring the
maps trio — including the service-level test that goes through `ModelsService`
over a recording handler and pins method, URL and body bytes for every raw-body
call. That test exists on maps only because the final review asked for it; it is
part of the template now rather than an afterthought.

`docker/make-fixtures.py` gains an `--stl` mode: a minimal binary STL is an
80-byte header, a 4-byte triangle count and one 50-byte triangle. `docker/seed.sh`
writes a tree that mirrors the folder-level inference:

```
models/Goblins/Presupported/Goblin Archer.stl
models/Goblins/Unsupported/Goblin Archer.stl
```

so `is_presupported` and `is_unsupported` are both observable on real rows.

The smoke block covers what unit tests cannot:

- `models list` returns the seeded models; `--limit 1` returns one
- `models get` reports `folder_path`, `folder_tags` and the derived pair
- the seeded pair reads one `is_presupported` and one `is_unsupported`
- `models update` sets `is_supported`, and `models get` reads the derived pair
  flipped to match
- **`{"is_supported": null}` answers `{"status": "ok"}` and changes nothing** —
  the one-way claim, measured rather than asserted from source
- `models batch-tag` adds a tag and `tags items --tag <t> --resource-type model`
  finds it — the symptom this issue names, closed end to end
- `models folders set` then `models folders list` shows the tag in display casing
- an unknown field exits **1** (client-side refusal), distinct from a server 422's
  exit 2

Writes stay on the seeded model fixtures with fixed values. The one-way
`is_supported` is the block's idempotence hazard: the run must leave it at a
fixed state rather than toggling, since nothing can restore unknown.

## Documentation

- README Commands table gains nine rows.
- `tools/generate-api-coverage.py` gains nine `IMPLEMENTED` entries and the table
  regenerates; `models` goes 0/10 → 9/10.
- `docs/grimoire-api-notes.md` gains a `## Models` section: the one-way
  `is_supported`, the read/write field mismatch, the filterless list, and the
  thumbnail 404.
- `docs/roadmap.md` loses the models item and renumbers. As with maps, re-read
  the framing prose: the sharpest-symptom paragraph currently names tokens,
  models and audio, and models leaves that set.

## Out of scope

`GET /api/models/{model_id}/file`. It gets its own issue rather than dissolving
into [#44](https://github.com/thomaslazar/grimoire-cli/issues/44)'s "fold each
into whichever block is in flight", because that is precisely what is not
happening to it: the file getters for maps, models, tokens and audio are a
coherent group, none ships today, and a large-binary download has no shipped
precedent anywhere in the CLI (`books file` is still open in
[#48](https://github.com/thomaslazar/grimoire-cli/issues/48)). Tracked in
[#62](https://github.com/thomaslazar/grimoire-cli/issues/62), opened alongside
this spec, which groups the four `file` getters and names the three decisions
they share.
