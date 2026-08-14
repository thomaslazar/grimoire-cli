# Add-on commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Seven commands over Grimoire's add-on endpoints — `addons list/refresh/install/update/upgrade-all/uninstall/settings` — so the CLI can install and manage the sources the metadata trio will depend on.

**Architecture:** Follows the `books` and `library` groups exactly: an `AddonsService` over the generated Kiota builders, commands as thin declarations, response DTOs on `AppJsonContext`, flag-composed bodies, shapes in `--help-full` from the two generators. Adds a static-file service to the compose stack so the install path is testable without a third party.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client, xUnit, docker compose, bash smoke test.

Spec: [docs/specs/2026-08-14-addons-commands-design.md](../specs/2026-08-14-addons-commands-design.md)

## Global Constraints

- Conventional Commits: `type: subject`, imperative, lowercase, no period, ~72 chars. **No `Co-Authored-By:` lines. No "Generated with Claude Code" attribution.**
- Run `dotnet format GrimoireCli.sln` after writing or modifying C# files.
- No unnecessary blank lines inside method bodies.
- Every type crossing the JSON boundary MUST be registered on `AppJsonContext`, or it fails at runtime under Native AOT rather than at build time.
- **All seven commands call `AddRoleRequired("admin")` and pass `permissionHint: "the admin role"`.**
- `--server` and `--token` are declared per subcommand and threaded into `CommandHelper.BuildClient`.
- **No command registers a request shape.** Every body is flag-composed, per the `library rescan` rule.
- Help text is terse. Notes text in this plan is verbatim — do not reword, expand, or add prose. Never state what a flag description, the subcommand list, or a response shape already shows.
- Comments say what the code does or why it must be so — never what was deliberately left out.
- **Anything that writes goes to the local stack, never the live instance.** The smoke test never points the stack at the community add-on index.
- Work happens on branch `feat/addons-commands`. Never commit to `main`.

---

### Task 0: Branch and design docs

- [ ] **Step 1: Create the branch and commit the docs**

```bash
git checkout -b feat/addons-commands
git add docs/specs/2026-08-14-addons-commands-design.md docs/plans/2026-08-14-addons-commands.md
git commit -m "docs: design the add-on commands"
```

---

### Task 1: Response DTOs

**Files:**
- Create: `src/GrimoireCli/Models/AddonInstalled.cs`, `AddonAvailable.cs`, `AddonListResponse.cs`, `AddonSettings.cs`, `RefreshResult.cs`, `UpgradeAllResult.cs`, `AddonUpgrade.cs`, `AddonUpgradeFailure.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`
- Modify: `tools/GenerateResponseExamples/Program.cs` (`BuildPropertyOverrides`)
- Regenerate: `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Models/AddonDtoTests.cs`

**Interfaces:**
- Produces: `AddonInstalled`, `AddonAvailable`, `AddonListResponse` (`Installed`, `Available`, `IndexUrl`, `DefaultIndexUrl`, `AllowScripts`, `IndexGenerated`), `AddonSettings` (`IndexUrl`, `AllowScripts`), `RefreshResult` (`Status`, `Count`), `UpgradeAllResult` (`Status`, `Updated`, `Failed`), `AddonUpgrade` (`Id`, `From`, `To`), `AddonUpgradeFailure` (`Id`, `Error`). Tasks 3, 4 and 5 consume them.

Field names are the wire names from `temp/grimoire/backend/addons/registry.py:257-282` (`describe`) and `temp/grimoire/backend/routers/addons/core.py:16-63` (the list envelope and the available row). **Read both ranges before writing** — the brief is correct as of v1.5.6, but the source settles a disagreement.

Follow the house style in `src/GrimoireCli/Models/BookSummary.cs`: public class, `[JsonPropertyName("<wire name>")]` on every property. Nullability follows the server: a value the handler always emits is non-nullable, one that can be absent or null is nullable. `describe()` builds a complete dict every time, so its booleans (`requires_script`, `script_approved`, `enabled`, `runnable`, `update_available`) are non-nullable `bool`; strings stay `string?` because a manifest can legitimately omit them.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Models/AddonDtoTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class AddonDtoTests
{
    // Captured from the running stack: GET /api/addons after installing one
    // add-on from the community index.
    [Fact]
    public void AddonListResponseSplitsInstalledFromAvailable()
    {
        const string json = """
        {"installed": [{"id": "ttrpg-wiki", "name": "TTRPG Wiki", "version": "1.0.1",
          "kind": "scraper", "target": "game-system", "requires_script": false,
          "script_approved": false, "enabled": true, "runnable": true,
          "blocked_reason": "", "source": "index", "available_version": "1.0.1",
          "update_available": false}],
         "available": [{"id": "ttrpg-wiki", "name": "TTRPG Wiki", "kind": "scraper",
          "target": "game-system", "version": "1.0.1", "requires_script": false,
          "script_sha256": "abc", "installed": true, "update_available": false}],
         "index_url": "https://example.test/index.json",
         "default_index_url": "https://example.test/index.json",
         "allow_scripts": false, "index_generated": "2026-08-12T03:19:48Z"}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.AddonListResponse)!;
        var installed = Assert.Single(result.Installed!);
        Assert.True(installed.Enabled);
        Assert.True(installed.Runnable);
        Assert.Equal("game-system", installed.Target);
        var available = Assert.Single(result.Available!);
        Assert.Equal("abc", available.ScriptSha256);
        Assert.True(available.Installed);
        Assert.False(result.AllowScripts);
    }

    // An installed-but-blocked add-on is the state that explains an empty
    // metadata-sources list, so the two fields carrying it must survive.
    [Fact]
    public void AddonInstalledCarriesTheBlockedState()
    {
        const string json = """
        {"id": "x", "name": "X", "enabled": true, "runnable": false,
         "blocked_reason": "script not approved", "requires_script": true,
         "script_approved": false, "update_available": false}
        """;
        var addon = JsonSerializer.Deserialize(json, AppJsonContext.Default.AddonInstalled)!;
        Assert.True(addon.Enabled);
        Assert.False(addon.Runnable);
        Assert.Equal("script not approved", addon.BlockedReason);
    }

    [Fact]
    public void UpgradeAllResultReadsBothLists()
    {
        const string json = """
        {"status": "ok",
         "updated": [{"id": "a", "from": "1.0.0", "to": "1.1.0"}],
         "failed": [{"id": "b", "error": "could not reach source"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.UpgradeAllResult)!;
        var upgraded = Assert.Single(result.Updated!);
        Assert.Equal("1.0.0", upgraded.From);
        Assert.Equal("1.1.0", upgraded.To);
        var failure = Assert.Single(result.Failed!);
        Assert.Equal("b", failure.Id);
        Assert.Equal("could not reach source", failure.Error);
    }

    [Fact]
    public void RefreshResultAndSettingsRoundTrip()
    {
        var refresh = JsonSerializer.Deserialize("""{"status": "ok", "count": 2}""",
            AppJsonContext.Default.RefreshResult)!;
        Assert.Equal(2, refresh.Count);
        var settings = JsonSerializer.Deserialize(
            """{"index_url": "https://example.test/index.json", "allow_scripts": true}""",
            AppJsonContext.Default.AddonSettings)!;
        Assert.True(settings.AllowScripts);
        Assert.Equal("https://example.test/index.json", settings.IndexUrl);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter AddonDtoTests`
Expected: FAIL — build error, none of these types exist.

- [ ] **Step 3: Write the DTOs**

`AddonInstalled.cs` — `id`, `name`, `version`, `kind`, `target`, `description`, `homepage`, `attribution`, `blocked_reason`, `source`, `available_version` as `string?`; `requires_script`, `script_approved`, `enabled`, `runnable`, `update_available` as `bool`.

`AddonAvailable.cs` — `id`, `name`, `kind`, `target`, `version`, `description`, `homepage`, `script_sha256` as `string?`; `requires_script`, `installed`, `update_available` as `bool`.

`AddonListResponse.cs` — `installed` as `List<AddonInstalled>?`, `available` as `List<AddonAvailable>?`, `index_url`, `default_index_url`, `index_generated` as `string?`, `allow_scripts` as `bool`.

`AddonSettings.cs` — `index_url` as `string?`, `allow_scripts` as `bool`.

`RefreshResult.cs` — `status` as `string?`, `count` as `int?`.

`UpgradeAllResult.cs` — `status` as `string?`, `updated` as `List<AddonUpgrade>?`, `failed` as `List<AddonUpgradeFailure>?`.

`AddonUpgrade.cs` — `id`, `from`, `to` as `string?`. **`from` is a C# keyword**, so the property is `From` with `[JsonPropertyName("from")]` — which is already how every DTO here maps names, so nothing special is needed beyond not naming the property `from`.

`AddonUpgradeFailure.cs` — `id`, `error` as `string?`. It cannot reuse `BulkError`: that DTO names the field `detail`, this endpoint names it `error`. Say so in a one-line doc comment.

- [ ] **Step 4: Register every new type on `AppJsonContext`**

Add one `[JsonSerializable(typeof(T))]` line per new type to `src/GrimoireCli/Models/JsonContext.cs`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter AddonDtoTests`
Expected: PASS, 4 tests.

- [ ] **Step 6: Add response-example overrides and regenerate**

In `tools/GenerateResponseExamples/Program.cs`'s `BuildPropertyOverrides`, so the samples read as real add-ons rather than `"<string>"`:

```csharp
        o.StringValues[(typeof(AddonInstalled), nameof(AddonInstalled.Kind))] = "scraper";
        o.StringValues[(typeof(AddonInstalled), nameof(AddonInstalled.Target))] = "game-system";
        o.StringValues[(typeof(AddonAvailable), nameof(AddonAvailable.Kind))] = "scraper";
        o.StringValues[(typeof(AddonAvailable), nameof(AddonAvailable.Target))] = "game-system";
```

Then regenerate, or `ResponseExamplesDriftTest` fails:

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
```

- [ ] **Step 7: Format, run the full suite, commit**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tools/GenerateResponseExamples/Program.cs tests/GrimoireCli.Tests
git commit -m "feat: add add-on response dtos"
```

---

### Task 2: `addons list` and `addons refresh`

**Files:**
- Create: `src/GrimoireCli/Services/AddonsService.cs`
- Create: `src/GrimoireCli/Commands/AddonsCommand.cs`
- Modify: `src/GrimoireCli/Program.cs`
- Test: `tests/GrimoireCli.Tests/Commands/AddonsCommandTests.cs`

**Interfaces:**
- Consumes: `AddonListResponse`, `RefreshResult` from Task 1.
- Produces: `AddonsService.ListAsync()` → `Task<AddonListResponse>`; `RefreshAsync()` → `Task<RefreshResult>`; `AddonsCommand.Create()` → `Command`. Tasks 3 and 4 extend both.

Read `src/GrimoireCli/Commands/LibraryCommand.cs` and `Services/LibraryService.cs` first — they are the closest sibling, being the other admin-only group with flag-composed bodies.

Generated builders: `client.Api.Api.Addons` (GET) and `client.Api.Api.Addons.Refresh` (POST, no body — use the parameterless `ToPostRequestInformation`).

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/AddonsCommandTests.cs`. Its `RenderHelp` is a one-line wrapper over the shared helper: `private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(AddonsCommand.Create(), path, full);`

```csharp
    [Fact]
    public void ListShowsBothAddonShapes()
    {
        var output = RenderHelp(["addons", "list"], full: true);
        Assert.Contains("\"installed\":", output);
        Assert.Contains("\"available\":", output);
        Assert.Contains("\"blocked_reason\":", output);
        Assert.Contains("\"script_sha256\":", output);
    }

    // runnable is the field that explains an empty metadata-sources list.
    [Fact]
    public void ListExplainsTheBlockedState()
    {
        var output = RenderHelp(["addons", "list"], full: false);
        Assert.Contains("runnable false", output);
        Assert.Contains("blocked_reason", output);
    }

    [Fact]
    public void RefreshShowsItsCount()
    {
        var output = RenderHelp(["addons", "refresh"], full: true);
        Assert.Contains("\"count\":", output);
    }

```

These cover only the two commands this task adds. The cross-cutting assertions — every verb carrying the admin tag, and no verb registering a request shape — live in Task 4, because they cannot pass until all seven exist and no task should commit a knowingly-red suite.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter AddonsCommandTests`
Expected: FAIL — build error, `AddonsCommand` does not exist.

- [ ] **Step 3: Write the service**

```csharp
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class AddonsService
{
    private readonly GrimoireApiClient _client;

    public AddonsService(GrimoireApiClient client) => _client = client;

    public async Task<AddonListResponse> ListAsync()
    {
        var info = _client.Api.Api.Addons.ToGetRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.AddonListResponse, permissionHint: "the admin role");
    }

    public async Task<RefreshResult> RefreshAsync()
    {
        var info = _client.Api.Api.Addons.Refresh.ToPostRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.RefreshResult, permissionHint: "the admin role");
    }
}
```

- [ ] **Step 4: Write the group and the two commands**

`AddonsCommand.Create()` returns `new Command("addons", "Install and manage metadata add-ons")`.

`list` — description `"List installed and available add-ons"`, `AddResponseExample<AddonListResponse>()`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "available comes from the cached index — empty until addons refresh runs,",
            "and stale afterwards until it runs again. index_generated is when the",
            "cache was built.",
            "",
            "runnable false while enabled is true means the add-on is installed but",
            "blocked; blocked_reason says why. Only runnable add-ons appear as",
            "metadata sources.");
        command.AddExamples("grimoire-cli addons list");
```

`refresh` — description `"Fetch the add-on index"`, `AddResponseExample<RefreshResult>()`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Fetches index_url over the network; count is what the index offered.",
            "",
            "Installing needs a cached index, so a fresh instance runs this first.");
        command.AddExamples("grimoire-cli addons refresh");
```

Both call `AddRoleRequired("admin")`, write with `ConsoleOutput.WriteJson(result, AppJsonContext.Default.<Type>)`, and return 0.

- [ ] **Step 5: Register the group**

In `src/GrimoireCli/Program.cs`, beside the existing group registrations:

```csharp
rootCommand.Subcommands.Add(AddonsCommand.Create());
```

- [ ] **Step 6: Run the tests, format, run the full suite, commit**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter AddonsCommandTests
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add addons list and addons refresh"
```

---

### Task 3: `addons install`, `update` and `uninstall`

**Files:**
- Modify: `src/GrimoireCli/Services/AddonsService.cs`
- Modify: `src/GrimoireCli/Commands/AddonsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/AddonsCommandTests.cs`
- Test: `tests/GrimoireCli.Tests/Services/AddonsServiceTests.cs`

**Interfaces:**
- Produces: `AddonsService.InstallAsync(string id, bool approveScript)` → `Task<AddonInstalled>`; `UpdateAsync(string id, bool? enabled, bool? scriptApproved)` → `Task<AddonInstalled>`; `UninstallAsync(string id)` → `Task<string>`; `AddonsService.BuildUpdateBody(bool? enabled, bool? scriptApproved)` → `Generated.Models.AddonUpdate`, internal and static so the body is assertable without an HTTP round-trip.

Generated builders: `client.Api.Api.Addons[id].Install` (POST, body `AddonInstall`), `client.Api.Api.Addons[id]` (PATCH, body `AddonUpdate`; DELETE).

Three model facts, verified against `src/GrimoireCli/Generated/Models/`:

- `AddonInstall.ApproveScript` is a plain `bool?` **whose constructor sets it to `false`**, matching the server's own default. `--approve-script` is a plain switch, so assigning the flag's value directly is correct — do not null it when absent.
- `AddonUpdate.Enabled` and `.ScriptApproved` are composed wrappers (`AddonUpdate.AddonUpdate_enabled`, `AddonUpdate.AddonUpdate_script_approved`) whose value branch is `Boolean`. The constructor sets neither, so an unassigned one stays absent from the body.
- Assign through the wrapper only when the flag was given: `body.Enabled = new Generated.Models.AddonUpdate.AddonUpdate_enabled { Boolean = enabled.Value };`

- [ ] **Step 1: Write the failing tests**

Add to `AddonsCommandTests.cs`:

```csharp
    [Fact]
    public void InstallDocumentsTheDigestAndTheScriptConsent()
    {
        var output = RenderHelp(["addons", "install"], full: false);
        Assert.Contains("verified against the index's digest", output);
        Assert.Contains("drops back to unapproved", output);
    }

    [Fact]
    public void UpdateSaysItDoesNotChangeVersion()
    {
        var output = RenderHelp(["addons", "update"], full: false);
        Assert.Contains("never version", output);
    }

    // Both are tri-state: omitted must leave the field alone, so a plain switch
    // could set but never clear.
    [Fact]
    public void UpdateTakesTriStateBooleans()
    {
        var output = RenderHelp(["addons", "update"], full: false);
        Assert.Contains("--enabled", output);
        Assert.Contains("--script-approved", output);
        Assert.Contains("true|false", output.Replace(" ", ""));
    }

    [Fact]
    public void UninstallRegistersNoResponseShape()
    {
        Assert.DoesNotContain("Response shape:", RenderHelp(["addons", "uninstall"], full: true));
        Assert.Contains("{\"status\": \"ok\"}", RenderHelp(["addons", "uninstall"], full: false));
    }
```

Create `tests/GrimoireCli.Tests/Services/AddonsServiceTests.cs`, following `LibraryServiceTests`:

```csharp
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class AddonsServiceTests
{
    // An omitted flag must stay absent from the PATCH body: the server ignores
    // what is not sent, and sending a value would clear or set a field the
    // caller never mentioned.
    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = AddonsService.BuildUpdateBody(enabled: null, scriptApproved: null);
        Assert.Null(body.Enabled);
        Assert.Null(body.ScriptApproved);
    }

    [Fact]
    public void GivenFlagsReachTheBodyThroughTheComposedWrapper()
    {
        var body = AddonsService.BuildUpdateBody(enabled: false, scriptApproved: true);
        Assert.False(body.Enabled!.Boolean);
        Assert.True(body.ScriptApproved!.Boolean);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "AddonsCommandTests|AddonsServiceTests"`
Expected: FAIL — the three subcommands and `BuildUpdateBody` do not exist.

- [ ] **Step 3: Add the three service methods**

`InstallAsync` posts `new Generated.Models.AddonInstall { ApproveScript = approveScript }` and returns `AppJsonContext.Default.AddonInstalled`. `UpdateAsync` patches `BuildUpdateBody(...)` and returns the same. `UninstallAsync` sends DELETE and returns the raw response string. All three pass `permissionHint: "the admin role"` and `notFoundHint: "No add-on with that ID. List them with: grimoire-cli addons list"`.

- [ ] **Step 4: Add the three commands**

Each takes `--id` (`Required = true`, description `"Add-on ID"`), `--server`, `--token`, and calls `AddRoleRequired("admin")`.

`install` — description `"Install or upgrade one add-on"`, `AddResponseExample<AddonInstalled>()`, plus:

```csharp
        var approveOption = new Option<bool>("--approve-script")
        {
            Description = "Consent to run this add-on's script; ignored when it ships none",
        };
```

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Takes an id from available in addons list; 400 if the index has no such",
            "entry, 502 if the download fails. The manifest is verified against the",
            "index's digest.",
            "",
            "Also upgrades: re-running on an installed add-on replaces it.",
            "",
            "--approve-script is consent to run third-party code, recorded against",
            "the script's digest and ignored for add-ons that ship no script. An",
            "upgrade that changes the script drops back to unapproved.");
```

`update` — description `"Enable, disable, or approve one add-on"`, `AddResponseExample<AddonInstalled>()`, two tri-state flags built as `new Option<bool?>("--enabled") { Description = "Enable or disable the add-on (true | false)" }` and `new Option<bool?>("--script-approved") { Description = "Grant or revoke script approval (true | false)" }`, plus:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Changes state, never version — upgrade with install or upgrade-all.",
            "",
            "404 if no such add-on is installed.");
```

`uninstall` — description `"Remove one add-on"`, no response shape, plus:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Removes the add-on's directory and forgets its state; reinstall with",
            "addons install --id. 404 if it is not installed.",
            "",
            "Responds {\"status\": \"ok\"}.");
```

Give each a one-line `AddExamples`. `uninstall` writes with `ConsoleOutput.WriteRawJson`; the other two write their typed result.

- [ ] **Step 5: Run the tests, format, run the full suite, commit**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "AddonsCommandTests|AddonsServiceTests"
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add addons install, update and uninstall"
```

---

### Task 4: `addons upgrade-all` and `addons settings`

**Files:**
- Modify: `src/GrimoireCli/Commands/BulkExit.cs`
- Modify: `src/GrimoireCli/Services/AddonsService.cs`
- Modify: `src/GrimoireCli/Commands/AddonsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/AddonsCommandTests.cs`, `tests/GrimoireCli.Tests/Services/AddonsServiceTests.cs`
- Test: wherever `BulkExit` is currently covered

**Interfaces:**
- Produces: `AddonsService.UpgradeAllAsync()` → `Task<UpgradeAllResult>`; `SettingsAsync(string? indexUrl, bool? allowScripts)` → `Task<AddonSettings>`; `AddonsService.BuildSettingsBody(string? indexUrl, bool? allowScripts)` → `Generated.Models.AddonSettingsUpdate`, internal and static.

Generated builders: `client.Api.Api.Addons.UpdateAll` (POST, no body) and `client.Api.Api.Addons.Settings` (PATCH, body `AddonSettingsUpdate`). `AddonSettingsUpdate.AllowScripts` and `.IndexUrl` are composed wrappers whose value branches are `Boolean` and `String`; the constructor sets neither.

- [ ] **Step 1: Write the failing tests**

Add to `AddonsCommandTests.cs`. The first two are the cross-cutting assertions deferred from Task 2 — all seven verbs exist only now:

```csharp
    [Fact]
    public void EveryAddonCommandCarriesTheAdminTag()
    {
        foreach (var verb in new[] { "list", "refresh", "install", "update", "upgrade-all", "uninstall", "settings" })
            Assert.Contains("Role required:\n  admin\n", RenderHelp(["addons", verb], full: false));
    }

    // No add-on body is written by the caller, so none of the seven documents one.
    [Fact]
    public void NoAddonCommandRegistersARequestShape()
    {
        foreach (var verb in new[] { "list", "refresh", "install", "update", "upgrade-all", "uninstall", "settings" })
            Assert.DoesNotContain("Request shape:", RenderHelp(["addons", verb], full: true));
    }

    [Fact]
    public void UpgradeAllDocumentsItsPartialFailure()
    {
        var output = RenderHelp(["addons", "upgrade-all"], full: true);
        Assert.Contains("Exit 3", output);
        Assert.Contains("\"failed\":", output);
        Assert.Contains("not carried over", output);
    }

    [Fact]
    public void SettingsRequiresAFlag()
    {
        var output = RenderHelp(["addons", "settings"], full: false);
        Assert.Contains("At least one flag is required.", output);
        Assert.Contains("does not refetch", output);
    }
```

Add to `AddonsServiceTests.cs`:

```csharp
    [Fact]
    public void OmittedSettingsFlagsLeaveTheBodyEmpty()
    {
        var body = AddonsService.BuildSettingsBody(indexUrl: null, allowScripts: null);
        Assert.Null(body.IndexUrl);
        Assert.Null(body.AllowScripts);
    }

    [Fact]
    public void GivenSettingsFlagsReachTheBody()
    {
        var body = AddonsService.BuildSettingsBody("https://example.test/index.json", allowScripts: true);
        Assert.Equal("https://example.test/index.json", body.IndexUrl!.String);
        Assert.True(body.AllowScripts!.Boolean);
    }
```

Add an exit-code test beside the existing `BulkExit` coverage, asserting a non-empty `failed` list maps to 3 and an empty or null one to 0.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "AddonsCommandTests|AddonsServiceTests|BulkExit"`
Expected: FAIL — the two subcommands and the builders do not exist.

- [ ] **Step 3: Generalise `BulkExit`**

`BulkExit.CodeFor` currently takes `List<BulkError>?`. Add an overload that takes any failure list, so `upgrade-all` shares the rule rather than copying it — `AddonUpgradeFailure` cannot be a `BulkError` because the wire field is `error`, not `detail`. Keep the existing signature working so no current call site changes, and extend the class doc comment to say exit 3 now covers three cases across two groups.

- [ ] **Step 4: Add the two service methods and the two commands**

`upgrade-all` — description `"Upgrade every installed add-on"`, no flags beyond `--server`/`--token`, `AddResponseExample<UpgradeAllResult>()`, action returns the generalised `BulkExit.CodeFor(result.Failed)`, plus:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Refreshes the index first, and carries on with the cached one if that",
            "fails.",
            "",
            "Skip-and-continue: an add-on that cannot be upgraded lands in failed and",
            "the rest still upgrade. Exit 3 is HTTP 200 with a non-empty failed list.",
            "",
            "Script approval is not carried over, so a script-backed add-on is",
            "unapproved until re-approved with install --approve-script.");
```

`settings` — description `"Set the add-on index URL and script switch"`, flags `--index-url` (`Option<string?>`, `"Add-on index URL"`) and `--allow-scripts` (`Option<bool?>`, `"Allow add-on scripts to run (true | false)"`), `AddResponseExample<AddonSettings>()`, plus:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "At least one flag is required.",
            "",
            "Changing --index-url does not refetch; run addons refresh after.",
            "",
            "--allow-scripts is the global switch. An add-on that ships a script also",
            "needs its own approval, from install --approve-script.");
```

The no-flag refusal is a command validator, so it is a parse error (exit 1) before any client is built — the shape `JsonBodyInput.RequireExactlyOneSource` uses:

```csharp
        command.Validators.Add(result =>
        {
            if (result.GetValue(indexUrlOption) is null && result.GetValue(allowScriptsOption) is null)
                result.AddError("Pass --index-url, --allow-scripts, or both.");
        });
```

- [ ] **Step 5: Run the whole suite, format, commit**

All seven verbs now exist, so the two cross-cutting tests added in Step 1 cover the whole group.

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet format GrimoireCli.sln
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add addons upgrade-all and addons settings"
```

---

### Task 5: The fixture index and the smoke test

**Files:**
- Create: `docker/addon-index/fixture-source.yml`
- Create: `docker/make-addon-index.py`
- Modify: `docker/docker-compose.yml`
- Modify: `docker/seed.sh`
- Modify: `docker/smoke-test.sh`
- Modify: `CLAUDE.md` (the reset procedure)

Read `docker/docker-compose.yml`, `docker/seed.sh`, `docker/smoke-test.sh` and `docker/make-fixtures.py` in full first.

**Why a fixture rather than the community index:** the install path is the only part of this group with real machinery — staging directory, digest verification, approval keyed to the script's digest — and it needs an index to install from. Pointing the smoke test at the published index would make every PR build depend on `raw.githubusercontent.com` and a third-party host, and would install third-party content on each run.

- [ ] **Step 1: Write the fixture manifest**

`docker/addon-index/fixture-source.yml`:

```yaml
# Minimal valid add-on for the smoke test. AddonManifest is strict (extra keys
# are rejected) but only id, name, version and kind are required; the fixture
# answers no searches because install, update, upgrade and uninstall never
# consult a source. The metadata-lookup work adds source/search/map here.
id: fixture-source
name: Fixture Source
version: 1.0.0
kind: scraper
target: game-system
description: Local fixture for the grimoire-cli smoke test.
```

- [ ] **Step 2: Write the index generator**

`docker/make-addon-index.py` writes `docker/addon-index/index.json` with the manifest's real digest. The shape is `AddonIndex` wrapping a list of `IndexEntry` (`temp/grimoire/backend/addons/manifest.py:350-375`); both ignore unknown keys, and only `id`, `name`, `version` and `path` are required on an entry:

```json
{
  "version": 1,
  "generated": "<ISO-8601>",
  "addons": [
    {
      "id": "fixture-source",
      "name": "Fixture Source",
      "kind": "scraper",
      "target": "game-system",
      "version": "1.0.0",
      "path": "fixture-source.yml",
      "sha256": "<sha256 of fixture-source.yml>",
      "requires_script": false
    }
  ]
}
```

`path` is resolved with `urljoin(index_url, path)`, so a bare filename beside `index.json` is what makes the fixture self-contained.

The generator must be the only thing that writes `index.json`: install verifies the manifest against the recorded digest, so a hand-edited manifest fails every install with a mismatch. Say that in the file's docstring.

- [ ] **Step 3: Add the static-file service**

In `docker/docker-compose.yml`, alongside `grimoire`:

```yaml
  addon-index:
    image: nginx:alpine
    volumes:
      - ${GRIMOIRE_ADDON_INDEX:-./addon-index}:/usr/share/nginx/html:ro
    networks:
      - grimoire-cli-dev
```

Follow the existing comment conventions in that file, and add the `GRIMOIRE_ADDON_INDEX` host-path note to `docker/.env.example` the way `GRIMOIRE_LIBRARY` and `GRIMOIRE_DATA` are handled — under docker-outside-of-docker the daemon resolves bind mounts against the host.

**The URL is fetched by the grimoire container, not by the smoke test**, so it is `http://addon-index/index.json` — a compose service name. The devcontainer cannot resolve that name and does not need to; `addons refresh` returning a count is what proves the service is reachable.

- [ ] **Step 4: Generate the index during seeding**

Have `docker/seed.sh` run `make-addon-index.py` so a seeded stack always has a valid index available, and so the digest can never drift from the manifest.

- [ ] **Step 5: Add the smoke-test section**

In the existing assertion style. The sequence, which must converge on a second run:

1. `addons settings --index-url http://addon-index/index.json` → `index_url` echoes back.
2. `addons refresh` → `count` is 1.
3. `addons install --id fixture-source` → `id` is `fixture-source`, `enabled` true, `runnable` true.
4. `addons list` → the fixture appears under `installed` with `enabled: true`, and under `available` with `installed: true`.
5. `addons update --id fixture-source --enabled false` → `enabled` false.
6. `addons upgrade-all` → exit 0, `updated` and `failed` both empty. **Assert the empty case honestly**: with one fixture at one version there is nothing to upgrade, so this exercises the plumbing and not the skip-and-continue path. Say so in a comment rather than implying coverage the assertion does not have.
7. `addons uninstall --id fixture-source` → `{"status":"ok"}`, and `addons list` no longer shows it under `installed`.
8. `addons settings --index-url <the default>` → restores the published index URL, so the run leaves no state behind. Take the default from `default_index_url` in `addons list` rather than hard-coding it.
9. `addons settings` with no flags → exits 1, and the error names the flags.

**Never point the stack at the community index** and never call `addons refresh` while it is pointed there.

- [ ] **Step 6: Reset from clean, then run the smoke test twice**

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books docker/addon-index/index.json
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Both runs must pass. A second-run failure means an assertion depends on prior state — fix the assertion, not the ordering.

- [ ] **Step 7: Update the reset procedure and commit**

`CLAUDE.md`'s reset instructions list the directories to remove. Add the generated `index.json` — the fixture manifest is checked in, its index is not.

```bash
git add docker/ CLAUDE.md
git commit -m "test: cover the add-on commands against a local fixture index"
```

---

### Task 6: Documentation

**Files:**
- Modify: `README.md` (Commands table)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/input-output.md`
- Modify: `docs/grimoire-api-notes.md` (only for behaviour the live runs verified)

`docs/roadmap.md` needs **no change**: the roadmap lists intended work and an item leaves when it ships, so work decided and shipped in one branch never lands there. `CHANGELOG.md` belongs to the release process and must not be touched.

- [ ] **Step 1: README Commands table**

Seven rows in the existing format, flags named as declared, `(admin)` suffixed on all seven, `exit 3 if partial` on `upgrade-all`. Read the flags from `src/GrimoireCli/Commands/AddonsCommand.cs` rather than from this plan.

- [ ] **Step 2: API coverage**

Add the seven endpoints to `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate with the stack running:

```bash
python3 tools/generate-api-coverage.py
```

Never hand-edit `docs/grimoire-api-coverage.md`. Verify the `addons` group shows 7/7 before committing.

- [ ] **Step 3: Exit codes**

`docs/input-output.md`'s exit-3 entry names two cases. Add the third — `addons upgrade-all` with a non-empty `failed` list — keeping the entry one paragraph.

- [ ] **Step 4: API notes**

Add only what the live runs settled and the source did not, each with its evidence. Candidates from this work: that an instance never fetches the add-on index until something asks, so `available` is empty on a fresh stack; and that `fetch_json` has no scheme allow-list, which is what makes a local index URL work. Add nothing you cannot point at.

- [ ] **Step 5: Commit**

```bash
git add README.md docs/ tools/generate-api-coverage.py
git commit -m "docs: record the add-on commands"
```

---

### Task 7: Pre-PR verification and PR

- [ ] **Step 1: Run all four checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

- [ ] **Step 2: Read the help output**

```bash
for c in list refresh install update upgrade-all uninstall settings; do
  dotnet run --project src/GrimoireCli -- addons $c --help-full
done
```

Confirm by eye: `Role required: admin` on all seven; no `Request shape` on any; a `Response shape` on all but `uninstall`; and no Notes line repeating a flag description or a shape.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin feat/addons-commands
gh pr create --title "feat: add-on commands" --body "$(cat <<'EOF'
Seven commands over Grimoire's add-on endpoints, so the CLI can install and
manage the sources metadata lookup depends on:

    addons list  refresh  install  update  upgrade-all  uninstall  settings

On a stock instance the add-on index is never fetched and nothing is installed,
so until now the only route from a fresh Grimoire to a working metadata source
was `curl`. The metadata-lookup trio is the next roadmap item and the release
gate; this is its prerequisite.

`PATCH /api/addons/{id}` sets fields and `POST /api/addons/update-all` changes
versions, so the latter is `addons upgrade-all` rather than mirroring the path:
`update` means "change fields" in all three command groups, and version changes
get a word of their own.

`addons upgrade-all` exits 3 on a non-empty `failed` list — the third use of the
established "HTTP 200, but not what you asked for" code.

The smoke test installs from a fixture index served by a static-file service in
the compose stack, so the install path — staging, digest verification, script
approval — is exercised without CI depending on a third-party host.

Design: `docs/specs/2026-08-14-addons-commands-design.md`
EOF
)"
```

- [ ] **Step 4: Watch CI to a terminal state**

`gh pr checks <num> --watch`. Report the result without being asked, and present the PR URL as a clickable link.

---

## Self-Review

**Spec coverage:** seven commands → Tasks 2-4; `upgrade-all` naming → Task 4; eight DTOs → Task 1; no request shapes anywhere → Tasks 2-4 plus Task 2's cross-cutting test; response shapes including the two absences (`uninstall` none, `refresh` present) → Tasks 2-4; tri-state flags versus `--approve-script`'s plain switch → Task 3 Step 3; `settings` requiring a flag → Task 4 Step 4; exit 3 on `upgrade-all` → Task 4 Step 3; every Notes block verbatim → Tasks 2-4; fixture index and smoke test → Task 5; README, coverage, exit-3 entry, api-notes → Task 6.

**Type consistency:** `AddonsService` is created in Task 2 and extended in Tasks 3 and 4; `AddonsCommand.Create()` likewise. `AddonListResponse.Installed`/`.Available`, `AddonInstalled.Enabled`/`.Runnable`/`.BlockedReason`, `RefreshResult.Count`, `UpgradeAllResult.Updated`/`.Failed`, `AddonUpgrade.From`/`.To`, `AddonUpgradeFailure.Error` and `AddonSettings.IndexUrl`/`.AllowScripts` are defined in Task 1 and used with those exact names in Tasks 2-5. `BuildUpdateBody` and `BuildSettingsBody` are defined in Tasks 3 and 4 and asserted by name in the same tasks' service tests.

**No task commits a red suite.** The two cross-cutting assertions — every verb tagged `admin`, no verb registering a request shape — are written in Task 4 rather than Task 2, because they can only pass once all seven commands exist. Every task therefore ends with the full suite green.
