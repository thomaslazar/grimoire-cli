# Backup commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `backups list|create|delete|download` and `backups settings get|set` — the six admin-only backup endpoints, so a backup can precede the destructive `files` block that follows.

**Architecture:** One command group in `BackupsCommand.cs` with the nested `settings` pair split into `BackupSettingsCommands.cs`, over one `BackupsService.cs`. `OptionHelpers` gains a `Range` sibling to `Choice`, because the server silently clamps the numeric settings fields instead of erroring.

**Tech Stack:** C# / .NET 10, `System.CommandLine`, Kiota-generated API client, xUnit, bash smoke test.

**Design spec:** [docs/specs/2026-09-04-backup-commands-design.md](../specs/2026-09-04-backup-commands-design.md)

## Global Constraints

- **Branch:** `feat/backup-commands`. Never commit to `main`.
- **Conventional Commits**, `type: subject`, imperative, lowercase, no period, max ~72 chars. **No `Co-Authored-By:` lines. No "Generated with Claude Code" attribution** anywhere.
- **Every command in this plan calls `command.AddRoleRequired("admin")`** and every service call passes `permissionHint: "the admin role"`. All six routes are `require_admin` in `temp/grimoire/backend/routers/backups/core.py`. The tag and the hint must agree.
- **Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.** CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** inside method bodies: none between consecutive `Subcommands.Add` calls or consecutive option declarations, none before a `return` that follows setup calls.
- **stdout is the server's bytes** via `ConsoleOutput.WriteRawJson`; logs go to stderr. `download` uses `ConsoleOutput.WriteStreamAsync`.
- **Thin pass-through**, with one stated exception: the numeric `settings set` flags are range-checked client-side (see the spec's "Client-side range rejection"). Nothing else may read a response, pre-fetch, or mirror server policy — in particular, `settings set` must NOT call `settings get` first to pre-empt a 400 on an env-locked field.
- **`CHANGELOG.md` is owned by the release process.** Do not touch it.
- **`docs/grimoire-api-coverage.md` is generated.** Edit `IMPLEMENTED` in `tools/generate-api-coverage.py` and regenerate; never hand-edit the markdown.
- **Anything that writes goes to the local stack, never a live instance.**
- Verified against `hunterreadca/grimoire:1.6.1`; the backups router is byte-identical between `v1.6.0` and `v1.6.1`.

---

## File Structure

- **Modify** `src/GrimoireCli/Commands/OptionHelpers.cs` — add `Range`.
- **Create** `src/GrimoireCli/Services/BackupsService.cs` — the six calls plus `BuildSettingsBody`.
- **Create** `src/GrimoireCli/Commands/BackupsCommand.cs` — `list`, `create`, `delete`, `download`, and hosting the `settings` subgroup.
- **Create** `src/GrimoireCli/Commands/BackupSettingsCommands.cs` — `settings get|set`.
- **Modify** `src/GrimoireCli/Program.cs` — register the group.
- **Create** `tests/GrimoireCli.Tests/Services/BackupsServiceTests.cs`.
- **Create** `tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs`.
- **Modify** `tests/GrimoireCli.Tests/Commands/OptionHelpersTests.cs` if it exists, else create it.
- **Modify** `docker/smoke-test.sh`, `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-notes.md`, `docs/cli-design.md`, `docs/roadmap.md`.

---

### Task 0: Commit the spec and plan

**Files:** commit `docs/specs/2026-09-04-backup-commands-design.md` (staged) and `docs/plans/2026-09-04-backup-commands.md`.

- [ ] **Step 1: Confirm branch and tree**

```bash
git rev-parse --abbrev-ref HEAD   # must print feat/backup-commands
git status --short
```

Expected: the spec staged (`A`), the plan untracked (`??`). Nothing else.

- [ ] **Step 2: Commit**

```bash
git add docs/specs/2026-09-04-backup-commands-design.md docs/plans/2026-09-04-backup-commands.md
git commit -m "docs: design the backup commands"
```

---

### Task 1: `OptionHelpers.Range`

**Files:**
- Modify: `src/GrimoireCli/Commands/OptionHelpers.cs`
- Test: `tests/GrimoireCli.Tests/Commands/OptionHelpersTests.cs`

**Interfaces:**
- Produces: `public static Option<int?> Range(string name, string description, int min, int? max = null)`. `max` null means "floor only, no ceiling". Used by Task 4.

- [ ] **Step 1: Write the failing test**

Create (or append to) `tests/GrimoireCli.Tests/Commands/OptionHelpersTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// Range rejects at parse time what the server would silently clamp
/// (routers/backups/core.py stores max(0, min(23, hour)) and answers 200), so
/// these pin the boundaries rather than the message.
/// </summary>
public class OptionHelpersTests
{
    private static ParseResult Parse(Option<int?> option, params string[] args)
    {
        var command = new Command("demo") { option };
        return command.Parse(args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(7)]
    public void RangeAcceptsValuesInsideItsBounds(int value)
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        Assert.Empty(Parse(option, "--hour", value.ToString()).Errors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(99)]
    public void RangeRejectsValuesOutsideItsBounds(int value)
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        Assert.NotEmpty(Parse(option, "--hour", value.ToString()).Errors);
    }

    [Fact]
    public void RangeErrorNamesTheOptionAndTheBounds()
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        var error = Assert.Single(Parse(option, "--hour", "99").Errors);
        Assert.Contains("--hour", error.Message);
        Assert.Contains("0", error.Message);
        Assert.Contains("23", error.Message);
    }

    // The two retention fields have a floor and no ceiling: the server applies
    // max(0, value) and nothing else.
    [Theory]
    [InlineData(0)]
    [InlineData(500000)]
    public void RangeWithoutAMaxAcceptsAnyValueAtOrAboveTheFloor(int value)
    {
        var option = OptionHelpers.Range("--retention-count", "Count", 0);
        Assert.Empty(Parse(option, "--retention-count", value.ToString()).Errors);
    }

    [Fact]
    public void RangeWithoutAMaxStillRejectsBelowTheFloor()
    {
        var option = OptionHelpers.Range("--retention-count", "Count", 0);
        Assert.NotEmpty(Parse(option, "--retention-count", "-1").Errors);
    }

    [Fact]
    public void AnOmittedRangeOptionIsNotAnError()
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        var parsed = Parse(option);
        Assert.Empty(parsed.Errors);
        Assert.Null(parsed.GetValue(option));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter OptionHelpersTests`

Expected: build failure — `OptionHelpers.Range` does not exist.

- [ ] **Step 3: Add `Range` to `src/GrimoireCli/Commands/OptionHelpers.cs`**

Add this method after the existing `Choice`:

```csharp
    /// <summary>
    /// An integer option constrained to a range, rejected at parse time. The
    /// backup settings fields it guards are clamped by the server rather than
    /// refused — `routers/backups/core.py` stores `max(0, min(23, hour))` and
    /// answers 200 — so an out-of-range value would otherwise be silently
    /// stored as a different value. <paramref name="max"/> is null for a field
    /// with a floor and no ceiling.
    /// </summary>
    public static Option<int?> Range(string name, string description, int min, int? max = null)
    {
        var option = new Option<int?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<int?>();
            if (value is null) return;
            if (value < min || (max is not null && value > max))
                result.AddError(max is null
                    ? $"'{value}' is not a valid value for {name}. Must be {min} or greater."
                    : $"'{value}' is not a valid value for {name}. Must be between {min} and {max}.");
        });
        return option;
    }
```

- [ ] **Step 4: Format, then run the test to verify it passes**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter OptionHelpersTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/OptionHelpers.cs tests/GrimoireCli.Tests/Commands/OptionHelpersTests.cs
git commit -m "feat: add a range-constrained int option helper"
```

---

### Task 2: `BackupsService`

**Files:**
- Create: `src/GrimoireCli/Services/BackupsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/BackupsServiceTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient` — `Task<string> SendAsync(RequestInformation, string? permissionHint, string? notFoundHint, TimeSpan?)`, `Task<Stream> SendStreamAsync(RequestInformation, string? permissionHint, string? notFoundHint, TimeSpan?)`, and `.Api.Api.Backups`, `.Api.Api.Backups.Settings`, `.Api.Api.Backups[id]`, `.Api.Api.Backups[id].Download`.
- Produces, all used by Tasks 3 and 4:
  - `BackupsService(GrimoireApiClient client)`
  - `Task<string> ListAsync()`
  - `Task<string> CreateAsync()`
  - `Task<string> DeleteAsync(string id)`
  - `Task<Stream> DownloadAsync(string id)`
  - `Task<string> SettingsAsync()`
  - `Task<string> UpdateSettingsAsync(string? schedule, int? hour, int? minute, int? weekday, int? retentionCount, int? retentionGb, string? dir)`
  - `internal static Generated.Models.BackupSettingsPatch BuildSettingsBody(string? schedule, int? hour, int? minute, int? weekday, int? retentionCount, int? retentionGb, string? dir)`

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Services/BackupsServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Every BackupSettingsPatch field is a composed-type wrapper, because each is
/// Optional upstream. These pin that an omitted flag stays absent from the body
/// — which is what makes the PUT behave as the partial patch the server
/// implements — and that each given one lands on the right wrapper branch.
/// </summary>
public class BackupsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static Generated.Models.BackupSettingsPatch Empty() =>
        BackupsService.BuildSettingsBody(null, null, null, null, null, null, null);

    [Fact]
    public void OmittedFlagsLeaveEveryFieldNull()
    {
        var body = Empty();
        Assert.Null(body.BackupSchedule);
        Assert.Null(body.BackupScheduleHour);
        Assert.Null(body.BackupScheduleMinute);
        Assert.Null(body.BackupScheduleWeekday);
        Assert.Null(body.BackupRetentionCount);
        Assert.Null(body.BackupRetentionGb);
        Assert.Null(body.BackupDir);
    }

    [Fact]
    public void ScheduleLandsOnTheStringBranch()
    {
        var body = BackupsService.BuildSettingsBody("daily", null, null, null, null, null, null);
        Assert.Equal("daily", body.BackupSchedule?.String);
        Assert.Null(body.BackupScheduleHour);
    }

    [Fact]
    public void TheNumericFieldsLandOnTheIntegerBranch()
    {
        var body = BackupsService.BuildSettingsBody(null, 3, 30, 6, 10, 25, null);
        Assert.Equal(3, body.BackupScheduleHour?.Integer);
        Assert.Equal(30, body.BackupScheduleMinute?.Integer);
        Assert.Equal(6, body.BackupScheduleWeekday?.Integer);
        Assert.Equal(10, body.BackupRetentionCount?.Integer);
        Assert.Equal(25, body.BackupRetentionGb?.Integer);
    }

    [Fact]
    public void DirLandsOnTheStringBranch()
    {
        var body = BackupsService.BuildSettingsBody(null, null, null, null, null, null, "/data/backups");
        Assert.Equal("/data/backups", body.BackupDir?.String);
    }

    // "" is meaningful: it resets backup_dir to DATA_PATH/backups. It must reach
    // the body as an empty string rather than be treated as absent.
    [Fact]
    public void AnEmptyDirSurvivesAsAnEmptyString()
    {
        var body = BackupsService.BuildSettingsBody(null, null, null, null, null, null, "");
        Assert.NotNull(body.BackupDir);
        Assert.Equal("", body.BackupDir?.String);
    }

    // Zero is meaningful for both retentions: it means "no limit of this kind".
    [Fact]
    public void ZeroRetentionIsSentRatherThanTreatedAsAbsent()
    {
        var body = BackupsService.BuildSettingsBody(null, null, null, null, 0, 0, null);
        Assert.Equal(0, body.BackupRetentionCount?.Integer);
        Assert.Equal(0, body.BackupRetentionGb?.Integer);
    }

    [Theory]
    [InlineData("list", "/api/backups")]
    [InlineData("settings", "/api/backups/settings")]
    public void TheCollectionPathsAreWhatTheBuildersProduce(string which, string expected)
    {
        var client = Client();
        var info = which == "list"
            ? client.Api.Api.Backups.ToGetRequestInformation()
            : client.Api.Api.Backups.Settings.ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test" + expected, info.URI.AbsoluteUri);
    }

    [Fact]
    public void TheDownloadPathIncludesTheIdAndTheDownloadSegment()
    {
        var info = Client().Api.Api.Backups["abc123"].Download.ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test/api/backups/abc123/download", info.URI.AbsoluteUri);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BackupsServiceTests`

Expected: build failure — `BackupsService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/GrimoireCli/Services/BackupsService.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The six backup endpoints, every one require_admin. Backups are written to the
/// data directory rather than the library, so none of this depends on the
/// library mount being writable.
///
/// There is no restore endpoint and no upload: the archive can be taken and
/// fetched, and putting one back is out of band.
/// </summary>
public class BackupsService
{
    private const string AdminHint = "the admin role";
    private const string NotFoundHint =
        "No backup with that ID. List them with: grimoire-cli backups list";

    private readonly GrimoireApiClient _client;

    public BackupsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/backups.</summary>
    public async Task<string> ListAsync()
        => await _client.SendAsync(
            _client.Api.Api.Backups.ToGetRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>
    /// POST /api/backups. Snapshots the database under a read lock, so it can
    /// run longer than a typical request, and answers 409 when a backup is
    /// already in flight.
    /// </summary>
    public async Task<string> CreateAsync()
        => await _client.SendAsync(
            _client.Api.Api.Backups.ToPostRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>DELETE /api/backups/{id}. Answers 204, so the body is empty.</summary>
    public async Task<string> DeleteAsync(string id)
        => await _client.SendAsync(
            _client.Api.Api.Backups[id].ToDeleteRequestInformation(),
            permissionHint: AdminHint,
            notFoundHint: NotFoundHint);

    /// <summary>GET /api/backups/{id}/download. Serves application/zip.</summary>
    public async Task<Stream> DownloadAsync(string id)
        => await _client.SendStreamAsync(
            _client.Api.Api.Backups[id].Download.ToGetRequestInformation(),
            permissionHint: AdminHint,
            notFoundHint: NotFoundHint);

    /// <summary>GET /api/backups/settings.</summary>
    public async Task<string> SettingsAsync()
        => await _client.SendAsync(
            _client.Api.Api.Backups.Settings.ToGetRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>
    /// PUT /api/backups/settings. A partial patch despite the method: omitted
    /// fields are left alone. Returns the full effective settings.
    /// </summary>
    public async Task<string> UpdateSettingsAsync(
        string? schedule, int? hour, int? minute, int? weekday,
        int? retentionCount, int? retentionGb, string? dir)
        => await _client.SendAsync(
            _client.Api.Api.Backups.Settings.ToPutRequestInformation(
                BuildSettingsBody(schedule, hour, minute, weekday, retentionCount, retentionGb, dir)),
            permissionHint: AdminHint);

    /// <summary>
    /// Every field is a composed-type wrapper, because each is Optional
    /// upstream. Assigning through the wrapper only when the flag was given
    /// leaves an omitted one absent from the body, which is what makes the PUT
    /// behave as the partial patch the server implements. Internal (not private)
    /// so a test can pin that a client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.BackupSettingsPatch BuildSettingsBody(
        string? schedule, int? hour, int? minute, int? weekday,
        int? retentionCount, int? retentionGb, string? dir)
    {
        var body = new Generated.Models.BackupSettingsPatch();
        if (schedule is not null)
            body.BackupSchedule = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule { String = schedule };
        if (hour is not null)
            body.BackupScheduleHour = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule_hour { Integer = hour.Value };
        if (minute is not null)
            body.BackupScheduleMinute = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule_minute { Integer = minute.Value };
        if (weekday is not null)
            body.BackupScheduleWeekday = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule_weekday { Integer = weekday.Value };
        if (retentionCount is not null)
            body.BackupRetentionCount = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_retention_count { Integer = retentionCount.Value };
        if (retentionGb is not null)
            body.BackupRetentionGb = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_retention_gb { Integer = retentionGb.Value };
        if (dir is not null)
            body.BackupDir = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_dir { String = dir };
        return body;
    }
}
```

- [ ] **Step 4: Format, then run the test to verify it passes**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BackupsServiceTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/BackupsService.cs tests/GrimoireCli.Tests/Services/BackupsServiceTests.cs
git commit -m "feat: add the backups service"
```

---

### Task 3: `backups list|create|delete|download`

**Files:**
- Create: `src/GrimoireCli/Commands/BackupsCommand.cs`
- Modify: `src/GrimoireCli/Program.cs`
- Test: `tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs`

**Interfaces:**
- Consumes: `BackupsService` from Task 2; `CommandHelper.BuildClient(string? serverOverride)`; `ConsoleOutput.WriteRawJson(string)`; `ConsoleOutput.WriteStreamAsync(Stream, string)`; `GrimoireCli.Models.SavedFile`; `GrimoireCli.Models.BodyInputException`.
- Produces: `BackupsCommand.Create()` returning the `backups` `Command`. It calls `BackupSettingsCommands.Create()`, which Task 4 adds — **until Task 4 lands, leave that line out** and add it in Task 4.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BackupsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(BackupsCommand.Create(), path, full);

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("download")]
    public void EveryCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["backups", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheFourVerbs()
    {
        var names = BackupsCommand.Create().Subcommands.Select(c => c.Name).ToArray();
        Assert.Contains("list", names);
        Assert.Contains("create", names);
        Assert.Contains("delete", names);
        Assert.Contains("download", names);
    }

    [Fact]
    public void DeleteRequiresAnId()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["delete"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["delete", "--id", "abc"]).Errors);
    }

    [Fact]
    public void DownloadRequiresBothIdAndOutput()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["download", "--id", "abc"]).Errors);
        Assert.NotEmpty(BackupsCommand.Create().Parse(["download", "--output", "-"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["download", "--id", "abc", "--output", "-"]).Errors);
    }

    // The archive is the whole recovery path, because the API has no restore
    // endpoint. An agent must not be left to infer a round trip that is absent.
    [Fact]
    public void DownloadWarnsThereIsNoRestoreEndpoint()
    {
        var output = Help(["backups", "download"]);
        Assert.Contains("no restore", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDocumentsTheReadLockAndTheConflict()
    {
        var output = Help(["backups", "create"]);
        Assert.Contains("lock", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("409", output);
    }

    [Fact]
    public void DeleteDocumentsThatItIsIrreversibleAndAnswersNoBody()
    {
        var output = Help(["backups", "delete"]);
        Assert.Contains("no body", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be undone", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("download")]
    public void EveryCommandWithABodyCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["backups", leaf], full: true));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BackupsCommandTests`

Expected: build failure — `BackupsCommand` does not exist.

- [ ] **Step 3: Write `src/GrimoireCli/Commands/BackupsCommand.cs`**

```csharp
using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BackupsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("backups", "Server backups of the database and user assets");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateDownloadCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List backups, newest first")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Reports directory and total_bytes alongside the rows.",
            "",
            "version is the app version that wrote the archive, or unknown when its",
            "manifest is unreadable.");
        command.AddExamples("grimoire-cli backups list");
        command.AddResponseExample<Generated.Models.BackupListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("create", "Take a backup now")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Snapshots the database under a read lock, so writes are held off until it",
            "finishes — brief for a typical library, not instant.",
            "",
            "409 if a backup is already running. Writes to the data directory, not the",
            "library, so a read-only library mount does not block it.");
        command.AddExamples("grimoire-cli backups create");
        command.AddResponseExample<Generated.Models.BackupItem>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.CreateAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Backup ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Delete one backup archive")
        {
            idOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Deletes the archive from disk. This cannot be undone, and there is no",
            "confirmation prompt.",
            "",
            "Answers 204: stdout carries no body.");
        command.AddExamples("grimoire-cli backups delete --id <backup-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.DeleteAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDownloadCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Backup ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for the zip on stdout",
            Required = true,
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("download", "Download one backup archive")
        {
            idOption, outputOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Serves the archive as application/zip.",
            "",
            "There is no restore endpoint: the archive is the whole recovery path, and",
            "putting one back is out of band.",
            "",
            "--output - writes the zip to stdout; a path writes the file and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli backups download --id <backup-id> --output backup.zip",
            "grimoire-cli backups download --id <backup-id> --output - > backup.zip");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            await using var stream = await service.DownloadAsync(parseResult.GetValue(idOption)!);
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Register the group in `Program.cs`**

`src/GrimoireCli/Program.cs` currently reads:

```csharp
rootCommand.Subcommands.Add(AddonsCommand.Create());
rootCommand.Subcommands.Add(GenresCommand.Create());
```

Insert `backups` between them, so it sits with the other resource groups and `self-test` stays last:

```csharp
rootCommand.Subcommands.Add(AddonsCommand.Create());
rootCommand.Subcommands.Add(BackupsCommand.Create());
rootCommand.Subcommands.Add(GenresCommand.Create());
```

- [ ] **Step 5: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build clean, all tests pass. If `JsonExamplesDriftTest` fails, stop and report — the response samples for `BackupListResponse` / `BackupItem` should already exist, since the generator walks the whole model assembly.

- [ ] **Step 6: Verify the help by hand**

```bash
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli backups --help
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli backups download --help-full
```

Expected: four subcommands listed; `download` shows `Role required: admin`, the no-restore note, and a `{path, bytes}` response shape.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/BackupsCommand.cs src/GrimoireCli/Program.cs \
        tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs
git commit -m "feat: add backups list, create, delete and download"
```

---

### Task 4: `backups settings get|set`

**Files:**
- Create: `src/GrimoireCli/Commands/BackupSettingsCommands.cs`
- Modify: `src/GrimoireCli/Commands/BackupsCommand.cs` (host the subgroup)
- Test: `tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs` (append)

**Interfaces:**
- Consumes: `BackupsService.SettingsAsync()` and `UpdateSettingsAsync(schedule, hour, minute, weekday, retentionCount, retentionGb, dir)` from Task 2; `OptionHelpers.Choice` and `OptionHelpers.Range` from Task 1.
- Produces: `BackupSettingsCommands.Create()` returning the `settings` `Command` with `get` and `set`.

- [ ] **Step 1: Write the failing test**

Append to `tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs`, inside the class:

```csharp
    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public void TheSettingsPairDeclaresTheAdminRole(string leaf)
    {
        var output = HelpRenderer.Render(BackupsCommand.Create(), ["backups", "settings", leaf], full: false);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheSettingsSubgroup()
    {
        var settings = Assert.Single(
            BackupsCommand.Create().Subcommands.Where(c => c.Name == "settings"));
        Assert.Equal(["get", "set"], settings.Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void SettingsSetErrorsWithNoFlags()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set"]).Errors);
    }

    [Fact]
    public void SettingsSetAcceptsASingleFlag()
    {
        Assert.Empty(BackupsCommand.Create().Parse(["settings", "set", "--schedule", "daily"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["settings", "set", "--hour", "3"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["settings", "set", "--dir", ""]).Errors);
    }

    [Fact]
    public void SettingsSetRejectsAnUnknownSchedule()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set", "--schedule", "fortnightly"]).Errors);
    }

    // The server clamps rather than refusing, so the CLI is the only thing that
    // can tell the caller their value was not stored as given.
    [Theory]
    [InlineData("--hour", "24")]
    [InlineData("--minute", "60")]
    [InlineData("--weekday", "7")]
    [InlineData("--retention-count", "-1")]
    [InlineData("--retention-gb", "-1")]
    public void SettingsSetRejectsOutOfRangeNumbers(string flag, string value)
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set", flag, value]).Errors);
    }

    [Fact]
    public void SettingsSetDocumentsThePatchSemanticsAndTheLocks()
    {
        var output = HelpRenderer.Render(BackupsCommand.Create(), ["backups", "settings", "set"], full: false);
        Assert.Contains("left alone", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0=Mon", output);
        Assert.Contains("400", output);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BackupsCommandTests`

Expected: the new tests FAIL — `settings` is not a subcommand yet.

- [ ] **Step 3: Write `src/GrimoireCli/Commands/BackupSettingsCommands.cs`**

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The backup schedule and retention pair. GET and PUT share one path, so they
/// nest as a subgroup the way `systems cover` does.
/// </summary>
public static class BackupSettingsCommands
{
    private static readonly string[] Schedules = ["off", "hourly", "daily", "weekly"];

    public static Command Create()
    {
        var command = new Command("settings", "Backup schedule and retention");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSetCommand());
        return command;
    }

    private static Command CreateGetCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("get", "Read the backup schedule and retention settings")
        {
            serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A field whose *_env_locked is true is pinned by an environment variable;",
            "settings set answers 400 for it.");
        command.AddExamples("grimoire-cli backups settings get");
        command.AddResponseExample<Generated.Models.BackupSettingsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.SettingsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var scheduleOption = OptionHelpers.Choice("--schedule", "How often to back up automatically", Schedules);
        var hourOption = OptionHelpers.Range("--hour", "Hour of day for the scheduled run", 0, 23);
        var minuteOption = OptionHelpers.Range("--minute", "Minute of the hour", 0, 59);
        var weekdayOption = OptionHelpers.Range("--weekday", "Day for a weekly schedule; 0=Mon", 0, 6);
        var retentionCountOption = OptionHelpers.Range("--retention-count", "Archives to keep; 0 for no limit", 0);
        var retentionGbOption = OptionHelpers.Range("--retention-gb", "Budget in GB; 0 for no limit", 0);
        var dirOption = new Option<string?>("--dir") { Description = "Backup directory; \"\" resets to the default" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("set", "Configure the backup schedule and retention")
        {
            scheduleOption, hourOption, minuteOption, weekdayOption,
            retentionCountOption, retentionGbOption, dirOption,
            serverOption
        };
        command.AddRoleRequired("admin");
        command.Validators.Add(result =>
        {
            var given =
                result.GetValue(scheduleOption) is not null
                || result.GetValue(hourOption) is not null
                || result.GetValue(minuteOption) is not null
                || result.GetValue(weekdayOption) is not null
                || result.GetValue(retentionCountOption) is not null
                || result.GetValue(retentionGbOption) is not null
                || result.GetValue(dirOption) is not null;
            if (!given)
                result.AddError("Pass at least one field to set.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A partial update despite the PUT: omitted fields are left alone. Echoes",
            "the full effective settings.",
            "",
            "--weekday is 0=Mon … 6=Sun, and applies only to --schedule weekly.",
            "",
            "--dir \"\" resets to the data directory's default. A path is checked for",
            "writability now, not at the next scheduled run.",
            "",
            "--schedule, --retention-count, --retention-gb and --dir are 400 when an",
            "environment variable pins them; settings get reports which.",
            "",
            "Out-of-range numbers are rejected here rather than sent: the server",
            "clamps them and answers 200, so a typo would otherwise be stored as a",
            "different value.");
        command.AddExamples(
            "grimoire-cli backups settings set --schedule daily --hour 3",
            "grimoire-cli backups settings set --retention-count 7 --retention-gb 20",
            "grimoire-cli backups settings set --dir \"\"");
        command.AddResponseExample<Generated.Models.BackupSettingsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new BackupsService(client);
            var result = await service.UpdateSettingsAsync(
                parseResult.GetValue(scheduleOption),
                parseResult.GetValue(hourOption),
                parseResult.GetValue(minuteOption),
                parseResult.GetValue(weekdayOption),
                parseResult.GetValue(retentionCountOption),
                parseResult.GetValue(retentionGbOption),
                parseResult.GetValue(dirOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Host the subgroup**

In `src/GrimoireCli/Commands/BackupsCommand.cs`, add the subgroup as the last entry in `Create()`, after `CreateDownloadCommand()`:

```csharp
        command.Subcommands.Add(CreateDownloadCommand());
        command.Subcommands.Add(BackupSettingsCommands.Create());
        return command;
```

- [ ] **Step 5: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: everything passes.

- [ ] **Step 6: Verify the help and the validators by hand**

```bash
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli backups settings set --help
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli backups settings set --hour 99
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli backups settings set
```

Expected: the help lists all seven flags with `--schedule`'s value set rendered by `Choice`; `--hour 99` is rejected naming 0 and 23; no flags is rejected asking for at least one field. None of these reaches the server.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/BackupSettingsCommands.cs \
        src/GrimoireCli/Commands/BackupsCommand.cs \
        tests/GrimoireCli.Tests/Commands/BackupsCommandTests.cs
git commit -m "feat: add backups settings get and set"
```

---

### Task 5: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`

`create` writes a real archive, so this block must clean up after itself or runs stop converging.

- [ ] **Step 1: Add the block**

Insert after the vocabulary block added by the previous feature (the loop ending with `ok "$cmd list returned a .$key envelope"`), using the file's existing `fail` / `ok` helpers, `$WORK` scratch directory and `$CLI`:

```bash
# Backups. create writes a real archive, so this creates one, exercises every
# read against it, and deletes it again — the create-then-clean-up shape, so a
# re-run converges instead of accumulating archives.
"$CLI" backups settings get >"$WORK/bset.out" 2>"$WORK/bset.err" \
  || { cat "$WORK/bset.err" >&2; fail "backups settings get exited non-zero"; }
jq -e 'has("backup_schedule") and has("backup_dir") and has("schedule_env_locked")' "$WORK/bset.out" >/dev/null \
  || fail "backups settings get should report settings and env locks: $(cat "$WORK/bset.out")"
ok "backups settings get reports settings and env locks"

# The fixture defaults, so this is a no-op on a seeded stack and converges.
"$CLI" backups settings set --schedule off --hour 3 >"$WORK/bsset.out" 2>"$WORK/bsset.err" \
  || { cat "$WORK/bsset.err" >&2; fail "backups settings set exited non-zero"; }
jq -e '.backup_schedule == "off" and .backup_schedule_hour == 3' "$WORK/bsset.out" >/dev/null \
  || fail "backups settings set should echo the full settings: $(cat "$WORK/bsset.out")"
ok "backups settings set echoes the effective settings"

"$CLI" backups create >"$WORK/bcreate.out" 2>"$WORK/bcreate.err" \
  || { cat "$WORK/bcreate.err" >&2; fail "backups create exited non-zero"; }
BACKUP_ID=$(jq -r .id "$WORK/bcreate.out")
[ -n "$BACKUP_ID" ] && [ "$BACKUP_ID" != "null" ] \
  || fail "backups create should return an id: $(cat "$WORK/bcreate.out")"
ok "backups create returned a new archive"

"$CLI" backups list >"$WORK/blist.out" 2>"$WORK/blist.err" \
  || { cat "$WORK/blist.err" >&2; fail "backups list exited non-zero"; }
jq -e --arg id "$BACKUP_ID" 'any(.backups[]; .id == $id)' "$WORK/blist.out" >/dev/null \
  || fail "backups list should include the new archive: $(cat "$WORK/blist.out")"
jq -e 'has("directory") and has("total_bytes")' "$WORK/blist.out" >/dev/null \
  || fail "backups list should report directory and total_bytes"
ok "backups list includes the new archive"

"$CLI" backups download --id "$BACKUP_ID" --output "$WORK/backup.zip" >"$WORK/bdl.out" 2>"$WORK/bdl.err" \
  || { cat "$WORK/bdl.err" >&2; fail "backups download exited non-zero"; }
EXPECTED_BYTES=$(jq -r --arg id "$BACKUP_ID" '.backups[] | select(.id == $id) | .size_bytes' "$WORK/blist.out")
jq -e --argjson n "$EXPECTED_BYTES" '.bytes == $n' "$WORK/bdl.out" >/dev/null \
  || fail "download receipt should match the listed size_bytes: $(cat "$WORK/bdl.out")"
ok "backups download wrote the archive and reported its size"

"$CLI" backups delete --id "$BACKUP_ID" >"$WORK/bdel.out" 2>"$WORK/bdel.err" \
  || { cat "$WORK/bdel.err" >&2; fail "backups delete exited non-zero"; }
[ ! -s "$WORK/bdel.out" ] || [ "$(tr -d '[:space:]' <"$WORK/bdel.out")" = "" ] \
  || fail "backups delete answers 204 and should print no body: $(cat "$WORK/bdel.out")"
ok "backups delete answered 204 with no body"

"$CLI" backups list >"$WORK/blist2.out" 2>/dev/null \
  || fail "backups list exited non-zero after delete"
jq -e --arg id "$BACKUP_ID" 'any(.backups[]; .id == $id) | not' "$WORK/blist2.out" >/dev/null \
  || fail "the deleted archive should be gone: $(cat "$WORK/blist2.out")"
ok "the deleted archive is gone, so the run converges"
```

- [ ] **Step 2: Build and run the smoke test**

The stack is already up on 1.6.1 and seeded. Verify first, and do NOT start or seed it yourself:

```bash
curl -s -m 5 http://host.docker.internal:9481/api/health
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: every pre-existing check passes, plus seven new `ok` lines. If a pre-existing check fails, report it rather than editing it.

- [ ] **Step 3: Run it a second time to prove convergence**

```bash
bash docker/smoke-test.sh
```

Expected: identical result. A differing second run means the block leaves an archive behind and must be fixed, not accommodated.

- [ ] **Step 4: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: smoke-test the backup commands"
```

---

### Task 6: README, coverage table, API notes and CLI design

**Files:**
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-notes.md`, `docs/cli-design.md`
- Regenerate: `docs/grimoire-api-coverage.md`

- [ ] **Step 1: Add six rows to the README Commands table**

Insert after the last `addons` row:

```markdown
| `backups list` | List backups, newest first, with the directory and total size (admin) |
| `backups create` | Take a backup now; 409 if one is already running (admin) |
| `backups delete --id <backup-id>` | Delete one archive; irreversible, no prompt (admin) |
| `backups download --id <backup-id> --output <path>` | Download one archive as zip; `-` for stdout (admin) |
| `backups settings get` | Read the backup schedule and retention settings (admin) |
| `backups settings set [--schedule off\|hourly\|daily\|weekly] [--hour <0-23>] [--minute <0-59>] [--weekday <0-6>] [--retention-count <n>] [--retention-gb <n>] [--dir <path>]` | Configure the schedule and retention (admin) |
```

- [ ] **Step 2: Add six `IMPLEMENTED` entries**

In `tools/generate-api-coverage.py`:

```python
    "GET /api/backups": "`backups list` ✅",
    "POST /api/backups": "`backups create` ✅",
    "GET /api/backups/settings": "`backups settings get` ✅",
    "PUT /api/backups/settings": "`backups settings set` ✅",
    "DELETE /api/backups/{backup_id}": "`backups delete` ✅",
    "GET /api/backups/{backup_id}/download": "`backups download` ✅",
```

- [ ] **Step 3: Regenerate the coverage table**

The stack is already up; do not start it.

```bash
python3 tools/generate-api-coverage.py
git diff docs/grimoire-api-coverage.md
```

Expected: `backups` moves from `0 / 6` to `6 / 6`, the Total rises by exactly 6, and the six rows gain their CLI entries. **If any other row changes, stop and report it.**

- [ ] **Step 4: Add a `## Backups` section to `docs/grimoire-api-notes.md`**

Append, matching the file's existing style (a `##` heading, a provenance line, then bolded-lead bullets):

```markdown
## Backups

Read from `backend/routers/backups/core.py` and
`backend/services/backup/_config.py` at tag `v1.6.1`; the router and service are
byte-identical to `v1.6.0`.

- **There is no restore endpoint, and no upload.** The six endpoints are list,
  create, settings read/write, delete and download. An archive can be taken and
  fetched; putting one back is out of band.
- **`POST /api/backups` snapshots the database under a read lock**, so writes are
  held off for its duration, and answers **409** when a backup is already in
  flight (`RuntimeError` → `HTTPException(409)`). An `OSError` becomes a 500.
- **`DELETE` answers 204** with no body, and is irreversible.
- **`PUT /api/backups/settings` is a partial patch despite the method.** Every
  `BackupSettingsPatch` field is optional and omitted ones are left alone. It
  returns the full effective settings rather than `{"status": "ok"}`.
- **`backup_schedule_hour`, `_minute`, `_weekday` and both retentions are
  silently clamped**, not refused: `max(0, min(23, hour))`, `min(59, minute)`,
  `min(6, weekday)`, `max(0, …)`. The response is 200 and reports the clamped
  value, so a caller who does not read it back cannot tell.
- **Four fields are env-lockable** — `backup_schedule`,
  `backup_retention_count`, `backup_retention_gb`, `backup_dir` — and writing a
  locked one is a **400**. The clamped numeric fields are *not* lockable, and
  both retentions are in both sets: they clamp *and* lock.
- **`backup_schedule` is a closed set**: `off`, `hourly`, `daily`, `weekly`.
- **`weekday` is 0=Mon … 6=Sun.**
- **`backup_dir: ""` resets to `DATA_PATH/backups`**, and a non-empty path is
  checked for existence and writability at save time rather than at the next
  scheduled run.
- **`GET /api/backups` reports `directory` and `total_bytes`** alongside the
  rows, and each row's `version` is `"unknown"` when the archive's manifest is
  unreadable — which is what makes a cross-version restore detectable.
```

- [ ] **Step 5: Add `backups` to `docs/cli-design.md`**

Add a `## Backups` section in the resource list, matching the shape of the existing `## Lookup vocabularies` section: a one-sentence intro then a `Command | Grimoire Endpoint | Description` table with the six rows. Do not restate the caveats — they live in the help text and in `grimoire-api-notes.md`.

- [ ] **Step 6: Verify nothing else drifted**

```bash
git status --short
```

Expected: exactly `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/cli-design.md`, `docs/grimoire-api-notes.md`.

- [ ] **Step 7: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/cli-design.md docs/grimoire-api-notes.md
git commit -m "docs: record the backup commands and their server behaviour"
```

---

### Task 7: Full verification and the PR

- [ ] **Step 1: Run all four pre-PR checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report actual output; never summarise a failure as a pass.

- [ ] **Step 2: Remove the shipped roadmap item**

In `docs/roadmap.md`, delete the whole numbered item **1. Safety** under `## MVP` and renumber the remaining items (`2. Ingest` → `1.`, `3. Discovery` → `2.`). Then grep for stale cross-references and fix them:

```bash
grep -nE "block [0-9]|MVP" docs/roadmap.md
```

The Ingest item currently has no numeric back-reference of its own, but check every hit — an inaccurate cross-reference is the failure mode here. Add no note that the item shipped; the roadmap is intent only.

- [ ] **Step 3: Commit and push**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
git add docs/roadmap.md
git commit -m "docs: drop the shipped safety roadmap item"
git push -u origin feat/backup-commands
```

- [ ] **Step 4: Open the PR**

```bash
gh pr create --title "feat: add backup commands" --body "$(cat <<'BODY'
Adds `backups list|create|delete|download` and `backups settings get|set` — the
six admin-only backup endpoints. Both `abs-cli` workflows open with a backup,
which stops being optional once the `files` block lets the CLI move and delete
real files. Backups write to the data directory rather than the library, so this
needs no remount and ships first.

`settings` nests because that path is GET+PUT, the same rule that gives
`systems cover` its shape.

## Verified server behaviour

Recorded in `docs/grimoire-api-notes.md`:

- **No restore endpoint, and no upload** — an archive can be taken and fetched;
  putting one back is out of band. `download`'s help says so rather than
  implying a round trip.
- `create` snapshots under a **read lock**, so writes are held off, and 409s when
  one is already running.
- `PUT /settings` is a **partial patch despite the method**, and echoes the full
  settings.
- The numeric fields are **silently clamped, not refused** — `--hour 99` would be
  stored as 23 with a 200.
- Four fields are **env-lockable** and 400 when written; the clamped fields are
  not lockable, and both retentions are in both sets.

## One deliberate exception

Out-of-range numbers are **rejected client-side** rather than passed through, on
the grounds `OptionHelpers.Choice` already uses: a server that silently
substitutes a value returns different data with exit 0. The cost is stated in the
spec — this mirrors a server constraint, so a widened range upstream makes the
CLI wrong until someone notices.

`create`'s 409 stays a plain error rather than borrowing `library rescan`'s exit
3, which exists for a 200 that is not really success.

## Verification

`dotnet format --verify-no-changes` clean · build 0 warnings/0 errors · full
suite green · `docker/smoke-test.sh` green, run twice with identical output. The
smoke block creates an archive, exercises list/download/delete against it and
removes it, so a re-run converges.
BODY
)"
```

- [ ] **Step 5: Present the PR URL as a clickable link, then watch CI**

```bash
gh pr checks --watch
```

A PR is done at "all checks green", not at "PR open". Report the terminal result without being asked.

---

## Self-Review

**Spec coverage.** Command shape → Tasks 3, 4. Service and `BuildSettingsBody` → Task 2. `Range` → Task 1. Client-side range rejection → Tasks 1, 4. The 204 passthrough → Task 3 (no special-casing, asserted in Task 5's smoke block). The 409 as a plain error → Task 3, no exit mapping added. Help caveats, all seven commands → Tasks 3, 4. Tests, all three groups → Tasks 1, 2, 3, 4. Smoke → Task 5. All five documentation items → Tasks 6, 7.

**Type consistency.** `BackupsService`'s seven public methods and `BuildSettingsBody` are named identically in Task 2's definition and Tasks 3–4's call sites. `OptionHelpers.Range(name, description, min, max?)` matches between Task 1 and Task 4. `BackupSettingsCommands.Create()` is defined in Task 4 and called from `BackupsCommand.Create()` in the same task — Task 3 deliberately omits that line, which is called out in its Interfaces block.
