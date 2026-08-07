# Grimoire API notes

Behaviour verified against Grimoire **v1.5.4** — the release the live instance
and `docker/docker-compose.yml` both run — by reading `temp/grimoire/` at that
tag and by calling the API. Don't re-derive these, and don't trust the published
docs over them. Re-verify after a server upgrade.

`main` upstream carries unreleased work (bulk endpoints, guarded renames) that no
instance runs. Pinning the reference clone to the release tag is not optional;
reading `main` is how the first round of wrong conclusions happened.

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
  Tags live in shared tables rather than a column (upstream #235). v1.5.4 has no
  additive tag endpoint and **no bulk endpoints** — `POST /api/{books,systems}/bulk`
  and `/bulk/tags` are unreleased `main` work (#270).
- **Renaming a system is unguarded and desyncs the slug.** `name` and `slug` are
  both `unique=True` (`backend/models/library.py:24-25`). The handler has no
  conflict check, so a duplicate name fails at commit as an opaque 500 rather than
  a 409. It writes only `name`; `slug` keeps its old folder-derived value, so the
  two diverge permanently and renaming the folder on disk creates a *second*
  system row instead of updating the first. The rename does survive a rescan — the
  scanner matches on `slug` and never writes `name` (`backend/indexer/scan.py`).
  (`_apply_rename` and its 409 are unreleased `main`, #261/#262.)
- **`GET /api/systems` filters are case-insensitive exact matches**, not
  substrings (`_has_value` in `backend/routers/systems/core.py`): `edition=5`
  matches `5` but never `5e`, and `genre=Cyber` never matches `Cyberpunk`. A list
  field matches if any element equals the value; an empty or null field never
  matches, so a freshly scanned system is excluded from every metadata filter.
  `genre=` tests the `genres` list, not the legacy `genre` string.
- **`category` on `GET /api/systems/{id}` is case-SENSITIVE**, unlike every other
  filter. `core.py:154` compares with `==` (`b.category == category`) while
  `genre` goes through `_has_value`, which lowercases both sides. So
  `category=Core` returns no books and `category=core` returns them. Verified
  against a running instance.

## Content and rescan

- **No upload API.** The library is mounted `:ro`; content arrives on the
  filesystem, then `POST /api/rescan`. A write channel is an open design question,
  tracked outside this repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a
  `scope` (e.g. `books/<system>/supplements`). `missing` reapplies OPF sidecars
  while treating any populated field as user-protected. Poll `GET /api/scan-status`.
- **Editions and language are metadata, not folders.** A new folder under `books/`
  creates a system row with only `name` set; `parent_system` / `edition` /
  `system_family` stay empty until a `PATCH /api/systems/{id}`.

## Scanner behaviour

Needed for the unwritten `docker/seed.sh`.

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
  (`if not system_dir.is_dir(): continue`, `backend/indexer/scan.py:178`), so a
  stray `foo.pdf` next to the system folders produces no system and no book.
  `_count_eligible_files` doesn't apply the same skip, so it still counts the
  file toward `total_books` — any wait loop polling
  `scanned_books >= total_books` hangs forever with a loose file present.
- **`one-page-rpgs/` (also `single-page-rpgs/`, `one-shot-rpgs/`)
  is one system, not one per file.** It gets `is_one_page: true`, and its
  immediate subfolder names become category labels — the same rule as the
  system-agnostic collection, not a per-file system split.
- **Category values are not a closed set.** `CATEGORY_MAP`
  (`backend/indexer/constants.py`) normalises known folder-name aliases
  (`supplements/`, `sourcebook/`, `guide/`, `companion/`, …) onto the canonical
  `core`, `supplement`, `adventure`, `character-sheet`, `map`, `handout`,
  `homebrew`, `starter-set`. A top-level folder that matches none of them
  becomes its own category: `guess_category` (`categories.py:153`) falls back
  to the slugified folder name (`Extras/` → `extras`). Special-collection roots
  (`one-page-rpgs/`, system-agnostic folders) go through `agnostic_category`
  (`categories.py:175`) instead, which slugs the immediate subfolder and yields
  `uncategorized` for books with no subfolder at all — confirmed live: this
  branch's `one-page-rpgs` fixture returns `category: "uncategorized"`.
- **`(nsfw)` in a system folder name sets `is_explicit`** and is stripped from the
  stored name, so `Vampire The Masquerade 5 EN (nsfw)/` becomes a system named
  `Vampire The Masquerade 5 EN` with `is_explicit: true`.
- **Leading `!`, `$`, `%` are stripped from system folder names**
  (`strip_sort_prefix`), so `!!Dungeons & Dragons/` is stored as
  `Dungeons & Dragons`. Only the contiguous leading run is removed.

## Systems have no language field

`GameSystemUpdate` has 17 fields and `serialize_system_summary` returns 24; neither
includes `language`. It exists only on books (`BookUpdate.language`), and
`GET /api/systems` has no `language` query parameter. A system's language can be
expressed only through its name (the `Shadowrun 6 DE` convention), a tag, or
per-book metadata.

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
