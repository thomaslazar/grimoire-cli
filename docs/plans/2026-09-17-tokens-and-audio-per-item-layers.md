# Tokens and audio per-item layers — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `tokens` and `audio` to the same per-item footing `maps` and
`models` have — nine commands each: list, get, one image getter, update, the two
batch verbs, and the three folder-tag verbs.

**Architecture:** A port of the shipped `models` group, done twice.
`src/GrimoireCli/Commands/ModelsCommand.cs`, `ModelFolderCommands.cs` and
`Services/ModelsService.cs` are the template — **read all three before starting
any task.** This plan gives the exact substitutions and the complete help text,
because the help is where the two collections genuinely differ and where a blind
port produces false claims.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client in
`src/GrimoireCli/Generated/` (already carries every builder and model — this plan
adds no generated code), xUnit, Python for the fixture generator.

**Spec:** [`docs/specs/2026-09-17-tokens-and-audio-per-item-layers-design.md`](../specs/2026-09-17-tokens-and-audio-per-item-layers-design.md)

## Global Constraints

- **Never hand-edit `src/GrimoireCli/Generated/`.** Everything needed exists.
- **Builder paths are plain here**, unlike models: `_client.Api.Api.Tokens`,
  `.TokenFolders`, `.Audio`, `.AudioFolders`, with namespaces matching. There is
  no `ModelsRequests`-style rename on either collection.
- **Generated type names are plain too:** `TokenUpdate`, `TokenBulkUpdate`,
  `TokenListResponse`, `TokenDetailResponse`, `TokenFoldersResponse`, and the
  same with `Audio`. No `Model3D` prefix analogue.
- **The folder-tag response model is namespaced** because four routers declare
  `FolderTagsOut`: use `Backend__routers__tokens___schemas__FolderTagsOut` and
  `Backend__routers__audio___schemas__FolderTagsOut`. Confirm with
  `ls src/GrimoireCli/Generated/Models/ | grep FolderTagsOut`.
- **Thin pass-through.** One command, one endpoint. No reading a response to emit
  a derived warning, no client-side mirroring of server policy.
- **stdout is the server's bytes** — `ConsoleOutput.WriteRawJson` for JSON,
  `ConsoleOutput.WriteStreamAsync` for the image getters.
- **Role tag ↔ permission hint must agree.** Every write is `gm or admin` with
  hint `"the gm or admin role"`. **No read carries a tag** on either collection.
- **`SavedFile` is in namespace `GrimoireCli.Models`** despite living in
  `src/GrimoireCli/Output/`. Both command files need `using GrimoireCli.Models;`.
- **Run `dotnet format GrimoireCli.sln`** after writing or modifying any C# file.
- **No unnecessary blank lines** inside method bodies.
- **Help text is terse.** No restating a flag's own description, no restating a
  field the generated request/response sample already renders.
- **Conventional Commits**, imperative, lowercase, no period, ≤72 chars. No
  `Co-Authored-By`, no "Generated with" attribution.
- **Batch cap, verbatim:** 1 to 1000 items, and no batch body may be empty.
- **`TokenUpdate` fields:** `description`, `tags`, `is_explicit`.
  **`AudioUpdate` fields:** `description`, `tags`. Neither is numerically bounded.

---

### Task 1: `TokensService`

**Files:**
- Create: `src/GrimoireCli/Services/TokensService.cs`
- Create: `tests/GrimoireCli.Tests/Services/TokensServiceTests.cs`

**Interfaces:**
- Produces: `TokensService(GrimoireApiClient)` with `ListAsync(int? limit, int? offset)`,
  `GetAsync(string id)`, `ThumbnailAsync(string id)` → `Task<Stream>`,
  `UpdateAsync(string id, string rawBody)`, `BatchUpdateAsync(string rawBody)`,
  `BatchTagAsync(string rawBody)`, `FoldersListAsync()`,
  `FoldersSetAsync(string rawBody)`, `FoldersBatchSetAsync(string rawBody)` —
  all `Task<string>` except `ThumbnailAsync`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Services/TokensServiceTests.cs`. This is
`tests/GrimoireCli.Tests/Services/ModelsServiceTests.cs` with `Model`→`Token`
and the route strings changed. **Read that file first**; it carries the
`[Collection("NLog")]` attribute and the `Uri()` helper that sets
`info.PathParameters["baseurl"]` before reading `.URI.AbsoluteUri`.

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
public class TokensServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListSendsOnlyThePagingItWasGiven()
    {
        var info = Client().Api.Api.Tokens.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = 100;
            c.QueryParameters.Offset = null;
        });
        var uri = Uri(info);
        Assert.Contains("limit=100", uri);
        Assert.DoesNotContain("offset", uri);
    }

    // The folder endpoints are keyed by a path in the body, not by an id in the
    // URL — a regeneration that moved them under /tokens would break callers.
    [Fact]
    public void FolderRoutesAreTopLevelTokenFolders()
    {
        var client = Client();
        Assert.EndsWith("/api/token-folders", Uri(client.Api.Api.TokenFolders.ToGetRequestInformation()));
        Assert.EndsWith("/api/token-folders/bulk",
            Uri(client.Api.Api.TokenFolders.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkFolderTags())));
    }

    [Fact]
    public void BulkRoutesAreDistinctFromTheItemRoute()
    {
        var client = Client();
        Assert.EndsWith("/api/tokens/bulk",
            Uri(client.Api.Api.Tokens.Bulk.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.TokenBulkUpdate())));
        Assert.EndsWith("/api/tokens/bulk/tags",
            Uri(client.Api.Api.Tokens.Bulk.Tags.ToPostRequestInformation(
                new GrimoireCli.Generated.Models.BulkAddTags())));
        Assert.EndsWith("/api/tokens/abc", Uri(client.Api.Api.Tokens["abc"].ToGetRequestInformation()));
        Assert.EndsWith("/api/tokens/abc/thumbnail",
            Uri(client.Api.Api.Tokens["abc"].Thumbnail.ToGetRequestInformation()));
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

    // Goes through TokensService itself rather than stopping at the builder, so a
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
        var service = new TokensService(new GrimoireApiClient(config, manager, handler));

        await service.UpdateAsync("abc", "{\"is_explicit\":true}");
        await service.BatchUpdateAsync("{\"items\":[{\"id\":\"abc\",\"is_explicit\":false}]}");
        await service.BatchTagAsync("{\"ids\":[\"abc\"],\"tags\":[\"goblin\"]}");
        await service.FoldersSetAsync("{\"path\":\"Monsters\",\"tags\":[\"goblin\"]}");
        await service.FoldersBatchSetAsync("{\"folders\":[{\"path\":\"Monsters\",\"tags\":[]}]}");

        Assert.Equal(new[]
        {
            (HttpMethod.Patch, "http://example.test/api/tokens/abc", "{\"is_explicit\":true}"),
            (HttpMethod.Post, "http://example.test/api/tokens/bulk", "{\"items\":[{\"id\":\"abc\",\"is_explicit\":false}]}"),
            (HttpMethod.Post, "http://example.test/api/tokens/bulk/tags", "{\"ids\":[\"abc\"],\"tags\":[\"goblin\"]}"),
            (HttpMethod.Patch, "http://example.test/api/token-folders", "{\"path\":\"Monsters\",\"tags\":[\"goblin\"]}"),
            (HttpMethod.Post, "http://example.test/api/token-folders/bulk", "{\"folders\":[{\"path\":\"Monsters\",\"tags\":[]}]}"),
        }, handler.Seen);
        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TokensServiceTests`
Expected: FAIL — `TokensService` does not exist, so the file does not compile.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/TokensService.cs`. This is
`src/GrimoireCli/Services/ModelsService.cs` with these substitutions and nothing
else changed:

| In `ModelsService.cs` | In `TokensService.cs` |
|---|---|
| class `ModelsService` | class `TokensService` |
| `"No model with that ID. List them with: grimoire-cli models list"` | `"No token with that ID. List them with: grimoire-cli tokens list"` |
| `_client.Api.Api.Models` | `_client.Api.Api.Tokens` |
| `_client.Api.Api.ModelFolders` | `_client.Api.Api.TokenFolders` |
| `Generated.Models.Model3DUpdate` | `Generated.Models.TokenUpdate` |
| `Generated.Models.Model3DBulkUpdate` | `Generated.Models.TokenBulkUpdate` |
| doc-comment "models"/"model" | "tokens"/"token" |

`Generated.Models.BulkAddTags`, `FolderTagsUpdate` and `BulkFolderTags` are
shared and stay as they are. The class doc-comment should read:

```csharp
/// <summary>
/// The nine endpoints behind `tokens` and `tokens folders`. The folder verbs live
/// here rather than in their own service because they are one collection's
/// endpoints, the way ModelsService carries `models folders`.
/// </summary>
```

Every raw-body method keeps the `SetStreamContent` line verbatim — that is what
makes the caller's bytes reach the server unaltered, and dropping it would send
an empty model instead.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TokensServiceTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/TokensService.cs tests/GrimoireCli.Tests/Services/TokensServiceTests.cs
git commit -m "feat: add TokensService covering the nine token endpoints"
```

---

### Task 2: The `tokens` command group

All nine commands, the registration, and the alignment-test rows.

**Files:**
- Create: `src/GrimoireCli/Commands/TokensCommand.cs`
- Create: `src/GrimoireCli/Commands/TokenFolderCommands.cs`
- Modify: `src/GrimoireCli/Program.cs` (one registration line)
- Modify: `tests/GrimoireCli.Tests/Commands/ResponseShapeExitCodeAlignmentTests.cs`
- Create: `tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs`
- Create: `tests/GrimoireCli.Tests/Commands/TokenFolderCommandTests.cs`

**Interfaces:**
- Consumes: every `TokensService` method from Task 1.
- Produces: `TokensCommand.Create()` returning a `Command` named `tokens`, and
  `TokenFolderCommands.Create()` returning one named `folders`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TokensCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TokensCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(TokensCommand.Create().Parse(["list"]).Errors);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("thumbnail")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["tokens", sub]));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["tokens", sub]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(TokensCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    [Fact]
    public void ListRendersItsLimitDefault()
    {
        Assert.Contains("[default: 100]", Help(["tokens", "list"]));
    }

    // Tokens carry is_explicit and the server filters on it per account. Audio
    // has no such field, so this claim is tokens-only and must be here.
    [Fact]
    public void ListSaysExplicitIsFilteredServerSide()
    {
        Assert.Contains("explicit permission", Help(["tokens", "list"]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(TokensCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    [Fact]
    public void BatchUpdateSaysOnlyAnUnresolvedIdIsPerItem()
    {
        var help = Help(["tokens", "batch-update"]);
        Assert.Contains("unresolved id", help);
        Assert.Contains("422", help);
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["tokens", "batch-update"]));
        Assert.Contains("1000", Help(["tokens", "batch-tag"]));
    }

    [Fact]
    public void BatchTagDocumentsTheForbiddenTagCharacters()
    {
        Assert.Contains("422 on the whole request", Help(["tokens", "batch-tag"]));
    }

    [Fact]
    public void UpdateRejectsAFieldTokenUpdateDoesNotDeclare()
    {
        Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"is_explict\":true}",
                GrimoireCli.Generated.Models.TokenUpdate.CreateFromDiscriminatorValue,
                "pass it with --id"));
    }

    [Fact]
    public void ThumbnailRequiresAnIdAndAnOutput()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["thumbnail", "--id", "x"]).Errors);
        Assert.NotEmpty(TokensCommand.Create().Parse(["thumbnail", "--output", "x.webp"]).Errors);
        Assert.Empty(TokensCommand.Create().Parse(["thumbnail", "--id", "x", "--output", "x.webp"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"tokens\"", Help(["tokens", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["tokens", "get"], full: true));
    }
}
```

Create `tests/GrimoireCli.Tests/Commands/TokenFolderCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TokenFolderCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TokensCommand.Create(), path, full);

    [Fact]
    public void FoldersListParsesWithNoArguments()
    {
        Assert.Empty(TokensCommand.Create().Parse(["folders", "list"]).Errors);
    }

    [Fact]
    public void FoldersListDeclaresNoRole()
    {
        Assert.DoesNotContain("Role required:", Help(["tokens", "folders", "list"]));
    }

    [Theory]
    [InlineData("set")]
    [InlineData("batch-set")]
    public void FolderWritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["tokens", "folders", sub]));
    }

    // The endpoint is keyed by the path in the body, not a parent id.
    [Fact]
    public void FoldersSetTakesNoId()
    {
        Assert.DoesNotContain("--id", Help(["tokens", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["folders", "set"]).Errors);
        Assert.NotEmpty(TokensCommand.Create()
            .Parse(["folders", "set", "--stdin", "--input", "f.json"]).Errors);
    }

    [Fact]
    public void FoldersListNotesTheDisplayCasingAsymmetry()
    {
        Assert.Contains("display", Help(["tokens", "folders", "list"]));
    }

    [Fact]
    public void FoldersSetWarnsThatARowIsPermanent()
    {
        Assert.Contains("permanent", Help(["tokens", "folders", "set"]));
    }

    [Fact]
    public void FoldersSetRendersItsRequestShape()
    {
        Assert.Contains("\"path\"", Help(["tokens", "folders", "set"], full: true));
    }
}
```

In `ResponseShapeExitCodeAlignmentTests.cs`, add two rows to the `cases` array
after the `models` rows:

```csharp
            (TokensCommand.Create(), ["tokens", "batch-update"], "errors"),
            (TokensCommand.Create(), ["tokens", "batch-tag"], "errors"),
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "TokensCommandTests|TokenFolderCommandTests"`
Expected: FAIL — neither command class exists.

- [ ] **Step 3: Write `TokensCommand.cs`**

Create `src/GrimoireCli/Commands/TokensCommand.cs` by porting
`src/GrimoireCli/Commands/ModelsCommand.cs`. Same structure, same six private
`Create*Command()` methods plus the folder registration, same action bodies with
`ModelsService` → `TokensService`. `Create()` registers, in order: `list`, `get`,
`thumbnail`, `update`, `batch-update`, `batch-tag`, then
`TokenFolderCommands.Create()`.

Group description: `"Read and edit token metadata"`.

The Notes blocks differ from models. Use exactly these.

`list` (options `--limit`, `--offset`; `AddResponseExample<Generated.Models.TokenListResponse>()`):

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "The account's explicit permission filters the list server-side.",
            "",
            "Page with --offset against total in the response.");
        command.AddExamples(
            "grimoire-cli tokens list",
            "grimoire-cli tokens list --limit 20 --offset 100");
```

`get` (`--id` required; `AddResponseExample<Generated.Models.TokenDetailResponse>()`)
carries **no Notes block** — models' only `get` note was about its derived
support pair, which tokens has no counterpart for, and everything else on the
response is rendered by the sample. Add the example only:

```csharp
        command.AddExamples("grimoire-cli tokens get --id <token-id>");
```

`thumbnail` (`--id` and `--output` both required;
`AddResponseExample<SavedFile>()`):

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Rendered from the image during a scan; 404 when has_thumbnail is false",
            "in tokens list.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli tokens thumbnail --id <id> --output goblin.webp",
            "grimoire-cli tokens thumbnail --id <id> --output - > goblin.webp");
```

`update`:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "Clear description with \"\"; an explicit null does nothing.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli tokens get --id <id>");
        command.AddExamples(
            "grimoire-cli tokens update --id <id> --input meta.json",
            "echo '{\"is_explicit\":true}' | grimoire-cli tokens update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.TokenUpdate>();
```

`batch-update`:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 items. Each item requires id.",
            "",
            "Only an unresolved id lands in errors, and the rest apply. Exit 3 is",
            "HTTP 200 with a non-empty errors list — a partial write.",
            "",
            "Nothing else is per-item: a schema-invalid item 422s the whole batch",
            "and nothing is written. No tag may contain / or \\.");
        command.AddExamples(
            "grimoire-cli tokens batch-update --input items.json",
            "jq -c '{items: .}' edits.json | grimoire-cli tokens batch-update --stdin");
        command.AddRequestShape<Generated.Models.TokenBulkUpdate>();
        command.AddResponseExample<Generated.Models.BulkResult>();
```

`batch-tag`:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 ids, and at least one tag; an empty list either side is a 422.",
            "",
            "Additive — it never removes a tag. tokens update replaces the set.",
            "",
            "Only an unresolved id lands in errors. Exit 3 is HTTP 200 with a",
            "non-empty errors list — a partial write. A tag containing / or \\ is a",
            "422 on the whole request, which writes nothing.");
        command.AddExamples(
            "grimoire-cli tokens batch-tag --input tags.json",
            "echo '{\"ids\":[\"<id>\"],\"tags\":[\"goblin\"]}' | grimoire-cli tokens batch-tag --stdin");
        command.AddRequestShape<Generated.Models.BulkAddTags>();
        command.AddResponseExample<Generated.Models.BulkTagResult>();
```

- [ ] **Step 4: Write `TokenFolderCommands.cs`**

Create `src/GrimoireCli/Commands/TokenFolderCommands.cs` by porting
`src/GrimoireCli/Commands/ModelFolderCommands.cs` with `Models`→`Tokens`,
`model`→`token`, `Model3DFoldersResponse`→`TokenFoldersResponse`, and
`Backend__routers__models___schemas__FolderTagsOut`→
`Backend__routers__tokens___schemas__FolderTagsOut`. Group description:
`"Token folders and their tags"`.

Notes blocks, exactly:

`list`:
```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Reports what has been tagged, never what is on disk — a row exists",
            "only once a path has been given tags.",
            "",
            "Tags come back in display casing here; set and batch-set echo the",
            "stored internal keys instead.");
```

`set`:
```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the folder's set; an empty list clears it and keeps the",
            "row. The folder is addressed by path in the body: a path that is not",
            "on disk still creates a row, and no endpoint removes one — a typo is",
            "permanent.",
            "",
            "A tag reaches every token at or below the path in tags items and",
            "search. tokens get matches folder_path exactly, so folder_tags on a",
            "token in a subfolder of the tagged path reads empty.");
        command.AddExamples(
            "echo '{\"path\":\"Monsters\",\"tags\":[\"goblin\"]}' | grimoire-cli tokens folders set --stdin");
```

`batch-set`:
```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "1 to 1000 folders. Each replaces that folder's tags, as set does, and",
            "each creates a permanent row the same way.",
            "",
            "All or nothing: there is no per-item error list and no exit 3 here.");
        command.AddExamples("grimoire-cli tokens folders batch-set --input folders.json");
```

- [ ] **Step 5: Register the group**

In `src/GrimoireCli/Program.cs`, add after the `ModelsCommand` line:

```csharp
rootCommand.Subcommands.Add(TokensCommand.Create());
```

- [ ] **Step 6: Run the tests and read the rendered help**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS, whole suite.

Then read every rendered help block — never judge help from source:

```bash
dotnet run --project src/GrimoireCli -- tokens --help
dotnet run --project src/GrimoireCli -- tokens list --help
dotnet run --project src/GrimoireCli -- tokens get --help
dotnet run --project src/GrimoireCli -- tokens thumbnail --help
dotnet run --project src/GrimoireCli -- tokens update --help
dotnet run --project src/GrimoireCli -- tokens batch-update --help
dotnet run --project src/GrimoireCli -- tokens batch-tag --help
dotnet run --project src/GrimoireCli -- tokens folders list --help
dotnet run --project src/GrimoireCli -- tokens folders set --help
dotnet run --project src/GrimoireCli -- tokens folders batch-set --help
```

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/TokensCommand.cs src/GrimoireCli/Commands/TokenFolderCommands.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add the tokens per-item command group"
```

---

### Task 3: `AudioService`

**Files:**
- Create: `src/GrimoireCli/Services/AudioService.cs`
- Create: `tests/GrimoireCli.Tests/Services/AudioServiceTests.cs`

**Interfaces:**
- Produces: `AudioService(GrimoireApiClient)` with `ListAsync(int? limit, int? offset)`,
  `GetAsync(string id)`, `ArtworkAsync(string id)` → `Task<Stream>`,
  `UpdateAsync(string id, string rawBody)`, `BatchUpdateAsync(string rawBody)`,
  `BatchTagAsync(string rawBody)`, `FoldersListAsync()`,
  `FoldersSetAsync(string rawBody)`, `FoldersBatchSetAsync(string rawBody)`.

Note the image method is **`ArtworkAsync`**, not `ThumbnailAsync` — audio has no
thumbnail endpoint, and the route is `/api/audio/{id}/artwork`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Services/AudioServiceTests.cs`. Identical in
shape to `TokensServiceTests` from Task 1 — including `[Collection("NLog")]`,
which is load-bearing for the same reason — with these differences:

- class `AudioServiceTests`, service `AudioService`
- `client.Api.Api.Audio`, `client.Api.Api.AudioFolders`
- `Generated.Models.AudioBulkUpdate`
- routes `/api/audio`, `/api/audio/abc`, `/api/audio/bulk`,
  `/api/audio/bulk/tags`, `/api/audio-folders`, `/api/audio-folders/bulk`
- the image route assertion is
  `Assert.EndsWith("/api/audio/abc/artwork", Uri(client.Api.Api.Audio["abc"].Artwork.ToGetRequestInformation()));`
- the raw-body test's bodies use audio's field set — `AudioUpdate` has only
  `description` and `tags`, so use:

```csharp
        await service.UpdateAsync("abc", "{\"description\":\"tavern loop\"}");
        await service.BatchUpdateAsync("{\"items\":[{\"id\":\"abc\",\"description\":\"x\"}]}");
        await service.BatchTagAsync("{\"ids\":[\"abc\"],\"tags\":[\"ambience\"]}");
        await service.FoldersSetAsync("{\"path\":\"Ambience\",\"tags\":[\"ambience\"]}");
        await service.FoldersBatchSetAsync("{\"folders\":[{\"path\":\"Ambience\",\"tags\":[]}]}");

        Assert.Equal(new[]
        {
            (HttpMethod.Patch, "http://example.test/api/audio/abc", "{\"description\":\"tavern loop\"}"),
            (HttpMethod.Post, "http://example.test/api/audio/bulk", "{\"items\":[{\"id\":\"abc\",\"description\":\"x\"}]}"),
            (HttpMethod.Post, "http://example.test/api/audio/bulk/tags", "{\"ids\":[\"abc\"],\"tags\":[\"ambience\"]}"),
            (HttpMethod.Patch, "http://example.test/api/audio-folders", "{\"path\":\"Ambience\",\"tags\":[\"ambience\"]}"),
            (HttpMethod.Post, "http://example.test/api/audio-folders/bulk", "{\"folders\":[{\"path\":\"Ambience\",\"tags\":[]}]}"),
        }, handler.Seen);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter AudioServiceTests`
Expected: FAIL — `AudioService` does not exist.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/AudioService.cs` by porting `TokensService.cs`
with `Tokens`→`Audio`, `TokenFolders`→`AudioFolders`, `TokenUpdate`→`AudioUpdate`,
`TokenBulkUpdate`→`AudioBulkUpdate`, and the not-found hint
`"No audio track with that ID. List them with: grimoire-cli audio list"`.

Rename the image method and point it at the artwork route:

```csharp
    /// <summary>
    /// GET /api/audio/{id}/artwork. Resolves a deliberately-set cover first, then
    /// folder art, then embedded album art; 404 when the track has none of them.
    /// </summary>
    public async Task<Stream> ArtworkAsync(string id)
    {
        var info = _client.Api.Api.Audio[id].Artwork.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter AudioServiceTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/AudioService.cs tests/GrimoireCli.Tests/Services/AudioServiceTests.cs
git commit -m "feat: add AudioService covering the nine audio endpoints"
```

---

### Task 4: The `audio` command group

**Files:**
- Create: `src/GrimoireCli/Commands/AudioCommand.cs`
- Create: `src/GrimoireCli/Commands/AudioFolderCommands.cs`
- Modify: `src/GrimoireCli/Program.cs` (one registration line)
- Modify: `tests/GrimoireCli.Tests/Commands/ResponseShapeExitCodeAlignmentTests.cs`
- Create: `tests/GrimoireCli.Tests/Commands/AudioCommandTests.cs`
- Create: `tests/GrimoireCli.Tests/Commands/AudioFolderCommandTests.cs`

**Interfaces:**
- Consumes: every `AudioService` method from Task 3.
- Produces: `AudioCommand.Create()` returning a `Command` named `audio`, and
  `AudioFolderCommands.Create()` returning one named `folders`.

**This is the task where a blind port produces false help. Two claims are
specific to audio and both were verified against the pinned v1.7.1 source:**

1. **Audio has no `is_explicit` anywhere** — not on the row, not on
   `AudioUpdate`, and `list_audio` (`audio/core.py:51-54`) takes only `limit`,
   `offset` and the session. **The explicit-filter line that `tokens list`,
   `books list` and `models list` all carry must NOT appear on `audio list`.**
2. **`duration`, `title`, `artist` and `album` are scan-derived and unwritable.**
   They are read from the file's tags at index time (`audio/core.py:38-40`), and
   `AudioUpdate` accepts none of them.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/AudioCommandTests.cs`. Same shape as
`TokensCommandTests` from Task 2 with `Tokens`→`Audio` and `thumbnail`→`artwork`,
**except** for these four, which encode the divergences:

```csharp
    // Audio has no is_explicit anywhere — claiming a server-side explicit filter
    // here would describe a field the collection does not have.
    [Fact]
    public void ListDoesNotClaimAnExplicitFilter()
    {
        Assert.DoesNotContain("explicit", Help(["audio", "list"]));
    }

    // duration/title/artist/album come from the file's tags at index time and
    // AudioUpdate accepts none of them, so a caller reading artist in a response
    // will otherwise try to PATCH it.
    [Fact]
    public void UpdateSaysTheTagMetadataIsUnwritable()
    {
        var help = Help(["audio", "update"]);
        Assert.Contains("artist", help);
        Assert.Contains("cannot be set here", help);
    }

    [Fact]
    public void ArtworkExplainsItsThreeSources()
    {
        var help = Help(["audio", "artwork"]);
        Assert.Contains("folder art", help);
        Assert.Contains("embedded", help);
        Assert.Contains("has_artwork", help);
    }

    [Fact]
    public void UpdateRejectsAFieldAudioUpdateDoesNotDeclare()
    {
        Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"artist\":\"nope\"}",
                GrimoireCli.Generated.Models.AudioUpdate.CreateFromDiscriminatorValue,
                "pass it with --id"));
    }
```

Drop `ListSaysExplicitIsFilteredServerSide` entirely — it is the tokens-only
claim. Keep the rest (role tags, limit floor and default, offset, batch caps,
the forbidden-characters note, request/response shape rendering), with
`ListRendersItsResponseShape` asserting `"audio"` and the artwork command's
required-flags test named `ArtworkRequiresAnIdAndAnOutput`.

Create `tests/GrimoireCli.Tests/Commands/AudioFolderCommandTests.cs` as
`TokenFolderCommandTests` with `Tokens`→`Audio` and `tokens`→`audio` throughout.

In `ResponseShapeExitCodeAlignmentTests.cs`, add two more rows:

```csharp
            (AudioCommand.Create(), ["audio", "batch-update"], "errors"),
            (AudioCommand.Create(), ["audio", "batch-tag"], "errors"),
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "AudioCommandTests|AudioFolderCommandTests"`
Expected: FAIL — neither command class exists.

- [ ] **Step 3: Write `AudioCommand.cs`**

Port `TokensCommand.cs` with `Tokens`→`Audio`, `token`→`audio track`,
`TokenListResponse`→`AudioListResponse`,
`TokenDetailResponse`→`AudioDetailResponse`, `TokenUpdate`→`AudioUpdate`,
`TokenBulkUpdate`→`AudioBulkUpdate`, and `thumbnail`→`artwork` (calling
`service.ArtworkAsync`). Group description: `"Read and edit audio metadata"`.

`Create()` registers `list`, `get`, `artwork`, `update`, `batch-update`,
`batch-tag`, then `AudioFolderCommands.Create()`.

Notes blocks, exactly:

`list` — **no explicit line**:
```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "Page with --offset against total in the response.");
        command.AddExamples(
            "grimoire-cli audio list",
            "grimoire-cli audio list --limit 20 --offset 100");
        command.AddResponseExample<Generated.Models.AudioListResponse>();
```

`get` — no Notes block, example only:
```csharp
        command.AddExamples("grimoire-cli audio get --id <audio-id>");
        command.AddResponseExample<Generated.Models.AudioDetailResponse>();
```

`artwork`:
```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Resolves three sources in order: a cover set deliberately, then folder",
            "art, then art embedded in the file. 404 when the track has none —",
            "has_artwork in audio list says whether any of the three exists.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli audio artwork --id <id> --output cover.jpg",
            "grimoire-cli audio artwork --id <id> --output - > cover.jpg");
        command.AddResponseExample<SavedFile>();
```

`update`:
```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "Clear description with \"\"; an explicit null does nothing.",
            "",
            "duration, title, artist and album are read from the file's tags at",
            "scan time and cannot be set here.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli audio get --id <id>");
        command.AddExamples(
            "grimoire-cli audio update --id <id> --input meta.json",
            "echo '{\"description\":\"tavern loop\"}' | grimoire-cli audio update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.AudioUpdate>();
```

`batch-update` and `batch-tag` are Task 2's blocks with `tokens`→`audio` and the
`goblin` example tag replaced by `ambience`.

- [ ] **Step 4: Write `AudioFolderCommands.cs`**

Port `TokenFolderCommands.cs` with `Tokens`→`Audio`, `token`→`audio track`,
`TokenFoldersResponse`→`AudioFoldersResponse`, the namespaced response model
`Backend__routers__audio___schemas__FolderTagsOut`, the example path
`"Ambience"` with tag `"ambience"`, and group description
`"Audio folders and their tags"`.

- [ ] **Step 5: Register the group**

In `src/GrimoireCli/Program.cs`, add after the `TokensCommand` line:

```csharp
rootCommand.Subcommands.Add(AudioCommand.Create());
```

- [ ] **Step 6: Run the tests and read the rendered help**

Run: `dotnet format GrimoireCli.sln` then
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS, whole suite.

```bash
dotnet run --project src/GrimoireCli -- audio --help
dotnet run --project src/GrimoireCli -- audio list --help
dotnet run --project src/GrimoireCli -- audio get --help
dotnet run --project src/GrimoireCli -- audio artwork --help
dotnet run --project src/GrimoireCli -- audio update --help
dotnet run --project src/GrimoireCli -- audio batch-update --help
dotnet run --project src/GrimoireCli -- audio batch-tag --help
dotnet run --project src/GrimoireCli -- audio folders list --help
dotnet run --project src/GrimoireCli -- audio folders set --help
dotnet run --project src/GrimoireCli -- audio folders batch-set --help
```

Confirm by eye that `audio list --help` contains no mention of explicit content.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/AudioCommand.cs src/GrimoireCli/Commands/AudioFolderCommands.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add the audio per-item command group"
```

---

### Task 5: Fixtures and the smoke-test blocks

**Files:**
- Modify: `docker/make-fixtures.py`
- Modify: `docker/seed.sh`
- Modify: `docker/smoke-test.sh`

- [ ] **Step 1: Add a `--wav` mode to the fixture generator**

`docker/make-fixtures.py` already has `--png` and `--stl`. Add `--wav` using the
stdlib `wave` module — no new dependency:

```python
def make_wav(path: str) -> None:
    """A short silent WAV.

    Grimoire indexes .wav (indexer/constants.py's AUDIO_EXTS) and reads duration
    from the file itself, so a real header is enough to get a non-zero duration
    with empty title/artist/album — which is exactly the shape the tag-metadata
    caveat describes. Written with the stdlib wave module; no audio library is
    installed in the devcontainer.
    """
    import wave

    with wave.open(path, "wb") as fh:
        fh.setnchannels(1)
        fh.setsampwidth(2)
        fh.setframerate(8000)
        fh.writeframes(b"\x00\x00" * 4000)  # half a second of silence
```

Extend the dispatcher with an `elif` for `--wav`, and add it to the module
docstring's Usage block alongside `--png` and `--stl`.

- [ ] **Step 2: Seed the fixtures**

In `docker/seed.sh`, beside the existing model block and before the rescan wait:

```bash
# Token and audio fixtures. Each collection gets a subfolder as well as a top
# folder, so the folder-tag inheritance gap — a tag on the parent reaches tags
# items and search but reads empty on an item in a child — is observable.
mkdir -p "$LIBRARY/tokens/Monsters/Undead" "$LIBRARY/audio/Ambience/Battle"
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/tokens/Monsters/Goblin.png"
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/tokens/Monsters/Undead/Skeleton.png"
python3 "$HERE/make-fixtures.py" --wav "$LIBRARY/audio/Ambience/Tavern.wav"
python3 "$HERE/make-fixtures.py" --wav "$LIBRARY/audio/Ambience/Battle/Drums.wav"
say "wrote 2 fixture tokens and 2 fixture audio tracks"
```

- [ ] **Step 3: Verify the fixtures index**

```bash
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
dotnet run --project src/GrimoireCli -- tokens list
dotnet run --project src/GrimoireCli -- audio list
```

Expected: two tokens and two audio tracks. On the audio rows check that
`duration` is greater than 0 while `title`, `artist` and `album` are empty
strings — that combination is what the unwritable-metadata caveat describes, and
it is what the smoke block will assert. **If `duration` comes back 0**, the
metadata reader could not parse the WAV; report what you find rather than
weakening the assertion.

- [ ] **Step 4: Add the smoke-test blocks**

In `docker/smoke-test.sh`, after the `models` block, add a `tokens` block and an
`audio` block. Follow the file's idiom exactly — `"$CLI"`, `ok`/`fail`, `jq -e`,
outputs into `"$WORK"` — and read the `models` block first, which is the
template.

**Pin every assertion to a fixture filename, never to a flag alone.** A flag-only
`select(...) | length >= 1` over a list captured before the run's own write stops
discriminating silently from the second run; that fault had to be fixed twice on
the models branch.

```bash
# ---- tokens -----------------------------------------------------------------
"$CLI" tokens list >"$WORK/tokens.out" 2>"$WORK/tokens.err" \
  || { cat "$WORK/tokens.err" >&2; fail "tokens list exited non-zero"; }
jq -e '.total >= 2 and (.tokens | length) >= 2' "$WORK/tokens.out" >/dev/null \
  || fail "tokens list should report the seeded tokens: $(cat "$WORK/tokens.out")"
ok "tokens list returns the seeded tokens"

"$CLI" tokens list --limit 1 >"$WORK/tokens-limit.out" 2>&1 \
  || fail "tokens list --limit exited non-zero"
jq -e '(.tokens | length) == 1' "$WORK/tokens-limit.out" >/dev/null \
  || fail "--limit 1 should return one row: $(cat "$WORK/tokens-limit.out")"
ok "tokens list --limit bounds the page"

TOKEN_ID=$(jq -r '.tokens[] | select(.filename == "Goblin.png") | .id' "$WORK/tokens.out")
[ -n "$TOKEN_ID" ] || fail "no Goblin.png fixture id: $(cat "$WORK/tokens.out")"

"$CLI" tokens get --id "$TOKEN_ID" >"$WORK/tokenget.out" 2>&1 \
  || fail "tokens get exited non-zero"
jq -e 'has("folder_path") and has("folder_tags") and has("is_explicit")' \
  "$WORK/tokenget.out" >/dev/null \
  || fail "tokens get should carry folder context: $(cat "$WORK/tokenget.out")"
ok "tokens get returns folder context"

"$CLI" tokens thumbnail --id "$TOKEN_ID" --output "$WORK/goblin.webp" >/dev/null 2>&1 \
  || fail "tokens thumbnail exited non-zero"
[ -s "$WORK/goblin.webp" ] || fail "tokens thumbnail wrote no bytes"
ok "tokens thumbnail downloads the rendered image"

echo "{\"ids\":[\"$TOKEN_ID\"],\"tags\":[\"smoke-token\"]}" \
  | "$CLI" tokens batch-tag --stdin >/dev/null 2>&1 \
  || fail "tokens batch-tag exited non-zero"
"$CLI" tags items --tag smoke-token --resource-type token >"$WORK/tokentag.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$TOKEN_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/tokentag.out" >/dev/null \
  || fail "the tagged token should be findable: $(cat "$WORK/tokentag.out")"
ok "tokens batch-tag tags a token without duplicates merge-metadata"

echo '{"path":"Monsters","tags":["Smoke Tokens"]}' \
  | "$CLI" tokens folders set --stdin >/dev/null 2>&1 \
  || fail "tokens folders set exited non-zero"
"$CLI" tokens folders list >"$WORK/tokenflist.out" 2>&1 \
  || fail "tokens folders list exited non-zero"
jq -e '[.folders[] | select(.path == "Monsters") | .tags[]] | any(. == "Smoke Tokens")' \
  "$WORK/tokenflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/tokenflist.out")"
ok "tokens folders set writes a tag that lists in display casing"

# An unknown field is refused client-side: exit 1, distinct from a server 422's 2.
set +e
echo '{"is_explict":true}' | "$CLI" tokens update --id "$TOKEN_ID" --stdin \
  >/dev/null 2>"$WORK/tokentypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown token field should exit 1, got $rc: $(cat "$WORK/tokentypo.err")"
grep -q "is_explict" "$WORK/tokentypo.err" || fail "no offending field named: $(cat "$WORK/tokentypo.err")"
ok "tokens update refuses an unknown field before any request"

# ---- audio ------------------------------------------------------------------
"$CLI" audio list >"$WORK/audio.out" 2>"$WORK/audio.err" \
  || { cat "$WORK/audio.err" >&2; fail "audio list exited non-zero"; }
jq -e '.total >= 2 and (.audio | length) >= 2' "$WORK/audio.out" >/dev/null \
  || fail "audio list should report the seeded tracks: $(cat "$WORK/audio.out")"
ok "audio list returns the seeded tracks"

AUDIO_ID=$(jq -r '.audio[] | select(.filename == "Tavern.wav") | .id' "$WORK/audio.out")
[ -n "$AUDIO_ID" ] || fail "no Tavern.wav fixture id: $(cat "$WORK/audio.out")"

# The tag metadata audio update cannot write: duration is read from the file,
# title/artist/album are empty because the fixture carries no tags.
jq -e '.audio[] | select(.filename == "Tavern.wav") | .duration > 0 and .title == ""' \
  "$WORK/audio.out" >/dev/null \
  || fail "the fixture should have a duration and no title: $(cat "$WORK/audio.out")"
ok "audio list reports scan-derived duration with empty tag metadata"

"$CLI" audio get --id "$AUDIO_ID" >"$WORK/audioget.out" 2>&1 \
  || fail "audio get exited non-zero"
jq -e 'has("folder_path") and has("folder_tags") and has("has_artwork")' \
  "$WORK/audioget.out" >/dev/null \
  || fail "audio get should carry folder context: $(cat "$WORK/audioget.out")"
ok "audio get returns folder context"

echo "{\"ids\":[\"$AUDIO_ID\"],\"tags\":[\"smoke-audio\"]}" \
  | "$CLI" audio batch-tag --stdin >/dev/null 2>&1 \
  || fail "audio batch-tag exited non-zero"
"$CLI" tags items --tag smoke-audio --resource-type audio >"$WORK/audiotag.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$AUDIO_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/audiotag.out" >/dev/null \
  || fail "the tagged track should be findable: $(cat "$WORK/audiotag.out")"
ok "audio batch-tag tags a track without duplicates merge-metadata"

echo '{"path":"Ambience","tags":["Smoke Audio"]}' \
  | "$CLI" audio folders set --stdin >/dev/null 2>&1 \
  || fail "audio folders set exited non-zero"
"$CLI" audio folders list >"$WORK/audioflist.out" 2>&1 \
  || fail "audio folders list exited non-zero"
jq -e '[.folders[] | select(.path == "Ambience") | .tags[]] | any(. == "Smoke Audio")' \
  "$WORK/audioflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/audioflist.out")"
ok "audio folders set writes a tag that lists in display casing"

# artist is scan-derived and AudioUpdate does not declare it, so this is refused
# client-side at exit 1 rather than reaching the server.
set +e
echo '{"artist":"nope"}' | "$CLI" audio update --id "$AUDIO_ID" --stdin \
  >/dev/null 2>"$WORK/audiotypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "writing artist should exit 1, got $rc: $(cat "$WORK/audiotypo.err")"
grep -q "artist" "$WORK/audiotypo.err" || fail "no offending field named: $(cat "$WORK/audiotypo.err")"
ok "audio update refuses the scan-derived artist field before any request"
```

**On `audio artwork`:** the seeded WAV carries no embedded art and its folder has
no cover image, so the endpoint will 404. Decide from what you observe — either
assert the 404 surfaces as a non-zero exit, or seed a folder cover image beside
the track so a real download can be asserted. Whichever you choose, **do not
leave an assertion that passes either way**, and say in your report which you did
and why.

- [ ] **Step 5: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: both pass. The second run is the idempotence check and is not
optional. All writes here are fixed values on seeded fixtures, so it converges.

- [ ] **Step 6: Commit**

```bash
git add docker/make-fixtures.py docker/seed.sh docker/smoke-test.sh
git commit -m "test: seed token and audio fixtures and cover both groups live"
```

---

### Task 6: Documentation

**Files:**
- Modify: `README.md`, `tools/generate-api-coverage.py`,
  `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`,
  `docs/roadmap.md`, `CLAUDE.md`

- [ ] **Step 1: README Commands table**

Add eighteen rows after the `models folders batch-set` row — nine for tokens,
nine for audio — following the exact style of the models rows directly above.
The flags must match what ships; check each against
`dotnet run --project src/GrimoireCli -- <group> <sub> --help` before writing the
row. Mark every write `(gm or admin)` and both batch verbs
`exit 3 if partial (gm or admin)`.

- [ ] **Step 2: API coverage**

Add eighteen `IMPLEMENTED` entries to `tools/generate-api-coverage.py`, following
the models entries' format:

```python
    "GET /api/tokens": "`tokens list` ✅",
    "GET /api/tokens/{token_id}": "`tokens get` ✅",
    "GET /api/tokens/{token_id}/thumbnail": "`tokens thumbnail` ✅",
    "PATCH /api/tokens/{token_id}": "`tokens update` ✅",
    "POST /api/tokens/bulk": "`tokens batch-update` ✅",
    "POST /api/tokens/bulk/tags": "`tokens batch-tag` ✅",
    "GET /api/token-folders": "`tokens folders list` ✅",
    "PATCH /api/token-folders": "`tokens folders set` ✅",
    "POST /api/token-folders/bulk": "`tokens folders batch-set` ✅",
    "GET /api/audio": "`audio list` ✅",
    "GET /api/audio/{audio_id}": "`audio get` ✅",
    "GET /api/audio/{audio_id}/artwork": "`audio artwork` ✅",
    "PATCH /api/audio/{audio_id}": "`audio update` ✅",
    "POST /api/audio/bulk": "`audio batch-update` ✅",
    "POST /api/audio/bulk/tags": "`audio batch-tag` ✅",
    "GET /api/audio-folders": "`audio folders list` ✅",
    "PATCH /api/audio-folders": "`audio folders set` ✅",
    "POST /api/audio-folders/bulk": "`audio folders batch-set` ✅",
```

Then regenerate against the running stack:

```bash
python3 tools/generate-api-coverage.py
```

Expected: `tokens` 0/10 → 9/10, `audio` 0/14 → 9/14, total up by 18.

- [ ] **Step 3: API notes**

Add a `## Tokens and audio` section to `docs/grimoire-api-notes.md`, after
`## Models`. Cross-reference the `## Models` facts that hold identically here
rather than restating them — the no-`validate`-hook rule, the null-is-a-no-op
rule, the folder-tag display/internal asymmetry and the permanent folder row all
apply to both collections unchanged. What this section must carry, each with a
source citation at tag `v1.7.1`:

```markdown
## Tokens and audio

Read from `backend/routers/tokens/` and `backend/routers/audio/` at tag
`v1.7.1`, and measured against the running 1.7.1 stack. The `## Models` facts
about the missing `validate` hook, the dropped `null`, and folder-tag handling
hold unchanged on both; only the differences are recorded here.

- **`audio` has no `is_explicit` at all** — not on the row, not on
  `AudioUpdate`, and `list_audio` (`audio/core.py:51-54`) takes only `limit`,
  `offset` and the session. `tokens` does filter per account
  (`tokens/core.py:34-37`), as `books` and `models` do. A caller cannot hide an
  audio track from a player by marking it explicit, because there is nothing to
  mark.
- **`audio` carries four scan-derived fields no endpoint can write.**
  `duration`, `title`, `artist` and `album` are read from the file's tags at
  index time (`audio/core.py:38-40`) and `AudioUpdate` declares none of them.
  Measured: a tagless WAV indexes with a real `duration` and empty strings for
  the other three.
- **`GET /api/audio/{id}/artwork` resolves three sources, then 404s**
  (`audio/core.py:145-170`): a cover set deliberately through the UI, then
  folder art, then art embedded in the file. `has_artwork` on the row says
  whether any exists. `has_cover` is true only for the first of the three, and
  nothing in the CLI reads or writes it yet.
- **The two list endpoints order differently.** `tokens` orders by
  `relative_path` (`tokens/core.py:41`), so a page is a contiguous run of
  folders in display order, as `maps` does. `audio` orders by `filename`
  (`audio/core.py:58`), so a page can straddle folders.
```

- [ ] **Step 4: Roadmap**

In `docs/roadmap.md`, delete both the tokens and audio items from `## Next` and
renumber the remainder. Then **re-read the whole file** and correct anything the
four completed collection layers have made false. At minimum: the paragraph
above `## Next` names which collections have `list`/`get`/`update`, and the
sharpest-symptom paragraph names tokens and audio as the ones still lacking a
way to set a tag — with all four shipped, that symptom no longer exists for any
collection and the paragraph cannot simply be re-aimed. Consider whether the
objective statement now reads as met.

Add no note that anything shipped. The roadmap records intent; git and
`grimoire-api-coverage.md` record status.

- [ ] **Step 5: CLAUDE.md**

The reset recipe under "Pre-PR verification" lists the fixture trees to remove.
Add `docker/library/tokens` and `docker/library/audio` alongside `books`, `maps`
and `models`.

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
git commit -m "docs: record the tokens and audio command groups"
```

---

## Self-review notes

- **Spec coverage.** Eighteen commands (Tasks 1–4), the two audio divergences
  with tests that would fail if either claim regressed (Task 4), the `--wav`
  fixture and both smoke blocks (Task 5), and every documentation item including
  the roadmap re-read and the CLAUDE.md reset recipe (Task 6).
- **Cross-cutting test.** `ResponseShapeExitCodeAlignmentTests` enumerates bulk
  commands by hand; Tasks 2 and 4 each add their two rows.
- **The NLog collection.** Both service test classes carry `[Collection("NLog")]`
  (Tasks 1 and 3). Omitting it reproduces the CI race the maps branch hit.
- **Filename-pinned smoke assertions** (Task 5) — the fault that needed fixing
  twice on the models branch.
- **Type consistency.** `TokensService`/`AudioService` method names are used
  identically in Tasks 2 and 4. The image methods are deliberately named
  differently — `ThumbnailAsync` for tokens, `ArtworkAsync` for audio — because
  the routes and the semantics differ.
- **Out of scope.** Audio's cover block
  ([#64](https://github.com/thomaslazar/grimoire-cli/issues/64)) and both `file`
  getters ([#62](https://github.com/thomaslazar/grimoire-cli/issues/62)).
