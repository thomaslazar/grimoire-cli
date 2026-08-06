# Handover — start here

Written 2026-08-06, from the Mac, before any development inside the container.
The environment is ready; the CLI is not designed. This file is the starting
point for the first session inside the devcontainer.

## Where things stand

- `dotnet build GrimoireCli.sln` succeeds, `self-test` passes, `dotnet format --verify-no-changes` is clean (verified on the host with .NET 10.0.300).
- Git repo initialised on `main`. **Not published. Do not create the GitHub repo yet.**
- It will be **public** when it is published, so before then: scrub the internal hostname from committed files (see "Before publishing" below).

## What is in `src/` — scaffolding, not a design

Written to prove the toolchain end to end, mirroring `abs-cli`'s structure:

| area | files |
|---|---|
| plumbing | `Api/GrimoireApiClient`, `ApiEndpoints`, `TokenHelper`, `DebugHttpHandler` |
| commands | `login`, `config get\|set`, `systems list\|get`, `self-test` |
| support | `Configuration/`, `Models/JsonContext`, `Output/`, `Commands/HelpExtensions`, `CommandHelper` |

No PATCH, no books, no rescan, no services layer. **Design the command surface
before extending it** — the first real job is metadata on systems and books.

## Next steps, in order

1. **Design the command surface.** Brainstorm first. The target job: fix and update metadata on existing entries — `PATCH /api/systems/{id}` (17 fields) and `PATCH /api/books/{id}` (18 fields), plus `POST /api/rescan`.
2. **Seeded local test stack.** `docker/docker-compose.yml` brings up an empty Grimoire. It still needs a `docker/seed.sh`, the counterpart to `abs-cli`'s — see below.
3. **Smoke test** against the AOT binary once there are commands worth exercising, then wire it into `.github/workflows/build.yml` (the job was deliberately left out).
4. **`grimoire-management`** — the separate skills/rules repo, counterpart to `abs-management`. Not started.

## Seeding a local instance — what was learned

Read from `temp/grimoire/` (upstream source), not from the docs.

- **First-run admin:** `POST /api/auth/setup` with `{"username","password"}` creates the initial admin and returns a JWT. It **fails once any user exists**, so it is once per fresh volume. `GET /api/auth/status` returns `{"initialized": bool}` — poll that to decide.
- **Both `/api/auth/setup` and `/api/auth/login` return `{"token": ..., "user": {...}}`** — the key is `token`, not `access_token`. `GrimoireApiClient.ExtractToken` already accepts either, but the spec types both responses as `{}`, so this is source-derived, not spec-derived.
- **Auth endpoints are rate-limited** (`AUTH_RATE_LIMIT`, `backend/security.py`). `abs-cli`'s smoke test logs in at every section and had to disable ABS's limiter; check whether Grimoire's limit is configurable before copying that pattern, or log in once and reuse the token.
- **Fixture content:** the scanner keys off extensions in `backend/indexer/constants.py` — `.pdf/.epub/.djvu` for books, `.png/.jpg/.jpeg/.gif/.webp/.bmp/.tiff/.svg` for images, plus archive and audio sets. A handful of tiny generated PDFs and PNGs under `docker/library/books/{system}/{category}/` is enough; no real books needed.
- **Categories** come from the folder name under the system (`backend/indexer/categories.py`), and a folder named `Maps` **directly under a system** becomes a map category — at subfolder depth the name is inert.
- **After seeding files, `POST /api/rescan`**, then poll `GET /api/scan-status`.
- Two scanner behaviours worth fixtures: a loose `foo.pdf` directly under `books/` becomes its own single-book system, and `one-page-rpgs/` (also `micro-rpgs/`, `single-page-rpgs/`, `one-shot-rpgs/`) makes each file its own system.

## Live instance

Real data lives at the URL in `temp/deployment-docs/` — one system, `Shadowrun 6 DE`,
227 books, whose `parent_system` / `edition` / `system_family` are deliberately
empty as a test fixture. Use it for read-side exploration and for discovering
response shapes; use the local stack for anything that writes.

Point the CLI at either with `GRIMOIRE_SERVER`, or `grimoire-cli config set server <url>`.

## Grimoire facts that shape the design

Also in `CLAUDE.md`. Verified against the live instance and the source.

- Auth is `HTTPBearer`; the JWT lasts 30 days and **there is no refresh endpoint** — expiry means logging in again. This is the main divergence from `abs-cli`, which refreshes.
- **Nearly every response is untyped in the OpenAPI spec** (FastAPI without `response_model`). Request bodies *are* typed. So response shapes come from calling the API or reading `temp/grimoire/`, and the CLI passes raw JSON through rather than modelling 66 schemas.
- **`GET /api/openapi.json` 500s when `OPDS_ENABLED=true`** — upstream `hunter-read/grimoire#276`. Both the live instance and `docker/docker-compose.yml` run with OPDS off.
- **No upload API**; the library is mounted `:ro`. Content lands on disk, then `POST /api/rescan`. The write channel is an open question in the `Grimoire-deployment` repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a `scope`. `missing` reapplies OPF sidecars while treating populated fields as user-protected.
- **Editions and language are metadata, not folders** — a new folder creates a system row with only `name` set.

## Reference material in `temp/` (gitignored)

- `temp/grimoire/` — upstream source, shallow clone. The authoritative reference.
- `temp/grimoire-openapi.json` — live spec snapshot, v1.5.4: 130 paths, 66 schemas.
- `temp/deployment-docs/` — the `Grimoire-deployment` design records and compose.

All three are populated by hand. `temp/` is in the bind-mounted workspace and
survives rebuilds, so `post-create.sh` no longer fetches any of it — a
fetch-on-create is a no-op after the first run and silently chose `main` as the
reference ref, which is how the unreleased-API confusion started.

## Before publishing

The repo is public-bound, so first:

- Remove the internal hostname from `CLAUDE.md` and any other committed file; keep it in `temp/` only, and refer to `$GRIMOIRE_SERVER` in committed docs.
- `.devcontainer/post-create.sh` hardcodes the host session path `-path-to-grimoire-cli`; make it a glob or drop it.
- Decide whether `docker/library/` fixtures are generated by `seed.sh` (preferred) or committed.
