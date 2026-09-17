# `maps` per-item layer

**Status:** approved
**Issue:** [#38](https://github.com/thomaslazar/grimoire-cli/issues/38)
**Verified against:** `hunterreadca/grimoire:1.7.0`, the pinned stack

## Problem

Grimoire holds five collections. The cross-cutting commands already reach all
of them — `duplicates` and `tags items` take every resource type, `search`
returns their hits with ids, `files` manages their trees, `library rescan
--scope` reaches every section. Only `list`/`get`/`update` stop at books.

The sharpest symptom: **the only way to set a tag on a map today is
`duplicates merge-metadata --resource-type map --fields tags`**, copying it off
another map that already carries it. When the sanctioned path to a basic
operation is a duplicate-resolution command, the group is missing.

Maps goes first of the four. It has the richest update model — grid geometry,
which the other three have nothing comparable to — and the only list filters
beyond paging, so it settles the shape that tokens, models and audio then port.

## Verified server behaviour

Read from `backend/routers/maps/core.py`, `_schemas.py` and `__init__.py`, and
`backend/services/bulk_service.py`, at tag `v1.7.0`. Line cites are that tag.

### Endpoints and roles

| Method | Path | Role | Source |
|---|---|---|---|
| GET | `/api/maps` | `require_not_guest` | `__init__.py:47` |
| GET | `/api/maps/{map_id}` | `get_current_user` | `core.py:164` |
| PATCH | `/api/maps/{map_id}` | `require_gm_or_admin` | `core.py:620` |
| POST | `/api/maps/bulk` | `require_gm_or_admin` | `core.py:650` |
| POST | `/api/maps/bulk/tags` | `require_gm_or_admin` | `core.py:665` |
| GET | `/api/map-folders` | `require_not_guest` | `__init__.py:56` |
| PATCH | `/api/map-folders` | `require_gm_or_admin` | `core.py:152` |
| POST | `/api/map-folders/bulk` | `require_gm_or_admin` | `core.py:676` |

Neither read carries a tag: `require_not_guest` is the documented default and
`get_current_user` is weaker still. Every write is `gm or admin`.

### `MapUpdate` — 7 fields

`description`, `tags`, `map_type`, `grid_size`, `grid_width`, `grid_height`,
`grid_px` (`_schemas.py:10-20`). Ranges are declared on the model:
`grid_width` and `grid_height` are `ge=0, le=1000`, `grid_px` is `ge=0, le=2000`
(`_schemas.py:18-20`).

`tags` validates as of 1.7.0 — `dedupe_tags(v, validate=True)`
(`_schemas.py:25`) — so a tag containing `/` or `\` is a 422. Same on
`FolderTagsUpdate` (`_schemas.py:50`).

### Measured facts

- **`limit` defaults to 100000 and has no ceiling.** `Query(100000)`
  (`core.py:85`), with no `le=`. Unflagged, `GET /api/maps` returns the whole
  library. `books` declares `Query(100, le=500)` by contrast, so the CLI's
  `books list --limit` default of 100 is pass-through, not an invention.
  `tokens`, `models` and `audio` all declare `Query(100000)` too, so whatever
  is decided here lands on all four.
- **Paging is implemented twice, and `--folder` picks which.** Without
  `folder`, the server pages in SQL: `q.offset(offset).limit(limit)`
  (`core.py:114`). With it, the whole subtree is materialised and sliced in
  Python: `filtered[offset : offset + limit]` (`core.py:111`). A negative limit
  therefore means two different things — SQLite reads `LIMIT -1` as unlimited,
  while the Python slice `filtered[0:-1]` drops the last row.
- **`folder` is an exact match, not a subtree.** The SQL prefix filter only
  narrows what is materialised; membership is decided by
  `_folder_path(m.relative_path) == folder` (`core.py:109`). So
  `folder=battlemaps` excludes `battlemaps/caves`.
- **Variants never reach the list.** `variants.parents_only` is applied before
  any filter (`core.py:91`).
- **A grid override is cleared by sending `0`, and only `update` honours it.**
  The validator normalises `0` to `None` (`_schemas.py:27-34`, `round(v, 2) or
  None`), which `exclude_none=True` would then swallow. `update_map` re-applies
  the clear from `model_fields_set` (`core.py:630-633`); `bulk_update_maps`
  does not — it dumps with `exclude_none=True` and no re-application
  (`core.py:658`). **A batch can set a grid, never clear one.**
- **`PATCH /api/maps/{id}` answers `{"status", "grid_warning"}`.** The warning
  rides along when a saved override looks implausible for the map's pixel
  dimensions, and is computed only when both `grid_width` and `grid_height` are
  truthy after the write (`core.py:639-645`). The write succeeded either way —
  `_schemas.py:149-160` calls it advisory.
- **`maps get` returns a detected grid *and* a stored override.** `grid` is
  what detection found, carrying its own `source` (`_schemas.py:80-92`);
  `grid_width`/`grid_height`/`grid_px` are what someone set
  (`_schemas.py:116-118`). All three null means detection is in charge.
- **Folder tags read and write differently.** `GET /api/map-folders` resolves
  to display casing via `folder_display_tags` (`core.py:144`); the PATCH and
  the bulk echo back the stored internal keys (`core.py:159`, `core.py:685`).
  `systems book-folders` has the same asymmetry, already recorded.
- **Every batch body caps at 1000, and none may be empty.**
  `MAX_BULK_ITEMS = 1000` (`bulk_service.py:36`) is enforced as `max_length`
  with `min_length=1` on `BulkAddTags.ids` (`_bulk_schemas.py:22`), on
  `BulkFolderTags.folders` (`:35`) and on the generated bulk-update `items`
  (`:68`). `BulkAddTags.tags` is `min_length=1` too (`:23`), so a tag-less
  batch is a 422 rather than a no-op.
- **`map-folders` has no delete.** `systems book-folders` has one; this
  collection does not, and gains a bulk variant books has no counterpart for.

## Design

### 1. Command surface

```
maps list [--map-type <t>] [--folder <path>] [--limit <n>] [--offset <n>]
maps get --id <id>
maps update --id <id> {--input <f> | --stdin}          gm or admin
maps batch-update {--input <f> | --stdin}              gm or admin
maps batch-tag {--input <f> | --stdin}                 gm or admin
maps folders list
maps folders set {--input <f> | --stdin}               gm or admin
maps folders batch-set {--input <f> | --stdin}         gm or admin
```

New `Commands/MapsCommand.cs`, `Commands/MapFolderCommands.cs` and
`Services/MapsService.cs`. The folder group is its own command file on the
shared service, matching `BookFolderCommands.cs` riding `SystemsService`.

`maps folders set` takes no `--id`: `PATCH /api/map-folders` is keyed by the
`path` in its body, unlike `systems book-folders set`, which hangs off a system.

**Writes take JSON, not per-field flags.** `--input <file>` / `--stdin`,
validated by `JsonBodyInput.Validate` against `MapUpdate`'s own
`GetFieldDeserializers()` keys, exactly as `books update` does. This settles
the shape for tokens, models and audio. Three reasons it is JSON:

- `batch-update` and `batch-tag` have to speak JSON regardless, so one input
  language covers the whole group rather than two.
- The allowed field set comes from the generated model. Per-field flags would
  be a hand-written mirror of the API's fields, which the repo has none of and
  is not to gain one.
- The CLI sends the body as raw bytes (`SetStreamContent`), so a literal `0`
  reaches the server verbatim — which is what makes the grid clear work at all.
  A flag path would have to preserve that distinction by hand.

CLAUDE.md's "ID-keyed resources use `update --id --field`" describes the
scalar-body commands (`addons update`, `backups settings set`), not the
metadata ones. `books`, `systems` and now `maps` all take JSON; that line is
narrowed to say so in the same PR.

| Flag | Construction |
|---|---|
| `--map-type` | free string — the server does no validation, so neither does the CLI |
| `--folder` | free string |
| `--limit` | `OptionHelpers.Range(1, …)`, floor only, CLI default 100 |
| `--offset` | `OptionHelpers.Range(0)` |

`--limit` is the one place the CLI holds an opinion the server does not.
Unflagged, `GET /api/maps` returns every map; a default of 100 matches what
`books list` feels like and keeps an agent from pulling a whole library into
its context by accident. The floor of 1 is not cosmetic: it refuses `-1`, which
`search` documents as unlimited and which here means "unlimited" or "drop the
last row" depending on whether `--folder` was given. Larger pages are just a
larger number — there is no ceiling to escape.

Exit 0 everywhere except the two batch verbs, which use
`BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"))` — exit 3 on
HTTP 200 with a non-empty `errors` list, as `books batch-update` does.
`grid_warning` is **not** a partial write and does not change the exit code.

### 2. Help text

`maps list`:

```
Notes:
  --limit defaults to 100 here; the server sets no ceiling, so a larger page is
  just a larger number.

  --folder is an exact folder, not a subtree: battlemaps excludes
  battlemaps/caves. Values are the folder part of relative_path.

  Variants are hidden — only the main copy of a family is listed.
```

`maps get`:

```
Notes:
  grid is what detection found, with its own source; grid_width, grid_height
  and grid_px are a manual override. All three null means detection is in
  charge.
```

`maps update`:

```
Role required:
  gm or admin

Notes:
  tags replace the set. To add without removing, use batch-tag.

  grid_width, grid_height and grid_px take 0 to clear the override and resume
  detection. batch-update cannot: it drops a 0 silently.

  grid_width and grid_height are 0-1000, grid_px 0-2000; outside that is a 422.

  grid_warning in the response is advisory — the write succeeded. Exit is 0.

  Responds {"status": "ok"} and echoes nothing — read back with:
  grimoire-cli maps get --id <id>
```

`maps batch-update` carries the 1000-item cap, the skip-and-continue rule and
exit 3, then points at `maps update` for the clear semantics it does not share.
`maps folders list` carries the display-vs-internal asymmetry, since that is
the one that surprises a caller diffing a write against a read.

Nothing restates a flag's own description or a field the generated response
sample already renders.

### 3. Testing

Unit tests in `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs` and
`Services/MapsServiceTests.cs`, mirroring the `Files` pair — request shapes and
flag validation, not HTTP:

- the `gm or admin` role section renders on all five writes and on neither read
- `--limit` rejects 0 and -1; `--offset` rejects negatives
- `--input` and `--stdin` are mutually exclusive and exactly one is required
- `JsonBodyInput.Validate` rejects a field `MapUpdate` does not declare
- `maps list` sends no `folder` or `map_type` when the flags are absent
- the response-shape sections render for `MapListResponse`, `MapDetailResponse`
  and the maps-namespaced `FolderTagsOut` — four collections declare that name,
  so the generated type is `Backend__routers__maps___schemas__FolderTagsOut`

A smoke-test block, because the grid semantics are live behaviour:

- `maps list` returns `total` and rows; `--limit 1` returns one
- `--folder` on a seeded folder returns only that folder's maps, not a
  subfolder's
- `maps get` on a seeded id returns `grid`, `folder_path` and `folder_tags`
- `maps update` sets `grid_px`, and `maps get` reads it back
- `maps update` with `grid_px: 0` clears it, and `maps get` reads null
- `maps batch-tag` adds a tag, and `tags items --tag <t> --resource-type map`
  finds the map — the symptom this issue names, closed end to end
- `maps folders set` then `maps folders list` shows the tag in display casing
- an unknown field in the body is refused client-side

The fixture library has no maps, so `docker/seed.sh` gains a `maps/` tree —
`make-fixtures.py --png` already generates images, so this is a `mkdir` and two
calls. Writes stay confined to the seeded map fixtures and to fixed values, so
the block re-runs cleanly, as the rest of the smoke test does.

## Documentation

- README Commands table gains eight rows.
- `tools/generate-api-coverage.py` gains eight `IMPLEMENTED` entries and the
  table regenerates; `maps` goes 0/16 → 8/16.
- `docs/grimoire-api-notes.md` gains a `## Maps` section: the two paging
  implementations and what a negative limit does to each, the exact-match
  `folder`, the batch-vs-single grid clear asymmetry, and `grid_warning` being
  advisory. None of it appears in the spec.
- CLAUDE.md's `update --id --field` line is narrowed to the scalar-body
  commands it describes.
- CLAUDE.md's reset recipe gains `docker/library/maps` alongside
  `docker/library/books`, since the seed now writes both.
- `docs/roadmap.md` loses the maps line once this ships, and item 2 (models)
  becomes the next one.

## Out of scope

The six binary getters (`file`, `page/{n}`, `thumbnail`, `vtt/image`,
`vtt/data`, `export.uvtt`) and the two VTT authoring routes new in 1.7.0. None
is per-item metadata; the binary getters are tracked in
[#48](https://github.com/thomaslazar/grimoire-cli/issues/48).
