# Testing

## Principle

Always test the compiled AOT binary, not JIT-mode `dotnet run`. If `dotnet run`
works but the AOT binary crashes, you've shipped a broken release. AOT disables
reflection-based serialization — bugs only surface in the real binary.

## Three Test Layers

### 1. Unit Tests (xUnit v3)

51 tests covering pure logic, help-output assertions, and JSON-shape drift
guards with no network or binary dependency:

- `Api/` — `ApiEndpointsTests`, `CompareVersionsTests`, `ExtractTokenTests`,
  `QueryBuilderTests`
- `Commands/` — `ReadPasswordFromStdinTests`, `ResponseExamplesDriftTest`,
  `ResponseExamplesJsonValidTest`, `SystemsCommandTests`
- `Configuration/` — `ConfigManagerTests`
- `Models/` — `GameSystemDtoTests`

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Two of these are generated-artifact guards rather than ordinary logic tests:

- `ResponseExamplesDriftTest` reruns `tools/GenerateResponseExamples` into a
  temp file and diffs it byte-for-byte against the checked-in
  `src/GrimoireCli/Commands/ResponseExamples.g.cs`. A stale generated file
  fails CI instead of silently drifting from its source.
- `ResponseExamplesJsonValidTest` parses every sample in `ResponseExamples.All`
  as JSON, catching a hand-edited example that no longer round-trips.

### 2. Self-Test (built-in command)

Offline integrity check exercising the AOT-sensitive code paths without
network access — source-generated JSON round-trips for `AppConfig`,
`LoginRequest` and `Dictionary<string,string>`, JWT expiry parsing, version
comparison, informational-version resolution, and login-response token
extraction:

```bash
grimoire-cli self-test
```

This exists because Native AOT trims reflection-based `System.Text.Json`: a
missing `[JsonSerializable]` registration on a DTO compiles cleanly and passes
every test in Debug (which still runs on the JIT), then fails only in the
published, trimmed binary. `self-test` is the check that runs against the
actual published artifact, on every RID, in CI — see the `build` job below.

### 3. Smoke Tests (bash, against live Grimoire)

16 assertions running the AOT binary against a real Grimoire Docker instance
seeded with 9 systems and 15 books:

- Health check, login, config persistence (server + token written to
  `~/.grimoire-cli/config.json`)
- `systems list` returns valid JSON on stdout with logs on stderr
- A bad password fails with exit 2, a `401`-mentioning message, and leaves
  the config file untouched
- The binary's own `self-test` passes
- `systems list` filters (`--genre`, `--edition`, `--license`, `--family`,
  `--explicit`, `--parent-system` with an `&` in the value) narrow the
  fixture set to the expected counts
- Sort ordering, including `--desc`, and rejection of an invalid `--sort`
  value before any request is made
- `systems get` filters embedded books by category and genre and recomputes
  `book_count` from the filtered set
- A missing system id exits 2 with a "not found" hint

```bash
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
CLI=./path/to/grimoire-cli bash docker/smoke-test.sh
```

**The smoke test requires `docker/seed.sh` to have already run.** Unlike
abs-cli's smoke test, `smoke-test.sh` itself is idempotent — it only reads
and logs in, so rerunning it alone is safe. But it asserts on exact fixture
counts (9 systems, 15 books, specific filter matches), so skipping the seed
step fails nearly every check below the login line, not just the ones that
obviously depend on data.

## Fixture Generation

`docker/seed.sh` builds the fixture library itself rather than shipping
static files: it writes real PDFs via `docker/make-fixtures.py`, which uses
PyMuPDF (`python3-fitz`) — the same library Grimoire's indexer uses to read
PDFs — so every fixture is guaranteed parseable rather than hoped to be.
`python3-fitz` is preinstalled in the dev container and installed by CI
before the smoke-test job builds the stack.

After writing the PDFs, `seed.sh` triggers `POST /api/rescan`, waits for the
scan to finish, then `PATCH`es the metadata that folder structure cannot
express (edition, family, parent system, genre, license, year, publishers).
Shadowrun 4 DE is deliberately left unpatched — a raw, freshly-scanned system
is itself a fixture, for the metadata commands not yet built.

**Renaming or re-marking a fixture folder needs a full reset, not just a
re-seed.** `rescan` only ever sets `is_explicit=true` on a system row and
never clears it — a one-way latch in Grimoire's scanner
(`backend/indexer/scan.py` in `temp/grimoire` @ v1.5.5). Dropping the
`(nsfw)` marker from a folder name and re-running `seed.sh` leaves the stale
flag in place; only a database reset picks up the change. The library tree
must also go, not just the database: the boot scan indexes whatever is on
disk, so a stale folder survives as an `is_missing` row that still counts
toward `book_count`.

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

## Local Dev

- `docker compose -f docker/docker-compose.yml up -d --wait` starts Grimoire
  (stays running)
- Seed once on first setup or after a database reset
- Works from inside the dev container (Docker-outside-of-Docker)

Under Docker-outside-of-Docker the daemon runs on the host, not the
container, which affects three variables differently:

- `GRIMOIRE_LIBRARY` / `GRIMOIRE_DATA` (compose bind-mount sources) must be
  **host** paths — the container path (`./library`, `./data`) doesn't
  resolve, because the daemon that interprets it runs outside the devcontainer.
- The stack is reachable at `http://host.docker.internal:9481`, not
  `localhost`.
- `GRIMOIRE_LIBRARY_LOCAL` is different again: `seed.sh` writes fixture PDFs
  directly with Python, not through the Docker daemon, so it needs the
  **devcontainer-side** path to the same directory (default `docker/library`).
  Point `GRIMOIRE_LIBRARY` at a library outside the repo without also
  setting `GRIMOIRE_LIBRARY_LOCAL`, and fixtures land in `docker/library`
  while the server scans an empty tree.

See `docker/.env.example` for all three.

## CI Pipeline

Three jobs, defined in `.github/workflows/build.yml`:

| Job | What | Platforms |
|-----|------|-----------|
| unit-test | `dotnet format --verify-no-changes` + 51 unit tests | ubuntu-latest |
| smoke-test | installs `python3-fitz`, publishes the AOT `linux-x64` binary, starts the stack, seeds it, runs `smoke-test.sh` (16 assertions) | ubuntu-latest only (needs Docker) |
| build | AOT publish + `self-test` per RID | linux-x64, linux-arm64, osx-arm64, osx-x64, win-x64, win-arm64 |

The smoke test is Linux-only because it needs a Docker Grimoire container.
`self-test` runs on all six platforms to validate AOT integrity everywhere a
binary actually ships, not just the one CI happens to build on.

## Deviations from abs-cli

- **The smoke test is idempotent.** abs-cli's mutates server state on every
  run (creates/updates/deletes items); grimoire-cli's only reads and logs
  in, because the library mount is read-only and the metadata-writing
  commands don't exist yet. Once `systems`/`books` `PATCH` commands land,
  expect this to change.
- **Fixtures are generated, not fixed files.** abs-cli seeds from checked-in
  audio fixtures; grimoire-cli generates PDFs on the fly via
  `docker/make-fixtures.py` so the tooling and the target format are
  verifiably compatible.
- **No `Output` / `Services` test areas yet.** abs-cli's tests are grouped
  into `Api` / `Commands` / `Configuration` / `Output` / `Services`.
  grimoire-cli's add a `Models/` area instead (response DTOs are a distinct
  surface here — see `CLAUDE.md`) and have not yet grown code under
  `Output` or `Services` worth a dedicated test folder.
