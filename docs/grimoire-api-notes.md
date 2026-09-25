# Grimoire API notes

Behaviour verified against Grimoire **v1.5.6** — the release the live instance
runs — by reading `temp/grimoire/` at that tag and by calling the API. The local
stack runs the `1.7.2` release, so a note measured there says so. Don't
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
- **A tag may not contain `/` or `\`**, as of 1.7.0 (`services/tag_service.py`'s
  `TAG_FORBIDDEN_CHARS`, applied by every request schema that accepts tags). A
  slash reads as a path separator in `/api/tags/{internal}`, and Grimoire has no
  subtags, so `Storage/Box1` was a flat tag that could be created but never
  renamed or deleted (upstream #430). Validation is on **input only**: the bulk
  add path and the duplicate-merge field copier fold stored tags back in and a
  legacy tag must not 500 an unrelated write, and a `tags.json` bypasses the
  check entirely because Grimoire treats that file as the user's own. The tag
  routes took `{internal:path}` in the same release, so a legacy slashed tag is
  still reachable by `tags items` and can be renamed out of trouble.
- **`GET /api/tags` counts are live, as of 1.7.1** (`routers/tags/core.py`'s
  switch to `tag_service.live_link_counts`). A tag's `count` now comes from the
  same live-resource resolution `/items` uses, so a tag whose carriers are gone
  reads 0 rather than counting dead links. Counts can drop across the 1.7.0 →
  1.7.1 upgrade with nothing having been untagged.
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

### Tag writes

- `PATCH /api/tags/{internal}` re-keys. The internal key follows the new
  display whenever its lowercased form changes, folder `tags.json` entries are
  rewritten onto the new key, and if another tag already owns that key the two
  are **merged**, with the survivor returned
  (`services/tag_service/_admin.py:68-130`, v1.7.1).
- `POST /api/tags/{internal}/merge` moves item links only. Folder tags are
  untouched (`routers/tags/core.py:208-237`), so a tag carried by a folder is
  still carried by it after the merge and reappears in `tags list`. The 404 is
  about a missing catalog row, not about having no links, and a folder tag
  normally has one, written through the API or registered by a `tags.json`
  scan (`services/tag_service/_folders.py:63`, `indexer/tags.py:81`) — which is
  why merging a folder-derived tag succeeded when verified live against 1.7.1.
  But `library cleanup-missing` reaches `prune_orphan_tags`
  (`services/tag_service/_admin.py:156`), which deletes any `Tag` row with no
  `ResourceTag` link (`routers/maintenance/_helpers.py:231`) — a folder tag has
  none by construction, its count being derived at read time — so after a
  cleanup run a tag carried only by folders has no catalog row and `merge`
  404s on it, while `rename` still works because it materialises a row first.
  The target is created if missing; a self-merge 400s.
- `POST /api/tags` is idempotent by internal key and answers 201; `display`
  defaults to the value's own trimmed casing (`_catalog.py:22`).
- `DELETE /api/tags/{internal}` answers 204 and strips folder associations as
  well as item links; the library is read-only, so a `tags.json` tag returns on
  the next rescan (`routers/tags/core.py:239-257`).
- `/` and `\` are rejected in a created value, a new display and a merge
  *target*, but not in a merge *source* — merging is the documented way out of
  a tag created before that rule (`_catalog.py:40`, `routers/tags/_schemas.py`).

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
- **The index setting is a comma-joined list**, as of 1.7.0. `AddonSettingsUpdate`
  takes `index_urls` as well as `index_url`, and the plural wins when both are
  sent; either way the value is stored in one setting and split back out on read.
  `addons list` and `addons settings` therefore answer with both, `index_url`
  being the first entry (or `default_index_url` when the list is empty). Entries
  gained `available_in`, `changelog` and `source_url`, and `addons refresh`
  reports per-index `errors` alongside its `count`. The CLI still sets one URL:
  `--index-url` writes the singular field, which is the whole list.

### Trusted add-on index URLs

- `GET /api/addons/verify-index` normalizes both the supplied URL and every
  trusted URL before comparing (`addons/constants.py:42-49`), so a URL differing
  only in normalization still verifies — which is why comparing against
  `addons list`'s `trusted_index_urls` client-side gives the wrong answer. It is
  guarded by `get_current_user` and carries no role; an omitted `url` yields
  `verified: false` rather than an error.

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

### Cover from-source

- **`POST /api/systems/{id}/cover/from-source` accepts `map`, `token`, `book`
  or `audio`; `campaign_file` is rejected by the route's own request schema,
  not by a downstream campaign lookup.** `SystemCoverSourceIn.known_source`
  builds its allowed set as every `services.image_source.SOURCE_TYPES` entry
  except `campaign_file` and raises on anything else
  (`routers/systems/_schemas.py:298-306`), so sending `campaign_file` here is
  a 422 validation error, never a 400 — the wider `SOURCE_TYPES` tuple in
  `services/image_source.py:32` includes it only for the callers (the banner)
  that do have a campaign in context. Verified live: `campaign_file` answered
  422 with `"source_type must be one of map, token, book, audio"`.
- The bytes are copied in exactly as an upload does, so folder cover art still
  takes precedence (`routers/systems/covers.py:161-181`).

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

## Library statistics

- `GET /api/stats` carries two size fields that are not the same number:
  `total_size_mb` is books only, while `library_size_mb` adds maps, tokens,
  audio and models (`routers/library/_schemas.py:77-79`, `core.py:150`, v1.7.1).
  Both scope the book portion to what the caller may see, so a restricted
  book's bytes stay out of either total.

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

Read from `backend/routers/lookups/` at tag `v1.7.1`.

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
  child genres. Verified live against 1.7.1: a forced `licenses delete` against a
  value one system carried returned `{"status":"ok","removed_usage":1}`, matching
  the `usage_count` the unforced attempt's 409 had reported, and the system kept
  the license string afterward.

### Vocabulary writes

- Built-in entries are deletable and not restorable — read from source, not
  verified live: `create` always returns `is_default: false`, so there is no
  way to construct an entry with `is_default: true` to delete and confirm
  against. No delete handler checks `is_default`, and the defaults are seeded
  by one-time migrations (`migrations/versions/0004_expand_metadata.py`,
  `0006_parent_system_licenses.py`) rather than re-seeded on boot.
- `create` matches an existing name case-insensitively (`ilike`) and 409s;
  `genres create` 404s on an unknown `parent_id`; `dice-materials create`
  coalesces a blank or omitted `group` to `"Custom"` (`core.py:278`).
- Usage is counted by name, case-insensitively. Genres and licenses count
  systems and books; system-families, parent-systems and dice-materials count
  systems only — `Book` has no `system_family`, `parent_system` or
  `dice_materials` column (`routers/lookups/_helpers.py:74-127`).

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

## Files

Read from `backend/routers/files/core.py` and `backend/services/library_fs/` at
tag `v1.7.0`.

- **Every write here needs the library mounted read-write.** Grimoire probes
  writability up front with `os.access` (`services/library_fs/paths.py`'s
  `assert_writable`) and answers **409**; each write module also catches `EROFS`
  as a backstop, so an unwritable mount is refused either way. The mount is the
  only thing gating the whole API.
- **`POST /api/files/delete` is *soft* by default and handles folders.**
  `delete_files: false` removes the index entries at or beneath the path —
  `_records_under` matches by path prefix — and leaves every file alone, so the
  next rescan re-adds whatever is still on disk. It skips the writability probe,
  so it works on a read-only library. The library root and the collection folders
  are refused either way. `confirm_name` is read only on the hard path;
  `unindex_path` ignores it.
- **A soft delete also prunes the `GameSystem` row**, as of 1.6.2
  (`deletes.py::_unindex_system_row`) — `_records_under` knows only path-keyed
  file rows, so unindexing a system folder used to forget its books and leave the
  shelf itself standing. The row goes only when it has no books and no child
  systems left, and never when it carries a custom name, a description, a cover,
  or a campaign reference. It is counted in `records`.
- **`DELETE /api/files/folder` is deliberately not implemented in the CLI.** It
  calls the same `fs.delete_path` with the same arguments as
  `POST /api/files/delete` does under `delete_files: true`, and carries no
  folder-only guard — `delete_path` deletes a plain file just as happily. So it
  offers nothing `files delete --delete-files` does not, while being reachable
  *without* naming the destructive flag. The `files folder` group description
  says where deletion lives instead. Do not add a command for it on a
  coverage-gap sweep.
- **A hard delete is not a trash move.** The file is deleted outright, and its
  index entry goes with its tags, favorites, bookmarks, progress and campaign
  links.
- **428 `confirm_required`** guards a folder that still holds content, until
  `confirm_name` matches the folder's own name. An empty folder, or one holding
  only markers and empty descendants, needs no confirmation.
- **The `on_conflict` defaults differ by endpoint**, deliberately: `upload`
  defaults to `rename` (an upload is an explicit "add this"), `move` to `skip` (a
  bulk reorganisation should step over a collision and report it). Neither ever
  overwrites — `_dest_for` has no overwrite branch.
- **`upload` does not validate `on_conflict`; `move` does.** `move`'s schema
  carries `pattern="^(skip|rename)$"` and 422s on anything else. `upload`'s is a
  bare `Form(...)` field, and `_dest_for` treats anything that is not `"skip"` as
  rename — so an unknown value silently renames and answers 200.
- **`upload` is one file per request by design**, so a large import that fails
  partway can report and retry precisely. 8 GiB cap → **413**. The file lands
  under a temporary name and is renamed into place only once fully written.
- **`browse` is DB-aware and bounded.** An entry carrying `record_id` is in the
  catalogue; one without is present on disk but not indexed — which is how "did
  my upload land *and* index?" is answerable at all. `limit` is silently clamped
  to `max(1, min(limit, 2000))`
  and `total`/`truncated` report what was withheld. `child_count` per folder row
  stops at 1000. Each row also carries `accepts_container_kind` and
  `accepts_frames_marker`, and the response carries the same pair about a *new
  child* as `children_accept_container_kind` / `children_accept_frames_marker` —
  computed server-side so the nesting rules stay with the scanner rather than
  being re-derived by a caller.
- **Container kinds** are `parent`, `one-page`, `agnostic`, `family`,
  `publisher`, `generic`. **`one-page` and `agnostic` are singletons** — only one
  of each may exist, recognised only at the top level of `books/`, and `browse`
  reports `singletons_taken` as `{kind: path}`.
- **A folder marker is refused where it would be inert**, as of 1.7.0
  (`library_fs/folders.py`'s `accepts_container_kind` / `accepts_frames_marker`).
  A container kind says "my children are game systems", which only the books
  scanner reads, so it is allowed only inside `books/` at a depth reached through
  containers alone — one level deeper is a *system* folder, whose children are
  categories. The frame marker is read only by the `token-frames` walk, so it is
  allowed anywhere under `tokens/` except `tokens/` itself, at any depth and with
  no precedence chain. Anything else is a 400 `invalid`. Both guards cover
  *setting* only: clearing is always allowed, so a marker written by hand in the
  wrong place stays removable through the API.
- **`frames_container` is a third marker, independent of the container kinds.**
  It declares that a token folder's images are token-editor frame art and says
  nothing about how the children relate, so it is reported and set separately
  from `container_kind` on `create`, `markers` and every `browse` row.
- **`scaffold` is idempotent**, creating Core, Supplements, Adventures, Character
  Sheets, Maps, Handouts, Homebrew and Starter Sets, and reporting `created` and
  `existing`.
- **`scaffold` takes a system folder, not a container.** As of 1.6.2 the guard is
  `is_category_host` rather than a depth test: under `books/`, at least one level
  down, and not itself a container — a container's children *are* the system
  folders. Anything else is a 400 `invalid`. `browse` answers the same question
  per row and for the browsed folder, as `category_host`, so a caller need not
  re-derive it. The old depth test also mis-read a system nested under two
  containers, creating one category folder instead of eight (upstream #412/#413).
- **Folder-tag rows follow the tree, as of 1.7.1** (`library_fs/deletes.py`'s
  `_purge_folders`, `moves.py`'s `_relink_folders`, upstream #445). Deleting a
  directory now removes the folder-tag rows at and beneath it, and a move or
  rename carries them onto the new path with their descendants; a destination row
  that already exists absorbs the source's tags rather than colliding on the
  unique path. On 1.7.0 both left the row behind — tags silently stopped applying
  to everything inside, while the row lived on as a folder the tags view still
  listed for a directory that no longer existed. **A row for a path that was
  never on disk is unreachable on either version**: the purge runs only from the
  real-directory delete path, so a folder-tag write to a typo'd path is permanent.
  Book folders are deliberately exempt — a `BookFolder.path` is
  `{system_id}/{category}/…` rather than a disk path.
- **`DELETE /api/files/folder` carries a request body**, which is unusual for a
  DELETE and is what the generated builder expects.

## Maps

Read from `backend/routers/maps/core.py` and `_schemas.py` at tag `v1.7.0`, and
measured against the running 1.7.0 stack.

- **`limit` defaults to 100000 with no ceiling.** `Query(100000)` and no `le=`,
  so an unflagged `GET /api/maps` returns the whole library. `tokens`, `models`
  and `audio` declare the same. `books` is the outlier at `Query(100, le=500)`.
  The CLI supplies its own default of 100 here, which is the one place `maps
  list` holds an opinion the server does not.
- **Paging is implemented twice and `folder` picks which.** Without it the
  server pages in SQL (`q.offset(offset).limit(limit)`); with it the whole
  subtree is materialised and sliced in Python (`filtered[offset : offset +
  limit]`). A negative limit is therefore unlimited in the first branch and
  "drop the last row" in the second. The CLI refuses one with
  `OptionHelpers.Range(1)`.
- **`folder` is an exact match against `folder_path`, not `relative_path`, and
  the leading path segment is stripped.** Membership is
  `_folder_path(m.relative_path) == folder`, and `_folder_path` drops
  `Path(relative_path).parts[1:-1]` — the first segment (the collection root,
  `maps/`) and the filename. Measured: a map at `relative_path`
  `maps/battlemaps/Crossroads.png` reports `folder_path` `battlemaps`. So
  `folder=battlemaps` returns it and excludes `battlemaps/caves`, while
  `folder=maps/battlemaps` matches nothing and comes back `{"total": 0}` with no
  error — the value simply is not in the index. Always pass what `maps get`
  reports as `folder_path`, never `relative_path`.
- **A grid override is cleared by sending `0`, and only the single PATCH
  honours it.** The validator normalises `0` to `None` (`round(v, 2) or None`),
  which `exclude_none=True` would swallow; `update_map` re-applies the clear
  from `model_fields_set`, and `bulk_update_maps` does not. A batch can set a
  grid, never clear one. Measured: `{"grid_px":0}` through `maps update` read
  back as null.
- **An explicit `null` is a silent no-op on the single PATCH.** `update_map`
  dumps with `exclude_none=True`, so `{"description": null}` answers
  `{"status": "ok"}` and changes nothing; `""` is what clears `description`,
  `map_type` or `grid_size`. The grid fields are the exception, re-applied from
  `model_fields_set` as above.
- **The bulk endpoints pass no `validate` hook, so only an unresolved id is a
  per-item error.** `bulk_update_maps` and `bulk_add_map_tags` call
  `bulk_service.run_bulk_update` / `run_bulk_add_tags` without one, and the only
  entry they can append to `errors` is `"Map not found"`. Anything a schema
  rejects — `grid_width`/`grid_height` outside 0-1000, `grid_px` outside 0-2000,
  a tag carrying `/` or `\` (`TAG_FORBIDDEN_CHARS`) — fails Pydantic on the
  envelope, so the request 422s and nothing is written. `books` differs: it
  passes `validate=_apply_access`, which is what makes skip-and-continue true
  there and not here.
- **`grid_warning` is advisory.** `PATCH /api/maps/{id}` answers `{"status",
  "grid_warning"}`, and the warning rides along when a saved override looks
  implausible for the map's pixel dimensions. The write succeeded regardless,
  so the CLI exits 0.
- **`GET /api/maps/{id}` carries two grids.** `grid` is what detection found,
  with its own `source`; `grid_width`/`grid_height`/`grid_px` are the stored
  override. All three null means detection is in charge.
- **Folder-tag inheritance does not reach `GET /api/maps/{id}`.** `get_map`
  resolves `folder_tags` from `MapFolder.filter_by(path=folder_path)` — an exact
  match — while `tags` and `search` bucket by `_ancestor_folder_paths`. Tagging
  `battlemaps` therefore shows up for a map under `battlemaps/caves` in those
  two and reads back as `folder_tags: []` on the map itself.
- **Folder tags read and write differently.** `GET /api/map-folders` resolves to
  display casing; the PATCH and the bulk echo the stored internal keys. Same
  asymmetry as `systems book-folders`.
- **Every batch body caps at 1000 and none may be empty.** `items`, `ids`,
  `folders` and `tags` each carry `min_length=1`, so an empty batch is a 422
  rather than a no-op.
- **`map-folders` has no delete**, unlike `systems book-folders`, and gains a
  bulk verb book folders have no counterpart for. `tag_service.upsert_folder_tags`
  inserts a row for any path string without checking the tree, so a typo'd path
  creates a row that nothing can ever remove.
- **`GET /api/maps/{id}/page/{n}` streams an image map as stored; `--width` is
  a no-op on it.** Verified live against a raster fixture map: page 1 with and
  without `--width` produced byte-identical output. Width only affects
  rendering a PDF page; page 2 on an image map 400s with `"Image maps have
  only one page"`.

### Universal VTT routes

- `GET /api/maps/{id}/vtt/data` is JSON, not a download: it is registered with
  `response_model=VttDataResponse` and returns the grid resolution and
  wall/portal/light counts with the embedded image omitted
  (`routers/maps/__init__.py:115-126`). Its sibling `vtt/image` serves the
  picture. Both 400 unless the map is a `.uvtt`/`.dd2vtt`.
- `GET /api/maps/{id}/export.uvtt` is a download whose payload is JSON: the
  handler returns a `Response` carrying the image as base64 WebP plus the grid
  and any authored geometry (`routers/maps/core.py:340-460`). A raster map is
  the normal case — verified live, a plain PNG fixture map exported 200
  `application/octet-stream` with top-level keys `format`, `resolution`,
  `image`, `environment`, `lights`, `line_of_sight`, `objects_line_of_sight`,
  `portals`. It 400s for PDF, video and archive maps, and for a raster already
  linked to a Universal VTT sibling.

## Models

Read from `backend/routers/models/core.py` and `_schemas.py` at tag `v1.7.1`,
and measured against the running 1.7.1 stack.

- **`is_supported` accepts true and false reversibly; only `null` is a
  one-way trip.** The column is tri-state — true, false, or null meaning "the
  scanner could not tell" — and `update_model` applies
  `model_dump(exclude_none=True)` with no `model_fields_set` re-application
  (`core.py:195`), so a sent `null` is dropped and the write answers
  `{"status": "ok"}` having changed nothing. Sending `true` or `false` writes
  normally in either direction, including back over the other value — nothing
  about the field itself is one-way. What cannot happen is returning to
  unknown once set: a model can leave null but never come back to it. Maps
  rescues a sent `0` deliberately (`maps/core.py:630-633`); nothing here
  rescues a `null`. Measured: `null` after `true` read back as still
  presupported; `false` after `true`, and `true` after `false`, both read back
  changed.
- **Read and write disagree about the same fact.** `Model3DOut` exposes the
  derived pair `is_presupported` / `is_unsupported` (`_schemas.py:43-52, 66-67`),
  both false when unknown; the write path takes the single `is_supported`. The
  field a caller reads is never the field it writes.
- **`GET /api/models` takes `limit` and `offset` only** (`core.py:32-33`), with
  `Query(100000)` and no `le=`. No folder or type filter, so unlike maps this
  endpoint only ever pages in SQL and a negative limit has one meaning.
- **Explicit rows are filtered server-side per account** (`core.py:37-40`), and
  variants never reach the list (`core.py:38`).
- **`bulk_update_models` passes no `validate` hook** (`core.py:200-212`), as on
  maps. Only `"Model not found"` reaches `errors`; a schema-invalid item 422s
  the whole batch with nothing written.
- **Supported/unsupported is inferred from the whole relative path — folder or
  filename.** `_detect_support` matches both regexes against the path with the
  filename included (`indexer/media.py:365-377`), so `goblin_unsupported.stl`
  is detected in an untagged folder; the convention it targets is folder-level,
  `Goblins/Presupported/goblin_a.stl`. Unsupported is tried first, since
  "unsupported" contains "supported".
- **`.stl` is the only format that renders a thumbnail**
  (`indexer/models3d.py:67`); `serve_model_thumbnail` 404s on a miss rather than
  serving a placeholder (`core.py:183`).
- **Folder tags read and write differently**, as on maps: display casing on the
  read (`core.py:76`), stored internal keys echoed by the PATCH and the bulk
  (`core.py:91`, `core.py:237`). `model-folders` has no delete.

## Tokens and audio

Read from `backend/routers/tokens/` and `backend/routers/audio/` at tag
`v1.7.1`, and measured against the running 1.7.1 stack. The `## Models` facts
about the missing `validate` hook, the dropped `null`, and folder-tag handling
hold unchanged on both; only the differences are recorded here.

- **`audio` has no `is_explicit` at all** — not on the row
  (`audio/core.py:30-44`), not on `AudioUpdate` (`audio/_schemas.py:10-12`),
  and `list_audio` (`audio/core.py:51-58`) takes only `limit`, `offset` and
  the session. `tokens` does filter per account (`tokens/core.py:34-37`), as
  `books` and `models` do. A caller cannot hide an audio track from a player
  by marking it explicit, because there is nothing to mark.
- **`audio` carries four scan-derived fields no endpoint can write.**
  `duration`, `title`, `artist` and `album` are read from the file at index
  time (`indexer/metadata.py:25-52`) and appear on the row
  (`audio/core.py:37-40`); `AudioUpdate` declares only `description` and
  `tags` (`audio/_schemas.py:10-12`). Measured: a tagless WAV indexes with a
  real `duration` and empty strings for the other three.
- **`GET /api/audio/{id}/artwork` resolves three sources, then 404s**
  (`audio/core.py:145-170`): a cover set deliberately through the UI, then
  folder art, then art embedded in the file. `has_artwork` on the row says
  whether any exists. `has_cover` (`audio/core.py:44`) is true only for the
  first of the three.
- **The two list endpoints order differently.** `tokens` orders by
  `relative_path` (`tokens/core.py:41`), so a page is a contiguous run of
  folders in display order, as `maps` does. `audio` orders by `filename`
  (`audio/core.py:58`), so a page can straddle folders.

### Audio covers

- `GET /api/audio/{id}/cover` serves only the deliberately-set cover and 404s
  when there is none, even on a track with folder or embedded art
  (`routers/audio/covers.py:149-165`, v1.7.1). `GET /api/audio/{id}/artwork` is
  the one that resolves all three in order. The split is deliberate upstream: it
  is how an editor tells "a cover was set here" apart from "the folder happens
  to have one". `has_cover` is the read-side signal for whether a set cover
  exists — `audio get`/`audio list` carry it, no `cover` request needed.
- `DELETE /api/audio/{id}/cover` clears the set cover and then **recomputes**
  `has_artwork` from folder art and embedded art (`covers.py:128-146`). As
  observed live, this recompute does not always leave `has_artwork` true: on a
  fixture track with no folder or embedded art, it went `false` → `true` (after
  `from-source`) → `false` (after `delete`), tracking `has_cover` in lockstep.
  That's consistent with the recompute being able to keep `has_artwork` true on
  a track that does have folder or embedded art — this fixture didn't have any
  to exercise that path.
- `POST /api/audio/{id}/cover` checks `content_type` first, then the size
  ceiling, then decodes: an unsupported type is 400, an oversized file 413, an
  empty one 400 (`covers.py:91-109`).
- `AudioCoverSourceIn` excludes `campaign_file` from its allowed set exactly as
  `SystemCoverSourceIn` does (`routers/audio/_schemas.py:100-118`), so the two
  `from-source` verbs behave identically, 422 included.

## Search

Read from `backend/routers/search/core.py`, `backend/routers/search/_query.py`,
`backend/routers/search/_books.py` and `backend/routers/search/_helpers.py` at
tag `v1.6.2`; the limit behaviour was verified against the running 1.6.1 stack.

`GET /api/search` is not full-text-only. As of 1.6.2 it returns six
independently-populated arrays — `results` (page text), `book_matches`, `maps`,
`tokens`, `audio`, `models` — and `total` counts every row across all of them,
so a book matching by title *and* by page text is counted twice.

- **`limit` bounds `results` alone.** `book_matches` is capped at
  `TITLE_MATCH_LIMIT = 50` and each media set at a literal `.limit(50)`,
  neither reachable from the query string. There is no offset, so those five
  are a hard ceiling rather than a page.
- **`limit` has no lower bound, and `-1` means unlimited.** The route declares
  `Query(50, le=200)`, which 422s above 200, but the value reaches SQLite as a
  bare `LIMIT :limit`. Verified against a 126-page index: `limit=-1` returned
  all 126 rows with a 200, and `limit=0` returned none. The CLI guards this
  with `OptionHelpers.Range(1, 200)`.
- **An unrecognised `field:` prefix is not an error.** It falls through to
  free text and is searched literally, because a colon is ordinary
  punctuation in a title. `q=titel:dsa` answers 200 with every array empty
  and `fields: []`, which is the only way to tell a typo from a genuine miss.
  A recognised metadata filter suppresses the page-text search; `text:`
  forces it back.
- **`campaigns/resources/search` has no command, deliberately.** It is a
  resource picker: `q` is optional so it enumerates a whole type, and its cap
  is 20000 per type rather than 50. That makes it the only way to list maps,
  tokens or audio without a search term — but those resource types have no
  commands yet, and each will arrive with its own paginated `list`. For
  books, `books list` already enumerates with `--offset` and `search` already
  filters. Do not add a command for it on a coverage-gap sweep; revisit it
  with the maps, tokens and audio blocks.

## Logs

Read from `backend/routers/logs/core.py` and `_schemas.py` at tag `v1.6.2`, and
measured against the running 1.6.2 stack.

- **An in-memory ring buffer of 20 000 entries**, with no disk history behind it.
  Anything older is gone.
- **DEBUG is always available regardless of `LOG_LEVEL`.** The env var governs
  console output; the handler feeding this buffer is installed at DEBUG. So the
  endpoint returns detail an operator cannot see in `docker logs`.
- **`level` is a minimum and hierarchical.** On a freshly booted stack: `debug`
  totalled 107, `info` 8, `error` 0.
- **A page is taken from the newest end and returned oldest-first.** With the
  eight `info` entries at seq `[1, 2, 7, 9, 10, 100, 102, 103]`, `limit=2` gave
  `[102, 103]`, `limit=2&offset=2` gave `[10, 100]`, and `limit=3&offset=5` gave
  `[1, 2, 7]`.
- **`offset` is ignored once `after_seq` is set** — `after_seq=100` and
  `after_seq=100&offset=2` returned the identical page.
- **`max_seq` tracks the whole buffer, not the filtered set.** `level=error`
  returned `entries: []` with `max_seq: 107`, so a poll filtered to a level that
  matches nothing still advances the cursor.
- **A poll truncates from the oldest end.** With `after_seq` set the handler
  returns `new[-limit:]` (`config.py:466`) — the *newest* `limit` of what is
  new, not the oldest. Measured: 96 entries arrived and a poll with `limit=3`
  returned seq `[1471, 1472, 1473]`, skipping 93 that advancing the cursor then
  loses for good. A caller catching up on a backlog must raise `limit`.
- **`total` counts what matches `level`**, not what the page holds.

## Duplicates

Read from `backend/routers/duplicates/__init__.py`, `core.py`, `detection.py`,
`_helpers.py`, `backend/models/variants.py`, `backend/services/variants.py` and
`backend/services/library_fs/deletes.py` at tag `v1.6.2`, and verified against
the running 1.6.2 stack.

- **`DELETE /items/{resource_type}/{item_id}` deletes the file by default.**
  `DeleteItemRequest.delete_file` is `True`, and an omitted body becomes
  `DeleteItemRequest()`, so a bodyless call removes the file. That is the inverse
  of `POST /api/files/delete`, which is soft by default — and deliberate:
  `delete_file: false` keeps the file, which the next library scan re-indexes,
  putting the duplicate back. The CLI requires the value rather than defaulting
  it either way.
- **What `delete` removes does not depend on the flag.** The row goes either way,
  and `purge_references` takes the `book_search` page rows, bookmarks, favorites,
  tags, and campaign resource links with their shares; `_purge_derived` drops the
  thumbnail and page cache. `--delete-file` decides only whether the file leaves
  the disk, with its sidecars. `ENOENT` is tolerated, so a record whose file is
  already gone still deletes and reports `file_deleted: false`; `EROFS` is a 409
  with nothing committed.
- **`/link` validates per child, not per request.** `validate_kind` and every
  structural guard run inside the loop, so a bad id, a repeated id or an
  unaccepted kind lands in `errors` while the remaining children commit at
  HTTP 200 — the exit-3 contract. Capped at 20 children.
- **`mergeable_fields` never reaches the client.** `/compare` declares
  `response_model=CompareResult`, which omits it; the `CompareResponse` model
  that carries it is unused by the route. Verified: a compare response holds only
  `differences`, `items`, `page_count_min`, `resource_type` and
  `suggested_parent_id`. So nothing exposes the copyable set, and the only way to
  learn it is the 400 from `/merge-metadata`, which lists it.
- **`/unlink` with neither `ids` nor `parent_id` is a silent no-op**, answering
  `200 {"unlinked": []}`. `parent_id` wins when both are given.
- **`variant_label` is trimmed to 120 characters silently**
  (`services/variants.py:215`), on both `/link` and `/promote`.
- **`/groups` has no lower bound on `limit`.** Declared `Query(50, le=200)`; a
  negative value slices to an empty page and answers 200.
- **`/scan` has two conflict paths.** A *library* scan in flight is a 409; a
  *duplicate* scan in flight is `200 {"status": "already_running"}` with nothing
  started. `/cancel-scan` answers `not_running`, `stop_requested` or
  `cleared_stale`, all 200.
- **A duplicate scan whose heartbeat went stale blocks nothing** (upstream #304,
  new in 1.6.2). `ScanStatus.heartbeat` is what distinguishes a slow scan from an
  abandoned one: `/scan` calls `force_clear` and starts anyway
  (`detection.py:43`), and `/cancel-scan` clears the record outright and reports
  `cleared_stale` (`detection.py:71`). Before that a killed worker left `running`
  true for good and refused every later scan.
- **`resource_type` is a body or query field on twelve of the thirteen routes** —
  only the item delete carries it in the path. So one command group covers books,
  maps, tokens, audio and models without those command groups existing.
- **`model` joined the vocabulary in 1.6.2**, and with it four variant kinds
  (`presupported`, `unsupported`, `split`, `merged`) and its own mergeable set
  (`description`, `is_explicit`, `is_supported`, `tags`). The four existing
  collections' kind and mergeable sets are unchanged: 1.6.2 moved them into
  `models/collections.py` verbatim.

## Book reading

Read from `backend/routers/books/pages.py` and `backend/indexer/formats.py` at
tag `v1.7.1`, the release the local stack runs, and verified against that
stack. Backs `books file`, `books page`, `books toc`, `books page-text` and
`books page-words`.

- `GET /api/books/{id}/page/{n}/text` reads the `book_search` FTS row for that
  page and only extracts live when there is none
  (`routers/books/pages.py:221-245`). Its format gate is `can_index`, not an
  `application/` MIME prefix, so `text/plain` and `text/markdown` books are
  readable too (`:215-219`).
- `GET /api/books/{id}/page/{n}/words` answers **200 with an empty overlay**
  (`{"width": 0, "height": 0, "words": []}`) for any book outside the `fitz`
  format family (`routers/books/pages.py:256-259`). Only PDF, EPUB and DjVu are
  `fitz`; `.txt`/`.md`/`.rtf` and the comic archives all get the empty overlay
  (`indexer/formats.py:71-85`). Its `page-text` sibling only 404s for the
  comic family — the text family is `can_index`, so `page-text` succeeds there
  while `page-words` still returns the empty overlay. An empty result
  therefore does not distinguish "not a renderable document" from "no words on
  this page"; `width` does.
- A page outside the book is **400 with the real page count** in the message,
  not 404 (`routers/books/pages.py:237-238, 243-244, 270`).
- `GET /api/books/{id}/toc` covers EPUB as well as PDF: the handler gates on
  `is_fitz_mime`, and PyMuPDF exposes an EPUB's nav document through the same
  API as a PDF outline (`routers/books/pages.py:64-77`). Its 404 is bare, so an
  unknown id and an unopenable format are indistinguishable from the response.
- `GET /api/books/{id}/file` and `GET /api/books/{id}/page/{n}` both **write on
  a missing file**: they set the book's `is_missing` to true and commit before
  raising 404 (`routers/books/core.py:388-392`; `pages.py:130-131, 140-141,
  179-180`, the file route's one site and the page route's three). A read that
  mutates is worth knowing about when a scripted sweep hits a library whose
  files have moved.
- `GET /api/books/{id}/page/{n}` defaults `width` to **1200**, where the maps
  equivalent defaults to 1600; both cap at 3000 (`pages.py:106`). Measured live:
  the same fixture page rendered at 8444 bytes with no `--width` and 1476 bytes
  with `--width 400` — a genuine re-render, not a passthrough. It renders PDF,
  EPUB and DjVu, serves a comic archive's page as the stored image member
  without rendering (`pages.py:138-146`), and serves a single-page image book
  as stored on page 1 only (`pages.py:126-134`). That makes it the one page
  read that works on a comic.

## Archive downloads

Read from `backend/routers/downloads/` at tag `v1.7.1`, the release the local
stack runs, and verified against that stack. Backs `downloads archive`.

- `GET /api/downloads/archive` selects among eleven scopes through `type`, each
  requiring a different combination of `id`, `category`, `tag`,
  `resource_type` and `folder` (`routers/downloads/core.py:27-146`). A missing
  one is 400 naming both the flag and the type.
- `library_folder` is admin-only, checked inside the handler rather than on the
  route, and reads a folder as it sits on disk — including files the scanner
  never indexed, unfiltered by book visibility, because nothing in it resolves
  through a book row (`core.py:131-141`).
- `_archive_response` checks the files list **before** the format, so an empty
  scope answers 404 even when `fmt` is invalid too — an unsupported format only
  surfaces as its own 400 against a scope that resolves to at least one file
  (`routers/downloads/_helpers.py:98-101`). An unknown `fmt` is 400 listing the
  valid ones; an unknown `type` is a separate 400 from the handler itself.
- The archive is built entirely in memory before any of it is sent: `_stream_zip`
  and `_stream_tar` write the whole thing into a `BytesIO`, then `seek(0)` and
  yield it in 64 KiB chunks (`_helpers.py:72-91`). Only the headers go out
  early — the client's request timeout covers the full server-side build, not
  just the time to first byte.
