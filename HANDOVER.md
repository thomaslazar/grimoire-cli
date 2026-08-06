# Handover — start here

Last updated 2026-08-06, from inside the devcontainer. The environment is
ready and the login + smoke-test path is built and passing; the metadata
command surface is still not designed. This file is the starting point for
the next session.

## Where things stand

- `dotnet build GrimoireCli.sln` succeeds, `self-test` passes, `dotnet format --verify-no-changes` is clean (verified on the host with .NET 10.0.300).
- `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj` passes (24 tests), and `bash docker/smoke-test.sh` passes against the local stack — see "Next steps" below.
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

1. **Seeded local test stack — done.** `docker/docker-compose.yml` binds `${GRIMOIRE_DATA:-./data}` (no named volume), runs with `RATE_LIMIT_ENABLED=false`, and overrides the image healthcheck for fast polling so `up -d --wait` returns quickly. `docker/users.json.example` seeds `admin/admin`, `gm/gm`, `player/player` via Grimoire's own `/data/users.json` startup seeding — no `/api/auth/setup` call. Reset is `docker compose down && rm -rf docker/data && mkdir -p docker/data && cp docker/users.json.example docker/data/users.json`; that flow was re-run end to end in this session and reseeds cleanly. (Skipping the `mkdir`/`cp` leaves an unseeded stack whose only symptom is a 401 after 30 retries.) `docker/seed.sh` and library fixtures are still unwritten — login needs no content, so that is the next increment, not this one.
2. **Smoke test — done.** `docker/smoke-test.sh` asserts health, `login` exits 0, config persists server+token, `systems list` emits valid JSON on stdout, a bad password exits 2 (`Program.cs`'s generic failure code — the test also greps stderr for "login failed" and "401" to stay specific) leaving the config untouched, and `self-test` exits 0. It does not start or seed the stack. Wired into `.github/workflows/build.yml` as a `smoke-test` job between `unit-test` and `build`, using `docker compose` directly rather than a `services:` container (Actions starts service containers before `actions/checkout`, which would never see the fixture). CI pulls the Grimoire image unauthenticated, so a Docker Hub rate limit would show up as a red `smoke-test` job on an unrelated PR; fixing that needs a repository secret (authenticated pull), so it can't be done before the repo is published. Known wrinkle, pre-existing and out of scope for this branch: `LoginCommand.cs:101-111` wraps the post-save `/api/about` version check in the same `try` as the login call, so a transient `/api/about` failure reports `Login failed:` and exits 2 *after* the token was already written to config — worth fixing when the login path is next touched.
3. **Design the command surface.** Still the main open work. The target job: fix and update metadata on existing entries — `PATCH /api/systems/{id}` (17 fields) and `PATCH /api/books/{id}` (18 fields), plus `POST /api/rescan`. One question was raised and deliberately parked, not decided: typed flags for the flat fields plus a `--json` escape hatch for the three nested array-of-object fields, versus a raw-JSON-body-only interface. Verified fact bearing on that choice: PATCH does `model_dump(exclude_none=True)`, so a JSON `null` is silently dropped — a field can never be cleared to null, and the integer fields (`year`, `month`, `day`) cannot be cleared at all, since `""` fails validation.
4. **`management-repo`** — the separate skills/rules repo, counterpart to `management-repo`. Not started.

## Seeding a local instance — what was learned

Read from `temp/grimoire/` (upstream source), not from the docs.

- **Mechanism in use: `/data/users.json` startup seeding**, not an API call. Grimoire reads that file at boot (`backend/seed_users.py`), creates the listed users, then renames the file to `users.json.imported`. That rename is unguarded and startup has no `except` around it, so `users.json` must sit inside a mounted *directory* — bind-mounting it as a single file makes the rename fail and the container won't start. See `docker/users.json.example`; the compose file copies it into `docker/data/` before first boot.
- **`POST /api/auth/setup` is real, and was considered and rejected.** It takes `{"username","password"}`, creates the initial admin, and returns a JWT — but it fails once any user exists, so it only ever covers one admin, not the `admin/gm/player` fixture set this branch needed, and `GET /api/auth/status` would have to be polled to know when it's safe to call. `users.json` seeding covers all three roles in one step with no polling. Don't re-litigate this.
- **Both `/api/auth/setup` and `/api/auth/login` return `{"token": ..., "user": {...}}`** — the key is `token`, not `access_token`. `GrimoireApiClient.ExtractToken` already accepts either, but the spec types both responses as `{}`, so this is source-derived, not spec-derived.
- **Auth rate limiting, answered:** `AUTH_RATE_LIMIT` (`backend/security.py:32`) defaults to `10/minute`; `RATE_LIMIT_ENABLED=false` (`backend/security.py:45`) disables it entirely. `docker/docker-compose.yml` now sets `RATE_LIMIT_ENABLED=false` — the smoke test logs in several times per run and would otherwise trip the default limit.
- **Fixture content:** the scanner keys off extensions in `backend/indexer/constants.py` — `.pdf/.epub/.djvu` for books, `.png/.jpg/.jpeg/.gif/.webp/.bmp/.tiff/.svg` for images, plus archive and audio sets. A handful of tiny generated PDFs and PNGs under `docker/library/books/{system}/{category}/` is enough; no real books needed.
- **Categories** come from the folder name under the system (`backend/indexer/categories.py`), and a folder named `Maps` **directly under a system** becomes a map category — at subfolder depth the name is inert.
- **After seeding files, `POST /api/rescan`**, then poll `GET /api/scan-status`.
- Two scanner behaviours worth fixtures: a loose `foo.pdf` directly under `books/` becomes its own single-book system, and `one-page-rpgs/` (also `micro-rpgs/`, `single-page-rpgs/`, `one-shot-rpgs/`) makes each file its own system.
- Still needed: `docker/seed.sh` to generate/drop the fixtures above — not written yet.

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
- **No upload API**; the library is mounted `:ro`. Content lands on disk, then `POST /api/rescan`. The write channel is an open question in the `deployment-repo` repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a `scope`. `missing` reapplies OPF sidecars while treating populated fields as user-protected.
- **Editions and language are metadata, not folders** — a new folder creates a system row with only `name` set.

## Reference material in `temp/` (gitignored)

- `temp/grimoire/` — upstream source, shallow clone pinned to the deployed release (v1.5.4). The authoritative reference.
- `temp/grimoire-openapi.json` — live spec snapshot, v1.5.4: 130 paths, 66 schemas.
- `temp/deployment-docs/` — the `deployment-repo` design records and compose.

All three are populated by hand. `temp/` is in the bind-mounted workspace and
survives rebuilds, so `post-create.sh` no longer fetches any of it — a
fetch-on-create is a no-op after the first run and silently chose `main` as the
reference ref, which is how the unreleased-API confusion started.

## Before publishing

The repo is public-bound, so first:

- Remove the internal hostname from `CLAUDE.md` and any other committed file; keep it in `temp/` only, and refer to `$GRIMOIRE_SERVER` in committed docs.
- `.devcontainer/post-create.sh` hardcodes the host session path `-path-to-grimoire-cli`; make it a glob or drop it. `docs/plans/2026-08-06-login-and-smoke-test.md` now has the same path four times too (in verification commands) — do not rewrite the plan itself, but scrub it before publishing.
- Decide whether `docker/library/` fixtures are generated by `seed.sh` (preferred) or committed. `docker/library` is now a compose mount source in CI; dockerd creates a missing bind-source directory on its own, so dropping the `.gitkeep` later will not break the job.
