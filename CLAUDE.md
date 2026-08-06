# CLAUDE.md

## Main rule
be brief

## Status

Early. The environment is set up; the CLI is not designed yet. What exists in
`src/` is scaffolding to prove the toolchain works (config, auth plumbing,
`systems list`, `self-test`) — it is a starting point, not an approved design.
Design the command surface before extending it.

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

- **Thin pass-through.** Each command maps to a single Grimoire API endpoint. No smart defaults that pre-fetch extra data, no reading the response to emit derived warnings, no client-side mirroring of server policy. Workflows spanning multiple endpoints are the caller's job to compose. Higher-level orchestration belongs in the `grimoire-management` skills repo, not here.
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
- **No upload API.** The library is mounted `:ro`; content arrives on the filesystem, then `POST /api/rescan`. A write channel is an open design question — see the `Grimoire-deployment` repo.
- **`POST /api/rescan`** takes `metadata_mode: new | missing | replace` and a `scope` (e.g. `books/Shadowrun 6 DE/supplements`). `missing` reapplies OPF sidecars while treating any populated field as user-protected.
- **Editions and language are metadata, not folders.** A new folder under `books/` creates a system row with only `name` set; `parent_system` / `edition` / `system_family` stay empty until a `PATCH /api/systems/{id}`.

## Reference material (`temp/`, gitignored)

`temp/` holds everything used to ground API decisions. `.devcontainer/post-create.sh`
repopulates what it can on container create.

- `temp/grimoire/` — the upstream source, the authoritative reference for behaviour and response shapes:
  ```bash
  git clone --depth 1 https://github.com/hunter-read/grimoire.git temp/grimoire
  ```
- `temp/grimoire-openapi.json` — spec snapshot from a running instance (v1.5.4: 130 paths, 66 schemas):
  ```bash
  curl -sf "$GRIMOIRE_SERVER/api/openapi.json" -o temp/grimoire-openapi.json
  ```
- `temp/deployment-docs/` — copies of the `Grimoire-deployment` repo's design records: the ABS-vs-Grimoire decision record, the zimaboard deployment plan (including the live library structure), the ingest sidecar design, and the upstream bug reports.

## Live instance

`https://grimoire.example.invalid` — one system, `Shadowrun 6 DE`, 227 books. Its
`parent_system` / `edition` / `system_family` are deliberately left empty as a
test fixture for the first real metadata command. OIDC is enabled via Pocket ID;
`grimoire-cli login` uses the local password path, not OIDC.
