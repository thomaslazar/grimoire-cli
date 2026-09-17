# Maps per-item layer — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `maps` to the same per-item footing `books` already has — eight
commands covering list, get, update, the two batch verbs, and the three folder-tag
verbs.

**Architecture:** Two command files on one service, matching how
`BookFolderCommands.cs` rides `SystemsService`. Every write takes its body as
JSON via `--input`/`--stdin`, validated against the generated model's own field
list, and reaches the server as raw bytes. No hand-written mirror of the API's
fields anywhere.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client in
`src/GrimoireCli/Generated/` (already carries every map builder and model — this
plan adds no generated code), xUnit.

**Spec:** [`docs/specs/2026-09-16-maps-per-item-layer-design.md`](../specs/2026-09-16-maps-per-item-layer-design.md)

## Global Constraints

- **Never hand-edit `src/GrimoireCli/Generated/`.** Every builder and model this
  plan needs already exists there.
- **Thin pass-through.** One command, one endpoint. No reading a response to emit
  a derived warning, no client-side mirroring of server policy.
- **stdout is the server's bytes, unmodified.** Use `ConsoleOutput.WriteRawJson`.
  Logs and human-facing lines go to stderr.
- **Role tag ↔ permission hint must agree.** Tag `gm or admin` ↔ hint
  `"the gm or admin role"`. Reads get **no** tag: `require_not_guest` is the
  default and `get_current_user` is weaker still.
- **Run `dotnet format GrimoireCli.sln`** after writing or modifying any C# file.
- **No unnecessary blank lines** inside method bodies — none between consecutive
  option declarations, none before `return` after setup calls.
- **Help text is terse.** No restating a flag's own description, no restating a
  field the generated response sample already renders. A `Choice` option renders
  its own value set.
- **Conventional Commits**, imperative, lowercase, no period, ≤72 chars.
- **Grid ranges, verbatim from the server:** `grid_width` and `grid_height` are
  `0–1000`, `grid_px` is `0–2000`.
- **Batch cap, verbatim:** 1000 items, and no batch body may be empty.

---

### Task 1: `MapsService`

All eight HTTP calls in one service. Nothing renders yet; this task is the wire
format and its tests.

**Files:**
- Create: `src/GrimoireCli/Services/MapsService.cs`
- Create: `tests/GrimoireCli.Tests/Services/MapsServiceTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient` (`_client.Api.Api.Maps`, `.MapFolders`), and the
  generated models `MapUpdate`, `MapBulkUpdate`, `BulkAddTags`, `FolderTagsUpdate`,
  `BulkFolderTags`.
- Produces: `MapsService(GrimoireApiClient)` with
  `ListAsync(string? mapType, string? folder, int? limit, int? offset)`,
  `GetAsync(string id)`, `UpdateAsync(string id, string rawBody)`,
  `BatchUpdateAsync(string rawBody)`, `BatchTagAsync(string rawBody)`,
  `FoldersListAsync()`, `FoldersSetAsync(string rawBody)`,
  `FoldersBatchSetAsync(string rawBody)` — all `Task<string>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Services/MapsServiceTests.cs`. Mirror
`FilesServiceTests.cs`: build the `RequestInformation` and assert on the URI and
method, never over HTTP.

```csharp
using GrimoireCli.Api;
using GrimoireCli.Models;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class MapsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://localhost:9481", Token = "t" });

    private static string Uri(RequestInformation info) => info.URI.ToString();

    // An omitted filter must not reach the wire as an empty value: the server
    // treats `folder=` as "the root folder", which is a different query.
    [Fact]
    public void ListSendsOnlyTheFiltersThatWereGiven()
    {
        var info = Client().Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = null;
            c.QueryParameters.Folder = null;
            c.QueryParameters.Limit = 100;
            c.QueryParameters.Offset = null;
        });
        var uri = Uri(info);
        Assert.Contains("limit=100", uri);
        Assert.DoesNotContain("folder", uri);
        Assert.DoesNotContain("map_type", uri);
        Assert.DoesNotContain("offset", uri);
    }

    [Fact]
    public void ListSendsEveryFilterWhenGiven()
    {
        var info = Client().Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = "battlemap";
            c.QueryParameters.Folder = "battlemaps";
            c.QueryParameters.Limit = 5;
            c.QueryParameters.Offset = 10;
        });
        var uri = Uri(info);
        Assert.Contains("map_type=battlemap", uri);
        Assert.Contains("folder=battlemaps", uri);
        Assert.Contains("limit=5", uri);
        Assert.Contains("offset=10", uri);
    }

    // The folder endpoints are keyed by a path in the body, not by an id in the
    // URL — a regeneration that moved them under /maps would break every caller.
    [Fact]
    public void FolderRoutesAreTopLevelMapFolders()
    {
        var client = Client();
        Assert.EndsWith("/api/map-folders", Uri(client.Api.Api.MapFolders.ToGetRequestInformation()));
        Assert.EndsWith("/api/map-folders/bulk",
            Uri(client.Api.Api.MapFolders.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkFolderTags())));
    }

    [Fact]
    public void BulkRoutesAreDistinctFromTheItemRoute()
    {
        var client = Client();
        Assert.EndsWith("/api/maps/bulk",
            Uri(client.Api.Api.Maps.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.MapBulkUpdate())));
        Assert.EndsWith("/api/maps/bulk/tags",
            Uri(client.Api.Api.Maps.Bulk.Tags.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkAddTags())));
        Assert.EndsWith("/api/maps/abc", Uri(client.Api.Api.Maps["abc"].ToGetRequestInformation()));
    }

    // The raw body is what makes a literal 0 survive to the server, which is the
    // documented way to clear a grid override. A model round-trip would drop it.
    [Fact]
    public void UpdateSendsTheRawBodyRatherThanTheModel()
    {
        var info = Client().Api.Api.Maps["abc"].ToPatchRequestInformation(
            new GrimoireCli.Generated.Models.MapUpdate());
        info.SetStreamContent(new MemoryStream("{\"grid_px\":0}"u8.ToArray()), "application/json");
        var body = new StreamReader(info.Content).ReadToEnd();
        Assert.Equal("{\"grid_px\":0}", body);
        Assert.Equal(Method.PATCH, info.HttpMethod);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MapsServiceTests`
Expected: FAIL — `MapsService` does not exist, so the file does not compile.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/MapsService.cs`:

```csharp
using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The eight JSON endpoints behind `maps` and `maps folders`. The folder verbs
/// live here rather than in their own service because they are one collection's
/// endpoints, the way SystemsService carries `systems book-folders`.
/// </summary>
public class MapsService
{
    private const string GmHint = "the gm or admin role";
    private const string NotFoundHint = "No map with that ID. List them with: grimoire-cli maps list";

    private readonly GrimoireApiClient _client;

    public MapsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/maps. Variants are excluded server-side; only family mains are listed.</summary>
    public async Task<string> ListAsync(string? mapType, string? folder, int? limit, int? offset)
    {
        var info = _client.Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = mapType;
            c.QueryParameters.Folder = folder;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/maps/{id}. Carries both the detected grid and the stored override.</summary>
    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Maps[id].ToGetRequestInformation();
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// PATCH /api/maps/{id}. The generated builder supplies the URL, method and
    /// path parameter only; the validated raw body replaces the content so it
    /// reaches the server byte-for-byte. That is what lets a literal 0 through,
    /// which is how a grid override is cleared.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Maps[id].ToPatchRequestInformation(new Generated.Models.MapUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/maps/bulk. One transaction, skip-and-continue via errors.</summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Maps.Bulk.ToPostRequestInformation(new Generated.Models.MapBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/maps/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Maps.Bulk.Tags.ToPostRequestInformation(new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>GET /api/map-folders. Resolves stored keys to display casing.</summary>
    public async Task<string> FoldersListAsync()
    {
        var info = _client.Api.Api.MapFolders.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/map-folders. Keyed by the path in the body; echoes internal keys.</summary>
    public async Task<string> FoldersSetAsync(string rawBody)
    {
        var info = _client.Api.Api.MapFolders.ToPatchRequestInformation(new Generated.Models.FolderTagsUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/map-folders/bulk. One transaction; no per-item error list.</summary>
    public async Task<string> FoldersBatchSetAsync(string rawBody)
    {
        var info = _client.Api.Api.MapFolders.Bulk.ToPostRequestInformation(new Generated.Models.BulkFolderTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
```

`FolderTagsUpdate` and `BulkFolderTags` are shared across the four collections
and are **not** namespaced. The response model `FolderTagsOut` *is*: four routers
declare that name, so the maps one generates as
`Backend__routers__maps___schemas__FolderTagsOut`. Task 4 needs that.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MapsServiceTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/MapsService.cs tests/GrimoireCli.Tests/Services/MapsServiceTests.cs
git commit -m "feat: add MapsService covering the eight map endpoints"
```

---

### Task 2: `maps list` and `maps get`

The two reads, plus the group's registration so the command is reachable.

**Files:**
- Create: `src/GrimoireCli/Commands/MapsCommand.cs`
- Modify: `src/GrimoireCli/Program.cs` (add one registration line)
- Create: `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`

**Interfaces:**
- Consumes: `MapsService.ListAsync`, `MapsService.GetAsync` from Task 1.
- Produces: `MapsCommand.Create()` returning a `Command` named `maps`, with
  subcommands `list` and `get`. Tasks 3 and 4 add subcommands to the same
  `Create()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MapsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(MapsCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list"]).Errors);
    }

    // Both reads are require_not_guest or weaker, which carries no tag. A tag
    // here would claim a permission the endpoint does not ask for.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["maps", sub]));
    }

    // -1 is documented as "unlimited" on search. Here it means unlimited without
    // --folder and "drop the last row" with it, because the server pages by
    // Python slice in that branch. Refusing it client-side is the whole point of
    // the floor.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The exact-match rule changes what a caller asks for, so it has to be said.
    [Fact]
    public void ListNotesThatFolderIsNotASubtree()
    {
        Assert.Contains("exact folder", Help(["maps", "list"]));
    }

    // Two grids in one response read as duplicates without this.
    [Fact]
    public void GetNotesTheDifferenceBetweenDetectedAndOverriddenGrid()
    {
        var help = Help(["maps", "get"]);
        Assert.Contains("detection", help);
        Assert.Contains("override", help);
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"maps\"", Help(["maps", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["maps", "get"], full: true));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MapsCommandTests`
Expected: FAIL — `MapsCommand` does not exist.

- [ ] **Step 3: Write the command file**

Create `src/GrimoireCli/Commands/MapsCommand.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class MapsCommand
{
    public static Command Create()
    {
        var command = new Command("maps", "Read and edit map metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var mapTypeOption = new Option<string?>("--map-type") { Description = "Filter by map type" };
        var folderOption = new Option<string?>("--folder") { Description = "Filter by exact folder path" };
        var limitOption = OptionHelpers.Range("--limit", "Results per page (default 100; the server sets no maximum)", 1);
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var command = new Command("list", "List maps")
        {
            mapTypeOption, folderOption, limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--limit defaults to 100 here; the server sets no ceiling, so a larger",
            "page is just a larger number.",
            "",
            "--folder is an exact folder, not a subtree: battlemaps excludes",
            "battlemaps/caves. Values are the folder part of relative_path.",
            "",
            "Variants are hidden — only the main copy of a family is listed.");
        command.AddExamples(
            "grimoire-cli maps list",
            "grimoire-cli maps list --folder battlemaps --limit 20",
            "grimoire-cli maps list --map-type battlemap --offset 100");
        command.AddResponseExample<Generated.Models.MapListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(mapTypeOption),
                parseResult.GetValue(folderOption),
                parseResult.GetValue(limitOption) ?? 100,
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var command = new Command("get", "Get one map")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "grid is what detection found, with its own source; grid_width,",
            "grid_height and grid_px are a manual override. All three null means",
            "detection is in charge.");
        command.AddExamples("grimoire-cli maps get --id <map-id>");
        command.AddResponseExample<Generated.Models.MapDetailResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

`Create()` carries only the two reads for now. Task 3 adds the three writes and
Task 4 adds the folder group, each registering its own subcommands in this same
method. Do not add a placeholder for either.

- [ ] **Step 4: Register the group**

Modify `src/GrimoireCli/Program.cs`. After the `FilesCommand` line (`:55`), add:

```csharp
rootCommand.Subcommands.Add(MapsCommand.Create());
```

Place it after `FilesCommand` and before `SearchCommand`, keeping the existing
grouping of resource commands before the cross-cutting ones.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MapsCommandTests`
Expected: PASS, 11 tests.

Then confirm the rendered help reads correctly — never judge help from source:

```bash
dotnet run --project src/GrimoireCli -- maps list --help
dotnet run --project src/GrimoireCli -- maps get --help
```

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/MapsCommand.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs
git commit -m "feat: add maps list and maps get"
```

---

### Task 3: `maps update`, `maps batch-update`, `maps batch-tag`

The three writes. This is where the grid semantics land.

**Files:**
- Modify: `src/GrimoireCli/Commands/MapsCommand.cs` (three new subcommands)
- Modify: `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs` (append)
- Modify: `tests/GrimoireCli.Tests/Commands/ResponseShapeExitCodeAlignmentTests.cs`

**Interfaces:**
- Consumes: `MapsService.UpdateAsync`, `BatchUpdateAsync`, `BatchTagAsync`.
- Produces: subcommands `update`, `batch-update`, `batch-tag` on the `maps`
  command.

- [ ] **Step 1: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`, inside the class:

```csharp
    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["maps", sub]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(MapsCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    // The asymmetry a caller cannot guess: a 0 clears on update and is dropped
    // on batch-update, so both commands have to say so.
    [Fact]
    public void UpdateDocumentsTheGridClear()
    {
        Assert.Contains("0 to clear", Help(["maps", "update"]));
    }

    [Fact]
    public void BatchUpdateSaysItCannotClearAGrid()
    {
        Assert.Contains("cannot", Help(["maps", "batch-update"]));
    }

    // grid_warning is not a partial write; conflating it with exit 3 would tell
    // a caller a successful write failed.
    [Fact]
    public void UpdateSaysTheGridWarningIsAdvisory()
    {
        Assert.Contains("advisory", Help(["maps", "update"]));
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["maps", "batch-update"]));
        Assert.Contains("1000", Help(["maps", "batch-tag"]));
    }

    [Fact]
    public void UpdateRendersTheRequestShape()
    {
        Assert.Contains("\"grid_px\"", Help(["maps", "update"], full: true));
    }
```

And in `ResponseShapeExitCodeAlignmentTests.cs`, add two rows to the `cases`
array, after the `books` rows:

```csharp
            (MapsCommand.Create(), ["maps", "batch-update"], "errors"),
            (MapsCommand.Create(), ["maps", "batch-tag"], "errors"),
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "MapsCommandTests|ResponseShapeExitCodeAlignment"`
Expected: FAIL — the three subcommands do not exist.

- [ ] **Step 3: Add the three subcommands**

In `MapsCommand.cs`, register them in `Create()` between `get` and the folder
group:

```csharp
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
```

Add `using GrimoireCli.Api;` at the top (for `GrimoireApiClient.HasItems`) and a
logger field at the top of the class, matching `BooksCommand`:

```csharp
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
```

Then the three methods:

```csharp
    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update one map's metadata")
        {
            idOption, inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "grid_width, grid_height and grid_px take 0 to clear the override and",
            "resume detection. batch-update cannot: it drops a 0 silently.",
            "",
            "grid_width and grid_height are 0-1000, grid_px 0-2000; outside that is",
            "a 422.",
            "",
            "grid_warning in the response is advisory — the write succeeded.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli maps get --id <id>");
        command.AddExamples(
            "grimoire-cli maps update --id <id> --input grid.json",
            "echo '{\"grid_px\":70}' | grimoire-cli maps update --id <id> --stdin",
            "echo '{\"grid_px\":0}' | grimoire-cli maps update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.MapUpdate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.MapUpdate.CreateFromDiscriminatorValue,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var response = await service.UpdateAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(response);
            return 0;
        });
        return command;
    }

    private static Command CreateBatchUpdateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("batch-update", "Update many maps in one transaction")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 items. Each item requires id.",
            "",
            "Skip-and-continue: a bad id or item lands in errors, the rest apply.",
            "Exit 3 is HTTP 200 with a non-empty errors list — a partial write.",
            "",
            "A grid field sent as 0 is dropped here, so this cannot clear an",
            "override — maps update can.");
        command.AddExamples(
            "grimoire-cli maps batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli maps batch-update --stdin");
        command.AddRequestShape<Generated.Models.MapBulkUpdate>();
        command.AddResponseExample<Generated.Models.BulkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.MapBulkUpdate.CreateFromDiscriminatorValue,
                    "pass each id inside items");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.BatchUpdateAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }

    private static Command CreateBatchTagCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("batch-tag", "Add tags to many maps, additively")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 ids, and at least one tag; an empty list either side is a 422.",
            "",
            "Additive — it never removes a tag. maps update replaces the set.",
            "",
            "Exit 3 is HTTP 200 with a non-empty errors list — a partial write.");
        command.AddExamples(
            "grimoire-cli maps batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"dungeon\"]}' | grimoire-cli maps batch-tag --stdin");
        command.AddRequestShape<Generated.Models.BulkAddTags>();
        command.AddResponseExample<Generated.Models.BulkTagResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BulkAddTags.CreateFromDiscriminatorValue,
                    "pass each id inside ids");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.BatchTagAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS, whole suite.

Then read the rendered help for all three:

```bash
dotnet run --project src/GrimoireCli -- maps update --help
dotnet run --project src/GrimoireCli -- maps batch-update --help
dotnet run --project src/GrimoireCli -- maps batch-tag --help
```

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/MapsCommand.cs tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add maps update, batch-update and batch-tag"
```

---

### Task 4: `maps folders list|set|batch-set`

**Files:**
- Create: `src/GrimoireCli/Commands/MapFolderCommands.cs`
- Modify: `src/GrimoireCli/Commands/MapsCommand.cs` (one registration line)
- Create: `tests/GrimoireCli.Tests/Commands/MapFolderCommandTests.cs`

**Interfaces:**
- Consumes: `MapsService.FoldersListAsync`, `FoldersSetAsync`, `FoldersBatchSetAsync`.
- Produces: `MapFolderCommands.Create()` returning a `Command` named `folders`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/MapFolderCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MapFolderCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(MapsCommand.Create(), path, full);

    [Fact]
    public void FoldersListParsesWithNoArguments()
    {
        Assert.Empty(MapsCommand.Create().Parse(["folders", "list"]).Errors);
    }

    [Fact]
    public void FoldersListDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["maps", "folders", "list"]));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("batch-set")]
    public void FolderWritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["maps", "folders", sub]));
    }

    // The endpoint is keyed by the path in the body. An --id here would imply a
    // parent resource this collection does not have.
    [Fact]
    public void FoldersSetTakesNoId()
    {
        Assert.DoesNotContain("--id", Help(["maps", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["folders", "set"]).Errors);
        Assert.NotEmpty(MapsCommand.Create()
            .Parse(["folders", "set", "--stdin", "--input", "f.json"]).Errors);
    }

    // Diffing a write against a read otherwise looks like data loss.
    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["maps", "folders", "list"]));
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["maps", "folders", "set"], full: true));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MapFolderCommandTests`
Expected: FAIL — `MapFolderCommands` does not exist.

- [ ] **Step 3: Write the command file**

Create `src/GrimoireCli/Commands/MapFolderCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Folder tags under `maps`. A second tagging layer addressed by path, the way
/// `systems book-folders` is — but keyed by the path in the body rather than by a
/// parent id, and with a bulk verb book folders have no counterpart for. There is
/// no delete: the collection offers none.
/// </summary>
public static class MapFolderCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("folders", "Map folders and their tags");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateBatchSetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List tagged map folders");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Reports what has been tagged, never what is on disk — a row exists",
            "only once a path has been given tags.",
            "",
            "Tags come back in display casing here; set and batch-set echo the",
            "stored internal keys instead.");
        command.AddExamples("grimoire-cli maps folders list");
        command.AddResponseExample<Generated.Models.MapFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.FoldersListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("set", "Replace one map folder's tags")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the folder's set; an empty list clears it and keeps the",
            "row. The folder is addressed by path in the body, and need not exist",
            "on disk.",
            "",
            "A tag reaches every map at or below the path.");
        command.AddExamples(
            "echo '{\"path\":\"battlemaps/caves\",\"tags\":[\"cave\"]}' | grimoire-cli maps folders set --stdin");
        command.AddRequestShape<Generated.Models.FolderTagsUpdate>();
        command.AddResponseExample<Generated.Models.Backend__routers__maps___schemas__FolderTagsOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.FolderTagsUpdate.CreateFromDiscriminatorValue,
                    "pass it as path");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.FoldersSetAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateBatchSetCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("batch-set", "Set tags on many map folders in one transaction")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 folders. Each replaces that folder's tags, as set does.",
            "",
            "All or nothing: there is no per-item error list and no exit 3 here.");
        command.AddExamples("grimoire-cli maps folders batch-set --input folders.json");
        command.AddRequestShape<Generated.Models.BulkFolderTags>();
        command.AddResponseExample<Generated.Models.MapFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BulkFolderTags.CreateFromDiscriminatorValue,
                    "pass each path inside folders");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.FoldersBatchSetAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Wire the folder group**

In `MapsCommand.cs`, add the folder group as the last subcommand in `Create()`,
after the three write commands Task 3 registered:

```csharp
        command.Subcommands.Add(MapFolderCommands.Create());
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS, whole suite.

Then read the rendered help:

```bash
dotnet run --project src/GrimoireCli -- maps folders --help
dotnet run --project src/GrimoireCli -- maps folders list --help
dotnet run --project src/GrimoireCli -- maps folders set --help
dotnet run --project src/GrimoireCli -- maps folders batch-set --help
```

If `Backend__routers__maps___schemas__FolderTagsOut` does not resolve, list the
generated models to find the exact name — do not substitute another collection's:

```bash
ls src/GrimoireCli/Generated/Models/ | grep FolderTagsOut
```

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/ tests/GrimoireCli.Tests/Commands/MapFolderCommandTests.cs
git commit -m "feat: add maps folders list, set and batch-set"
```

---

### Task 5: Map fixtures and the smoke-test block

The help claims are about live behaviour, so they get exercised live.

**Files:**
- Modify: `docker/seed.sh`
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: every command from Tasks 2–4.
- Produces: a `maps/` fixture tree the smoke test can rely on — two maps under
  `maps/battlemaps` and one under `maps/battlemaps/caves`, so the exact-match
  folder rule is observable.

- [ ] **Step 1: Seed map fixtures**

In `docker/seed.sh`, after the fixture-cover block (the
`python3 "$HERE/make-fixtures.py" --png "$HERE/fixture-cover.png"` line), add:

```bash
# Map fixtures. Two in one folder and one in a child, so the smoke test can show
# that --folder is an exact match rather than a subtree.
mkdir -p "$LIBRARY/maps/battlemaps/caves"
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/maps/battlemaps/Tavern.png"
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/maps/battlemaps/Crossroads.png"
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/maps/battlemaps/caves/Deep Cave.png"
say "wrote 3 fixture maps"
```

Keep it above the rescan wait — the scan has to see the tree.

- [ ] **Step 2: Verify the fixtures index**

```bash
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
dotnet run --project src/GrimoireCli -- maps list
```

Expected: three maps, `relative_path` values under `maps/battlemaps`.

- [ ] **Step 3: Add the smoke-test block**

In `docker/smoke-test.sh`, after the `files` section and before the `search`
section, add a `maps` block. Follow the file's existing idiom exactly — `"$CLI"`,
`ok`/`fail`, `jq -e`, output into `"$WORK"`. Writes go only to the seeded map
fixtures and only to fixed values, so a re-run converges.

```bash
# ---- maps -------------------------------------------------------------------
"$CLI" maps list >"$WORK/maps.out" 2>"$WORK/maps.err" \
  || { cat "$WORK/maps.err" >&2; fail "maps list exited non-zero"; }
jq -e '.total >= 3 and (.maps | length) >= 3' "$WORK/maps.out" >/dev/null \
  || fail "maps list should report the seeded maps: $(cat "$WORK/maps.out")"
ok "maps list returns the seeded maps"

"$CLI" maps list --limit 1 >"$WORK/maps-limit.out" 2>&1 \
  || fail "maps list --limit exited non-zero"
jq -e '(.maps | length) == 1' "$WORK/maps-limit.out" >/dev/null \
  || fail "--limit 1 should return one row: $(cat "$WORK/maps-limit.out")"
ok "maps list --limit bounds the page"

# The exact-match rule: the child folder's map must not appear.
"$CLI" maps list --folder "maps/battlemaps" >"$WORK/maps-folder.out" 2>&1 \
  || fail "maps list --folder exited non-zero"
jq -e '[.maps[].relative_path] | all(startswith("maps/battlemaps/caves") | not)' \
  "$WORK/maps-folder.out" >/dev/null \
  || fail "--folder must not reach a subfolder: $(cat "$WORK/maps-folder.out")"
ok "maps list --folder is an exact folder, not a subtree"

MAP_ID=$(jq -r '.maps[] | select(.filename == "Tavern.png") | .id' "$WORK/maps.out")
[ -n "$MAP_ID" ] || fail "no fixture map id found: $(cat "$WORK/maps.out")"

"$CLI" maps get --id "$MAP_ID" >"$WORK/mapget.out" 2>&1 \
  || fail "maps get exited non-zero"
jq -e 'has("grid") and has("folder_path") and has("folder_tags")' "$WORK/mapget.out" >/dev/null \
  || fail "maps get should carry grid and folder context: $(cat "$WORK/mapget.out")"
ok "maps get returns grid and folder context"

echo '{"grid_px":70}' | "$CLI" maps update --id "$MAP_ID" --stdin >"$WORK/mapupd.out" 2>&1 \
  || fail "maps update exited non-zero"
"$CLI" maps get --id "$MAP_ID" >"$WORK/mapget2.out" 2>&1
jq -e '.grid_px == 70' "$WORK/mapget2.out" >/dev/null \
  || fail "the grid override should read back: $(cat "$WORK/mapget2.out")"
ok "maps update sets a grid override"

# The documented clear. This is also what makes the block idempotent.
echo '{"grid_px":0}' | "$CLI" maps update --id "$MAP_ID" --stdin >"$WORK/mapclr.out" 2>&1 \
  || fail "maps update --stdin with 0 exited non-zero"
"$CLI" maps get --id "$MAP_ID" >"$WORK/mapget3.out" 2>&1
jq -e '.grid_px == null' "$WORK/mapget3.out" >/dev/null \
  || fail "0 should clear the grid override: $(cat "$WORK/mapget3.out")"
ok "maps update clears a grid override with 0"

# The symptom the whole group exists to fix: tagging a map without duplicates.
echo "{\"ids\":[\"$MAP_ID\"],\"tags\":[\"smoke-map\"]}" \
  | "$CLI" maps batch-tag --stdin >"$WORK/maptag.out" 2>&1 \
  || fail "maps batch-tag exited non-zero"
"$CLI" tags items --tag smoke-map --resource-type map >"$WORK/maptagitems.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$MAP_ID" '[.. | .id? // empty] | any(. == $id)' "$WORK/maptagitems.out" >/dev/null \
  || fail "the tagged map should be findable: $(cat "$WORK/maptagitems.out")"
ok "maps batch-tag tags a map without duplicates merge-metadata"

echo '{"path":"maps/battlemaps","tags":["Smoke Folder"]}' \
  | "$CLI" maps folders set --stdin >"$WORK/mapfset.out" 2>&1 \
  || fail "maps folders set exited non-zero"
"$CLI" maps folders list >"$WORK/mapflist.out" 2>&1 \
  || fail "maps folders list exited non-zero"
jq -e '[.folders[] | select(.path == "maps/battlemaps") | .tags[]] | any(. == "Smoke Folder")' \
  "$WORK/mapflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/mapflist.out")"
ok "maps folders set writes a tag that lists in display casing"

echo '{"grid_px":"seventy"}' | "$CLI" maps update --id "$MAP_ID" --stdin >/dev/null 2>&1 \
  && fail "a wrong-typed grid should not exit 0"
ok "maps update refuses a body the server would reject"
```

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: both runs pass. The second run is the idempotence check — the tag and
folder-tag writes are fixed values, and the grid ends cleared.

- [ ] **Step 5: Commit**

```bash
git add docker/seed.sh docker/smoke-test.sh
git commit -m "test: seed map fixtures and cover the maps commands live"
```

---

### Task 6: Documentation

**Files:**
- Modify: `README.md` (Commands table)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`)
- Modify: `docs/grimoire-api-coverage.md` (regenerated, not hand-edited)
- Modify: `docs/grimoire-api-notes.md` (new `## Maps` section)
- Modify: `CLAUDE.md` (two lines)
- Modify: `docs/roadmap.md` (drop the maps item)

- [ ] **Step 1: README Commands table**

Add eight rows after the `files folder contents` row:

```markdown
| `maps list [--map-type <t>] [--folder <path>] [--limit <n>] [--offset <n>]` | List maps (defaults to 100 results) |
| `maps get --id <id>` | Get one map, with its detected grid and any manual override |
| `maps update --id <id> {--input <file> \| --stdin}` | Update one map's metadata (gm or admin) |
| `maps batch-update {--input <file> \| --stdin}` | Update many maps in one transaction; exit 3 if partial (gm or admin) |
| `maps batch-tag {--input <file> \| --stdin}` | Add tags to many maps, additively; exit 3 if partial (gm or admin) |
| `maps folders list` | List tagged map folders |
| `maps folders set {--input <file> \| --stdin}` | Replace one map folder's tags (gm or admin) |
| `maps folders batch-set {--input <file> \| --stdin}` | Set tags on many map folders in one transaction (gm or admin) |
```

- [ ] **Step 2: API coverage**

In `tools/generate-api-coverage.py`, add to `IMPLEMENTED`:

```python
    "GET /api/maps": "`maps list` ✅",
    "GET /api/maps/{map_id}": "`maps get` ✅",
    "PATCH /api/maps/{map_id}": "`maps update` ✅",
    "POST /api/maps/bulk": "`maps batch-update` ✅",
    "POST /api/maps/bulk/tags": "`maps batch-tag` ✅",
    "GET /api/map-folders": "`maps folders list` ✅",
    "PATCH /api/map-folders": "`maps folders set` ✅",
    "POST /api/map-folders/bulk": "`maps folders batch-set` ✅",
```

Then regenerate against the running stack:

```bash
python3 tools/generate-api-coverage.py
```

Expected: `maps` moves 0/16 → 8/16 and the total rises by 8.

- [ ] **Step 3: API notes**

Add a `## Maps` section to `docs/grimoire-api-notes.md`, after `## Files`:

```markdown
## Maps

Read from `backend/routers/maps/core.py` and `_schemas.py` at tag `v1.7.0`, and
measured against the running 1.7.0 stack.

- **`limit` defaults to 100000 with no ceiling.** `Query(100000)` and no `le=`,
  so an unflagged `GET /api/maps` returns the whole library. `tokens`, `models`
  and `audio` declare the same. `books` is the outlier at `Query(100, le=500)`.
  The CLI supplies its own default of 100 here, which is the one place `maps
  list` holds an opinion the server does not.
- **Paging is implemented twice and `folder` picks which.** Without it the
  server pages in SQL (`q.offset(offset).limit(limit)`); with it the whole
  subtree is materialised and sliced in Python (`filtered[offset : offset +
  limit]`). A negative limit is therefore unlimited in the first branch and
  "drop the last row" in the second. The CLI refuses one with
  `OptionHelpers.Range(1)`.
- **`folder` is an exact match, not a subtree.** The SQL prefix filter only
  narrows what is materialised; membership is `_folder_path(m.relative_path)
  == folder`. So `folder=maps/battlemaps` excludes `maps/battlemaps/caves`.
- **A grid override is cleared by sending `0`, and only the single PATCH
  honours it.** The validator normalises `0` to `None` (`round(v, 2) or None`),
  which `exclude_none=True` would swallow; `update_map` re-applies the clear
  from `model_fields_set`, and `bulk_update_maps` does not. A batch can set a
  grid, never clear one. Measured: `{"grid_px":0}` through `maps update` read
  back as null.
- **`grid_warning` is advisory.** `PATCH /api/maps/{id}` answers `{"status",
  "grid_warning"}`, and the warning rides along when a saved override looks
  implausible for the map's pixel dimensions. The write succeeded regardless,
  so the CLI exits 0.
- **`GET /api/maps/{id}` carries two grids.** `grid` is what detection found,
  with its own `source`; `grid_width`/`grid_height`/`grid_px` are the stored
  override. All three null means detection is in charge.
- **Folder tags read and write differently.** `GET /api/map-folders` resolves to
  display casing; the PATCH and the bulk echo the stored internal keys. Same
  asymmetry as `systems book-folders`.
- **Every batch body caps at 1000 and none may be empty.** `items`, `ids`,
  `folders` and `tags` each carry `min_length=1`, so an empty batch is a 422
  rather than a no-op.
- **`map-folders` has no delete**, unlike `systems book-folders`, and gains a
  bulk verb book folders have no counterpart for.
```

- [ ] **Step 4: CLAUDE.md**

Narrow the `update --id --field` line under "Command implementation
conventions". Replace:

```
ID-keyed resources use `update --id --field`, where the flags mirror the API's body field names.
```

with:

```
Commands whose body is a handful of scalars take them as flags mirroring the API's body field names — `addons update`, `backups settings set`. Commands carrying a metadata body take it as JSON via `--input`/`--stdin`, validated against the generated model's own field list: `books`, `systems` and `maps` all do, and a hand-written mirror of the API's fields is not to be added.
```

And in the reset recipe under "Pre-PR verification", replace
`rm -rf docker/data docker/library/books docker/addon-index/index.json` with
`rm -rf docker/data docker/library/books docker/library/maps docker/addon-index/index.json`.

- [ ] **Step 5: Roadmap**

In `docs/roadmap.md`, delete item 1 (maps per-item layer) from `## Next` and
renumber the remaining five. Item 2 (models) becomes item 1. Change nothing
else — the roadmap records intent, not status.

- [ ] **Step 6: Run the full pre-PR gate**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

Expected: all four clean.

- [ ] **Step 7: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs/ CLAUDE.md
git commit -m "docs: record the maps command group and its verified behaviour"
```

---

## Self-review notes

- **Spec coverage.** All eight commands (Tasks 2–4), the JSON-body decision
  (Task 3's `JsonBodyInput.Validate` calls), the `--limit` default and floor
  (Task 2), all four help blocks (Tasks 2–4), unit and smoke tests (Tasks 1–5),
  and every documentation item including the CLAUDE.md narrowing and the reset
  recipe (Task 6).
- **Cross-cutting test.** `ResponseShapeExitCodeAlignmentTests` enumerates bulk
  commands by hand and would not fail on its own if the two new ones were
  omitted, so Task 3 adds them explicitly.
- **Type consistency.** `MapsService` method names are used identically in
  Tasks 2, 3 and 4. The namespaced `Backend__routers__maps___schemas__FolderTagsOut`
  appears only in Task 4, with a verification command beside it.
