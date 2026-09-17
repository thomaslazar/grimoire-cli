# Models per-item layer — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `models` to the same per-item footing `maps` has — nine commands
covering list, get, thumbnail, update, the two batch verbs, and the three
folder-tag verbs.

**Architecture:** A direct port of the shipped `maps` group: two command files on
one service, every write taking its body as JSON via `--input`/`--stdin`
validated against the generated model's own field list, and reaching the server
as raw bytes. Read `src/GrimoireCli/Commands/MapsCommand.cs`,
`MapFolderCommands.cs` and `Services/MapsService.cs` before starting — this plan
is those three files with a different noun and a shorter field list.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client in
`src/GrimoireCli/Generated/` (already carries every models builder and model —
this plan adds no generated code), xUnit, Python for the fixture generator.

**Spec:** [`docs/specs/2026-09-17-models-per-item-layer-design.md`](../specs/2026-09-17-models-per-item-layer-design.md)

## Global Constraints

- **Never hand-edit `src/GrimoireCli/Generated/`.** Everything needed exists.
- **The generated namespace for models is `Api.ModelsRequests`, not `Api.Models`**
  — Kiota renamed it to avoid colliding with the `Generated/Models/` DTO
  namespace. The call site `_client.Api.Api.Models` is unaffected. There is no
  `src/GrimoireCli/Generated/Api/Models/` directory; do not go looking for one.
- **Thin pass-through.** One command, one endpoint. No reading a response to emit
  a derived warning, no client-side mirroring of server policy.
- **stdout is the server's bytes, unmodified.** `ConsoleOutput.WriteRawJson` for
  JSON, `ConsoleOutput.WriteStreamAsync` for the thumbnail.
- **Role tag ↔ permission hint must agree.** Tag `gm or admin` ↔ hint
  `"the gm or admin role"`. The three reads (`list`, `get`, `thumbnail`) get
  **no** tag: `require_not_guest` is the default and `get_current_user` is weaker.
- **Run `dotnet format GrimoireCli.sln`** after writing or modifying any C# file.
- **No unnecessary blank lines** inside method bodies.
- **Help text is terse.** No restating a flag's own description, no restating a
  field the generated request/response sample already renders.
- **Conventional Commits**, imperative, lowercase, no period, ≤72 chars. No
  `Co-Authored-By`, no "Generated with" attribution.
- **`Model3DUpdate` has exactly 4 fields:** `description`, `tags`,
  `is_explicit`, `is_supported`. None is numerically bounded.
- **Batch cap, verbatim:** 1 to 1000 items, and no batch body may be empty.

---

### Task 1: `ModelsService`

All nine HTTP calls in one service. Nothing renders yet.

**Files:**
- Create: `src/GrimoireCli/Services/ModelsService.cs`
- Create: `tests/GrimoireCli.Tests/Services/ModelsServiceTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient` (`_client.Api.Api.Models`, `.ModelFolders`), and
  the generated models `Model3DUpdate`, `Model3DBulkUpdate`, `BulkAddTags`,
  `FolderTagsUpdate`, `BulkFolderTags`.
- Produces: `ModelsService(GrimoireApiClient)` with
  `ListAsync(int? limit, int? offset)`, `GetAsync(string id)`,
  `ThumbnailAsync(string id)` → `Task<Stream>`,
  `UpdateAsync(string id, string rawBody)`, `BatchUpdateAsync(string rawBody)`,
  `BatchTagAsync(string rawBody)`, `FoldersListAsync()`,
  `FoldersSetAsync(string rawBody)`, `FoldersBatchSetAsync(string rawBody)` —
  all `Task<string>` except `ThumbnailAsync`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Services/ModelsServiceTests.cs`. Read
`tests/GrimoireCli.Tests/Services/MapsServiceTests.cs` first — this mirrors it,
including its `[Collection("NLog")]` attribute, which is load-bearing: the
service-level test sends through `GrimoireApiClient`'s pipeline, and
`DebugHttpHandler` writes into the global NLog target that
`DebugHttpHandlerTests` asserts on. Without the collection they race in CI.

```csharp
using System.Net;
using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

// The raw-body test sends through GrimoireApiClient's pipeline, and
// DebugHttpHandler sits in it — so this class writes into whatever global NLog
// target is configured at the time. Without the collection it races
// DebugHttpHandlerTests, whose assertions count the lines in that target.
[Collection("NLog")]
public class ModelsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://localhost:9481", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://localhost:9481";
        return info.URI.AbsoluteUri;
    }

    // An omitted flag must not reach the wire: the server's own default is what
    // should apply, not an empty value.
    [Fact]
    public void ListSendsOnlyThePagingItWasGiven()
    {
        var info = Client().Api.Api.Models.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = 100;
            c.QueryParameters.Offset = null;
        });
        var uri = Uri(info);
        Assert.Contains("limit=100", uri);
        Assert.DoesNotContain("offset", uri);
    }

    // The folder endpoints are keyed by a path in the body, not by an id in the
    // URL — a regeneration that moved them under /models would break callers.
    [Fact]
    public void FolderRoutesAreTopLevelModelFolders()
    {
        var client = Client();
        Assert.EndsWith("/api/model-folders", Uri(client.Api.Api.ModelFolders.ToGetRequestInformation()));
        Assert.EndsWith("/api/model-folders/bulk",
            Uri(client.Api.Api.ModelFolders.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkFolderTags())));
    }

    [Fact]
    public void BulkRoutesAreDistinctFromTheItemRoute()
    {
        var client = Client();
        Assert.EndsWith("/api/models/bulk",
            Uri(client.Api.Api.Models.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.Model3DBulkUpdate())));
        Assert.EndsWith("/api/models/bulk/tags",
            Uri(client.Api.Api.Models.Bulk.Tags.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkAddTags())));
        Assert.EndsWith("/api/models/abc", Uri(client.Api.Api.Models["abc"].ToGetRequestInformation()));
        Assert.EndsWith("/api/models/abc/thumbnail",
            Uri(client.Api.Api.Models["abc"].Thumbnail.ToGetRequestInformation()));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((request.Method, request.RequestUri!.AbsoluteUri, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    // Goes through ModelsService itself rather than stopping at the builder, so a
    // raw-body call that lost its SetStreamContent — and would therefore send the
    // empty model instead of what the caller typed — fails here.
    [Fact]
    public async Task EveryRawBodyCallSendsItsBodyUnchangedToItsOwnRoute()
    {
        var handler = new RecordingHandler();
        var dir = Directory.CreateTempSubdirectory().FullName;
        var config = new AppConfig
        {
            Server = "http://example.test",
            AccessToken = "t",
            // Keeps PreflightAsync from probing /api/about through the stub.
            LastVersionCheck = DateTimeOffset.UtcNow,
            LastServerVersion = "nightly"
        };
        var manager = new ConfigManager(Path.Combine(dir, "config.json"));
        manager.Save(config);
        var service = new ModelsService(new GrimoireApiClient(config, manager, handler));

        await service.UpdateAsync("abc", "{\"is_supported\":true}");
        await service.BatchUpdateAsync("{\"items\":[{\"id\":\"abc\",\"is_explicit\":false}]}");
        await service.BatchTagAsync("{\"ids\":[\"abc\"],\"tags\":[\"goblin\"]}");
        await service.FoldersSetAsync("{\"path\":\"Goblins\",\"tags\":[\"goblin\"]}");
        await service.FoldersBatchSetAsync("{\"folders\":[{\"path\":\"Goblins\",\"tags\":[]}]}");

        Assert.Equal(new[]
        {
            (HttpMethod.Patch, "http://example.test/api/models/abc", "{\"is_supported\":true}"),
            (HttpMethod.Post, "http://example.test/api/models/bulk", "{\"items\":[{\"id\":\"abc\",\"is_explicit\":false}]}"),
            (HttpMethod.Post, "http://example.test/api/models/bulk/tags", "{\"ids\":[\"abc\"],\"tags\":[\"goblin\"]}"),
            (HttpMethod.Patch, "http://example.test/api/model-folders", "{\"path\":\"Goblins\",\"tags\":[\"goblin\"]}"),
            (HttpMethod.Post, "http://example.test/api/model-folders/bulk", "{\"folders\":[{\"path\":\"Goblins\",\"tags\":[]}]}"),
        }, handler.Seen);
        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ModelsServiceTests`
Expected: FAIL — `ModelsService` does not exist, so the file does not compile.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/ModelsService.cs`:

```csharp
using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The nine endpoints behind `models` and `models folders`. The folder verbs live
/// here rather than in their own service because they are one collection's
/// endpoints, the way MapsService carries `maps folders`.
/// </summary>
public class ModelsService
{
    private const string GmHint = "the gm or admin role";
    private const string NotFoundHint = "No model with that ID. List them with: grimoire-cli models list";

    private readonly GrimoireApiClient _client;

    public ModelsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/models. Variants and disallowed explicit rows are excluded server-side.</summary>
    public async Task<string> ListAsync(int? limit, int? offset)
    {
        var info = _client.Api.Api.Models.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/models/{id}. Carries the derived is_presupported/is_unsupported pair.</summary>
    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Models[id].ToGetRequestInformation();
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/models/{id}/thumbnail. 404 when no thumbnail was rendered.</summary>
    public async Task<Stream> ThumbnailAsync(string id)
    {
        var info = _client.Api.Api.Models[id].Thumbnail.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>
    /// PATCH /api/models/{id}. The generated builder supplies the URL, method and
    /// path parameter only; the validated raw body replaces the content so it
    /// reaches the server byte-for-byte.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Models[id].ToPatchRequestInformation(new Generated.Models.Model3DUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/models/bulk. One transaction; only an unresolved id reaches errors.</summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Models.Bulk.ToPostRequestInformation(new Generated.Models.Model3DBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/models/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Models.Bulk.Tags.ToPostRequestInformation(new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>GET /api/model-folders. Resolves stored keys to display casing.</summary>
    public async Task<string> FoldersListAsync()
    {
        var info = _client.Api.Api.ModelFolders.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/model-folders. Keyed by the path in the body; echoes internal keys.</summary>
    public async Task<string> FoldersSetAsync(string rawBody)
    {
        var info = _client.Api.Api.ModelFolders.ToPatchRequestInformation(new Generated.Models.FolderTagsUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/model-folders/bulk. One transaction; no per-item error list.</summary>
    public async Task<string> FoldersBatchSetAsync(string rawBody)
    {
        var info = _client.Api.Api.ModelFolders.Bulk.ToPostRequestInformation(new Generated.Models.BulkFolderTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ModelsServiceTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/ModelsService.cs tests/GrimoireCli.Tests/Services/ModelsServiceTests.cs
git commit -m "feat: add ModelsService covering the nine model endpoints"
```

---

### Task 2: `models list`, `models get`, `models thumbnail`

The three reads, plus the group's registration.

**Files:**
- Create: `src/GrimoireCli/Commands/ModelsCommand.cs`
- Modify: `src/GrimoireCli/Program.cs` (one registration line)
- Create: `tests/GrimoireCli.Tests/Commands/ModelsCommandTests.cs`

**Interfaces:**
- Consumes: `ModelsService.ListAsync`, `GetAsync`, `ThumbnailAsync`.
- Produces: `ModelsCommand.Create()` returning a `Command` named `models` with
  subcommands `list`, `get`, `thumbnail`. Tasks 3 and 4 add more to the same
  `Create()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/ModelsCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ModelsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(ModelsCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(ModelsCommand.Create().Parse(["list"]).Errors);
    }

    // All three reads are require_not_guest or weaker, which carries no tag.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("thumbnail")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["models", sub]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(ModelsCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The default has to render natively, not live in a description string.
    [Fact]
    public void ListRendersItsLimitDefault()
    {
        Assert.Contains("[default: 100]", Help(["models", "list"]));
    }

    // The read side exposes a derived pair; the write side takes one tri-state
    // field. Without this a caller reads is_presupported and tries to write it.
    [Fact]
    public void GetExplainsTheDerivedSupportPair()
    {
        var help = Help(["models", "get"]);
        Assert.Contains("is_presupported", help);
        Assert.Contains("unknown", help);
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ThumbnailRequiresAnIdAndAnOutput()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["thumbnail", "--id", "x"]).Errors);
        Assert.NotEmpty(ModelsCommand.Create().Parse(["thumbnail", "--output", "x.webp"]).Errors);
        Assert.Empty(ModelsCommand.Create().Parse(["thumbnail", "--id", "x", "--output", "x.webp"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"models\"", Help(["models", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["models", "get"], full: true));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ModelsCommandTests`
Expected: FAIL — `ModelsCommand` does not exist.

- [ ] **Step 3: Write the command file**

Create `src/GrimoireCli/Commands/ModelsCommand.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Api;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class ModelsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("models", "Read and edit 3D model metadata");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateThumbnailCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var limitOption = OptionHelpers.Range("--limit", "Results per page (the server sets no maximum)", 1);
        limitOption.DefaultValueFactory = _ => 100;
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var command = new Command("list", "List 3D models")
        {
            limitOption, offsetOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "The account's explicit permission filters the list server-side.",
            "",
            "Page with --offset against total in the response.");
        command.AddExamples(
            "grimoire-cli models list",
            "grimoire-cli models list --limit 20 --offset 100");
        command.AddResponseExample<Generated.Models.Model3DListResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Model ID", Required = true };
        var command = new Command("get", "Get one 3D model")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "is_presupported and is_unsupported are derived from is_supported, which",
            "models update writes. Both false means unknown, not unsupported.");
        command.AddExamples("grimoire-cli models get --id <model-id>");
        command.AddResponseExample<Generated.Models.Model3DDetailResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
            var result = await service.GetAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateThumbnailCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Model ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("thumbnail", "Download the model's rendered thumbnail")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Rendered from the geometry during a scan. Only .stl renders one, so a",
            "model in any other format is a 404 — as is one whose has_thumbnail is",
            "false in models list.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli models thumbnail --id <id> --output mini.webp",
            "grimoire-cli models thumbnail --id <id> --output - > mini.webp");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
            await using var stream = await service.ThumbnailAsync(parseResult.GetValue(idOption)!);
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

`SavedFile` is the existing receipt type used by `books thumbnail` — check its
namespace with `grep -rn "class SavedFile" src/GrimoireCli/` and add the `using`
if it is not already in scope.

`Create()` carries only the three reads for now. Tasks 3 and 4 add the writes and
the folder group to this same method; do not add a placeholder for either.

- [ ] **Step 4: Register the group**

Modify `src/GrimoireCli/Program.cs`. Add after the `MapsCommand` line:

```csharp
rootCommand.Subcommands.Add(ModelsCommand.Create());
```

- [ ] **Step 5: Run the tests and read the rendered help**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ModelsCommandTests`
Expected: PASS.

Then read the rendered help — never judge help from source:

```bash
dotnet run --project src/GrimoireCli -- models list --help
dotnet run --project src/GrimoireCli -- models get --help
dotnet run --project src/GrimoireCli -- models thumbnail --help
```

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/ModelsCommand.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/ModelsCommandTests.cs
git commit -m "feat: add models list, get and thumbnail"
```

---

### Task 3: `models update`, `models batch-update`, `models batch-tag`

**Files:**
- Modify: `src/GrimoireCli/Commands/ModelsCommand.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/ModelsCommandTests.cs` (append)
- Modify: `tests/GrimoireCli.Tests/Commands/ResponseShapeExitCodeAlignmentTests.cs`

**Interfaces:**
- Consumes: `ModelsService.UpdateAsync`, `BatchUpdateAsync`, `BatchTagAsync`.
- Produces: subcommands `update`, `batch-update`, `batch-tag`.

- [ ] **Step 1: Write the failing tests**

Append inside the `ModelsCommandTests` class:

```csharp
    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["models", sub]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(ModelsCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    // The sharp one: is_supported can leave unknown but never return to it.
    [Fact]
    public void UpdateSaysIsSupportedIsOneWay()
    {
        var help = Help(["models", "update"]);
        Assert.Contains("one-way", help);
        Assert.Contains("unknown", help);
    }

    // Only an unresolved id is per-item here; models passes no validate hook.
    [Fact]
    public void BatchUpdateSaysOnlyAnUnresolvedIdIsPerItem()
    {
        var help = Help(["models", "batch-update"]);
        Assert.Contains("unresolved id", help);
        Assert.Contains("422", help);
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["models", "batch-update"]));
        Assert.Contains("1000", Help(["models", "batch-tag"]));
    }

    [Fact]
    public void UpdateRejectsAFieldModel3DUpdateDoesNotDeclare()
    {
        Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"is_suported\":true}",
                GrimoireCli.Generated.Models.Model3DUpdate.CreateFromDiscriminatorValue,
                "pass it with --id"));
    }

    [Fact]
    public void UpdateRendersTheRequestShape()
    {
        Assert.Contains("\"is_supported\"", Help(["models", "update"], full: true));
    }
```

Add `using GrimoireCli.Commands;` is already present; the `JsonBodyInput` and
`BodyInputException` types live in that same namespace.

In `ResponseShapeExitCodeAlignmentTests.cs`, add two rows to the `cases` array
after the `maps` rows:

```csharp
            (ModelsCommand.Create(), ["models", "batch-update"], "errors"),
            (ModelsCommand.Create(), ["models", "batch-tag"], "errors"),
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "ModelsCommandTests|ResponseShapeExitCodeAlignment"`
Expected: FAIL — the three subcommands do not exist.

- [ ] **Step 3: Add the three subcommands**

Register them in `Create()` between `thumbnail` and the end:

```csharp
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateBatchUpdateCommand());
        command.Subcommands.Add(CreateBatchTagCommand());
```

Then the three methods:

```csharp
    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Model ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update one model's metadata")
        {
            idOption, inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "Clear description with \"\"; an explicit null does nothing.",
            "",
            "is_supported is one-way: a model whose support state is unknown can be",
            "set true or false, but nothing sets it back to unknown — a null is",
            "dropped and the write still answers ok.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli models get --id <id>");
        command.AddExamples(
            "grimoire-cli models update --id <id> --input meta.json",
            "echo '{\"is_supported\":true}' | grimoire-cli models update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.Model3DUpdate>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.Model3DUpdate.CreateFromDiscriminatorValue,
                    "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
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
        var command = new Command("batch-update", "Update many models in one transaction")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 items. Each item requires id.",
            "",
            "Only an unresolved id lands in errors, and the rest apply. Exit 3 is",
            "HTTP 200 with a non-empty errors list — a partial write.",
            "",
            "Nothing else is per-item: a schema-invalid item 422s the whole batch",
            "and nothing is written. No tag may contain / or \\.",
            "",
            "is_supported cannot be cleared here either — see models update.");
        command.AddExamples(
            "grimoire-cli models batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli models batch-update --stdin");
        command.AddRequestShape<Generated.Models.Model3DBulkUpdate>();
        command.AddResponseExample<Generated.Models.BulkResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.Model3DBulkUpdate.CreateFromDiscriminatorValue,
                    "pass each id inside items");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
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
        var command = new Command("batch-tag", "Add tags to many models, additively")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 ids, and at least one tag; an empty list either side is a 422.",
            "",
            "Additive — it never removes a tag. models update replaces the set.",
            "",
            "Only an unresolved id lands in errors. Exit 3 is HTTP 200 with a",
            "non-empty errors list — a partial write.");
        command.AddExamples(
            "grimoire-cli models batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"goblin\"]}' | grimoire-cli models batch-tag --stdin");
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
            var service = new ModelsService(client);
            var result = await service.BatchTagAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));
        });
        return command;
    }
```

- [ ] **Step 4: Run the tests and read the rendered help**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS, whole suite.

```bash
dotnet run --project src/GrimoireCli -- models update --help
dotnet run --project src/GrimoireCli -- models batch-update --help
dotnet run --project src/GrimoireCli -- models batch-tag --help
```

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/ModelsCommand.cs tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add models update, batch-update and batch-tag"
```

---

### Task 4: `models folders list|set|batch-set`

**Files:**
- Create: `src/GrimoireCli/Commands/ModelFolderCommands.cs`
- Modify: `src/GrimoireCli/Commands/ModelsCommand.cs` (one registration line)
- Create: `tests/GrimoireCli.Tests/Commands/ModelFolderCommandTests.cs`

**Interfaces:**
- Consumes: `ModelsService.FoldersListAsync`, `FoldersSetAsync`, `FoldersBatchSetAsync`.
- Produces: `ModelFolderCommands.Create()` returning a `Command` named `folders`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/ModelFolderCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ModelFolderCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(ModelsCommand.Create(), path, full);

    [Fact]
    public void FoldersListParsesWithNoArguments()
    {
        Assert.Empty(ModelsCommand.Create().Parse(["folders", "list"]).Errors);
    }

    [Fact]
    public void FoldersListDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["models", "folders", "list"]));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("batch-set")]
    public void FolderWritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["models", "folders", sub]));
    }

    // The endpoint is keyed by the path in the body, not a parent id.
    [Fact]
    public void FoldersSetTakesNoId()
    {
        Assert.DoesNotContain("--id", Help(["models", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["folders", "set"]).Errors);
        Assert.NotEmpty(ModelsCommand.Create()
            .Parse(["folders", "set", "--stdin", "--input", "f.json"]).Errors);
    }

    // Diffing a write against a read otherwise looks like data loss.
    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["models", "folders", "list"]));
    }

    // A typo'd path creates a row nothing can reach afterwards.
    [Fact]
    public void FoldersSetWarnsThatARowIsPermanent()
    {
        Assert.Contains("permanent", Help(["models", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["models", "folders", "set"], full: true));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ModelFolderCommandTests`
Expected: FAIL — `ModelFolderCommands` does not exist.

- [ ] **Step 3: Write the command file**

Create `src/GrimoireCli/Commands/ModelFolderCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Folder tags under `models`. A second tagging layer addressed by path, keyed by
/// the path in the body rather than by a parent id. There is no delete: the
/// collection offers none.
/// </summary>
public static class ModelFolderCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("folders", "Model folders and their tags");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateBatchSetCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List tagged model folders");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Reports what has been tagged, never what is on disk — a row exists",
            "only once a path has been given tags.",
            "",
            "Tags come back in display casing here; set and batch-set echo the",
            "stored internal keys instead.");
        command.AddExamples("grimoire-cli models folders list");
        command.AddResponseExample<Generated.Models.Model3DFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new ModelsService(client);
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
        var command = new Command("set", "Replace one model folder's tags")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the folder's set; an empty list clears it and keeps the",
            "row. The folder is addressed by path in the body: a path that is not",
            "on disk still creates a row, and no endpoint removes one — a typo is",
            "permanent.",
            "",
            "A tag reaches every model at or below the path in tags items and",
            "search. models get matches folder_path exactly, so folder_tags on a",
            "model in a subfolder of the tagged path reads empty.");
        command.AddExamples(
            "echo '{\"path\":\"Goblins\",\"tags\":[\"goblin\"]}' | grimoire-cli models folders set --stdin");
        command.AddRequestShape<Generated.Models.FolderTagsUpdate>();
        command.AddResponseExample<Generated.Models.Backend__routers__models___schemas__FolderTagsOut>();
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
            var service = new ModelsService(client);
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
        var command = new Command("batch-set", "Set tags on many model folders in one transaction")
        {
            inputOption, stdinOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 folders. Each replaces that folder's tags, as set does, and",
            "each creates a permanent row the same way.",
            "",
            "All or nothing: there is no per-item error list and no exit 3 here.");
        command.AddExamples("grimoire-cli models folders batch-set --input folders.json");
        command.AddRequestShape<Generated.Models.BulkFolderTags>();
        command.AddResponseExample<Generated.Models.Model3DFoldersResponse>();
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
            var service = new ModelsService(client);
            var result = await service.FoldersBatchSetAsync(body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

If `Backend__routers__models___schemas__FolderTagsOut` does not resolve, find the
real name and use the **models** one — four collections declare that schema:

```bash
ls src/GrimoireCli/Generated/Models/ | grep FolderTagsOut
```

- [ ] **Step 4: Wire the folder group**

In `ModelsCommand.cs`, add the folder group as the last subcommand in `Create()`:

```csharp
        command.Subcommands.Add(ModelFolderCommands.Create());
```

- [ ] **Step 5: Run the tests and read the rendered help**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS, whole suite.

```bash
dotnet run --project src/GrimoireCli -- models --help
dotnet run --project src/GrimoireCli -- models folders list --help
dotnet run --project src/GrimoireCli -- models folders set --help
dotnet run --project src/GrimoireCli -- models folders batch-set --help
```

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/ tests/GrimoireCli.Tests/Commands/ModelFolderCommandTests.cs
git commit -m "feat: add models folders list, set and batch-set"
```

---

### Task 5: STL fixtures and the smoke-test block

**Files:**
- Modify: `docker/make-fixtures.py`
- Modify: `docker/seed.sh`
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: every command from Tasks 2–4.
- Produces: a `models/` fixture tree with one model under a `Presupported/`
  folder and one under `Unsupported/`, so both derived flags are observable.
  The two carry different filenames so neither the path nor the intent is
  ambiguous.

- [ ] **Step 1: Add an `--stl` mode to the fixture generator**

`docker/make-fixtures.py` currently generates PDFs and PNGs with PyMuPDF. A
binary STL needs no library — it is an 80-byte header, a little-endian uint32
triangle count, then 50 bytes per triangle (12 floats plus a 2-byte attribute
count). Add:

```python
def make_stl(path: str) -> None:
    """A one-triangle binary STL.

    Grimoire renders thumbnails for .stl only (backend/indexer/models3d.py), and
    reads the triangle count straight out of the header — so a single real
    triangle is enough to index, count and render. Written with struct rather
    than a mesh library: none is installed, and the format is 84 bytes of header
    plus 50 per facet.
    """
    import struct

    header = b"grimoire-cli fixture".ljust(80, b"\0")
    triangle = struct.pack(
        "<12fH",
        0.0, 0.0, 1.0,   # normal
        0.0, 0.0, 0.0,   # vertex 1
        1.0, 0.0, 0.0,   # vertex 2
        0.0, 1.0, 0.0,   # vertex 3
        0,               # attribute byte count
    )
    with open(path, "wb") as fh:
        fh.write(header + struct.pack("<I", 1) + triangle)
```

And extend the dispatcher:

```python
    if len(sys.argv) == 3 and sys.argv[1] == "--png":
        make_png(sys.argv[2])
    elif len(sys.argv) == 3 and sys.argv[1] == "--stl":
        make_stl(sys.argv[2])
    elif len(sys.argv) == 3:
        make_pdf(sys.argv[1], int(sys.argv[2]))
    else:
        sys.exit(
            "Usage: make-fixtures.py <path> <pages>\n"
            "       make-fixtures.py --png <path>\n"
            "       make-fixtures.py --stl <path>"
        )
```

Update the module docstring's Usage block to list `--stl` too.

- [ ] **Step 2: Seed the model fixtures**

In `docker/seed.sh`, beside the existing map-fixture block and before the rescan
wait, add:

```bash
# Model fixtures. The supported/unsupported flag is inferred folder-level, not
# per file, so the same mini goes under both to make each derived flag
# observable.
mkdir -p "$LIBRARY/models/Goblins/Presupported" "$LIBRARY/models/Goblins/Unsupported"
python3 "$HERE/make-fixtures.py" --stl "$LIBRARY/models/Goblins/Presupported/Goblin Archer.stl"
python3 "$HERE/make-fixtures.py" --stl "$LIBRARY/models/Goblins/Unsupported/Goblin Shaman.stl"
say "wrote 2 fixture models"
```

- [ ] **Step 3: Verify the fixtures index**

```bash
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
dotnet run --project src/GrimoireCli -- models list
```

Expected: two models, `Goblin Archer` reporting `is_presupported: true` and
`Goblin Shaman` reporting `is_unsupported: true`.

The inference reads the whole relative path against two regexes in
`backend/indexer/media.py:355-362`. `_UNSUPPORTED_RE` is tried **first and
deliberately** — "unsupported" contains "supported", so a supported-first check
would label every unsupported file presupported. Both `Presupported/` and
`Unsupported/` match as written, case-insensitively. If both rows come back
false, the tree did not land where the scanner walks; check the path before
touching the names.

- [ ] **Step 4: Add the smoke-test block**

In `docker/smoke-test.sh`, after the `maps` block, add a `models` block. Follow
the file's idiom exactly — `"$CLI"`, `ok`/`fail`, `jq -e`, output into `"$WORK"`.

```bash
# ---- models -----------------------------------------------------------------
"$CLI" models list >"$WORK/models.out" 2>"$WORK/models.err" \
  || { cat "$WORK/models.err" >&2; fail "models list exited non-zero"; }
jq -e '.total >= 2 and (.models | length) >= 2' "$WORK/models.out" >/dev/null \
  || fail "models list should report the seeded models: $(cat "$WORK/models.out")"
ok "models list returns the seeded models"

"$CLI" models list --limit 1 >"$WORK/models-limit.out" 2>&1 \
  || fail "models list --limit exited non-zero"
jq -e '(.models | length) == 1' "$WORK/models-limit.out" >/dev/null \
  || fail "--limit 1 should return one row: $(cat "$WORK/models-limit.out")"
ok "models list --limit bounds the page"

# The folder-level inference: one fixture under Presupported/, one under
# Unsupported/, so each derived flag has a row that carries it.
jq -e '[.models[] | select(.is_presupported == true)] | length >= 1' "$WORK/models.out" >/dev/null \
  || fail "no presupported fixture: $(cat "$WORK/models.out")"
jq -e '[.models[] | select(.is_unsupported == true)] | length >= 1' "$WORK/models.out" >/dev/null \
  || fail "no unsupported fixture: $(cat "$WORK/models.out")"
ok "the derived support pair reflects the fixture folders"

MODEL_ID=$(jq -r '.models[] | select(.is_unsupported == true) | .id' "$WORK/models.out" | head -1)
[ -n "$MODEL_ID" ] || fail "no unsupported fixture id: $(cat "$WORK/models.out")"

"$CLI" models get --id "$MODEL_ID" >"$WORK/modelget.out" 2>&1 \
  || fail "models get exited non-zero"
jq -e 'has("folder_path") and has("folder_tags") and has("is_presupported")' \
  "$WORK/modelget.out" >/dev/null \
  || fail "models get should carry folder context and the pair: $(cat "$WORK/modelget.out")"
ok "models get returns folder context and the derived pair"

# Fixed target state, so a re-run converges: this model ends presupported.
echo '{"is_supported":true}' | "$CLI" models update --id "$MODEL_ID" --stdin >/dev/null 2>&1 \
  || fail "models update exited non-zero"
"$CLI" models get --id "$MODEL_ID" >"$WORK/modelget2.out" 2>&1
jq -e '.is_presupported == true' "$WORK/modelget2.out" >/dev/null \
  || fail "is_supported true should read back as presupported: $(cat "$WORK/modelget2.out")"
ok "models update writes is_supported and the derived pair follows"

# The one-way rule: a null is dropped, the write still answers ok, and the
# value does not return to unknown. This is what the help text claims.
echo '{"is_supported":null}' | "$CLI" models update --id "$MODEL_ID" --stdin >"$WORK/modelnull.out" 2>&1 \
  || fail "models update with a null exited non-zero"
"$CLI" models get --id "$MODEL_ID" >"$WORK/modelget3.out" 2>&1
jq -e '.is_presupported == true' "$WORK/modelget3.out" >/dev/null \
  || fail "a null should have changed nothing: $(cat "$WORK/modelget3.out")"
ok "models update cannot return is_supported to unknown"

# The symptom the whole group exists to fix: tagging a model directly.
echo "{\"ids\":[\"$MODEL_ID\"],\"tags\":[\"smoke-model\"]}" \
  | "$CLI" models batch-tag --stdin >/dev/null 2>&1 \
  || fail "models batch-tag exited non-zero"
"$CLI" tags items --tag smoke-model --resource-type model >"$WORK/modeltag.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$MODEL_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/modeltag.out" >/dev/null \
  || fail "the tagged model should be findable: $(cat "$WORK/modeltag.out")"
ok "models batch-tag tags a model without duplicates merge-metadata"

echo '{"path":"Goblins","tags":["Smoke Models"]}' \
  | "$CLI" models folders set --stdin >/dev/null 2>&1 \
  || fail "models folders set exited non-zero"
"$CLI" models folders list >"$WORK/modelflist.out" 2>&1 \
  || fail "models folders list exited non-zero"
jq -e '[.folders[] | select(.path == "Goblins") | .tags[]] | any(. == "Smoke Models")' \
  "$WORK/modelflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/modelflist.out")"
ok "models folders set writes a tag that lists in display casing"

"$CLI" models thumbnail --id "$MODEL_ID" --output "$WORK/mini.webp" >/dev/null 2>&1 \
  || fail "models thumbnail exited non-zero"
[ -s "$WORK/mini.webp" ] || fail "models thumbnail wrote no bytes"
ok "models thumbnail downloads the rendered image"

# An unknown field is refused client-side: exit 1, and no request is made.
set +e
echo '{"is_suported":true}' | "$CLI" models update --id "$MODEL_ID" --stdin \
  >/dev/null 2>"$WORK/modeltypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown model field should exit 1, got $rc: $(cat "$WORK/modeltypo.err")"
grep -q "is_suported" "$WORK/modeltypo.err" || fail "no offending field named: $(cat "$WORK/modeltypo.err")"
ok "models update refuses an unknown field before any request"
```

**On the thumbnail assertion:** the render happens during the scan and may not
exist for a one-triangle STL. Run it and see. If the endpoint 404s, replace that
assertion with one that asserts the 404 surfaces as a non-zero exit and say so in
your report — do not leave an assertion that passes either way.

- [ ] **Step 5: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: both pass. The second run is the idempotence check. Note the hazard:
`is_supported` is one-way, so the block must leave it at a fixed state (true)
rather than toggling — which the assertions above already do.

- [ ] **Step 6: Commit**

```bash
git add docker/make-fixtures.py docker/seed.sh docker/smoke-test.sh
git commit -m "test: seed model fixtures and cover the models commands live"
```

---

### Task 6: Documentation

**Files:**
- Modify: `README.md`, `tools/generate-api-coverage.py`,
  `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`,
  `docs/roadmap.md`, `CLAUDE.md`

- [ ] **Step 1: README Commands table**

Add nine rows after the `maps folders batch-set` row:

```markdown
| `models list [--limit <n>] [--offset <n>]` | List 3D models (defaults to 100 results) |
| `models get --id <id>` | Get one model, with its derived support pair |
| `models thumbnail --id <id> --output <path\|->` | Download a model's rendered thumbnail |
| `models update --id <id> {--input <file> \| --stdin}` | Update one model's metadata (gm or admin) |
| `models batch-update {--input <file> \| --stdin}` | Update many models in one transaction; exit 3 if partial (gm or admin) |
| `models batch-tag {--input <file> \| --stdin}` | Add tags to many models, additively; exit 3 if partial (gm or admin) |
| `models folders list` | List tagged model folders |
| `models folders set {--input <file> \| --stdin}` | Replace one model folder's tags (gm or admin) |
| `models folders batch-set {--input <file> \| --stdin}` | Set tags on many model folders in one transaction (gm or admin) |
```

- [ ] **Step 2: API coverage**

Add to `IMPLEMENTED` in `tools/generate-api-coverage.py`:

```python
    "GET /api/models": "`models list` ✅",
    "GET /api/models/{model_id}": "`models get` ✅",
    "GET /api/models/{model_id}/thumbnail": "`models thumbnail` ✅",
    "PATCH /api/models/{model_id}": "`models update` ✅",
    "POST /api/models/bulk": "`models batch-update` ✅",
    "POST /api/models/bulk/tags": "`models batch-tag` ✅",
    "GET /api/model-folders": "`models folders list` ✅",
    "PATCH /api/model-folders": "`models folders set` ✅",
    "POST /api/model-folders/bulk": "`models folders batch-set` ✅",
```

Then regenerate against the running stack:

```bash
python3 tools/generate-api-coverage.py
```

Expected: `models` moves 0/10 → 9/10 and the total rises by 9.

- [ ] **Step 3: API notes**

Add a `## Models` section to `docs/grimoire-api-notes.md`, after `## Maps`:

```markdown
## Models

Read from `backend/routers/models/core.py` and `_schemas.py` at tag `v1.7.1`,
and measured against the running 1.7.1 stack.

- **`is_supported` is a one-way trip.** The column is tri-state — true, false,
  or null meaning "the scanner could not tell" — but `update_model` applies
  `model_dump(exclude_none=True)` with no `model_fields_set` re-application
  (`core.py:195`), so a sent `null` is dropped and the write answers
  `{"status": "ok"}` having changed nothing. A model moves from unknown to true
  or false and never back. Maps rescues a sent `0` deliberately
  (`maps/core.py:630-633`); nothing here rescues a `null`. Measured: `null`
  after `true` read back as still presupported.
- **Read and write disagree about the same fact.** `Model3DOut` exposes the
  derived pair `is_presupported` / `is_unsupported` (`_schemas.py:43-52, 66-67`),
  both false when unknown; the write path takes the single `is_supported`. The
  field a caller reads is never the field it writes.
- **`GET /api/models` takes `limit` and `offset` only** (`core.py:32-33`), with
  `Query(100000)` and no `le=`. No folder or type filter, so unlike maps this
  endpoint only ever pages in SQL and a negative limit has one meaning.
- **Explicit rows are filtered server-side per account** (`core.py:37-40`), and
  variants never reach the list (`core.py:38`).
- **`bulk_update_models` passes no `validate` hook** (`core.py:200-212`), as on
  maps. Only `"Model not found"` reaches `errors`; a schema-invalid item 422s
  the whole batch with nothing written.
- **Supported/unsupported is inferred folder-level**, not per file —
  `Goblins/Presupported/goblin_a.stl` (`indexer/media.py:349`).
- **`.stl` is the only format that renders a thumbnail**
  (`indexer/models3d.py:67`); `serve_model_thumbnail` 404s on a miss rather than
  serving a placeholder (`core.py:183`).
- **Folder tags read and write differently**, as on maps: display casing on the
  read (`core.py:76`), stored internal keys echoed by the PATCH and the bulk
  (`core.py:91`, `core.py:237`). `model-folders` has no delete.
```

- [ ] **Step 4: Roadmap**

In `docs/roadmap.md`, delete the models item from `## Next` and renumber the
remaining four. Then **re-read the framing prose above `## Next`** and correct
anything the models layer has made false — the sharpest-symptom paragraph
currently names tokens, models and audio, and models is leaving that set. Add
no note that models shipped: the roadmap records intent, not status.

- [ ] **Step 5: CLAUDE.md**

The reset recipe under "Pre-PR verification" lists the fixture trees to remove.
Add `docker/library/models` alongside `docker/library/books` and
`docker/library/maps`.

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
git commit -m "docs: record the models command group and its verified behaviour"
```

---

## Self-review notes

- **Spec coverage.** All nine commands (Tasks 2–4), the JSON-body shape and the
  `--limit` floor/default (Tasks 2–3), both models-specific caveats (Tasks 2–3),
  the five ported caveats (Tasks 3–4), STL fixtures and live coverage of the
  one-way rule (Task 5), and every documentation item (Task 6).
- **Cross-cutting test.** `ResponseShapeExitCodeAlignmentTests` enumerates bulk
  commands by hand, so Task 3 adds the two models rows explicitly.
- **The NLog collection.** Task 1's test class carries `[Collection("NLog")]`
  because it sends through the pipeline. Omitting it reproduces the CI race the
  maps branch hit — a pre-existing test failing on line counts in a shared
  target.
- **Type consistency.** `ModelsService` method names are used identically in
  Tasks 2, 3 and 4. `Model3DListResponse`, `Model3DDetailResponse`,
  `Model3DUpdate`, `Model3DBulkUpdate`, `Model3DFoldersResponse` and the
  namespaced `Backend__routers__models___schemas__FolderTagsOut` are the
  generated names, verified present.
- **Out of scope.** `GET /api/models/{model_id}/file`, tracked in
  [#62](https://github.com/thomaslazar/grimoire-cli/issues/62).
