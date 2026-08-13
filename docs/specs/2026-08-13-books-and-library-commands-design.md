# Books and library commands — design

Date: 2026-08-13
Status: draft, awaiting review

## Goal

Give the CLI the workflow it exists for: drop files into the library by hand,
have the server find and index them, then correct their metadata. That is three
steps, and today the CLI can do none of them — `books` is the only resource with
no commands at all.

Ten commands:

```
books    list  get  update  batch-update  batch-tag  reindex  rescan
library  rescan  scan-status  cancel-scan
```

Out of scope, each already its own roadmap item: the metadata-lookup trio for
books and systems (next, and the release is cut after it), the remaining systems
endpoints, book text extraction (`toc`, page text, page words), and every binary
endpoint (`file`, `thumbnail`, page render), which needs an output convention
this CLI has not had to settle.

## Grounding

Verified against Grimoire v1.5.6 — `temp/grimoire` at that tag, read directly
rather than taken from the published docs. Behaviour that outlives this spec
belongs in [grimoire-api-notes.md](../grimoire-api-notes.md).

### The three tiers of scanning

The workflow's middle step is not a books endpoint, which is the finding that
shaped this spec's scope:

| Tier | Endpoint | Role | What it does |
|---|---|---|---|
| Whole library | `POST /api/rescan` | admin | walks the tree, adds new files, indexes them |
| A subtree | the same, with `scope` | admin | scoped by **path**, not by system id |
| One known book | `POST /api/books/{id}/rescan`, `/reindex` | gm or admin | re-reads one existing row's file |

`books rescan` 404s on a book that has no row, so it cannot discover anything.
Only `POST /api/rescan` picks up a hand-copied file. There is no system-level
scan endpoint — a system is a folder under `books/`, so scoping to one is the
subtree case. `resolve_scope` (`backend/indexer/metadata.py:257`) requires the
scope to begin with `books/`, `maps/`, `tokens/` or `audio/`, rejects anything
escaping the library root, and resolves the collection folder
case-insensitively.

**The CLI does not translate a system id into a scope path.** That composes two
endpoints, which thin pass-through leaves to the caller; the help says where the
path comes from instead.

### Role dependencies, read from the routers

`GET /api/books` carries `require_not_guest` (`backend/routers/books/__init__.py:32-35`),
which per `CLAUDE.md` is the default for reads and takes **no** tag.
`GET /api/books/{book_id}` has no role dependency at all — also no tag. The five
writes take `require_gm_or_admin`. All three library endpoints take
`require_admin`, making them the first `admin`-tagged commands in this CLI.

### Three book shapes, not one

They are not interchangeable, so they get three DTOs:

| Shape | Source | Distinctive fields |
|---|---|---|
| `BookSummary` | `GET /api/books` items | `game_system_id`; no description/authors/tags |
| `BookDetail` | `GET /api/books/{id}` | `description`, `authors`, `artists`, `genres`, `tags`, `ocr_pending`, nested `game_system` |
| `Book` (existing) | books inside `GameSystemDetail` | `relative_path`, `index_error` |

`Book` keeps its name. Renaming a DTO that already ships, in a branch about
something else, buys clarity at the cost of a diff nobody asked for.

## Commands

`--server` and `--token` are declared per subcommand on all ten, per the
existing convention.

| Command | Endpoint | Role tag | Flags |
|---|---|---|---|
| `books list` | `GET /api/books` | — | `--system-id`, `--category`, `--limit`, `--offset` |
| `books get` | `GET /api/books/{id}` | — | `--id` |
| `books update` | `PATCH /api/books/{id}` | gm or admin | `--id`, `--input`/`--stdin` |
| `books batch-update` | `POST /api/books/bulk` | gm or admin | `--input`/`--stdin` |
| `books batch-tag` | `POST /api/books/bulk/tags` | gm or admin | `--input`/`--stdin` |
| `books reindex` | `POST /api/books/{id}/reindex` | gm or admin | `--id`, `--ocr-dpi` |
| `books rescan` | `POST /api/books/{id}/rescan` | gm or admin | `--id` |
| `library rescan` | `POST /api/rescan` | admin | `--scope`, `--metadata-mode` |
| `library scan-status` | `GET /api/scan-status` | admin | — |
| `library cancel-scan` | `POST /api/cancel-scan` | admin | — |

Every `permissionHint` mirrors its tag: `"the gm or admin role"`,
`"the admin role"`.

### Paging

`--limit` is `Option<int>` with `DefaultValueFactory = _ => 100`, and `--offset`
is `Option<int?>` with none. This is abs-cli's pattern — a client-side default,
named in both the flag description and the command description, and no
auto-paging — with the constant taken from Grimoire rather than from abs-cli's
50, so the CLI and a raw request behave identically.

The 500 cap is documented and not enforced client-side; the server's 422 stays
the single source of that rule. Grimoire pages by `offset` where ABS pages by
`page`, so `--offset` rather than `--page` is a difference that follows from the
API, recorded here so it is not "fixed" back to parity.

### `library rescan` takes flags, not a JSON body

`RescanRequest` is two optional scalars and the body itself is optional.
`--scope` and `--metadata-mode` mirror the API's field names, which is the
convention for a body small enough to be flags; `--input`/`--stdin` stays for
the ~19-field metadata bodies.

## Help text

`--help-full` is the primary interface for the agents driving this CLI, so this
section is a requirement, not a formatting note. Terseness is calibrated against
`SystemsCommand.cs`: one-liners, blank line between paragraphs, wrapped near 72
columns, and **nothing that restates a flag description, a subcommand list, or
the request/response shape below it**.

### Which shape blocks each command registers

| Command | Request shape | Response shape |
|---|---|---|
| `books list` | — | `AddResponseExample<BookListResponse>()` |
| `books get` | — | `AddResponseExample<BookDetail>()` |
| `books update` | `AddRequestShape<BookUpdate>()` | — |
| `books batch-update` | `AddRequestShape<BookBulkUpdate>()` | `AddResponseExample<BulkUpdateResult>()` |
| `books batch-tag` | `AddRequestShape<BulkAddTags>()` | `AddResponseExample<BulkTagResult>()` |
| `books reindex` | — | — |
| `books rescan` | — | — |
| `library rescan` | — | — |
| `library scan-status` | — | `AddResponseExample<ScanStatus>()` |
| `library cancel-scan` | — | — |

Three deliberate absences:

- **`books list` uses `AddResponseExample<T>`, not `AddResponseExampleArray<T>`.**
  `GET /api/books` returns the `{total, books: […]}` envelope; the array helper
  exists for `GET /api/systems`, which returns a bare array.
- **`library rescan` registers no request shape.** Its body is composed from
  `--scope` and `--metadata-mode`; a caller never writes JSON for it, so a
  Request shape block would document a body that is already visible as flags.
- **The status-only responses register no response shape.** `books update`,
  `reindex`, `rescan`, `library rescan` and `cancel-scan` return one or two
  fields whose *values* are the information — `reindex_queued`,
  `already_running`, `not_running`. A generated sample renders those as
  `"<string>"`, which is strictly less useful than naming them in Notes, which is
  what `systems update` already does for `{"status": "ok"}`.

`BulkAddTags` is one component in Grimoire's spec, shared by both resources, so
`books batch-tag`'s request shape is identical to `systems batch-tag`'s.

### Notes, per command

Verbatim, so the implementer writes no prose of their own.

**`books list`**

```
--limit defaults to 100 and 422s above 500; page with --offset against
the total in the response.

--category is the normalised value, not the folder name ('supplement',
not 'supplements'), and is case-sensitive: Core matches nothing.

The account's explicit permission filters the list server-side.
```

**`books get`**

```
403 if the book is explicit and the account disallows explicit content.
```

**`books update`**

```
Clear a field with ""; an explicit null does nothing.

year, month and day cannot be cleared at all: null is dropped and ""
fails coercion with a 422.

tags replace the set. To add without removing, use batch-tag.

Responds {"status": "ok"} and echoes nothing — read back with:
grimoire-cli books get --id <id>
```

**`books batch-update`**

```
At most 1000 items. Each item requires id.

Skip-and-continue: a bad id or item lands in errors, the rest apply.
Exit 3 is HTTP 200 with a non-empty errors list — a partial write.
updated lists the ids that resolved, not the fields that changed.

"" not null clears a field, and year/month/day cannot be cleared — see
books update.
```

**`books batch-tag`**

```
ids and tags are both required and non-empty; max 1000 ids.

Additive only: merges with existing tags, never removes one. To replace
a set, use batch-update with tags.

Exit 3 is HTTP 200 with a non-empty errors list — some ids did not
resolve while the rest were tagged.
```

**`books reindex`**

```
OCR only: 400 unless the book is an image-only PDF. A book with a real
text layer has nothing to re-OCR.

Clears the book's search index and re-queues it from page 1. The OCR
runs in the background — watch it with:
grimoire-cli library scan-status
```

**`books rescan`**

```
Re-reads the file and rebuilds the index, refreshing page count and
thumbnail if the file changed. PDFs only: 400 on an epub or djvu, 404
if the file is gone from disk.

No-ops (silently skipped) under a library scan already running, and
blocks a library rescan started right after it; the response is
rescan_queued either way. Watch it with:
grimoire-cli library scan-status
```

**`library rescan`**

```
The only command that finds a file copied into the library by hand;
books rescan re-reads a book the server already knows.

--scope is a path from the library root beginning books/, maps/,
tokens/ or audio/ — the directory part of a book's relative_path in
systems get, not the file path itself.
A scope matching nothing still reports scan_started.

Exit 3 is HTTP 200 with already_running: a scan was already in flight
and this one did not start — a books rescan still running is one
cause.
```

**`library scan-status`**

```
phase is scanning, indexing or ocr; the counters belong to the scan in
flight.

A loose file directly under books/ counts toward total_books but is
never scanned, so scanned_books >= total_books never becomes true. Poll
running instead.
```

**`library cancel-scan`**

```
Requests a graceful stop; the scan ends at its next checkpoint. Exits 0
whether or not one was running.
```

### Flag descriptions worth stating here

`--ocr-dpi` carries its range, so the Notes do not: `"OCR resolution for this
book (72-600); omit for the server default"`. `--metadata-mode` is a
`ChoiceOption` over `new`, `missing`, `replace`, which renders its own value set,
so its description must not repeat it. `--limit` names its default:
`"Results per page (default 100, max 500)"`.

## Response DTOs

New, all registered on `AppJsonContext` so `GenerateResponseExamples` picks them
up: `BookSummary`, `BookDetail`, `BookListResponse` (`total`, `books`),
`ScanStatus`, and `ScanTriggerResult` (`status`). `BulkUpdateResult` and
`BulkTagResult` are reused as-is.

`ScanTriggerResult` exists because `library rescan` must read `status` to choose
its exit code. It is not rendered as a response shape; parsing it is what makes
exit 3 possible.

`GenerateResponseExamples`' `BuildPropertyOverrides` gains realistic values for
the new shapes the way it already has them for `GameSystemSummary` — at minimum
`BookSummary.Category` and `BookDetail.Category`, so the samples do not read as
`"<string>"` where a real vocabulary exists.

`ScanStatus` fields, from `_DEFAULT_STATUS`
(`backend/routers/library/_helpers.py:24-49`): `running`, `phase`, and
`total_*` / `scanned_*` / `new_*` per collection, plus `updated_books`,
`indexed`, `to_index` and the OCR queue counters.

## Exit codes

`BulkExit` is reused unchanged for the two bulk commands. `library rescan` adds
the second use of exit 3: HTTP 200 carrying `already_running`, where the
requested action did not happen. `cancel-scan`'s `not_running` exits 0 — the
outcome asked for is the outcome obtained.

`docs/input-output.md`'s exit-code list stops at 2 and has done since the systems
write commands landed, though `BulkExit` and `SystemsCommand`'s Notes both
document 3. Adding the missing entry belongs in this work.

## Testing

- Unit tests in the existing areas: `Models/` for the three book DTOs and
  `ScanStatus`, `Commands/` for help output including the role tags and the shape
  blocks each command registers.
- `docker/seed.sh` gains fixtures sufficient to exercise `--system-id`,
  `--category`, `--limit` and `--offset` — enough books in one system that a
  limit below the total proves paging rather than coincidence.
- The smoke test grows a books section. Writes stay confined to
  `Shadowrun 4 DE` with values fixed in the script, so a second run converges;
  `library rescan` is always called with `--scope`, never library-wide, and
  `cancel-scan` is exercised against the `not_running` path, which is
  idempotent.
- `books reindex` cannot be smoke-tested against the fixtures: they are
  generated PDFs with a text layer, which the endpoint rejects by design. Its
  400 is the assertable behaviour.

## Docs

- README Commands table — ten new rows.
- `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate
  `grimoire-api-coverage.md`. Ten endpoints across two OpenAPI tags.
- `grimoire-api-notes.md` for anything the live run verifies that the source
  alone did not settle.
- `docs/input-output.md`: the missing exit 3.
- `docs/roadmap.md`: item 1 leaves when this ships.

## Risks

**The scan is asynchronous and the CLI does not wait.** Every maintenance
command returns as soon as work is queued, so a caller that treats exit 0 as
"indexed" is wrong. This is thin pass-through working as intended — polling is a
workflow across two endpoints — but it makes `library scan-status`'s Notes
load-bearing rather than decorative.

**`scanned_books >= total_books` is not a termination condition.** A loose file
under `books/` inflates the total forever. Any polling example in the help or
the smoke test must key on `running`.

**`--scope` is a path in a CLI that is otherwise all ids.** The mismatch is the
API's, not ours. The help cross-reference to `relative_path` is what keeps it
usable; without it the flag is unusable without reading Grimoire's source.
