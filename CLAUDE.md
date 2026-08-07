# CLAUDE.md

## Main rule
be brief

## Git Conventions

- **Ask before committing** after ad-hoc or exploratory changes — report what changed, then ask. Exception: when executing a pre-approved implementation plan whose tasks specify commit messages, commit per the plan without pausing each task (the plan is the approval). Never autonomous for amends, force pushes, or commits to `main`.
- **Conventional Commits** format required: `type: subject`
- Types: `feat`, `fix`, `docs`, `test`, `ci`, `refactor`, `chore`
- Subject line: imperative mood, lowercase, no period, max ~72 chars
- Body (optional): explain *why*, not *what*. Wrap at 72 chars.
- Do NOT include `Co-Authored-By:` lines in commit messages.
- Do NOT add "Generated with Claude Code" or similar attribution lines to PRs, commits, or any auto-generated content.
- After creating a pull request, always present the PR URL as a clickable link.

Examples:
```
feat: add systems update command
fix: send empty string rather than null to clear a field
docs: record patch semantics verified against v1.5.4
test: cover token extraction and config resolution
```

## Pre-PR verification

Run all four before opening a PR — unit tests and `self-test` alone miss anything in the live HTTP path:

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

The smoke test expects a running stack and does not seed one. Bring it up first:

```bash
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
```

- Copying the fixture before the first boot is required; skip it and the stack comes up with no users, whose only symptom is a 401. Logins are `admin/admin`, `gm/gm`, `player/player`.
- Reset with `docker compose -f docker/docker-compose.yml down && rm -rf docker/data`, then recreate as above. Unlike `abs-cli`'s, this smoke test is idempotent — it only reads and logs in.
- Under docker-outside-of-docker the daemon runs on the host: set `GRIMOIRE_LIBRARY` and `GRIMOIRE_DATA` to host paths (see `docker/.env.example`) and reach the stack at `http://host.docker.internal:9481`, not `localhost`.
- **Anything that writes goes to the local stack, never the live instance.** Its one system has `parent_system` / `edition` / `system_family` deliberately empty as a fixture for the first metadata command — don't spend it casually.

## Post-PR verification

- After `gh pr create`, watch CI until every check reaches a terminal state and report the result without being asked. A PR is done at "all checks green", not at "PR open".
- `gh pr checks <num>` for one-shot status; `gh run watch <run-id>` for long jobs.

## Docs, specs & roadmap

- **Specs** go in `docs/specs/YYYY-MM-DD-<topic>-design.md`, **plans** in `docs/plans/YYYY-MM-DD-<topic>.md` — never `docs/superpowers/…`, whatever a skill defaults to.
- **Hold spec/plan commits until the implementation branch exists**, then commit spec + plan + code together on that branch so design and delivery are reviewed as one unit.
- **Once a feature branch exists, keep its docs edits on that branch** — they reach `main` via the PR.
- **`CHANGELOG.md` is owned by the release process** (`release/v{version}` branches only). Never edit it from a feature branch.
- Current state and open work live in [docs/roadmap.md](docs/roadmap.md).

## Code Formatting

- `.editorconfig` (from dotnet/runtime) enforces style. CI checks with `dotnet format --verify-no-changes`.
- Run `dotnet format GrimoireCli.sln` after writing or modifying C# files.
- **No unnecessary blank lines** inside method bodies: no blanks between consecutive `AddCommand`/`AddOption` calls, no blank before `return` after setup calls, no blanks between consecutive variable declarations of the same kind.

## Comments

- Comment what the code does or why it must be this way — never what was deliberately left out. If something isn't done, its absence needs no defence.
- Prefer stating a requirement positively ("the server must come from the saved config") over narrating a rejected alternative.

## CLI design principles

- **Thin pass-through.** Each command maps to a single Grimoire API endpoint. No smart defaults that pre-fetch extra data, no reading the response to emit derived warnings, no client-side mirroring of server policy. Workflows spanning multiple endpoints are the caller's job to compose. Higher-level orchestration belongs in the `management-repo` skills repo, not here.
- **JSON in, JSON out.** stdout is valid JSON from the API; logs and human-facing lines go to stderr.

## Help text

`--help` is the primary interface for the AI agents that consume this CLI, and every word costs tokens. Keep it terse and self-contained.

- **Terse.** One-liners over prose, bullets over paragraphs.
- **Document every non-obvious caveat** at the call site — destructive side effects, hidden API behaviours, outcome-affecting defaults. The CLI is thin, so API quirks leak through; help text is where they must surface.
- **Don't state what's already visible** from the flags or subcommand list.

## Grimoire Source Reference

The upstream source is the authoritative reference for behaviour and response shapes. The OpenAPI spec types nearly every response as `{}` (FastAPI without `response_model`), and the published docs have been wrong before.

- Expected location: `temp/grimoire/` (gitignored). **Pin it to the deployed release, never `main`** — `main` carries unreleased work that no instance runs:
  ```bash
  # Match MinSupportedVersion / MaxTestedVersion in src/GrimoireCli/Api/GrimoireApiClient.cs
  git clone --depth 1 --branch v1.5.4 https://github.com/hunter-read/grimoire.git temp/grimoire
  ```
- `temp/grimoire-openapi.json` — spec snapshot pulled from a running instance; refresh after a server upgrade:
  ```bash
  curl -sf "$GRIMOIRE_SERVER/api/openapi.json" -o temp/grimoire-openapi.json
  ```
- `temp/deployment-docs/` — design records copied from the `deployment-repo` repo, including the live instance's URL and library structure.
- `temp/` sits in the bind-mounted workspace and survives container rebuilds; populate it by hand.

Verified API behaviour that the source alone is slow to reveal — read this before designing a command: [docs/grimoire-api-notes.md](docs/grimoire-api-notes.md).
