# `systems book-folders` returns

**Status:** approved
**Workstream:** D of [grimoire-1.6.0-migration.md](../grimoire-1.6.0-migration.md)
**Verified against:** `hunterreadca/grimoire:nightly`, commit
`7f5937071f51dfc65bc09f5e5e49d33c431f0a5d`

## Problem

`systems book-folders list|set` were built and then cut before merge (`5c566b4`)
because the server's two readers of a folder path disagreed by one segment for a
container child: the frontend used `parts[categoryDepth + 1:-1]` while the
backend hardcoded `parts[3:-1]`. No path the CLI could send was correct for both
readers, so the commands would have written folder tags that resolved for one
reader and not the other.

[hunter-read/grimoire#357](https://github.com/hunter-read/grimoire/issues/357)
is fixed, and fixed more broadly than reported: `system_category_depth` walks the
whole container chain (`2 + <ancestor count>`) rather than special-casing one
level of `parent_id`, so arbitrarily nested containers resolve too. A companion
`system_category_depths` returns every system's depth in one query for the bulk
resolvers, and both functions stop on a cycle rather than hanging.

## What a book folder is

A second tagging layer addressed by path rather than by id. Tagging the path
`{system_id}/{category}/{subfolder…}` gives every book at or below it those tags,
without the books themselves carrying them. It is the way to tag by library
layout — one campaign's subfolder, one publisher's imprint — instead of book by
book.

## Verified server behaviour

Measured against the build above. Every item was observed, not read from source.

- **`GET`** returns `{"folders": [{"path", "tags"}]}` and needs no role beyond a
  non-guest account. **`PATCH`** takes `{"path", "tags"}` and echoes the same
  shape. **`DELETE`** takes the path as a **query** parameter — not a body — and
  returns `{"status": "deleted"}`. Both writes require `gm or admin`.
- **A row exists only once a path has been tagged.** `core/errata` existed on
  disk with a book in it and `GET` still returned `{"folders": []}` until a
  `PATCH` created the row. The scanner and indexer never create one, so `list`
  reports what has been tagged, never what is on disk.
- **The path must belong to the system in the URL.** Writing another system's
  path returns `400 {"detail": "path must be
  '{system_id}/{category}/{subfolder...}' for this system"}`. This is a change:
  the 1.5.6 note recorded that the URL's `system_id` was ignored by the write and
  that a caller could write any system's folder through any system's URL. That is
  no longer true and the note is corrected by this work.
- **`PATCH` replaces the tag list.** Writing `["second"]` over
  `["Errata Fixture"]` left only `second` — unlike `books batch-tag` /
  `systems batch-tag`, which add.
- **An empty `tags` clears the folder but keeps the row.** After
  `{"tags": []}` the folder still appears in `GET` with an empty list. Clearing is
  therefore not deleting, which is what `DELETE` is for.
- **`DELETE` is not idempotent.** Removing a path that has no row returns
  `404 {"detail": "Book folder not found"}`. Any repeatable check must create
  before it deletes.
- **Read and write disagree on tag casing.** `PATCH` with `["Errata Fixture"]`
  echoed `["errata fixture"]`, the internal key; `GET` returned
  `["Errata Fixture"]`, the display casing. A round trip does not match
  byte-for-byte.
- **Inheritance never reaches a book's own tags.** With the folder tagged
  `errata-fixture`, `GET /api/books/{id}` reported `"tags": []`. The inherited
  tags surface only through `book-folders list` and the tag catalogue.
- **#357 is fixed, measured rather than inferred.**
  `GET /api/tags/errata-fixture/items` returned
  `{"items": [], "folders": [{"resource_type": "book", "path": "errata",
  "items": [{"title": "DSA5 Errata", …}]}]}` for a **container child**. The
  folder path comes back as one segment, `errata`, which is
  `parts[categoryDepth + 1:-1]`; the old defect would have produced
  `core/errata`. `GET /api/tags` also counts the book against the tag
  (`category: "book"`, `count: 1`).

## Design

### 1. This is a re-implementation, not a revert

The migration doc describes the work as "a revert plus the new endpoint". It
cannot be. The cut file uses `GrimoireCli.Models`,
`ConsoleOutput.WriteJson(result, typeInfo)`,
`AddResponseExample<BookFolderList>` and `--token`. Workstream B deleted the
whole `Models/` tree in favour of passing response bytes through, and the
`--token` flag and `GRIMOIRE_TOKEN` were removed with the refresh work. So
`5c566b4^` is a reference for the help text and the shape of the surface, and
nothing in it is restored verbatim.

### 2. Command surface

`BookFolderCommands.cs`, wired into `SystemsCommand` as the `book-folders`
group, following the conventions the other write commands already use.

| Command | Endpoint | Role tag |
|---|---|---|
| `systems book-folders list --id` | `GET /api/systems/{id}/book-folders` | none |
| `systems book-folders set --id --input\|--stdin` | `PATCH /api/systems/{id}/book-folders` | `gm or admin` |
| `systems book-folders delete --id --path` | `DELETE /api/systems/{id}/book-folders?path=` | `gm or admin` |

`set` reads its body through `JsonBodyInput` with
`RequireExactlyOneSource(--input, --stdin)` and validates it against the
generated `BookFolderUpdate`, exactly as `systems update` validates against
`GameSystemUpdate`. `delete` takes `--path` as a flag rather than a positional:
the resource is id-keyed, which the conventions reserve positionals against.

Three `SystemsService` methods return the raw response body, and each command
writes it with `ConsoleOutput.WriteRawJson`. Response samples come from the
generated models, whose exact names are `BookFoldersResponse` (`GET`),
`BookFolderUpdate` / `BookFolderOut` (`PATCH` request and response) and
`Backend__routers__systems___schemas__StatusResponse` (`DELETE`) — the last
already carries a sample in `JsonExamples.g.cs`.

### 3. Help text

Three behaviours are invisible from the flags and cost a caller real time, so
they belong in the Notes blocks:

- `list` reports tagged folders, not folders on disk. Nothing enumerates the
  tree.
- `set` replaces the tag list, and an empty list clears the folder without
  removing it — `delete` removes it.
- The path's first segment must be the same system as `--id`, or the server
  answers 400.

The tag-casing asymmetry goes in `list`'s notes rather than `set`'s: a caller
comparing what they wrote against what they read back is reading.

### 4. Fixture

**The fixture cannot currently reach this feature at all.** Every book in
`docker/library` sits directly in a category directory, and such a book belongs
to no folder, so nothing inherits a folder tag and the #357 fix is unobservable
through the API.

One line in `docker/seed.sh` adds
`Das Schwarze Auge/5 DE/core/errata/DSA5 Errata.pdf`, and `EXPECTED_BOOKS` in the
smoke test goes 17 → 18. `Das Schwarze Auge 5 DE` was chosen because it is a
container child, which is the case the depth fix is about, and because the smoke
test mentions it nowhere — `Shadowrun 6 DE` carries hardcoded book counts and
`Shadowrun 4 DE` is the reserved metadata-write fixture. A subfolder below a
category directory creates no system, so the system counts are unaffected.

### 5. Smoke coverage

One block, in this order, which is what makes it converge on a re-run:

1. `set` the folder's tags; assert the echoed path.
2. `list`; assert the folder appears with those tags.
3. Assert the tag resolves to the book through
   `GET /api/tags/{internal}/items` — the round trip that #357 broke, and the
   only assertion that proves the feature rather than the plumbing.
4. `delete`; assert `{"status": "deleted"}`.
5. `list`; assert the folder is gone.

This is stronger than the coverage that was cut, which asserted only `set` and
`list` against a path that existed nowhere on disk and therefore never exercised
inheritance.

## Testing

Unit tests mirror the cut `BookFolderCommandTests`: parse-level coverage that
each subcommand accepts its flags and rejects a missing `--id`, that `set`
refuses both `--input` and `--stdin` together and refuses neither, and that
`delete` requires `--path`. Role tags are asserted the way `RoleSectionTests`
already asserts them.

## Documentation

- `docs/grimoire-api-notes.md` — the "Book folders" section is labelled
  *Verified against v1.5.6* and its last bullet records #357 as live behaviour.
  Every bullet is re-verified against the RC and the section relabelled, rather
  than patching one bullet into a section whose provenance would then be mixed.
  Two bullets change outright: the depth mismatch is fixed, and the
  ignored-`system_id` write is now validated.
- `README.md` — three rows in the Commands table.
- `tools/generate-api-coverage.py` — three `IMPLEMENTED` entries, then
  regenerate.
- `docs/roadmap.md` — item 3 is removed. It is no longer intended work, and the
  roadmap records intent rather than status.

## Out of scope

Anything that enumerates the folder tree. The server has no endpoint for it, and
composing one from `books list` paths belongs in the calling layer, not in a
thin pass-through CLI.

## Correction 2026-08-26

The "Verified server behaviour" list above says `GET` "needs no role beyond a
non-guest account". That is wrong. Its route registration carries no
`dependencies=` at all — unlike `GET /{system_id}` directly above it, which has
`require_not_guest` — and `list_book_folders` depends on `get_current_user`, so a
guest can read book folders. `systems book-folders list` is unaffected: a route
guarded by `get_current_user` takes no role tag, which is what it has.
[grimoire-api-notes.md](../grimoire-api-notes.md) records the accurate version.
