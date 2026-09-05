# File management commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the ten admin-only `files` endpoints — `files browse|upload|move|rename|delete` and `files folder create|delete|markers|scaffold|contents` — the front of the ingest pipeline.

**Architecture:** One service, two command files (the `files` verbs, and the nested `folder` subgroup), following the same shape as `backups` + `backups settings`. `upload` is the CLI's second multipart call, after `systems cover upload`.

**Tech Stack:** C# / .NET 10, `System.CommandLine`, Kiota-generated API client, xUnit, bash smoke test.

**Design spec:** [docs/specs/2026-09-05-files-commands-design.md](../specs/2026-09-05-files-commands-design.md)

## Global Constraints

- **Branch:** `feat/files-commands`. Never commit to `main`.
- **Conventional Commits**, `type: subject`, imperative, lowercase, no period, max ~72 chars. **No `Co-Authored-By:` lines. No "Generated with Claude Code" attribution.**
- **Every command calls `AddRoleRequired("admin")`** and every service call passes `permissionHint: "the admin role"`. All ten routes are `require_admin`.
- **Run `dotnet format GrimoireCli.sln` after any C# edit.** CI runs `--verify-no-changes`, and the build must stay at **0 warnings, 0 errors**.
- **No unnecessary blank lines** inside method bodies: none between consecutive `Subcommands.Add` calls or option declarations, none before a `return` following setup calls.
- **Thin pass-through.** No command may pre-fetch, read a response to derive a warning, loop over an endpoint, or mirror server policy — except the four documented parse-time validators below, which follow the settled `Choice`/`Range` precedent. stdout is the server's bytes via `ConsoleOutput.WriteRawJson`.
- **Neither delete command takes a prompt or a `--yes`.** Settled by `library cleanup-missing`; the server's 428 is the guard.
- **`CHANGELOG.md` is owned by the release process.** Do not touch it.
- **`docs/grimoire-api-coverage.md` is generated.** Edit `IMPLEMENTED` in `tools/generate-api-coverage.py` and regenerate; never hand-edit the markdown.
- **Anything that writes goes to the local stack, never a live instance.**
- The dev stack is **already running on Grimoire 1.6.1, seeded, and the library is mounted `:rw`**. Do not start, seed, or re-mount it. Health check: `curl -s -m 5 http://host.docker.internal:9481/api/health`.

---

## File Structure

- **Create** `src/GrimoireCli/Services/FilesService.cs` — ten calls, two body builders.
- **Create** `src/GrimoireCli/Commands/FilesCommand.cs` — `browse`, `upload`, `move`, `rename`, `delete`, hosting the `folder` subgroup.
- **Create** `src/GrimoireCli/Commands/FilesFolderCommands.cs` — the five `folder` subcommands.
- **Modify** `src/GrimoireCli/Program.cs` — register the group.
- **Create** `tests/GrimoireCli.Tests/Services/FilesServiceTests.cs`, `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`.
- **Modify** `docker/smoke-test.sh`, `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-notes.md`, `docs/cli-design.md`, `docs/roadmap.md`.

---

### Task 0: Commit the spec and plan

- [ ] **Step 1: Confirm branch and tree**

```bash
git rev-parse --abbrev-ref HEAD   # must print feat/files-commands
git status --short
```

Expected: the spec and plan, nothing else.

- [ ] **Step 2: Commit**

```bash
git add docs/specs/2026-09-05-files-commands-design.md docs/plans/2026-09-05-files-commands.md
git commit -m "docs: design the file management commands"
```

---

### Task 1: `FilesService`

**Files:**
- Create: `src/GrimoireCli/Services/FilesService.cs`
- Test: `tests/GrimoireCli.Tests/Services/FilesServiceTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint, string? notFoundHint, TimeSpan?)`, and the generated builders under `.Api.Api.Files` — `.Browse`, `.Upload`, `.Move`, `.Rename`, `.Delete`, `.Folder`, `.Folder.Markers`, `.Folder.Scaffold`, `.Folder.Contents`.
- Produces, all used by Tasks 2–4:
  - `FilesService(GrimoireApiClient client)`
  - `Task<string> BrowseAsync(string? path, int? limit)`
  - `Task<string> UploadAsync(string destination, string filePath, string? relativeDir, string? onConflict)`
  - `Task<string> MoveAsync(string[] sources, string destination, string? onConflict)`
  - `Task<string> RenameAsync(string path, string newName)`
  - `Task<string> DeleteAsync(string path, string? confirmName, bool deleteFiles)`
  - `Task<string> CreateFolderAsync(string parent, string name, string? containerKind, bool nsfw)`
  - `Task<string> DeleteFolderAsync(string path, string? confirmName)`
  - `Task<string> MarkersAsync(string path, string? containerKind, bool? nsfw)`
  - `Task<string> ScaffoldAsync(string path)`
  - `Task<string> FolderContentsAsync(string path)`
  - `internal static Generated.Models.MarkersRequest BuildMarkersBody(string path, string? containerKind, bool? nsfw)`
  - `internal static Generated.Models.DeleteRequest BuildDeleteBody(string path, string? confirmName, bool deleteFiles)`

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Services/FilesServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Ten near-identical sends is exactly where a copy-paste reaches the wrong
/// endpoint, so every path is pinned. The two partial-patch bodies are pinned
/// separately: a field present-but-null would stop the server leaving it alone.
/// </summary>
public class FilesServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void EachEndpointResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Files;
        Assert.Equal("http://example.test/api/files/browse", Uri(api.Browse.ToGetRequestInformation()));
        Assert.Equal("http://example.test/api/files/move", Uri(api.Move.ToPostRequestInformation(new Generated.Models.MoveRequest())));
        Assert.Equal("http://example.test/api/files/rename", Uri(api.Rename.ToPostRequestInformation(new Generated.Models.RenameRequest())));
        Assert.Equal("http://example.test/api/files/delete", Uri(api.Delete.ToPostRequestInformation(new Generated.Models.DeleteRequest())));
        Assert.Equal("http://example.test/api/files/folder", Uri(api.Folder.ToPostRequestInformation(new Generated.Models.CreateFolderRequest())));
        Assert.Equal("http://example.test/api/files/folder", Uri(api.Folder.ToDeleteRequestInformation(new Generated.Models.DeleteFolderRequest())));
        Assert.Equal("http://example.test/api/files/folder/markers", Uri(api.Folder.Markers.ToPutRequestInformation(new Generated.Models.MarkersRequest())));
        Assert.Equal("http://example.test/api/files/folder/scaffold", Uri(api.Folder.Scaffold.ToPostRequestInformation(new Generated.Models.ScaffoldRequest())));
        Assert.Equal("http://example.test/api/files/folder/contents", Uri(api.Folder.Contents.ToGetRequestInformation()));
    }

    [Fact]
    public void BrowseSendsPathAndLimitAsQueryParameters()
    {
        var info = Client().Api.Api.Files.Browse.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Path = "books/D&D";
            c.QueryParameters.Limit = 50;
        });
        var uri = Uri(info);
        Assert.Contains("limit=50", uri);
        Assert.Contains("path=books", uri);
    }

    // markers is a partial patch: an omitted field must be absent from the body,
    // not present-and-null, or the server would stop leaving it alone.
    [Fact]
    public void OmittedMarkerFieldsAreAbsentFromTheBody()
    {
        var body = FilesService.BuildMarkersBody("books/X", null, null);
        Assert.Equal("books/X", body.Path);
        Assert.Null(body.ContainerKind);
        Assert.Null(body.Nsfw);
    }

    [Fact]
    public void GivenMarkerFieldsLandOnTheirWrapperBranches()
    {
        var body = FilesService.BuildMarkersBody("books/X", "parent", true);
        Assert.Equal("parent", body.ContainerKind?.String);
        Assert.True(body.Nsfw?.Boolean);
    }

    // Clearing a container kind is expressed as "", which must survive rather
    // than be treated as absent.
    [Fact]
    public void AnEmptyContainerKindSurvivesAsAnEmptyString()
    {
        var body = FilesService.BuildMarkersBody("books/X", "", null);
        Assert.NotNull(body.ContainerKind);
        Assert.Equal("", body.ContainerKind?.String);
    }

    [Fact]
    public void FalseIsSentForNsfwRatherThanTreatedAsAbsent()
    {
        var body = FilesService.BuildMarkersBody("books/X", null, false);
        Assert.NotNull(body.Nsfw);
        Assert.False(body.Nsfw?.Boolean);
    }

    [Fact]
    public void AnOmittedConfirmNameIsAbsentFromTheDeleteBody()
    {
        var body = FilesService.BuildDeleteBody("books/X", null, deleteFiles: false);
        Assert.Equal("books/X", body.Path);
        Assert.Null(body.ConfirmName);
        Assert.False(body.DeleteFiles);
    }

    [Fact]
    public void AGivenConfirmNameLandsOnTheStringBranch()
    {
        var body = FilesService.BuildDeleteBody("books/X", "X", deleteFiles: true);
        Assert.Equal("X", body.ConfirmName?.String);
        Assert.True(body.DeleteFiles);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FilesServiceTests`

Expected: build failure — `FilesService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/GrimoireCli/Services/FilesService.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

/// <summary>
/// The ten library file-management endpoints, every one require_admin. They
/// write inside the library tree, so each one answers 409 when the library is
/// mounted read-only — Grimoire detects that from EROFS on the write itself
/// (services/library_fs/folders.py).
/// </summary>
public class FilesService
{
    private const string AdminHint = "the admin role";
    private const string NotFoundHint =
        "No such path in the library. List a folder with: grimoire-cli files browse --path <path>";

    private readonly GrimoireApiClient _client;

    public FilesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/files/browse. Merged with the index, and capped at 2000 entries.</summary>
    public async Task<string> BrowseAsync(string? path, int? limit)
    {
        var info = _client.Api.Api.Files.Browse.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Path = path;
            c.QueryParameters.Limit = limit;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// POST /api/files/upload. One file per request by the server's design, so
    /// this sends exactly one. The multipart body carries the Form fields
    /// alongside the file part, which FastAPI binds by name.
    /// </summary>
    public async Task<string> UploadAsync(string destination, string filePath, string? relativeDir, string? onConflict)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new BodyInputException($"Could not read {filePath}: {ex.Message}");
        }
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("destination", "text/plain", destination);
        if (relativeDir is not null)
            body.AddOrReplacePart("relative_dir", "text/plain", relativeDir);
        if (onConflict is not null)
            body.AddOrReplacePart("on_conflict", "text/plain", onConflict);
        body.AddOrReplacePart("file", "application/octet-stream", bytes, Path.GetFileName(filePath));
        var info = _client.Api.Api.Files.Upload.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/move. One request carrying every source.</summary>
    public async Task<string> MoveAsync(string[] sources, string destination, string? onConflict)
    {
        var body = new Generated.Models.MoveRequest
        {
            Sources = [.. sources],
            Destination = destination,
            OnConflict = onConflict,
        };
        var info = _client.Api.Api.Files.Move.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/rename.</summary>
    public async Task<string> RenameAsync(string path, string newName)
    {
        var body = new Generated.Models.RenameRequest { Path = path, NewName = newName };
        var info = _client.Api.Api.Files.Rename.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// POST /api/files/delete. Soft unless deleteFiles is set: the rows go and
    /// the files stay, which a rescan then re-adds.
    /// </summary>
    public async Task<string> DeleteAsync(string path, string? confirmName, bool deleteFiles)
    {
        var info = _client.Api.Api.Files.Delete.ToPostRequestInformation(
            BuildDeleteBody(path, confirmName, deleteFiles));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/folder.</summary>
    public async Task<string> CreateFolderAsync(string parent, string name, string? containerKind, bool nsfw)
    {
        var body = new Generated.Models.CreateFolderRequest
        {
            Parent = parent,
            Name = name,
            ContainerKind = containerKind,
            Nsfw = nsfw,
        };
        var info = _client.Api.Api.Files.Folder.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// DELETE /api/files/folder, which carries a request body. Always removes the
    /// files: unlike files delete, it has no soft form.
    /// </summary>
    public async Task<string> DeleteFolderAsync(string path, string? confirmName)
    {
        var body = new Generated.Models.DeleteFolderRequest { Path = path };
        if (confirmName is not null)
            body.ConfirmName = new Generated.Models.DeleteFolderRequest.DeleteFolderRequest_confirm_name { String = confirmName };
        var info = _client.Api.Api.Files.Folder.ToDeleteRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>PUT /api/files/folder/markers. A partial patch: omitted fields are left alone.</summary>
    public async Task<string> MarkersAsync(string path, string? containerKind, bool? nsfw)
    {
        var info = _client.Api.Api.Files.Folder.Markers.ToPutRequestInformation(
            BuildMarkersBody(path, containerKind, nsfw));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/files/folder/scaffold. Reports created and existing, so it is idempotent.</summary>
    public async Task<string> ScaffoldAsync(string path)
    {
        var body = new Generated.Models.ScaffoldRequest { Path = path };
        var info = _client.Api.Api.Files.Folder.Scaffold.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/files/folder/contents.</summary>
    public async Task<string> FolderContentsAsync(string path)
    {
        var info = _client.Api.Api.Files.Folder.Contents.ToGetRequestInformation(c =>
            c.QueryParameters.Path = path);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// container_kind and nsfw are composed-type wrappers because both are
    /// Optional upstream. Assigning through the wrapper only when the flag was
    /// given is what keeps this a partial patch. Internal (not private) so a test
    /// can pin that a client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.MarkersRequest BuildMarkersBody(string path, string? containerKind, bool? nsfw)
    {
        var body = new Generated.Models.MarkersRequest { Path = path };
        if (containerKind is not null)
            body.ContainerKind = new Generated.Models.MarkersRequest.MarkersRequest_container_kind { String = containerKind };
        if (nsfw is not null)
            body.Nsfw = new Generated.Models.MarkersRequest.MarkersRequest_nsfw { Boolean = nsfw.Value };
        return body;
    }

    /// <summary>
    /// confirm_name is a composed-type wrapper; delete_files is a plain bool the
    /// server defaults to false, and the CLI sends what the flag says.
    /// </summary>
    internal static Generated.Models.DeleteRequest BuildDeleteBody(string path, string? confirmName, bool deleteFiles)
    {
        var body = new Generated.Models.DeleteRequest { Path = path, DeleteFiles = deleteFiles };
        if (confirmName is not null)
            body.ConfirmName = new Generated.Models.DeleteRequest.DeleteRequest_confirm_name { String = confirmName };
        return body;
    }
}
```

**If a generated member name in this file does not compile** — a wrapper class name, a property, or a `MultipartBody` overload — stop and report the actual name you found rather than substituting one silently.

- [ ] **Step 4: Format, run the focused test, then the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FilesServiceTests
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

The full suite was **488/488** before this task.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/FilesService.cs tests/GrimoireCli.Tests/Services/FilesServiceTests.cs
git commit -m "feat: add the files service"
```

---

### Task 2: `files browse` and `files upload`

**Files:**
- Create: `src/GrimoireCli/Commands/FilesCommand.cs`
- Modify: `src/GrimoireCli/Program.cs`
- Test: `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`

**Interfaces:**
- Consumes: `FilesService.BrowseAsync`, `UploadAsync`; `OptionHelpers.Range`, `OptionHelpers.Choice`; `CommandHelper.BuildClient`; `ConsoleOutput.WriteRawJson`; `BodyInputException` (declared in `JsonBodyInput.cs`, namespace `GrimoireCli.Commands`, so no `using` is needed for it).
- Produces: `FilesCommand.Create()`. **Tasks 3 and 4 add to this same file** — register only `browse` and `upload` now, and do not create `FilesFolderCommands.cs`.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class FilesCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(FilesCommand.Create(), path, full);

    [Theory]
    [InlineData("browse")]
    [InlineData("upload")]
    public void EveryCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["files", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("browse")]
    [InlineData("upload")]
    public void EveryCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["files", leaf], full: true));
    }

    // --path is optional: the server lists the library root for an empty path.
    [Fact]
    public void BrowseParsesWithNoArguments()
    {
        Assert.Empty(FilesCommand.Create().Parse(["browse"]).Errors);
    }

    // The server clamps limit to max(1, min(limit, 2000)) and answers 200, so a
    // value outside the range would be silently honoured as a different one.
    [Theory]
    [InlineData("0")]
    [InlineData("2001")]
    [InlineData("-5")]
    public void BrowseRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["browse", "--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2000")]
    public void BrowseAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(FilesCommand.Create().Parse(["browse", "--limit", limit]).Errors);
    }

    [Fact]
    public void UploadRequiresDestinationAndFile()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["upload", "--destination", "books"]).Errors);
        Assert.NotEmpty(FilesCommand.Create().Parse(["upload", "--file", "a.pdf"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["upload", "--destination", "books", "--file", "a.pdf"]).Errors);
    }

    // upload's on_conflict is an unvalidated Form field upstream, and _dest_for
    // treats anything that is not "skip" as rename — so an unknown value would
    // silently rename and answer 200.
    [Fact]
    public void UploadRejectsAnUnknownConflictPolicy()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(
            ["upload", "--destination", "books", "--file", "a.pdf", "--on-conflict", "overwrite"]).Errors);
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("rename")]
    public void UploadAcceptsTheTwoConflictPolicies(string policy)
    {
        Assert.Empty(FilesCommand.Create().Parse(
            ["upload", "--destination", "books", "--file", "a.pdf", "--on-conflict", policy]).Errors);
    }

    [Fact]
    public void UploadDocumentsThatItTakesOneFilePerRequest()
    {
        var output = Help(["files", "upload"]);
        Assert.Contains("one file", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowseDocumentsItsCapAndTheIndexedDistinction()
    {
        var output = Help(["files", "browse"]);
        Assert.Contains("truncated", output);
        Assert.Contains("record_id", output);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FilesCommandTests`

Expected: build failure — `FilesCommand` does not exist.

- [ ] **Step 3: Write `src/GrimoireCli/Commands/FilesCommand.cs`**

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class FilesCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    internal static readonly string[] ConflictPolicies = ["skip", "rename"];

    public static Command Create()
    {
        var command = new Command("files", "The library tree on disk");
        command.Subcommands.Add(CreateBrowseCommand());
        command.Subcommands.Add(CreateUploadCommand());
        return command;
    }

    private static Command CreateBrowseCommand()
    {
        var pathOption = new Option<string?>("--path") { Description = "Folder to list; omit for the library root" };
        var limitOption = OptionHelpers.Range("--limit", "Entries to return; default and cap 2000", 1, 2000);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("browse", "List a library folder with indexing state")
        {
            pathOption, limitOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Merged with the index: record_id and title mark an indexed row, and their",
            "absence marks a loose file the scanner has not taken.",
            "",
            "Capped at 2000 entries — read total and truncated before treating the",
            "listing as complete. child_count per folder stops counting at 1000.",
            "",
            "singletons_taken reports which one-of-a-kind container kinds already",
            "exist, and writable whether the library mount allows writes.");
        command.AddExamples(
            "grimoire-cli files browse",
            "grimoire-cli files browse --path books --limit 100");
        command.AddResponseExample<Generated.Models.BrowseResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.BrowseAsync(
                parseResult.GetValue(pathOption),
                parseResult.GetValue(limitOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateUploadCommand()
    {
        var destinationOption = new Option<string>("--destination") { Description = "Library folder to upload into", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Local file to upload", Required = true };
        var relativeDirOption = new Option<string?>("--relative-dir") { Description = "Sub-path under the destination, created if missing" };
        var onConflictOption = OptionHelpers.Choice("--on-conflict", "Collision policy; default rename", ConflictPolicies);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("upload", "Upload a single file into a library folder")
        {
            destinationOption, fileOption, relativeDirOption, onConflictOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Sends one file per request, as the server requires — loop for many, so a",
            "failure names the file it happened on.",
            "",
            "Defaults to renaming on a collision and never overwrites. 413 above 8 GiB.",
            "",
            "The file lands under a temporary name and is renamed into place once it is",
            "fully written, so an interrupted upload leaves nothing for the scanner.");
        command.AddExamples(
            "grimoire-cli files upload --destination \"books/D&D 5e\" --file ./phb.pdf",
            "for f in *.pdf; do grimoire-cli files upload --destination books --file \"$f\"; done");
        command.AddResponseExample<Generated.Models.UploadResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            try
            {
                var result = await service.UploadAsync(
                    parseResult.GetValue(destinationOption)!,
                    parseResult.GetValue(fileOption)!,
                    parseResult.GetValue(relativeDirOption),
                    parseResult.GetValue(onConflictOption));
                ConsoleOutput.WriteRawJson(result);
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

Insert after the `BackupsCommand` line, keeping `self-test` last:

```csharp
rootCommand.Subcommands.Add(BackupsCommand.Create());
rootCommand.Subcommands.Add(FilesCommand.Create());
```

- [ ] **Step 5: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

If `JsonExamplesDriftTest` fails, stop and report it — all eight files response samples already exist, so no generator run is needed.

- [ ] **Step 6: Verify the help by hand**

```bash
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files --help
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files browse --help-full
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files browse --limit 5000
```

`--limit 5000` must be rejected naming 1 and 2000, and must not reach the server.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/FilesCommand.cs src/GrimoireCli/Program.cs \
        tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs
git commit -m "feat: add files browse and upload"
```

---

### Task 3: `files move`, `rename`, `delete`

**Files:**
- Modify: `src/GrimoireCli/Commands/FilesCommand.cs`
- Test: append to `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`

**Interfaces:**
- Consumes: `FilesService.MoveAsync`, `RenameAsync`, `DeleteAsync`; `FilesCommand.ConflictPolicies` from Task 2.
- Produces: three more subcommands on the existing `files` group.

- [ ] **Step 1: Write the failing test**

Append inside the class in `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`:

```csharp
    [Theory]
    [InlineData("move")]
    [InlineData("rename")]
    [InlineData("delete")]
    public void TheMutatingCommandsDeclareTheAdminRole(string leaf)
    {
        var output = Help(["files", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void MoveTakesRepeatableSourcesAndRequiresADestination()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["move", "--sources", "a"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(
            ["move", "--sources", "a", "b", "--destination", "books"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(
            ["move", "--sources", "a", "--sources", "b", "--destination", "books"]).Errors);
    }

    [Fact]
    public void RenameRequiresPathAndNewName()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["rename", "--path", "a"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["rename", "--path", "a", "--new-name", "b"]).Errors);
    }

    [Fact]
    public void DeleteRequiresAPathAndDefaultsToTheSoftForm()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["delete"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["delete", "--path", "a"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["delete", "--path", "a", "--delete-files"]).Errors);
    }

    // The two deletes behave oppositely and nothing in their names says so, so
    // each must state its own default where an agent will read it.
    [Fact]
    public void DeleteDocumentsThatItIsSoftUnlessAsked()
    {
        var output = Help(["files", "delete"]);
        Assert.Contains("--delete-files", output);
        Assert.Contains("rescan", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("428", output);
    }

    [Fact]
    public void MoveDocumentsThatItSkipsWhereUploadRenames()
    {
        var output = Help(["files", "move"]);
        Assert.Contains("skip", output, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FilesCommandTests`

Expected: the new tests fail — those subcommands do not exist.

- [ ] **Step 3: Add the three builders to `FilesCommand.cs`**

Register them in `Create()` after `upload`:

```csharp
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateMoveCommand());
        command.Subcommands.Add(CreateRenameCommand());
        command.Subcommands.Add(CreateDeleteCommand());
```

And add the three methods:

```csharp
    private static Command CreateMoveCommand()
    {
        var sourcesOption = new Option<string[]>("--sources")
        {
            Description = "Paths to move; repeatable",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var destinationOption = new Option<string>("--destination") { Description = "Destination folder", Required = true };
        var onConflictOption = OptionHelpers.Choice("--on-conflict", "Collision policy; default skip", ConflictPolicies);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("move", "Move files or folders, preserving their metadata")
        {
            sourcesOption, destinationOption, onConflictOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Defaults to skipping a collision and reporting it, where upload renames.",
            "Never overwrites either way.",
            "",
            "One request for every source: moved and skipped report per path.");
        command.AddExamples(
            "grimoire-cli files move --sources books/loose.pdf --destination \"books/D&D 5e\"",
            "grimoire-cli files move --sources a.pdf b.pdf --destination books --on-conflict rename");
        command.AddResponseExample<Generated.Models.MoveResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.MoveAsync(
                parseResult.GetValue(sourcesOption)!,
                parseResult.GetValue(destinationOption)!,
                parseResult.GetValue(onConflictOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRenameCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Path to rename", Required = true };
        var newNameOption = new Option<string>("--new-name") { Description = "New name, without any path", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("rename", "Rename a file or folder on disk")
        {
            pathOption, newNameOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "records reports how many indexed rows followed the rename.");
        command.AddExamples("grimoire-cli files rename --path books/old.pdf --new-name new.pdf");
        command.AddResponseExample<Generated.Models.RenameResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.RenameAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(newNameOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "File or folder to remove", Required = true };
        var confirmNameOption = new Option<string?>("--confirm-name") { Description = "The folder's own name, required when it holds content" };
        var deleteFilesOption = new Option<bool>("--delete-files") { Description = "Also unlink the files; irreversible" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Remove a file or folder from the index, or from disk")
        {
            pathOption, confirmNameOption, deleteFilesOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Soft by default: the indexed rows go, the files stay, and a rescan re-adds",
            "whatever is still on disk. Works on a read-only library.",
            "",
            "--delete-files is irreversible — the file is unlinked rather than moved to",
            "a trash folder, and the row goes with its tags, favorites, bookmarks,",
            "progress and campaign links. files folder delete is always this form.",
            "",
            "428 when the target is a folder still holding content and --confirm-name",
            "is absent or does not match its name.");
        command.AddExamples(
            "grimoire-cli files delete --path books/gone.pdf",
            "grimoire-cli files delete --path books/old --confirm-name old --delete-files");
        command.AddResponseExample<Generated.Models.DeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.DeleteAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(confirmNameOption),
                parseResult.GetValue(deleteFilesOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 4: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/FilesCommand.cs tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs
git commit -m "feat: add files move, rename and delete"
```

---

### Task 4: the `files folder` subgroup

**Files:**
- Create: `src/GrimoireCli/Commands/FilesFolderCommands.cs`
- Modify: `src/GrimoireCli/Commands/FilesCommand.cs` (one added line)
- Test: append to `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`

**Interfaces:**
- Consumes: `FilesService.CreateFolderAsync`, `DeleteFolderAsync`, `MarkersAsync`, `ScaffoldAsync`, `FolderContentsAsync`.
- Produces: `FilesFolderCommands.Create()` returning the `folder` group with `create`, `delete`, `markers`, `scaffold`, `contents`.

- [ ] **Step 1: Write the failing test**

Append inside the class:

```csharp
    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("markers")]
    [InlineData("scaffold")]
    [InlineData("contents")]
    public void EveryFolderCommandDeclaresTheAdminRole(string leaf)
    {
        var output = HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", leaf], full: false);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheFolderSubgroup()
    {
        var folder = FilesCommand.Create().Subcommands.Single(c => c.Name == "folder");
        Assert.Equal(
            ["create", "delete", "markers", "scaffold", "contents"],
            folder.Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void FolderCreateRequiresParentAndName()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "create", "--parent", "books"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["folder", "create", "--parent", "books", "--name", "X"]).Errors);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("one-page")]
    [InlineData("agnostic")]
    [InlineData("family")]
    [InlineData("publisher")]
    [InlineData("generic")]
    public void FolderCreateAcceptsEveryContainerKind(string kind)
    {
        Assert.Empty(FilesCommand.Create().Parse(
            ["folder", "create", "--parent", "books", "--name", "X", "--container-kind", kind]).Errors);
    }

    [Fact]
    public void FolderCreateRejectsAnUnknownContainerKind()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(
            ["folder", "create", "--parent", "books", "--name", "X", "--container-kind", "shelf"]).Errors);
    }

    // The one-of-a-kind kinds are the trap: a second one is refused server-side.
    [Fact]
    public void FolderCreateDocumentsTheSingletonKinds()
    {
        var output = HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", "create"], full: false);
        Assert.Contains("one-page", output);
        Assert.Contains("singletons_taken", output);
    }

    // files delete is soft by default; this one never is, and only its own help
    // can say so where an agent will read it.
    [Fact]
    public void FolderDeleteDocumentsThatItAlwaysRemovesTheFiles()
    {
        var output = HelpRenderer.Render(FilesCommand.Create(), ["files", "folder", "delete"], full: false);
        Assert.Contains("428", output);
        Assert.Contains("files delete", output);
    }

    [Fact]
    public void FolderMarkersAndContentsAndScaffoldRequireAPath()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "markers"]).Errors);
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "scaffold"]).Errors);
        Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "contents"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["folder", "markers", "--path", "a"]).Errors);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FilesCommandTests`

Expected: the new tests fail — `folder` is not a subcommand yet.

- [ ] **Step 3: Write `src/GrimoireCli/Commands/FilesFolderCommands.cs`**

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Folder management under `files`. POST and DELETE share /api/files/folder, so
/// the group nests; markers, scaffold and contents are sibling paths and stay
/// flat leaves under it. Distinct from BookFolderCommands, which serves
/// `systems book-folders` — a tagging layer, not the tree on disk.
/// </summary>
public static class FilesFolderCommands
{
    private static readonly string[] ContainerKinds =
        ["parent", "one-page", "agnostic", "family", "publisher", "generic"];

    public static Command Create()
    {
        var command = new Command("folder", "Folders in the library tree");
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateMarkersCommand());
        command.Subcommands.Add(CreateScaffoldCommand());
        command.Subcommands.Add(CreateContentsCommand());
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var parentOption = new Option<string>("--parent") { Description = "Folder to create it in", Required = true };
        var nameOption = new Option<string>("--name") { Description = "New folder's name", Required = true };
        var containerKindOption = OptionHelpers.Choice("--container-kind", "Mark it as a container of this kind", ContainerKinds);
        var nsfwOption = new Option<bool>("--nsfw") { Description = "Mark it NSFW" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("create", "Create a folder, optionally as a container or NSFW")
        {
            parentOption, nameOption, containerKindOption, nsfwOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "one-page and agnostic may exist only once in the library, and are",
            "recognised only at the top level of books/ — files browse reports",
            "singletons_taken for the ones already gone.");
        command.AddExamples(
            "grimoire-cli files folder create --parent books --name \"Call of Cthulhu\"",
            "grimoire-cli files folder create --parent books --name Publishers --container-kind publisher");
        command.AddResponseExample<Generated.Models.FolderResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.CreateFolderAsync(
                parseResult.GetValue(parentOption)!,
                parseResult.GetValue(nameOption)!,
                parseResult.GetValue(containerKindOption),
                parseResult.GetValue(nsfwOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder to delete", Required = true };
        var confirmNameOption = new Option<string?>("--confirm-name") { Description = "The folder's own name, required when it holds content" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Delete a folder, recursively when confirmed by name")
        {
            pathOption, confirmNameOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Always removes the files, unlike files delete, which is soft unless",
            "--delete-files is passed. Irreversible.",
            "",
            "An empty folder, or one holding only markers and empty descendants, goes",
            "without confirmation. One still holding content is 428 until",
            "--confirm-name matches its own name.");
        command.AddExamples("grimoire-cli files folder delete --path books/old --confirm-name old");
        command.AddResponseExample<Generated.Models.DeleteResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.DeleteFolderAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(confirmNameOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateMarkersCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder to mark", Required = true };
        var containerKindOption = OptionHelpers.Choice("--container-kind", "Container kind; \"\" clears it", ContainerKinds);
        var nsfwOption = new Option<bool?>("--nsfw") { Description = "NSFW flag (true | false)" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("markers", "Set a folder's container/NSFW markers")
        {
            pathOption, containerKindOption, nsfwOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Omitted fields are left alone.");
        command.AddExamples(
            "grimoire-cli files folder markers --path books/adult --nsfw true",
            "grimoire-cli files folder markers --path books/imprints --container-kind publisher");
        command.AddResponseExample<Generated.Models.FolderResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.MarkersAsync(
                parseResult.GetValue(pathOption)!,
                parseResult.GetValue(containerKindOption),
                parseResult.GetValue(nsfwOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateScaffoldCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "System folder to scaffold", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("scaffold", "Create the standard category folders in a system folder")
        {
            pathOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Creates Core, Supplements, Adventures, Character Sheets, Maps, Handouts,",
            "Homebrew and Starter Sets. created and existing report which were made,",
            "so re-running is safe.");
        command.AddExamples("grimoire-cli files folder scaffold --path \"books/D&D 5e\"");
        command.AddResponseExample<Generated.Models.ScaffoldResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.ScaffoldAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateContentsCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder to check", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("contents", "Report whether a folder holds content")
        {
            pathOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "has_content false means folder delete needs no --confirm-name.");
        command.AddExamples("grimoire-cli files folder contents --path books/old");
        command.AddResponseExample<Generated.Models.FolderContentsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new FilesService(client);
            var result = await service.FolderContentsAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Host the subgroup**

In `FilesCommand.Create()`, add as the last entry before `return`:

```csharp
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(FilesFolderCommands.Create());
        return command;
```

- [ ] **Step 5: Format, build, run the full suite, verify help**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files folder --help
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files folder delete --help
```

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/FilesFolderCommands.cs src/GrimoireCli/Commands/FilesCommand.cs \
        tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs
git commit -m "feat: add the files folder subgroup"
```

---

### Task 5: Smoke test

**Files:** modify `docker/smoke-test.sh`

Every command here writes into the fixture tree, so the block must clean up after itself or runs stop converging.

- [ ] **Step 1: Add the block**

Insert after the backups block (the line `ok "the deleted archive is gone, so the run converges"`), using the file's existing `fail`/`ok` helpers, `$WORK` and `$CLI`:

```bash
# Files. Every command here writes into the fixture tree, so the whole lifecycle
# happens under one temp folder that is deleted at the end — the same
# create-then-clean-up shape the backups block uses, so a re-run converges.
SMOKE_DIR="__smoke_files"
"$CLI" files folder create --parent books --name "$SMOKE_DIR" >"$WORK/fcreate.out" 2>"$WORK/fcreate.err" \
  || { cat "$WORK/fcreate.err" >&2; fail "files folder create exited non-zero"; }
jq -e --arg p "books/$SMOKE_DIR" '.path == $p' "$WORK/fcreate.out" >/dev/null \
  || fail "files folder create should echo the new path: $(cat "$WORK/fcreate.out")"
ok "files folder create made the temp folder"

"$CLI" files folder contents --path "books/$SMOKE_DIR" >"$WORK/fcontents.out" 2>/dev/null \
  || fail "files folder contents exited non-zero"
jq -e '.has_content == false' "$WORK/fcontents.out" >/dev/null \
  || fail "a new folder should report has_content false: $(cat "$WORK/fcontents.out")"
ok "files folder contents reports an empty folder"

"$CLI" files folder scaffold --path "books/$SMOKE_DIR" >"$WORK/fscaffold.out" 2>/dev/null \
  || fail "files folder scaffold exited non-zero"
jq -e '.created | length == 8' "$WORK/fscaffold.out" >/dev/null \
  || fail "scaffold should create the eight category folders: $(cat "$WORK/fscaffold.out")"
ok "files folder scaffold created the category folders"

"$CLI" files folder markers --path "books/$SMOKE_DIR" --nsfw true >"$WORK/fmarkers.out" 2>/dev/null \
  || fail "files folder markers exited non-zero"
jq -e '.nsfw == true' "$WORK/fmarkers.out" >/dev/null \
  || fail "markers should report the NSFW flag: $(cat "$WORK/fmarkers.out")"
ok "files folder markers set the NSFW flag"

# A tiny file of our own, so the upload never depends on a fixture book.
printf 'smoke' >"$WORK/smoke-upload.txt"
"$CLI" files upload --destination "books/$SMOKE_DIR" --file "$WORK/smoke-upload.txt" >"$WORK/fupload.out" 2>"$WORK/fupload.err" \
  || { cat "$WORK/fupload.err" >&2; fail "files upload exited non-zero"; }
jq -e '.name == "smoke-upload.txt" and .size == 5' "$WORK/fupload.out" >/dev/null \
  || fail "upload should report the name and size: $(cat "$WORK/fupload.out")"
ok "files upload landed one file"

"$CLI" files browse --path "books/$SMOKE_DIR" >"$WORK/fbrowse.out" 2>/dev/null \
  || fail "files browse exited non-zero"
jq -e 'any(.entries[]; .name == "smoke-upload.txt")' "$WORK/fbrowse.out" >/dev/null \
  || fail "browse should list the uploaded file: $(cat "$WORK/fbrowse.out")"
jq -e 'has("total") and has("truncated") and has("writable")' "$WORK/fbrowse.out" >/dev/null \
  || fail "browse should report total, truncated and writable"
# The point of the DB-aware listing: an uploaded, unscanned file carries no
# record_id, which is how "landed but not indexed" is visible at all.
jq -e '.entries[] | select(.name == "smoke-upload.txt") | .record_id == null' "$WORK/fbrowse.out" >/dev/null \
  || fail "an unindexed upload should carry no record_id: $(cat "$WORK/fbrowse.out")"
ok "files browse distinguishes the unindexed upload"

"$CLI" files rename --path "books/$SMOKE_DIR/smoke-upload.txt" --new-name "renamed.txt" >"$WORK/frename.out" 2>/dev/null \
  || fail "files rename exited non-zero"
jq -e --arg t "books/$SMOKE_DIR/renamed.txt" '.to == $t' "$WORK/frename.out" >/dev/null \
  || fail "rename should report where it landed: $(cat "$WORK/frename.out")"
ok "files rename moved the file to its new name"

"$CLI" files move --sources "books/$SMOKE_DIR/renamed.txt" --destination "books/$SMOKE_DIR/Core" >"$WORK/fmove.out" 2>/dev/null \
  || fail "files move exited non-zero"
jq -e '.count == 1' "$WORK/fmove.out" >/dev/null \
  || fail "move should report one moved entry: $(cat "$WORK/fmove.out")"
ok "files move relocated the file"

# Soft delete: the row goes, the file stays — files_deleted false is the proof.
"$CLI" files delete --path "books/$SMOKE_DIR/Core/renamed.txt" >"$WORK/fdelete.out" 2>/dev/null \
  || fail "files delete exited non-zero"
jq -e '.files_deleted == false' "$WORK/fdelete.out" >/dev/null \
  || fail "a delete without --delete-files should report files_deleted false: $(cat "$WORK/fdelete.out")"
ok "files delete defaulted to the soft form"

"$CLI" files folder delete --path "books/$SMOKE_DIR" --confirm-name "$SMOKE_DIR" >"$WORK/ffdelete.out" 2>"$WORK/ffdelete.err" \
  || { cat "$WORK/ffdelete.err" >&2; fail "files folder delete exited non-zero"; }
ok "files folder delete removed the temp folder"

"$CLI" files browse --path books >"$WORK/fbrowse2.out" 2>/dev/null \
  || fail "files browse exited non-zero after cleanup"
jq -e --arg n "$SMOKE_DIR" 'any(.entries[]; .name == $n) | not' "$WORK/fbrowse2.out" >/dev/null \
  || fail "the temp folder should be gone: $(cat "$WORK/fbrowse2.out")"
ok "the temp folder is gone, so the run converges"
```

- [ ] **Step 2: Build and run the smoke test**

The stack is already up, seeded, and the library is mounted `:rw`. Verify, then run:

```bash
curl -s -m 5 http://host.docker.internal:9481/api/health
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

- [ ] **Step 3: Run it twice and diff, to prove convergence**

```bash
bash docker/smoke-test.sh > /tmp/files-run1.txt 2>&1; echo "exit=$?"
bash docker/smoke-test.sh > /tmp/files-run2.txt 2>&1; echo "exit=$?"
diff -q /tmp/files-run1.txt /tmp/files-run2.txt && echo IDENTICAL
```

Both must exit 0 and the outputs must be identical. Then confirm nothing was left behind:

```bash
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files browse --path books | jq '[.entries[].name] | map(select(startswith("__smoke")))'
```

Expected: `[]`. If it is not empty, the cleanup failed — report it rather than deleting by hand.

- [ ] **Step 4: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: smoke-test the file management commands"
```

---

### Task 6: README, coverage table, API notes and CLI design

**Files:** `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md` (regenerated), `docs/grimoire-api-notes.md`, `docs/cli-design.md`

- [ ] **Step 1: Add ten rows to the README Commands table**

Insert after the last `backups` row:

```markdown
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
```

- [ ] **Step 2: Add ten `IMPLEMENTED` entries**

```python
    "GET /api/files/browse": "`files browse` ✅",
    "POST /api/files/upload": "`files upload` ✅",
    "POST /api/files/move": "`files move` ✅",
    "POST /api/files/rename": "`files rename` ✅",
    "POST /api/files/delete": "`files delete` ✅",
    "POST /api/files/folder": "`files folder create` ✅",
    "DELETE /api/files/folder": "`files folder delete` ✅",
    "PUT /api/files/folder/markers": "`files folder markers` ✅",
    "POST /api/files/folder/scaffold": "`files folder scaffold` ✅",
    "GET /api/files/folder/contents": "`files folder contents` ✅",
```

- [ ] **Step 3: Regenerate the coverage table**

```bash
python3 tools/generate-api-coverage.py
git diff docs/grimoire-api-coverage.md
```

Expected: `files` `0 / 10` → `10 / 10`, Total up by exactly 10 (49 → 59), the ten rows gaining CLI cells. **If any other row changes, stop and report it.**

- [ ] **Step 4: Add a `## Files` section to `docs/grimoire-api-notes.md`**

```markdown
## Files

Read from `backend/routers/files/core.py` and `backend/services/library_fs/` at
tag `v1.6.1`.

- **Every write here needs the library mounted read-write.** Grimoire detects a
  read-only mount from `EROFS` on the write itself
  (`services/library_fs/folders.py`) and answers **409**, so the mount is the
  only thing gating the whole API.
- **The two deletes are not a matched pair.** `POST /api/files/delete` is *soft*
  by default: `delete_files: false` removes the indexed rows and everything keyed
  to them, the files stay, and the next rescan re-adds whatever is still on disk.
  `DELETE /api/files/folder` calls the same `fs.delete_path` that
  `delete_files: true` does, so **it always unlinks** and has no soft form.
- **A hard delete is not a trash move.** The file is unlinked, and the row goes
  with its tags, favorites, bookmarks, progress and campaign links.
- **428 `confirm_required`** guards a folder that still holds content, until
  `confirm_name` matches the folder's own name. An empty folder, or one holding
  only markers and empty descendants, needs no confirmation.
- **The `on_conflict` defaults differ by endpoint**, deliberately: `upload`
  defaults to `rename` (an upload is an explicit "add this"), `move` to `skip` (a
  bulk reorganisation should step over a collision and report it). Neither ever
  overwrites — `_dest_for` has no overwrite branch.
- **`upload` does not validate `on_conflict`; `move` does.** `move`'s schema
  carries `pattern="^(skip|rename)$"` and 422s on anything else. `upload`'s is a
  bare `Form(...)` field, and `_dest_for` treats anything that is not `"skip"` as
  rename — so an unknown value silently renames and answers 200.
- **`upload` is one file per request by design**, so a large import that fails
  partway can report and retry precisely. 8 GiB cap → **413**. The file lands
  under a temporary name and is renamed into place only once fully written.
- **`browse` is DB-aware and bounded.** `record_id` marks an indexed row and its
  absence a loose file; `limit` is silently clamped to `max(1, min(limit, 2000))`
  and `total`/`truncated` report what was withheld. `child_count` per folder row
  stops at 1000.
- **Container kinds** are `parent`, `one-page`, `agnostic`, `family`,
  `publisher`, `generic`. **`one-page` and `agnostic` are singletons** — only one
  of each may exist, recognised only at the top level of `books/`, and `browse`
  reports `singletons_taken` as `{kind: path}`.
- **`scaffold` is idempotent**, creating Core, Supplements, Adventures, Character
  Sheets, Maps, Handouts, Homebrew and Starter Sets, and reporting `created` and
  `existing`.
- **`DELETE /api/files/folder` carries a request body**, which is unusual for a
  DELETE and is what the generated builder expects.
```

- [ ] **Step 5: Add `files` to `docs/cli-design.md`**

A `## Files` section matching the shape of the existing `## Backups` section: a one-sentence intro then a `Command | Grimoire Endpoint | Description` table with the ten rows. Do not restate the caveats.

- [ ] **Step 6: Verify scope and commit**

```bash
git status --short
```

Expected: exactly the five files.

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/grimoire-api-notes.md docs/cli-design.md
git commit -m "docs: record the file commands and their server behaviour"
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

In `docs/roadmap.md`, delete the whole numbered item **1. Ingest** under `## MVP` and renumber `2. Discovery` to `1.`. Then check for stale cross-references:

```bash
grep -nE "^[0-9]\. \*\*|block [0-9]|files API|Ingest" docs/roadmap.md
```

Fix any that no longer resolve. Add no note that the item shipped — the roadmap is intent only.

- [ ] **Step 3: Commit and push**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
git add docs/roadmap.md
git commit -m "docs: drop the shipped ingest roadmap item"
git push -u origin feat/files-commands
```

- [ ] **Step 4: Open the PR**

```bash
gh pr create --title "feat: add file management commands" --body "$(cat <<'BODY'
Adds the ten admin-only `files` endpoints — `files browse|upload|move|rename|delete`
and `files folder create|delete|markers|scaffold|contents`. This is the front of
the ingest pipeline and what Grimoire 1.6.0 made the library writable for.

`folder` nests because POST and DELETE share `/api/files/folder`; its three
sibling paths stay flat leaves under it.

## The asymmetry worth knowing

`files delete` is **soft by default** — the index rows go, the files stay, and a
rescan re-adds them. `files folder delete` calls the same code path as
`files delete --delete-files` and so is **always** irreversible. Nothing in the
names says that, so both commands' help does.

## Verified server behaviour

Recorded in `docs/grimoire-api-notes.md`:

- The `on_conflict` defaults differ by endpoint on purpose — `upload` renames, `move` skips — and neither ever overwrites.
- `upload` does **not** validate `on_conflict` while `move` does, and anything that is not `skip` is treated as rename, so an unknown value would silently rename and answer 200. `--on-conflict` is a `Choice` for that reason.
- `browse` is DB-aware — `record_id` is what distinguishes an indexed record from a loose file — and silently clamps `limit` to 1–2000, which `--limit` rejects at parse time instead.
- `one-page` and `agnostic` container kinds are singletons; `browse` reports `singletons_taken`.
- 428 `confirm_required` guards a non-empty folder until `--confirm-name` matches.

## Verification

`dotnet format --verify-no-changes` clean · build 0 warnings/0 errors · full suite
green · `docker/smoke-test.sh` green, run twice with byte-identical output. The
smoke block runs a whole lifecycle — folder create, scaffold, markers, upload,
browse, rename, move, soft delete, folder delete — under one temp folder it
removes at the end, so a re-run converges.
BODY
)"
```

- [ ] **Step 5: Present the PR URL as a clickable link, then watch CI**

```bash
gh pr checks --watch
```

A PR is done at "all checks green", not at "PR open".

---

## Self-Review

**Spec coverage.** Ten commands → Tasks 2, 3, 4. Service and both body builders → Task 1. The four validators → Tasks 2 and 4. One-file-per-upload → Task 2, with the loop in its own Examples. The delete asymmetry → Tasks 3 and 4, asserted in both directions. Smoke lifecycle → Task 5. All five documentation items → Tasks 6 and 7.

**Type consistency.** `FilesService`'s ten methods and two builders are named identically in Task 1's definition and Tasks 2–4's call sites. `FilesCommand.ConflictPolicies` is defined in Task 2 and reused in Task 3. `FilesFolderCommands.Create()` is defined and hosted in Task 4; Tasks 2 and 3 deliberately omit it, which their Interfaces blocks state.
