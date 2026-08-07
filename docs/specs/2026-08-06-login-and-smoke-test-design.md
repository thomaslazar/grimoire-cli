# Login and smoke test — design

Date: 2026-08-06
Status: approved, not yet implemented

## Goal

`grimoire-cli login` works end to end against a disposable local Grimoire, and a
smoke test proves it on every pull request. This is the first build: it makes the
existing scaffolding demonstrably work rather than adding surface.

Out of scope, deliberately:

- metadata commands (`systems update`, `books update`, `rescan`) — designed later
- library fixtures and `docker/seed.sh` — login needs no content to exist
- a `setup` command, a `--config` flag, bulk endpoints

## Grounding

Verified against Grimoire v1.5.4 (`temp/grimoire` at tag `v1.5.4`, the release both
the live instance and `docker-compose.yml` run) and the live OpenAPI spec.

| Fact | Source |
|---|---|
| `/api/health`, `/api/about`, `/api/auth/me`, `/api/auth/login` all exist | live `openapi.json` v1.5.4 |
| Users are seeded from `{DATA_PATH}/users.json` on first startup — array of `{username, password, role}`, roles `admin\|gm\|player`, plaintext or bcrypt | `backend/seed_users.py` |
| After seeding, the file is renamed to `users.json.imported`; the rename is unguarded and `seed_users` is called with no `except` | `backend/seed_users.py`, `backend/main.py:135` |
| Auth rate limiting is configurable: `AUTH_RATE_LIMIT` (default `10/minute`), `RATE_LIMIT_ENABLED` disables it | `backend/security.py:32,45` |
| `hunterreadca/grimoire:latest` and `:1.5.4` are the same digest; `:edge` tracks unreleased `main` | Docker Hub |
| `host.docker.internal` resolves from the devcontainer (OrbStack) | verified in container |
| Config path is `$HOME/.grimoire-cli/config.json`, hardcoded; the devcontainer's `HOME` is not the host's, so local runs cannot clobber live-server credentials | `ConfigManager.cs:17`, `devcontainer.json` |
| `dotnet test` exits 0 with zero tests, so CI is green today | verified in container |

## Decisions

### Bootstrap: `users.json` via a `/data` directory bind

Grimoire seeds its own users at startup, so no `POST /api/auth/setup` call is
needed. `/data` becomes a host-directory bind holding the fixture. This gives
`gm` and `player` accounts for free, which the role-gated `PATCH` endpoints
(`require_gm_or_admin`) will need later.

Rejected: calling `/api/auth/setup` from the smoke script (admin only, and a
once-per-volume branch in the script); adding a `grimoire-cli setup` command
(useful once per instance, widens this build).

### CI starts the stack with `docker compose`, not `services:`

GitHub Actions starts `services:` containers **before** `actions/checkout`, so a
service container cannot see `users.json` — it boots, finds no fixture, and seeds
nothing. abs-cli is unaffected because ABS has an API-driven first run.

Bringing the stack up in a step after checkout means CI and local development run
the *identical* compose definition, with no drift between them. This is a
deliberate divergence from abs-cli's job shape, for a reason abs-cli doesn't have.

Rejected: `services:` plus `/api/auth/setup` (two different bootstrap paths, so CI
and local stop testing the same thing); `services:` plus `docker cp` and a restart
to trigger seeding (works, but needs a paragraph of comment to justify itself).

### No config isolation

The devcontainer's `HOME` is `/home/vscode` inside the container, and
`devcontainer.json` binds only `~/.claude/projects` and the peon packs. A smoke
run in the container writes `/home/vscode/.grimoire-cli/config.json` and cannot
touch the host's. Live testing happens with a PR build installed from its CI
artifact and run against the real server. No `--config` flag, no
`GRIMOIRE_CONFIG` env var, no `HOME` override.

## Changes

### `docker/docker-compose.yml`

- replace the `grimoire-data` named volume with `${GRIMOIRE_DATA:-./data}:/data`
- add `RATE_LIMIT_ENABLED=false` — the smoke test logs in repeatedly and the
  default is `10/minute`
- add a healthcheck on `/api/health` so `docker compose up -d --wait` blocks
  until the API answers
- header comment: reach the stack from the devcontainer at
  `http://host.docker.internal:9481`, replacing the `docker inspect`
  container-IP instructions
- keep `OPDS_ENABLED=false` (upstream #276) and `OCR_ENABLED=false`
- reset is now `docker compose down && rm -rf docker/data`

### `docker/users.json.example` (committed)

```json
[
  { "username": "admin",  "password": "admin",  "role": "admin"  },
  { "username": "gm",     "password": "gm",     "role": "gm"     },
  { "username": "player", "password": "player", "role": "player" }
]
```

Plaintext credentials in a public repo, knowingly: this is a fixture for a
throwaway local instance with a fixed dev `SECRET_KEY`, never for a real
deployment. A comment in the file says so.

`docker/data/` is added to `.gitignore`. The example must be copied to
`docker/data/users.json` **before the stack first starts** — by the CI step in the
workflow, and by the developer locally as documented in the compose header and
`docker/.env.example`. `smoke-test.sh` does not manage the stack or the fixture; it
asserts against an already-running instance, so it behaves identically in both
places. A comment records that Grimoire renames the file after seeding and
that the rename is unguarded, so it must never be mounted as a single file.

### `.gitignore` and `docker/.env.example`

Ignore `docker/data/`. Add `docker/.env.example` documenting `GRIMOIRE_DATA` and
`GRIMOIRE_LIBRARY` as **host** paths, required under docker-outside-of-docker.

### `docker/smoke-test.sh`

Environment: `GRIMOIRE_SERVER` (default `http://host.docker.internal:9481`),
`CLI` (default the Debug build path). Mirrors abs-cli's `docker/smoke-test.sh`
in name, shape, and env-var convention; `GRIMOIRE_SERVER` is reused rather than a
new `GRIMOIRE_URL`, because the CLI already reads it, so one variable feeds both
the script and the binary.

Assertions, each failing loudly with the offending output:

1. `/api/health` answers within a bounded wait loop
2. `login --username admin --password-stdin` exits 0, retried within a bounded
   loop so a health-before-seeding race fails slowly rather than flakily
3. `config.json` contains the expected server and a non-empty token
4. `systems list` writes jq-parseable JSON to **stdout** and logs to **stderr** —
   this proves the token authenticates *and* enforces the JSON-out rule
5. `login` with a wrong password exits 2 and leaves `config.json` unchanged
6. `self-test` exits 0

### `.github/workflows/build.yml`

New `smoke-test` job, `needs: unit-test`, alongside the unchanged `unit-test` and
`build` jobs: checkout, setup-dotnet, publish the linux-x64 AOT binary to
`./publish`, copy the fixture, `docker compose -f docker/docker-compose.yml up -d
--wait`, then `bash docker/smoke-test.sh` with `GRIMOIRE_SERVER=http://localhost:9481`
and `CLI=./publish/grimoire-cli`. No `seed.sh` step — there are no library
fixtures yet.

### `tests/GrimoireCli.Tests` (optional)

The project is wired for xunit.v3 and empty. Four cheap tests give the
`unit-test` job something real: `LoginCommand.ReadPasswordFromStdin`,
`GrimoireApiClient.ExtractToken`, `ConfigManager.Resolve` precedence
(flag > env > file), and `CompareVersions` ordering.

## Risks

- ~~**`/data` ownership.**~~ Resolved before implementation: the upstream
  `Dockerfile` declares no `USER`, so Grimoire runs as root and can write a
  host-created bind directory. No `chown` step needed.
- **`--wait` needs the healthcheck to be correct.** If `/api/health` returns 200
  before seeding finishes, the first `login` could race the user creation. The
  wait loop in `smoke-test.sh` covers this by retrying login, not just health.
- **`docker compose` from the devcontainer** resolves bind paths against the
  *host* filesystem under docker-outside-of-docker, so `GRIMOIRE_DATA` needs a
  host path locally, exactly like the existing `GRIMOIRE_LIBRARY`. CI is native
  and uses the relative default. `docker/.env.example` documents both.

## Acceptance

- `docker compose -f docker/docker-compose.yml up -d --wait` yields a healthy
  instance with `admin`, `gm`, and `player` present
- `bash docker/smoke-test.sh` passes locally against that stack and in CI
- `dotnet format GrimoireCli.sln --verify-no-changes` is clean
- the `smoke-test` job passes on a pull request, and the `build` job's artifacts
  remain downloadable and runnable
