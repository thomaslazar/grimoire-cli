# Systems Write Commands and `me` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the CLI its first write surface — `systems update`, `systems batch-update`, `systems batch-tag` — plus `me`, so an agent can discover whether it is allowed to write before trying.

**Architecture:** Bodies arrive as raw JSON from `--input <file>` or `--stdin`, are validated by deserializing them into hand-written request DTOs that reject unknown keys, and are then sent **unchanged** as the request content. The generated Kiota builders supply only the URL, method and path parameter; their request models never reach the wire. Responses are hand-written DTOs, as everywhere else in this CLI.

**Tech Stack:** C# / .NET 10, System.CommandLine, source-generated `System.Text.Json`, Kiota-generated request builders, Native AOT, xUnit, bash.

**Design spec:** [`docs/specs/2026-08-10-systems-write-commands-design.md`](../specs/2026-08-10-systems-write-commands-design.md). Read it first — §2 is the verified server behaviour, §3.2 is why the generated request models are not used.

## Global Constraints

- **Branch is `feat/systems-write-commands`**, already created off `main` at `3e63d37`. Spec, plan and code all land on it; it reaches `main` through one PR.
- **The build carries zero warnings today. Keep it there.** A new warning is a failed task.
- **Anything that writes goes to the local docker stack, never the live instance.** `Shadowrun 4 DE` is the deliberately-unpatched fixture to target.
- **`CHANGELOG.md` is never touched on a feature branch.**
- **Conventional Commits**, imperative, lowercase, no trailing period, max ~72 chars. No `Co-Authored-By:`, no AI attribution.
- **Run `dotnet format GrimoireCli.sln` after modifying any hand-written C# file.** No blank lines between consecutive option declarations or consecutive `Add*` calls.
- **Never hand-edit `src/GrimoireCli/Generated/`.** Never call a generated execute method (`.GetAsync()`, `.PatchAsync()`); build a `RequestInformation` and send it through `GrimoireApiClient.SendAsync`.
- **Every command that consumes a saved token declares its own `--server` and `--token`** and threads them into `CommandHelper.BuildClient`.
- **Help text is terse** — one-liners, no prose, never restate what a flag description or the response sample already shows. Calibrate against `SystemsCommand.cs`.
- **Role tagging:** all three write commands call `AddRoleRequired("gm or admin")` and pass `permissionHint: "the gm or admin role"`. `me` gets no tag.
- **Exit codes:** 0 applied, 1 client-side refusal, 2 API error, 3 partial bulk application (HTTP 200 with a non-empty `errors`).
- **Every command added updates the README Commands table and `tools/generate-api-coverage.py`'s `IMPLEMENTED` map in the same task** (then regenerate the coverage doc).
- **After adding a DTO to `AppJsonContext`, regenerate `ResponseExamples.g.cs`** or `ResponseExamplesDriftTest` fails:
  ```bash
  dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
  ```

## Verified facts this plan depends on

Measured on 2026-08-11. Do not re-derive; do notice if one stops holding.

| fact | how it was measured | value |
|---|---|---|
| `[JsonUnmappedMemberHandling(Disallow)]` under the source generator | throwaway console app, net10.0 | unknown key throws `JsonException`, `ex.Path == "$.nmae"` |
| the attribute on **nested** element types | same | **does not propagate** — an unknown key inside a child type without the attribute is silently dropped, `Path == "$.strict[0].nmae"` when it has one |
| the attribute on a **derived** type | same | **does not inherit** — a derived class must repeat it |
| C# `required` property under the source generator | same | missing → `JsonException` "was missing required properties including: 'id'"; **`"id": null` is accepted** (present, not non-null) |
| type mismatch | same | `{"year":"soon"}` throws `JsonException`, `Path == "$.year"` |
| `JsonTypeInfo.Properties` under the source generator | same | populated, `Name` is the JSON name (`system_family`) |
| `GameSystemUpdate` (generated) | `Generated/Models/GameSystemUpdate.cs` | `IAdditionalDataHolder` (`:12`), `WriteAdditionalData` (`:221`) — unknown keys are **transmitted**; `Name` is `GameSystemUpdate_name?` (`:100`) |
| routes | `temp/grimoire@v1.5.6` `routers/systems/__init__.py:75,86`, `routers/auth/__init__.py:77` | `PATCH /api/systems/{id}`, **`POST`** `/api/systems/bulk`, **`POST`** `/api/systems/bulk/tags`, `GET /api/auth/me` |
| roles | `backend/auth.py:45` | `("admin", "gm", "player", "guest")`; `require_gm_or_admin` allows admin, gm (`:170`) |
| `me` response | `routers/auth/core.py:177-186`, `models/users.py:23` | 8 fields; `id` is a `String(36)` UUID, `campaign_access`/`allow_explicit`/`oidc_linked` are plain bools |
| bulk responses | `services/bulk_service.py:96,157-161` | `{updated: [id], errors: [{id, detail}]}`; tags adds `tags: {id: [display]}` |
| no tags are seeded | `docker/seed.sh` has no `tags` key | additivity must be proven by two successive `batch-tag` calls |

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/GrimoireCli/Models/MeResponse.cs` | **Create.** `GET /api/auth/me` response | 1 |
| `src/GrimoireCli/Services/AuthService.cs` | **Create.** Builds the `me` request | 1 |
| `src/GrimoireCli/Commands/MeCommand.cs` | **Create.** Top-level `me` | 1 |
| `src/GrimoireCli/Program.cs` | Register `me` | 1 |
| `src/GrimoireCli/Models/GameSystemUpdateRequest.cs` | **Create.** 17 editable fields, strict | 2 |
| `src/GrimoireCli/Models/PublisherEntryRequest.cs`, `LinkEntryRequest.cs` | **Create.** Strict nested entries | 2 |
| `src/GrimoireCli/Commands/JsonBodyInput.cs` | **Create.** `--input`/`--stdin` reading, `JsonException` translation | 3 |
| `src/GrimoireCli/Services/SystemsService.cs` | `UpdateAsync`, `BatchUpdateAsync`, `BatchTagAsync` | 4, 6 |
| `src/GrimoireCli/Commands/SystemsCommand.cs` | `update`, `batch-update`, `batch-tag` subcommands | 4, 6 |
| `src/GrimoireCli/Models/GameSystemBulkItemRequest.cs`, `GameSystemBulkUpdateRequest.cs`, `BulkAddTagsRequest.cs` | **Create.** Bulk request bodies | 5 |
| `src/GrimoireCli/Models/BulkUpdateResult.cs`, `BulkTagResult.cs`, `BulkError.cs` | **Create.** Bulk responses | 5 |
| `src/GrimoireCli/Commands/BulkExit.cs` | **Create.** Exit-3 mapping | 5 |
| `src/GrimoireCli/Models/JsonContext.cs` | Register every new DTO | 1, 2, 5 |
| `tools/GenerateResponseExamples/Program.cs` | Exclude request DTOs from response samples | 2, 5 |
| `src/GrimoireCli/Commands/ResponseExamples.g.cs` | Regenerated, never hand-edited | 1, 5 |
| `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md` | Coverage rows for the four new operations | 1, 4, 6 |
| `README.md` | Commands table rows | 1, 4, 6 |
| `docker/smoke-test.sh` | Live assertions per command | 1, 4, 6 |
| `docs/grimoire-api-notes.md`, `docs/roadmap.md` | Verified behaviour, current state | 7 |

---

## Task 1: `me`

**Files:**
- Create: `src/GrimoireCli/Models/MeResponse.cs`
- Create: `src/GrimoireCli/Services/AuthService.cs`
- Create: `src/GrimoireCli/Commands/MeCommand.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`
- Modify: `src/GrimoireCli/Program.cs:21` (subcommand registration)
- Modify: `src/GrimoireCli/Commands/ResponseExamples.g.cs` (regenerated)
- Modify: `README.md:80` (Commands table), `tools/generate-api-coverage.py:41` (`IMPLEMENTED`)
- Modify: `docker/smoke-test.sh`
- Test: `tests/GrimoireCli.Tests/Models/MeResponseTests.cs`, `tests/GrimoireCli.Tests/Commands/MeCommandTests.cs`

**Interfaces:**
- Produces: `GrimoireCli.Models.MeResponse` (8 properties below); `AuthService(GrimoireApiClient)` with `Task<MeResponse> MeAsync()`; `MeCommand.Create() → Command`.
- Consumes: `CommandHelper.BuildClient(serverOverride, tokenOverride)`, `GrimoireApiClient.SendAsync<T>(RequestInformation, JsonTypeInfo<T>, string?, string?, TimeSpan?)`, `ConsoleOutput.WriteJson<T>`.

- [ ] **Step 1: Write the failing DTO test**

`tests/GrimoireCli.Tests/Models/MeResponseTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class MeResponseTests
{
    [Fact]
    public void DeserializesEveryFieldTheServerSends()
    {
        const string json = """
        {
          "id": "3f1c8e5a-0000-4000-8000-000000000001",
          "username": "admin",
          "display_name": "Admin",
          "email": "admin@example.test",
          "role": "admin",
          "allow_explicit": true,
          "campaign_access": true,
          "oidc_linked": false
        }
        """;
        var me = JsonSerializer.Deserialize(json, AppJsonContext.Default.MeResponse)!;
        Assert.Equal("3f1c8e5a-0000-4000-8000-000000000001", me.Id);
        Assert.Equal("admin", me.Username);
        Assert.Equal("Admin", me.DisplayName);
        Assert.Equal("admin@example.test", me.Email);
        Assert.Equal("admin", me.Role);
        Assert.True(me.AllowExplicit);
        Assert.True(me.CampaignAccess);
        Assert.False(me.OidcLinked);
    }

    // display_name and email are nullable columns; a bare account sends nulls.
    [Fact]
    public void ToleratesNullDisplayNameAndEmail()
    {
        const string json = """
        {"id":"x","username":"gm","display_name":null,"email":null,"role":"gm",
         "allow_explicit":false,"campaign_access":false,"oidc_linked":true}
        """;
        var me = JsonSerializer.Deserialize(json, AppJsonContext.Default.MeResponse)!;
        Assert.Null(me.DisplayName);
        Assert.Null(me.Email);
        Assert.Equal("gm", me.Role);
    }

    // A field a newer Grimoire adds must be ignored, not throw — reads stay lenient.
    [Fact]
    public void IgnoresAnUnknownField()
    {
        const string json = """{"username":"admin","role":"admin","future_field":"x"}""";
        var me = JsonSerializer.Deserialize(json, AppJsonContext.Default.MeResponse)!;
        Assert.Equal("admin", me.Username);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MeResponseTests`
Expected: compile error — `MeResponse` and `AppJsonContext.Default.MeResponse` do not exist.

- [ ] **Step 3: Add the DTO and register it**

`src/GrimoireCli/Models/MeResponse.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class MeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    // One of admin, gm, player, guest (backend/auth.py:45). Writes need gm or admin.
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("allow_explicit")]
    public bool AllowExplicit { get; set; }

    // A bool, not a list: the server collapses the user's campaign_access column
    // to "has access to any campaign" before sending it (routers/auth/core.py:184).
    [JsonPropertyName("campaign_access")]
    public bool CampaignAccess { get; set; }

    [JsonPropertyName("oidc_linked")]
    public bool OidcLinked { get; set; }
}
```

In `src/GrimoireCli/Models/JsonContext.cs`, add `[JsonSerializable(typeof(MeResponse))]` after the `GameSystemDetail` line.

- [ ] **Step 4: Run the DTO tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MeResponseTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Write the failing command test**

`tests/GrimoireCli.Tests/Commands/MeCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MeCommandTests
{
    private static string RenderHelp()
    {
        var root = new RootCommand { MeCommand.Create() };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        root.Parse(new[] { "me", "--help" }).Invoke(new InvocationConfiguration { Output = output });
        return output.ToString();
    }

    [Fact]
    public void TakesNoPositionalArguments()
    {
        var root = new RootCommand { MeCommand.Create() };
        Assert.NotEmpty(root.Parse("me extra").Errors);
    }

    [Fact]
    public void AcceptsServerAndTokenOverrides()
    {
        var root = new RootCommand { MeCommand.Create() };
        Assert.Empty(root.Parse("me --server http://x --token t").Errors);
    }

    // GET /api/auth/me is Depends(get_current_user) — any authenticated user,
    // so tagging it with a role would be a lie.
    [Fact]
    public void HasNoRoleRequiredSection()
    {
        Assert.DoesNotContain("Role required:", RenderHelp());
    }

    [Fact]
    public void HelpMentionsWhatRoleIsFor()
    {
        Assert.Contains("gm or admin", RenderHelp());
    }
}
```

- [ ] **Step 6: Run it and watch it fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MeCommandTests`
Expected: compile error — `MeCommand` does not exist.

- [ ] **Step 7: Add the service**

`src/GrimoireCli/Services/AuthService.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class AuthService
{
    private readonly GrimoireApiClient _client;

    public AuthService(GrimoireApiClient client) => _client = client;

    public async Task<MeResponse> MeAsync()
    {
        var info = _client.Api.Api.Auth.Me.ToGetRequestInformation();
        return await _client.SendAsync(info, AppJsonContext.Default.MeResponse);
    }
}
```

- [ ] **Step 8: Add the command**

`src/GrimoireCli/Commands/MeCommand.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class MeCommand
{
    public static Command Create()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("me", "Show the authenticated account")
        {
            serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "role is admin, gm, player or guest. Writes need gm or admin.",
            "",
            "Called with a bearer token and no cookie, the server sets a session",
            "cookie on the response. The CLI stores no cookies.");
        command.AddExamples(
            "grimoire-cli me",
            "grimoire-cli me | jq -r .role");
        command.AddResponseExample<MeResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var result = await new AuthService(client).MeAsync();
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.MeResponse);
            return 0;
        });
        return command;
    }
}
```

In `src/GrimoireCli/Program.cs`, register it directly after `LoginCommand`:

```csharp
rootCommand.Subcommands.Add(LoginCommand.Create());
rootCommand.Subcommands.Add(MeCommand.Create());
```

- [ ] **Step 9: Regenerate the response samples and run the whole suite**

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build with **zero warnings**; every test passes, including `ResponseExamplesDriftTest` (it will fail if the regeneration was skipped).

- [ ] **Step 10: Update the README table and the coverage map**

`README.md` — add after the `login` row:

```markdown
| `me` | Show the authenticated account (id, username, role, flags) |
```

`tools/generate-api-coverage.py`, in `IMPLEMENTED`:

```python
    "GET /api/auth/me": "`me` ✅",
```

Then regenerate and confirm the row moved from `—`:

```bash
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

- [ ] **Step 11: Add the smoke assertion**

In `docker/smoke-test.sh`, after the login/config checks (before the `systems` section), add:

```bash
# 6b. me: the caller's own account, and the role a write command will need.
ME_JSON=$("$CLI" me 2>"$WORK/me.err") \
  || { cat "$WORK/me.err" >&2; fail "me exited non-zero"; }
[ "$(echo "$ME_JSON" | jq -r .username)" = "admin" ] \
  || fail "me should report username admin: $ME_JSON"
[ "$(echo "$ME_JSON" | jq -r .role)" = "admin" ] \
  || fail "me should report role admin: $ME_JSON"
[ "$(echo "$ME_JSON" | jq -r .id)" != "null" ] || fail "me returned no id: $ME_JSON"
ok "me reports the seeded admin account"
```

- [ ] **Step 12: Run the smoke test**

The stack must be up and seeded (see `CLAUDE.md`). Then:

```bash
bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed`, including the new `ok: me reports the seeded admin account`.

- [ ] **Step 13: Commit**

```bash
git add src/GrimoireCli/Models/MeResponse.cs src/GrimoireCli/Services/AuthService.cs \
        src/GrimoireCli/Commands/MeCommand.cs src/GrimoireCli/Models/JsonContext.cs \
        src/GrimoireCli/Program.cs src/GrimoireCli/Commands/ResponseExamples.g.cs \
        tests/GrimoireCli.Tests/Models/MeResponseTests.cs \
        tests/GrimoireCli.Tests/Commands/MeCommandTests.cs \
        README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docker/smoke-test.sh
git commit -m "feat: add me command"
```

---

## Task 2: Strict request DTOs for a single system update

**Files:**
- Create: `src/GrimoireCli/Models/GameSystemUpdateRequest.cs`
- Create: `src/GrimoireCli/Models/PublisherEntryRequest.cs`
- Create: `src/GrimoireCli/Models/LinkEntryRequest.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`
- Modify: `tools/GenerateResponseExamples/Program.cs:94-97` (the `excluded` set)
- Test: `tests/GrimoireCli.Tests/Models/GameSystemUpdateRequestTests.cs`

**Interfaces:**
- Produces: `GameSystemUpdateRequest` (17 editable fields, no `id`), `PublisherEntryRequest { name, url }`, `LinkEntryRequest { label, url }`, all three carrying `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`; `AppJsonContext.Default.GameSystemUpdateRequest`.
- Consumed by: Task 3's `JsonBodyInput.Validate<T>`, Task 4's `systems update`, Task 5's `GameSystemBulkItemRequest` (which derives from `GameSystemUpdateRequest`).

**Why the nested types are new rather than the existing `PublisherEntry` / `LinkEntry`:** `Disallow` does not propagate into element types (measured — see the facts table), so a typo inside `publishers` would be silently transmitted unless the element type is strict too. Adding `Disallow` to the shared response DTOs instead would make *reads* fail when a newer Grimoire adds a field, which contradicts §8 Q3 of the spec.

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/Models/GameSystemUpdateRequestTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

/// <summary>
/// Grimoire drops unknown keys at pydantic validation and answers {"status":"ok"},
/// so a misspelled field silently changes nothing. These types are what turns that
/// silent no-op into a client-side refusal.
/// </summary>
public class GameSystemUpdateRequestTests
{
    private static GameSystemUpdateRequest Parse(string json)
        => JsonSerializer.Deserialize(json, AppJsonContext.Default.GameSystemUpdateRequest)!;

    [Fact]
    public void AcceptsEveryEditableField()
    {
        const string json = """
        {
          "name": "Shadowrun 4 DE",
          "description": "d",
          "publishers": [{"name": "Pegasus Spiele", "url": ""}],
          "character_builder_url": "https://old",
          "character_builder_urls": [{"label": "Chummer", "url": "https://c"}],
          "urls": [{"label": "Site", "url": "https://s"}],
          "tags": ["cyberpunk"],
          "genre": "Cyberpunk",
          "genres": ["Cyberpunk"],
          "dice_materials": ["d6"],
          "system_family": "Shadowrun",
          "parent_system": "Shadowrun",
          "edition": "4 DE",
          "license": "Proprietary",
          "year": 2009,
          "cover_book_id": "abc",
          "is_explicit": false
        }
        """;
        var req = Parse(json);
        Assert.Equal("Shadowrun 4 DE", req.Name);
        Assert.Equal(2009, req.Year);
        Assert.Equal("Pegasus Spiele", req.Publishers![0].Name);
        Assert.Equal("Chummer", req.CharacterBuilderUrls![0].Label);
        Assert.False(req.IsExplicit);
    }

    [Fact]
    public void RejectsAMisspelledField()
    {
        var ex = Assert.Throws<JsonException>(() => Parse("""{"nmae":"typo"}"""));
        Assert.Equal("$.nmae", ex.Path);
    }

    // id is not editable, so the same check that catches typos catches a body
    // pasted from a systems get dump or a batch-update file.
    [Fact]
    public void RejectsId()
    {
        var ex = Assert.Throws<JsonException>(() => Parse("""{"id":"abc","name":"x"}"""));
        Assert.Equal("$.id", ex.Path);
    }

    // Read-only fields from the 31-field response DTO must not be waved through.
    [Theory]
    [InlineData("book_count")]
    [InlineData("has_cover")]
    [InlineData("child_count")]
    [InlineData("name_is_custom")]
    public void RejectsAReadOnlyResponseField(string field)
    {
        Assert.Throws<JsonException>(() => Parse($"{{\"{field}\": 1}}"));
    }

    [Fact]
    public void RejectsAWrongType()
    {
        var ex = Assert.Throws<JsonException>(() => Parse("""{"year":"soon"}"""));
        Assert.Equal("$.year", ex.Path);
    }

    // Disallow does not propagate into element types, so the nested entry types
    // carry it themselves.
    [Fact]
    public void RejectsAMisspelledFieldInsideAPublisher()
    {
        var ex = Assert.Throws<JsonException>(
            () => Parse("""{"publishers":[{"nmae":"typo"}]}"""));
        Assert.Equal("$.publishers[0].nmae", ex.Path);
    }

    [Fact]
    public void RejectsAMisspelledFieldInsideALinkEntry()
    {
        var ex = Assert.Throws<JsonException>(
            () => Parse("""{"urls":[{"lable":"typo"}]}"""));
        Assert.Equal("$.urls[0].lable", ex.Path);
    }

    // Value rules stay the server's: "" is how a field is cleared, and a blank
    // name is a 422 from Grimoire, not a client-side refusal.
    [Fact]
    public void AcceptsEmptyStringsAndABlankName()
    {
        Assert.Equal("", Parse("""{"system_family":""}""").SystemFamily);
        Assert.Equal("", Parse("""{"name":""}""").Name);
    }

    [Fact]
    public void AcceptsAnEmptyBody()
    {
        Assert.Null(Parse("{}").Name);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter GameSystemUpdateRequestTests`
Expected: compile error — the types do not exist.

- [ ] **Step 3: Add the nested entry types**

`src/GrimoireCli/Models/PublisherEntryRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Request-side publisher entry (routers/systems/_schemas.py:10-12). Separate from
/// <see cref="PublisherEntry"/> because Disallow does not propagate into element
/// types: without a strict type here, a typo inside publishers would be sent.
/// Marking the response DTO strict instead would make reads fail on a field a
/// newer Grimoire adds.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class PublisherEntryRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
```

`src/GrimoireCli/Models/LinkEntryRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Request-side labeled link (routers/systems/_schemas.py:15-19), used by both
/// urls and character_builder_urls. Strict for the reason given on
/// <see cref="PublisherEntryRequest"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class LinkEntryRequest
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
```

- [ ] **Step 4: Add the request DTO**

`src/GrimoireCli/Models/GameSystemUpdateRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Body of PATCH /api/systems/{id} — the 17 editable fields of Grimoire's
/// GameSystemUpdate (routers/systems/_schemas.py:56-75), and nothing else.
/// Deserializing a body into this type is the only check made before sending:
/// Grimoire drops unknown keys and answers {"status":"ok"}, so a misspelled field
/// would otherwise report success having changed nothing. The type is the field
/// list, so there is no separate list to drift.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class GameSystemUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("publishers")]
    public List<PublisherEntryRequest>? Publishers { get; set; }

    // Legacy single; new clients send character_builder_urls.
    [JsonPropertyName("character_builder_url")]
    public string? CharacterBuilderUrl { get; set; }

    [JsonPropertyName("character_builder_urls")]
    public List<LinkEntryRequest>? CharacterBuilderUrls { get; set; }

    [JsonPropertyName("urls")]
    public List<LinkEntryRequest>? Urls { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    // Legacy single; new clients send genres.
    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("dice_materials")]
    public List<string>? DiceMaterials { get; set; }

    [JsonPropertyName("system_family")]
    public string? SystemFamily { get; set; }

    [JsonPropertyName("parent_system")]
    public string? ParentSystem { get; set; }

    [JsonPropertyName("edition")]
    public string? Edition { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("cover_book_id")]
    public string? CoverBookId { get; set; }

    [JsonPropertyName("is_explicit")]
    public bool? IsExplicit { get; set; }
}
```

- [ ] **Step 5: Register the types and keep them out of the response samples**

In `src/GrimoireCli/Models/JsonContext.cs` add, after the response registrations:

```csharp
[JsonSerializable(typeof(GameSystemUpdateRequest))]
[JsonSerializable(typeof(PublisherEntryRequest))]
[JsonSerializable(typeof(LinkEntryRequest))]
```

In `tools/GenerateResponseExamples/Program.cs`, extend the `excluded` set and its comment — request bodies are not response payloads, and `AddResponseExample<T>` must never be able to print one:

```csharp
        //  - request DTOs (GameSystemUpdateRequest and friends) — bodies, not payloads
        var excluded = new HashSet<Type>
        {
            typeof(AppConfig),
            typeof(GameSystemUpdateRequest),
            typeof(PublisherEntryRequest),
            typeof(LinkEntryRequest),
        };
```

- [ ] **Step 6: Run everything**

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
git diff --stat src/GrimoireCli/Commands/ResponseExamples.g.cs
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: `ResponseExamples.g.cs` is **unchanged** (the exclusions cancel the new registrations); build has zero warnings; all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Models tools/GenerateResponseExamples/Program.cs \
        tests/GrimoireCli.Tests/Models/GameSystemUpdateRequestTests.cs
git commit -m "feat: add strict request DTO for system updates"
```

---

## Task 3: Body input and readable validation errors

**Files:**
- Create: `src/GrimoireCli/Commands/JsonBodyInput.cs`
- Test: `tests/GrimoireCli.Tests/Commands/JsonBodyInputTests.cs`

**Interfaces:**
- Consumes: `AppJsonContext.Default.GameSystemUpdateRequest` (Task 2).
- Produces:
  - `class BodyInputException : Exception` — carries a ready-to-print message; the command action catches it and returns 1.
  - `static string JsonBodyInput.Read(string? inputPath, bool useStdin)` — reads the file or all of stdin; throws `BodyInputException` on both/neither/unreadable/empty.
  - `static void JsonBodyInput.Validate<T>(string json, JsonTypeInfo<T> typeInfo, string idHint)` — deserializes to check the shape and discards the result; throws `BodyInputException` with a translated message on `JsonException`. `idHint` is the sentence appended when the offending key is `id`.

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/Commands/JsonBodyInputTests.cs`:

```csharp
using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Commands;

public class JsonBodyInputTests
{
    private const string IdHint = "pass it with --id";

    private static void Validate(string json)
        => JsonBodyInput.Validate(json, AppJsonContext.Default.GameSystemUpdateRequest, IdHint);

    [Fact]
    public void ReadsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"body-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"year":2009}""");
        try
        {
            Assert.Equal("""{"year":2009}""", JsonBodyInput.Read(path, useStdin: false));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsBothSources()
    {
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read("f.json", useStdin: true));
        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public void RejectsNeitherSource()
    {
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(null, useStdin: false));
        Assert.Contains("--input", ex.Message);
        Assert.Contains("--stdin", ex.Message);
    }

    [Fact]
    public void RejectsAMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json");
        var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(missing, useStdin: false));
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void RejectsAnEmptyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "   \n");
        try
        {
            var ex = Assert.Throws<BodyInputException>(() => JsonBodyInput.Read(path, useStdin: false));
            Assert.Contains("empty", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AcceptsAValidBody()
    {
        Validate("""{"system_family":"Shadowrun","year":2009}""");
    }

    [Fact]
    public void NamesTheUnknownFieldAndSuggestsTheNearestMatch()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"system_familly":"x"}"""));
        Assert.Contains("system_familly", ex.Message);
        Assert.Contains("system_family", ex.Message);
    }

    [Fact]
    public void ListsTheAllowedFieldsWhenNothingIsClose()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"zzzzzz":"x"}"""));
        Assert.Contains("zzzzzz", ex.Message);
        Assert.Contains("cover_book_id", ex.Message);
    }

    [Fact]
    public void GivesIdItsOwnAdvice()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"id":"abc"}"""));
        Assert.Contains("'id'", ex.Message);
        Assert.Contains(IdHint, ex.Message);
    }

    [Fact]
    public void ReportsTheJsonPathForANestedUnknownField()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"urls":[{"lable":"x"}]}"""));
        Assert.Contains("$.urls[0].lable", ex.Message);
    }

    [Fact]
    public void ReportsAWrongTypeWithoutASuggestion()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("""{"year":"soon"}"""));
        Assert.Contains("year", ex.Message);
        Assert.DoesNotContain("Did you mean", ex.Message);
    }

    [Fact]
    public void ReportsMalformedJson()
    {
        var ex = Assert.Throws<BodyInputException>(() => Validate("{not json"));
        Assert.Contains("not valid JSON", ex.Message);
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter JsonBodyInputTests`
Expected: compile error — `JsonBodyInput` does not exist.

- [ ] **Step 3: Implement it**

`src/GrimoireCli/Commands/JsonBodyInput.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GrimoireCli.Commands;

/// <summary>A client-side refusal, carrying the message to print before exiting 1.</summary>
public class BodyInputException : Exception
{
    public BodyInputException(string message) : base(message) { }
}

/// <summary>
/// Reads a JSON request body from --input or --stdin and checks its shape against a
/// request DTO. The body is validated by deserializing it and is then sent
/// unchanged, so an explicit "" stays "" and an omitted field stays omitted.
/// </summary>
public static class JsonBodyInput
{
    public static string Read(string? inputPath, bool useStdin)
    {
        if (inputPath != null && useStdin)
            throw new BodyInputException("Provide --input or --stdin, not both.");
        if (inputPath == null && !useStdin)
            throw new BodyInputException("A request body is required. Provide --input <file> or --stdin.");

        string body;
        if (useStdin)
        {
            body = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(body))
                throw new BodyInputException("The request body on stdin is empty.");
        }
        else
        {
            try
            {
                body = File.ReadAllText(inputPath!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BodyInputException($"Could not read {inputPath}: {ex.Message}");
            }
            if (string.IsNullOrWhiteSpace(body))
                throw new BodyInputException($"The request body in {inputPath} is empty.");
        }
        return body;
    }

    /// <summary>
    /// Deserializes to check the shape and discards the result. Unknown keys throw
    /// because the request DTOs carry JsonUnmappedMemberHandling.Disallow — Grimoire
    /// itself drops them and answers success, so this is the only place a typo can
    /// still be caught.
    /// </summary>
    public static void Validate<T>(string json, JsonTypeInfo<T> typeInfo, string idHint)
    {
        try
        {
            JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new BodyInputException(Translate(ex, typeInfo, idHint));
        }
    }

    private static string Translate<T>(JsonException ex, JsonTypeInfo<T> typeInfo, string idHint)
    {
        var key = UnknownKey(ex);
        if (key == null)
            return ex.Path == null
                ? $"The request body is not valid JSON. {ex.Message}"
                : $"The request body is invalid at {ex.Path}. {ex.Message}";

        var message = $"Unknown field '{key}' in the request body at {ex.Path}.";
        if (key == "id")
            return $"{message} 'id' is not an editable field — {idHint}.";

        var allowed = typeInfo.Properties.Select(p => p.Name).ToList();
        var nearest = allowed
            .Select(name => (name, distance: Distance(key, name)))
            .Where(c => c.distance <= 3)
            .OrderBy(c => c.distance)
            .Select(c => c.name)
            .FirstOrDefault();
        return nearest != null
            ? $"{message} Did you mean '{nearest}'?"
            : $"{message} Allowed fields: {string.Join(", ", allowed)}.";
    }

    // The unmapped-member message is the only one whose Path's last segment is a
    // key that does not exist on the type; a type mismatch names a real field and
    // must not be offered a suggestion.
    private static string? UnknownKey(JsonException ex)
    {
        if (ex.Path == null) return null;
        if (!ex.Message.Contains("could not be mapped", StringComparison.Ordinal)) return null;
        var lastDot = ex.Path.LastIndexOf('.');
        return lastDot < 0 ? null : ex.Path[(lastDot + 1)..];
    }

    // Levenshtein, iterative with two rows. Only ever runs on one rejected key
    // against at most 18 field names, so nothing here needs to be clever.
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter JsonBodyInputTests`
Expected: PASS (13 tests). If `ReportsMalformedJson` fails, check the branch chosen for a null `ex.Path` — malformed JSON has no path.

- [ ] **Step 5: Format, build, full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: zero warnings, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/JsonBodyInput.cs \
        tests/GrimoireCli.Tests/Commands/JsonBodyInputTests.cs
git commit -m "feat: read and validate JSON request bodies"
```

---

## Task 4: `systems update`

**Files:**
- Modify: `src/GrimoireCli/Services/SystemsService.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/RoleSectionTests.cs` (its class docstring is now wrong)
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docker/smoke-test.sh`
- Test: `tests/GrimoireCli.Tests/Api/RawBodyRequestTests.cs`, additions to `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs`

**Interfaces:**
- Consumes: `JsonBodyInput.Read/Validate`, `BodyInputException` (Task 3), `AppJsonContext.Default.GameSystemUpdateRequest` (Task 2), `GrimoireApiClient.SendAsync(RequestInformation, string?, string?, TimeSpan?)` (the untyped overload — the response is `{"status":"ok"}` and is passed through raw).
- Produces: `SystemsService.UpdateAsync(string id, string rawBody) → Task<string>`; `systems update` subcommand.

- [ ] **Step 1: Write the failing request-building test**

`tests/GrimoireCli.Tests/Api/RawBodyRequestTests.cs`:

```csharp
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Api;

/// <summary>
/// The generated builder supplies the URL, method and path parameter; the body is
/// the caller's own bytes. These pin that the throwaway generated model used to
/// build the request never reaches the wire.
/// </summary>
public class RawBodyRequestTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static RequestInformation UpdateInfo(string id, string body)
    {
        var info = Client().Api.Api.Systems[id].ToPatchRequestInformation(
            new GrimoireCli.Generated.Models.GameSystemUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/json");
        return info;
    }

    [Fact]
    public void UsesPatchOnTheSystemPath()
    {
        var info = UpdateInfo("sys-1", "{}");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(Method.PATCH, info.HttpMethod);
        Assert.Equal("/api/systems/sys-1", info.URI.AbsolutePath);
    }

    [Fact]
    public void SendsTheCallersBytesVerbatim()
    {
        const string body = """{"system_family":"Shadowrun","description":"a \"quoted\" word"}""";
        var info = UpdateInfo("sys-1", body);
        using var reader = new StreamReader(info.Content!);
        Assert.Equal(body, reader.ReadToEnd());
    }

    [Fact]
    public void SendsJsonContentType()
    {
        var info = UpdateInfo("sys-1", "{}");
        Assert.Contains("application/json", info.Headers["Content-Type"]);
    }

    [Fact]
    public void APathParameterCannotEscapeItsSegment()
    {
        var info = UpdateInfo("../about", "{}");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Contains("..%2Fabout", info.URI.AbsoluteUri);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter RawBodyRequestTests`
Expected: FAIL. `SendsTheCallersBytesVerbatim` and `SendsJsonContentType` are the ones that matter — if `SetStreamContent` does not replace the serialized model, or `Content-Type` ends up doubled or missing, this is where it shows. Fix the service implementation in Step 3 to whatever these prove is needed, then keep the tests as the guard.

- [ ] **Step 3: Add the service method**

In `src/GrimoireCli/Services/SystemsService.cs` — add `using System.Text;` and `using GrimoireCli.Generated.Models;` aliased if needed to avoid colliding with `GrimoireCli.Models`:

```csharp
    /// <summary>
    /// PATCH /api/systems/{id}. The generated builder is used for the URL, method and
    /// path parameter only; its request model would transmit unknown keys
    /// (IAdditionalDataHolder), so the validated raw body replaces the content and
    /// reaches the server byte-for-byte. Returns the raw response — {"status":"ok"},
    /// which confirms nothing about what changed.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Systems[id].ToPatchRequestInformation(
            new Generated.Models.GameSystemUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }
```

- [ ] **Step 4: Run the request tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter RawBodyRequestTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Write the failing command tests**

Append to `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs`:

```csharp
    [Fact]
    public void UpdateRequiresAnId()
    {
        Assert.NotEmpty(Root().Parse("systems update --stdin").Errors);
    }

    [Fact]
    public void UpdateAcceptsEitherInputSource()
    {
        Assert.Empty(Root().Parse("systems update --id x --stdin").Errors);
        Assert.Empty(Root().Parse("systems update --id x --input body.json").Errors);
    }

    [Fact]
    public void UpdateRejectsBothInputSourcesAtParseTime()
    {
        var result = Root().Parse("systems update --id x --stdin --input body.json");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("not both", result.Errors[0].Message);
    }

    [Fact]
    public void UpdateRejectsNeitherInputSourceAtParseTime()
    {
        var result = Root().Parse("systems update --id x");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("--input", result.Errors[0].Message);
    }
```

And append to `tests/GrimoireCli.Tests/Commands/RoleSectionTests.cs` — this is the tag's first real call site, which is what that class exists to guard:

```csharp
    [Fact]
    public void SystemsUpdateCommandHasTheGmOrAdminRoleSection()
    {
        var root = new RootCommand { SystemsCommand.Create() };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        root.Parse(new[] { "systems", "update", "--help" })
            .Invoke(new InvocationConfiguration { Output = output });
        Assert.Contains("Role required:", output.ToString());
        Assert.Contains("gm or admin", output.ToString());
    }
```

`RenderHelp` in that class parses `command.Name --help`, which cannot reach a subcommand, so this test builds its own invocation rather than reusing it.

- [ ] **Step 6: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter SystemsCommandTests`
Expected: FAIL — there is no `update` subcommand.

- [ ] **Step 7: Add the subcommand**

In `src/GrimoireCli/Commands/SystemsCommand.cs`, register it in `Create()` after `CreateGetCommand()`, and add:

```csharp
    /// <summary>
    /// Declares --input / --stdin as mutually exclusive and exactly one required,
    /// as a command validator so the refusal is a parse error (exit 1) before any
    /// client is built.
    /// </summary>
    private static void RequireExactlyOneBodySource(
        Command command, Option<string?> inputOption, Option<bool> stdinOption)
    {
        command.Validators.Add(result =>
        {
            var hasInput = result.GetValue(inputOption) != null;
            var hasStdin = result.GetValue(stdinOption);
            if (hasInput && hasStdin)
                result.AddError("Provide --input or --stdin, not both.");
            else if (!hasInput && !hasStdin)
                result.AddError("A request body is required. Provide --input <file> or --stdin.");
        });
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("update", "Update one game system's metadata")
        {
            idOption, inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        RequireExactlyOneBodySource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body is Grimoire's own field object, without id. Editable fields:",
            "name, description, publishers, character_builder_url,",
            "character_builder_urls, urls, tags, genre, genres, dice_materials,",
            "system_family, parent_system, edition, license, year, cover_book_id,",
            "is_explicit. An unknown field is rejected before the request is made.",
            "",
            "Renaming is permanent: setting name marks it custom, and the scanner",
            "then never re-derives it from the folder again, on any later rescan.",
            "",
            "Clear a field with \"\". An explicit null is dropped server-side and",
            "does nothing.",
            "",
            "genre and character_builder_url are legacy singles; prefer genres",
            "and character_builder_urls.",
            "",
            "Responds {\"status\": \"ok\"} — it does not echo the system, so read",
            "the result back with: grimoire-cli systems get --id <id>");
        command.AddExamples(
            "grimoire-cli systems update --id <id> --input metadata.json",
            "echo '{\"system_family\":\"Shadowrun\"}' | grimoire-cli systems update --id <id> --stdin");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, AppJsonContext.Default.GameSystemUpdateRequest,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new SystemsService(client);
            var response = await service.UpdateAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }
```

`SystemsCommand` has no logger yet — add at the top of the class:

```csharp
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
```

- [ ] **Step 8: Fix the now-false docstring on `RoleSectionTests`**

Its class comment claims `AddRoleRequired` "is currently unused by any real command". Replace that paragraph with:

```csharp
/// <summary>
/// AddRoleRequired's first real call sites are the systems write commands
/// (require_gm_or_admin). These tests exercise the mechanism directly on a
/// throwaway command, the way abs-cli's PermissionSectionTests does, and assert
/// that the commands which need no role carry no tag.
/// </summary>
```

- [ ] **Step 9: Run the whole suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: zero warnings, all tests pass.

- [ ] **Step 10: Update the README table and coverage map**

`README.md`, after the `systems get` row:

```markdown
| `systems update --id <id> {--input <file> \| --stdin}` | Update one system's metadata (gm or admin) |
```

`tools/generate-api-coverage.py`:

```python
    "PATCH /api/systems/{system_id}": "`systems update` ✅",
```

Then `python3 tools/generate-api-coverage.py`.

- [ ] **Step 11: Add the smoke assertions**

At the end of `docker/smoke-test.sh`, before the final `echo`:

```bash
# The first write in this suite. Shadowrun 4 DE is seeded raw for exactly this.
# description is the field used deliberately: no assertion above filters on it,
# so re-running the suite converges instead of drifting. Do NOT write
# system_family here — the "--family Shadowrun should match 2" check depends on
# this system having none.
syslist --include-children
SR4=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 4 DE") | .id')
[ -n "$SR4" ] || fail "no Shadowrun 4 DE fixture to write to"

echo '{"description":"smoke fixture description"}' \
  | "$CLI" systems update --id "$SR4" --stdin >"$WORK/upd.out" 2>"$WORK/upd.err" \
  || { cat "$WORK/upd.err" >&2; fail "systems update exited non-zero"; }
jq -e '.status == "ok"' "$WORK/upd.out" >/dev/null \
  || fail "update should answer {\"status\":\"ok\"}: $(cat "$WORK/upd.out")"
sysget --id "$SR4"
[ "$(echo "$GET_JSON" | jq -r .description)" = "smoke fixture description" ] \
  || fail "the written description did not read back: $(echo "$GET_JSON" | jq -r .description)"
ok "systems update writes a field and systems get reads it back"

# An unknown field is refused client-side: exit 1, and no request is made.
printf '{"descriptoin":"typo"}' >"$WORK/typo.json"
set +e
"$CLI" systems update --id "$SR4" --input "$WORK/typo.json" >/dev/null 2>"$WORK/typo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown field should exit 1, got $rc: $(cat "$WORK/typo.err")"
grep -q "descriptoin" "$WORK/typo.err" || fail "no offending field named: $(cat "$WORK/typo.err")"
grep -q "description" "$WORK/typo.err" || fail "no suggestion offered: $(cat "$WORK/typo.err")"
ok "an unknown field exits 1 before any request"

# Both sources, and neither, are parse-time refusals.
set +e
"$CLI" systems update --id "$SR4" --stdin --input "$WORK/typo.json" >/dev/null 2>"$WORK/both.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "--stdin with --input should exit 1, got $rc"
grep -q "not both" "$WORK/both.err" || fail "no mutual-exclusion message: $(cat "$WORK/both.err")"
set +e
"$CLI" systems update --id "$SR4" >/dev/null 2>"$WORK/none.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "no body source should exit 1, got $rc"
ok "--input and --stdin are mutually exclusive and one is required"
```

If the parse-error exit code turns out not to be 1, do not paper over it in the test — report it, because exit 1 for a client-side refusal is a spec requirement (§3.3) and the fix belongs in the command, not the assertion.

- [ ] **Step 12: Run the smoke test**

```bash
bash docker/smoke-test.sh
bash docker/smoke-test.sh   # again: it must still pass, unchanged, on a written-to fixture
```

Expected: both runs end `smoke: all checks passed`.

- [ ] **Step 13: Commit**

```bash
git add src/GrimoireCli/Services/SystemsService.cs src/GrimoireCli/Commands/SystemsCommand.cs \
        tests/GrimoireCli.Tests README.md tools/generate-api-coverage.py \
        docs/grimoire-api-coverage.md docker/smoke-test.sh
git commit -m "feat: add systems update command"
```

---

## Task 5: Bulk request and response DTOs, and the exit-3 mapping

**Files:**
- Create: `src/GrimoireCli/Models/GameSystemBulkItemRequest.cs`, `GameSystemBulkUpdateRequest.cs`, `BulkAddTagsRequest.cs`
- Create: `src/GrimoireCli/Models/BulkError.cs`, `BulkUpdateResult.cs`, `BulkTagResult.cs`
- Create: `src/GrimoireCli/Commands/BulkExit.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`, `tools/GenerateResponseExamples/Program.cs`, `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Models/BulkRequestTests.cs`, `tests/GrimoireCli.Tests/Models/BulkResultTests.cs`, `tests/GrimoireCli.Tests/Commands/BulkExitTests.cs`

**Interfaces:**
- Produces:
  - `GameSystemBulkItemRequest : GameSystemUpdateRequest` — repeats `[JsonUnmappedMemberHandling(Disallow)]` (it does not inherit) and adds `public required string Id { get; set; }` mapped to `id`.
  - `GameSystemBulkUpdateRequest` — `public required List<GameSystemBulkItemRequest> Items { get; set; }` mapped to `items`.
  - `BulkAddTagsRequest` — `required List<string> Ids` (`ids`), `required List<string> Tags` (`tags`).
  - `BulkError { string? Id, string? Detail }`, `BulkUpdateResult { List<string>? Updated, List<BulkError>? Errors }`, `BulkTagResult : BulkUpdateResult { Dictionary<string, List<string>>? Tags }`.
  - `static int BulkExit.CodeFor(List<BulkError>? errors)` → 3 when non-empty, else 0.
- Consumed by: Task 6's commands.

- [ ] **Step 1: Write the failing request tests**

`tests/GrimoireCli.Tests/Models/BulkRequestTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BulkRequestTests
{
    private static GameSystemBulkUpdateRequest ParseUpdate(string json)
        => JsonSerializer.Deserialize(json, AppJsonContext.Default.GameSystemBulkUpdateRequest)!;

    private static BulkAddTagsRequest ParseTags(string json)
        => JsonSerializer.Deserialize(json, AppJsonContext.Default.BulkAddTagsRequest)!;

    [Fact]
    public void AcceptsItemsCarryingAnIdAndFields()
    {
        var req = ParseUpdate("""{"items":[{"id":"a","year":2009},{"id":"b","genres":["Fantasy"]}]}""");
        Assert.Equal(2, req.Items.Count);
        Assert.Equal("a", req.Items[0].Id);
        Assert.Equal(2009, req.Items[0].Year);
    }

    [Fact]
    public void RequiresItems()
    {
        Assert.Throws<JsonException>(() => ParseUpdate("{}"));
    }

    // The batch item's id is mandatory where the single-item body must not carry
    // one at all — which is why they are separate types.
    [Fact]
    public void RequiresAnIdOnEveryItem()
    {
        Assert.Throws<JsonException>(() => ParseUpdate("""{"items":[{"year":2009}]}"""));
    }

    // Disallow does not inherit, so the derived item type repeats the attribute.
    [Fact]
    public void RejectsAMisspelledFieldInsideAnItem()
    {
        var ex = Assert.Throws<JsonException>(() => ParseUpdate("""{"items":[{"id":"a","yaer":1}]}"""));
        Assert.Equal("$.items[0].yaer", ex.Path);
    }

    [Fact]
    public void RejectsAnUnknownEnvelopeKey()
    {
        var ex = Assert.Throws<JsonException>(() => ParseUpdate("""{"itesm":[]}"""));
        Assert.Equal("$.itesm", ex.Path);
    }

    [Fact]
    public void AcceptsIdsAndTags()
    {
        var req = ParseTags("""{"ids":["a","b"],"tags":["cyberpunk"]}""");
        Assert.Equal(2, req.Ids.Count);
        Assert.Single(req.Tags);
    }

    [Theory]
    [InlineData("""{"ids":["a"]}""")]
    [InlineData("""{"tags":["t"]}""")]
    public void RequiresBothIdsAndTags(string json)
    {
        Assert.Throws<JsonException>(() => ParseTags(json));
    }

    [Fact]
    public void RejectsAnUnknownTagEnvelopeKey()
    {
        Assert.Throws<JsonException>(() => ParseTags("""{"ids":["a"],"tags":["t"],"remove":["x"]}"""));
    }
}
```

Note for the implementer: an **empty** `items`/`ids` array and a **`"id": null`** both pass client-side (`required` means present, not non-empty or non-null) and are rejected by the server — 422 from `Field(..., min_length=1)` and from `id: str`. That is the correct division of labour: the type expresses names and types, the server owns value rules. Do not add client-side length checks.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BulkRequestTests`
Expected: compile error — the types do not exist.

- [ ] **Step 3: Add the request types**

`src/GrimoireCli/Models/GameSystemBulkItemRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// One item of POST /api/systems/bulk: the editable fields plus a required id
/// (routers/_bulk_schemas.py::bulk_update_model). Separate from
/// <see cref="GameSystemUpdateRequest"/> because the single-item body must not
/// carry an id at all — sharing one type would allow it where it is rejected, or
/// reject it where it is mandatory. The attribute is repeated because
/// JsonUnmappedMemberHandling does not inherit.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class GameSystemBulkItemRequest : GameSystemUpdateRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
}
```

`src/GrimoireCli/Models/GameSystemBulkUpdateRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>Body of POST /api/systems/bulk. At most 1000 items (server-enforced).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class GameSystemBulkUpdateRequest
{
    [JsonPropertyName("items")]
    public required List<GameSystemBulkItemRequest> Items { get; set; }
}
```

`src/GrimoireCli/Models/BulkAddTagsRequest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Body of POST /api/systems/bulk/tags. Both lists are required and must be
/// non-empty; ids is capped at 1000 (routers/_bulk_schemas.py::BulkAddTags).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class BulkAddTagsRequest
{
    [JsonPropertyName("ids")]
    public required List<string> Ids { get; set; }

    [JsonPropertyName("tags")]
    public required List<string> Tags { get; set; }
}
```

- [ ] **Step 4: Write the failing response tests**

`tests/GrimoireCli.Tests/Models/BulkResultTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BulkResultTests
{
    [Fact]
    public void ReadsUpdatedAndErrors()
    {
        const string json = """
        {"updated":["a"],"errors":[{"id":"bogus","detail":"System not found"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BulkUpdateResult)!;
        Assert.Equal(["a"], result.Updated);
        Assert.Equal("bogus", result.Errors![0].Id);
        Assert.Equal("System not found", result.Errors[0].Detail);
    }

    [Fact]
    public void ReadsTheTagMapOnTheTagResponse()
    {
        const string json = """
        {"updated":["a"],"errors":[],"tags":{"a":["Cyberpunk","Smoke"]}}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BulkTagResult)!;
        Assert.Empty(result.Errors!);
        Assert.Equal(["Cyberpunk", "Smoke"], result.Tags!["a"]);
    }
}
```

`tests/GrimoireCli.Tests/Commands/BulkExitTests.cs`:

```csharp
using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Commands;

public class BulkExitTests
{
    [Fact]
    public void NoErrorsIsZero() => Assert.Equal(0, BulkExit.CodeFor([]));

    [Fact]
    public void NullErrorsIsZero() => Assert.Equal(0, BulkExit.CodeFor(null));

    // 3, not 2: the request succeeded and some items did not apply. Conflating it
    // with an API error is exactly what an unattended caller cannot afford.
    [Fact]
    public void AnyErrorIsThree()
        => Assert.Equal(3, BulkExit.CodeFor([new BulkError { Id = "x", Detail = "Not found" }]));
}
```

- [ ] **Step 5: Add the response types and the exit mapping**

`src/GrimoireCli/Models/BulkError.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One skipped item from a bulk response (services/bulk_service.py:109).</summary>
public class BulkError
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
```

`src/GrimoireCli/Models/BulkUpdateResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Response of POST /api/systems/bulk. Skip-and-continue: an unresolved id or a
/// rejected item lands in errors while the rest still apply, so a non-empty errors
/// list is a partial application, not a failure. An id in updated means the row
/// resolved, not that any value changed.
/// </summary>
public class BulkUpdateResult
{
    [JsonPropertyName("updated")]
    public List<string>? Updated { get; set; }

    [JsonPropertyName("errors")]
    public List<BulkError>? Errors { get; set; }
}
```

`src/GrimoireCli/Models/BulkTagResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Response of POST /api/systems/bulk/tags. tags maps each updated id to its full
/// display-tag set after the merge, so the caller sees the result without refetching.
/// </summary>
public class BulkTagResult : BulkUpdateResult
{
    [JsonPropertyName("tags")]
    public Dictionary<string, List<string>>? Tags { get; set; }
}
```

`src/GrimoireCli/Commands/BulkExit.cs`:

```csharp
using GrimoireCli.Models;

namespace GrimoireCli.Commands;

/// <summary>
/// Maps a bulk response to an exit code. 3 means HTTP 200 with items skipped —
/// distinct from 2 (the request failed) and 1 (a client-side refusal), because an
/// unattended caller has to tell "nothing was applied" from "most of it was".
/// </summary>
public static class BulkExit
{
    public static int CodeFor(List<BulkError>? errors) => errors is { Count: > 0 } ? 3 : 0;
}
```

- [ ] **Step 6: Register everything and regenerate the samples**

In `src/GrimoireCli/Models/JsonContext.cs`:

```csharp
[JsonSerializable(typeof(BulkUpdateResult))]
[JsonSerializable(typeof(BulkTagResult))]
[JsonSerializable(typeof(BulkError))]
[JsonSerializable(typeof(GameSystemBulkItemRequest))]
[JsonSerializable(typeof(GameSystemBulkUpdateRequest))]
[JsonSerializable(typeof(BulkAddTagsRequest))]
```

In `tools/GenerateResponseExamples/Program.cs`, add the three new **request** types to `excluded` (the three result types are genuine responses and must stay in). Then:

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
```

Expected: the file gains samples for `BulkUpdateResult`, `BulkTagResult` and `BulkError`, and none for the request types.

- [ ] **Step 7: Run the suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: zero warnings, all tests pass. If `required` members produce a warning under the source generator, fix the DTO — do not suppress it.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Models src/GrimoireCli/Commands/BulkExit.cs \
        src/GrimoireCli/Commands/ResponseExamples.g.cs \
        tools/GenerateResponseExamples/Program.cs tests/GrimoireCli.Tests
git commit -m "feat: add bulk request and response types"
```

---

## Task 6: `systems batch-update` and `systems batch-tag`

**Files:**
- Modify: `src/GrimoireCli/Services/SystemsService.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs`
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docker/smoke-test.sh`
- Test: additions to `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs` and `tests/GrimoireCli.Tests/Api/RawBodyRequestTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3 and 5.
- Produces: `SystemsService.BatchUpdateAsync(string rawBody) → Task<BulkUpdateResult>`, `SystemsService.BatchTagAsync(string rawBody) → Task<BulkTagResult>`; `batch-update` and `batch-tag` subcommands.

- [ ] **Step 1: Write the failing request tests**

Append to `tests/GrimoireCli.Tests/Api/RawBodyRequestTests.cs`:

```csharp
    [Fact]
    public void BatchUpdateUsesPostOnTheBulkPath()
    {
        var info = Client().Api.Api.Systems.Bulk.ToPostRequestInformation(
            new GrimoireCli.Generated.Models.GameSystemBulkUpdate());
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(Method.POST, info.HttpMethod);
        Assert.Equal("/api/systems/bulk", info.URI.AbsolutePath);
    }

    [Fact]
    public void BatchTagUsesPostOnTheBulkTagsPath()
    {
        var info = Client().Api.Api.Systems.Bulk.Tags.ToPostRequestInformation(
            new GrimoireCli.Generated.Models.BulkAddTags());
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(Method.POST, info.HttpMethod);
        Assert.Equal("/api/systems/bulk/tags", info.URI.AbsolutePath);
    }
```

- [ ] **Step 2: Run them and watch them fail or pass**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter RawBodyRequestTests`
Expected: PASS immediately — these assert generated-builder facts, not new code. If a path or method differs from the assertion, the assertion is wrong and the service must follow the builder; re-read `Generated/Api/Systems/Bulk/`.

- [ ] **Step 3: Add the service methods**

In `src/GrimoireCli/Services/SystemsService.cs`:

```csharp
    /// <summary>
    /// POST /api/systems/bulk. One transaction, skip-and-continue: an unresolved id
    /// or a rejected item goes to errors and the rest still apply. Tag creation is
    /// serialised here, which per-item concurrent PATCHes could not do.
    /// </summary>
    public async Task<BulkUpdateResult> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Systems.Bulk.ToPostRequestInformation(
            new Generated.Models.GameSystemBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BulkUpdateResult,
            permissionHint: "the gm or admin role");
    }

    /// <summary>POST /api/systems/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<BulkTagResult> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Systems.Bulk.Tags.ToPostRequestInformation(
            new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BulkTagResult,
            permissionHint: "the gm or admin role");
    }
```

- [ ] **Step 4: Write the failing command tests**

Append to `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs`:

```csharp
    [Theory]
    [InlineData("systems batch-update --stdin")]
    [InlineData("systems batch-update --input items.json")]
    [InlineData("systems batch-tag --stdin")]
    [InlineData("systems batch-tag --input tags.json")]
    public void BatchCommandsAcceptEitherInputSource(string input)
    {
        Assert.Empty(Root().Parse(input).Errors);
    }

    [Theory]
    [InlineData("systems batch-update")]
    [InlineData("systems batch-tag")]
    [InlineData("systems batch-update --stdin --input items.json")]
    [InlineData("systems batch-tag --stdin --input tags.json")]
    public void BatchCommandsRequireExactlyOneInputSource(string input)
    {
        Assert.NotEmpty(Root().Parse(input).Errors);
    }

    // Neither takes --id: the ids are in the body.
    [Theory]
    [InlineData("systems batch-update --stdin --id x")]
    [InlineData("systems batch-tag --stdin --id x")]
    public void BatchCommandsTakeNoIdFlag(string input)
    {
        Assert.NotEmpty(Root().Parse(input).Errors);
    }
```

- [ ] **Step 5: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter SystemsCommandTests`
Expected: FAIL — the subcommands do not exist.

- [ ] **Step 6: Add both subcommands**

In `src/GrimoireCli/Commands/SystemsCommand.cs`, register both in `Create()` after `update`, and add:

```csharp
    private static Command CreateBatchUpdateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("batch-update", "Update many game systems in one transaction")
        {
            inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        RequireExactlyOneBodySource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body is {\"items\": [{\"id\": \"…\", …fields}]}, at most 1000 items;",
            "fields are those of: grimoire-cli systems update --help",
            "",
            "Skip-and-continue: an unresolved id or a rejected item lands in",
            "errors and the rest still apply. Exit 3 means HTTP 200 with a",
            "non-empty errors list — a partial application, not a failure.",
            "",
            "updated reports ids, not fields: an id there means the row resolved,",
            "not that any value changed.",
            "",
            "Renaming is permanent, and \"\" not null clears a field — same as",
            "systems update.");
        command.AddExamples(
            "grimoire-cli systems batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli systems batch-update --stdin");
        command.AddResponseExample<BulkUpdateResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, AppJsonContext.Default.GameSystemBulkUpdateRequest,
                    "put it in each item");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var result = await new SystemsService(client).BatchUpdateAsync(body);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BulkUpdateResult);
            return BulkExit.CodeFor(result.Errors);
        });
        return command;
    }

    private static Command CreateBatchTagCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("batch-tag", "Add tags to many game systems")
        {
            inputOption, stdinOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        RequireExactlyOneBodySource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Body is {\"ids\": [\"…\"], \"tags\": [\"…\"]}, both non-empty, at most",
            "1000 ids.",
            "",
            "Additive only: it merges with each system's existing tags and never",
            "removes one. To replace a tag set, use batch-update with tags.",
            "",
            "Exit 3 means HTTP 200 with a non-empty errors list — some ids did",
            "not resolve while the rest were tagged.");
        command.AddExamples(
            "grimoire-cli systems batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"cyberpunk\"]}' | grimoire-cli systems batch-tag --stdin");
        command.AddResponseExample<BulkTagResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, AppJsonContext.Default.BulkAddTagsRequest,
                    "put it in ids");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var result = await new SystemsService(client).BatchTagAsync(body);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.BulkTagResult);
            return BulkExit.CodeFor(result.Errors);
        });
        return command;
    }
```

- [ ] **Step 7: Run the suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: zero warnings, all tests pass.

- [ ] **Step 8: Update the README table and the coverage map**

`README.md`, after the `systems update` row:

```markdown
| `systems batch-update {--input <file> \| --stdin}` | Update many systems in one transaction; exit 3 if partial (gm or admin) |
| `systems batch-tag {--input <file> \| --stdin}` | Add tags to many systems, additively; exit 3 if partial (gm or admin) |
```

`tools/generate-api-coverage.py`:

```python
    "POST /api/systems/bulk": "`systems batch-update` ✅",
    "POST /api/systems/bulk/tags": "`systems batch-tag` ✅",
```

Then `python3 tools/generate-api-coverage.py`.

- [ ] **Step 9: Add the smoke assertions**

At the end of `docker/smoke-test.sh`, after the `systems update` block:

```bash
# batch-update: one good id and one bogus id must exit 3, applying the good one.
# license, not description or system_family: no assertion above filters on a
# license other than OGL, so this stays idempotent across re-runs.
cat >"$WORK/batch.json" <<JSON
{"items":[{"id":"$SR4","license":"Smoke Fixture License"},
          {"id":"no-such-id","license":"x"}]}
JSON
set +e
"$CLI" systems batch-update --input "$WORK/batch.json" >"$WORK/batch.out" 2>"$WORK/batch.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "a partial batch should exit 3, got $rc: $(cat "$WORK/batch.err")"
jq -e --arg id "$SR4" '.updated | index($id) != null' "$WORK/batch.out" >/dev/null \
  || fail "the good id should be in updated: $(cat "$WORK/batch.out")"
jq -e '.errors | length == 1 and .[0].id == "no-such-id"' "$WORK/batch.out" >/dev/null \
  || fail "the bogus id should be the only error: $(cat "$WORK/batch.out")"
ok "batch-update applies the good id and exits 3 on a partial"

# A fully-applying batch exits 0.
echo "{\"items\":[{\"id\":\"$SR4\",\"license\":\"Smoke Fixture License\"}]}" \
  | "$CLI" systems batch-update --stdin >"$WORK/batch2.out" 2>"$WORK/batch2.err" \
  || { cat "$WORK/batch2.err" >&2; fail "a fully-applying batch should exit 0"; }
jq -e '.errors | length == 0' "$WORK/batch2.out" >/dev/null \
  || fail "no errors expected: $(cat "$WORK/batch2.out")"
ok "batch-update exits 0 when every item applies"

# batch-tag is additive: the second call must not displace the first tag.
echo "{\"ids\":[\"$SR4\"],\"tags\":[\"smoke-alpha\"]}" \
  | "$CLI" systems batch-tag --stdin >"$WORK/tag1.out" 2>"$WORK/tag1.err" \
  || { cat "$WORK/tag1.err" >&2; fail "batch-tag exited non-zero"; }
echo "{\"ids\":[\"$SR4\"],\"tags\":[\"smoke-beta\"]}" \
  | "$CLI" systems batch-tag --stdin >"$WORK/tag2.out" 2>"$WORK/tag2.err" \
  || { cat "$WORK/tag2.err" >&2; fail "the second batch-tag exited non-zero"; }
jq -e --arg id "$SR4" '.tags[$id] | index("smoke-alpha") != null and index("smoke-beta") != null' \
  "$WORK/tag2.out" >/dev/null \
  || fail "batch-tag should have merged both tags: $(cat "$WORK/tag2.out")"
sysget --id "$SR4"
echo "$GET_JSON" | jq -e '.tags | index("smoke-alpha") != null' >/dev/null \
  || fail "the first tag did not survive the second call: $(echo "$GET_JSON" | jq -c .tags)"
ok "batch-tag adds a tag and leaves the existing one in place"

# A bogus id alone is still exit 3, and no ids resolve.
echo '{"ids":["no-such-id"],"tags":["smoke-alpha"]}' \
  >"$WORK/tagbad.json"
set +e
"$CLI" systems batch-tag --input "$WORK/tagbad.json" >"$WORK/tagbad.out" 2>"$WORK/tagbad.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "an all-bogus batch-tag should exit 3, got $rc"
ok "batch-tag exits 3 when an id does not resolve"

# An unknown key in a batch item is refused client-side.
printf '{"items":[{"id":"%s","licence":"typo"}]}' "$SR4" >"$WORK/batchtypo.json"
set +e
"$CLI" systems batch-update --input "$WORK/batchtypo.json" >/dev/null 2>"$WORK/batchtypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown field in an item should exit 1, got $rc"
grep -q "licence" "$WORK/batchtypo.err" || fail "no offending field named: $(cat "$WORK/batchtypo.err")"
ok "an unknown field inside a batch item exits 1"
```

- [ ] **Step 10: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: both runs pass. The second run is the idempotency check — tags merge to the same set and licences overwrite with the same value.

- [ ] **Step 11: Commit**

```bash
git add src/GrimoireCli tests/GrimoireCli.Tests README.md \
        tools/generate-api-coverage.py docs/grimoire-api-coverage.md docker/smoke-test.sh
git commit -m "feat: add systems batch-update and batch-tag commands"
```

---

## Task 7: Docs, full verification, PR

**Files:**
- Modify: `docs/grimoire-api-notes.md`, `docs/roadmap.md`
- Verify: everything

- [ ] **Step 1: Record the verified write behaviour**

In `docs/grimoire-api-notes.md`, add a section covering what a future reader would otherwise have to re-derive from the server source, each with its citation:

- `PATCH /api/systems/{id}` answers `{"status":"ok"}` and echoes nothing (`routers/systems/core.py:311`).
- Unknown keys are dropped by pydantic and never surface as an error (`:302`); explicit nulls are dropped by `model_dump(exclude_none=True)`, so `""` is the only way to clear a field.
- A rename sets `name_is_custom` permanently, after which the scanner stops re-deriving the name (`:334`, `indexer/scan.py:358`); renaming to the same value returns early and does **not** set the flag (`:325-326`).
- Bulk is skip-and-continue, commits once and only if at least one item applied, and caps at `MAX_BULK_ITEMS = 1000` (`services/bulk_service.py:96`, `:38`).
- `bulk/tags` merges and never removes, and returns the full display-tag set per updated id (`:157-161`).
- `GET /api/auth/me` sets a session cookie when called with a bearer token and no cookie, reusing the existing token rather than minting one (`routers/auth/core.py:167-170`).

- [ ] **Step 2: Update the roadmap**

In `docs/roadmap.md`, move the systems write commands and `me` from open work to done, and state what the next increment is (books have no commands at all; the systems cover, book-folders and metadata-lookup endpoints remain).

- [ ] **Step 3: Run all four pre-PR gates**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report the actual test count and the smoke `ok:` count.

- [ ] **Step 4: Check the published AOT binary too**

The Debug build is not what ships; trimming is where a source-generated context or a reflection-dependent path fails.

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -o publish
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
./publish/grimoire-cli me --help-full
./publish/grimoire-cli systems update --help
```

Expected: zero IL trim warnings on publish, the smoke test passes against the published binary, and both help screens render their Notes, Role required and response-shape sections.

- [ ] **Step 5: Verify the help text against the rules**

Read `--help` for `me`, `systems update`, `systems batch-update` and `systems batch-tag` as an agent would. Each Notes block must state only non-obvious, outcome-affecting things: no restating a flag description, no narrating the subcommand list, no selling another command. Every caveat in the spec's §4 must appear at exactly one call site.

- [ ] **Step 6: Commit the docs**

```bash
git add docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record verified write semantics and roadmap state"
```

- [ ] **Step 7: Push and open the PR**

```bash
git push -u origin feat/systems-write-commands
gh pr create --title "feat: systems write commands and me" --body "$(cat <<'EOF'
## What

First write surface for the CLI: `systems update`, `systems batch-update`,
`systems batch-tag`, plus `me` so an agent can discover its role before writing.

Design: `docs/specs/2026-08-10-systems-write-commands-design.md` (§3.2 revised
against the generated client). Plan: `docs/plans/2026-08-11-systems-write-commands.md`.

## Notes for review

- Bodies come from `--input` or `--stdin`, exactly one required, and are validated
  by deserializing into strict request DTOs, then sent **unchanged**. The generated
  Kiota request models are not used: they are `IAdditionalDataHolder`, so a typo'd
  field would be transmitted rather than rejected.
- `JsonUnmappedMemberHandling.Disallow` neither inherits nor propagates into nested
  types — measured, hence the separate strict entry and item types.
- Exit 3 is new: HTTP 200 with a non-empty `errors` list, distinct from 1
  (client-side refusal) and 2 (API error).
- First real `AddRoleRequired("gm or admin")` call sites.

## Verification

Four pre-PR gates plus the published AOT binary; the smoke test runs twice to prove
it stays idempotent now that it writes.
EOF
)"
```

Then present the PR URL as a clickable link and watch CI to a terminal state:

```bash
gh pr checks --watch
```

Report the result without being asked. A PR is done at "all checks green", not at "PR open".

---

## Self-review notes

Checked against the spec:

- §3 four commands → Tasks 1, 4, 6. §3.1 input contract → Task 3 + the validator in Task 4. §3.2 request DTOs → Tasks 2, 5. §3.3 exit codes → `BulkExit` (Task 5), the `BodyInputException` catch (Tasks 4, 6), `EnsureSuccessAsync` (already exit 2). §3.4 role tagging → Tasks 4, 6, with the `RoleSectionTests` docstring corrected in Task 4.
- §4's six caveats: rename permanence, `""` not `null`, legacy singles, and the no-echo response are in `systems update`'s Notes; additive-only is in `batch-tag`'s; ids-not-fields is in `batch-update`'s; the `me` cookie side effect is in `me`'s.
- §5 sequencing is preserved: `me` (Task 1), `systems update` (Tasks 2-4), the batch pair (Tasks 5-6).
- §7's six smoke additions are all present, plus two the spec did not ask for: a fully-applying batch exiting 0, and the second smoke run that proves idempotency now that the suite writes.
- §6 out of scope is respected: no books, no cover/book-folders/metadata endpoints, no `logout`, no cross-endpoint orchestration.
