# grimoire-cli

A command-line interface for [Grimoire](https://github.com/hunter-read/grimoire), the self-hosted TTRPG library manager. Built for agent-driven metadata management — JSON in, JSON out.

Native AOT binary. No runtime required. ~10 MB.

> **Note:** This tool was built using agentic software engineering (AI-assisted coding) and reviewed by a human. See the git history for details.

## Features

- **JSON-only output** — stdout is always valid JSON from the Grimoire API, logs and errors go to stderr
- **Metadata lookup** — search Grimoire's add-on sources and get a per-field diff against what you already have; applying is your own explicit `update`
- **Native AOT** — single self-contained binary, no .NET runtime needed
- **Thin pass-through** — one command, one endpoint; no hidden pre-fetching or client-side policy
- **Config precedence** — CLI flags > environment variables > config file
- **Terse `--help`** — written for AI agents that pay for every token

## Installation

### Homebrew (macOS / Linux)

```bash
brew tap thomaslazar/grimoire-cli
brew install grimoire-cli
```

### Install script (macOS / Linux)

```bash
curl -fsSL https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.sh | bash
```

Installs to `~/.local/bin/grimoire-cli`. Override with environment variables:

```bash
# specific version
curl -fsSL https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.sh | GRIMOIRE_CLI_VERSION=v0.1.0 bash

# custom directory
curl -fsSL https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.sh | GRIMOIRE_CLI_INSTALL_DIR=/usr/local/bin bash
```

### Install script (Windows)

```powershell
irm https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.ps1 | iex
```

Installs to `%LOCALAPPDATA%\grimoire-cli\`. Override with environment variables:

```powershell
# specific version
$env:GRIMOIRE_CLI_VERSION = "v0.1.0"; irm https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.ps1 | iex

# custom directory
$env:GRIMOIRE_CLI_INSTALL_DIR = "C:\tools\grimoire-cli"; irm https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.ps1 | iex
```

### Deb package (Debian / Ubuntu)

Download from the [latest release](https://github.com/thomaslazar/grimoire-cli/releases/latest):

```bash
sudo dpkg -i grimoire-cli_0.1.0_amd64.deb
```

### Download a release

Grab the binary for your platform from the [latest release](https://github.com/thomaslazar/grimoire-cli/releases/latest):

| Platform | Binary |
|----------|--------|
| Linux x64 | `grimoire-cli-linux-x64` |
| Linux ARM64 | `grimoire-cli-linux-arm64` |
| macOS Apple Silicon | `grimoire-cli-osx-arm64` |
| macOS Intel | `grimoire-cli-osx-x64` |
| Windows x64 | `grimoire-cli-win-x64.exe` |
| Windows ARM64 | `grimoire-cli-win-arm64.exe` |

```bash
chmod +x grimoire-cli-linux-x64
mv grimoire-cli-linux-x64 ~/.local/bin/grimoire-cli
```

**macOS users:** the binaries are not signed or notarized, so Gatekeeper blocks them on first run. Clear the quarantine attribute:

```bash
sudo xattr -d com.apple.quarantine grimoire-cli-osx-arm64
```

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

## Agent use cases

grimoire-cli is a set of sharp primitives that AI agents compose into workflows. The CLI handles the Grimoire API; the agent makes the decisions.

### Fill in metadata from a source

You point an agent at a system whose metadata is thin. The agent:

1. Checks that a source can answer for it (`grimoire-cli systems metadata-sources --id <id>`) — the list is empty until an add-on is installed, enabled and *runnable*, which `grimoire-cli addons list` diagnoses
2. Searches it (`grimoire-cli systems metadata-search --id <id> --source-id <src>`), letting the omitted `--query` default to the system's own name
3. Picks a candidate from the ranked results and fetches its diff (`grimoire-cli systems metadata-fetch --id <id> --source-id <src> --identity <identity>`), passing back the same `--query` the candidate came from
4. Reads the per-field diff: `only_incoming` is a safe fill-in, `differs` is a decision, `same` is nothing to do — `current` sits beside `incoming`, so nothing is overwritten blind
5. **Stops and shows you what it proposes**, with `current` against `incoming` for every row it wants to change. You approve, trim, or reject it — nothing is written until you do
6. Applies only the approved fields (`echo '{"system_family":"Shadowrun"}' | grimoire-cli systems update --id <id> --stdin`)

The fetch never writes, so step 4 can be read, edited, or thrown away — and an agent that already knows the record can skip the search with `--paste <source-url>`. Two quirks to expect: `parent_system` and `edition` are derived from the library's folder layout and silently ignore a PATCH, and `urls` comes back as the union with the existing list rather than a replacement.

### Metadata cleanup on request

You notice a gap across the library and describe it:

> "A lot of books have no description. Work out which, fill in the ones you're confident about, and ask me about the rest."

The agent:

1. Enumerates systems (`grimoire-cli systems list --include-children`)
2. Reads each one (`grimoire-cli systems get --id <id>`), which embeds the full metadata for that system's books — `books list` is the wrong tool here: it has no metadata filters and its summary omits `description` entirely, so the per-system read is what makes this one call per system instead of one per book
3. Filters client-side for the gap (`jq '.books[] | select(.description == "" or .description == null)'`)
4. **Comes back with the list before touching anything** — what it found, what it proposes for each, and which ones it is unsure about. A sweep across the library is the last place to discover a bad judgement after the fact
5. Applies the approved set, one book (`echo '{"description":"..."}' | grimoire-cli books update --id <id> --stdin`) or many in one transaction (`grimoire-cli books batch-update --stdin`)
6. Escalates the ones you flagged, or it did, and repeats

A batch verb is skip-and-continue: it exits 3 on a partial failure and names each rejection in `errors`, so an agent that only checks for a zero exit will believe a half-applied change succeeded.

## Configuration

Config is stored at `~/.grimoire-cli/config.json`. Values resolve in this order:

1. **CLI flags** (`--server`)
2. **Environment variables** (`GRIMOIRE_SERVER`)
3. **Config file**

```bash
grimoire-cli config get
grimoire-cli config set server https://grimoire.example.com
```

`login` stores a 30-minute access token plus a 30-day refresh token, and the CLI renews the pair transparently — before a request when the access token is nearly out, and again if the server reports it expired. The renewed pair is written back to the config file. Once the refresh token is gone or the session is revoked, the next command reports `Session expired. Run: grimoire-cli login`.

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
| `books thumbnail --id <id> --output <path\|->` | Download a book's scan-generated cover thumbnail |
| `systems cover get --id <id> --output <path\|->` | Download a system's cover image |
| `systems cover upload --id <id> --file <path>` | Upload a system's cover image (gm or admin) |
| `systems cover delete --id <id>` | Delete a system's uploaded cover image (gm or admin) |
| `systems book-folders list --id <id>` | List a system's tagged subcategory folders |
| `systems book-folders set --id <id> {--input <file> \| --stdin}` | Replace a subcategory folder's tags (gm or admin) |
| `systems book-folders delete --id <id> --path <path>` | Remove a subcategory folder's record (gm or admin) |
| `systems metadata-sources --id <id>` | Add-ons that can supply metadata for this system (gm or admin) |
| `systems metadata-search --id <id> --source-id <src> [--query]` | Ranked candidates from one add-on (gm or admin) |
| `systems metadata-fetch --id <id> --source-id <src> {--identity <i> \| --paste <url>} [--query]` | Diff a candidate against the system; writes nothing (gm or admin) |
| `books metadata-sources --id <id>` | Add-ons that can supply metadata for this book (gm or admin) |
| `books metadata-search --id <id> --source-id <src> [--query]` | Ranked candidates from one add-on (gm or admin) |
| `books metadata-fetch --id <id> --source-id <src> {--identity <i> \| --paste <url>} [--query]` | Diff a candidate against the book; writes nothing (gm or admin) |
| `library rescan [--scope <path>] [--metadata-mode new\|missing\|replace]` | Scan the library for new and changed files; exit 3 if already running (admin) |
| `library scan-status` | Show the running scan's progress (admin) |
| `library cancel-scan` | Stop the running scan (admin) |
| `library cleanup-missing` | Remove DB entries for files no longer on disk (admin; deletes each book's bookmarks too) |
| `addons list` | List installed and available add-ons (admin) |
| `addons refresh` | Fetch the add-on index (admin) |
| `addons install --id <addon-id> [--approve-script]` | Install or upgrade one add-on (admin) |
| `addons update --id <addon-id> [--enabled true\|false] [--script-approved true\|false]` | Enable, disable, or approve one add-on (admin) |
| `addons upgrade-all` | Upgrade every installed add-on; exit 3 if partial (admin) |
| `addons uninstall --id <addon-id>` | Remove one add-on (admin) |
| `addons settings [--index-url <url>] [--allow-scripts true\|false]` | Set the add-on index URL and script switch (admin) |
| `backups list` | List backups, newest first, with the directory and total size (admin) |
| `backups create` | Take a backup now; 409 if one is already running (admin) |
| `backups delete --id <backup-id>` | Delete one archive; irreversible, no prompt (admin) |
| `backups download --id <backup-id> --output <path\|->` | Download one archive as zip; `-` for stdout (admin) |
| `backups settings get` | Read the backup schedule and retention settings (admin) |
| `backups settings set [--schedule off\|hourly\|daily\|weekly] [--hour <0-23>] [--minute <0-59>] [--weekday <0-6>] [--retention-count <n>] [--retention-gb <n>] [--dir <path>]` | Configure the schedule and retention (admin) |
| `files browse [--path <path>] [--limit <1-2000>]` | List a library folder, merged with indexing state (admin) |
| `files upload --destination <path> --file <path> [--relative-dir <path>] [--on-conflict skip\|rename]` | Upload one file; loop for many (admin) |
| `files move --sources <path>... --destination <path> [--on-conflict skip\|rename]` | Move files or folders, keeping their metadata (admin) |
| `files rename --path <path> --new-name <name>` | Rename a file or folder on disk (admin) |
| `files delete --path <path> [--confirm-name <name>] [--delete-files]` | Drop index rows; `--delete-files` also unlinks, irreversibly (admin) |
| `files folder create --parent <path> --name <name> [--container-kind <kind>] [--nsfw]` | Create a folder, optionally a container or NSFW (admin) |
| `files folder delete --path <path> [--confirm-name <name>]` | Delete a folder and its files; always irreversible (admin) |
| `files folder markers --path <path> [--container-kind <kind>] [--nsfw true\|false]` | Set a folder's container/NSFW markers (admin) |
| `files folder scaffold --path <path>` | Create the standard category folders (admin) |
| `files folder contents --path <path>` | Report whether a folder holds content (admin) |
| `genres list` | List the genre vocabulary (tiered via `parent_id`) |
| `licenses list` | List the license vocabulary |
| `parent-systems list` | List the parent-system vocabulary (ships empty) |
| `system-families list` | List the system-family vocabulary |
| `dice-materials list` | List the dice/material vocabulary |
| `self-test` | Verify binary integrity (AOT validation, no network required) |

Every command supports `--help` with examples and caveats.

## Logging

Warnings and errors go to stderr with a timestamp + level prefix:

```
2026-08-07T14:23:45.123Z WARN  Access token has expired or is about to. Run: grimoire-cli login
2026-08-07T14:23:45.123Z ERROR Not authenticated, or the token has expired. Run: grimoire-cli login
```

`--debug` (or `GRIMOIRE_DEBUG=1`) adds one line per HTTP call, plus token-expiry and version-check decisions; `--log-json` switches stderr to single-line JSON. `--pretty` re-indents stdout (compact by default). All three work before or after the subcommand. The bearer token is never logged.

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
  Configuration/  # Config file, env var, flag resolution, JsonContext for AOT
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

Tested against Grimoire **v1.6.1** (v1.6.0 also supported). The CLI warns on login if the server reports a version outside the tested range. See [docs/grimoire-compatibility.md](docs/grimoire-compatibility.md) for the version matrix and the bump procedure.

## License

[MIT](LICENSE)
