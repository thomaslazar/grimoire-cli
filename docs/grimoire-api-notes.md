# Grimoire API notes

Behaviour verified against Grimoire **v1.5.6** — the release the live instance
runs — by reading `temp/grimoire/` at that tag and by calling the API. The local
stack now rides the 1.6.0 nightly, so a note measured there says so. Don't
re-derive these, and don't trust the published docs over them. Re-verify after a
server upgrade — see [grimoire-compatibility.md](grimoire-compatibility.md) for
the bump procedure.

`main` upstream carries unreleased work that no instance runs. Pinning the
reference clone to the release tag is not optional; reading `main` is how the
first round of wrong conclusions happened. Bulk endpoints and guarded renames
were both examples of this at v1.5.4 — both shipped in v1.5.5, see below.

## Auth

Measured against `hunterreadca/grimoire:1.6.0` — not the v1.5.6 the
rest of this file describes.

- **`HTTPBearer`.** `POST /api/auth/login` returns `{"token": …, "user": {…}}`
  and sets two HttpOnly cookies: `grimoire_session=<jwt>` (`Path=/`,
  `SameSite=lax`) and `grimoire_refresh=<opaque>` (`Path=/api/auth`,
  `SameSite=strict`). Both carry `Max-Age=2592000` whatever the JWT's real
  life, so cookie lifetime says nothing about expiry.
- **`/api/auth/setup` returns the same body shape.** The key is `token`, not
  `access_token`.
- **The access token lives 30 minutes.** Observed `exp - iat` is 1800s, per
  `ACCESS_TOKEN_EXPIRE_MINUTES` (`backend/sessions.py`, env-overridable). Claims
  are `sub`, `username`, `role`, `iat`, `jti`, `exp`, `sid`. v1.5.6 issued a
  single JWT valid 30 days and no refresh token at all.
- **The refresh token is opaque text, not a JWT**, so its expiry cannot be read
  locally. Only the server knows when it dies.
- **`POST /api/auth/refresh` authenticates on the cookie alone** — no bearer
  header, and the spec declares no bearer security on it. It returns the same
  `{"token", "user"}` shape and re-sets both cookies. `sid` is unchanged across
  a refresh: the session persists and only the tokens rotate. `rotate_session`
  also slides `expires_at` 30 days forward, so an actively used session does
  not age out.
- **It tolerates a stale `Authorization` header** — verified 200 with a
  deliberately expired token attached.
- **Replaying a rotated refresh token revokes the session.** `rotate_session`
  moves the old hash into `previous_token_hash`, and `get_active_session` reads
  a hit there as theft (`backend/sessions.py`). Verified: after rotating
  `T0 → T1`, replaying `T0` returned
  `401 {"detail":"Invalid or expired refresh token"}` *and* killed `T1`, valid
  seconds earlier. There is no grace column, timestamp or knob.
- **An expired access token is distinguishable from other 401s.**
  `get_current_user` answers it with `401`,
  `{"detail":"Token expired - please log in again"}` and the header
  `X-Token-Expired: 1`. A missing or malformed token yields
  `{"detail":"Not authenticated"}` or `{"detail":"Invalid token"}` with **no**
  such header.
- **Access tokens are not checked against the session table** —
  `get_current_user` only decodes the JWT (`backend/auth.py`). Revoking a
  session does not kill tokens already issued; they stand until their own
  `exp`. The blast radius of a revocation is the next refresh.
- **Revocation is routine, not exceptional.** A password change revokes all
  other sessions, an admin edit carrying a `revoke_reason` revokes all of a
  user's, and guest promotion or removal from a campaign revoke too. The web UI
  manages sessions directly.
- **The renewal happy path, hand-run against the local stack:** a minted
  expired access token swapped into the config produced `access token expiring
  in -60s, refreshing` → `POST /api/auth/refresh 200` → `token refresh
  succeeded` → the request succeeding, with the rotated cookie written to disk.
  CI does not cover this; the smoke test covers only the retired-session
  failure path.
- Auth endpoints are rate-limited: `AUTH_RATE_LIMIT` defaults to `10/minute`,
  and `RATE_LIMIT_ENABLED=false` disables it (`backend/security.py`). The local
  stack sets the latter. `/api/auth/refresh` is among them, because the cookie
  is a bearer credential.

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

Measured against `hunterreadca/grimoire:1.6.0` — the 1.6.0 RC, not the v1.5.6 the
rest of this file describes. Every claim below was observed against that build;
source citations name the shipped backend inside the container. Backs
`systems book-folders list|set|delete`.

- **A book folder is a second tagging layer, addressed by path rather than by
  id.** Its path takes the form `{system_id}/{category}/{subfolder…}`; the model
  has three columns, `id`, `path`, `tags` (`backend/models/library.py:214`).
  Tagging one folder covers every book at or below that path, resolved on read by
  `_book_folder_ancestor_paths` (`backend/services/tag_service.py:117`). A book
  directly in the category directory (no subfolder) belongs to no folder.
- **The verbs.** `GET` returns `{"folders": [{"path", "tags"}]}` and needs only
  an authenticated user — `list_book_folders` depends on `get_current_user`, so
  it carries no role. `PATCH` takes `{"path", "tags"}` and echoes the same shape;
  `DELETE` takes the path as a **query** parameter, not a body, and returns
  `{"status": "deleted"}`. Both writes depend on `require_gm_or_admin`
  (`backend/routers/systems/core.py:296`, `:313`, `:330`).
- **A `BookFolder` row exists only once a path has been tagged.**
  `upsert_folder_tags` (`tag_service.py:244`) is the only site that inserts one;
  the scanner and indexer never do. Measured: `core/errata` existed on disk with
  a book in it and `GET` returned `{"folders": []}` until a `PATCH` created the
  row. So `list` reports what has been tagged, never what is on disk.
- **The path must belong to the system in the URL.** `_require_owned_folder_path`
  (`core.py:281`) 404s an unknown system and 400s a path that is not prefixed
  with the system's id or has fewer than three segments. Measured: writing
  `wrong/core/x` returned `400 {"detail": "path must be
  '{system_id}/{category}/{subfolder...}' for this system"}`. v1.5.6 ignored the
  URL's `system_id` on the write, so any system's folder was writable through any
  system's URL; that is no longer true.
- **`PATCH` replaces the tag list**, unlike `books batch-tag` /
  `systems batch-tag`, which are additive. Measured: `["second"]` over
  `["Errata Fixture"]` left only `second`.
- **An empty `tags` clears the folder but keeps the row.** Measured: after
  `{"tags": []}` the folder still appeared in `GET` with an empty list. Removing
  the row is what `DELETE` is for.
- **`DELETE` removes the row and is not idempotent.** Measured:
  `{"status": "deleted"}`, then the folder gone from `GET`, then
  `404 {"detail": "Book folder not found"}` on the same path. Any repeatable
  check must create before it deletes.
- **Read and write return tags differently.** `GET` resolves stored internal keys
  to display casing via `folder_display_tags` (`tag_service.py:263`); `PATCH`
  echoes the internal keys straight from `upsert_folder_tags`. Measured:
  `PATCH ["Errata Fixture"]` echoed `["errata fixture"]` and `GET` returned
  `["Errata Fixture"]`. A round trip does not match byte-for-byte.
- **The inheritance never reaches a book's own `tags`.** `tags_for_resource`
  (`tag_service.py:435`) reads only the `ResourceTag` join table; folder
  inheritance is resolved separately in `folder_tags_in_use` (`:565`), which
  serves the tag catalogue. Measured: with the folder tagged `errata-fixture`,
  `GET /api/books/{id}` reported `"tags": []`. `book-folders list` and the tag
  catalogue (`GET /api/tags`, `backend/routers/tags/core.py:35`) are the only
  places the tags surface.
- **The subfolder depth is derived per system and handles nested containers.**
  `system_category_depth` (`tag_service.py:52`) walks the whole container chain
  for `2 + <ancestor count>`, `system_category_depths` (`:78`) returns every
  system's depth in one query for the bulk resolvers, and both stop on a cycle.
  Measured on a **container child**: with `{system}/core/errata` tagged
  `errata-fixture`, `GET /api/tags/errata-fixture/items` returned
  `{"items": [], "folders": [{"resource_type": "book", "path": "errata",
  "items": [{"title": "DSA5 Errata", …}]}]}` — a one-segment folder path — and
  `GET /api/tags` counted the book (`category: "book"`, `count: 1`). v1.5.6's
  hardcoded `parts[3:-1]` disagreed with the frontend by one segment here, so no
  path was correct for both readers
  ([hunter-read/grimoire#357](https://github.com/hunter-read/grimoire/issues/357),
  fixed).

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

## Controlled vocabularies

Read from `backend/routers/lookups/` at tag `v1.6.0`.

- **Systems and books store the vocabulary `name`, not the `id`.** Every usage
  count in `_helpers.py` matches on `name`, case-insensitively and with
  surrounding whitespace stripped (`_matches`). The `id` a lookup read returns
  addresses the vocabulary entry itself, which only `DELETE` needs.
- **No write path validates a value against a vocabulary.**
  `services/bulk_service.py:apply_updates` is a blind `setattr` loop over the
  payload; no lookup table is consulted by `PATCH /api/systems/{id}`,
  `PATCH /api/books/{id}` or either `bulk` endpoint. An unmatched string is
  stored as written, and merely stops matching `?genre=` and the server's own
  usage counts. The five lists are conventions to agree with, not enforced sets.
- **`parent-systems` ships empty.** `models/lookup_defaults.py` seeds genres,
  system families, licenses and dice materials, but `DEFAULT_PARENT_SYSTEMS` is
  `()`. A container child's `parent_system` is folder-derived, so values in use
  and values in the vocabulary diverge freely.
- **All five reads are `Depends(get_current_user)`** — no role, guests included.
  Only the `POST` and `DELETE` on each path are `require_admin`.
- **A `DELETE` strips nothing.** It removes the vocabulary row only; every system
  and book carrying that name keeps it, because the value is a string rather than
  a foreign key. The response field is named `removed_usage` but reports the
  count that *would* have blocked the delete. Deleting a genre cascades to its
  child genres.

## Backups

Read from `backend/routers/backups/core.py` and
`backend/services/backup/_config.py` at tag `v1.6.1`; the router and service are
byte-identical to `v1.6.0`.

- **There is no restore endpoint, and no upload.** The six endpoints are list,
  create, settings read/write, delete and download. An archive can be taken and
  fetched; putting one back is out of band.
- **`POST /api/backups` snapshots the database under a read lock**, so writes are
  held off for its duration, and answers **409** when a backup is already in
  flight (`RuntimeError` → `HTTPException(409)`). An `OSError` becomes a 500.
- **The archive does not contain the library.** It holds the database plus
  `campaign_uploads`, `system_covers` and `audio_covers`
  (`services/backup/_archive.py`), and its own manifest names the excludes:
  `library (mounted read-only; back it up separately)`, `thumbnails` and
  `page_cache`, the last two regenerating on demand. So a backup taken before a
  destructive file operation protects the catalogue, not the files.
- **`DELETE` answers 204** with no body, and is irreversible.
- **`PUT /api/backups/settings` is a partial patch despite the method.** Every
  `BackupSettingsPatch` field is optional and omitted ones are left alone. It
  returns the full effective settings rather than `{"status": "ok"}`.
- **`backup_schedule_hour`, `_minute`, `_weekday` and both retentions are
  silently clamped**, not refused: `max(0, min(23, hour))`, `min(59, minute)`,
  `min(6, weekday)`, `max(0, …)`. The response is 200 and reports the clamped
  value, so a caller who does not read it back cannot tell.
- **Four fields are env-lockable** — `backup_schedule`,
  `backup_retention_count`, `backup_retention_gb`, `backup_dir` — and writing a
  locked one is a **400**. The clamped numeric fields are *not* lockable, and
  both retentions are in both sets: they clamp *and* lock.
- **`backup_schedule` is a closed set**: `off`, `hourly`, `daily`, `weekly`.
- **`weekday` is 0=Mon … 6=Sun.**
- **`backup_dir: ""` resets to `DATA_PATH/backups`**, and a non-empty path is
  checked for existence and writability at save time rather than at the next
  scheduled run.
- **`GET /api/backups` reports `directory` and `total_bytes`** alongside the
  rows, and each row's `version` is `"unknown"` when the archive's manifest is
  unreadable — which is what makes a cross-version restore detectable.
