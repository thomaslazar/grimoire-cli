# Grimoire API notes

Behaviour verified against Grimoire **v1.5.5** — the release the live instance
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
  system.name != name`, `backend/indexer/scan.py:358`), so an unrenamed
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

## Content and rescan

- **No upload API.** The library is mounted `:ro`; content arrives on the
  filesystem, then `POST /api/rescan`. A write channel is an open design question,
  tracked outside this repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a
  `scope` (e.g. `books/<system>/supplements`). `missing` reapplies OPF sidecars
  while treating any populated field as user-protected. Poll `GET /api/scan-status`.
- **Editions and language are metadata, not folders.** A new *flat* (non-container)
  folder under `books/` creates a system row with only `name` set; `parent_system`
  / `edition` / `system_family` stay empty until a `PATCH /api/systems/{id}`.
  This no longer covers the whole story in v1.5.5: a folder scanned as a
  *container child* gets `parent_system` and `edition` auto-populated at
  creation instead — see "System containers" below. `system_family` still has
  no folder route either way.

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

Verified against v1.5.5. Every citation is a file in `temp/grimoire`.

- A folder becomes a container via a `.parent-system-container` /
  `.one-page-container` marker file, a `(parent-system)` / `(one-page)` name
  suffix, or a reserved one-page slug (`indexer/categories.py::detect_container_kind`).
- A child's display name is `"<container> <folder>"` (`indexer/scan.py:443`), so
  `Shadowrun` + `6 DE` is `Shadowrun 6 DE`.
- `edition` is the child folder name **verbatim** (`indexer/scan.py:490`): a
  folder called `6 DE` yields edition `6 DE`, not `6`.
- `parent_system` is still a free-text column, auto-set to the container's name
  on child creation (`indexer/scan.py:332`). Both it and the real `parent_id`
  foreign key are returned.
- Sort prefixes are stripped before the container name is used
  (`indexer/scan.py:188`), so `!!Dungeons & Dragons` yields `Dungeons & Dragons`.
- `system_depth` is the constant 3 (`indexer/scan.py:504`), so a category folder
  must sit exactly one level below the edition folder. Nesting containers is
  unavailable until `.system-family-container` reaches a tagged release.
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
