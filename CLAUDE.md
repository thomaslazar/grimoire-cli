# CLAUDE.md

## Main rule
be brief

## Status

Early. `login` works end to end against a disposable local stack and is covered
by `docker/smoke-test.sh` in CI. Everything else in `src/` is still scaffolding
to prove the toolchain works (config, auth plumbing, `systems list`,
`self-test`) — a starting point, not an approved design.

Open work, in order:

1. **The metadata command surface** — `PATCH /api/systems/{id}` (17 fields),
   `PATCH /api/books/{id}` (18 fields), `POST /api/rescan`. Design it before
   extending `src/`. One question was raised and deliberately parked, not
   decided: typed flags for the flat fields plus a `--json` escape hatch for the
   three nested array-of-object fields, versus a raw-JSON-body-only interface.
2. **Fixture generation** — `docker/seed.sh` and library content under
   `docker/library/`. Login needed no content, so this was deferred.
3. **`management-repo`** — the separate skills/rules repo, counterpart to
   `management-repo`. Not started.

## Local test stack

A disposable Grimoire for anything that writes. Never test writes against a real
instance.

```bash
mkdir -p docker/data
cp docker/users.json.example docker/data/users.json   # before the first boot
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/smoke-test.sh
```

- Users come from `/data/users.json`, which Grimoire seeds at startup and then
  renames to `users.json.imported` (`backend/seed_users.py`). The rename is
  unguarded and startup has no `except` around it, so the file must sit **inside**
  a mounted directory — bind-mounting it as a single file stops the container
  booting. Logins are `admin/admin`, `gm/gm`, `player/player`.
- Nothing copies the fixture for you. Skipping that step yields a stack with no
  users, whose only symptom is a 401.
- Reset: `docker compose -f docker/docker-compose.yml down && rm -rf docker/data`,
  then recreate and re-copy as above.
- Under docker-outside-of-docker the daemon runs on the host, so bind paths
  resolve against the **host** filesystem: set `GRIMOIRE_LIBRARY` and
  `GRIMOIRE_DATA` to host paths (see `docker/.env.example`), and reach the stack
  at `http://host.docker.internal:9481` rather than `localhost`.

## Scanner behaviour, for the unwritten `seed.sh`

Read from `temp/grimoire/`, not from the docs.

- **Fixture content:** the scanner keys off extensions in
  `backend/indexer/constants.py` — `.pdf/.epub/.djvu` for books,
  `.png/.jpg/.jpeg/.gif/.webp/.bmp/.tiff/.svg` for images, plus archive and audio
  sets. A handful of tiny generated PDFs and PNGs under
  `docker/library/books/{system}/{category}/` is enough; no real books needed.
- **Categories** come from the folder name under the system
  (`backend/indexer/categories.py`); a folder named `Maps` **directly** under a
  system becomes a map category, while at subfolder depth the name is inert.
- After dropping files in, `POST /api/rescan`, then poll `GET /api/scan-status`.
- Two behaviours worth fixtures: a loose `foo.pdf` directly under `books/`
  becomes its own single-book system, and `one-page-rpgs/` (also `micro-rpgs/`,
  `single-page-rpgs/`, `one-shot-rpgs/`) makes each file its own system.

## Git Conventions

- **Ask before committing** after ad-hoc or exploratory changes — report what changed, then ask. Exception: when executing a pre-approved implementation plan whose tasks specify commit messages, commit per the plan without pausing each task (the plan is the approval). Never autonomous for amends, force pushes, or commits to `main`.
- **Conventional Commits** format required: `type: subject`
- Types: `feat`, `fix`, `docs`, `test`, `ci`, `refactor`, `chore`
- Subject line: imperative mood, lowercase, no period, max ~72 chars
- Body (optional): explain *why*, not *what*. Wrap at 72 chars.
- Do NOT include `Co-Authored-By:` lines in commit messages.
- Do NOT add "Generated with Claude Code" or similar attribution lines to PRs, commits, or any auto-generated content.
- After creating a pull request, always present the PR URL as a clickable link.

## Docs, specs & roadmap

- **Specs** go in `docs/specs/YYYY-MM-DD-<topic>-design.md`, **plans** in `docs/plans/YYYY-MM-DD-<topic>.md` — never `docs/superpowers/…`, whatever a skill defaults to.
- **Hold spec/plan commits until the implementation branch exists**, then commit spec + plan + code together on that branch so design and delivery are reviewed as one unit.
- **`CHANGELOG.md` is owned by the release process** (`release/v{version}` branches only). Never edit it from a feature branch.

## Code Formatting

- `.editorconfig` (from dotnet/runtime) enforces style. CI checks with `dotnet format --verify-no-changes`.
- Run `dotnet format GrimoireCli.sln` after writing or modifying C# files.
- **No unnecessary blank lines** inside method bodies: no blanks between consecutive `AddCommand`/`AddOption` calls, no blank before `return` after setup calls, no blanks between consecutive variable declarations of the same kind.

## CLI design principles

- **Thin pass-through.** Each command maps to a single Grimoire API endpoint. No smart defaults that pre-fetch extra data, no reading the response to emit derived warnings, no client-side mirroring of server policy. Workflows spanning multiple endpoints are the caller's job to compose. Higher-level orchestration belongs in the `management-repo` skills repo, not here.
- **JSON in, JSON out.** stdout is valid JSON from the API; logs and human-facing lines go to stderr.

## Help text

`--help` is the primary interface for the AI agents that consume this CLI, and every word costs tokens. Keep it terse and self-contained.

- **Terse.** One-liners over prose, bullets over paragraphs.
- **Document every non-obvious caveat** at the call site — destructive side effects, hidden API behaviours, outcome-affecting defaults. The CLI is thin, so API quirks leak through; help text is where they must surface.
- **Don't state what's already visible** from the flags or subcommand list.

## Grimoire specifics

These are established facts, verified against the live instance. Don't re-derive
them, and don't trust the published docs over them.

- **Auth is `HTTPBearer`.** `POST /api/auth/login` returns a JWT valid 30 days. **There is no refresh endpoint** — an expired token means logging in again. (Unlike ABS, which has `auth/refresh` and a refresh token.)
- **The login response body is untyped in the spec**, as is nearly every response — FastAPI without `response_model`. Response shapes must be discovered by calling the API, not read off the spec. Request bodies *are* typed (`GameSystemUpdate`, `BookUpdate`, `RescanRequest`, …).
- **`GET /api/openapi.json` returns 500 when `OPDS_ENABLED=true`** — upstream `hunter-read/grimoire#276`. Both our live instance and `docker/docker-compose.yml` run with OPDS off so the spec serves.
- **No upload API.** The library is mounted `:ro`; content arrives on the filesystem, then `POST /api/rescan`. A write channel is an open design question — see the `deployment-repo` repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a `scope` (e.g. `books/Shadowrun 6 DE/supplements`). `missing` reapplies OPF sidecars while treating any populated field as user-protected.
- **PATCH drops nulls.** Both `PATCH /api/systems/{id}` and `PATCH /api/books/{id}` do `model_dump(exclude_none=True)` then `setattr` the rest, so a JSON `null` is *silently ignored* — a field cannot be cleared to null. Clearing means `""` / `[]` for string and list fields. The integer fields (`year` on systems; `year`/`month`/`day` on books) cannot be cleared at all — `null` is dropped and `""` fails Pydantic coercion with a 422. Verified in v1.5.4 (`backend/routers/{systems,books}/core.py`).
- **PATCH returns `{"status":"ok"}`**, not the updated row. Seeing the result takes a follow-up `GET`.
- **`tags` on PATCH replace, they don't merge** (`tag_service.sync_tags_from_payload`); tags live in shared tables, not a column (upstream #235). v1.5.4 has no additive tag endpoint and **no bulk endpoints** — `POST /api/{books,systems}/bulk` and `/bulk/tags` are unreleased `main` work (#270).
- **Renaming a system via PATCH is unguarded, and desyncs the slug.** `name` and `slug` are both `unique=True` (`backend/models/library.py:24-25`). v1.5.4's handler has no conflict check, so a duplicate name fails at commit as an opaque 500 rather than a 409. It also writes only `name` — `slug` keeps its old folder-derived value, so the two diverge permanently, and renaming the folder on disk then creates a *second* system row instead of updating the first. A rename does survive a rescan: the scanner matches on `slug` and never writes `name` (`backend/indexer/scan.py:188-195,230-235`). (`_apply_rename` and its 409 are unreleased `main`, #261/#262.)
- **Editions and language are metadata, not folders.** A new folder under `books/` creates a system row with only `name` set; `parent_system` / `edition` / `system_family` stay empty until a `PATCH /api/systems/{id}`.

## Reference material (`temp/`, gitignored)

`temp/` holds everything used to ground API decisions. It lives in the bind-mounted
workspace, so it survives container rebuilds — populate it by hand with the commands
below, and refresh both after a server upgrade.

- `temp/grimoire/` — the upstream source, the authoritative reference for behaviour and response shapes. **Pin it to the deployed release, never `main`** — `main` carries unreleased work (bulk endpoints, sticky renames) that no instance runs:
  ```bash
  git clone --depth 1 --branch v1.5.4 https://github.com/hunter-read/grimoire.git temp/grimoire
  ```
- `temp/grimoire-openapi.json` — spec snapshot from a running instance (v1.5.4: 130 paths, 66 schemas):
  ```bash
  curl -sf "$GRIMOIRE_SERVER/api/openapi.json" -o temp/grimoire-openapi.json
  ```
- `temp/deployment-docs/` — copies of the `deployment-repo` repo's design records: the ABS-vs-Grimoire decision record, the zimaboard deployment plan (including the live library structure), the ingest sidecar design, and the upstream bug reports.

## Live instance

Set `GRIMOIRE_SERVER` to it, or `grimoire-cli config set server <url>` — the URL
stays out of committed files; it is recorded in `temp/deployment-docs/`.

One system, `Shadowrun 6 DE`, 227 books. Its `parent_system` / `edition` /
`system_family` are deliberately left empty as a fixture for the first real
metadata command — **don't spend it casually**. OIDC is enabled via Pocket ID;
`grimoire-cli login` uses the local password path, not OIDC. Use it for read-side
exploration and for discovering response shapes; use the local stack for anything
that writes.
