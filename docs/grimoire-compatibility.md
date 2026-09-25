# Grimoire compatibility

## Matrix

| grimoire-cli | Grimoire | Status |
|---|---|---|
| 0.1.x | 1.5.6 | initial support, maintained on `support/grimoire-1.5.6` |
| 0.2.x | 1.6.2 | superseded by 0.3.x |
| 0.3.x | 1.7.0 – 1.7.2 | current, on `main` |

**The floor rises only when something forces it**, not on every server release.
Each row above records a pairing that was necessary at the time — 1.5.6 → 1.6.0
because the 1.6.x line changed the token lifetime and made the library writable,
1.7.0 because the CLI now offers a flag older servers silently drop (below).
Absent a reason like that, a bump raises `MaxTestedVersion` and leaves the floor
where it is. Whoever stays on Grimoire 1.5.6 stays on grimoire-cli `0.1.x`, which
is maintained on `support/grimoire-1.5.6` — fixes are made and released there,
then cherry-picked forward.

**Adding a flag that an in-range server would ignore is what forces a floor, so
it is a decision to take deliberately rather than a detail of a bump.** A field
an older Grimoire does not know is dropped by Pydantic rather than rejected, so
the command answers 200 and does nothing — the one failure the version warning
exists to catch.

`main` targets Grimoire 1.7.0. Reaching 1.6.0 was more than a version bump: it
shortened the access token from 30 days to 30 minutes and made the library
writable, which is why the CLI renews its own session
([authentication.md](authentication.md)) and why the `files` endpoints exist at
all. 1.6.1 and 1.6.2 were additive on top of it, the latter adding `model` as a
fifth collection alongside books, maps, tokens and audio.

1.7.0 is additive again. What reaches the shipped commands:

- **Token frames are a third folder marker.** A folder anywhere under `tokens/`
  can declare that its images are token-editor frame art, so `files folder
  create` and `files folder markers` both gained `--frames-container`, and
  `files browse` reports `frames_container` per row. The `/api/token-frames`
  routes that read the marker are unimplemented here.
- **Both markers are now refused where they would be inert.** A container kind
  only means something inside `books/` at a depth where a game system belongs,
  and the frame marker only under `tokens/`; writing either elsewhere used to
  succeed and change nothing about the next scan, and is now a 400. `browse`
  answers the same question up front, per row as `accepts_container_kind` /
  `accepts_frames_marker` and for the browsed folder as
  `children_accept_container_kind` / `children_accept_frames_marker`.
- **Tags can no longer contain `/` or `\`** on any write path — `books update`,
  the `batch-tag` commands and `systems book-folders set` all 422 on one. The
  tag routes took `{internal:path}` in exchange, so a slashed tag created before
  the rule is reachable by `tags items` rather than stranded.
- **The add-on index is a list.** `addons list` and `addons settings` answer with
  `index_urls` alongside the singular `index_url`, and each entry carries
  `available_in`, `changelog` and `source_url`. `addons refresh` reports
  per-index `errors`. Setting more than one index, choosing which to install
  from, and `GET /api/addons/verify-index` are unimplemented.
- **`search` hits carry `variants`**, and `GET /api/changelog` and the map VTT
  authoring routes are new and unimplemented.

Upstream also fixed the rename path: `files move` and `files rename` matched
index rows on the absolute `filepath`, which is spelled however `LIBRARY_PATH`
is written, so a mismatch renamed the file while relinking nothing and the next
scan inserted a second row. Matching moved to `relative_path`.

**`MinSupportedVersion` moved to 1.7.0 for one reason: `--frames-container`.**
Nothing the CLI calls was removed or renamed, and every other command still
works against 1.6.2 — but 1.6.2's `CreateFolderRequest` and `MarkersRequest` are
plain `BaseModel`s with Pydantic's default `extra='ignore'`, so that flag's field
is dropped and the folder is created with no marker and a 200. A silent no-op is
what the floor warning is for; without the new flag this release would not have
needed one.
`docker/docker-compose.yml` pins the `1.7.2` release tag, so the spec cannot
drift under the committed client between regenerations.

**1.7.1 raised `MaxTestedVersion` and left the floor alone** — the first bump
here to do so, and the shape a bump takes when nothing forces a floor. Its
request surface is unchanged but for one additive response field, so the CLI
reaches 1.7.0 and 1.7.1 alike and the supported range is both. What it changes
is behaviour the CLI passes through:

- **`tags list` counts are live.** A tag's `count` now comes from the same
  resolution `/items` uses, so a tag whose carriers are gone reads 0 rather than
  counting dead links. Counts can therefore drop across this upgrade without
  anything having been untagged.
- **Folder-tag rows follow the tree.** `files delete` on a directory now purges
  the folder-tag rows at and beneath it, and `files move` / `files rename` carry
  them onto the new path, descendants included; a collision absorbs the source's
  tags into the existing destination row. On 1.7.0 both left the rows behind,
  stranded on a path that no longer existed (upstream #445). A row for a path
  that was never on disk is still unreachable on either version — nothing walks
  it, because the purge runs only when a real directory is deleted.
- **`ocr_pages_skipped` is new on `books get` and the book rows of
  `systems get`.** Greater than 0 means the book is indexed but only partly
  searchable: those pages exceeded `OCR_PAGE_TIMEOUT` and their text is missing.
  `books reindex` resets it to 0.

**1.7.2 likewise raised `MaxTestedVersion` alone.** It adds one book field and
one query parameter, both optional, so 1.7.0, 1.7.1 and 1.7.2 are all in range:

- **`product_code` is a new book metadata field** — the publisher's catalogue
  number (`PZO9001`, `TSR 9247`), the identifier most RPG PDFs carry instead of
  an ISBN. It reads back on `books get`, `books list`, the book rows of
  `systems get` and `search`, and `books update` / `books batch-update` accept
  it because the generated model does. An older server drops it and answers 200.
- **`search` gained the `code:` field**, aliased `sku` and `product_code`, and a
  bare query now matches the product code alongside title and filename. Matching
  is blind to spaces and hyphens on both sides, so `code:TSR9247` finds
  `TSR 9247`. On 1.7.0 / 1.7.1 the prefix is unrecognised and searched
  literally, which returns nothing rather than erroring — `search fields`
  answers what the server in front of you actually takes.
- **A quoted phrase is one FTS token**, so `"lucky feat"` and `text:"lucky
  feat"` are phrase searches rather than an implicit AND. Same query, different
  hits, across this upgrade.
- **`GET /api/maps` and `GET /api/tokens` take `sort=path|name`.** Unexposed —
  a `--sort` flag is what forces a floor, and `path` (the default, and what both
  commands already get) is the order the CLI has always returned.
- **Map and token thumbnails survive a rename.** Both routes now fall back to
  the path hash when the filename-derived name misses, instead of 404ing for an
  image that is on disk.
- **An add-on update that needs a newer Grimoire is no longer offered.**
  `addons list` reports `update_available: false` and omits the entry from
  `available_in` where the build in the index requires a server past this one.

## Runtime check

`src/GrimoireCli/Api/GrimoireApiClient.cs` defines `MinSupportedVersion` and
`MaxTestedVersion`, currently `"1.7.0"` and `"1.7.2"`. A check runs before the first
request of any command, calling `GET /api/about` and comparing the reported
version against that range. It is throttled to once every 24 hours — a
config with a recent `lastVersionCheck` skips the probe entirely — and
`login` always forces one regardless of how recently the last check ran.
Both fields live in the config file (`lastVersionCheck`, `lastServerVersion`;
see [configuration.md](configuration.md)) so the cadence persists across
invocations.

- Below `MinSupportedVersion` → a warning on stderr that some features may
  not work.
- Above `MaxTestedVersion` → a warning on stderr that the server is newer
  than anything this CLI has been tested against.
- Inside the range → nothing (a debug line only, under `--debug`).
- No numeric component to compare (e.g. the literal `nightly`) → skipped
  silently, with a debug line only.

A probe that fails — unreachable server, non-2xx, unparseable body — is
silent except under `--debug`, and leaves `lastVersionCheck` untouched so the
next invocation retries rather than waiting out the full window.

Either way the CLI **never refuses to run** — the warning is advisory, not a
hard gate.

### Known limitation: one server, one slot

`lastVersionCheck` / `lastServerVersion` are a single slot in the config
file, not keyed by server. Pointing `GRIMOIRE_SERVER` at a
second instance records that instance's version into the same slot, which
can suppress the check for the original server for up to 24 hours and, if
the two are alternated, can print a warning claiming the server "moved"
between versions that is not true of either one. This is warn-only, so
nothing breaks — but treat the record as belonging to whichever server was
checked most recently, not to any one server in particular.

## Handling a Grimoire release

1. Pin the reference clone to the new tag:

   ```bash
   git -C temp/grimoire fetch --depth 1 origin tag vX.Y.Z
   git -C temp/grimoire checkout vX.Y.Z
   ```

2. **Bump the Grimoire image tag in `docker/docker-compose.yml` and restart the
   stack on the new version first** — the client is regenerated from a running
   server, never a file on disk:

   ```bash
   docker compose -f docker/docker-compose.yml up -d --wait
   ```

3. **Regenerate the committed client and review the diff.** This is the
   authoritative list of what changed in the request surface — paths, methods,
   query parameters and every request body — and it beats reading release
   notes. Regenerating in place is what makes the diff exist at all: the
   previous output has to already be in git for `git diff` to show anything.

   ```bash
   bash tools/generate-api-client.sh
   git diff src/GrimoireCli/Generated
   ```

4. Diff the serializers backing the response shapes the spec still types as
   `{}` — the generator cannot see those, and stdout is a byte passthrough
   with no DTO to update, but a field the exit-code readers
   (`ReadStringProperty`, `HasItems`) key on, or documented behaviour in
   [grimoire-api-notes.md](grimoire-api-notes.md), can still change shape
   underneath them:

   ```bash
   git -C temp/grimoire diff vOLD..vNEW -- backend/routers/ backend/models/ backend/services/
   ```

   Scope it no tighter than that. A release can move a documented rule out of
   `core.py` entirely: 1.6.2 changed the scaffold guard in
   `services/library_fs/folders.py`, the duplicate-scan statuses in
   `routers/duplicates/detection.py` and the mergeable-field sets in
   `models/collections.py`, none of which a `*/core.py` glob reaches.

5. Update flags and help text to match. Regenerate the `--help-full` sample
   file, now downstream of the client regeneration in step 3:

   ```bash
   dotnet run --project tools/GenerateJsonExamples -- src/GrimoireCli/Commands/JsonExamples.g.cs
   ```

   Re-run `bash docker/seed.sh` and `bash docker/smoke-test.sh`. Update
   `MinSupportedVersion` / `MaxTestedVersion` in `GrimoireApiClient.cs`, the
   matrix above, and the compatibility line in `README.md` — all in the same
   PR as the code change, alongside the regenerated
   `src/GrimoireCli/Generated/`.
