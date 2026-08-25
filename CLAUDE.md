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
- Reset with `docker compose -f docker/docker-compose.yml down`, then `rm -rf docker/data docker/library/books docker/addon-index/index.json`, then recreate as above — the boot scan indexes whatever library tree is on disk, so a database-only reset leaves stale rows that survive as `is_missing` and still count toward `book_count`. The fixture add-on manifest (`docker/addon-index/fixture-source.yml`) is checked in; its generated `index.json` is not, and `docker/seed.sh` regenerates it. Unlike `abs-cli`'s, this smoke test is idempotent: it writes, but only to `Shadowrun 4 DE`, and only values fixed in the script, so a re-run converges instead of drifting. Keep it that way — a write whose expected value depends on the fixture's prior state breaks the second run.
- Under docker-outside-of-docker the daemon runs on the host: set `GRIMOIRE_LIBRARY`, `GRIMOIRE_DATA` and `GRIMOIRE_ADDON_INDEX` to host paths (see `docker/env.example`) and reach the stack at `http://host.docker.internal:9481`, not `localhost`. `docker/seed.sh` writes fixtures itself rather than through the daemon, so it reads the container-side path from a separate var, `GRIMOIRE_LIBRARY_LOCAL` (defaults to `docker/library`) — pointing `GRIMOIRE_LIBRARY` at a library outside the repo without also setting this writes fixtures into `docker/library` while the server scans an empty tree.
- **Anything that writes goes to the local stack, never the live instance.** `Shadowrun 4 DE` is left unpatched by `seed.sh`, so `system_family` is deliberately empty as a fixture for the first metadata command — `parent_system` and `edition` are already populated, folder-derived from being a container child. Don't spend it casually.

## Post-PR verification

- After `gh pr create`, watch CI until every check reaches a terminal state and report the result without being asked. A PR is done at "all checks green", not at "PR open".
- `gh pr checks <num>` for one-shot status; `gh run watch <run-id>` for long jobs.

## Docs, specs & roadmap

- **Specs** go in `docs/specs/YYYY-MM-DD-<topic>-design.md`, **plans** in `docs/plans/YYYY-MM-DD-<topic>.md` — never `docs/superpowers/…`, whatever a skill defaults to.
- **Hold spec/plan commits until the implementation branch exists**, then commit spec + plan + code together on that branch so design and delivery are reviewed as one unit.
- **Once a feature branch exists, keep its docs edits on that branch** — they reach `main` via the PR.
- **`CHANGELOG.md` is owned by the release process** (`release/v{version}` branches only). Never edit it from a feature branch.
- **[docs/roadmap.md](docs/roadmap.md) lists intended work only**, and only what the maintainer has decided to do. It is not a status document, not a place to record findings, and not a running tally — never add to it to note that something happened or was discovered. What is implemented is in [docs/grimoire-api-coverage.md](docs/grimoire-api-coverage.md), verified server behaviour in [docs/grimoire-api-notes.md](docs/grimoire-api-notes.md), and what changed is in git.
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

`abs-cli` is the mature reference for this pair of tools. Its conventions, systems and hard-won rules were worked out there first. **The point is to harvest that work, not to mirror it: before deriving a convention here, check whether abs-cli already settled it, and adopt what it settled.** Re-deriving a solved problem is the cost being avoided.

Parity is therefore the default rather than the goal. A difference that follows from a genuine local difference needs no ceremony; record one here only when a reader might otherwise "fix" it back. Do not diverge on something abs-cli deliberately decided without a reason that survives being written down.

Deliberate deviations today:

- **`docs/grimoire-api-coverage.md` is generated, not hand-maintained.** `tools/generate-api-coverage.py` builds it from the spec plus the role dependency on each route in `temp/grimoire`. Grimoire publishes an OpenAPI spec and ABS does not, so abs-cli has to maintain its table by hand. Update `IMPLEMENTED` in that script, not the markdown.
- **Grouped by the spec's own OpenAPI tags** rather than hand-picked resource headings, for the same reason: the grouping is machine-derived and cannot drift from the API.
- **`docs/grimoire-api-notes.md` has no abs-cli counterpart.** A meaningful slice of Grimoire's success responses still type as `{}` in the spec (see below), so verified behaviour needs somewhere to live; ABS's behaviour is read from its server source on demand.
- **The README Commands table rule lives under "Docs, specs & roadmap", not "Command implementation conventions"** where abs-cli keeps it. It is paired there with the API-coverage rule, which abs-cli has no counterpart for, and splitting the pair to match abs-cli's placement would cost more than the drift does.
- **No confirm-gated command.** abs-cli exempts `libraries delete` from thin pass-through with a type-the-name prompt. `library cleanup-missing` settled the question here and takes neither a prompt nor a `--yes`: the callers are agents, so a prompt is either bypassed by a flag that becomes boilerplate or hangs a non-interactive caller. The warning lives in the help text, where an agent reads it.
- **The `release` skill carries an extra step reconciling the supported server
  range.** `MinSupportedVersion` / `MaxTestedVersion`, the compatibility matrix
  and the README line must agree before a tag is cut. abs-cli has no counterpart
  because it has no version gate. Its preflight also differs: the
  `docker/users.json.example` fixture must be copied before first boot, and the
  `--version` check asserts bare output because PR builds carry a
  `+pr-<n>.<sha7>` suffix.

The docs set and the release plumbing (`install.sh`, `install.ps1`, deb packaging, Homebrew tap job) are in place, and the `thomaslazar/homebrew-grimoire-cli` tap repo exists. What remains before a first release is narrower — see [docs/releasing.md](docs/releasing.md).

## CLI design principles

- **Thin pass-through.** Each command maps to a single Grimoire API endpoint. No smart defaults that pre-fetch extra data, no reading the response to emit derived warnings, no client-side mirroring of server policy. Workflows spanning multiple endpoints are the caller's job to compose. Higher-level orchestration belongs in the calling layer, not here.
- **JSON in, JSON out.** stdout is valid JSON from the API; logs and human-facing lines go to stderr.

## Command implementation conventions

- **Role tagging.** Every command whose endpoint carries a non-default role dependency MUST call `command.AddRoleRequired("<role>")` immediately after construction. Grimoire has three role dependencies (`temp/grimoire/backend/routers/`): `require_admin` → tag `admin`, `require_gm_or_admin` → tag `gm or admin`, and `require_not_guest`, which is the default for reads and gets **no** tag. A route guarded by `get_current_user` (or `get_current_user_optional`, or nothing at all, as `POST /api/auth/login` is) carries no role and likewise gets no tag. The tag must agree with the router's actual dependency, not with what the docs claim.
- **Role hint mirroring.** When the service call passes a `permissionHint`, it MUST agree with the tag and read as a noun phrase, because `GrimoireApiClient` renders it as `Permission denied. This operation requires {hint}.` — tag `admin` ↔ hint `"the admin role"`; tag `gm or admin` ↔ hint `"the gm or admin role"`. The help-section tag and the 403 message always agree.
- **`--server` and `--token` are declared per-subcommand on commands that consume a saved token**, matching abs-cli, and threaded into `CommandHelper.BuildClient` so the flag tier of `flags > env > file` is actually reachable. Two exceptions, both principled: `login` takes `--server` alone, because it *produces* the token and builds its own client directly; `config` and `self-test` take neither, having no API call at all.
- **Positional args for value-only subcommands.** Subcommands whose parameters ARE the values, with no ID key, take positional args rather than flags — `config set <key> <value>`. ID-keyed resources use `update --id --field`, where the flags mirror the API's body field names.
- **README Commands table and API coverage** are updated in the same PR — see [Docs, specs & roadmap](#docs-specs--roadmap).

`systems list` / `systems get` need no role tag: any authenticated non-guest can read them, so the mechanism is currently exercised only by `RoleSectionTests`. The first write command is what will use it for real, and is also the first to decide whether write commands take `--server` / `--token`.

## Help text

`--help` is the primary interface for the AI agents that consume this CLI, and every word costs tokens. Keep it terse and self-contained.

- **Terse.** One-liners over prose, bullets over paragraphs, no "useful when…" framing. Calibrate against `SystemsCommand.cs`, whose Notes blocks are sized to what abs-cli allows.
- **Document every non-obvious caveat** at the call site — destructive side effects, hidden API behaviours (children hidden before filters apply, folder-derived fields that ignore a PATCH), outcome-affecting defaults. The CLI is thin, so API quirks leak through; help text is where they must surface, not spec docs.
- **Don't state what's already visible.** Skip anything apparent from the flags, subcommand list, or response-shape sample: no verb-by-verb group narration, no "X cannot change" when there's no such flag, no restating a flag's own description or a response field. A `ChoiceOption` renders its own value set, so the description must not repeat it.
- **Cross-references are one-way** (consumer → producer) and allowed only when required to use *this* command: where a required input comes from, a behaviour warning, a piping pitfall, a shared external dependency. Never sell another command's use case.

These rules exist because help text sits on the hot path for the agents driving this CLI, where every repeated word is paid for on each invocation. They are not a style guide to enforce everywhere: `login` states its `--password` caveat in both the flag description and the Notes, and that stays — it runs once per month, and a security caveat is not the worse for being said twice.

## API client generation

**The API interface is generated from the OpenAPI spec by tooling, not hand-written.** The spec is a published contract and a standard artefact; generating from it is what makes the client's surface reliable rather than a transcription that can quietly drift. The generator is the baseline, and hand-written code covers only what the spec cannot express.

- **The spec comes from the running stack, never from a file.** `docker/docker-compose.yml` pins the exact build the CLI targets, so its `/api/openapi.json` cannot disagree with what we build against. A snapshot on disk can be stale and has to be remembered; a container cannot contradict itself.
  ```bash
  docker compose -f docker/docker-compose.yml up -d --wait
  # then generate straight from http://host.docker.internal:9481/api/openapi.json
  ```
- **A digest pin is a temporary exception, not the convention.** The pin is normally a release tag. It is a digest only while the targeted server version is unreleased — right now, because the CLI is being built alongside active server development. `edge` moves, and a floating tag would make a red CI run indistinguishable from upstream changing, whereas a digest is as reproducible as a release tag. **When 1.6.0 releases, the pin goes back to a release tag** (`hunterreadca/grimoire:1.6.0`); see workstream C in [docs/grimoire-1.6.0-migration.md](docs/grimoire-1.6.0-migration.md). Until then, move it deliberately and regenerate the client in the same commit, and check whether it has gone stale with:
  ```bash
  docker pull hunterreadca/grimoire:edge
  docker inspect hunterreadca/grimoire:edge --format '{{index .RepoDigests 0}}'
  ```
- **Released-version support lives on `support/grimoire-1.5.6`**, where the pin is the `1.5.6` tag. Fixes for 1.5.6 are made there and released from there, then cherry-picked forward — not merged, since `main` no longer has the DTO layer they were written against.
- **Generate with a .NET-native generator** — Kiota is the fit: a `dotnet tool`, handles the spec's OpenAPI 3.1, emits C#. No node or java is available in the devcontainer.
- **What the spec gives you and what it does not.** Today's spec has 342 component schemas and 282 operations, 238 of whose success responses carry a schema — request bodies, paths, methods, query parameters and most response shapes all come from the generator and are trustworthy. The remainder are 204s or still type as `{}`. Neither gap matters for runtime deserialization any more: the CLI passes response bytes through unmodified rather than reading them into a typed model (see [docs/input-output.md](docs/input-output.md)). The generated response models are used only to render `--help` response samples, via `tools/GenerateJsonExamples`.
- **On a Grimoire version bump, regenerate and diff.** That diff is the authoritative list of what changed in the API surface, and it replaces reading release notes and hoping. See [docs/grimoire-compatibility.md](docs/grimoire-compatibility.md).
- **Regenerate with `bash tools/generate-api-client.sh`** — reads the spec from the running stack and rewrites `src/GrimoireCli/Generated/`. Never hand-edit that tree; it is committed so a version bump produces a reviewable diff.
- **The spec is normalised on the way into the generator, by `tools/normalize-spec.py`.** Kiota drops every property of a schema reached only through an `anyOf: [array of $ref, null]` — [microsoft/kiota#2338](https://github.com/microsoft/kiota/issues/2338), open and present in 1.34.1, the latest release. FastAPI emits that wrapper for any `Optional[list[Model]]`, which is how `publishers`, `urls` and `character_builder_urls` are declared, so without it those models generate as empty shells that know none of their own fields. Collapsing the wrapper to its array branch restores them. **Recheck #2338 whenever the Kiota pin moves**: if it is fixed, delete the normaliser and its call rather than carrying a workaround for a bug that no longer exists.
- **Never run `kiota update`.** It refetches the spec from `descriptionLocation` — the server's raw one — and would regenerate without the normalisation above, silently reinstating the empty models. `tools/generate-api-client.sh` is the only supported path.
- **The generated models are what validates a request body.** A model's own field list — `GetFieldDeserializers().Keys` — is the allowed set for that endpoint, so there is no hand-written mirror of the API's fields anywhere in this repo and none is to be added. See `JsonBodyInput.Validate`.

## Grimoire Source Reference

The upstream source is the authoritative reference for **behaviour and response shapes** — the half the spec does not cover (see above). The published docs have been wrong before.

- Expected location: `temp/grimoire/` (gitignored). **Pin it to the version the branch targets.** On `support/grimoire-1.5.6` that is a release tag; never upstream `main`, which carries work no instance runs:
  ```bash
  # Match MinSupportedVersion / MaxTestedVersion in src/GrimoireCli/Api/GrimoireApiClient.cs
  git clone --depth 1 --branch v1.5.6 https://github.com/hunter-read/grimoire.git temp/grimoire
  ```
- **No spec snapshot is kept.** The spec comes from the running stack's `/api/openapi.json` at the moment it is needed — see [API client generation](#api-client-generation). A file on disk can be stale; the pinned container cannot.
- **On `main` there is no tag to pin to yet**, so do not clone at all: the image ships its own backend source, and the digest-pinned container is by definition the exact build being targeted. Read behaviour out of it — `docker exec docker-grimoire-1 grep -n EXPIRE backend/sessions.py` — and repin a clone once 1.6.0 tags.
- `temp/deployment-docs/` — deployment design records copied in by hand, including the live instance's URL and library structure.
- `temp/` sits in the bind-mounted workspace and survives container rebuilds; populate it by hand.

Verified API behaviour that the source alone is slow to reveal — read this before designing a command: [docs/grimoire-api-notes.md](docs/grimoire-api-notes.md).
