# Metadata sidecar export

**Status:** approved
**Issue:** [#47](https://github.com/thomaslazar/grimoire-cli/issues/47)
**Verified against:** `hunterreadca/grimoire:1.7.2`, the pinned stack

## Problem

Grimoire can write each book's metadata to a sidecar file beside it — `.opf`,
`.nfo`, `.grimoire.json` or `.yaml` — so a curated library survives the instance
and is readable by anything that understands those formats. Three admin
endpoints drive it, and the CLI implements none of them.

The CLI can already trigger the *read* direction: `library rescan
--metadata-mode new|missing|replace` makes the indexer apply sidecar metadata to
already-indexed books (`indexer/_book_records.py:165-177`). It cannot trigger
the *write* direction. Both are server-side operations — the CLI never opens a
sidecar file and must not start — so this is a gap in the remote control, not in
data handling.

## Verified server behaviour

Read from `backend/routers/maintenance/`, `backend/metadata/` and
`backend/routers/books/core.py` at tag `v1.7.2`. Line cites are that tag.

### Endpoints and roles

All three carry `require_admin` (`maintenance/core.py:38,51,76`), so all three
get the `admin` tag and the `"the admin role"` permission hint.

| Method | Path | Handler |
|---|---|---|
| GET | `/api/maintenance/sidecars/settings` | `get_sidecar_settings` (`core.py:37`) |
| PUT | `/api/maintenance/sidecars/settings` | `update_sidecar_settings` (`core.py:49`) |
| POST | `/api/maintenance/sidecars/export` | `export_sidecars` (`core.py:75`) |

### The export is a backfill, not a re-export

This is the fact that shapes the whole feature, and the issue does not record it.

`export_sidecars` calls `export_library(db, formats)` (`core.py:104`), and
`export_library` hard-defaults `skip_existing=True` (`metadata/export.py:313`).
The route never overrides it, so **the endpoint creates sidecars that are
missing and can never refresh or rewrite one that exists.** There is no
full-rewrite path through the HTTP API at all.

That is not a deficiency, because two other paths keep sidecars current without
anyone asking:

- **`refresh_existing_safe`** runs after every committed book-metadata edit
  (`routers/books/core.py:48,217`) with `only_existing=True`
  (`metadata/export.py:242-262`) — it updates the sidecars a book already has
  and creates none.
- **`export_new_book`** runs from the scanner for each newly indexed book
  (`metadata/export.py:279-304`) with `skip_existing=True`.

So the shape of the feature is: enable a format once, backfill once, and the
library maintains itself from then on. `sidecars export` is a catch-up for books
that predate the feature being switched on — **not** something to re-run after a
metadata sweep.

### Settings are a whole-object replace

`SidecarSettings` (`maintenance/_schemas.py:19-32`) declares `formats:
list[str] = []`, `covers: bool = False` and `overwrite_foreign: bool = False` —
concrete defaults, not `Optional[...] = None` — and `update_sidecar_settings`
applies all three unconditionally (`core.py:56-60`). A PUT that omits `covers`
therefore **sets it false**.

This is the opposite of `backups settings set`, whose help documents "A partial
update despite the PUT: omitted fields are left alone" because that model's
fields really are optional. The difference is worth stating loudly in help; a
reader who knows the backups command will otherwise assume wrong.

`set_enabled_formats` rejects an unrecognised format with a 400
(`metadata/settings.py:59-67`); the value set is
`ALL_FORMATS = (opf, nfo, json, yaml)` (`metadata/formats.py:27`).

### Export failure modes

- **400** when no format is enabled: `"Metadata sidecar export is disabled.
  Enable at least one format first."` (`core.py:97-101`).
- **409** while a library scan is running (`core.py:88-95`) — a scan rewrites
  the rows being exported, so the sidecars would capture a moving target.
- **Runs inline** (`core.py:81-85`), not in the background: the response carries
  the per-item outcome rather than a status to poll.
- Response: `written`, `skipped_foreign`, `skipped_missing`, `failed`, `covers`,
  `read_only`, `errors` (`core.py:112-120`). `read_only: true` means the mount
  is not writable.
- **Books only.** `export_library` iterates indexed books
  (`metadata/export.py:306-315`); maps, tokens, audio and models get nothing.

## Design

### Commands

A new top-level `sidecars` group with its own command file and its own service,
per this repo's one-file-per-command-group convention. Each command maps to one
endpoint and carries `AddRoleRequired("admin")` with the `"the admin role"` hint.

The GET and PUT share one path, so they nest as a `settings` subgroup with `get`
and `set` beneath it — the shape `backups settings` already uses. Every group in
this CLI is a pure group; none carries an action of its own, and this one does
not become the first.

```
grimoire-cli sidecars settings get
grimoire-cli sidecars settings set --formats opf json
grimoire-cli sidecars export
```

`SidecarsService` exposes `SettingsAsync()`, `SettingsSetAsync(...)` and
`ExportAsync()`. The generated models `SidecarSettings` and
`SidecarExportResponse` already exist, so `AddResponseExample` renders both
shapes without any hand-written mirror.

### Flags

`sidecars settings get` and `sidecars export` take none — neither endpoint has a
body, a path parameter or a query parameter.

`sidecars settings set` takes the body's three fields as flags:

| Flag | Type | Notes |
|---|---|---|
| `--formats` | repeatable string | **Required.** Values `opf`, `nfo`, `json`, `yaml` |
| `--covers` | bool | Write the cover image beside the metadata file |
| `--overwrite-foreign` | bool | Allow replacing sidecars Grimoire did not write |

`--formats` mirrors `duplicates scan --resource-types`: an `Option<string[]>`
with `AllowMultipleArgumentsPerToken`, a validator against the value set, and
completions. The server 400s on an unknown format, so rejecting locally saves a
round-trip and gives the caller the value set for free.

**`--formats` is required** so the destructive case is structurally impossible:
without it, `sidecars settings set --covers` would send `formats: []` and
silently switch the whole feature off. The consequence is that sidecar export
cannot be *disabled* from the CLI — that stays a UI action, and is a deliberate
limitation rather than an oversight.

### Exit codes

`sidecars export` uses the existing `BulkExit` rule: exit 3 when the run had
failures, 0 otherwise, with the full JSON still on stdout. It is the same
situation the five endpoints already sharing that rule face — HTTP 200, some
items did not land.

It keys on `errors`, not on `failed`, because `GrimoireApiClient.HasItems` tests
a non-empty array and `failed` is an integer here. The two are equivalent:
`_record_failure` (`metadata/export.py:158-171`) increments `failed` and appends
to `errors` in the same breath, so the first failure always lands a message.
`errors` is deduplicated and capped at 20 entries, which does not affect the
test — a capped list is still non-empty. This needs no new helper.

Skips are not failures. `skipped_foreign` is the server correctly declining to
overwrite a hand-maintained `.opf`, and `skipped_missing` is a row whose file is
gone; both leave the exit code at 0.

### Help text

On `sidecars export`, in this order:

- Creates only the sidecars that are missing; never rewrites one that exists.
- No need to re-run after a metadata sweep — an edit refreshes a book's existing
  sidecars on its own, and the scanner writes them for new books.
- Books only. Maps, tokens, audio and models get nothing.
- 400 until a format is enabled — see `sidecars settings set`.
- 409 while a library scan is running. Runs inline; there is no status to poll.
- `read_only: true` means the library mount is not writable.
- Exit 3 when `failed` is above 0.

On `sidecars settings set`:

- Replaces the whole settings object: an omitted `--covers` or
  `--overwrite-foreign` is set false.
- `--overwrite-foreign` lets a backfill replace hand-maintained `.opf` files.

`sidecars settings get` needs no Notes — the response sample shows the three fields.

## Testing

### Unit

- All three commands declare the `admin` role; the two service calls that can
  403 pass the matching `"the admin role"` hint.
- `--formats` is required: `sidecars settings set --covers` is a parse error.
- An unrecognised format is a parse error; `opf nfo json yaml` all parse, singly
  and together.
- `export` and `settings` accept no options.
- The help caveats above are present, in particular the never-rewrites line and
  the whole-object-replace line.
- `BulkExit.CodeFor` drives the export exit code.

### Smoke

Against the live stack, in order:

1. `sidecars settings get` reads back the current configuration.
2. `sidecars settings set --formats opf` enables one format and echoes it.
3. `sidecars export` returns the documented shape with `failed == 0`.

**The assertion must not key on `written`.** The second run of the smoke test
writes nothing, because the backfill is additive — `written` would be 36 on the
first run and 0 on the second. Asserting the response *shape* plus `failed == 0`
converges; asserting a count does not, and the smoke test is required to
converge on a re-run.

This leaves `.opf` files in the fixture library. They are removed by the
documented reset, which already deletes the library subtrees, and `.opf` is not
a book extension so the boot scan does not index them as content.

## Not in scope

- **Any local file handling.** No `--output`, no sidecar parsing, no format
  rendering. The server writes the files; the CLI asks it to.
- **Disabling export from the CLI**, which follows from `--formats` being
  required. See above.
- **A full-rewrite flag.** The HTTP API exposes no way to force one.
