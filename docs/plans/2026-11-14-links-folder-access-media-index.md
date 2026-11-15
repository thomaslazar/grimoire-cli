# Linked Books, Folder Access Rules and Media Index Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap the Grimoire 1.11.0 routes for media index, the map-copies preview, linked books and folder access rules, as specified in `docs/specs/2026-11-14-links-folder-access-media-index-design.md`.

**Architecture:** Each command is a thin pass-through to one endpoint. Requests are built on the generated Kiota builders and sent with `GrimoireApiClient.SendAsync`, and stdout is the raw response. New groups get their own command file and service. Request builders are `internal` so tests can pin the URL without HTTP.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client, xUnit, bash smoke test against the local docker stack.

## Global Constraints

- Branch `feat/links-folder-access-media-index`. The local stack runs Grimoire 1.11.0 at `http://host.docker.internal:9481`. The source clone `temp/grimoire` is on `v1.11.0`.
- **Commits:** Conventional Commits. **No `Co-Authored-By:` line and no "Generated with" line**, in any commit.
- **Generated code:** never hand-edit `src/GrimoireCli/Generated/`.
- **Formatting:** run `dotnet format GrimoireCli.sln` after C# edits. No blank lines between consecutive `Subcommands.Add`/`AddOption` calls.
- **Role tags** must match the handler dependency:
  - `admin` for `files campaign-map-copies` and `files folder access *`
  - `gm or admin` for `books links add|remove`
  - no tag for `books links list` and the four `index` commands
- **`permissionHint`** mirrors the tag: `"the admin role"` / `"the gm or admin role"`.
- **Help text:** terse. Don't restate flags or response fields; a `ChoiceOption`'s values are rendered for you. Run `--help` on each new command before calling a task done.
- **Comments:** describe what the code does or why, never what was left out.
- **Scope:** no `systems factions` (it is deferred to the armory round), and no change to existing commands' behaviour.
- **Version floor** stays at 1.10.1. `MinSupportedVersion` is untouched.

---

### Task 1: Media `index` commands

**Files:**
- Modify: `src/GrimoireCli/Services/MapsService.cs`, `TokensService.cs`, `ModelsService.cs`, `AudioService.cs` (add `IndexAsync` and `IndexRequest` next to `FolderCountsAsync`/`FolderCountsRequest`)
- Modify: `src/GrimoireCli/Commands/MapsCommand.cs`, `TokensCommand.cs`, `ModelsCommand.cs`, `AudioCommand.cs` (add an `index` subcommand right after `folder-counts`)
- Create: `tests/GrimoireCli.Tests/Services/MediaIndexServiceTests.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`
- Also commit the spec and this plan in this task's commit.

**Interfaces:**
- Produces:
  - `MapsService.IndexRequest(string? mapType, MediaBrowseFilter filter)`, plus `IndexRequest(MediaBrowseFilter filter)` on `TokensService`, `ModelsService` and `AudioService`. Each is `internal` and returns `RequestInformation`.
  - The matching public `IndexAsync` methods, each returning `Task<string>`.

- [ ] **Step 1: Write the failing service test**

`tests/GrimoireCli.Tests/Services/MediaIndexServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each collection's index request to its own /index path: the four
/// services are near-identical copies, and every response has the same shape,
/// so a builder left unswapped would query another collection unnoticed.
/// </summary>
public class MediaIndexServiceTests
{
    private static readonly MediaBrowseFilter NoFilter = new(null, null, null, null, null, null, null);

    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    private static RequestInformation Request(string collection, MediaBrowseFilter filter)
    {
        var client = Client();
        return collection switch
        {
            "maps" => new MapsService(client).IndexRequest(null, filter),
            "tokens" => new TokensService(client).IndexRequest(filter),
            "audio" => new AudioService(client).IndexRequest(filter),
            "models" => new ModelsService(client).IndexRequest(filter),
            _ => throw new ArgumentException($"Unknown collection '{collection}'.", nameof(collection)),
        };
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("tokens")]
    [InlineData("audio")]
    [InlineData("models")]
    public void EachCollectionResolvesToItsOwnIndexRoute(string collection)
        => Assert.Equal($"http://example.test/api/{collection}/index", Uri(Request(collection, NoFilter)));

    [Theory]
    [InlineData("maps")]
    [InlineData("tokens")]
    [InlineData("audio")]
    [InlineData("models")]
    public void EachCollectionSendsEveryFilter(string collection)
    {
        var uri = Uri(Request(collection, new MediaBrowseFilter(
            "forest", "[\"x\"]", true, "2026-01-01T00:00:00Z", "battlemaps", "name", "desc")));
        foreach (var part in new[] { "q=forest", "tags=", "favorites=true", "added_since=", "folder=battlemaps", "sort=name", "order=desc" })
            Assert.Contains(part, uri);
    }

    [Fact]
    public void MapsSendsMapType()
        => Assert.Contains("map_type=battlemap",
            Uri(new MapsService(Client()).IndexRequest("battlemap", NoFilter)));
}
```

Before relying on the `MediaBrowseFilter` positional order used above, check it against `src/GrimoireCli/Services/MediaBrowseFilter.cs`, and adjust the test's argument order to match the record.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MediaIndexServiceTests`
Expected: compile failure because `IndexRequest` is not defined.

- [ ] **Step 3: Implement the service methods**

In `MapsService.cs`, after `FolderCountsRequest`:

```csharp
    /// <summary>GET /api/maps/index. Each first letter's (name sort) or month's (added_at sort) run, with its offset.</summary>
    public async Task<string> IndexAsync(string? mapType, MediaBrowseFilter filter)
        => await _client.SendAsync(IndexRequest(mapType, filter));

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation IndexRequest(string? mapType, MediaBrowseFilter filter)
        => _client.Api.Api.Maps.Index.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = mapType;
            c.QueryParameters.Q = filter.Query;
            c.QueryParameters.Tags = filter.Tags;
            c.QueryParameters.Favorites = filter.Favorites;
            c.QueryParameters.AddedSince = filter.AddedSince;
            c.QueryParameters.Folder = filter.Folder;
            c.QueryParameters.Sort = filter.Sort;
            if (filter.Order is not null)
                c.QueryParameters.OrderAsGetOrderQueryParameterType =
                    Enum.Parse<Generated.Api.Maps.IndexNamespace.GetOrderQueryParameterType>(filter.Order, true);
        });
```

Add the same to `TokensService`, `ModelsService` and `AudioService`, with three changes per copy:
- drop `mapType`
- swap `Maps` for `Tokens`/`Models`/`Audio` in both the builder and the enum namespace (`Generated.Api.Tokens.IndexNamespace.GetOrderQueryParameterType`, and so on)
- adjust the summary's collection noun

Check that each builder exposes `.Index` (`src/GrimoireCli/Generated/Api/<X>/<X>RequestBuilder.cs`). The `Models` builder lives under `Api/ModelsRequests/`, so mirror whatever `ModelsService.FolderCountsRequest` uses.

- [ ] **Step 4: Run the service test**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter MediaIndexServiceTests`
Expected: PASS.

- [ ] **Step 5: Write the failing command tests** (append to `MapsCommandTests.cs`)

```csharp
    [Fact]
    public void IndexSitsRightAfterFolderCounts()
    {
        var names = MapsCommand.Create().Subcommands.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("folder-counts") + 1, names.IndexOf("index"));
    }

    [Fact]
    public void IndexTakesTheListFiltersAndMapType()
        => Assert.Empty(MapsCommand.Create().Parse(
            ["index", "--map-type", "battlemap", "--query", "x", "--tags", "[\"x\"]",
             "--favorites", "--added-since", "2026-01-01T00:00:00Z", "--folder", "battlemaps",
             "--sort", "name", "--order", "desc"]).Errors);

    // Every other sort answers no entries, which reads as "empty library".
    [Fact]
    public void IndexSaysOnlyNameAndAddedAtProduceEntries()
        => Assert.Contains("added_at", Help(["maps", "index"]));
```

For tokens, models and audio, add one test each to their existing command test files, asserting that `index` parses with `--sort name`:

```csharp
    [Fact]
    public void IndexParsesWithTheBrowseFilters()
        => Assert.Empty(TokensCommand.Create().Parse(["index", "--sort", "name", "--order", "asc"]).Errors);
```

Use `ModelsCommand`/`AudioCommand` in the other two. If a test file lacks a `Help(...)` helper, copy the one `MapsCommandTests` uses.

- [ ] **Step 6: Implement the `index` subcommands**

In `MapsCommand.cs`, register right after `folder-counts`, then add:

```csharp
    private static Command CreateIndexCommand()
    {
        var browse = new MediaBrowseOptions();
        var mapTypeOption = /* same construction as in CreateFolderCountsCommand */;
        var command = new Command("index", "Where each letter or month starts in the sorted list")
        {
            mapTypeOption
        };
        browse.AddTo(command, "maps");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Entries come only for --sort name (one per first letter, # for digits",
            "and symbols) and --sort added_at (one per month); other sorts return",
            "none. offset is the run's position in maps list for the same filters.");
        command.AddExamples("grimoire-cli maps index --sort name");
        command.AddResponseExample<Generated.Models.BrowseIndexResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.IndexAsync(
                parseResult.GetValue(mapTypeOption),
                browse.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

Copy the `mapTypeOption` and `browse` construction exactly from that file's `folder-counts` command; don't invent new ones. Mirror the command into `TokensCommand`, `ModelsCommand` and `AudioCommand`, without `--map-type` and with each collection's noun in the Notes (`tokens list`, and so on).

- [ ] **Step 7: Run the build, the tests and the rendered help**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet run --project src/GrimoireCli --no-build -- maps index --help
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli maps index --sort name
```

Expected: everything passes, and the live call prints `{"entries":[...]}`. The local stack must be logged in as admin; if it isn't, run `printf admin | grimoire-cli login --server http://host.docker.internal:9481 --username admin --password-stdin`.

If `JsonExamplesDriftTest` fails, regenerate with `dotnet run --project tools/GenerateJsonExamples -- src/GrimoireCli/Commands/JsonExamples.g.cs`.

- [ ] **Step 8: Commit**

```bash
git add docs/specs/2026-11-14-links-folder-access-media-index-design.md docs/plans/2026-11-14-links-folder-access-media-index.md src tests
git commit -m "feat: add index commands for maps, tokens, models and audio"
```

---

### Task 2: `files campaign-map-copies`

**Files:**
- Modify: `src/GrimoireCli/Services/FilesService.cs`
- Modify: `src/GrimoireCli/Commands/FilesCommand.cs` (register after `delete`)
- Modify: `tests/GrimoireCli.Tests/Services/FilesServiceTests.cs`, `tests/GrimoireCli.Tests/Commands/FilesCommandTests.cs`

**Interfaces:**
- Produces:
  - `FilesService.CampaignMapCopiesRequest(string path)`: `internal`, returns `RequestInformation`
  - `FilesService.CampaignMapCopiesAsync(string path)`: returns `Task<string>`

- [ ] **Step 1: Write the failing tests**

In `FilesServiceTests.cs`:

```csharp
    [Fact]
    public void CampaignMapCopiesSendsThePath()
        => Assert.Equal("http://example.test/api/files/campaign-map-copies?path=maps%2Fdungeons",
            Uri(new FilesService(Client()).CampaignMapCopiesRequest("maps/dungeons")));
```

If the generated URI template leaves `/` unescaped, assert on `path=maps` and `dungeons` with `Assert.Contains` instead.

In `FilesCommandTests.cs`:

```csharp
    [Fact]
    public void CampaignMapCopiesRequiresPath()
    {
        Assert.NotEmpty(FilesCommand.Create().Parse(["campaign-map-copies"]).Errors);
        Assert.Empty(FilesCommand.Create().Parse(["campaign-map-copies", "--path", "maps/x.png"]).Errors);
    }

    [Fact]
    public void CampaignMapCopiesIsAdminOnly()
        => Assert.Contains("admin", Help(["files", "campaign-map-copies"]));
```

Use the `Help` helper the file already has, or copy it from `MapsCommandTests`.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FilesServiceTests|FilesCommandTests"`
Expected: compile failure.

- [ ] **Step 3: Implement**

In `FilesService.cs`:

```csharp
    /// <summary>GET /api/files/campaign-map-copies. What a delete of path would copy into each campaign using its maps.</summary>
    public async Task<string> CampaignMapCopiesAsync(string path)
        => await _client.SendAsync(CampaignMapCopiesRequest(path), permissionHint: AdminHint);

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation CampaignMapCopiesRequest(string path)
        => _client.Api.Api.Files.CampaignMapCopies.ToGetRequestInformation(c => c.QueryParameters.Path = path);
```

In `FilesCommand.cs`:

```csharp
    private static Command CreateCampaignMapCopiesCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "File or folder a delete would remove", Required = true };
        var command = new Command("campaign-map-copies", "Campaigns a delete would copy maps into, and whether each copy fits")
        {
            pathOption
        };
        command.AddRoleRequired("admin");
        command.AddExamples("grimoire-cli files campaign-map-copies --path \"maps/Dungeons\"");
        command.AddResponseExample<Generated.Models.CampaignMapCopiesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new FilesService(client);
            var result = await service.CampaignMapCopiesAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

Register it with `command.Subcommands.Add(CreateCampaignMapCopiesCommand());` right after `CreateDeleteCommand()`.

- [ ] **Step 4: Build, test, render help and make one live call**

```bash
dotnet format GrimoireCli.sln && dotnet build GrimoireCli.sln && dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet run --project src/GrimoireCli --no-build -- files campaign-map-copies --help
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli files campaign-map-copies --path maps
```

Expected: everything passes, and the live call prints `{"campaigns":[...]}`.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: add files campaign-map-copies"
```

---

### Task 3: `books links list|add|remove`

**Files:**
- Create: `src/GrimoireCli/Services/BookLinksService.cs`
- Create: `src/GrimoireCli/Commands/BookLinksCommands.cs`
- Modify: `src/GrimoireCli/Commands/BooksCommand.cs` (register `BookLinksCommands.Create()` after `BookCoverCommands.Create()`)
- Create: `tests/GrimoireCli.Tests/Services/BookLinksServiceTests.cs`, `tests/GrimoireCli.Tests/Commands/BookLinksCommandTests.cs`

**Interfaces:**
- Produces:
  - `BookLinksService(GrimoireApiClient)` with `ListAsync(string id)`, `AddAsync(string id, string systemId)` and `RemoveAsync(string id, string systemId)`, each returning `Task<string>`
  - `internal` request builders: `ListRequest`, `AddRequest` and `RemoveRequest`, with the same parameters

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/Services/BookLinksServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class BookLinksServiceTests
{
    private static BookLinksService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListResolvesToTheBooksSystemsRoute()
    {
        var info = Service().ListRequest("b1");
        Assert.Equal(Method.GET, info.HttpMethod);
        Assert.Equal("http://example.test/api/books/b1/systems", Uri(info));
    }

    [Fact]
    public void AddPostsTheSystemId()
    {
        var info = Service().AddRequest("b1", "s1");
        Assert.Equal(Method.POST, info.HttpMethod);
        Assert.Equal("http://example.test/api/books/b1/systems", Uri(info));
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"system_id\":\"s1\"}", reader.ReadToEnd());
    }

    [Fact]
    public void RemoveDeletesTheOneLink()
    {
        var info = Service().RemoveRequest("b1", "s1");
        Assert.Equal(Method.DELETE, info.HttpMethod);
        Assert.Equal("http://example.test/api/books/b1/systems/s1", Uri(info));
    }
}
```

The body assertion depends on how Kiota serialises; if the content stream is null until it is sent, follow the body-reading pattern in `tests/GrimoireCli.Tests/Services/FilesServiceTests.cs` (the markers body tests).

`tests/GrimoireCli.Tests/Commands/BookLinksCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BookLinksCommandTests
{
    private static Command Links() => BooksCommand.Create().Subcommands.Single(c => c.Name == "links");

    [Theory]
    [InlineData("list")]
    [InlineData("add")]
    [InlineData("remove")]
    public void TheLinksSubgroupHostsItsThreeVerbs(string leaf)
        => Assert.Contains(leaf, Links().Subcommands.Select(c => c.Name));

    [Fact]
    public void LinksRequireTheirFlags()
    {
        var books = BooksCommand.Create();
        Assert.NotEmpty(books.Parse(["links", "list"]).Errors);
        Assert.Empty(books.Parse(["links", "list", "--id", "b1"]).Errors);
        Assert.NotEmpty(books.Parse(["links", "add", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["links", "add", "--id", "b1", "--system-id", "s1"]).Errors);
        Assert.NotEmpty(books.Parse(["links", "remove", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["links", "remove", "--id", "b1", "--system-id", "s1"]).Errors);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public void WritesNeedGmOrAdmin(string leaf)
        => Assert.Contains("gm or admin", HelpRenderer.Render(["books", "links", leaf]));

    [Fact]
    public void ListNeedsNoRole()
        => Assert.DoesNotContain("Role required", HelpRenderer.Render(["books", "links", "list"]));
}
```

Check `tests/GrimoireCli.Tests/Commands/HelpRenderer.cs` for the actual helper name and signature, and use it. Add `using System.CommandLine;` for `Command`.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "BookLinks"`
Expected: compile failure.

- [ ] **Step 3: Implement the service**

`src/GrimoireCli/Services/BookLinksService.cs`:

```csharp
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>A book's links onto other systems' shelves. Every call answers with the book's link list.</summary>
public class BookLinksService
{
    private const string WriteHint = "the gm or admin role";
    private const string NotFoundHint = "No book with that ID. List them with: grimoire-cli books list";

    private readonly GrimoireApiClient _client;

    public BookLinksService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync(string id)
        => await _client.SendAsync(ListRequest(id), notFoundHint: NotFoundHint);

    public async Task<string> AddAsync(string id, string systemId)
        => await _client.SendAsync(AddRequest(id, systemId), permissionHint: WriteHint);

    public async Task<string> RemoveAsync(string id, string systemId)
        => await _client.SendAsync(RemoveRequest(id, systemId), permissionHint: WriteHint);

    internal RequestInformation ListRequest(string id)
        => _client.Api.Api.Books[id].Systems.ToGetRequestInformation();

    internal RequestInformation AddRequest(string id, string systemId)
        => _client.Api.Api.Books[id].Systems.ToPostRequestInformation(
            new Generated.Models.BookSystemLinkCreate { SystemId = systemId });

    internal RequestInformation RemoveRequest(string id, string systemId)
        => _client.Api.Api.Books[id].Systems[systemId].ToDeleteRequestInformation();
}
```

`add` and `remove` get no `notFoundHint`: their 404 can mean a missing book, a missing system or a link that isn't there, and the server's own detail tells them apart. Check what `SendAsync` does with a 404 when no hint is given; it should surface the server's detail.

- [ ] **Step 4: Implement the commands**

`src/GrimoireCli/Commands/BookLinksCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BookLinksCommands
{
    public static Command Create()
    {
        var command = new Command("links", "Show a book on other systems' shelves too");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateAddCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var command = new Command("list", "Systems the book is linked onto")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Never includes the book's own system; only systems you can see.");
        command.AddExamples("grimoire-cli books links list --id <book-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedSystemRef>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new BookLinksService(client).ListAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateAddCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var systemIdOption = new Option<string>("--system-id") { Description = "System whose shelf gets the book", Required = true };
        var command = new Command("add", "Link the book onto another system's shelf")
        {
            idOption, systemIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "One file and one row, listed on both shelves; category, tags and",
            "access level still come from the book. Linking twice is a no-op.",
            "",
            "400 for the book's own system, a version of a book (link its main",
            "version) or a system across the books/ / armory/ divide.");
        command.AddExamples("grimoire-cli books links add --id <book-id> --system-id <system-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedSystemRef>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new BookLinksService(client).AddAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(systemIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var systemIdOption = new Option<string>("--system-id") { Description = "System to take the book off", Required = true };
        var command = new Command("remove", "Take the book off another system's shelf")
        {
            idOption, systemIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The book stays on its own system's shelf. 404 when it is not linked there.");
        command.AddExamples("grimoire-cli books links remove --id <book-id> --system-id <system-id>");
        command.AddResponseExampleArray<Generated.Models.LinkedSystemRef>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new BookLinksService(client).RemoveAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(systemIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

Register `command.Subcommands.Add(BookLinksCommands.Create());` in `BooksCommand.cs` directly after the `BookCoverCommands.Create()` line.

- [ ] **Step 5: Build, test, render help and make a live round trip**

```bash
dotnet format GrimoireCli.sln && dotnet build GrimoireCli.sln && dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
for v in list add remove; do dotnet run --project src/GrimoireCli --no-build -- books links $v --help; done
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
SR4=$($CLI systems list --include-children | jq -r '.[]|select(.name=="Shadowrun 4 DE").id')
SR5=$($CLI systems list --include-children | jq -r '.[]|select(.name=="Shadowrun 5 DE").id')
B=$($CLI books list --system-id "$SR4" | jq -r '.books[0].id')
$CLI books links add --id "$B" --system-id "$SR5"; $CLI books links list --id "$B"; $CLI books links remove --id "$B" --system-id "$SR5"
```

Expected: `add` and `list` show SR5, and `remove` returns `[]`.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add books links commands"
```

---

### Task 4: `files folder access list|set|remove`

**Files:**
- Create: `src/GrimoireCli/Services/FolderAccessService.cs`
- Create: `src/GrimoireCli/Commands/FolderAccessCommands.cs`
- Modify: `src/GrimoireCli/Commands/FilesFolderCommands.cs` (register `FolderAccessCommands.Create()` after `CreateContentsCommand()`)
- Create: `tests/GrimoireCli.Tests/Services/FolderAccessServiceTests.cs`, `tests/GrimoireCli.Tests/Commands/FolderAccessCommandTests.cs`

**Interfaces:**
- Produces:
  - `FolderAccessService(GrimoireApiClient)` with `ListAsync()`, `SetAsync(string path, string level)` and `RemoveAsync(string path)`, each returning `Task<string>`
  - `internal` request builders: `ListRequest()`, `SetRequest(path, level)` and `RemoveRequest(path)`

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/Services/FolderAccessServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class FolderAccessServiceTests
{
    private static FolderAccessService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListResolvesToTheFolderAccessRoute()
        => Assert.Equal("http://example.test/api/settings/folder-access", Uri(Service().ListRequest()));

    [Theory]
    [InlineData("gm")]
    [InlineData("admin")]
    public void SetPutsPathAndLevel(string level)
    {
        var info = Service().SetRequest("books/Shadowrun/4 DE", level);
        Assert.Equal(Method.PUT, info.HttpMethod);
        Assert.Equal("http://example.test/api/settings/folder-access", Uri(info));
        using var reader = new StreamReader(info.Content);
        var body = reader.ReadToEnd();
        Assert.Contains("\"path\":\"books/Shadowrun/4 DE\"", body);
        Assert.Contains($"\"level\":\"{level}\"", body);
    }

    [Fact]
    public void RemoveSendsThePathAsAQuery()
    {
        var info = Service().RemoveRequest("books/x");
        Assert.Equal(Method.DELETE, info.HttpMethod);
        Assert.StartsWith("http://example.test/api/settings/folder-access?path=books", Uri(info));
    }
}
```

`tests/GrimoireCli.Tests/Commands/FolderAccessCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class FolderAccessCommandTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("remove")]
    public void AccessSitsUnderFilesFolderAndIsAdminOnly(string leaf)
        => Assert.Contains("admin", HelpRenderer.Render(["files", "folder", "access", leaf]));

    [Fact]
    public void SetTakesOnlyGmOrAdmin()
    {
        var files = FilesCommand.Create();
        Assert.Empty(files.Parse(["folder", "access", "set", "--path", "books/x", "--level", "gm"]).Errors);
        Assert.Empty(files.Parse(["folder", "access", "set", "--path", "books/x", "--level", "admin"]).Errors);
        Assert.NotEmpty(files.Parse(["folder", "access", "set", "--path", "books/x", "--level", "player"]).Errors);
        Assert.NotEmpty(files.Parse(["folder", "access", "set", "--path", "books/x"]).Errors);
    }

    [Fact]
    public void RemoveRequiresPath()
        => Assert.NotEmpty(FilesCommand.Create().Parse(["folder", "access", "remove"]).Errors);

    // Lifting the rule does not restore the shares it demoted; that is the
    // irreversible part a caller has to know before set.
    [Fact]
    public void SetWarnsThatRemoveDoesNotRestoreShares()
        => Assert.Contains("does not", HelpRenderer.Render(["files", "folder", "access", "set"]));
}
```

As in Task 3, use the real `HelpRenderer` API.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FolderAccess"`
Expected: compile failure.

- [ ] **Step 3: Implement the service**

`src/GrimoireCli/Services/FolderAccessService.cs`:

```csharp
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Admin rules restricting every book beneath a books/ or armory/ folder.</summary>
public class FolderAccessService
{
    private const string AdminHint = "the admin role";

    private readonly GrimoireApiClient _client;

    public FolderAccessService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync()
        => await _client.SendAsync(ListRequest(), permissionHint: AdminHint);

    public async Task<string> SetAsync(string path, string level)
        => await _client.SendAsync(SetRequest(path, level), permissionHint: AdminHint);

    public async Task<string> RemoveAsync(string path)
        => await _client.SendAsync(RemoveRequest(path), permissionHint: AdminHint);

    internal RequestInformation ListRequest()
        => _client.Api.Api.Settings.FolderAccess.ToGetRequestInformation();

    internal RequestInformation SetRequest(string path, string level)
        => _client.Api.Api.Settings.FolderAccess.ToPutRequestInformation(new Generated.Models.FolderAccessRuleIn
        {
            Path = path,
            Level = Enum.Parse<Generated.Models.FolderAccessRuleIn_level>(level, true),
        });

    internal RequestInformation RemoveRequest(string path)
        => _client.Api.Api.Settings.FolderAccess.ToDeleteRequestInformation(c => c.QueryParameters.Path = path);
}
```

- [ ] **Step 4: Implement the commands**

`src/GrimoireCli/Commands/FolderAccessCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class FolderAccessCommands
{
    private static readonly string[] Levels = ["gm", "admin"];

    public static Command Create()
    {
        var command = new Command("access", "Restrict every book beneath a folder by role");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateRemoveCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "Folder access rules");
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "exists false: the folder left disk outside the file manager, so the",
            "rule covers nothing.");
        command.AddExamples("grimoire-cli files folder access list");
        command.AddResponseExample<Generated.Models.FolderAccessRulesResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new FolderAccessService(client).ListAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Folder from the library root", Required = true };
        var levelOption = OptionHelpers.Choice("--level", "Who may still see the books", Levels);
        levelOption.Required = true;
        var command = new Command("set", "Restrict a folder, replacing any level it had")
        {
            pathOption, levelOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--path must be a folder inside books/ or armory/, not the root itself",
            "(400); a missing folder is 404.",
            "",
            "A book gets the stricter of the rule and its system's or category's",
            "level; its own access_level overrides both.",
            "",
            "Campaign shares of the covered books are demoted to GM-only, and remove",
            "does not restore them. The rule follows the folder on files move.");
        command.AddExamples("grimoire-cli files folder access set --path \"books/Shadowrun/Adventures\" --level gm");
        command.AddResponseExample<Generated.Models.FolderAccessRuleOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new FolderAccessService(client).SetAsync(
                parseResult.GetValue(pathOption)!, parseResult.GetValue(levelOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRemoveCommand()
    {
        var pathOption = new Option<string>("--path") { Description = "Restricted folder", Required = true };
        var command = new Command("remove", "Lift a folder's restriction")
        {
            pathOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "404 when the folder has no rule. Campaign shares the rule demoted stay",
            "GM-only.");
        command.AddExamples("grimoire-cli files folder access remove --path \"books/Shadowrun/Adventures\"");
        command.AddResponseExample<Generated.Models.FolderAccessStatusResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var result = await new FolderAccessService(client).RemoveAsync(parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

Check `OptionHelpers.Choice`'s return type and how other commands make a choice option required (for example `FilesFolderCommands`), and follow that. Register `command.Subcommands.Add(FolderAccessCommands.Create());` in `FilesFolderCommands.Create()` after `CreateContentsCommand()`.

- [ ] **Step 5: Build, test, render help and make a live round trip**

```bash
dotnet format GrimoireCli.sln && dotnet build GrimoireCli.sln && dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
for v in list set remove; do dotnet run --project src/GrimoireCli --no-build -- files folder access $v --help; done
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
$CLI files browse --path "books/Shadowrun/4 DE" | jq -r '.entries[].name'   # find the real subfolder names
$CLI files folder access set --path "books/Shadowrun/4 DE/adventures" --level gm
$CLI files folder access list
$CLI files folder access remove --path "books/Shadowrun/4 DE/adventures"
```

Expected: `set` echoes the rule with `books >= 1`, `list` holds it, and `remove` returns `{"status":"ok"}`. Adjust the folder name to whatever `browse` shows.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add files folder access commands"
```

---

### Task 5: Docs, coverage and smoke test

**Files:**
- Modify: `README.md` (Commands table: one row per new command, placed next to its group's rows, in the existing row format)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docker/smoke-test.sh`

- [ ] **Step 1: README rows**

```
| `maps index [--map-type <t>] [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--folder <path>] [--sort path\|name\|size\|added_at\|title\|duration\|track] [--order asc\|desc]` | Where each letter or month starts in the sorted map list |
| `tokens index [same filters as tokens folder-counts]` | ... |
| `models index [...]` | ... |
| `audio index [...]` | ... |
| `files campaign-map-copies --path <path>` | Campaigns a delete would copy maps into, and whether each fits (admin) |
| `books links list --id <id>` | Systems a book is linked onto |
| `books links add --id <id> --system-id <id>` | Link a book onto another system's shelf (gm or admin) |
| `books links remove --id <id> --system-id <id>` | Take a book off another system's shelf (gm or admin) |
| `files folder access list` | Folder access rules (admin) |
| `files folder access set --path <path> --level gm\|admin` | Restrict every book beneath a folder (admin) |
| `files folder access remove --path <path>` | Lift a folder's restriction (admin) |
```

Write the token, model and audio rows out in full, mirroring their `folder-counts` rows exactly. Each table row's flag list must match the rendered `--help`.

- [ ] **Step 2: Coverage**

Add these entries to `IMPLEMENTED` in `tools/generate-api-coverage.py`, in the same style as their neighbours:

```python
    "GET /api/maps/index": "`maps index` ✅",
    "GET /api/tokens/index": "`tokens index` ✅",
    "GET /api/models/index": "`models index` ✅",
    "GET /api/audio/index": "`audio index` ✅",
    "GET /api/files/campaign-map-copies": "`files campaign-map-copies` ✅",
    "GET /api/books/{book_id}/systems": "`books links list` ✅",
    "POST /api/books/{book_id}/systems": "`books links add` ✅",
    "DELETE /api/books/{book_id}/systems/{system_id}": "`books links remove` ✅",
    "GET /api/settings/folder-access": "`files folder access list` ✅",
    "PUT /api/settings/folder-access": "`files folder access set` ✅",
    "DELETE /api/settings/folder-access": "`files folder access remove` ✅",
```

Run `python3 tools/generate-api-coverage.py`. Expected: `252 covered`, up from 241.

- [ ] **Step 3: API notes**

Add short bullets citing v1.11.0, in the house style of the sections they join:
- **Maps:** an `index` bullet covering the sorts that produce entries, `#`, and that `offset` matches the list (`services/browse/media.py:276-333`). Note that `Tokens and audio` and `Models` share it.
- **Files:** the `campaign-map-copies` preview and its `outcome` values (`routers/files/core.py:429-455`, `routers/files/_schemas.py:185-201`). The map-copy delete bullet may already mention it; extend that bullet rather than duplicating it.
- **Book links:** a new `## Book links` section with the three routes, the 400 cases (`routers/books/links.py:41-43`, `services/book_links.py:136-150`), the 404 on remove, list visibility, and the return value.
- **Folder access rules:** a new `## Folder access rules` section covering:
  - the path must be inside `books/` or `armory/` (`services/folder_access.py:47-60`)
  - a missing folder is a 404, and the rule replaces any existing level (`routers/settings/folder_access.py:116-149`)
  - share demotion, and that remove does not restore it (`:152-166`)
  - stricter-of resolution (`routers/settings/__init__.py:53-63`)
  - `exists` and `books` in the output
  
  The `## Files` section already mentions rules moving with folders; leave that there.

- [ ] **Step 4: Smoke test**

Append a block before `# ---- api keys ----` in `docker/smoke-test.sh`. It runs as admin, after the campaign sections. The `Smoke Campaign` persists between runs and shares `books list`'s first book, and `set` demotes shares that `remove` doesn't restore. So the block asserts that the restricted folder doesn't hold that book.

```bash
# ---- links, folder access, media index ---------------------------------------
# Every write here is undone in the same run, so a re-run converges.
# The folder rule demotes campaign shares irreversibly, so its folder must not
# hold the book the Smoke Campaign shares (books list's first book).
syslist --include-children
SR5=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 5 DE") | .id')
[ -n "$SR4" ] && [ -n "$SR5" ] || fail "no Shadowrun 4/5 DE fixtures"
LINK_BOOK=$("$CLI" books list --system-id "$SR4" 2>"$WORK/cli.err" | jq -r '.books[0].id')
[ -n "$LINK_BOOK" ] && [ "$LINK_BOOK" != "null" ] || fail "no Shadowrun 4 DE book to link"

# linking a Shadowrun 4 DE book onto Shadowrun 5 DE, then removing the link
"$CLI" books links add --id "$LINK_BOOK" --system-id "$SR5" >"$WORK/link.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books links add exited non-zero"; }
jq -e --arg s "$SR5" 'any(.[]; .id == $s)' "$WORK/link.out" >/dev/null \
  || fail "the new link is not in the answer: $(cat "$WORK/link.out")"
"$CLI" books links remove --id "$LINK_BOOK" --system-id "$SR5" >"$WORK/unlink.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books links remove exited non-zero"; }
jq -e --arg s "$SR5" 'all(.[]; .id != $s)' "$WORK/unlink.out" >/dev/null \
  || fail "the link survived remove: $(cat "$WORK/unlink.out")"
ok "books links add then remove converges"

# restricting a Shadowrun 4 DE subfolder, listing it, lifting it
RULE_PATH="books/Shadowrun/4 DE/adventures"
SHARED_BOOK=$("$CLI" books list 2>"$WORK/cli.err" | jq -r '.books[0].relative_path')
case "$SHARED_BOOK" in "${RULE_PATH#books/}"/*|"$RULE_PATH"/*) fail "the rule folder holds the shared smoke book: $SHARED_BOOK";; esac
"$CLI" files folder access set --path "$RULE_PATH" --level gm >"$WORK/rule.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "files folder access set exited non-zero"; }
jq -e '.level == "gm" and .books >= 1' "$WORK/rule.out" >/dev/null \
  || fail "set should echo a gm rule covering a book: $(cat "$WORK/rule.out")"
"$CLI" files folder access list 2>"$WORK/cli.err" \
  | jq -e --arg p "$RULE_PATH" 'any(.rules[]; .path == $p)' >/dev/null \
  || fail "the rule is not listed"
"$CLI" files folder access remove --path "$RULE_PATH" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "files folder access remove exited non-zero"; }
ok "files folder access set, list and remove converge"

# reading the map index sorted by name
"$CLI" maps index --sort name 2>"$WORK/cli.err" | jq -e '.entries | type == "array"' >/dev/null \
  || { cat "$WORK/cli.err" >&2; fail "maps index should answer an entries array"; }
ok "maps index answers entries"
```

Several of these lines are assumptions to check:
- The `relative_path` field name and its root, and the `adventures` folder name: check both with one live `books list` and `files browse`.
- Whether `$SR4` is still set at this point. If it isn't, re-derive it as the systems update section does.
- Whether `RULE_PATH` is case-sensitive: the server re-reads the folder name from disk (`_folder_on_disk`), so the echoed `path` may differ in case, and the `list` assertion has to use the echoed path.

Run the smoke test twice; both runs must pass.

- [ ] **Step 5: Full pre-PR verification**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

- [ ] **Step 6: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs docker/smoke-test.sh
git commit -m "docs: cover linked books, folder access rules and media index"
```
