# grimoire-cli

A command-line interface for [Grimoire](https://github.com/hunter-read/grimoire), the self-hosted TTRPG library manager. Built for agent-driven metadata management — JSON in, JSON out.

Native AOT binary. No runtime required. ~10 MB.

> **Note:** This tool was built using agentic software engineering (AI-assisted coding) and reviewed by a human. See the git history for details.

## Features

- **JSON-only output** — stdout is always valid JSON from the Grimoire API, logs and errors go to stderr
- **Native AOT** — single self-contained binary, no .NET runtime needed
- **Thin pass-through** — one command, one endpoint; no hidden pre-fetching or client-side policy
- **Config precedence** — CLI flags > environment variables > config file
- **Terse `--help`** — written for AI agents that pay for every token

## Installation

Build from source, or download a binary from a CI run.

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 --self-contained true /p:PublishAot=true
# Binary at: src/GrimoireCli/bin/Release/net10.0/linux-x64/publish/grimoire-cli
```

Swap `-r` for your platform: `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, `win-x64`, `win-arm64`.

### From a CI build

Every pull request publishes a binary per platform as a workflow artifact (5-day retention):

```bash
gh run download <run-id> -n grimoire-cli-osx-arm64
chmod +x grimoire-cli
```

**macOS users:** the binaries are not signed or notarized, so Gatekeeper blocks them on first run. Clear the quarantine attribute with `sudo xattr -d com.apple.quarantine grimoire-cli`.

CI-built binaries stamp their origin into the version, so an installed one identifies itself — `grimoire-cli --version` prints `0.1.0+pr-1.a1b2c3d` for a pull-request build and a bare `0.1.0` for a release. The same string goes out in the `User-Agent`.

## Quick start

```bash
# Authenticate (prompts for anything you leave out)
grimoire-cli login --server https://grimoire.example.com

# List game systems
grimoire-cli systems list | jq

# One system, with its books
grimoire-cli systems get --id <system-id>

# Verify the binary itself, no server needed
grimoire-cli self-test
```

## Configuration

Config is stored at `~/.grimoire-cli/config.json`. Values resolve in this order:

1. **CLI flags** (`--server`)
2. **Environment variables** (`GRIMOIRE_SERVER`, `GRIMOIRE_TOKEN`)
3. **Config file**

```bash
grimoire-cli config get
grimoire-cli config set server https://grimoire.example.com
```

The JWT is valid for 30 days. Grimoire has **no refresh endpoint**, so an expired token means logging in again — the CLI warns when one is close to expiry.

## Commands

| Command | Description |
|---------|-------------|
| `login [--server <url>] [--username <u>] [--password <pw> \| --password-stdin]` | Authenticate and store the JWT (flags fall back to interactive prompts) |
| `me` | Show the authenticated account (id, username, role, flags) |
| `config get` | Show current configuration |
| `config set <key> <value>` | Set a configuration value (`server` is the only valid key) |
| `systems list [--sort name\|book_count\|page_count\|year] [--desc] [--genre <g>] [--family <f>] [--parent-system <p>] [--edition <e>] [--license <l>] [--explicit true\|false] [--parent-id <id>] [--include-children]` | List all game systems |
| `systems get --id <id> [--book-sort category\|title\|page_count\|year] [--book-desc] [--genre <g>] [--category <c>] [--explicit true\|false]` | Get a single game system, with its books |
| `systems update --id <id> {--input <file> \| --stdin}` | Update one system's metadata (gm or admin) |
| `systems batch-update {--input <file> \| --stdin}` | Update many systems in one transaction; exit 3 if partial (gm or admin) |
| `systems batch-tag {--input <file> \| --stdin}` | Add tags to many systems, additively; exit 3 if partial (gm or admin) |
| `books list [--system-id <id>] [--category <c>] [--limit <n>] [--offset <n>]` | List books (defaults to 100 results) |
| `books get --id <id>` | Get one book |
| `books update --id <id> {--input <file> \| --stdin}` | Update one book's metadata (gm or admin) |
| `books batch-update {--input <file> \| --stdin}` | Update many books in one transaction; exit 3 if partial (gm or admin) |
| `books batch-tag {--input <file> \| --stdin}` | Add tags to many books, additively; exit 3 if partial (gm or admin) |
| `books reindex --id <id> [--ocr-dpi <n>]` | Re-run OCR on one book (gm or admin) |
| `books rescan --id <id>` | Re-read one book from disk and rebuild its index (gm or admin) |
| `library rescan [--scope <path>] [--metadata-mode new\|missing\|replace]` | Scan the library for new and changed files; exit 3 if already running (admin) |
| `library scan-status` | Show the running scan's progress (admin) |
| `library cancel-scan` | Stop the running scan (admin) |
| `addons list` | List installed and available add-ons (admin) |
| `addons refresh` | Fetch the add-on index (admin) |
| `addons install --id <addon-id> [--approve-script]` | Install or upgrade one add-on (admin) |
| `addons update --id <addon-id> [--enabled true\|false] [--script-approved true\|false]` | Enable, disable, or approve one add-on (admin) |
| `addons upgrade-all` | Upgrade every installed add-on; exit 3 if partial (admin) |
| `addons uninstall --id <addon-id>` | Remove one add-on (admin) |
| `addons settings [--index-url <url>] [--allow-scripts true\|false]` | Set the add-on index URL and script switch (admin) |
| `self-test` | Verify binary integrity (AOT validation, no network required) |

Every command supports `--help` with examples and caveats.

## Logging

Warnings and errors go to stderr with a timestamp + level prefix:

```
2026-08-07T14:23:45.123Z WARN  Access token has expired or is about to. Run: grimoire-cli login
2026-08-07T14:23:45.123Z ERROR Not authenticated, or the token has expired. Run: grimoire-cli login
```

`--debug` and `--log-json` are root options, so they go **before** the subcommand — `grimoire-cli --debug systems list`, not `systems list --debug`. `--debug` (or `GRIMOIRE_DEBUG=1`) adds one line per HTTP call, plus token-expiry and version-check decisions; `--log-json` switches stderr to single-line JSON. The bearer token is never logged.

## Development

### Dev container (recommended)

The repo includes a dev container with .NET 10, the AOT toolchain (`clang`, `zlib1g-dev`), `gh`, Docker-outside-of-Docker, and `python3-fitz` (MuPDF bindings, used to generate library fixtures).

After changing anything under `.devcontainer/`, rebuild the container — **Dev Containers: Rebuild Container** in VS Code — or the new tooling won't be present.

### Running tests

```bash
# Unit tests
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj

# Self-test (AOT integrity checks, no network needed)
dotnet run --project src/GrimoireCli -- self-test

# Smoke test against a local Grimoire
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
bash docker/smoke-test.sh
docker compose -f docker/docker-compose.yml down && rm -rf docker/data docker/library/books
```

The fixture copy is required before the first boot — Grimoire seeds its users from `/data/users.json` at startup, and without it the stack comes up with no users. Seeded logins are `admin/admin`, `gm/gm`, `player/player`; throwaway dev credentials for a throwaway stack. `docker/seed.sh` then populates the library with fixture books — `smoke-test.sh` asserts on that fixture set and fails without it.

From inside the dev container the daemon runs on the host, so reach the stack at `http://host.docker.internal:9481` rather than `localhost`, and set `GRIMOIRE_LIBRARY` / `GRIMOIRE_DATA` to host paths — see `docker/env.example`. `docker/seed.sh` writes fixtures itself rather than through the daemon, so it reads a third var, `GRIMOIRE_LIBRARY_LOCAL` — the same directory's *container*-side path, defaulting to `docker/library`.

### Project structure

```
src/GrimoireCli/
  Commands/       # CLI command definitions (System.CommandLine)
  Api/            # HTTP client, token helper, debug handler
  Generated/      # Kiota client, generated from the OpenAPI spec — never hand-edit
  Models/         # JsonContext — every type crossing the JSON boundary, for AOT
  Configuration/  # Config file, env var, flag resolution
  Output/         # JSON stdout, stderr logging setup
tests/GrimoireCli.Tests/   # unit tests
docker/
  docker-compose.yml   # disposable Grimoire instance
  users.json.example   # first-run users, seeded at startup
  smoke-test.sh        # end-to-end CLI smoke test
docs/                  # API notes, roadmap, specs and plans
temp/                  # reference material, gitignored — see CLAUDE.md
```

`src/GrimoireCli/Generated/` regenerates with `bash tools/generate-api-client.sh` against a running stack; see [CLAUDE.md](CLAUDE.md) for the policy.

Verified API behaviour worth reading before designing a command: [docs/grimoire-api-notes.md](docs/grimoire-api-notes.md).

## Compatibility

Tested against Grimoire **v1.5.6**. The CLI warns on login if the server reports a version outside the tested range. See [docs/grimoire-compatibility.md](docs/grimoire-compatibility.md) for the version matrix and the bump procedure.

## License

[MIT](LICENSE)
