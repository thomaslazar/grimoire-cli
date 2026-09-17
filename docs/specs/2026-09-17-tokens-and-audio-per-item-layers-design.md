# `tokens` and `audio` per-item layers

**Status:** approved
**Issues:** [#40](https://github.com/thomaslazar/grimoire-cli/issues/40), [#41](https://github.com/thomaslazar/grimoire-cli/issues/41)
**Verified against:** `hunterreadca/grimoire:1.7.1`, the pinned stack

## Problem

The last two of the four collections, after
[maps](2026-09-16-maps-per-item-layer-design.md) settled the shape and
[models](2026-09-17-models-per-item-layer-design.md) proved the port. Same
symptom on both: the cross-cutting commands reach them, but nothing lists them,
so **the only way to set a tag on a token or an audio track is
`duplicates merge-metadata --resource-type <t> --fields tags`**, copying it off
another item that already carries it.

They are batched because the port is mechanical and the risk was front-loaded
into models, where four Important findings surfaced and were fixed. Both
collections inherit that corrected template.

## Verified server behaviour

Read from `backend/routers/tokens/` and `backend/routers/audio/` at tag
`v1.7.1`. Line cites are that tag.

### Endpoints and roles

Identical shape per collection. `tokens`:

| Method | Path | Role | Source |
|---|---|---|---|
| GET | `/api/tokens` | `require_not_guest` | `__init__.py:38` |
| GET | `/api/tokens/{token_id}` | `get_current_user` | `core.py:95` |
| GET | `/api/tokens/{token_id}/thumbnail` | `get_current_user` | `core.py:167` |
| PATCH | `/api/tokens/{token_id}` | `require_gm_or_admin` | `core.py:192` |
| POST | `/api/tokens/bulk` | `require_gm_or_admin` | `core.py:205` |
| POST | `/api/tokens/bulk/tags` | `require_gm_or_admin` | `core.py:224` |
| GET | `/api/token-folders` | `require_not_guest` | `__init__.py:47` |
| PATCH | `/api/token-folders` | `require_gm_or_admin` | `core.py:83` |
| POST | `/api/token-folders/bulk` | `require_gm_or_admin` | `core.py:235` |

`audio` is the same nine with `audio`/`audio-folders` in place of
`tokens`/`token-folders`, and `artwork` in place of `thumbnail`:
`__init__.py:45` and `:54` carry `require_not_guest`; `core.py:99` (`get`),
`:147` (`artwork`), `:176` (`update`), `:189` (`bulk`), `:204` (`bulk/tags`),
`:87` (folder PATCH), `:215` (folder bulk).

Neither collection's reads carry a tag, per the same rule as maps and models.

### Update models

- `TokenUpdate`: `description`, `tags`, `is_explicit` — models minus
  `is_supported`.
- `AudioUpdate`: `description`, `tags` — the thinnest of the four.

Both validate `tags` with `dedupe_tags(v, validate=True)`, so a tag containing
`/` or `\` is a 422, as everywhere since 1.7.0.

### Facts that port unchanged from models

Verified present in both collections, not assumed:

- **`limit` defaults to 100000 with no ceiling** — `Query(100000)`, no `le=`
  (`tokens/core.py:29`, `audio/core.py:52`). No filters beyond paging on either,
  so both only ever page in SQL.
- **Neither `bulk_update_*` passes a `validate` hook**
  (`tokens/core.py:203-220`, `audio/core.py:187-200`). Only an unresolved id
  reaches `errors`; a schema-invalid item 422s the whole batch with nothing
  written. The books wording does not apply.
- **An explicit `null` is a silent no-op** — both updates use
  `model_dump(exclude_none=True)` with no `model_fields_set` re-application
  (`tokens/core.py:198`, `audio/core.py:182`). `""` clears a string field.
- **Variants never reach either list** (`variants.parents_only`).
- **Folder tags read and write differently**: display casing on the read
  (`tokens/core.py:71`, `audio/core.py:79`), stored internal keys echoed by the
  PATCH and the bulk (`tokens/core.py:90` and `:244`, `audio/core.py:94` and
  `:224`). Neither `token-folders` nor `audio-folders` has a delete, and
  `upsert_folder_tags` inserts a row for any path string — so a folder-tag write
  to a path that was never on disk is unreachable afterwards.
- **`get` resolves `folder_tags` by exact `folder_path` match**
  (`tokens/core.py:123`, `audio/core.py:112`), so a tag on a parent folder
  reaches `tags items` and `search` but reads empty on an item in a subfolder.

### Where the two diverge

These are the claims a blind port would get wrong.

- **`tokens` filters explicit content server-side per account**
  (`core.py:34-37`), as `books` and `models` do. **`audio` has no
  `is_explicit` at all** — not on the row, not on `AudioUpdate`, and
  `list_audio` (`core.py:51-54`) takes only `limit`, `offset` and the session.
  The explicit-filter line must not appear on `audio list`.
- **`tokens` orders by `relative_path`**, so a page is a contiguous run of
  folders in display order (`core.py:41`, with the same reasoning as
  `list_maps`). `audio` orders by `filename` (`core.py:58`).
- **`audio` carries four scan-derived, unwritable fields.** `duration`,
  `title`, `artist` and `album` are read from the file's tags at index time
  (`_serialize`, `core.py:38-40`, each `a.title or ""`), and `AudioUpdate`
  accepts none of them. A caller reading `artist` in a response will reasonably try
  to PATCH it; the generated request sample shows what *can* be sent but not
  where those came from.
- **`audio artwork` resolves three sources in order, then 404s.** A cover set
  deliberately through the UI wins, then folder art, then embedded album art
  (`core.py:145-170`, the ordering commented as issue #286). `has_artwork` in
  the list output says whether any of the three exists. `tokens thumbnail` by
  contrast is one rendered file or a 404.
- **`AudioOut` also carries `has_cover`**, which is true only when a cover was
  set deliberately. Nothing in this change reads or writes that cover — see
  Out of scope.

## Design

### 1. Command surface

```
tokens list [--limit <n>] [--offset <n>]        audio list [--limit <n>] [--offset <n>]
tokens get --id <id>                            audio get --id <id>
tokens thumbnail --id <id> --output <path|->    audio artwork --id <id> --output <path|->
tokens update --id <id> {--input|--stdin}       audio update --id <id> {--input|--stdin}
tokens batch-update {--input|--stdin}           audio batch-update {--input|--stdin}
tokens batch-tag {--input|--stdin}              audio batch-tag {--input|--stdin}
tokens folders list|set|batch-set               audio folders list|set|batch-set
```

Every write is `gm or admin`; no read carries a tag.

Six new files: `Commands/TokensCommand.cs`, `Commands/TokenFolderCommands.cs`,
`Services/TokensService.cs`, and the same trio for audio. That is the models
layout twice, per the one-file-per-command-group rule. **No shared abstraction
across the four collections** — four near-identical groups is the established
shape here, and collapsing them is explicitly not the convention.

Writes take JSON via `--input`/`--stdin`, validated by `JsonBodyInput.Validate`
against the generated model's own `GetFieldDeserializers()` keys. `--limit` uses
`OptionHelpers.Range(…, 1)` with `DefaultValueFactory = _ => 100` — floor of 1,
no ceiling, default rendered natively by `--help`.

`tokens thumbnail` and `audio artwork` mirror `books thumbnail`:
`--output <path|->`, bytes to stdout for `-`, `{path, bytes}` for a path, via
`SendStreamAsync` and `ConsoleOutput.WriteStreamAsync`.

Exit 0 everywhere except `batch-update` and `batch-tag`, which return
`BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"))`.
`folders batch-set` returns 0.

### 2. Help text

Six caveats port from the corrected models text and are not re-derived: the
`batch-update` 422 rule, the `/`-and-`\` tag rule on both batch verbs,
explicit-null-is-a-no-op, the permanent folder row, the folder-tag inheritance
gap on `get`, and hidden variants plus the paging note on `list`.

Collection-specific:

- `tokens list` carries the server-side explicit filter. **`audio list` must
  not** — it has no such field.
- `audio update`: "title, artist, album and duration are read from the file's
  tags and cannot be set here."
- `audio artwork`: the three-source resolution order and the 404, and that
  `has_artwork` in `audio list` says whether any source exists.
- `tokens thumbnail`: 404 when `has_thumbnail` is false.

### 3. Testing

Unit tests mirror the models trio per collection — command tests for flags,
role tags and the Notes claims; service tests for route shapes plus the
service-level raw-body test that goes through the service over a recording
handler. **Both service test classes carry `[Collection("NLog")]`**: they send
through `GrimoireApiClient`'s pipeline, `DebugHttpHandler` writes into the
global NLog target that `DebugHttpHandlerTests` counts, and omitting the
attribute passes locally and fails in CI — which is exactly what happened on
the maps branch.

`ResponseShapeExitCodeAlignmentTests` gains four rows (two batch verbs × two
collections), since it enumerates bulk commands by hand.

`docker/make-fixtures.py` gains a `--wav` mode. Python's stdlib `wave` module
writes a valid short silent file, so this needs no new dependency — the same
approach as `--stl`. A tagless WAV indexes with a real `duration` and empty
`title`/`artist`/`album`, which makes the unwritable-metadata caveat directly
assertable. Tokens reuse the existing `--png` mode.

Fixture trees, each with a subfolder so the folder-tag inheritance gap is
observable:

```
tokens/Monsters/Goblin.png
tokens/Monsters/Undead/Skeleton.png
audio/Ambience/Tavern.wav
audio/Ambience/Battle/Drums.wav
```

The smoke block covers, per collection: list and `--limit`, `get`'s folder
context, the image getter writing real bytes, `batch-tag` followed by
`tags items --resource-type <t>` — the symptom, closed end to end — folder tags
round-tripping in display casing, and an unknown field refused **client-side at
exit 1**, distinct from a server 422's exit 2. For audio additionally: that
`duration` is populated while `title` is empty, and that `audio update` refuses
`artist` client-side.

**Assertions are pinned to fixture filenames, never to a flag alone.** A
flag-only `select(...) | length >= 1` over a list captured before the run's own
write stops discriminating silently from the second run — the fault that had to
be fixed twice on the models branch.

Writes stay on the seeded fixtures with fixed values, so the suite converges on
a re-run.

## Documentation

- README Commands table gains eighteen rows.
- `tools/generate-api-coverage.py` gains eighteen `IMPLEMENTED` entries and the
  table regenerates; `tokens` 0/10 → 9/10 and `audio` 0/14 → 9/14.
- `docs/grimoire-api-notes.md` gains a `## Tokens and audio` section: the
  explicit asymmetry, audio's unwritable scan-derived metadata, the artwork
  resolution order, and the two orderings. Facts already recorded under
  `## Models` are cross-referenced rather than restated.
- `docs/roadmap.md` loses both items and renumbers. The framing prose above
  `## Next` names tokens and audio as the collections still short of
  `list`/`get`/`update`, and the sharpest-symptom paragraph names them too —
  with all four shipped, both need re-aiming, and the objective may now read as
  met rather than pending. Re-read the whole file; record intent, never status.
- CLAUDE.md's reset recipe gains `docker/library/tokens` and
  `docker/library/audio`.

## Out of scope

- **Audio's cover block** — `GET`/`POST`/`DELETE /api/audio/{id}/cover` and
  `POST /api/audio/{id}/cover/from-source`. Three are a port of the shipped
  `systems cover get|upload|delete`, but `from-source` copies an image across
  resource types on a `{source_type, source_id}` body and has no precedent —
  and [#44](https://github.com/thomaslazar/grimoire-cli/issues/44) already parks
  the identical `systems cover from-source`. Tracked in
  [#64](https://github.com/thomaslazar/grimoire-cli/issues/64), opened alongside
  this spec, which groups audio's four cover verbs with that one and names the
  `source_type` vocabulary as the decision they share.
- **The `file` getters** for both collections, tracked in
  [#62](https://github.com/thomaslazar/grimoire-cli/issues/62).
