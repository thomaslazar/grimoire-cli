# grimoire-cli

A command-line interface for [Grimoire](https://github.com/hunter-read/grimoire), the self-hosted TTRPG library manager. Built for agent-driven metadata management — JSON in, JSON out.

Native AOT binary. No runtime required. ~14 MB.

> **Note:** This tool was built using agentic software engineering (AI-assisted coding) and reviewed by a human. See the git history for details.

## Features

- **Metadata lookup** — search Grimoire's add-on sources and get a per-field diff against what you already have; applying is your own explicit `update`
- **Library file management** — browse the tree with its indexing state, upload, move, rename and delete; create folders, mark containers, scaffold a system's category folders
- **Duplicate resolution** — scan for candidates, compare copies side by side, file one under another as a variant, promote a different copy, merge metadata across them
- **Search and tags** — full-text page search plus `field:value` metadata filters, and tags across every resource type
- **Backups** — take one before a bulk change, then list, download and manage the schedule
- **Batch operations** — update or tag many systems or books in one transaction, with per-item errors and exit 3 on a partial
- **Transparent session renewal** — Grimoire's 30-minute access token is refreshed before the request, and again if the server reports it expired
- **JSON-only output** — stdout is always valid JSON from the Grimoire API, logs and errors go to stderr
- **Native AOT** — single self-contained binary, no .NET runtime needed
- **Thin pass-through** — one command, one endpoint; no hidden pre-fetching or client-side policy
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
curl -fsSL https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.sh | GRIMOIRE_CLI_VERSION=v0.2.0 bash

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
$env:GRIMOIRE_CLI_VERSION = "v0.2.0"; irm https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.ps1 | iex

# custom directory
$env:GRIMOIRE_CLI_INSTALL_DIR = "C:\tools\grimoire-cli"; irm https://raw.githubusercontent.com/thomaslazar/grimoire-cli/main/install.ps1 | iex
```

### Deb package (Debian / Ubuntu)

Download from the [latest release](https://github.com/thomaslazar/grimoire-cli/releases/latest):

```bash
sudo dpkg -i grimoire-cli_0.2.0_amd64.deb
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

CI-built binaries stamp their origin into the version, so an installed one identifies itself — `grimoire-cli --version` prints `0.2.0+pr-1.a1b2c3d` for a pull-request build and a bare `0.2.0` for a release. The same string goes out in the `User-Agent`.

## Quick start

```bash
# Authenticate (prompts for anything you leave out)
grimoire-cli login --server https://grimoire.example.com

# List game systems
grimoire-cli systems list | jq

# One system, with its books and their full metadata
grimoire-cli systems get --id <system-id>

# Search page text and metadata together
grimoire-cli search --query "author:'Ben Robbins' year:>2010"

# What is on disk, and which of it Grimoire has indexed
grimoire-cli files browse --path "books/Call of Cthulhu"

# Edit one book
echo '{"description":"A haunted-house scenario."}' | grimoire-cli books update --id <book-id> --stdin

# Take a backup before a bulk change
grimoire-cli backups create

# Look for duplicate copies
grimoire-cli duplicates scan --accuracy exact

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

1. **Environment variables** (`GRIMOIRE_SERVER`)
2. **Config file**

`login` is the exception: it takes `--server`, falls back to `GRIMOIRE_SERVER`, then prompts. No other command takes a server flag — the access token lives in the config file alone, so there is nothing for a per-command flag to switch to.

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
| `books file --id <id> --output <path\|->` | Download the book file as stored |
| `books page --id <id> --page <n> [--width <px>] --output <path\|->` | Render or extract one page as an image |
| `books toc --id <id>` | The book's table of contents (PDF, EPUB or DjVu) |
| `books page-text --id <id> --page <n>` | The text of one page |
| `books page-words --id <id> --page <n>` | Word bounding boxes for one page |
| `systems cover get --id <id> --output <path\|->` | Download a system's cover image |
| `systems cover upload --id <id> --file <path>` | Upload a system's cover image (gm or admin) |
| `systems cover delete --id <id>` | Delete a system's uploaded cover image (gm or admin) |
| `systems cover from-source --id <id> --source-type <type> --source-id <id>` | Set a system cover from a library image (gm or admin) |
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
| `library stats` | Counts and sizes across the whole library |
| `downloads archive --type <type> [scope flags] [--fmt <fmt>] --output <path\|->` | Download a slice of the library as one archive |
| `addons list` | List installed and available add-ons (admin) |
| `addons refresh` | Fetch the add-on index (admin) |
| `addons install --id <addon-id> [--approve-script]` | Install or upgrade one add-on (admin) |
| `addons update --id <addon-id> [--enabled true\|false] [--script-approved true\|false]` | Enable, disable, or approve one add-on (admin) |
| `addons upgrade-all` | Upgrade every installed add-on; exit 3 if partial (admin) |
| `addons uninstall --id <addon-id>` | Remove one add-on (admin) |
| `addons settings [--index-url <url>] [--allow-scripts true\|false]` | Set the add-on index URL and script switch (admin) |
| `addons verify-index --url <url>` | Check whether an add-on index URL is trusted |
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
| `files delete --path <path> [--confirm-name <name>] [--delete-files]` | Drop index entries; `--delete-files` also deletes the files, irreversibly (admin) |
| `files folder create --parent <path> --name <name> [--container-kind <kind>] [--nsfw] [--frames-container]` | Create a folder, optionally a container, a frame folder, or NSFW (admin) |
| `files folder markers --path <path> [--container-kind <kind>] [--nsfw true\|false] [--frames-container true\|false]` | Set a folder's container/NSFW/frame markers (admin) |
| `files folder scaffold --path <path>` | Create the standard category folders (admin) |
| `files folder contents --path <path>` | Report whether a folder holds content (admin) |
| `maps list [--map-type <t>] [--folder <path>] [--limit <n>] [--offset <n>]` | List maps (defaults to 100 results) |
| `maps get --id <id>` | Get one map, with its detected grid and any manual override |
| `maps thumbnail --id <id> --output <path\|->` | Download a map's scan-generated thumbnail |
| `maps file --id <id> --output <path\|->` | Download the map file as stored |
| `maps page --id <id> --page <n> [--width <px>] --output <path\|->` | Render one page of a PDF map as WebP |
| `maps vtt image --id <id> --output <path\|->` | Download the battlemap inside a Universal VTT file |
| `maps vtt data --id <id>` | Grid and feature counts from a Universal VTT file |
| `maps vtt export --id <id> --output <path\|->` | Export a raster map as a Universal VTT file |
| `maps update --id <id> {--input <file> \| --stdin}` | Update one map's metadata (gm or admin) |
| `maps batch-update {--input <file> \| --stdin}` | Update many maps in one transaction; exit 3 if partial (gm or admin) |
| `maps batch-tag {--input <file> \| --stdin}` | Add tags to many maps, additively; exit 3 if partial (gm or admin) |
| `maps folders list` | List tagged map folders |
| `maps folders set {--input <file> \| --stdin}` | Replace one map folder's tags (gm or admin) |
| `maps folders batch-set {--input <file> \| --stdin}` | Set tags on many map folders in one transaction (gm or admin) |
| `models list [--limit <n>] [--offset <n>]` | List 3D models (defaults to 100 results) |
| `models get --id <id>` | Get one model, with its derived support pair |
| `models thumbnail --id <id> --output <path\|->` | Download a model's rendered thumbnail |
| `models file --id <id> --output <path\|->` | Download the 3D model file |
| `models update --id <id> {--input <file> \| --stdin}` | Update one model's metadata (gm or admin) |
| `models batch-update {--input <file> \| --stdin}` | Update many models in one transaction; exit 3 if partial (gm or admin) |
| `models batch-tag {--input <file> \| --stdin}` | Add tags to many models, additively; exit 3 if partial (gm or admin) |
| `models folders list` | List tagged model folders |
| `models folders set {--input <file> \| --stdin}` | Replace one model folder's tags (gm or admin) |
| `models folders batch-set {--input <file> \| --stdin}` | Set tags on many model folders in one transaction (gm or admin) |
| `tokens list [--limit <n>] [--offset <n>]` | List tokens (defaults to 100 results) |
| `tokens get --id <id>` | Get one token |
| `tokens thumbnail --id <id> --output <path\|->` | Download a token's rendered thumbnail |
| `tokens file --id <id> --output <path\|->` | Download the token image as stored |
| `tokens update --id <id> {--input <file> \| --stdin}` | Update one token's metadata (gm or admin) |
| `tokens batch-update {--input <file> \| --stdin}` | Update many tokens in one transaction; exit 3 if partial (gm or admin) |
| `tokens batch-tag {--input <file> \| --stdin}` | Add tags to many tokens, additively; exit 3 if partial (gm or admin) |
| `tokens folders list` | List tagged token folders |
| `tokens folders set {--input <file> \| --stdin}` | Replace one token folder's tags (gm or admin) |
| `tokens folders batch-set {--input <file> \| --stdin}` | Set tags on many token folders in one transaction (gm or admin) |
| `audio list [--limit <n>] [--offset <n>]` | List audio tracks (defaults to 100 results) |
| `audio get --id <id>` | Get one audio track |
| `audio artwork --id <id> --output <path\|->` | Download an audio track's artwork |
| `audio cover get --id <id> --output <path\|->` | Download the track's set cover image (gm or admin) |
| `audio cover upload --id <id> --file <path>` | Upload a cover image for the track (gm or admin) |
| `audio cover delete --id <id>` | Remove the track's set cover image (gm or admin) |
| `audio cover from-source --id <id> --source-type <type> --source-id <id>` | Set a track cover from a library image (gm or admin) |
| `audio file --id <id> --output <path\|->` | Download the audio file |
| `audio update --id <id> {--input <file> \| --stdin}` | Update one audio track's metadata (gm or admin) |
| `audio batch-update {--input <file> \| --stdin}` | Update many audio tracks in one transaction; exit 3 if partial (gm or admin) |
| `audio batch-tag {--input <file> \| --stdin}` | Add tags to many audio tracks, additively; exit 3 if partial (gm or admin) |
| `audio folders list` | List tagged audio folders |
| `audio folders set {--input <file> \| --stdin}` | Replace one audio folder's tags (gm or admin) |
| `audio folders batch-set {--input <file> \| --stdin}` | Set tags on many audio folders in one transaction (gm or admin) |
| `genres list` | List the genre vocabulary (tiered via `parent_id`) |
| `genres create --name <name> [--parent-id <id>]` | Create a genre (admin) |
| `genres delete --id <id> [--force]` | Delete a genre and its children; no undo (admin) |
| `licenses list` | List the license vocabulary |
| `licenses create --name <name>` | Create a license (admin) |
| `licenses delete --id <id> [--force]` | Delete a license; no undo (admin) |
| `parent-systems list` | List the parent-system vocabulary (ships empty) |
| `parent-systems create --name <name>` | Create a parent system (admin) |
| `parent-systems delete --id <id> [--force]` | Delete a parent system; no undo (admin) |
| `system-families list` | List the system-family vocabulary |
| `system-families create --name <name>` | Create a system family (admin) |
| `system-families delete --id <id> [--force]` | Delete a system family; no undo (admin) |
| `dice-materials list` | List the dice/material vocabulary |
| `dice-materials create --name <name> [--group <group>]` | Create a dice/material (admin) |
| `dice-materials delete --id <id> [--force]` | Delete a dice/material; no undo (admin) |
| `search --query <q> [--limit <1-200>] [--book-id <id>] [--system-id <id>]` | Search page text and metadata across the library |
| `search fields` | The `field:` prefixes a search query accepts |
| `logs [--level <l>] [--limit <n>] [--offset <n>] [--after-seq <n>]` | Read the server's application log (admin) |
| `tags list [--in-use-by <type>]` | List tags with their usage counts |
| `tags items --tag <key> [--resource-type <type>]` | Items and folders carrying a tag |
| `tags create --value <value> [--display <text>]` | Create a tag up front; idempotent (gm or admin) |
| `tags rename --tag <key> --display <text>` | Rename a tag; the key follows and may merge (gm or admin) |
| `tags delete --tag <key>` | Delete a tag everywhere; no undo (gm or admin) |
| `tags merge --tag <key> --into <key>` | Merge one tag into another (gm or admin) |
| `duplicates link {--input <file> \| --stdin}` | File items under a parent as its variants; exit 3 if partial (admin) |
| `duplicates promote --resource-type <t> --new-parent-id <id> --old-parent-id <id> [--kind <k>] [--label <l>]` | Make a different copy the main version of a family (admin) |
| `duplicates unlink --resource-type <t> (--ids <id>... \| --parent-id <id>)` | Promote variants back to standalone entries (admin) |
| `duplicates merge-metadata --resource-type <t> --source-id <id> --target-id <id> --fields <f>... [--overwrite]` | Copy metadata fields from one copy onto another (admin) |
| `duplicates delete --resource-type <t> --id <id> --delete-file true\|false [--reparent-to <id>]` | Delete one duplicate's record, and optionally its file (admin) |
| `duplicates compare --resource-type <t> --ids <id>...` | Compare two to four copies side by side (admin) |
| `duplicates scan [--resource-types <t>...] [--accuracy exact\|high\|medium\|low]` | Start a duplicate-detection pass; exit 3 if already running (admin) |
| `duplicates scan-status` | Show the duplicate scan's progress (admin) |
| `duplicates cancel-scan` | Stop the running duplicate scan (admin) |
| `duplicates groups [--resource-type <t>] [--min-confidence <n>] [--limit <1-200>] [--offset <n>]` | List candidate duplicate groups from the last scan (admin) |
| `duplicates dismiss --resource-type <t> --member-ids <id>... [--note <text>]` | Mark a group as not duplicates (admin) |
| `duplicates dismissals [--resource-type <t>]` | List dismissed groups (admin) |
| `duplicates undismiss --id <id>` | Undo a dismissal (admin) |
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
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books docker/addon-index/index.json
```

The fixture copy is required before the first boot — Grimoire seeds its users from `/data/users.json` at startup, and without it the stack comes up with no users. Seeded logins are `admin/admin`, `gm/gm`, `player/player`; throwaway dev credentials for a throwaway stack. `docker/seed.sh` then populates the library with fixture books — `smoke-test.sh` asserts on that fixture set and fails without it.

From inside the dev container the daemon runs on the host, so reach the stack at `http://host.docker.internal:9481` rather than `localhost`, and set `GRIMOIRE_LIBRARY` / `GRIMOIRE_DATA` to host paths — see `docker/env.example`. `docker/seed.sh` writes fixtures itself rather than through the daemon, so it reads a third var, `GRIMOIRE_LIBRARY_LOCAL` — the same directory's *container*-side path, defaulting to `docker/library`.

### Project structure

```
src/GrimoireCli/
  Commands/       # CLI command definitions (System.CommandLine)
  Services/       # one per command group; wraps the generated client
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

Requires Grimoire **v1.7.0 or v1.7.1**. The CLI warns on login if the server reports anything else. See [docs/grimoire-compatibility.md](docs/grimoire-compatibility.md) for the version matrix and the bump procedure.

## License

[MIT](LICENSE)
