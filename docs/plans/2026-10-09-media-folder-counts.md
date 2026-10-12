# Media Folder Counts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `maps|tokens|audio|models folder-counts`, passing through Grimoire 1.8.0's `GET /api/{collection}/groups`.

**Architecture:** Each collection's own `*Command.cs` gains a `folder-counts` subcommand registered right after `list`, and its own `*Service.cs` gains `FolderCountsRequest` / `FolderCountsAsync` against the generated `Groups` builder. Both reuse the existing `MediaBrowseOptions` (flags + help) and `MediaBrowseFilter` (values) exactly as `list` does, so the flags mirror the server.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client (`src/GrimoireCli/Generated/`), xUnit, bash smoke test against the local Grimoire 1.8.0 stack.

**Spec:** [docs/specs/2026-10-09-media-folder-counts-design.md](../specs/2026-10-09-media-folder-counts-design.md)

## Global Constraints

- Target Grimoire **1.8.0**; `MinSupportedVersion` / `MaxTestedVersion` do not change.
- Command name is `folder-counts`, registered directly after `list` in each collection.
- Flags mirror the server exactly: everything `MediaBrowseOptions.AddTo` adds (`--query`, `--tags`, `--favorites`, `--added-since`, `--folder`, `--sort`, `--order`), plus `--map-type` on maps. No `--limit` / `--offset`.
- No role tag: all four routes are `require_not_guest`, the default.
- stdout is the server's bytes unmodified (`ConsoleOutput.WriteRawJson`).
- One file per command group: each collection keeps its own command file and its own service; no shared table.
- Never hand-edit `src/GrimoireCli/Generated/`.
- Run `dotnet format GrimoireCli.sln` after editing C#. No blank lines between consecutive `AddCommand`/`AddOption`-style calls or before a `return` after setup.
- Conventional Commits, lowercase imperative subject, **no `Co-Authored-By:` or any Claude/"Generated with" attribution** in commits or the PR.
- Do not edit `CHANGELOG.md` or `docs/roadmap.md`.
- Anything that writes goes to the local stack only. The smoke additions are read-only.
- Local stack: `GRIMOIRE_SERVER=http://host.docker.internal:9481` (docker-outside-of-docker). Bring-up and seeding are in `CLAUDE.md` § Pre-PR verification.

## Server facts the code and help rely on

Verified in `temp/grimoire` at `v1.8.0` and live on 2026-10-09:

- Response `{"total": N, "groups": [{"path": "...", "count": N}]}` (`services/browse/media.py:215-234`).
- `count` is items **directly** in that folder, not subfolders. Fixture: maps `battlemaps` = 2, `battlemaps/caves` = 1.
- `path` is collection-relative; `""` holds the collection root's items. Only folders with a matching item appear.
- Groups come back sorted by path; `sort` / `order` are validated (unknown → 422, `routers/_browse.py:63`) but change nothing.
- Live fixture output: tokens `Monsters`=1, `Monsters/Undead`=2; audio `Ambience`=1, `Ambience/Battle`=1; models `Goblins/Loose`, `Goblins/Presupported`, `Goblins/Unsupported` = 1 each.

## File Structure

| File | Change |
|---|---|
| `src/GrimoireCli/Commands/MediaBrowseOptions.cs` | `--folder` help line names `folder-counts` as a second source |
| `src/GrimoireCli/Commands/{Maps,Tokens,Audio,Models}Command.cs` | `CreateFolderCountsCommand()` + registration after `list` |
| `src/GrimoireCli/Services/{Maps,Tokens,Audio,Models}Service.cs` | `FolderCountsRequest` + `FolderCountsAsync` |
| `tests/GrimoireCli.Tests/Commands/{Maps,Tokens,Audio,Models}CommandTests.cs` | placement, parsing, help caveat |
| `tests/GrimoireCli.Tests/Services/MediaFolderCountsServiceTests.cs` | **new**: pins each request to its own `/groups` path |
| `docker/smoke-test.sh` | read-only `folder-counts` assertions |
| `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md` | docs |

---

### Task 0: Branch and commit the design

- [ ] **Step 1: Create the branch from an up-to-date `main`**

```bash
git checkout main && git pull
git checkout -b feat/media-folder-counts
```

- [ ] **Step 2: Commit spec and plan together**

Both files already exist in the working tree (untracked).

```bash
git add docs/specs/2026-10-09-media-folder-counts-design.md docs/plans/2026-10-09-media-folder-counts.md
git commit -m "docs: design media folder counts"
```

---

### Task 1: `maps folder-counts`

**Files:**
- Modify: `src/GrimoireCli/Commands/MediaBrowseOptions.cs` (the Filters help section in `AddTo`)
- Modify: `src/GrimoireCli/Services/MapsService.cs`
- Modify: `src/GrimoireCli/Commands/MapsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`
- Create: `tests/GrimoireCli.Tests/Services/MediaFolderCountsServiceTests.cs`

**Interfaces:**
- Consumes: `MediaBrowseOptions` (`AddTo(Command, string collection)`, `Read(ParseResult) → MediaBrowseFilter`); `MediaBrowseFilter(string? Query, string? Tags, bool? Favorites, string? AddedSince, string? Folder, string? Sort, string? Order)`.
- Produces: `MapsService.FolderCountsRequest(string? mapType, MediaBrowseFilter filter) → RequestInformation` (internal), `MapsService.FolderCountsAsync(string? mapType, MediaBrowseFilter filter) → Task<string>`; `MediaFolderCountsServiceTests` with a `Requests()` theory data that Task 2 extends.

- [ ] **Step 1: Write the failing command tests** — append to `MapsCommandTests` (inside the class):

```csharp
    [Fact]
    public void FolderCountsSitsRightAfterList()
    {
        var names = MapsCommand.Create().Subcommands.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("list") + 1, names.IndexOf("folder-counts"));
    }

    [Fact]
    public void FolderCountsTakesTheListFiltersAndMapType()
    {
        Assert.Empty(MapsCommand.Create().Parse(
            ["folder-counts", "--map-type", "battlemap", "--query", "tag:forest", "--tags", "[\"x\"]",
             "--favorites", "--added-since", "2026-01-01T00:00:00Z", "--folder", "battlemaps",
             "--sort", "name", "--order", "desc"]).Errors);
    }

    [Theory]
    [InlineData("--sort", "bogus")]
    [InlineData("--order", "up")]
    public void FolderCountsRejectsAnUnknownSortOrOrder(string flag, string value)
        => Assert.NotEmpty(MapsCommand.Create().Parse(["folder-counts", flag, value]).Errors);

    // A count covers one folder's own items; reading it as a subtree total is
    // the mistake a caller who knows tags items would make.
    [Fact]
    public void FolderCountsSaysCountsAreDirectContentsOnly()
        => Assert.Contains("not its subfolders", Help(["maps", "folder-counts"]));

    [Fact]
    public void ListNamesFolderCountsAsASourceForFolder()
        => Assert.Contains("path from maps folder-counts", Help(["maps", "list"]));
```

- [ ] **Step 2: Write the failing service test** — create `tests/GrimoireCli.Tests/Services/MediaFolderCountsServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each collection's folder-counts request to its own /groups path. The
/// four services are near-identical copies, so one copied without swapping the
/// builder would query another collection — which nothing else here would
/// catch, since every response has the same shape.
/// </summary>
public class MediaFolderCountsServiceTests
{
    private static readonly MediaBrowseFilter NoFilter = new(null, null, null, null, null, null, null);

    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    public static TheoryData<string, string> Requests() => new()
    {
        { "maps", "/api/maps/groups" },
    };

    private static RequestInformation Request(string collection, MediaBrowseFilter filter)
    {
        var client = Client();
        return collection switch
        {
            "maps" => new MapsService(client).FolderCountsRequest(null, filter),
            _ => throw new ArgumentException($"Unknown collection '{collection}'.", nameof(collection)),
        };
    }

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void EachServiceResolvesToItsOwnPath(string collection, string expectedPath)
        => Assert.Equal("http://example.test" + expectedPath, Uri(Request(collection, NoFilter)));

    [Theory]
    [MemberData(nameof(Requests))]
    public void TheFilterReachesTheQueryString(string collection, string expectedPath)
    {
        _ = expectedPath;
        var uri = Uri(Request(collection, new MediaBrowseFilter("forest", null, true, null, "a", "name", "desc")));
        Assert.Contains("q=forest", uri);
        Assert.Contains("favorites=true", uri);
        Assert.Contains("folder=a", uri);
        Assert.Contains("sort=name", uri);
        Assert.Contains("order=desc", uri);
    }

    [Fact]
    public void MapsSendsMapType()
        => Assert.Contains("map_type=battlemap",
            Uri(new MapsService(Client()).FolderCountsRequest("battlemap", NoFilter)));
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: build error — `MapsService` does not contain a definition for `FolderCountsRequest`.

- [ ] **Step 4: Add the service methods** — in `src/GrimoireCli/Services/MapsService.cs`, add `using Microsoft.Kiota.Abstractions;` to the usings, and insert directly after the closing brace of `ListAsync`:

```csharp

    /// <summary>GET /api/maps/groups. Each folder holding matching maps, with its direct count.</summary>
    public async Task<string> FolderCountsAsync(string? mapType, MediaBrowseFilter filter)
        => await _client.SendAsync(FolderCountsRequest(mapType, filter));

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation FolderCountsRequest(string? mapType, MediaBrowseFilter filter)
        => _client.Api.Api.Maps.Groups.ToGetRequestInformation(c =>
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
                    Enum.Parse<Generated.Api.Maps.Groups.GetOrderQueryParameterType>(filter.Order, true);
        });
```

- [ ] **Step 5: Add the command** — in `src/GrimoireCli/Commands/MapsCommand.cs`, register it after `list`:

```csharp
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateFolderCountsCommand());
```

and insert this method directly before `private static Command CreateGetCommand()`:

```csharp
    private static Command CreateFolderCountsCommand()
    {
        var mapTypeOption = new Option<string?>("--map-type") { Description = "Filter by map type" };
        var browse = new MediaBrowseOptions();
        var command = new Command("folder-counts", "Folders holding matching maps, with counts")
        {
            mapTypeOption
        };
        browse.AddTo(command, "maps");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Each count covers the maps directly in that folder, not its subfolders.",
            "path is relative to the maps/ root; \"\" holds the root's own maps.",
            "",
            "Folders come back in path order; --sort and --order change nothing today.");
        command.AddExamples(
            "grimoire-cli maps folder-counts",
            "grimoire-cli maps folder-counts --tags '[\"__grim:none__\"]'");
        command.AddResponseExample<Generated.Models.FolderGroupsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.FolderCountsAsync(
                parseResult.GetValue(mapTypeOption),
                browse.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

```

- [ ] **Step 6: Name `folder-counts` as a `--folder` source** — in `src/GrimoireCli/Commands/MediaBrowseOptions.cs`, inside `AddTo`, replace the first two lines of the `"Filters"` section:

```csharp
            $"--folder is one exact folder, not a subtree: folder_path from {collection} get,",
            "relative to the collection root; \"\" is the root.",
```

with:

```csharp
            "--folder is one exact folder, not a subtree, relative to the collection",
            $"root: folder_path from {collection} get, or path from {collection} folder-counts.",
            "\"\" is the root.",
```

Update the class summary to say the flags serve `list` and `folder-counts`:

```csharp
/// <summary>
/// The filter and sort flags of the maps, tokens, audio and models list and
/// folder-counts commands. Shared because the server declares them once for all
/// eight routes, in <c>routers/_browse.py</c> (<c>media_browse_params</c>).
/// </summary>
```

- [ ] **Step 7: Format and run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all pass. If an existing test asserts the old `--folder` wording ("folder_path from maps get,"), update its expected substring to the new text rather than reverting the help.

- [ ] **Step 8: Check the rendered help**

Run: `dotnet build GrimoireCli.sln && src/GrimoireCli/bin/Debug/net10.0/grimoire-cli maps folder-counts --help`
Expected: Notes, Filters, Query syntax and Tag filter sections; options `--map-type`, `--query`, `--tags`, `--favorites`, `--added-since`, `--folder`, `--sort`, `--order`; no `--limit`.

- [ ] **Step 9: Commit**

```bash
git add src/GrimoireCli/Commands/MediaBrowseOptions.cs src/GrimoireCli/Commands/MapsCommand.cs \
  src/GrimoireCli/Services/MapsService.cs tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs \
  tests/GrimoireCli.Tests/Services/MediaFolderCountsServiceTests.cs
git commit -m "feat: add maps folder-counts"
```

---

### Task 2: `tokens`, `audio` and `models folder-counts`

The same shape as Task 1 for the three remaining collections, each in its own files. None takes `--map-type`.

**Files:**
- Modify: `src/GrimoireCli/Services/TokensService.cs`, `AudioService.cs`, `ModelsService.cs`
- Modify: `src/GrimoireCli/Commands/TokensCommand.cs`, `AudioCommand.cs`, `ModelsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs`, `AudioCommandTests.cs`, `ModelsCommandTests.cs`
- Test: `tests/GrimoireCli.Tests/Services/MediaFolderCountsServiceTests.cs`

**Interfaces:**
- Consumes: Task 1's `MediaFolderCountsServiceTests` (`Requests()`, `Request(...)` switch); `MediaBrowseOptions`, `MediaBrowseFilter`.
- Produces: `TokensService` / `AudioService` / `ModelsService` each with `internal RequestInformation FolderCountsRequest(MediaBrowseFilter filter)` and `public Task<string> FolderCountsAsync(MediaBrowseFilter filter)`.

| Collection | Command class | Service | Builder | Order enum | Noun in help | Root in help |
|---|---|---|---|---|---|---|
| tokens | `TokensCommand` | `TokensService` | `_client.Api.Api.Tokens.Groups` | `Generated.Api.Tokens.Groups.GetOrderQueryParameterType` | tokens | `tokens/` |
| audio | `AudioCommand` | `AudioService` | `_client.Api.Api.Audio.Groups` | `Generated.Api.Audio.Groups.GetOrderQueryParameterType` | audio tracks | `audio/` |
| models | `ModelsCommand` | `ModelsService` | `_client.Api.Api.Models.Groups` | `Generated.Api.ModelsRequests.Groups.GetOrderQueryParameterType` | 3D models | `models/` |

- [ ] **Step 1: Extend the service test** — in `MediaFolderCountsServiceTests`, make `Requests()` and `Request(...)`:

```csharp
    public static TheoryData<string, string> Requests() => new()
    {
        { "maps", "/api/maps/groups" },
        { "tokens", "/api/tokens/groups" },
        { "audio", "/api/audio/groups" },
        { "models", "/api/models/groups" },
    };

    private static RequestInformation Request(string collection, MediaBrowseFilter filter)
    {
        var client = Client();
        return collection switch
        {
            "maps" => new MapsService(client).FolderCountsRequest(null, filter),
            "tokens" => new TokensService(client).FolderCountsRequest(filter),
            "audio" => new AudioService(client).FolderCountsRequest(filter),
            "models" => new ModelsService(client).FolderCountsRequest(filter),
            _ => throw new ArgumentException($"Unknown collection '{collection}'.", nameof(collection)),
        };
    }
```

- [ ] **Step 2: Write the failing command tests** — append to each test class, substituting `{Cmd}` and `{name}` per row (`TokensCommand`/`tokens`, `AudioCommand`/`audio`, `ModelsCommand`/`models`). Written out for tokens; repeat verbatim with the substitution in `AudioCommandTests` and `ModelsCommandTests`:

```csharp
    [Fact]
    public void FolderCountsSitsRightAfterList()
    {
        var names = TokensCommand.Create().Subcommands.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("list") + 1, names.IndexOf("folder-counts"));
    }

    [Fact]
    public void FolderCountsTakesTheListFilters()
    {
        Assert.Empty(TokensCommand.Create().Parse(
            ["folder-counts", "--query", "tag:forest", "--tags", "[\"x\"]", "--favorites",
             "--added-since", "2026-01-01T00:00:00Z", "--folder", "a", "--sort", "name", "--order", "desc"]).Errors);
    }

    [Theory]
    [InlineData("--sort", "bogus")]
    [InlineData("--order", "up")]
    public void FolderCountsRejectsAnUnknownSortOrOrder(string flag, string value)
        => Assert.NotEmpty(TokensCommand.Create().Parse(["folder-counts", flag, value]).Errors);

    [Fact]
    public void FolderCountsSaysCountsAreDirectContentsOnly()
        => Assert.Contains("not its subfolders", Help(["tokens", "folder-counts"]));
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: build error — `TokensService` does not contain a definition for `FolderCountsRequest`.

- [ ] **Step 4: Add the service methods** — in each service add `using Microsoft.Kiota.Abstractions;` if absent, and insert directly after `ListAsync`. Tokens shown; for audio and models swap the builder, enum and noun per the table:

```csharp

    /// <summary>GET /api/tokens/groups. Each folder holding matching tokens, with its direct count.</summary>
    public async Task<string> FolderCountsAsync(MediaBrowseFilter filter)
        => await _client.SendAsync(FolderCountsRequest(filter));

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation FolderCountsRequest(MediaBrowseFilter filter)
        => _client.Api.Api.Tokens.Groups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Q = filter.Query;
            c.QueryParameters.Tags = filter.Tags;
            c.QueryParameters.Favorites = filter.Favorites;
            c.QueryParameters.AddedSince = filter.AddedSince;
            c.QueryParameters.Folder = filter.Folder;
            c.QueryParameters.Sort = filter.Sort;
            if (filter.Order is not null)
                c.QueryParameters.OrderAsGetOrderQueryParameterType =
                    Enum.Parse<Generated.Api.Tokens.Groups.GetOrderQueryParameterType>(filter.Order, true);
        });
```

- [ ] **Step 5: Add the commands** — in each command file register after `list`:

```csharp
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateFolderCountsCommand());
```

and insert before `CreateGetCommand()`. Tokens shown; for audio and models swap the noun, root, collection name, service and response sample stays `FolderGroupsResponse`:

```csharp
    private static Command CreateFolderCountsCommand()
    {
        var browse = new MediaBrowseOptions();
        var command = new Command("folder-counts", "Folders holding matching tokens, with counts");
        browse.AddTo(command, "tokens");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Each count covers the tokens directly in that folder, not its subfolders.",
            "path is relative to the tokens/ root; \"\" holds the root's own tokens.",
            "",
            "Folders come back in path order; --sort and --order change nothing today.");
        command.AddExamples(
            "grimoire-cli tokens folder-counts",
            "grimoire-cli tokens folder-counts --tags '[\"__grim:none__\"]'");
        command.AddResponseExample<Generated.Models.FolderGroupsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TokensService(client);
            var result = await service.FolderCountsAsync(browse.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

```

For audio: description `"Folders holding matching audio tracks, with counts"`, notes `"Each count covers the tracks directly in that folder, not its subfolders."` / `"path is relative to the audio/ root; \"\" holds the root's own tracks."`, `AddTo(command, "audio")`, `new AudioService(client)`, examples with `audio`.
For models: description `"Folders holding matching 3D models, with counts"`, notes `"Each count covers the models directly in that folder, not its subfolders."` / `"path is relative to the models/ root; \"\" holds the root's own models."`, `AddTo(command, "models")`, `new ModelsService(client)`, examples with `models`.

- [ ] **Step 6: Format and run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all pass, including all four rows of `MediaFolderCountsServiceTests`.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/TokensCommand.cs src/GrimoireCli/Commands/AudioCommand.cs \
  src/GrimoireCli/Commands/ModelsCommand.cs src/GrimoireCli/Services/TokensService.cs \
  src/GrimoireCli/Services/AudioService.cs src/GrimoireCli/Services/ModelsService.cs \
  tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs tests/GrimoireCli.Tests/Commands/AudioCommandTests.cs \
  tests/GrimoireCli.Tests/Commands/ModelsCommandTests.cs tests/GrimoireCli.Tests/Services/MediaFolderCountsServiceTests.cs
git commit -m "feat: add folder-counts to tokens, audio and models"
```

---

### Task 3: Smoke test and docs

**Files:**
- Modify: `docker/smoke-test.sh` (after the line `ok "maps list --folder is an exact folder, not a subtree"`)
- Modify: `README.md` (Commands table)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md` (`## Maps` section)

**Interfaces:**
- Consumes: the four `folder-counts` commands from Tasks 1–2.

- [ ] **Step 1: Add read-only smoke assertions** — in `docker/smoke-test.sh`, insert directly after `ok "maps list --folder is an exact folder, not a subtree"`:

```bash

# Counts are per folder, not per subtree: battlemaps holds three maps beneath it
# but only two directly, so a 3 here would mean the server rolled caves up.
# listing map folder counts expecting direct counts for battlemaps and its subfolder
"$CLI" maps folder-counts >"$WORK/map-counts.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "maps folder-counts exited non-zero"; }
jq -e '[.groups[] | {(.path): .count}] | add == {"battlemaps": 2, "battlemaps/caves": 1}' \
  "$WORK/map-counts.out" >/dev/null \
  || fail "maps folder-counts should count each folder's own maps: $(cat "$WORK/map-counts.out")"
ok "maps folder-counts counts each folder's direct contents"

# listing folder counts for the other three collections expecting at least one folder each
for collection in tokens audio models; do
  "$CLI" "$collection" folder-counts >"$WORK/$collection-counts.out" 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "$collection folder-counts exited non-zero"; }
  jq -e '(.groups | length) > 0 and .total > 0' "$WORK/$collection-counts.out" >/dev/null \
    || fail "$collection folder-counts should list the seeded folders: $(cat "$WORK/$collection-counts.out")"
done
ok "tokens, audio and models folder-counts list their folders"
```

- [ ] **Step 2: Run the smoke test** against the local 1.8.0 stack, seeded per `CLAUDE.md`:

```bash
dotnet build GrimoireCli.sln
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

Expected: ends with `smoke: all checks passed`. Run it twice; the second run must pass too.

- [ ] **Step 3: README Commands table** — insert one row directly after each collection's `list` row:

```markdown
| `maps folder-counts [--map-type <t>] [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--folder <path>] [--sort path\|name\|size\|added_at\|title\|duration] [--order asc\|desc]` | Folders holding matching maps, with direct counts |
| `models folder-counts [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--folder <path>] [--sort path\|name\|size\|added_at\|title\|duration] [--order asc\|desc]` | Folders holding matching 3D models, with direct counts |
| `tokens folder-counts [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--folder <path>] [--sort path\|name\|size\|added_at\|title\|duration] [--order asc\|desc]` | Folders holding matching tokens, with direct counts |
| `audio folder-counts [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--folder <path>] [--sort path\|name\|size\|added_at\|title\|duration] [--order asc\|desc]` | Folders holding matching audio tracks, with direct counts |
```

- [ ] **Step 4: API coverage** — in `tools/generate-api-coverage.py`, add to `IMPLEMENTED` next to each collection's `GET /api/{collection}` entry:

```python
    "GET /api/maps/groups": "`maps folder-counts` ✅",
    "GET /api/models/groups": "`models folder-counts` ✅",
    "GET /api/tokens/groups": "`tokens folder-counts` ✅",
    "GET /api/audio/groups": "`audio folder-counts` ✅",
```

then regenerate:

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py
```

Expected: `wrote .../docs/grimoire-api-coverage.md: 360 operations, 172 covered, 2 internal-only`.

- [ ] **Step 5: API notes** — in `docs/grimoire-api-notes.md` under `## Maps`, add this bullet after the bullet describing the shared browse parameters (`media_browse_params`):

```markdown
- **`/groups` counts a folder's own items, not its subtree**, as of 1.8.0.
  `GET /api/{maps,tokens,audio,models}/groups` takes the list's parameters
  (maps also `map_type`) and returns `{total, groups: [{path, count}]}`
  (`services/browse/media.py:215-234`). `count` is the items directly in
  `path` — the fixture's `battlemaps` reads 2 though three maps sit beneath it —
  unlike `tags items`' `folders[].count`, which covers the subtree. `path` is
  collection-relative, `""` for the root, and only folders with a match appear.
  Groups come back in path order: `sort` and `order` are validated
  (`routers/_browse.py:63`) but change nothing. Backs `<collection> folder-counts`.
```

- [ ] **Step 6: Final verification** (all four, per `CLAUDE.md`):

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

Expected: no format changes, 0 warnings / 0 errors, all tests pass, `smoke: all checks passed`.

- [ ] **Step 7: Commit**

```bash
git add docker/smoke-test.sh README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md
git commit -m "docs: document folder-counts and cover it in the smoke test"
```
