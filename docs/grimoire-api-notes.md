# Grimoire API notes

Behaviour verified against Grimoire **v1.5.6** — the release the live instance
and `docker/docker-compose.yml` both run — by reading `temp/grimoire/` at that
tag and by calling the API. Don't re-derive these, and don't trust the published
docs over them. Re-verify after a server upgrade — see
[grimoire-compatibility.md](grimoire-compatibility.md) for the bump procedure.

`main` upstream carries unreleased work that no instance runs. Pinning the
reference clone to the release tag is not optional; reading `main` is how the
first round of wrong conclusions happened. Bulk endpoints and guarded renames
were both examples of this at v1.5.4 — both shipped in v1.5.5, see below.

## Auth

- **`HTTPBearer`.** `POST /api/auth/login` returns a JWT valid 30 days.
- **There is no refresh endpoint.** An expired token means logging in again — the
  main divergence from ABS, which has `auth/refresh` and a refresh token.
- Both `/api/auth/login` and `/api/auth/setup` return `{"token": …, "user": {…}}`.
  The key is `token`, not `access_token`.
- Auth endpoints are rate-limited: `AUTH_RATE_LIMIT` defaults to `10/minute`, and
  `RATE_LIMIT_ENABLED=false` disables it (`backend/security.py`). The local stack
  sets the latter.

## Responses

- **Nearly every response is untyped in the spec** — FastAPI without
  `response_model`. Response shapes come from calling the API or reading the
  source, not from the spec. Request bodies *are* typed (`GameSystemUpdate`,
  `BookUpdate`, `RescanRequest`, …).
- **`GET /api/openapi.json` returns 500 when `OPDS_ENABLED=true`** — upstream
  `hunter-read/grimoire#276`. Both the live instance and the local stack run with
  OPDS off so the spec serves.

## PATCH semantics and filtering

Applies to both `PATCH /api/systems/{id}` and `PATCH /api/books/{id}`
(`backend/routers/{systems,books}/core.py`).

- **Nulls are dropped.** Both handlers do `model_dump(exclude_none=True)` and then
  `setattr` what survives, so a JSON `null` is *silently ignored* — a field cannot
  be cleared to null, and a body of nothing but nulls returns 200 having changed
  nothing.
- **Clearing** works with `""` / `[]` for string and list fields. The integer
  fields (`year` on systems; `year`/`month`/`day` on books) **cannot be cleared at
  all** — `null` is dropped and `""` fails Pydantic coercion with a 422.
- **Unknown keys are silently discarded.** Neither `GameSystemUpdate` nor
  `BookUpdate` sets `model_config`, so Pydantic's default `extra='ignore'` applies:
  a typo'd field name returns `{"status":"ok"}` and changes nothing.
- **The response is `{"status":"ok"}`**, not the updated row. Seeing the result
  takes a follow-up `GET`.
- **`tags` replace, they don't merge** (`tag_service.sync_tags_from_payload`).
  Tags live in shared tables rather than a column (upstream #235). v1.5.4 had no
  additive tag endpoint and no bulk endpoints. **v1.5.5 ships both** (issue
  #270): `POST /api/{books,systems,maps,tokens,audio}/bulk` applies per-item
  edits in one transaction (`run_bulk_update` / `apply_updates`,
  `backend/services/bulk_service.py:49-123`), and `/bulk/tags` additively
  applies tags across the whole selection the same way (`run_bulk_add_tags`,
  `backend/services/bulk_service.py:126-161`, request shape in
  `backend/routers/_bulk_schemas.py`). An unresolved id, or a `validate` hook
  rejection (e.g. a system name clash), fails only its own item — reported in
  the response's `errors` — not the whole batch.
- **Renaming a system was unguarded on v1.5.4.** `name` and `slug` are both
  `unique=True` (`backend/models/library.py:24-25`); the handler had no
  conflict check, so a duplicate name failed at commit as an opaque 500 rather
  than a 409, and it wrote only `name` — `slug` keeps its old folder-derived
  value regardless, so the two diverge permanently and renaming the folder on
  disk creates a *second* system row instead of updating the first.
  **v1.5.5 guards the rename** (`_apply_rename`,
  `backend/routers/systems/core.py:314-334`, issue #261/#262): a name clash now
  raises a 409, and a successful rename sets `GameSystem.name_is_custom`
  (`backend/models/library.py:76`). This supersedes the old "scanner never
  writes `name`" mechanism — the scanner now only skips refreshing `name` from
  the folder when `name_is_custom` is set (`if not system.name_is_custom and
  system.name != name`, `backend/indexer/scan.py:376`), so an unrenamed
  (default-named) system's display name still tracks a folder rename. The
  slug-divergence behaviour above is unaffected either way; the full rename
  write-up has not been re-verified end-to-end against v1.5.5 beyond this.
- **`GET /api/systems` filters are case-insensitive exact matches**, not
  substrings (`_has_value` in `backend/routers/systems/core.py`): `edition=5`
  matches `5` but never `5e`, and `genre=Cyber` never matches `Cyberpunk`. A list
  field matches if any element equals the value; an empty or null field never
  matches, so a freshly scanned system is excluded from every metadata filter.
  `genre=` tests the `genres` list, not the legacy `genre` string.
- **`category` on `GET /api/systems/{id}` is case-SENSITIVE**, unlike every other
  filter. `core.py:175` compares with `==` (`b.category == category`) while
  `genre` goes through `_has_value`, which lowercases both sides. So
  `category=Core` returns no books and `category=core` returns them. Verified
  against a running instance.

## Systems writes and `me`

Verified against v1.5.6, backing `systems update`, `systems batch-update`,
`systems batch-tag` and `me`.

- **`PATCH /api/systems/{id}` answers `{"status":"ok"}`** and echoes nothing
  back (`routers/systems/core.py:311`); a follow-up `GET` is the only way to
  see the result.
- **The payload that reaches the row has already had nulls and unknown keys
  removed** (`payload = data.model_dump(exclude_none=True)`,
  `routers/systems/core.py:302`): a JSON `null` is dropped rather than
  clearing the field, so `""` is the only way to clear a string.
- **A rename sets `name_is_custom` permanently**
  (`routers/systems/core.py:334`), after which the scanner stops re-deriving
  the name from the folder (`indexer/scan.py:376`). Renaming to the system's
  own current value returns before the flag is touched
  (`routers/systems/core.py:325-326`), so it stays folder-derived.
- **Bulk update is skip-and-continue.** An unresolved id or a validation
  rejection (e.g. a name clash) is reported in `errors` and does not fail the
  rest of the batch. The transaction commits once, only if at least one item
  applied, and the request is capped at `MAX_BULK_ITEMS = 1000` items
  (`services/bulk_service.py:106-122`, `:38`).
- **`bulk/tags` merges and never removes** existing tags, and returns the
  full post-merge display-tag set for every updated id
  (`services/bulk_service.py:157-161`).
- **`GET /api/auth/me` sets a session cookie as a side effect** when called
  with a bearer token and no existing cookie, reusing that token rather than
  minting a new one (`routers/auth/core.py:167-170`) — a bare read has a
  write side effect on the client's cookie jar.

## Content and rescan

- **No upload API.** The library is mounted `:ro`; content arrives on the
  filesystem, then `POST /api/rescan`. A write channel is an open design question,
  tracked outside this repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a
  `scope` (e.g. `books/<system>/supplements`). `missing` reapplies OPF sidecars
  while treating any populated field as user-protected. Poll `GET /api/scan-status`.
- **A `scope` that resolves to no real directory still answers `scan_started`.**
  `resolve_scope` (`indexer/metadata.py:257-286`) validates only that the path
  begins with a known collection (`books`/`maps`/`tokens`/`audio`) and does not
  escape the library root — it never checks the target exists. A scope typo'd
  or naming a non-existent subtree walks nothing and completes instantly with
  no error, so `scan_started` alone confirms the request was well-formed, not
  that anything was scanned.
- **`books rescan` and `library rescan` share one `running` flag.**
  `rescan_single_book` (`backend/routers/library/_helpers.py:291-306`, backing
  `POST /api/books/{id}/rescan`) and `run_rescan_sync`
  (`backend/routers/library/_helpers.py:337-353`, backing `POST /api/rescan`)
  both guard on and set the same status flag. If a `books rescan` is still
  running in the background, a `library rescan` fired right after it sees the
  flag set and answers `already_running` (CLI exit 3) instead of
  `scan_started`. The reverse guard runs the other way: a `books rescan`
  fired while a `library rescan` is in progress sees the flag already set and
  silently skips the single-book re-read rather than racing the full scan —
  it still answers `rescan_queued`. Verified live: calling the two back to
  back without waiting for `GET /api/scan-status`'s `running` to clear
  reproduces both directions.
- **Editions and language are metadata, not folders.** A new *flat* (non-container)
  folder under `books/` creates a system row with only `name` set; `parent_system`
  / `edition` / `system_family` stay empty until a `PATCH /api/systems/{id}`.
  This no longer covers the whole story from v1.5.5 on: a folder scanned as a
  *container child* gets `parent_system` and `edition` auto-populated at
  creation instead — see "System containers" below. **`system_family` gained a
  folder route in v1.5.6** (upstream #301): a `.system-family-container` fills
  it in on each child. It remains PATCH-only for a shelf that uses no family
  container, which is what `docker/seed.sh`'s fixtures do.

## Scanner behaviour

Backs `docker/seed.sh`'s fixture layout and the `--category` / `--book-sort`
flags on `systems get`.

- **Fixture content:** the scanner keys off extensions in
  `backend/indexer/constants.py` — `.pdf/.epub/.djvu` for books,
  `.png/.jpg/.jpeg/.gif/.webp/.bmp/.tiff/.svg` for images, plus archive and audio
  sets. A handful of tiny generated PDFs and PNGs under
  `docker/library/books/{system}/{category}/` is enough; no real books needed.
- **Categories** come from the folder name under the system
  (`backend/indexer/categories.py`). A folder named `Maps` **directly** under a
  system becomes a map category; at subfolder depth the name is inert.
- **A loose file directly under `books/` is never indexed, but is still counted.**
  `_scan_books` skips anything that isn't a directory
  (`if not system_dir.is_dir(): continue`, `backend/indexer/scan.py:398`), so a
  stray `foo.pdf` next to the system folders produces no system and no book.
  `_count_eligible_files` doesn't apply the same skip, so it still counts the
  file toward `total_books` — any wait loop polling
  `scanned_books >= total_books` hangs forever with a loose file present.

### One-page collections

Verified against v1.5.5 (`backend/indexer/constants.py:95-105`,
`backend/indexer/categories.py::detect_container_kind`).

The reserved slugs are `one-page-rpgs`, `single-page-rpgs`, `one-shot-rpgs` and
`micro-rpgs`. **`micro-rpgs` is new in v1.5.5** (upstream #262) — it did not
exist in v1.5.4, where recording it as fabricated was correct.

A reserved slug **declares a one-page container on its own**, with no marker
file: `detect_container_kind` returns `"one-page"` for it. Each loose file
directly under such a folder becomes its own single-book system, named by
`prettify_collection_name`, which capitalises any word containing no uppercase
letter — so `Lasers and Feelings.pdf` indexes as `Lasers And Feelings`.

Marker files are tested first, so a `.parent-system-container` in a
reserved-slug folder overrides the one-page flavour.

On v1.5.4 the same folder produced one system whose immediate subfolders became
category labels. Any claim about this behaviour must name a version.

- **Category values are not a closed set.** `CATEGORY_MAP`
  (`backend/indexer/constants.py`) normalises known folder-name aliases
  (`supplements/`, `sourcebook/`, `guide/`, `companion/`, …) onto the canonical
  `core`, `supplement`, `adventure`, `character-sheet`, `map`, `handout`,
  `homebrew`, `starter-set`. A top-level folder that matches none of them
  becomes its own category: `guess_category` (`categories.py:200-234`) falls
  back to the slugified folder name (`Extras/` → `extras`). System-agnostic
  folders go through `agnostic_category` (`categories.py:237-249`) instead,
  which slugs the immediate subfolder and yields `uncategorized` for books with
  no subfolder at all. One-page folders no longer take this path in v1.5.5 —
  they're a container (see above), so each loose file gets ordinary category
  inference as its own single-book system.
- **`(nsfw)` in a system folder name sets `is_explicit`** and is stripped from the
  stored name, so `Vampire The Masquerade 5 EN (nsfw)/` becomes a system named
  `Vampire The Masquerade 5 EN` with `is_explicit: true`.
- **`is_explicit` only ever latches on, never off, via rescan.** The
  existing-system branch is `if folder.is_nsfw and not system.is_explicit:
  system.is_explicit = True` (`backend/indexer/scan.py:347-348`) — removing
  `(nsfw)` from a folder name and rescanning does not clear the flag on that
  system row; only the creation branch (`is_explicit=folder.is_nsfw`, line 327)
  sets it from the folder state. Clearing a stale flag needs a database reset, not a
  rescan. Verified against source, not yet against a live instance.
- **Leading `!`, `$`, `%` are stripped from system folder names**
  (`strip_sort_prefix`), so `!!Dungeons & Dragons/` is stored as
  `Dungeons & Dragons`. Only the contiguous leading run is removed.

### System containers

Verified against v1.5.6. Every citation is a file in `temp/grimoire`.

- **Five container kinds as of v1.5.6** (upstream #301, which added the last
  three): `parent` (children are editions of one game), `one-page`,
  `family` (related but distinct systems sharing a lineage), `publisher`
  (one company's systems), and `generic` — a bare `.container` shelf that
  claims no relationship and propagates nothing (`indexer/constants.py`).
- A folder becomes a container via a marker file
  (`.parent-system-container`, `.one-page-container`,
  `.system-family-container`, `.publisher-container`, `.container`), the
  equivalent folder-name suffix, or a reserved one-page slug
  (`indexer/categories.py::detect_container_kind`). A folder carrying more
  than one declaration resolves by `CONTAINER_PRECEDENCE`, most specific
  first, so markers and suffixes can never disagree.
- **Only `parent` sets its children's `parent_system`.** A `family` container
  fills in each child's `system_family`, a `publisher` fills in `publishers`,
  and `generic` propagates nothing.
- A child's display name is `"<container> <folder>"` (`indexer/scan.py:443`), so
  `Shadowrun` + `6 DE` is `Shadowrun 6 DE`.
- `edition` is the child folder name **verbatim** (`indexer/scan.py:490`): a
  folder called `6 DE` yields edition `6 DE`, not `6`.
- `parent_system` is still a free-text column, auto-set to the container's name
  on child creation (`indexer/scan.py:332`). Both it and the real `parent_id`
  foreign key are returned.
- Sort prefixes are stripped before the container name is used
  (`indexer/scan.py:188`), so `!!Dungeons & Dragons` yields `Dungeons & Dragons`.
- **Containers nest as of v1.5.6.** `_scan_container` recurses into a child
  that is itself a container (`indexer/scan.py:493`), and category depth
  follows it: `system_depth=2 + depth` (`indexer/scan.py:557`), where `depth`
  counts the containers above a child. On v1.5.5 that was the constant 3, so
  only one container level worked — any claim that nesting is unavailable
  describes v1.5.5 and earlier.
- **`GET /api/systems` hides container children before applying any filter**
  (`routers/systems/core.py:78-95`). A filter on metadata only children carry
  returns `[]` with exit 0 unless `include_children=true` is also sent.
- `GET /api/systems/{id}` returns the summary shape plus `books` **and**
  `children` (`routers/systems/core.py:186-196`).
- `Book.category` is assigned in exactly two places: the new-book insert
  (`indexer/scan.py:782`) and a re-home, guarded by
  `if existing.game_system_id != system.id` (`indexer/scan.py:730`). An ordinary
  rescan does **not** re-derive it, so a `PATCH category` holds — but converting
  a folder into a container re-homes every book in it and does reset it.

## Systems have no language field

`GameSystemUpdate` has 17 fields and `serialize_system_summary` returns 31; neither
includes `language`. It exists only on books (`BookUpdate.language`), and
`GET /api/systems` has no `language` query parameter. A system's language can be
expressed only through its name (the `Shadowrun 6 DE` convention), a tag, or
per-book metadata.

## Add-ons

Verified against v1.5.6, backing the seven `addons` commands.

- **A fresh instance has never fetched the add-on index.** `available()`
  (`backend/addons/install.py:99`) reads the index straight off
  `get_cached_index` (`backend/addons/registry.py:114-115`), which stays empty
  until `refresh_index` first saves one (`backend/addons/install.py:60-77`) —
  nothing ships bundled with the server. Verified live: `addons list` reports
  `available: []` and a system's `metadata-sources` reports no source until
  `addons refresh` has run at least once.
- **`fetch_json` carries no host allow-list** (`backend/addons/fetch.py:92-134`)
  — it is plain httpx, restricted only by the `http(s)://` scheme check in
  `refresh_index` (`backend/addons/install.py:63-64`), not by destination.
  A URL on the docker-compose network works exactly like the published index,
  which is what lets `docker/smoke-test.sh` point `addons settings
  --index-url` at the internal `addon-index` service instead of the real
  `raw.githubusercontent.com` catalogue.

## Metadata lookup

Verified against v1.5.6, backing `systems metadata-sources` / `metadata-search`
/ `metadata-fetch` and their `books` counterparts, both live against the
fixture add-on (`docker/addon-index/fixture-source.yml`) and via
`docker/smoke-test.sh`.

- **An omitted `--query` echoes back the fallback it actually searched.**
  `systems metadata-search --id <SR4>` with no `--query` returned `"query":
  "Shadowrun 4 DE"` — the system's own name, not a placeholder.
- **`fields` came back in exactly the order `only_incoming`, `differs`,
  `same`** for `systems metadata-fetch --identity`: `system_family`
  (`only_incoming`, `current: null`), `parent_system` (`differs`, folder-derived
  `"Shadowrun"` vs. the fixture's `"Shadowrun (fixture)"`), `description`
  (`same`). Grouped by status, not by field-declaration order.
- **A fetch changed nothing.** `systems get` on the same system after both an
  `--identity` and a `--paste` fetch still reported `system_family: ""`, the
  field the fetch had offered a value for.
- **`--paste` resolved to the same `identity` the search returned** (
  `shadowrun-4-de`) for `--paste https://fixture.test/systems/shadowrun-4-de`,
  and the rest of the response was byte-identical to the `--identity` fetch.
- **A `game-system`-targeted add-on does not appear as a book source.**
  `books metadata-sources` on a book under the same system returned `"sources":
  []` while `systems metadata-sources` for that system listed the fixture —
  confirms target filtering, not that the endpoint is simply empty.
- **400, not 502, for both bad-input cases actually seen.** `metadata-fetch
  --identity <unknown>` against a real source returned `Bad request. {"detail":
  "that result is no longer available from the source"}`; `metadata-search
  --source-id <unknown>` returned `Bad request. {"detail":"add-on '<id>' is not
  installed"}`. No 502 was produced in these live runs — the fixture add-on
  never fails, so that path is unverified live.

## System covers

Verified against v1.5.6 by reading `backend/routers/systems/covers.py` and
`backend/routers/systems/__init__.py`, backing `systems cover get|upload|delete`
and `books thumbnail`.

- **Three sources of system cover art, in precedence order**
  (`systems/__init__.py:96`): a `cover.*`/`folder.*` image in the system's
  library folder (library-managed, not reachable through the API), then an
  uploaded cover (`system.cover_image`, under `SYSTEM_COVER_DIR`), then
  neither — `GET` 404s and a client is expected to fall back to
  `cover_book_id`, served by `GET /api/books/{id}/thumbnail`.
- **An upload can succeed and change nothing about what `GET` returns.**
  Folder art always wins over an upload, and `DELETE` never touches folder
  art — it only removes the upload.
- **`POST` is `multipart/form-data`** with the part named `file`
  (`covers.py:122-153`), FastAPI binding `file: UploadFile = File(...)`. It
  checks `file.content_type` against `image/png`, `image/jpeg`, `image/webp`,
  `image/gif`; caps at 10 MB (413); rejects an empty body; and runs
  `PIL.Image.verify()`, so a disguised file is a 400 even when the declared
  type is right. It replaces any existing upload and answers
  `{"cover_image": "<system-id><ext>"}`. `DELETE` answers `{"status": "ok"}`.
- **Books have no cover endpoint of their own.** `GET /api/books/{id}/thumbnail`
  is scan-derived from the book file, not an uploaded image, and there is no
  corresponding upload or delete for it.

## Book folders

Verified against v1.5.6 by reading `backend/routers/systems/core.py:261-288`,
`backend/models/library.py:167` and `backend/services/tag_service.py`, backing
`systems book-folders list|set`.

- **A book folder is a second, invisible tagging layer, addressed by path
  rather than enumerated.** Its path takes the form
  `{system_id}/{category}/{subfolder…}`; the model has three columns, `id`,
  `path`, `tags`. Tagging one folder covers every book at or below that path,
  resolved on read by `_book_folder_ancestor_paths` (`tag_service.py:63`). A
  book directly in the category directory (no subfolder) belongs to no
  folder.
- **A `BookFolder` row exists only once a path has been tagged.** The call to
  `upsert_folder_tags` (`routers/systems/core.py:284`) is the only site that
  inserts one; the scanner and indexer never do. So `list_book_folders`
  (`core.py:261`) returns folders that have been tagged, not every
  subcategory folder present on disk — a real, untagged subfolder has no row
  and does not appear.
- **The inheritance never reaches a book's own `tags`.** `tags_for_resource`
  (`tag_service.py:379`) reads only the `ResourceTag` join table; folder
  inheritance is resolved separately in `folder_tags_in_use` (`:509`), which
  serves the tag catalogue instead. `books get` and `systems get` do not show
  inherited tags. Within this CLI, `book-folders list` is the only way to see
  them; server-side, `folder_tags_in_use` also feeds `GET /api/tags`
  (`routers/tags/core.py:35`).
- **`PATCH` replaces the tag list**, unlike `books batch-tag` /
  `systems batch-tag`, which are additive. An empty `tags` clears the folder.
- **The `{system_id}` in the PATCH URL is ignored by the write.**
  `update_book_folder` takes it as a path parameter and never reads it —
  `data.path` alone decides which row is written, with nothing validating that
  the path belongs to that system or exists on disk. `GET` *does* filter by
  `path.like(f"{system_id}/%")`, so read and write disagree about what the URL
  means. A caller can write another system's folder through any system's URL.
- **Read and write return tags differently.** `GET` resolves stored internal
  keys to display casing via `folder_display_tags`; `PATCH` echoes the
  internal keys straight from `upsert_folder_tags`. A round trip need not
  match byte-for-byte.

## Cleanup of missing files

Verified against v1.5.6 by reading `backend/routers/maintenance/`, backing
`library cleanup-missing`.

- **An absent path deletes; a hung one does not.** `_path_exists`
  (`_helpers.py:13-30`) runs `os.path.exists` on a daemon thread with a
  5-second join and **returns True if the thread is still alive**, so a stalled
  mount is treated as present and skipped. A directory that is simply gone
  returns False promptly and every row beneath it is removed. The asymmetry is
  deliberate and is the difference between a storage hiccup and data loss.
- **It commits per row** (`_helpers.py:127`, and per pruned system at `:99`), so
  a failure part-way through leaves earlier removals applied; the handler's
  `except` rolls back only the uncommitted remainder. The docstring at
  `:110-111` gives the reason: releasing the write lock between rows so a
  concurrent scanner session is not blocked.
- **A book takes its search index and bookmarks with it**
  (`_helpers.py:122-126`): `DELETE FROM book_search`, then every `Bookmark`
  pointing at the book, then the book. Bookmarks are user data and a rescan
  does not restore them, nor any hand-entered metadata.
- **Container folders are protected from the orphan sweep.**
  `_prune_orphaned_systems` (`_helpers.py:33`) keeps a system alive if it has
  books, a campaign references it, **or it has a surviving child** — the last
  rule phrased over `parent_id` rather than `container_kind`, so container kinds
  added later are covered without changing the function. Without it a container
  (which holds no books of its own) read as an orphan and the
  `GameSystem.children` `delete-orphan` cascade took its editions and their
  books: upstream issue #309, fixed by the release this CLI targets. Systems are
  walked deepest-first so a container emptied earlier in the same pass is still
  collected.
- **A running scan is a 409**, not a queue: `"A library scan is already running;
  retry after it completes."` (`core.py`). `library scan-status` reports the
  state it refers to.
- **Live, against the seeded fixture stack:** two consecutive calls each
  answered `{"removed": {"books": 0, "maps": 0, "tokens": 0, "audio": 0,
  "systems": 0}}` with exit 0. The destructive path is not exercised there —
  every fixture file is present — so the removal counts are unverified live.

## First-run users

- Grimoire seeds users from `{DATA_PATH}/users.json` at startup
  (`backend/seed_users.py`): a JSON array of `{username, password, role}` with
  roles `admin|gm|player`, plaintext or bcrypt. It then renames the file to
  `users.json.imported`.
- That rename is unguarded and the startup call has no `except` around it
  (`backend/main.py`), so the file must sit **inside** a mounted directory —
  bind-mounting it as a single file makes the rename fail and the container will
  not start.
- `POST /api/auth/setup` also exists and creates the first admin, failing once any
  user exists. The local stack uses the `users.json` path instead, so no bootstrap
  branch is needed in scripts.
