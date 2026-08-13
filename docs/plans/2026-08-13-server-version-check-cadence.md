# Server Version Check Cadence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Check the server version once every 24 hours from any command, instead of only inside `login`.

**Architecture:** Every request already funnels through `GrimoireApiClient.SendAsync(RequestInformation, …)`, which becomes a `PreflightAsync()` that keeps the per-request token warning and adds a once-per-process version check. The check reads `lastVersionCheck` from the config, probes `GET /api/about` on a 3-second budget when it is stale, warns via a pure function, and records the result by a read-modify-write of the on-disk config.

**Tech Stack:** C# / .NET 10, System.CommandLine, source-generated `System.Text.Json`, Kiota-generated request builders, Native AOT, xUnit, bash.

**Design spec:** [`docs/specs/2026-08-13-server-version-check-cadence-design.md`](../specs/2026-08-13-server-version-check-cadence-design.md). Read it first — it records why the probe is authenticated and why the persistence path is deliberately awkward.

## Global Constraints

- **Branch is `feat/version-check-cadence`**, already created off `main` at `aee3ad8`. Spec, plan and code land on it together; it reaches `main` through one PR.
- **The build carries zero warnings today. Keep it there.**
- **Warn-only.** The check must never block, refuse, or change an exit code. A failed probe is a Debug line, never a command failure.
- **The probe must not route through `EnsureSuccessAsync`**, which maps non-2xx to `Environment.Exit(2)`. A diagnostic may not take down the command it precedes.
- **Never persist the resolved config.** `Resolve()` merges `GRIMOIRE_SERVER` / `GRIMOIRE_TOKEN` from the environment, so writing it would put a token on disk that the operator kept out of it. Version state is written by read-modify-write of `Load()`.
- Run `dotnet format GrimoireCli.sln` after modifying any hand-written C# file. No blank lines between consecutive option declarations or consecutive `Add*` calls.
- Never hand-edit `src/GrimoireCli/Generated/`. Build requests from the generated builders.
- **Any test that makes production code log joins `[Collection("NLog")]`** — NLog's configuration is process-global and `tests/GrimoireCli.Tests/NLogCollection.cs` exists for this.
- Conventional Commits: `type: subject`, imperative, lowercase, no trailing period, max ~72 chars. **No `Co-Authored-By:` and no AI attribution.**
- `CHANGELOG.md` is never touched on a feature branch.
- README Commands table is unchanged — no verb or flag is added.
- The docker stack is up and seeded (`admin/admin`); run `bash docker/smoke-test.sh` as-is.

## Verified facts this plan depends on

Measured against the pinned 1.5.6 stack on 2026-08-13. Do not re-derive; do notice if one stops holding.

| fact | value |
|---|---|
| `GET /api/about` authenticated | 103 bytes, 25 ms, `{version, commit_hash, python_version}` |
| `GET /api/about` unauthenticated | **401** (deliberate upstream; `backend/tests/test_library.py:111`) |
| `GET /api/openapi.json` | unauthenticated, 252 KB — rejected as the probe |
| `GET /api/health` | unauthenticated, carries **no** version |
| `ConfigManager.Resolve` | merges `GRIMOIRE_SERVER` (`:53`) and `GRIMOIRE_TOKEN` (`:56`) |
| `LoginCommand` | already builds its authed client from `configManager.Load()` (`:94`, `:115`), so on-disk version state reaches it |
| existing helpers | `ReadStringProperty(json, "version")`, `CompareVersions`, `ClientVersion`, `WarnIfTokenExpired` all exist on `GrimoireApiClient` |

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/GrimoireCli/Configuration/AppConfig.cs` | The two state fields | 1 |
| `src/GrimoireCli/Configuration/ConfigManager.cs` | Carry them through `Resolve`; `UpdateVersionCheck` | 1 |
| `src/GrimoireCli/Commands/ConfigCommand.cs` | Surface them in `config get` | 1 |
| `src/GrimoireCli/Api/GrimoireApiClient.cs` | `ShouldCheckVersion`, `VersionWarning`, preflight, probe, recorder | 2, 3 |
| `src/GrimoireCli/Commands/LoginCommand.cs` | Use the shared probe path | 3 |
| `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs` | Resolve + UpdateVersionCheck | 1 |
| `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs` | **Create.** Staleness and message wording | 2 |
| `docker/smoke-test.sh` | Cadence assertions against the live stack | 4 |
| `docs/grimoire-compatibility.md`, `docs/configuration.md` | Record the new behaviour | 4 |

---

## Task 1: Persist the version-check state

**Files:**
- Modify: `src/GrimoireCli/Configuration/AppConfig.cs`, `ConfigManager.cs`, `src/GrimoireCli/Commands/ConfigCommand.cs`
- Test: `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs`

**Interfaces:**
- Produces: `AppConfig.LastVersionCheck` (`DateTimeOffset?`, key `lastVersionCheck`), `AppConfig.LastServerVersion` (`string?`, key `lastServerVersion`), `ConfigManager.UpdateVersionCheck(string? serverVersion, DateTimeOffset checkedAt)`.
- Consumed by: Task 3's recorder.

- [ ] **Step 1: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs`. Follow the file's existing pattern for creating a temp config path.

```csharp
    // Resolve must carry these through. If it drops them, LastVersionCheck arrives
    // null on every run and the CLI probes on every single invocation — the whole
    // point of the cadence, silently defeated, with no other test failing.
    [Fact]
    public void ResolveCarriesTheVersionCheckState()
    {
        var path = TempConfigPath();
        try
        {
            var checkedAt = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
            var manager = new ConfigManager(path);
            manager.Save(new AppConfig
            {
                Server = "http://example.test",
                LastServerVersion = "1.5.6",
                LastVersionCheck = checkedAt,
            });
            var resolved = manager.Resolve(envLookup: _ => null);
            Assert.Equal("1.5.6", resolved.LastServerVersion);
            Assert.Equal(checkedAt, resolved.LastVersionCheck);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdateVersionCheckPreservesUnrelatedFields()
    {
        var path = TempConfigPath();
        try
        {
            var manager = new ConfigManager(path);
            manager.Save(new AppConfig { Server = "http://example.test", AccessToken = "on-disk-token" });
            var checkedAt = new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
            manager.UpdateVersionCheck("1.5.6", checkedAt);
            var reloaded = manager.Load();
            Assert.Equal("http://example.test", reloaded.Server);
            Assert.Equal("on-disk-token", reloaded.AccessToken);
            Assert.Equal("1.5.6", reloaded.LastServerVersion);
            Assert.Equal(checkedAt, reloaded.LastVersionCheck);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The hazard this design exists to avoid: Resolve merges env vars into memory,
    // so persisting the resolved config would write a token the operator kept out
    // of the file. UpdateVersionCheck reads the file, not the resolved config.
    [Fact]
    public void UpdateVersionCheckDoesNotPersistEnvironmentValues()
    {
        var path = TempConfigPath();
        try
        {
            var manager = new ConfigManager(path);
            manager.Save(new AppConfig { Server = "http://example.test" });
            manager.Resolve(envLookup: name => name == "GRIMOIRE_TOKEN" ? "env-only-token" : null);
            manager.UpdateVersionCheck("1.5.6", DateTimeOffset.UtcNow);
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("env-only-token", raw);
            Assert.Null(manager.Load().AccessToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A first check on a machine with no config file creates one holding only these
    // two fields. Accepted deliberately: no secrets, and it stops a re-probe on
    // every invocation.
    [Fact]
    public void UpdateVersionCheckCreatesAConfigWhenNoneExists()
    {
        var path = TempConfigPath();
        File.Delete(path);
        try
        {
            new ConfigManager(path).UpdateVersionCheck("1.5.6", DateTimeOffset.UtcNow);
            var written = new ConfigManager(path).Load();
            Assert.Equal("1.5.6", written.LastServerVersion);
            Assert.NotNull(written.LastVersionCheck);
            Assert.Null(written.Server);
            Assert.Null(written.AccessToken);
        }
        finally
        {
            File.Delete(path);
        }
    }
```

If the existing file has no `TempConfigPath()` helper, use whatever it already does to get a throwaway path and keep the tests consistent with it rather than introducing a second style. If `Resolve`'s signature does not take a named `envLookup` parameter, match the real one.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ConfigManagerTests`
Expected: compile errors — the fields and `UpdateVersionCheck` do not exist.

- [ ] **Step 3: Add the fields**

In `src/GrimoireCli/Configuration/AppConfig.cs`:

```csharp
    // Written by the CLI, not by the operator: when the server version was last
    // checked, and what it was. config set does not accept either.
    [JsonPropertyName("lastVersionCheck")]
    public DateTimeOffset? LastVersionCheck { get; set; }

    [JsonPropertyName("lastServerVersion")]
    public string? LastServerVersion { get; set; }
```

- [ ] **Step 4: Carry them through `Resolve` and add the writer**

In `ConfigManager.Resolve`, copy both fields from the loaded config onto the resolved one, beside the existing assignments. They have no flag or environment override — the file is their only source.

Then add:

```csharp
    /// <summary>
    /// Records a version observation by read-modify-write of the config file.
    /// Deliberately reads <see cref="Load"/> rather than a resolved config:
    /// <see cref="Resolve"/> merges GRIMOIRE_SERVER and GRIMOIRE_TOKEN from the
    /// environment, and persisting those would write a token to disk that the
    /// operator chose to keep out of it.
    /// </summary>
    public void UpdateVersionCheck(string? serverVersion, DateTimeOffset checkedAt)
    {
        var onDisk = Load();
        onDisk.LastServerVersion = serverVersion;
        onDisk.LastVersionCheck = checkedAt;
        Save(onDisk);
    }
```

- [ ] **Step 5: Surface them in `config get`**

In `ConfigCommand.cs`'s display dictionary, after `accessToken`:

```csharp
                ["lastVersionCheck"] = config.LastVersionCheck?.ToString("u") ?? "(never)",
                ["lastServerVersion"] = config.LastServerVersion ?? "(unknown)",
```

`config set` is unchanged: `server` remains the only settable key.

- [ ] **Step 6: Run the suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: zero warnings, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Configuration src/GrimoireCli/Commands/ConfigCommand.cs tests/GrimoireCli.Tests/Configuration
git commit -m "feat: persist when the server version was last checked"
```

---

## Task 2: The staleness rule and the warning text

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs`
- Test: `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs` (create)

**Interfaces:**
- Produces: `internal static readonly TimeSpan VersionCheckInterval`; `internal static bool ShouldCheckVersion(DateTimeOffset? lastCheck, DateTimeOffset now)`; `internal static string? VersionWarning(string? observed, string? previous)`.
- Removes: `public static void CheckServerVersion(string?)` — Task 3 moves its only caller.
- Consumed by: Task 3.

Both functions are pure so the wording and the boundary are testable without capturing logs or touching the network.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class VersionCheckCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AMissingTimestampIsDue() => Assert.True(GrimoireApiClient.ShouldCheckVersion(null, Now));

    [Fact]
    public void JustUnderTheIntervalIsNotDue()
        => Assert.False(GrimoireApiClient.ShouldCheckVersion(Now.AddHours(-23), Now));

    // The boundary is >=, so exactly the interval is due.
    [Fact]
    public void ExactlyTheIntervalIsDue()
        => Assert.True(GrimoireApiClient.ShouldCheckVersion(Now - GrimoireApiClient.VersionCheckInterval, Now));

    [Fact]
    public void PastTheIntervalIsDue()
        => Assert.True(GrimoireApiClient.ShouldCheckVersion(Now.AddHours(-25), Now));

    // A clock that moved backwards would otherwise park the check in the future
    // forever, never checking again.
    [Fact]
    public void ATimestampInTheFutureIsDue()
        => Assert.True(GrimoireApiClient.ShouldCheckVersion(Now.AddHours(1), Now));

    [Fact]
    public void TheIntervalIsADay() => Assert.Equal(TimeSpan.FromHours(24), GrimoireApiClient.VersionCheckInterval);

    [Fact]
    public void AnInRangeVersionWarnsAboutNothing()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.5.6", previous: "1.5.6"));

    [Fact]
    public void AnUnknownVersionWarnsAboutNothing()
        => Assert.Null(GrimoireApiClient.VersionWarning(null, previous: null));

    [Fact]
    public void ANewerServerNamesBothVersionsAndTheClient()
    {
        var warning = GrimoireApiClient.VersionWarning("1.6.0", previous: null);
        Assert.NotNull(warning);
        Assert.Contains("1.6.0", warning);
        Assert.Contains("1.5.6", warning);
        Assert.Contains(GrimoireApiClient.ClientVersion, warning);
        Assert.Contains("newer grimoire-cli", warning);
    }

    [Fact]
    public void AnOlderServerWarnsAboutTheFloor()
    {
        var warning = GrimoireApiClient.VersionWarning("1.4.0", previous: null);
        Assert.NotNull(warning);
        Assert.Contains("1.4.0", warning);
        Assert.Contains("older", warning);
    }

    // The operator's real signal is that the server moved, so say so.
    [Fact]
    public void AChangedVersionSaysItMoved()
    {
        var warning = GrimoireApiClient.VersionWarning("1.6.0", previous: "1.5.6");
        Assert.NotNull(warning);
        Assert.Contains("moved", warning);
        Assert.Contains("1.5.6", warning);
        Assert.Contains("1.6.0", warning);
    }

    // An unchanged in-range version stays silent even across checks.
    [Fact]
    public void AnUnchangedInRangeVersionStaysSilent()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.5.6", previous: "1.5.6"));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter VersionCheckCadenceTests`
Expected: compile errors — neither member exists.

- [ ] **Step 3: Add the two pure functions**

In `GrimoireApiClient.cs`, beside `CompareVersions`:

```csharp
    internal static readonly TimeSpan VersionCheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// True when the version is worth re-checking: never checked, a full interval
    /// has passed, or the recorded time is in the future — which means the clock
    /// moved backwards, and treating that as fresh would park the check forever.
    /// </summary>
    internal static bool ShouldCheckVersion(DateTimeOffset? lastCheck, DateTimeOffset now)
        => lastCheck is null
           || now - lastCheck.Value >= VersionCheckInterval
           || lastCheck.Value > now;

    /// <summary>
    /// The warning for an observed server version, or null when there is nothing to
    /// say. Pure so the wording is testable without capturing logs. The check is
    /// provenance, not protection: it reports being off the tested versions and
    /// never blocks.
    /// </summary>
    internal static string? VersionWarning(string? observed, string? previous)
    {
        if (string.IsNullOrEmpty(observed)) return null;

        var moved = !string.IsNullOrEmpty(previous) && previous != observed
            ? $"This server moved from Grimoire {previous} to {observed} since the last check. "
            : "";

        if (CompareVersions(observed, MinSupportedVersion) < 0)
            return $"{moved}Grimoire server version {observed} is older than the minimum supported version "
                   + $"({MinSupportedVersion}). Some features may not work.";

        if (CompareVersions(observed, MaxTestedVersion) > 0)
            return $"{moved}grimoire-cli {ClientVersion} was tested up to Grimoire {MaxTestedVersion}; "
                   + $"this server is {observed}. Check for a newer grimoire-cli.";

        return null;
    }
```

Leave `CheckServerVersion` in place for now — Task 3 removes it with its caller, so this task does not break the build.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter VersionCheckCadenceTests`
Expected: PASS (12 tests). If `AChangedVersionSaysItMoved` fails because 1.6.0 is in range, check `MaxTestedVersion` is still `1.5.6`; if the range has moved, update the versions in the tests, not the assertions' intent.

- [ ] **Step 5: Format, build, full suite, commit**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli/Api/GrimoireApiClient.cs tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs
git commit -m "feat: add the version staleness rule and warning text"
```

---

## Task 3: Probe on a cadence from every command

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs`, `src/GrimoireCli/Commands/LoginCommand.cs`

**Interfaces:**
- Consumes: Task 1's `UpdateVersionCheck` and config fields; Task 2's `ShouldCheckVersion` / `VersionWarning`.
- Produces: `public async Task CheckVersionNowAsync()` for `login`.
- Removes: `CheckServerVersion`.

- [ ] **Step 1: Hold the config and add the preflight**

`GrimoireApiClient` currently keeps no reference to the `AppConfig` it was built from. Add one (`private readonly AppConfig _config;`), assigned in the constructor, and a `private bool _versionCheckDone;`.

Then, in `SendAsync(RequestInformation info, string? permissionHint, string? notFoundHint, TimeSpan? timeout)`, replace the opening `WarnIfTokenExpired();` with `await PreflightAsync();` and add:

```csharp
    /// <summary>
    /// Runs before every request. The token warning is per-request because it is
    /// local and a long command can cross an expiry mid-run; the version check is
    /// once per process, and at most once a day across processes.
    /// </summary>
    private async Task PreflightAsync()
    {
        WarnIfTokenExpired();
        await EnsureVersionCheckedAsync();
    }

    private async Task EnsureVersionCheckedAsync()
    {
        if (_versionCheckDone) return;
        _versionCheckDone = true;
        if (!ShouldCheckVersion(_config.LastVersionCheck, DateTimeOffset.UtcNow))
        {
            _logger.Debug($"server version checked {_config.LastVersionCheck:u}, next due in "
                          + $"{VersionCheckInterval - (DateTimeOffset.UtcNow - _config.LastVersionCheck!.Value):hh\\:mm}");
            return;
        }
        var observed = await ProbeServerVersionAsync();
        if (observed != null) RecordServerVersion(observed);
    }
```

Setting `_versionCheckDone` **before** the probe matters: a failed probe must not be retried on every subsequent request in the same process.

- [ ] **Step 2: Add the probe**

```csharp
    /// <summary>
    /// GET /api/about on its own short budget, deliberately outside the normal send
    /// path: a diagnostic may not exit the process through EnsureSuccessAsync, and
    /// routing it through PreflightAsync would re-enter the check that triggered it.
    /// Returns null on any failure, which leaves the timestamp alone so the next
    /// invocation retries.
    /// </summary>
    private async Task<string?> ProbeServerVersionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(VersionProbeTimeout);
            var info = Api.Api.About.ToGetRequestInformation();
            var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token);
            if (request == null) return null;
            var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Debug($"version probe: {(int)response.StatusCode} from /api/about");
                return null;
            }
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return ReadStringProperty(body, "version");
        }
        catch (Exception ex)
        {
            // Unreachable, timed out, not JSON — all the same to a diagnostic. The
            // real command will report the outage a moment later if there is one.
            _logger.Debug($"version probe failed: {ex.Message}");
            return null;
        }
    }

    internal static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(3);
```

- [ ] **Step 3: Add the recorder**

```csharp
    /// <summary>
    /// One place owns "a version was observed", so the daily probe and login warn
    /// and persist identically.
    /// </summary>
    internal void RecordServerVersion(string? observed)
    {
        if (observed == null) return;
        var warning = VersionWarning(observed, _config.LastServerVersion);
        if (warning != null) _logger.Warn(warning);
        else _logger.Debug($"server version {observed} (in tested range {MinSupportedVersion}-{MaxTestedVersion})");

        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            new ConfigManager().UpdateVersionCheck(observed, checkedAt);
        }
        catch (Exception ex)
        {
            // An unwritable config only costs a re-probe next invocation.
            _logger.Debug($"could not record the version check: {ex.Message}");
        }
        // Keep the in-memory config in step, so a later Save(_config) by the same
        // process cannot write the stale values back over what was just recorded.
        _config.LastServerVersion = observed;
        _config.LastVersionCheck = checkedAt;
    }

    /// <summary>Probes and records regardless of the interval. Used by login, where a fresh verdict is the point.</summary>
    public async Task CheckVersionNowAsync()
    {
        _versionCheckDone = true;
        RecordServerVersion(await ProbeServerVersionAsync());
    }
```

- [ ] **Step 4: Delete `CheckServerVersion` and switch `login`**

Remove `public static void CheckServerVersion(string? version)` from `GrimoireApiClient`.

In `LoginCommand.cs`, replace the body of the post-login `try` (currently the `authed` client, the `/api/about` send and the `CheckServerVersion` call) with:

```csharp
                var authed = new GrimoireApiClient(config);
                await authed.CheckVersionNowAsync();
```

`config` here comes from `configManager.Load()` (`:94`), so it carries `lastServerVersion` from disk and the "moved from X to Y" wording works on this path. Do not simplify it to a fresh `AppConfig { Server = … }`. The surrounding comment and the `catch` that warns and still exits 0 stay as they are.

- [ ] **Step 5: Build and run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: zero warnings, all tests pass. If a test referenced `CheckServerVersion`, port it to `VersionWarning` rather than reinstating the method.

- [ ] **Step 6: Verify by hand against the live stack**

```bash
CONFIG=$HOME/.grimoire-cli/config.json
jq '.lastVersionCheck, .lastServerVersion' "$CONFIG"          # state after a login
jq 'del(.lastVersionCheck)' "$CONFIG" > /tmp/c && mv /tmp/c "$CONFIG"
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli systems list --debug 2>&1 | grep -i version
jq '.lastVersionCheck' "$CONFIG"                               # now set
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli systems list --debug 2>&1 | grep -i version
```

Expected: the first run probes and records; the second reports the check as not due. Report the actual debug lines — the smoke assertions in Task 4 grep for them, so their wording must be known before writing them.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Api/GrimoireApiClient.cs src/GrimoireCli/Commands/LoginCommand.cs
git commit -m "feat: check the server version daily, not only at login"
```

---

## Task 4: Smoke assertions and docs

**Files:**
- Modify: `docker/smoke-test.sh`, `docs/grimoire-compatibility.md`, `docs/configuration.md`

- [ ] **Step 1: Add the cadence assertions**

The smoke test already writes `$HOME/.grimoire-cli/config.json` and holds it in `$CONFIG`. Add after the login/config section, using the debug wording confirmed in Task 3 Step 6 (adjust the greps to what the code actually logs — do not change the code to match a guess):

```bash
# 4b. The version check runs on a cadence, not only at login.
jq -e '.lastServerVersion == "'"$EXPECTED_VERSION"'"' "$CONFIG" >/dev/null \
  || fail "login should have recorded the server version: $(cat "$CONFIG")"
jq -e '.lastVersionCheck != null' "$CONFIG" >/dev/null \
  || fail "login should have recorded a check timestamp"
ok "login records the server version"

# Inside the window: no probe, and the timestamp is untouched.
BEFORE=$(jq -r .lastVersionCheck "$CONFIG")
"$CLI" systems list --debug >/dev/null 2>"$WORK/inwindow.err"
grep -qi "next due" "$WORK/inwindow.err" \
  || fail "a check inside the window should say it is not due: $(cat "$WORK/inwindow.err")"
[ "$(jq -r .lastVersionCheck "$CONFIG")" = "$BEFORE" ] \
  || fail "a check inside the window must not move the timestamp"
ok "no probe inside the 24-hour window"

# Backdated: probes, warns nothing (the stack is the tested version), and advances.
jq '.lastVersionCheck = "2020-01-01T00:00:00+00:00"' "$CONFIG" > "$WORK/cfg" && mv "$WORK/cfg" "$CONFIG"
"$CLI" systems list --debug >/dev/null 2>"$WORK/stale.err"
[ "$(jq -r .lastVersionCheck "$CONFIG")" != "2020-01-01T00:00:00+00:00" ] \
  || fail "a stale timestamp should have triggered a probe: $(cat "$WORK/stale.err")"
jq -e '.lastServerVersion == "'"$EXPECTED_VERSION"'"' "$CONFIG" >/dev/null \
  || fail "the probe should have recorded the version"
ok "a stale timestamp triggers a probe and advances"
```

`EXPECTED_VERSION` does not exist yet — define it near the top beside the other expectations, reading the truth from the stack rather than hardcoding it:

```bash
EXPECTED_VERSION=$(curl -sf "$SERVER/api/openapi.json" | jq -r .info.version)
```

- [ ] **Step 2: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Both must pass: the second run starts with state the first left behind, which is the point.

- [ ] **Step 3: Rewrite the compatibility doc's "Runtime check"**

`docs/grimoire-compatibility.md` currently says the check happens in `login`. Replace that section with what is now true: the check runs before the first request of any command, at most once per 24 hours, recorded in `lastVersionCheck` / `lastServerVersion`; `login` forces one; a failed probe is silent except under `--debug` and leaves the timestamp alone so the next invocation retries; it is warn-only and never blocks. Keep the floor/ceiling/in-range bullets, which are still accurate.

- [ ] **Step 4: Document the two config keys**

In `docs/configuration.md`, add both keys to the config-file description, marked as written by the CLI and not settable with `config set`. State that a machine with no config file gets one created holding only these two fields on the first check.

- [ ] **Step 5: Run all four gates and the published binary**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -o publish
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
```

Report the test count, the smoke `ok:` count, and whether the publish emitted any IL warnings.

- [ ] **Step 6: Commit**

```bash
git add docker/smoke-test.sh docs/grimoire-compatibility.md docs/configuration.md
git commit -m "docs: record the version check cadence"
```

---

## Self-review notes

Checked against the spec:

- Trigger, staleness, probe, message, persistence, failure handling, recorder, login and `config get` → Tasks 1-3. Testing → Tasks 1, 2 and 4. Docs → Task 4.
- The three bugs abs-cli caught in review are each pinned by a step: `Resolve` carrying the fields (Task 1 Step 1, first test), `login` keeping a config that holds `lastServerVersion` (Task 3 Step 4), and `RecordServerVersion` updating `_config` as well as disk (Task 3 Step 3).
- No `self-test` change and no README change, per the spec: no new DTO crosses the JSON boundary and no verb or flag is added.
