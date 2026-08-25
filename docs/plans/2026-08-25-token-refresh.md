# Transparent Token Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Renew the 30-minute Grimoire 1.6.0 access token from the stored refresh
cookie, transparently, so a saved login keeps working instead of dying half an
hour later.

**Architecture:** The refresh token is persisted in `config.json` beside the
access token and presented as a `Cookie` header on `POST /api/auth/refresh`.
Refresh fires proactively when the JWT is within 60 seconds of expiry, and
reactively on a `401` carrying `X-Token-Expired`, in which case the request is
rebuilt from its `RequestInformation` and sent once more. Everything lives in
`GrimoireApiClient`, which already owns sending and error mapping.

**Tech Stack:** C# / .NET 10, System.CommandLine 2.0.7, Kiota-generated request
builders, NLog, xUnit, Native AOT publish.

**Spec:** [docs/specs/2026-08-25-token-refresh-design.md](../specs/2026-08-25-token-refresh-design.md)

## Global Constraints

- **Do not touch `MinSupportedVersion` / `MaxTestedVersion`** in
  `src/GrimoireCli/Api/GrimoireApiClient.cs`. Both stay `"1.5.6"`; the
  supported-range reconciliation is workstream C.
- **Never hand-edit `src/GrimoireCli/Generated/` or any `*.g.cs`.** Regenerate.
- **Never edit `CHANGELOG.md`, `docs/roadmap.md`, or
  `docs/grimoire-api-coverage.md`.** The last is generated from
  `tools/generate-api-coverage.py`.
- **Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.**
  CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** in method bodies: none between consecutive
  `AddCommand`/`AddOption` calls, none before a `return` that follows setup, none
  between consecutive variable declarations of the same kind.
- **Comments say what the code does or why it must be this way — never what was
  deliberately left out.** State requirements positively.
- **The refresh cookie name is `grimoire_refresh`** — exact string, one place.
- **The user-facing failure message is exactly**
  `Session expired. Run: grimoire-cli login`, exit code `2`.
- **Anything that writes goes to the local stack**
  (`http://host.docker.internal:9481`), never a live instance.
- **Commit per task**, Conventional Commits (`type: subject`, imperative,
  lowercase, no period). **No `Co-Authored-By` and no tool-attribution lines.**

## Verified server facts these tasks rely on

Measured against `hunterreadca/grimoire:nightly` commit
`7f5937071f51dfc65bc09f5e5e49d33c431f0a5d`. Do not re-derive; do not "correct"
code that matches these.

- Login sets `grimoire_refresh=<opaque>; HttpOnly; Max-Age=2592000;
  Path=/api/auth; SameSite=strict`. The value is **not** a JWT — its expiry
  cannot be read locally.
- `POST /api/auth/refresh` authenticates on that cookie alone, returns
  `{"token": …, "user": {…}}`, and re-sets both cookies.
- **It tolerates a stale `Authorization: Bearer` header** — verified 200 with a
  deliberately expired token attached. So `RefreshAsync` does not strip it, and
  must not try: `HttpClient` re-applies `DefaultRequestHeaders.Authorization` at
  send time regardless of what the request object says.
- An expired-but-validly-signed JWT yields `401`,
  `{"detail":"Token expired - please log in again"}`, **and the header
  `X-Token-Expired: 1`**. A missing or malformed token yields
  `{"detail":"Not authenticated"}` / `{"detail":"Invalid token"}` with **no**
  such header.
- Replaying an already-rotated refresh token returns `401` **and revokes the
  session**, killing the token that replaced it. There is no grace window.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/GrimoireCli/Configuration/AppConfig.cs` | gains `RefreshToken` |
| `src/GrimoireCli/Configuration/ConfigManager.cs` | resolve scoping + `UpdateTokens` |
| `src/GrimoireCli/Api/GrimoireApiClient.cs` | cookie extraction, refresh, retry, injectable handler |
| `src/GrimoireCli/Commands/LoginCommand.cs` | persist the cookie; correct the help text |
| `src/GrimoireCli/Commands/SelfTestCommand.cs` | round-trip the new field |
| `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs` | resolve scoping, `UpdateTokens` |
| `tests/GrimoireCli.Tests/Api/ExtractCookieTests.cs` | **new** — cookie parsing |
| `tests/GrimoireCli.Tests/Api/TokenRefreshTests.cs` | **new** — predicates + live retry through a stub handler |
| `docker/smoke-test.sh` | the stale-refresh-token negative case |
| `tools/generate-api-coverage.py` | mark `/api/auth/refresh` implemented |
| `docs/authentication.md`, `docs/configuration.md`, `docs/grimoire-api-notes.md`, `CLAUDE.md` | documentation |

---

## Task 1: Store and scope the refresh token

**Files:**
- Modify: `src/GrimoireCli/Configuration/AppConfig.cs`
- Modify: `src/GrimoireCli/Configuration/ConfigManager.cs:146-176`
- Modify: `src/GrimoireCli/Commands/SelfTestCommand.cs:28-33`
- Test: `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs`

**Interfaces:**
- Produces: `AppConfig.RefreshToken` (`string?`, JSON name `refreshToken`);
  `ConfigManager.UpdateTokens(string accessToken, string? refreshToken)`.
- Consumes: nothing.

- [ ] **Step 1: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs`. The
existing `InTempDir(out var path)` helper in that class gives you a
`ConfigManager` over a throwaway file — use it exactly as the neighbouring tests
do, including the `finally` cleanup.

```csharp
    // A token from a flag or the environment belongs to a different session than
    // the stored cookie, so the cookie must not be offered as a way to renew it.
    [Fact]
    public void ResolveDropsRefreshTokenWhenTheAccessTokenIsOverridden()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig
            {
                Server = "http://example.test",
                AccessToken = "file-access",
                RefreshToken = "file-refresh"
            });
            var fromFile = manager.Resolve();
            Assert.Equal("file-refresh", fromFile.RefreshToken);
            var flagged = manager.Resolve(flagToken: "flag-access");
            Assert.Null(flagged.RefreshToken);
            var fromEnv = manager.Resolve(
                envLookup: name => name == "GRIMOIRE_TOKEN" ? "env-access" : null);
            Assert.Null(fromEnv.RefreshToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void UpdateTokensWritesBothAndDoesNotPersistEnvironmentValues()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test" });
            manager.Resolve(envLookup: name => name == "GRIMOIRE_TOKEN" ? "env-only-token" : null);
            manager.UpdateTokens("new-access", "new-refresh");
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("env-only-token", raw);
            var back = manager.Load();
            Assert.Equal("new-access", back.AccessToken);
            Assert.Equal("new-refresh", back.RefreshToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The server rotates on every refresh, so a 200 that carried no new cookie
    // leaves the stored one as the best available credential.
    [Fact]
    public void UpdateTokensKeepsTheStoredRefreshTokenWhenGivenNull()
    {
        var manager = InTempDir(out var path);
        try
        {
            manager.Save(new AppConfig { AccessToken = "old", RefreshToken = "keep-me" });
            manager.UpdateTokens("new-access", null);
            var back = manager.Load();
            Assert.Equal("new-access", back.AccessToken);
            Assert.Equal("keep-me", back.RefreshToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~ConfigManagerTests"
```

Expected: compile errors — `AppConfig.RefreshToken` and
`ConfigManager.UpdateTokens` do not exist.

- [ ] **Step 3: Add the config field**

In `src/GrimoireCli/Configuration/AppConfig.cs`, after the `AccessToken`
property:

```csharp
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }
```

- [ ] **Step 4: Scope it in `Resolve`**

Replace the body of `ConfigManager.Resolve` (currently at `:146-160`) with:

```csharp
        envLookup ??= Environment.GetEnvironmentVariable;
        var fileConfig = Load();
        var tokenOverride = flagToken ?? envLookup("GRIMOIRE_TOKEN");
        return new AppConfig
        {
            Server = flagServer
                ?? envLookup("GRIMOIRE_SERVER")
                ?? fileConfig.Server,
            AccessToken = tokenOverride ?? fileConfig.AccessToken,
            // The stored cookie renews the session it was issued for, so it
            // travels only with the access token from the same file.
            RefreshToken = tokenOverride == null ? fileConfig.RefreshToken : null,
            LastVersionCheck = fileConfig.LastVersionCheck,
            LastServerVersion = fileConfig.LastServerVersion
        };
```

- [ ] **Step 5: Add `UpdateTokens`**

Append to `ConfigManager`, directly after `UpdateVersionCheck`:

```csharp
    /// <summary>
    /// Persists a refreshed token pair by read-modify-write of the config file,
    /// for the same reason as <see cref="UpdateVersionCheck"/>: writing a
    /// resolved config would put a GRIMOIRE_TOKEN value on disk that the operator
    /// chose to keep out of it. A null <paramref name="refreshToken"/> leaves the
    /// stored one in place — the server rotates on every refresh, so the value
    /// already on disk is the best credential available if a response carried no
    /// new cookie.
    /// </summary>
    public void UpdateTokens(string accessToken, string? refreshToken)
    {
        var onDisk = Load();
        onDisk.AccessToken = accessToken;
        if (refreshToken != null)
            onDisk.RefreshToken = refreshToken;
        Save(onDisk);
    }
```

- [ ] **Step 6: Cover the new field in `self-test`**

In `src/GrimoireCli/Commands/SelfTestCommand.cs`, the `AppConfig` round-trip
check currently builds a config and compares `Server` and `AccessToken`. Add the
new field to both the constructed value and the comparison. Find the block that
serializes `config` with `AppJsonContext.Default.AppConfig` and extend its
condition:

```csharp
            if (back?.Server != config.Server || back.AccessToken != config.AccessToken
                || back.RefreshToken != config.RefreshToken)
                failures.Add("AppConfig JSON round-trip failed");
```

Set `RefreshToken` on the `config` object that block builds, so the comparison
is not vacuous. If that object is built inline, give it a non-null
`RefreshToken` value.

- [ ] **Step 7: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all pass. The whole suite must be green, not just the filter.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Configuration src/GrimoireCli/Commands/SelfTestCommand.cs \
        tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs
git commit -m "feat: store the refresh token beside the access token"
```

---

## Task 2: Capture the refresh cookie at login

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs:34-85`
- Modify: `src/GrimoireCli/Commands/LoginCommand.cs:22-27,86-100`
- Test: `tests/GrimoireCli.Tests/Api/ExtractCookieTests.cs` (create)

**Interfaces:**
- Consumes: `AppConfig.RefreshToken` from Task 1.
- Produces:
  - `internal const string GrimoireApiClient.RefreshCookieName = "grimoire_refresh";`
  - `internal static string? GrimoireApiClient.ExtractCookie(IEnumerable<string> setCookieHeaders, string name)`
  - `GrimoireApiClient.LoginAsync` now returns
    `Task<(string Body, string? RefreshToken)>`.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Api/ExtractCookieTests.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

// Grimoire delivers the refresh token only as a Set-Cookie header, so this
// parser is the sole way the CLI ever obtains one.
public class ExtractCookieTests
{
    private const string Session = "grimoire_session=jwt; HttpOnly; Max-Age=2592000; Path=/; SameSite=lax";
    private const string Refresh = "grimoire_refresh=abc123; HttpOnly; Max-Age=2592000; Path=/api/auth; SameSite=strict";

    [Fact]
    public void FindsTheNamedCookieAmongOthers()
        => Assert.Equal("abc123",
            GrimoireApiClient.ExtractCookie(new[] { Session, Refresh }, "grimoire_refresh"));

    [Fact]
    public void ReturnsNullWhenTheCookieIsAbsent()
        => Assert.Null(GrimoireApiClient.ExtractCookie(new[] { Session }, "grimoire_refresh"));

    [Fact]
    public void ReturnsNullForNoHeadersAtAll()
        => Assert.Null(GrimoireApiClient.ExtractCookie(Array.Empty<string>(), "grimoire_refresh"));

    [Fact]
    public void HandlesAValueWithNoTrailingAttributes()
        => Assert.Equal("bare",
            GrimoireApiClient.ExtractCookie(new[] { "grimoire_refresh=bare" }, "grimoire_refresh"));

    // A longer name must not match on its prefix.
    [Fact]
    public void DoesNotMatchANameThatMerelyStartsTheSame()
        => Assert.Null(GrimoireApiClient.ExtractCookie(
            new[] { "grimoire_refresh_other=nope; Path=/" }, "grimoire_refresh"));

    // The server clears a dead cookie by sending it empty. Callers test with
    // string.IsNullOrEmpty, so "" and null are equivalent to them.
    [Fact]
    public void ReturnsAnEmptyStringWhenTheServerClearsTheCookie()
        => Assert.Equal("",
            GrimoireApiClient.ExtractCookie(new[] { "grimoire_refresh=; Path=/api/auth" }, "grimoire_refresh"));
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~ExtractCookieTests"
```

Expected: compile error — `ExtractCookie` does not exist.

- [ ] **Step 3: Implement `ExtractCookie` and the cookie-name constant**

In `src/GrimoireCli/Api/GrimoireApiClient.cs`, next to `ExtractToken`:

```csharp
    /// <summary>The refresh token's cookie name, per <c>REFRESH_COOKIE_NAME</c>.</summary>
    internal const string RefreshCookieName = "grimoire_refresh";

    /// <summary>
    /// Reads one cookie's value out of a response's Set-Cookie headers. Grimoire
    /// delivers the refresh token only this way, so this is the sole path by
    /// which the CLI obtains one. Returns the text between "name=" and the first
    /// ";", which is empty when the server is clearing the cookie.
    /// </summary>
    internal static string? ExtractCookie(IEnumerable<string> setCookieHeaders, string name)
    {
        var prefix = name + "=";
        foreach (var header in setCookieHeaders)
        {
            if (!header.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var value = header[prefix.Length..];
            var end = value.IndexOf(';');
            return end < 0 ? value : value[..end];
        }
        return null;
    }
```

- [ ] **Step 4: Own cookie handling explicitly and return the captured value**

In the constructor, replace `new HttpClientHandler()` with a handler that leaves
cookies to this class:

```csharp
        // The CLI reads the refresh cookie off the login response itself and
        // stores it, so cookie handling stays here rather than in a container
        // whose contents die with the process.
        var debugHandler = new DebugHttpHandler(new HttpClientHandler { UseCookies = false });
```

Then change `LoginAsync` to return the cookie alongside the body:

```csharp
    public async Task<(string Body, string? RefreshToken)> LoginAsync(string username, string password)
    {
        var body = new Generated.Models.LoginRequest { Username = username, Password = password };
        var info = Api.Api.Auth.Login.ToPostRequestInformation(body);
        using var cts = new CancellationTokenSource(DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException("Failed to build login request");
        var response = await _http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
        return (responseBody, ReadRefreshCookie(response));
    }

    /// <summary>The rotated refresh token from a login or refresh response.</summary>
    private static string? ReadRefreshCookie(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? ExtractCookie(cookies, RefreshCookieName)
            : null;
```

Update the XML doc on `LoginAsync` so it mentions that the refresh token arrives
as a cookie rather than in the body.

- [ ] **Step 5: Persist it in `LoginCommand`**

At `src/GrimoireCli/Commands/LoginCommand.cs:86`, the call site becomes:

```csharp
                var (body, refreshToken) = await client.LoginAsync(username!, password!);
                var token = GrimoireApiClient.ExtractToken(body);
```

and after `config.AccessToken = token;` add:

```csharp
                config.RefreshToken = refreshToken;
```

Assigning unconditionally is correct: a server that issues no refresh cookie
(1.5.6) must clear any stale one rather than leave a token from an older session
in place.

- [ ] **Step 6: Correct the login help text**

The Notes block at `LoginCommand.cs:22-27` currently claims Grimoire has no
refresh endpoint, which is now false, and `--help` is the primary interface for
the agents driving this CLI. Replace those two lines:

```csharp
            "The session refreshes itself; log in again only after 30 days idle,",
            "or if the session is revoked (password change, admin edit).",
```

Keep the `--password` lines and the OIDC line exactly as they are. Then check
whether any test asserts the old wording:

```bash
grep -rn "no refresh endpoint\|valid 30 days" tests/ src/
```

Update any assertion that matches, and fix the same stale claim in the comment
above `WarnIfTokenExpired` in `GrimoireApiClient.cs` and in the two
`ConfigManager` XML docs that say a token "cannot be refreshed"
(`QuarantineUnparseableConfig` and `Save`).

- [ ] **Step 7: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 8: Verify against the live stack by hand**

The stack must be up and seeded. This proves the cookie actually lands on disk:

```bash
printf 'admin' | src/GrimoireCli/bin/Debug/net10.0/grimoire-cli login \
  --server http://host.docker.internal:9481 --username admin --password-stdin
jq -e '.refreshToken | length > 20' ~/.grimoire-cli/config.json
```

Expected: the `jq` exits 0. If it does not, the cookie was not captured — do not
proceed.

- [ ] **Step 9: Commit**

```bash
git add src/GrimoireCli tests/GrimoireCli.Tests/Api/ExtractCookieTests.cs
git commit -m "feat: capture the refresh cookie at login"
```

---

## Task 3: Refresh proactively and on an expired-token 401

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs:34-72,191-247`
- Test: `tests/GrimoireCli.Tests/Api/TokenRefreshTests.cs` (create)

**Interfaces:**
- Consumes: `RefreshCookieName`, `ExtractCookie`, `ReadRefreshCookie` (Task 2);
  `ConfigManager.UpdateTokens` (Task 1).
- Produces:
  - `GrimoireApiClient(AppConfig config, ConfigManager? configManager = null, HttpMessageHandler? innerHandler = null)`
  - `internal static bool ShouldRefreshProactively(string? accessToken, bool haveRefreshToken)`
  - `internal static bool ShouldRefreshOn401(HttpResponseMessage response, bool haveRefreshToken)`

**Why the predicates are separate:** this repo already splits a pure verdict from
the action that exits — `IsJsonOrEmpty` is unit-tested, `EnsureJson` calls
`Environment.Exit` and is not. Follow that split: the decisions are tested
directly, and the tests never drive a path that exits, because
`Environment.Exit` would kill the test host.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Api/TokenRefreshTests.cs`. Note two things the
stub setup must get right, or the tests will fail for the wrong reason:
`LastVersionCheck` is set to now so `PreflightAsync` skips its `/api/about`
probe, and every stubbed response must be a 2xx JSON body so
`EnsureSuccessAsync` and `EnsureJson` never reach `Environment.Exit`.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Api;

public class TokenRefreshTests
{
    // Signature-free JWTs: only the exp claim is read, by TokenHelper.
    private static string Jwt(int secondsFromNow)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddSeconds(secondsFromNow).ToUnixTimeSeconds();
        return $"{B64("{\"alg\":\"HS256\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string Body, string? Auth, string? Cookie)> Seen { get; } = new();
        public Func<int, HttpResponseMessage>? Respond { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("Cookie", out var cookie);
            Seen.Add((request.RequestUri!.AbsolutePath, body,
                request.Headers.Authorization?.Parameter, cookie?.FirstOrDefault()));
            return Respond!(Seen.Count - 1);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ExpiredTokenUnauthorized()
    {
        var response = Json(HttpStatusCode.Unauthorized,
            "{\"detail\":\"Token expired - please log in again\"}");
        response.Headers.Add("X-Token-Expired", "1");
        return response;
    }

    private static HttpResponseMessage RefreshOk(string newAccess, string newRefresh)
    {
        var response = Json(HttpStatusCode.OK, $"{{\"token\":\"{newAccess}\"}}");
        response.Headers.TryAddWithoutValidation("Set-Cookie",
            $"grimoire_refresh={newRefresh}; HttpOnly; Path=/api/auth; SameSite=strict");
        return response;
    }

    private static (GrimoireApiClient client, ConfigManager manager, string path, AppConfig config)
        Build(RecordingHandler handler, string accessToken, string? refreshToken)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var manager = new ConfigManager(path);
        var config = new AppConfig
        {
            Server = "http://grimoire.test",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            // Keeps PreflightAsync from probing /api/about through the stub.
            LastVersionCheck = DateTimeOffset.UtcNow,
            LastServerVersion = "nightly"
        };
        manager.Save(config);
        return (new GrimoireApiClient(config, manager, handler), manager, path, config);
    }

    [Theory]
    [InlineData(30, true, true)]    // inside the 60s threshold, cookie held
    [InlineData(30, false, false)]  // inside it, but nothing to refresh with
    [InlineData(3600, true, false)] // plenty of life left
    public void ShouldRefreshProactively_OnlyInsideTheThresholdAndWithACookie(
        int secondsLeft, bool haveRefreshToken, bool expected)
        => Assert.Equal(expected,
            GrimoireApiClient.ShouldRefreshProactively(Jwt(secondsLeft), haveRefreshToken));

    [Fact]
    public void ShouldRefreshProactively_IsFalseWithNoAccessToken()
        => Assert.False(GrimoireApiClient.ShouldRefreshProactively(null, haveRefreshToken: true));

    [Fact]
    public void ShouldRefreshOn401_TrueForAnExpiredTokenWithACookie()
        => Assert.True(GrimoireApiClient.ShouldRefreshOn401(
            ExpiredTokenUnauthorized(), haveRefreshToken: true));

    // A bare 401 is "not authenticated" or "invalid token" — refreshing would
    // spend a request against a rate-limited endpoint for nothing.
    [Fact]
    public void ShouldRefreshOn401_FalseWithoutTheHeader()
        => Assert.False(GrimoireApiClient.ShouldRefreshOn401(
            Json(HttpStatusCode.Unauthorized, "{\"detail\":\"Not authenticated\"}"),
            haveRefreshToken: true));

    [Fact]
    public void ShouldRefreshOn401_FalseForAPermissionDenial()
        => Assert.False(GrimoireApiClient.ShouldRefreshOn401(
            Json(HttpStatusCode.Forbidden, "{\"detail\":\"Forbidden\"}"), haveRefreshToken: true));

    [Fact]
    public void ShouldRefreshOn401_FalseWithNoCookieHeld()
        => Assert.False(GrimoireApiClient.ShouldRefreshOn401(
            ExpiredTokenUnauthorized(), haveRefreshToken: false));

    // The whole point of the reactive path: a PATCH that meets an expired token
    // must reach the server a second time with its body intact. abs-cli's
    // equivalent rebuilds the request without content and silently sends nothing.
    [Fact]
    public async Task RetryAfterRefresh_ResendsTheOriginalBody()
    {
        var handler = new RecordingHandler
        {
            Respond = i => i switch
            {
                0 => ExpiredTokenUnauthorized(),
                1 => RefreshOk(Jwt(1800), "rotated-cookie"),
                _ => Json(HttpStatusCode.OK, "{\"id\":\"sys-1\"}")
            }
        };
        var (client, manager, path, _) = Build(handler, Jwt(1800), "stored-cookie");
        try
        {
            // Mirrors SystemsService.UpdateAsync: an empty generated model carries
            // the shape, then the validated raw body replaces the content. That is
            // the only way a PATCH body is built in this CLI, so it is the shape
            // the retry has to preserve.
            var info = client.Api.Api.Systems["sys-1"].ToPatchRequestInformation(
                new GrimoireCli.Generated.Models.GameSystemUpdate());
            info.SetStreamContent(
                new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"Renamed\"}")),
                "application/json");

            var body = await client.SendAsync(info);

            Assert.Equal("{\"id\":\"sys-1\"}", body);
            Assert.Equal(3, handler.Seen.Count);
            Assert.Equal("/api/auth/refresh", handler.Seen[1].Path);
            Assert.Equal("grimoire_refresh=stored-cookie", handler.Seen[1].Cookie);
            // The retry carries the same body as the first attempt.
            Assert.Contains("Renamed", handler.Seen[0].Body);
            Assert.Equal(handler.Seen[0].Body, handler.Seen[2].Body);
            // ...and the new access token, not the dead one.
            Assert.NotEqual(handler.Seen[0].Auth, handler.Seen[2].Auth);
            // The rotated pair is on disk for the next invocation.
            var saved = manager.Load();
            Assert.Equal("rotated-cookie", saved.RefreshToken);
            Assert.Equal(handler.Seen[2].Auth, saved.AccessToken);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task ProactiveRefresh_HappensBeforeTheRequest()
    {
        var handler = new RecordingHandler
        {
            Respond = i => i == 0
                ? RefreshOk(Jwt(1800), "rotated-cookie")
                : Json(HttpStatusCode.OK, "[]")
        };
        var (client, _, path, _) = Build(handler, Jwt(10), "stored-cookie");
        try
        {
            await client.SendAsync(client.Api.Api.Systems.ToGetRequestInformation());
            Assert.Equal(2, handler.Seen.Count);
            Assert.Equal("/api/auth/refresh", handler.Seen[0].Path);
            Assert.Equal("/api/systems", handler.Seen[1].Path);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task NoRefreshTokenMeansNoRefreshAttempt()
    {
        var handler = new RecordingHandler { Respond = _ => Json(HttpStatusCode.OK, "[]") };
        var (client, _, path, _) = Build(handler, Jwt(10), refreshToken: null);
        try
        {
            await client.SendAsync(client.Api.Api.Systems.ToGetRequestInformation());
            Assert.Single(handler.Seen);
            Assert.Equal("/api/systems", handler.Seen[0].Path);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
```

These spellings are verified against `SystemsService.cs:61-63` and
`SystemsService.cs:18`. `GameSystemUpdate.Name` is a composed-type wrapper, not a
`string`, which is why the body goes on as raw bytes rather than through the
model's properties. Do **not** edit generated code to make a test compile.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~TokenRefreshTests"
```

Expected: compile errors — the third constructor parameter and both predicates
do not exist.

- [ ] **Step 3: Make the handler injectable**

Change the constructor signature and the handler it builds:

```csharp
    public GrimoireApiClient(AppConfig config, ConfigManager? configManager = null,
        HttpMessageHandler? innerHandler = null)
    {
        _config = config;
        _configManager = configManager ?? new ConfigManager();
        // The CLI reads the refresh cookie off the login response itself and
        // stores it, so cookie handling stays here rather than in a container
        // whose contents die with the process.
        var debugHandler = new DebugHttpHandler(
            innerHandler ?? new HttpClientHandler { UseCookies = false });
```

Leave the rest of the constructor as it is.

- [ ] **Step 4: Add the two predicates**

Place them beside `IsJsonOrEmpty`, which they mirror in style:

```csharp
    /// <summary>
    /// Whether to renew before sending. The threshold matches abs-cli's, and a
    /// stored cookie is required because there is nothing else to renew with.
    /// </summary>
    internal static bool ShouldRefreshProactively(string? accessToken, bool haveRefreshToken)
        => haveRefreshToken
           && !string.IsNullOrEmpty(accessToken)
           && TokenHelper.IsExpiringSoon(accessToken, thresholdSeconds: 60);

    /// <summary>
    /// Whether a failed response is the one kind of 401 a refresh can fix.
    /// Grimoire marks an expired access token with X-Token-Expired specifically
    /// so it stays distinguishable from "not authenticated" and "invalid token",
    /// and the refresh endpoint is rate-limited, so the other 401s are left alone.
    /// </summary>
    internal static bool ShouldRefreshOn401(HttpResponseMessage response, bool haveRefreshToken)
        => haveRefreshToken
           && response.StatusCode == System.Net.HttpStatusCode.Unauthorized
           && response.Headers.Contains("X-Token-Expired");
```

- [ ] **Step 5: Implement the refresh itself**

Add to `GrimoireApiClient`, near `PreflightAsync`:

```csharp
    private bool HasRefreshToken => !string.IsNullOrEmpty(_config.RefreshToken);

    /// <summary>
    /// Exchanges the stored refresh cookie for a new token pair. The endpoint
    /// authenticates on the cookie alone; the stale bearer header that
    /// HttpClient attaches from its defaults is ignored by the server. Any
    /// failure is terminal for this command — the session is gone and only a
    /// fresh login can replace it.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var info = Api.Api.Auth.Refresh.ToPostRequestInformation();
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cancellationToken)
            ?? throw new InvalidOperationException("Failed to build refresh request");
        request.Headers.Add("Cookie", $"{RefreshCookieName}={_config.RefreshToken}");
        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Debug($"refresh rejected: {(int)response.StatusCode} "
                          + $"{TruncateForLogging(await response.Content.ReadAsStringAsync(cancellationToken))}");
            SessionExpired();
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = ExtractToken(body);
        if (token == null)
        {
            _logger.Debug($"refresh response carried no token: {TruncateForLogging(body)}");
            SessionExpired();
        }
        var rotated = ReadRefreshCookie(response);
        _config.AccessToken = token;
        if (!string.IsNullOrEmpty(rotated))
            _config.RefreshToken = rotated;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            _configManager.UpdateTokens(token!, string.IsNullOrEmpty(rotated) ? null : rotated);
        }
        catch (ConfigWriteException ex)
        {
            // This command still works, but the rotated token is the only live
            // one and it is not on disk, so the next invocation starts from a
            // credential the server has already retired.
            _logger.Warn($"{ex.Message} The next command may need a fresh login.");
        }
        _logger.Debug("token refresh succeeded");
    }

    /// <summary>
    /// Reports a session that can no longer be renewed. Routine rather than
    /// exceptional: the server also revokes on a password change or an admin edit.
    /// </summary>
    private static void SessionExpired()
    {
        _logger.Error("Session expired. Run: grimoire-cli login");
        Environment.Exit(2);
    }
```

- [ ] **Step 6: Wire the proactive path**

Replace `WarnIfTokenExpired` with a version that renews when it can, and keep the
warning for the case where it cannot. `PreflightAsync` becomes async on this
call, which it already is:

```csharp
    /// <summary>
    /// Renews the access token before sending when it is nearly out and a stored
    /// cookie can renew it. With no cookie — a token from a flag or the
    /// environment, or a 1.5.6 server that issues none — the request still goes
    /// out and the server answers with its own 401.
    /// </summary>
    private async Task EnsureValidTokenAsync(CancellationToken cancellationToken)
    {
        var token = _http.DefaultRequestHeaders.Authorization?.Parameter;
        if (token == null) return;
        if (ShouldRefreshProactively(token, HasRefreshToken))
        {
            _logger.Debug($"access token expiring in {TokenHelper.SecondsUntilExpiry(token)}s, refreshing");
            await RefreshAsync(cancellationToken);
            return;
        }
        if (TokenHelper.IsExpiringSoon(token, thresholdSeconds: 60))
            _logger.Warn("Access token has expired or is about to. Run: grimoire-cli login");
        else
            _logger.Debug($"access token valid ({TokenHelper.SecondsUntilExpiry(token)}s remaining)");
    }
```

and in `PreflightAsync`:

```csharp
    private async Task PreflightAsync(CancellationToken cancellationToken)
    {
        await EnsureValidTokenAsync(cancellationToken);
        await EnsureVersionCheckedAsync();
    }
```

Update both `PreflightAsync` callers to pass `cts.Token`. Keep the existing XML
doc on `PreflightAsync`, adjusting the sentence about the token warning to
describe renewal.

- [ ] **Step 7: Wire the reactive path**

Add the shared send-with-retry helper, and route both public senders through it:

```csharp
    /// <summary>
    /// Sends a request, and if the access token turns out to have expired,
    /// renews it and sends the request once more. The retry rebuilds the native
    /// request from the same <see cref="RequestInformation"/> — an
    /// HttpRequestMessage cannot be resent — and rewinds the body stream first so
    /// the second attempt carries the same content as the first. Every body the
    /// CLI sends is a seekable MemoryStream — the SetStreamContent call sites in
    /// Services wrap one over an in-memory byte array, and the generated builders
    /// use SetContentFromParsable. A body that reports otherwise is not replayed.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRefreshAsync(
        RequestInformation info, CancellationToken cancellationToken)
    {
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to build request for {info.URI.AbsolutePath}");
        var response = await _http.SendAsync(request, cancellationToken);
        if (!ShouldRefreshOn401(response, HasRefreshToken)) return response;
        if (info.Content is { CanSeek: false })
        {
            // Replaying needs the body back, and this one cannot be rewound. The
            // 401 stands, and the next invocation renews before it sends.
            _logger.Debug($"not replaying {info.URI.AbsolutePath}: the request body cannot be rewound");
            return response;
        }
        _logger.Debug($"access token expired on {info.URI.AbsolutePath}, refreshing and retrying");
        await RefreshAsync(cancellationToken);
        if (info.Content != null)
            info.Content.Position = 0;
        var retry = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cancellationToken)
            ?? throw new InvalidOperationException($"Failed to rebuild request for {info.URI.AbsolutePath}");
        return await _http.SendAsync(retry, cancellationToken);
    }
```

`SendAsync` becomes:

```csharp
    public async Task<string> SendAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        await PreflightAsync(cts.Token);
        var response = await SendWithRefreshAsync(info, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        EnsureJson(body, info.URI.PathAndQuery);
        return body;
    }
```

`SendStreamAsync` takes the same shape, returning
`await response.Content.ReadAsStreamAsync(cts.Token)`. Note the
`CancellationTokenSource` now has to be created **before** the preflight, since
the preflight may issue the refresh.

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green, including the pre-existing suite.

- [ ] **Step 9: Run the new tests repeatedly**

These tests write temp config files and share no process state, but a previous
regression in this repo was a race invisible to a single green run. Confirm
stability:

```bash
for i in 1 2 3; do
  dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj || break
done
```

Expected: three clean runs. If any run fails, the cause is a shared-state leak —
fix it rather than re-running until it passes.

- [ ] **Step 10: Commit**

```bash
git add src/GrimoireCli/Api/GrimoireApiClient.cs tests/GrimoireCli.Tests/Api/TokenRefreshTests.cs
git commit -m "feat: refresh the access token proactively and on expiry"
```

---

## Task 4: Prove the failure path against the live stack

**Files:**
- Modify: `docker/smoke-test.sh` (append before the final `echo "smoke: all checks passed"`)

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: one new `ok` assertion, taking the count from 79 to 80.

The stack must be up and seeded first:

```bash
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- [ ] **Step 1: Add the case**

Append to `docker/smoke-test.sh`, immediately before the final
`echo "smoke: all checks passed"`.

The case needs the CLI to hold a refresh token the server has already rotated
away, and an access token the CLI itself judges spent — otherwise nothing
triggers the refresh path. Both are arranged deterministically, with no sleep:
`curl` rotates the cookie out from under the CLI, and a locally minted
expired-but-validly-signed JWT stands in for a token that has aged out. The dev
stack's signing key is the checked-in placeholder from `docker-compose.yml`, so
the mint is guarded on that key still being in use and skips rather than fails if
it ever changes.

```bash
# Grimoire does not merely refuse a refresh token it has already rotated away:
# it reads the replay as theft and revokes the session. Reaching that state on
# purpose is the only way to check the failure path without waiting out a
# 30-minute access token.
DEV_SECRET=$(docker inspect docker-grimoire-1 \
  --format '{{range .Config.Env}}{{println .}}{{end}}' 2>/dev/null \
  | sed -n 's/^SECRET_KEY=//p')
if [ "$DEV_SECRET" != "dev-only-not-a-real-secret" ]; then
  echo "  skip: the retired-session case needs the dev SECRET_KEY" >&2
else
  STORED=$(jq -r '.refreshToken // empty' "$CONFIG")
  [ -n "$STORED" ] || fail "login stored no refresh token: $(cat "$CONFIG")"
  curl -sf -X POST "$SERVER/api/auth/refresh" \
    -H "Cookie: grimoire_refresh=$STORED" -o /dev/null \
    || fail "could not rotate the refresh token out from under the CLI"
  # An expired but correctly signed token: TokenHelper reads its exp, finds it
  # spent, and the CLI refreshes before sending — with the cookie just retired.
  STALE_JWT=$(python3 -c "
import base64,hmac,hashlib,json,time
def b64(b): return base64.urlsafe_b64encode(b).rstrip(b'=')
h=b64(json.dumps({'alg':'HS256','typ':'JWT'},separators=(',',':')).encode())
n=int(time.time())
p=b64(json.dumps({'sub':'x','username':'admin','role':'admin','iat':n-3600,'jti':'p','exp':n-60,'sid':'p'},separators=(',',':')).encode())
print((h+b'.'+p+b'.'+b64(hmac.new(b'$DEV_SECRET',h+b'.'+p,hashlib.sha256).digest())).decode())")
  jq --arg t "$STALE_JWT" '.accessToken = $t' "$CONFIG" >"$WORK/stale.json"
  mv "$WORK/stale.json" "$CONFIG"
  rc=0
  "$CLI" systems list >"$WORK/stale.out" 2>"$WORK/stale.err" || rc=$?
  [ "$rc" -eq 2 ] || fail "a retired refresh token should exit 2, got $rc"
  grep -qi "session expired" "$WORK/stale.err" \
    || fail "no readable message for a retired session: $(cat "$WORK/stale.err")"
  grep -q "at GrimoireCli" "$WORK/stale.err" \
    && fail "a retired session leaked a stack trace: $(cat "$WORK/stale.err")"
  [ ! -s "$WORK/stale.out" ] || fail "stdout should stay empty when the session is gone"
  ok "a retired refresh token fails readably with no stack trace"

  # Restore a working session: this script must converge on a re-run, not drift.
  printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
    >/dev/null 2>"$WORK/relogin2.err" \
    || { cat "$WORK/relogin2.err" >&2; fail "login should recover a revoked session"; }
  syslist
  [ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "the CLI should work again after re-login"
  ok "login recovers a revoked session"
fi
```

`syslist` and `$COUNT` / `$EXPECTED_SYSTEMS` are the script's existing helpers —
see the "login repairs a corrupt config" case just above for the same usage.

- [ ] **Step 2: Run the smoke test**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed`, with the two new `ok` lines present.

- [ ] **Step 3: Run it a second time**

```bash
bash docker/smoke-test.sh
```

Expected: identical result. This script must be idempotent — a second run has to
converge, not drift. That is why the case ends by logging in again.

- [ ] **Step 4: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover a retired refresh token end to end"
```

---

## Task 5: Documentation and API coverage

**Files:**
- Modify: `docs/authentication.md`
- Modify: `docs/configuration.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `tools/generate-api-coverage.py:41-70`
- Modify: `CLAUDE.md`
- Generated: `docs/grimoire-api-coverage.md` (never hand-edited)

- [ ] **Step 1: Mark the endpoint implemented**

In `tools/generate-api-coverage.py`, add to `IMPLEMENTED` next to the login row:

```python
    "POST /api/auth/refresh": "🔒 automatic session renewal (all commands)",
```

The lock emoji matches the convention already used for `GET /api/about`: an
endpoint the CLI calls on its own rather than one a named command exposes.

- [ ] **Step 2: Regenerate the coverage table**

```bash
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly one row changes. If the diff is larger, the generator has
picked up an unrelated spec change — report that rather than committing it
silently.

- [ ] **Step 3: Rewrite `docs/authentication.md`**

The "Token Model (Grimoire 1.5.6)" section and numbered auth flow are now wrong.
Rewrite them for 1.6.0, keeping the file's existing structure and heading style:

- Access token 30 minutes (`ACCESS_TOKEN_EXPIRE_MINUTES`, env-overridable),
  refresh token 30 days in the `grimoire_refresh` cookie, `Path=/api/auth`,
  `SameSite=strict`. The refresh token is opaque, not a JWT, so its expiry is not
  locally inspectable.
- **Delete the "No proactive or fallback refresh… a 401 is terminal" item** and
  replace it with the proactive-at-60s and reactive-on-`X-Token-Expired`
  behaviour, and the retry that preserves the request body.
- Record that a session ends routinely — a password change revokes all other
  sessions, an admin edit revokes all of a user's — so `Session expired. Run:
  grimoire-cli login` is normal, not a fault.
- Record that a token supplied by `--token` or `GRIMOIRE_TOKEN` is never
  refreshed, and why.
- Add a short subsection recording the contrast with abs-cli: ABS grants a
  grace period on the previous refresh token (60s in 2.35, 10 minutes and
  configurable in 2.36) so concurrent refreshers cannot break each other;
  Grimoire instead treats a replay as theft and revokes the session, with no
  grace window. State that the CLI issues one request at a time and its callers
  invoke it serially, so the window does not open in practice.
- Update the `login` row's description if it mentions the 30-day token.
- Update the Source Reference list: `RefreshAsync`, `ShouldRefreshProactively`,
  `ShouldRefreshOn401`, `ExtractCookie`.

- [ ] **Step 4: Update `docs/configuration.md`**

Add `refreshToken` to the config-file field list, and note that a token supplied
through `--token` or `GRIMOIRE_TOKEN` is good only for the access token's own 30
minutes on 1.6.0 because no refresh token accompanies it — `login` plus the
config file is the durable path.

- [ ] **Step 5: Record the verified behaviour in `docs/grimoire-api-notes.md`**

Add an entry under the auth material covering, with the observed responses:

- `POST /api/auth/refresh` authenticates on the cookie alone, returns
  `{"token", "user"}`, re-sets both cookies, keeps `sid` stable, and slides
  `expires_at` 30 days forward.
- It tolerates a stale `Authorization` header.
- Replay of a rotated token returns `401 {"detail":"Invalid or expired refresh
  token"}` **and revokes the session**, so the token that replaced it dies too.
- An expired access token yields `401` + `X-Token-Expired: 1` +
  `{"detail":"Token expired - please log in again"}`; a missing or malformed one
  yields no such header.
- Access tokens are not validated against the session table, so revocation does
  not kill tokens already issued.
- Name the build these were measured on:
  `hunterreadca/grimoire:nightly` commit `7f5937071f51dfc65bc09f5e5e49d33c431f0a5d`.

- [ ] **Step 6: Add the `CLAUDE.md` deviation line**

Under "Deliberate deviations today", add one bullet:

```markdown
- **The 401 fallback keys on `X-Token-Expired`, not on any 401.** abs-cli
  refreshes on every 401. Grimoire marks an expired access token with that
  header specifically so it stays distinguishable from "not authenticated" and
  "invalid token", and `POST /api/auth/refresh` is rate-limited, so refreshing
  on a permission denial would spend a request for nothing.
```

- [ ] **Step 7: Confirm no README change is needed**

```bash
grep -n "30 days\|refresh" README.md
```

No command is added or renamed and no user-visible flag changes, so the Commands
table stays as it is. Fix only a factual claim about token lifetime if one is
there.

- [ ] **Step 8: Full pre-PR verification**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. `self-test` alone does not cover the live HTTP path.

- [ ] **Step 9: Commit**

```bash
git add docs CLAUDE.md tools/generate-api-coverage.py
git commit -m "docs: record 1.6.0 session renewal"
```

---

## Self-review notes

Spec sections mapped to tasks: §1 → Task 1; §2 → Task 2; §3 and §4 → Task 3;
§5 → Task 3 (`SessionExpired`, and the null-`RefreshToken` degradation in
Task 1's `Resolve`); §6 → Task 5 Step 3 (documented, not implemented, by
design); Testing → Tasks 3 and 4; Documentation → Task 5.

The known gap the spec names — CI never proving the happy path against a live
server — stands. Task 2 Step 8 and Task 4 are hand-run and scripted checks
against the local stack; neither exercises a real 30-minute expiry.
