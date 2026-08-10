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

The smoke test expects a running, seeded stack; it neither starts nor seeds one. Bring it up and seed it first:

```bash
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- Copying the fixture before the first boot is required; skip it and the stack comes up with no users, whose only symptom is a 401. Logins are `admin/admin`, `gm/gm`, `player/player`.
- Reset with `docker compose -f docker/docker-compose.yml down`, then `rm -rf docker/data docker/library/books`, then recreate as above — the boot scan indexes whatever library tree is on disk, so a database-only reset leaves stale rows that survive as `is_missing` and still count toward `book_count`. Unlike `abs-cli`'s, this smoke test is idempotent — it only reads and logs in.
- Under docker-outside-of-docker the daemon runs on the host: set `GRIMOIRE_LIBRARY` and `GRIMOIRE_DATA` to host paths (see `docker/.env.example`) and reach the stack at `http://host.docker.internal:9481`, not `localhost`. `docker/seed.sh` writes fixtures itself rather than through the daemon, so it reads the container-side path from a separate var, `GRIMOIRE_LIBRARY_LOCAL` (defaults to `docker/library`) — pointing `GRIMOIRE_LIBRARY` at a library outside the repo without also setting this writes fixtures into `docker/library` while the server scans an empty tree.
- **Anything that writes goes to the local stack, never the live instance.** `Shadowrun 4 DE` is left unpatched by `seed.sh`, so `system_family` is deliberately empty as a fixture for the first metadata command — `parent_system` and `edition` are already populated, folder-derived from being a container child. Don't spend it casually.

## Post-PR verification

- After `gh pr create`, watch CI until every check reaches a terminal state and report the result without being asked. A PR is done at "all checks green", not at "PR open".
- `gh pr checks <num>` for one-shot status; `gh run watch <run-id>` for long jobs.

## Docs, specs & roadmap

- **Specs** go in `docs/specs/YYYY-MM-DD-<topic>-design.md`, **plans** in `docs/plans/YYYY-MM-DD-<topic>.md` — never `docs/superpowers/…`, whatever a skill defaults to.
- **Hold spec/plan commits until the implementation branch exists**, then commit spec + plan + code together on that branch so design and delivery are reviewed as one unit.
- **Once a feature branch exists, keep its docs edits on that branch** — they reach `main` via the PR.
- **`CHANGELOG.md` is owned by the release process** (`release/v{version}` branches only). Never edit it from a feature branch.
- Current state and open work live in [docs/roadmap.md](docs/roadmap.md).
- **Any PR that adds, renames or removes a command, or changes a user-visible flag, updates the README Commands table in the same change.**
- **Any PR that touches which endpoints are called updates [docs/grimoire-api-coverage.md](docs/grimoire-api-coverage.md) in the same change.**

## Code Formatting

- `.editorconfig` (from dotnet/runtime) enforces style. CI checks with `dotnet format --verify-no-changes`.
- Run `dotnet format GrimoireCli.sln` after writing or modifying C# files.
- **No unnecessary blank lines** inside method bodies: no blanks between consecutive `AddCommand`/`AddOption` calls, no blank before `return` after setup calls, no blanks between consecutive variable declarations of the same kind.

## Comments

- Comment what the code does or why it must be this way — never what was deliberately left out. If something isn't done, its absence needs no defence.
- Prefer stating a requirement positively ("the server must come from the saved config") over narrating a rejected alternative.

## Relationship to abs-cli

`abs-cli` is the mature reference for this pair of tools. **Match its structure and conventions unless there is a reason, and record the reason here** — drift between the two costs more than the occasional awkward fit.

Deliberate deviations today:

- **`docs/grimoire-api-coverage.md` is generated, not hand-maintained.** `tools/generate-api-coverage.py` builds it from the spec plus the role dependency on each route in `temp/grimoire`. Grimoire publishes an OpenAPI spec and ABS does not, so abs-cli has to maintain its table by hand. Update `IMPLEMENTED` in that script, not the markdown.
- **Grouped by the spec's own OpenAPI tags** rather than hand-picked resource headings, for the same reason: the grouping is machine-derived and cannot drift from the API.
- **`docs/grimoire-api-notes.md` has no abs-cli counterpart.** Grimoire types nearly every response as `{}`, so verified behaviour needs somewhere to live; ABS's behaviour is read from its server source on demand.
- **Tests add a `Models/` area** alongside abs-cli's `Api` / `Commands` / `Configuration` / `Output` / `Services`, because the response DTOs are a distinct surface here.
- **The `release` skill carries an extra step reconciling the supported server
  range.** `MinSupportedVersion` / `MaxTestedVersion`, the compatibility matrix
  and the README line must agree before a tag is cut. abs-cli has no counterpart
  because it has no login-time version gate. Its preflight also differs: the
  `docker/users.json.example` fixture must be copied before first boot, and the
  `--version` check asserts bare output because PR builds carry a
  `+pr-<n>.<sha7>` suffix.

The docs set and the release plumbing (`install.sh`, `install.ps1`, deb packaging, Homebrew tap job) are in place, and the `thomaslazar/homebrew-grimoire-cli` tap repo exists. What remains before a first release is narrower — see [docs/releasing.md](docs/releasing.md).

## CLI design principles

- **Thin pass-through.** Each command maps to a single Grimoire API endpoint. No smart defaults that pre-fetch extra data, no reading the response to emit derived warnings, no client-side mirroring of server policy. Workflows spanning multiple endpoints are the caller's job to compose. Higher-level orchestration belongs in the calling layer, not here.
- **JSON in, JSON out.** stdout is valid JSON from the API; logs and human-facing lines go to stderr.
- **Commands whose endpoint needs a non-default role call `command.AddRoleRequired("<role>")`**, and the string matches the `permissionHint` passed to the service call. `systems list` / `systems get` need no tag: any authenticated non-guest can read them, so the mechanism is currently exercised only by `RoleSectionTests` — the first write command is what will use it for real.
- **`--server` and `--token` are declared per-subcommand on commands that call the API**, matching abs-cli, and threaded into `CommandHelper.BuildClient` so the flag tier of `flags > env > file` is actually reachable. They are not on `config` (no API call) or `self-test` (offline). Write commands have not opted in yet — a deliberate call for whoever designs the first one.

## Help text

`--help` is the primary interface for the AI agents that consume this CLI, and every word costs tokens. Keep it terse and self-contained.

- **Terse.** One-liners over prose, bullets over paragraphs, no "useful when…" framing. Calibrate against `SystemsCommand.cs`, whose Notes blocks are sized to what abs-cli allows.
- **Document every non-obvious caveat** at the call site — destructive side effects, hidden API behaviours (children hidden before filters apply, folder-derived fields that ignore a PATCH), outcome-affecting defaults. The CLI is thin, so API quirks leak through; help text is where they must surface, not spec docs.
- **Don't state what's already visible.** Skip anything apparent from the flags, subcommand list, or response-shape sample: no verb-by-verb group narration, no "X cannot change" when there's no such flag, no restating a flag's own description or a response field. A `ChoiceOption` renders its own value set, so the description must not repeat it.
- **Cross-references are one-way** (consumer → producer) and allowed only when required to use *this* command: where a required input comes from, a behaviour warning, a piping pitfall, a shared external dependency. Never sell another command's use case.

## Grimoire Source Reference

The upstream source is the authoritative reference for behaviour and response shapes. The OpenAPI spec types nearly every response as `{}` (FastAPI without `response_model`), and the published docs have been wrong before.

- Expected location: `temp/grimoire/` (gitignored). **Pin it to the deployed release, never `main`** — `main` carries unreleased work that no instance runs:
  ```bash
  # Match MinSupportedVersion / MaxTestedVersion in src/GrimoireCli/Api/GrimoireApiClient.cs
  git clone --depth 1 --branch v1.5.5 https://github.com/hunter-read/grimoire.git temp/grimoire
  ```
- `temp/grimoire-openapi.json` — spec snapshot pulled from a running instance; refresh after a server upgrade:
  ```bash
  curl -sf "$GRIMOIRE_SERVER/api/openapi.json" -o temp/grimoire-openapi.json
  ```
- `temp/deployment-docs/` — deployment design records copied in by hand, including the live instance's URL and library structure.
- `temp/` sits in the bind-mounted workspace and survives container rebuilds; populate it by hand.

Verified API behaviour that the source alone is slow to reveal — read this before designing a command: [docs/grimoire-api-notes.md](docs/grimoire-api-notes.md).
