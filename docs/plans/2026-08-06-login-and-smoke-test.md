# Login and Smoke Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `grimoire-cli login` demonstrably work against a disposable local Grimoire, proven by a smoke test that runs on every pull request.

**Architecture:** `docker/docker-compose.yml` brings up Grimoire v1.5.4 with a host-bound `/data` directory holding a `users.json` fixture that Grimoire seeds at startup. `docker/smoke-test.sh` asserts against an already-running instance — it never starts or seeds the stack, so it behaves identically in CI and locally. A new `smoke-test` job in `build.yml` brings the stack up with `docker compose` (not `services:`, which starts before checkout and so cannot see the fixture) and runs the script against the published AOT binary.

**Tech Stack:** .NET 10, Native AOT, System.CommandLine, xunit.v3, bash, Docker Compose, GitHub Actions.

## Global Constraints

- Target Grimoire **v1.5.4** only. No bulk endpoints — `POST /api/{books,systems}/bulk` is unreleased upstream `main`.
- `OPDS_ENABLED=false` must stay set: with OPDS on, `GET /api/openapi.json` returns 500 (upstream #276).
- Run `dotnet format GrimoireCli.sln` after modifying any C# file. CI enforces `--verify-no-changes`.
- No blank lines inside method bodies between consecutive declarations, `AddCommand`/`AddOption` calls, or before a `return` that follows setup calls.
- Conventional Commits: `type: subject`, imperative, lowercase, no trailing period, ≤72 chars. No `Co-Authored-By`, no generated-with attribution.
- stdout is API JSON only; all logs and human-facing lines go to stderr.
- Branch is `feat/login-and-smoke-test`, already created. Never commit to `main`.
- `temp/` is gitignored reference material — never commit anything from it.

---

### Task 1: Commit the groundwork

The working tree already carries the spec, this plan, and four documentation/comment corrections made during design. CLAUDE.md requires spec and plan to land on the implementation branch together with code, so they go in first as their own commit.

**Files:**
- Create: `docs/specs/2026-08-06-login-and-smoke-test-design.md` (already written)
- Create: `docs/plans/2026-08-06-login-and-smoke-test.md` (already written)
- Modify: `CLAUDE.md` (already edited — pinned clone command, hand-populated `temp/`, four verified v1.5.4 API facts)
- Modify: `HANDOVER.md` (already edited — `temp/` no longer fetched on container create)
- Modify: `.devcontainer/post-create.sh` (already edited — reference-fetch block removed)
- Modify: `src/GrimoireCli/Api/ApiEndpoints.cs` (already edited — no committed spec snapshot)

- [ ] **Step 1: Confirm the tree is what the plan expects**

Run: `git status --short && git branch --show-current`
Expected: branch `feat/login-and-smoke-test`; modified `CLAUDE.md`, `HANDOVER.md`, `.devcontainer/post-create.sh`, `src/GrimoireCli/Api/ApiEndpoints.cs`; untracked `docs/`. Nothing from `temp/`.

- [ ] **Step 2: Verify build, format and tests are clean before committing**

Run: `dotnet format GrimoireCli.sln --verify-no-changes && dotnet build GrimoireCli.sln && dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: format silent, build succeeds with 0 warnings, test run exits 0 (zero tests is fine at this point).

- [ ] **Step 3: Commit the docs**

```bash
git add docs/specs/2026-08-06-login-and-smoke-test-design.md \
        docs/plans/2026-08-06-login-and-smoke-test.md
git commit -m "docs: add login and smoke-test design and plan"
```

- [ ] **Step 4: Commit the corrections separately**

```bash
git add CLAUDE.md HANDOVER.md .devcontainer/post-create.sh src/GrimoireCli/Api/ApiEndpoints.cs
git commit -m "docs: pin upstream reference to v1.5.4 and record patch semantics"
```

---

### Task 2: Unit tests for the existing helpers

The test project is wired for xunit.v3 and contains no tests. `GrimoireCli.csproj:22` already has `<InternalsVisibleTo Include="GrimoireCli.Tests" />`, so `internal` members are directly testable — no new wiring needed.

**Files:**
- Create: `tests/GrimoireCli.Tests/ExtractTokenTests.cs`
- Create: `tests/GrimoireCli.Tests/CompareVersionsTests.cs`
- Create: `tests/GrimoireCli.Tests/ReadPasswordFromStdinTests.cs`
- Create: `tests/GrimoireCli.Tests/ConfigManagerTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient.ExtractToken(string) → string?`, `GrimoireApiClient.CompareVersions(string, string) → int`, `LoginCommand.ReadPasswordFromStdin(TextReader) → string`, `ConfigManager.Resolve(string?, string?, Func<string,string?>?) → AppConfig` — all already implemented.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/ExtractTokenTests.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Tests;

public class ExtractTokenTests
{
    // Grimoire's login response is untyped in the spec; the key is "token",
    // but "access_token" is the FastAPI convention and is accepted too.
    [Theory]
    [InlineData("{\"token\":\"abc\"}", "abc")]
    [InlineData("{\"access_token\":\"abc\"}", "abc")]
    [InlineData("{\"accessToken\":\"abc\"}", "abc")]
    public void ReturnsTokenForEveryAcceptedSpelling(string body, string expected)
    {
        Assert.Equal(expected, GrimoireApiClient.ExtractToken(body));
    }

    [Theory]
    [InlineData("{\"nope\":1}")]
    [InlineData("{\"token\":42}")]
    [InlineData("not json")]
    [InlineData("")]
    public void ReturnsNullWhenNoStringTokenIsPresent(string body)
    {
        Assert.Null(GrimoireApiClient.ExtractToken(body));
    }

    [Fact]
    public void PrefersAccessTokenWhenBothArePresent()
    {
        Assert.Equal("first", GrimoireApiClient.ExtractToken("{\"access_token\":\"first\",\"token\":\"second\"}"));
    }
}
```

`tests/GrimoireCli.Tests/CompareVersionsTests.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Tests;

public class CompareVersionsTests
{
    [Fact]
    public void TreatsEqualVersionsAsEqual()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("1.5.4", "1.5.4"));
    }

    [Fact]
    public void OrdersNewerVersionsAbove()
    {
        Assert.True(GrimoireApiClient.CompareVersions("1.6.0", "1.5.4") > 0);
    }

    [Fact]
    public void ToleratesLeadingV()
    {
        Assert.True(GrimoireApiClient.CompareVersions("v1.5.3", "1.5.4") < 0);
    }

    [Fact]
    public void IgnoresPreReleaseSuffix()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("1.5.4-rc1", "1.5.4"));
    }

    [Fact]
    public void TreatsMissingSegmentsAsZero()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("1.5", "1.5.0"));
    }

    // An unparseable version must not throw — it would take down a working command.
    [Fact]
    public void TreatsUnparseableSegmentsAsZero()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("dev", "0.0.0"));
    }
}
```

`tests/GrimoireCli.Tests/ReadPasswordFromStdinTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests;

public class ReadPasswordFromStdinTests
{
    [Fact]
    public void ReadsTheFirstLine()
    {
        Assert.Equal("secret", LoginCommand.ReadPasswordFromStdin(new StringReader("secret\nignored\n")));
    }

    [Fact]
    public void StripsTheTrailingNewline()
    {
        Assert.Equal("secret", LoginCommand.ReadPasswordFromStdin(new StringReader("secret\n")));
    }

    [Fact]
    public void StripsACarriageReturnNewlinePair()
    {
        Assert.Equal("secret", LoginCommand.ReadPasswordFromStdin(new StringReader("secret\r\n")));
    }

    [Fact]
    public void ReturnsEmptyForEmptyStdin()
    {
        Assert.Equal("", LoginCommand.ReadPasswordFromStdin(new StringReader("")));
    }

    [Fact]
    public void PreservesSpacesInsideThePassword()
    {
        Assert.Equal("two words", LoginCommand.ReadPasswordFromStdin(new StringReader("two words\n")));
    }
}
```

`tests/GrimoireCli.Tests/ConfigManagerTests.cs`:

```csharp
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests;

public class ConfigManagerTests
{
    private static ConfigManager InTempDir(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        return new ConfigManager(path);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://example.invalid", AccessToken = "tok" });
            var loaded = manager.Load();
            Assert.Equal("https://example.invalid", loaded.Server);
            Assert.Equal("tok", loaded.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void LoadReturnsEmptyConfigWhenFileIsAbsent()
    {
        var manager = new ConfigManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json"));
        var loaded = manager.Load();
        Assert.Null(loaded.Server);
        Assert.Null(loaded.AccessToken);
    }

    [Fact]
    public void ResolvePrefersFlagOverEnvAndFile()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://file.invalid", AccessToken = "file-token" });
            var resolved = manager.Resolve(
                flagServer: "https://flag.invalid",
                flagToken: "flag-token",
                envLookup: key => key == "GRIMOIRE_SERVER" ? "https://env.invalid" : "env-token");
            Assert.Equal("https://flag.invalid", resolved.Server);
            Assert.Equal("flag-token", resolved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ResolvePrefersEnvOverFile()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://file.invalid", AccessToken = "file-token" });
            var resolved = manager.Resolve(
                envLookup: key => key == "GRIMOIRE_SERVER" ? "https://env.invalid" : "env-token");
            Assert.Equal("https://env.invalid", resolved.Server);
            Assert.Equal("env-token", resolved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ResolveFallsBackToTheFile()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "https://file.invalid", AccessToken = "file-token" });
            var resolved = manager.Resolve(envLookup: _ => null);
            Assert.Equal("https://file.invalid", resolved.Server);
            Assert.Equal("file-token", resolved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests to see which fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`

Expected: most pass immediately (they cover already-working code). Two are genuine probes and may fail — if so, **the test is describing intent that the code does not implement**, so stop and report rather than editing the test to match the code:

- `PrefersAccessTokenWhenBothArePresent` — passes only if `ExtractToken` tries `access_token` first, which `GrimoireApiClient.cs:63` does.
- `StripsACarriageReturnNewlinePair` — `TextReader.ReadLine` strips `\r\n`, so this should pass. If it fails on a `\r` remnant, that is a real bug in `ReadPasswordFromStdin` worth reporting.

- [ ] **Step 3: Format**

Run: `dotnet format GrimoireCli.sln`

- [ ] **Step 4: Verify the full suite and the format gate**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj && dotnet format GrimoireCli.sln --verify-no-changes`
Expected: all tests pass, format silent.

- [ ] **Step 5: Commit**

```bash
git add tests/GrimoireCli.Tests
git commit -m "test: cover token extraction, version compare, stdin password and config resolution"
```

---

### Task 3: Local stack with a seeded admin

Grimoire seeds users from `{DATA_PATH}/users.json` on first startup, so `/data` becomes a host-bound directory holding the fixture. It then renames the file to `users.json.imported`; that rename is unguarded and `seed_users` is called with no `except` (`backend/main.py:135`), so `users.json` must live **inside** a mounted directory and never be mounted as a single file.

**Files:**
- Modify: `docker/docker-compose.yml`
- Create: `docker/users.json.example`
- Create: `docker/.env.example`
- Modify: `.gitignore`

**Interfaces:**
- Produces: a healthy Grimoire on port 9481 with users `admin`, `gm`, `player` (all password = username), reachable at `http://host.docker.internal:9481` from the devcontainer and `http://localhost:9481` on a CI runner. Task 4 and Task 5 depend on this.

- [ ] **Step 1: Write the fixture**

`docker/users.json.example`:

```json
[
  { "username": "admin", "password": "admin", "role": "admin" },
  { "username": "gm", "password": "gm", "role": "gm" },
  { "username": "player", "password": "player", "role": "player" }
]
```

Exactly that content, with no comments: Grimoire parses the file with `json.loads`, which rejects `//`. The explanation of what these credentials are for lives in the compose header (Step 4) instead.

- [ ] **Step 2: Update `.gitignore`**

Append:

```gitignore
# Local stack state. /data holds the Grimoire database plus the seeded
# users.json, which Grimoire renames to users.json.imported on first boot.
docker/data/
docker/.env
```

- [ ] **Step 3: Write `docker/.env.example`**

```bash
# Copy to docker/.env (gitignored). Both paths are resolved by the Docker daemon,
# which under docker-outside-of-docker runs on the HOST — so these must be paths
# as the host sees them, not container paths.
GRIMOIRE_LIBRARY=/Users/you/Development/grimoire-cli/docker/library
GRIMOIRE_DATA=/Users/you/Development/grimoire-cli/docker/data
```

- [ ] **Step 4: Modify `docker/docker-compose.yml`**

Replace the `volumes:` block and the named-volume declaration, and add the two new settings. The file becomes:

```yaml
# Disposable Grimoire instance for local development.
#
#   cp docker/users.json.example docker/data/users.json   # before the first start
#   cd docker && docker compose up -d --wait
#   curl -sf http://localhost:9481/api/health
#
# From inside the devcontainer the published port lives on the host, so reach it
# at http://host.docker.internal:9481 rather than localhost.
#
# First-run users come from /data/users.json, which Grimoire seeds at startup and
# then renames to users.json.imported (backend/seed_users.py). That rename is
# unguarded and startup has no except around it, so users.json must sit INSIDE a
# mounted directory — bind-mounting it as a single file makes the rename fail and
# the container will not start. Fixture logins are admin/admin, gm/gm,
# player/player: throwaway credentials for a throwaway stack with a fixed dev
# SECRET_KEY, never for a real deployment.
#
# Reset the instance:  docker compose down && rm -rf data
#
# The library is a bind mount so fixtures can be dropped in and picked up with
# POST /api/rescan. Grimoire mounts it read-only: it has no upload API and
# never writes to the library.
#
# IMPORTANT under docker-outside-of-docker: the daemon runs on the HOST, so bind
# mount paths resolve against the HOST filesystem, not the container's.
# `./library` and `./data` therefore do not work from inside the devcontainer —
# set GRIMOIRE_LIBRARY and GRIMOIRE_DATA to the repo's path as the host sees it.
# Put them in docker/.env (gitignored); see docker/.env.example.
services:
  grimoire:
    image: hunterreadca/grimoire:latest
    ports:
      - "9481:9481"
    environment:
      # Disposable stack — a fixed key keeps sessions valid across restarts.
      - SECRET_KEY=dev-only-not-a-real-secret
      - LIBRARY_PATH=/library
      - DATA_PATH=/data
      - WORKERS=1
      - TZ=Europe/Berlin
      - LOG_LEVEL=debug
      # OCR off: this stack is for exercising the API, not for indexing scans.
      - OCR_ENABLED=false
      # OPDS must stay off — with it on, GET /api/openapi.json returns 500
      # (hunter-read/grimoire#276), and that spec is what the CLI is built from.
      - OPDS_ENABLED=false
      # The smoke test logs in several times; the default is 10/minute.
      - RATE_LIMIT_ENABLED=false
    volumes:
      - ${GRIMOIRE_LIBRARY:-./library}:/library:ro
      - ${GRIMOIRE_DATA:-./data}:/data
    # The image already healthchecks /api/health, but with interval=30s and
    # start_period=30s, which makes `up -d --wait` sit for up to a minute.
    # Poll faster so CI and local starts are quick.
    healthcheck:
      test: ["CMD", "python", "-c", "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:9481/api/health', timeout=4).status == 200 else 1)"]
      interval: 5s
      timeout: 5s
      start_period: 10s
      retries: 12
```

Note what is removed: the `volumes: grimoire-data:` top-level block and the `grimoire-data:/data` mount. Reset is now `rm -rf docker/data` instead of a volume prune.

- [ ] **Step 5: Bring the stack up and verify seeding**

The devcontainer needs the host path, so pass it explicitly. Substitute the host path this repo is bind-mounted from:

```bash
mkdir -p docker/data
cp docker/users.json.example docker/data/users.json
GRIMOIRE_LIBRARY=/path/to/grimoire-cli/docker/library \
GRIMOIRE_DATA=/path/to/grimoire-cli/docker/data \
  docker compose -f docker/docker-compose.yml up -d --wait
```

Expected: the command returns with the container healthy, within ~15s.

- [ ] **Step 6: Prove the fixture seeded and that login works over HTTP**

```bash
docker compose -f docker/docker-compose.yml logs | grep -i "Seeded user"
ls docker/data/                     # users.json.imported present, users.json gone
curl -sf http://host.docker.internal:9481/api/auth/login \
  -H 'content-type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq -e '.token != null'
```

Expected: three `Seeded user '...' (role=...)` lines; `users.json.imported` in `docker/data/`; the curl prints `true`.

If the rename failed and the container is unhealthy, the fixture was mounted as a file rather than sitting in a mounted directory — re-check Step 4's `volumes:` block.

- [ ] **Step 7: Commit**

```bash
git add docker/docker-compose.yml docker/users.json.example docker/.env.example .gitignore
git commit -m "feat: seed a local admin and bind /data for the dev stack"
```

---

### Task 4: The smoke test script

**Files:**
- Create: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: a running instance from Task 3.
- Produces: `docker/smoke-test.sh`, reading `GRIMOIRE_SERVER` (default `http://host.docker.internal:9481`) and `CLI` (default the Debug build path). Task 5 invokes it with both set.

- [ ] **Step 1: Write the script**

`docker/smoke-test.sh`:

```bash
#!/usr/bin/env bash
# Smoke test: exercises a built grimoire-cli binary against a running Grimoire.
#
#   bash docker/smoke-test.sh
#   GRIMOIRE_SERVER=http://localhost:9481 CLI=./publish/grimoire-cli bash docker/smoke-test.sh
#
# It does NOT start, seed or reset the stack — bring it up first (see
# docker/docker-compose.yml). That keeps the script identical in CI and locally.
set -euo pipefail

SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
CLI="${CLI:-src/GrimoireCli/bin/Debug/net10.0/grimoire-cli}"
CONFIG="$HOME/.grimoire-cli/config.json"

# Deliberately NOT exporting GRIMOIRE_SERVER: `systems list` must resolve the
# server from the config file that `login` wrote. With it in the environment,
# a login that failed to persist anything would still pass this test.

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }
ok() { echo "  ok: $*" >&2; }

echo "smoke: $CLI against $SERVER" >&2
[ -x "$CLI" ] || fail "no executable CLI at $CLI"

# 1. The instance answers at all.
for i in $(seq 1 60); do
  curl -sf "$SERVER/api/health" >/dev/null 2>&1 && break
  [ "$i" -eq 60 ] && fail "no response from $SERVER/api/health after 60s"
  sleep 1
done
ok "health"

# 2. Login. Retried: the healthcheck can go green before user seeding commits,
# so a first-attempt 401 is a race, not a failure.
for i in $(seq 1 30); do
  if printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
      >"$WORK/login.out" 2>"$WORK/login.err"; then
    break
  fi
  if [ "$i" -eq 30 ]; then
    cat "$WORK/login.err" >&2
    fail "login never succeeded"
  fi
  sleep 1
done
ok "login exited 0"

# 3. The token and server were persisted.
[ -f "$CONFIG" ] || fail "no config written at $CONFIG"
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "config server is not $SERVER: $(cat "$CONFIG")"
jq -e '.accessToken | type == "string" and length > 0' "$CONFIG" >/dev/null \
  || fail "config holds no access token: $(cat "$CONFIG")"
ok "config has server and token"

# 4. The token authenticates, and stdout is JSON with logs kept on stderr.
"$CLI" systems list >"$WORK/list.out" 2>"$WORK/list.err" \
  || { cat "$WORK/list.err" >&2; fail "systems list exited non-zero"; }
jq -e . "$WORK/list.out" >/dev/null \
  || fail "systems list stdout was not valid JSON: $(cat "$WORK/list.out")"
ok "systems list returned JSON on stdout"

# 5. A bad password fails cleanly and leaves the config alone.
cp "$CONFIG" "$WORK/config.before"
set +e
printf 'definitely-wrong' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
  >"$WORK/bad.out" 2>"$WORK/bad.err"
rc=$?
set -e
[ "$rc" -eq 2 ] || fail "bad password exited $rc, expected 2"
grep -qi "login failed" "$WORK/bad.err" \
  || fail "bad password produced no 'Login failed' message: $(cat "$WORK/bad.err")"
cmp -s "$CONFIG" "$WORK/config.before" \
  || fail "a failed login modified $CONFIG"
ok "bad password exits 2 and leaves the config untouched"

# 6. The binary's own offline integrity check.
"$CLI" self-test >"$WORK/self.out" 2>"$WORK/self.err" \
  || { cat "$WORK/self.err" >&2; fail "self-test exited non-zero"; }
ok "self-test"

echo "smoke: all checks passed" >&2
```

- [ ] **Step 2: Make it executable and run it**

```bash
chmod +x docker/smoke-test.sh
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: six `ok:` lines and `smoke: all checks passed`, exit 0. The stack from Task 3 must still be up.

- [ ] **Step 3: Prove the script actually fails when it should**

A smoke test that cannot fail is worthless. Confirm two failure paths:

```bash
CLI=/bin/false bash docker/smoke-test.sh; echo "exit=$? (expect non-zero)"
GRIMOIRE_SERVER=http://127.0.0.1:1 bash docker/smoke-test.sh; echo "exit=$? (expect non-zero)"
```

Expected: the first fails at the executable check or at login; the second fails the health wait after 60s. Both exit non-zero with a `SMOKE FAIL:` line.

- [ ] **Step 4: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: add smoke test covering login, token persistence and json output"
```

---

### Task 5: The CI smoke-test job

**Files:**
- Modify: `.github/workflows/build.yml`

**Interfaces:**
- Consumes: `docker/docker-compose.yml`, `docker/users.json.example` (Task 3), `docker/smoke-test.sh` (Task 4).

- [ ] **Step 1: Add the job**

Insert between the `unit-test` and `build` jobs in `.github/workflows/build.yml`:

```yaml
  smoke-test:
    needs: unit-test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '10.0.x'
      - name: Build AOT binary
        run: >
          dotnet publish src/GrimoireCli/GrimoireCli.csproj
          -c Release -r linux-x64 --self-contained true /p:PublishAot=true
          -o ./publish
      # Grimoire seeds its first users from /data/users.json at startup, so the
      # fixture has to be in place before the container boots. A `services:`
      # container starts before actions/checkout runs and could never see it,
      # which is why the stack comes up here instead.
      - name: Start Grimoire
        run: |
          mkdir -p docker/data
          cp docker/users.json.example docker/data/users.json
          docker compose -f docker/docker-compose.yml up -d --wait
      - name: Run smoke test against the AOT binary
        run: bash docker/smoke-test.sh
        env:
          GRIMOIRE_SERVER: http://localhost:9481
          CLI: ./publish/grimoire-cli
      - name: Grimoire logs
        if: failure()
        run: docker compose -f docker/docker-compose.yml logs
```

Note: no `GRIMOIRE_LIBRARY`/`GRIMOIRE_DATA` are set — on a CI runner the daemon is local, so the compose defaults (`./library`, `./data`, relative to the compose file) resolve correctly. `docker/library/` must exist in the repo for the read-only mount to bind; if it is empty and therefore untracked by git, add `docker/library/.gitkeep` in this step's commit.

- [ ] **Step 2: Check whether `docker/library/` survives checkout**

Run: `git ls-files docker/library | head`
Expected: if this prints nothing, the directory does not exist after a fresh clone and the compose mount will fail in CI. In that case:

```bash
touch docker/library/.gitkeep
git add docker/library/.gitkeep
```

- [ ] **Step 3: Validate the workflow YAML**

Run: `python3 -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/build.yml')); print(list(d['jobs'].keys()))"`
Expected: `['unit-test', 'smoke-test', 'build']`

- [ ] **Step 4: Rehearse the CI path locally**

CI uses the compose defaults rather than host paths, which cannot be reproduced inside the devcontainer — but the command sequence can be checked for typos against the real files:

```bash
bash -n docker/smoke-test.sh && echo "smoke-test.sh parses"
docker compose -f docker/docker-compose.yml config >/dev/null && echo "compose config valid"
```

Expected: both print their confirmation. Real verification happens when the pull request runs.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/build.yml docker/library/.gitkeep
git commit -m "ci: run the smoke test against a seeded local grimoire"
```

---

### Task 6: Final verification and handover

- [ ] **Step 1: Full local gate**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```
Expected: all four succeed. Report the actual output; do not claim success without it.

- [ ] **Step 2: Confirm a clean reset works**

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
GRIMOIRE_LIBRARY=/path/to/grimoire-cli/docker/library \
GRIMOIRE_DATA=/path/to/grimoire-cli/docker/data \
  docker compose -f docker/docker-compose.yml up -d --wait
bash docker/smoke-test.sh
```
Expected: the stack reseeds from scratch and the smoke test passes again. This proves the fixture flow is repeatable rather than depending on leftover state.

- [ ] **Step 3: Update HANDOVER.md**

Replace the "Next steps" ordering so the next session sees what is done. Mark the smoke test and local stack complete, note that `docker/seed.sh` and library fixtures are still unwritten, and record that the metadata command surface is designed only as far as the parked discussion in this session — the flags-plus-`--json` question is still open.

- [ ] **Step 4: Commit and report**

```bash
git add HANDOVER.md
git commit -m "docs: record login and smoke-test completion in handover"
git log --oneline main..HEAD
```

Then stop. Ask before pushing or opening a pull request — CLAUDE.md requires it, and this branch has not been pushed.

---

## Notes for the implementer

- **Do not add a `setup` command, a `--config` flag or a `GRIMOIRE_CONFIG` env var.** All three were considered and rejected during design; the reasons are in the spec.
- **Do not write `docker/seed.sh` or library fixtures.** Login needs no content, and the seed script is the next increment, not this one.
- **The live server at `$GRIMOIRE_SERVER` is not a test target.** Everything here runs against the local stack. The live instance's one system has deliberately empty `parent_system`/`edition`/`system_family` fields, kept as a fixture for the metadata work that comes later — do not PATCH it.
- If a step's expected output does not match, stop and report rather than adapting the assertion to whatever the code happens to do.
