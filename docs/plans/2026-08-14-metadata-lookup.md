# Metadata Lookup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Six commands — `metadata-sources`, `metadata-search`, `metadata-fetch` on both `systems` and `books` — so the CLI can find metadata from installed add-ons as well as edit it.

**Architecture:** One command factory and one service, both parameterised by resource, mirroring the server's own shared `routers/_metadata_lookup.py`. All three endpoints are reads; nothing here writes, and applying stays `systems update` / `books update`. Response DTOs are hand-written with the polymorphic `current`/`incoming` values as `JsonElement?`.

**Tech Stack:** C# / .NET 8, System.CommandLine, Kiota-generated request builders (already present under `src/GrimoireCli/Generated/Api/{Systems,Books}/Item/Metadata*`), xUnit, bash smoke test against the docker stack.

**Design doc:** [docs/specs/2026-08-14-metadata-lookup-design.md](../specs/2026-08-14-metadata-lookup-design.md)

## Global Constraints

- **Branch first.** All work, including the spec and this plan, lands on `feat/metadata-lookup`. Never commit to `main`.
- **Conventional Commits**, imperative, lowercase, no period, ≤72 chars. No `Co-Authored-By`, no tool attribution.
- **Run `dotnet format GrimoireCli.sln` after any C# edit.** CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** in method bodies — none between consecutive `Subcommands.Add`/option declarations, none before a `return` that follows setup calls.
- **Role tag and permission hint must agree**: all six routes are `require_gm_or_admin`, so `AddRoleRequired("gm or admin")` and `permissionHint: "the gm or admin role"`.
- **`--server` and `--token` are declared per subcommand** and threaded into `CommandHelper.BuildClient`.
- **Help text is terse**, calibrated against `SystemsCommand.cs`. Never restate what a flag description or response sample already shows.
- **Comments say what the code does or why it must be so** — never what was deliberately left out.
- **`CHANGELOG.md` is owned by the release process.** Do not touch it.
- **Anything that writes goes to the local docker stack, never the live instance.** Every command in this plan is a read regardless.

---

### Task 1: Branch, spec and plan

**Files:**
- Create: branch `feat/metadata-lookup`
- Commit: `docs/specs/2026-08-14-metadata-lookup-design.md`, `docs/plans/2026-08-14-metadata-lookup.md`

**Interfaces:**
- Consumes: nothing
- Produces: the branch every later task commits to

- [ ] **Step 1: Create the branch**

```bash
git checkout -b feat/metadata-lookup
```

- [ ] **Step 2: Verify both documents are present and untracked**

Run: `git status --short docs/`
Expected: `?? docs/plans/2026-08-14-metadata-lookup.md` and `?? docs/specs/2026-08-14-metadata-lookup-design.md`

- [ ] **Step 3: Commit**

```bash
git add docs/specs/2026-08-14-metadata-lookup-design.md docs/plans/2026-08-14-metadata-lookup.md
git commit -m "docs: design the metadata lookup commands"
```

---

### Task 2: Response DTOs and the walker's JsonElement case

**Files:**
- Create: `src/GrimoireCli/Models/MetadataSource.cs`, `src/GrimoireCli/Models/MetadataSourceList.cs`, `src/GrimoireCli/Models/MetadataCandidate.cs`, `src/GrimoireCli/Models/MetadataSearchResult.cs`, `src/GrimoireCli/Models/MetadataFieldDiff.cs`, `src/GrimoireCli/Models/MetadataFetchResult.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`, `tools/GenerateResponseExamples/SampleJsonWalker.cs`
- Regenerate: `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Models/MetadataDtoTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `GrimoireCli.Models.MetadataSourceList` (`Sources`), `MetadataSearchResult` (`Query`, `Results`), `MetadataFetchResult` (`SourceId`, `Identity`, `Url`, `Attribution`, `Fields`), `MetadataFieldDiff` (`Field`, `Current`, `Incoming`, `Status`), all registered on `AppJsonContext` as `AppJsonContext.Default.<TypeName>`

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Models/MetadataDtoTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class MetadataDtoTests
{
    // Shape from routers/_metadata_lookup.py:list_sources. supports_paste is
    // what tells a caller whether metadata-fetch --paste is available.
    [Fact]
    public void SourceListCarriesSupportsPaste()
    {
        const string json = """
        {"sources": [{"id": "fixture-source", "name": "Fixture Source",
          "description": "Local fixture.", "homepage": "", "attribution": "",
          "supports_paste": true}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataSourceList)!;
        var source = Assert.Single(result.Sources!);
        Assert.Equal("fixture-source", source.Id);
        Assert.True(source.SupportsPaste);
    }

    // Shape from addons/interpreter.py:search. query echoes the effective
    // query, which is the resource's own name when the caller sent none.
    [Fact]
    public void SearchResultEchoesTheEffectiveQuery()
    {
        const string json = """
        {"query": "Shadowrun 4 DE",
         "results": [{"identity": "shadowrun-4-de", "label": "Shadowrun 4 DE",
           "score": 1.0, "url": "https://fixture.test/systems/shadowrun-4-de"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataSearchResult)!;
        Assert.Equal("Shadowrun 4 DE", result.Query);
        var candidate = Assert.Single(result.Results!);
        Assert.Equal("shadowrun-4-de", candidate.Identity);
        Assert.Equal(1.0, candidate.Score);
    }

    // current and incoming are typed per field by the server: a string, an int,
    // a list of strings, or a list of objects. JsonElement is what lets one DTO
    // carry all four, and re-emit each verbatim.
    [Fact]
    public void FieldDiffCarriesEveryValueShape()
    {
        const string json = """
        {"source_id": "fixture-source", "identity": "shadowrun-4-de",
         "url": "https://fixture.test/systems/shadowrun-4-de",
         "attribution": "Fixture data",
         "fields": [
           {"field": "system_family", "current": null, "incoming": "Shadowrun",
            "status": "only_incoming"},
           {"field": "year", "current": 2005, "incoming": 2006, "status": "differs"},
           {"field": "genres", "current": ["Cyberpunk"],
            "incoming": ["Cyberpunk", "Urban Fantasy"], "status": "differs"},
           {"field": "urls", "current": [{"label": "Wiki", "url": "https://a"}],
            "incoming": [{"label": "Wiki", "url": "https://a"},
                         {"label": "Source", "url": "https://b"}],
            "status": "only_incoming"}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataFetchResult)!;
        Assert.Equal("fixture-source", result.SourceId);
        Assert.Equal(4, result.Fields!.Count);
        Assert.Equal("only_incoming", result.Fields[0].Status);
        Assert.Null(result.Fields[0].Current);
        Assert.Equal("Shadowrun", result.Fields[0].Incoming!.Value.GetString());
        Assert.Equal(2005, result.Fields[1].Current!.Value.GetInt32());
        Assert.Equal(JsonValueKind.Array, result.Fields[2].Incoming!.Value.ValueKind);
        Assert.Equal("Source",
            result.Fields[3].Incoming!.Value[1].GetProperty("label").GetString());
    }

    // Round-tripping is what proves stdout stays the server's own JSON: the
    // CLI writes these DTOs back out, and a JsonElement must survive that.
    [Fact]
    public void FieldDiffReEmitsItsValuesVerbatim()
    {
        const string json = """
        {"field": "publishers",
         "current": null,
         "incoming": [{"name": "FanPro", "url": "https://fanpro.test"}],
         "status": "only_incoming"}
        """;
        var diff = JsonSerializer.Deserialize(json, AppJsonContext.Default.MetadataFieldDiff)!;
        var written = JsonSerializer.Serialize(diff, AppJsonContext.Default.MetadataFieldDiff);
        using var reparsed = JsonDocument.Parse(written);
        var incoming = reparsed.RootElement.GetProperty("incoming");
        Assert.Equal("FanPro", incoming[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, reparsed.RootElement.GetProperty("current").ValueKind);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~MetadataDtoTests`
Expected: build failure — `AppJsonContext.Default.MetadataSourceList` and the other four do not exist.

- [ ] **Step 3: Write the DTOs**

Create `src/GrimoireCli/Models/MetadataSource.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One add-on able to supply metadata (routers/_metadata_lookup.py:list_sources).</summary>
public class MetadataSource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    /// <summary>True when the manifest declares an identity_pattern, which is what
    /// makes metadata-fetch --paste resolvable for this source.</summary>
    [JsonPropertyName("supports_paste")]
    public bool SupportsPaste { get; set; }
}
```

Create `src/GrimoireCli/Models/MetadataSourceList.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>GET /api/{systems,books}/{id}/metadata-sources response.</summary>
public class MetadataSourceList
{
    [JsonPropertyName("sources")]
    public List<MetadataSource>? Sources { get; set; }
}
```

Create `src/GrimoireCli/Models/MetadataCandidate.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>One ranked search hit (addons/interpreter.py:search).</summary>
public class MetadataCandidate
{
    /// <summary>The source's own key for this record; what metadata-fetch --identity takes.</summary>
    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
```

Create `src/GrimoireCli/Models/MetadataSearchResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/{systems,books}/{id}/metadata-search response.</summary>
public class MetadataSearchResult
{
    /// <summary>The query actually searched, after the server's fallback to the
    /// system's name or the book's title.</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("results")]
    public List<MetadataCandidate>? Results { get; set; }
}
```

Create `src/GrimoireCli/Models/MetadataFieldDiff.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// One field's comparison (addons/diff.py:build). current and incoming are typed
/// by the field they describe — a string, an int, a list of strings, or a list of
/// objects — so they are carried as JsonElement and re-emitted verbatim.
/// </summary>
public class MetadataFieldDiff
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("current")]
    public JsonElement? Current { get; set; }

    [JsonPropertyName("incoming")]
    public JsonElement? Incoming { get; set; }

    /// <summary>only_incoming, differs, or same.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
```

Create `src/GrimoireCli/Models/MetadataFetchResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/{systems,books}/{id}/metadata-fetch response. A report, not a write.</summary>
public class MetadataFetchResult
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    [JsonPropertyName("fields")]
    public List<MetadataFieldDiff>? Fields { get; set; }
}
```

- [ ] **Step 4: Register the six types on `AppJsonContext`**

In `src/GrimoireCli/Models/JsonContext.cs`, add beneath the existing `Addon*` registrations:

```csharp
[JsonSerializable(typeof(MetadataSource))]
[JsonSerializable(typeof(MetadataSourceList))]
[JsonSerializable(typeof(MetadataCandidate))]
[JsonSerializable(typeof(MetadataSearchResult))]
[JsonSerializable(typeof(MetadataFieldDiff))]
[JsonSerializable(typeof(MetadataFetchResult))]
```

- [ ] **Step 5: Run the DTO tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~MetadataDtoTests`
Expected: PASS, 4 tests.

- [ ] **Step 6: Run the response-example drift test to see the walker fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~ResponseExamplesDriftTest`
Expected: FAIL — `ResponseExamples.g.cs` is stale, and the freshly generated file renders `JsonElement`'s own properties (`ValueKind`, …) for `current` and `incoming`.

- [ ] **Step 7: Teach the walker about `JsonElement`**

In `tools/GenerateResponseExamples/SampleJsonWalker.cs`, immediately after the `if (type == typeof(string))` block in `WriteValue`:

```csharp
        // A JsonElement property is a value whose type is decided per row by the
        // server (metadata diff rows carry a string, an int, or a list). There is
        // no single shape to render, so the sample says so.
        if (type == typeof(JsonElement))
        {
            writer.WriteStringValue("<any>");
            return;
        }
```

Add `using System.Text.Json;` to the file's usings if it is not already there.

- [ ] **Step 8: Regenerate the response examples**

Run: `dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs`
Expected: exit 0; `git diff` shows six new entries, with `"current": "<any>"` and `"incoming": "<any>"` inside `MetadataFieldDiff` and `MetadataFetchResult`.

- [ ] **Step 9: Format, build, and run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: all green, including `ResponseExamplesDriftTest` and `ResponseExamplesJsonValidTest`.

- [ ] **Step 10: Commit**

```bash
git add src/GrimoireCli/Models/ src/GrimoireCli/Commands/ResponseExamples.g.cs \
        tools/GenerateResponseExamples/SampleJsonWalker.cs \
        tests/GrimoireCli.Tests/Models/MetadataDtoTests.cs
git commit -m "feat: add metadata lookup response models"
```

---

### Task 3: MetadataService

**Files:**
- Create: `src/GrimoireCli/Services/MetadataService.cs`
- Test: `tests/GrimoireCli.Tests/Services/MetadataServiceTests.cs`

**Interfaces:**
- Consumes: the six DTOs from Task 2; `GrimoireApiClient.SendAsync(RequestInformation, JsonTypeInfo<T>, string? permissionHint, string? notFoundHint)`
- Produces:
  - `new MetadataService(GrimoireApiClient client, string resource)` where `resource` is `"systems"` or `"books"`
  - `Task<MetadataSourceList> SourcesAsync(string id)`
  - `Task<MetadataSearchResult> SearchAsync(string id, string sourceId, string? query)`
  - `Task<MetadataFetchResult> FetchAsync(string id, string sourceId, string? identity, string? query, string? paste)`
  - `internal static Generated.Models.MetadataFetch BuildFetchBody(string sourceId, string? identity, string? query, string? paste)`

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Services/MetadataServiceTests.cs`:

```csharp
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class MetadataServiceTests
{
    // The generated model's properties are plain strings whose constructor sets
    // none of them, so an omitted flag must stay unset rather than be sent as "".
    // Pinned here because a client regeneration could change that quietly.
    [Fact]
    public void FetchBodyOmitsWhatWasNotGiven()
    {
        var body = MetadataService.BuildFetchBody("fixture-source", identity: "abc",
            query: null, paste: null);
        Assert.Equal("fixture-source", body.SourceId);
        Assert.Equal("abc", body.Identity);
        Assert.Null(body.Query);
        Assert.Null(body.Paste);
    }

    [Fact]
    public void FetchBodyCarriesPasteInsteadOfIdentity()
    {
        var body = MetadataService.BuildFetchBody("fixture-source", identity: null,
            query: null, paste: "https://fixture.test/systems/shadowrun-4-de");
        Assert.Null(body.Identity);
        Assert.Equal("https://fixture.test/systems/shadowrun-4-de", body.Paste);
    }

    // An unknown resource is a programming error, not user input: the only two
    // callers pass a literal.
    [Fact]
    public void AnUnknownResourceIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MetadataService(null!, "maps"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~MetadataServiceTests`
Expected: build failure — `MetadataService` does not exist.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/MetadataService.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

/// <summary>
/// The three add-on metadata endpoints, which systems and books share.  Upstream
/// serves both from one implementation (routers/_metadata_lookup.py); the two
/// differ here only in the generated builder each path reaches for, so the
/// resource is a constructor argument rather than a second class.
///
/// All three are reads.  Applying a fetched value is the caller's own
/// systems update / books update.
/// </summary>
public class MetadataService
{
    private const string Systems = "systems";
    private const string Books = "books";

    private readonly GrimoireApiClient _client;
    private readonly string _resource;

    public MetadataService(GrimoireApiClient client, string resource)
    {
        if (resource is not (Systems or Books))
            throw new ArgumentException($"Unsupported metadata resource '{resource}'.", nameof(resource));
        _client = client;
        _resource = resource;
    }

    private string NotFoundHint => _resource == Systems
        ? "No system with that ID. List them with: grimoire-cli systems list"
        : "No book with that ID. List them with: grimoire-cli books list";

    public async Task<MetadataSourceList> SourcesAsync(string id)
    {
        var info = _resource == Systems
            ? _client.Api.Api.Systems[id].MetadataSources.ToGetRequestInformation()
            : _client.Api.Api.Books[id].MetadataSources.ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.MetadataSourceList,
            permissionHint: "the gm or admin role",
            notFoundHint: NotFoundHint);
    }

    public async Task<MetadataSearchResult> SearchAsync(string id, string sourceId, string? query)
    {
        var info = _resource == Systems
            ? _client.Api.Api.Systems[id].MetadataSearch.ToPostRequestInformation(
                new Generated.Models.Backend__routers__systems___schemas__MetadataSearch
                {
                    SourceId = sourceId,
                    Query = query,
                })
            : _client.Api.Api.Books[id].MetadataSearch.ToPostRequestInformation(
                new Generated.Models.Backend__routers__books___schemas__MetadataSearch
                {
                    SourceId = sourceId,
                    Query = query,
                });
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.MetadataSearchResult,
            permissionHint: "the gm or admin role",
            notFoundHint: NotFoundHint);
    }

    public async Task<MetadataFetchResult> FetchAsync(
        string id, string sourceId, string? identity, string? query, string? paste)
    {
        var body = BuildFetchBody(sourceId, identity, query, paste);
        var info = _resource == Systems
            ? _client.Api.Api.Systems[id].MetadataFetch.ToPostRequestInformation(body)
            : _client.Api.Api.Books[id].MetadataFetch.ToPostRequestInformation(body);
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.MetadataFetchResult,
            permissionHint: "the gm or admin role",
            notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// The generated model sets none of its properties in its constructor, so an
    /// omitted flag stays absent from the body and the server applies its own
    /// default. Internal (not private) so a test can pin that a client
    /// regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.MetadataFetch BuildFetchBody(
        string sourceId, string? identity, string? query, string? paste)
        => new()
        {
            SourceId = sourceId,
            Identity = identity,
            Query = query,
            Paste = paste,
        };
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~MetadataServiceTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Format and build**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
```
Expected: no changes from format, build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Services/MetadataService.cs tests/GrimoireCli.Tests/Services/MetadataServiceTests.cs
git commit -m "feat: add metadata lookup service"
```

---

### Task 4: The six commands

**Files:**
- Create: `src/GrimoireCli/Commands/MetadataCommands.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs:16-22` (the `Create()` body), `src/GrimoireCli/Commands/BooksCommand.cs` (its `Create()` body)
- Test: `tests/GrimoireCli.Tests/Commands/MetadataCommandTests.cs`

**Interfaces:**
- Consumes: `MetadataService(client, resource)` and its three methods from Task 3; `CommandHelper.BuildClient(serverOverride, tokenOverride)`; `ConsoleOutput.WriteJson(value, jsonTypeInfo)`; `HelpExtensions.AddRoleRequired/AddHelpSection/AddExamples/AddResponseExample<T>`
- Produces: `MetadataCommands.Create(string resource)` returning `IEnumerable<Command>` — the three subcommands for that resource, in the order `metadata-sources`, `metadata-search`, `metadata-fetch`

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/MetadataCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class MetadataCommandTests
{
    private static string SystemsHelp(string leaf, bool full) =>
        HelpRenderer.Render(SystemsCommand.Create(), ["systems", leaf], full);

    private static string BooksHelp(string leaf, bool full) =>
        HelpRenderer.Render(BooksCommand.Create(), ["books", leaf], full);

    [Theory]
    [InlineData("metadata-sources")]
    [InlineData("metadata-search")]
    [InlineData("metadata-fetch")]
    public void EveryCommandExistsOnBothResources(string leaf)
    {
        Assert.Contains(leaf, SystemsHelp(leaf, full: false));
        Assert.Contains(leaf, BooksHelp(leaf, full: false));
    }

    // All six routes are require_gm_or_admin.
    [Theory]
    [InlineData("metadata-sources")]
    [InlineData("metadata-search")]
    [InlineData("metadata-fetch")]
    public void EveryCommandIsTaggedGmOrAdmin(string leaf)
    {
        Assert.Contains("gm or admin", SystemsHelp(leaf, full: false));
        Assert.Contains("gm or admin", BooksHelp(leaf, full: false));
    }

    // The empty-sources case has three distinct causes and only one of them is
    // "no add-on installed", so help must point at where it is diagnosed.
    [Fact]
    public void SourcesExplainsWhyTheListIsEmpty()
    {
        var output = SystemsHelp("metadata-sources", full: false);
        Assert.Contains("addons list", output);
        Assert.Contains("targets this resource type", output);
        Assert.Contains("supports_paste", output);
    }

    // The fallback noun is the only wording that differs between the two sets.
    [Fact]
    public void SearchNamesTheResourcesOwnFallback()
    {
        Assert.Contains("defaults to the name", SystemsHelp("metadata-search", full: false));
        Assert.Contains("defaults to the title", BooksHelp("metadata-search", full: false));
    }

    [Fact]
    public void FetchSaysItWritesNothingAndNamesTheApplyPath()
    {
        var output = SystemsHelp("metadata-fetch", full: false);
        Assert.Contains("Writes nothing", output);
        Assert.Contains("systems update", output);
        Assert.Contains("only_incoming", output);
        Assert.Contains("union with the existing list", output);
    }

    [Fact]
    public void FetchNamesBooksUpdateOnBooks()
    {
        Assert.Contains("books update", BooksHelp("metadata-fetch", full: false));
    }

    [Fact]
    public void SearchRendersItsResponseShape()
    {
        var output = SystemsHelp("metadata-search", full: true);
        Assert.Contains("\"identity\":", output);
        Assert.Contains("\"score\":", output);
    }

    [Fact]
    public void FetchRendersItsResponseShape()
    {
        var output = SystemsHelp("metadata-fetch", full: true);
        Assert.Contains("\"status\":", output);
        Assert.Contains("\"<any>\"", output);
    }

    // Neither flag is a request the server answers with anything but a 400, and
    // both is ambiguous — the server silently prefers paste.
    [Fact]
    public void FetchRefusesNeitherIdentityNorPaste()
    {
        var result = SystemsCommand.Create().Parse(["metadata-fetch", "--id", "1", "--source-id", "x"]);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("--identity") && e.Message.Contains("--paste"));
    }

    [Fact]
    public void FetchRefusesBothIdentityAndPaste()
    {
        var result = SystemsCommand.Create().Parse(
            ["metadata-fetch", "--id", "1", "--source-id", "x", "--identity", "a", "--paste", "b"]);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void FetchAcceptsEitherOne()
    {
        Assert.Empty(SystemsCommand.Create()
            .Parse(["metadata-fetch", "--id", "1", "--source-id", "x", "--identity", "a"]).Errors);
        Assert.Empty(SystemsCommand.Create()
            .Parse(["metadata-fetch", "--id", "1", "--source-id", "x", "--paste", "b"]).Errors);
    }

    [Fact]
    public void SearchRequiresASourceId()
    {
        var result = SystemsCommand.Create().Parse(["metadata-search", "--id", "1"]);
        Assert.NotEmpty(result.Errors);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~MetadataCommandTests`
Expected: FAIL — no `metadata-sources` subcommand exists on either resource.

- [ ] **Step 3: Write the command factory**

Create `src/GrimoireCli/Commands/MetadataCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The add-on metadata trio, built once and added to both systems and books.
/// The endpoints are one implementation upstream against two targets
/// (routers/_metadata_lookup.py), and differ here only in the resource noun and
/// the fallback the server substitutes for an empty query.
/// </summary>
public static class MetadataCommands
{
    public static IEnumerable<Command> Create(string resource)
    {
        var fallback = resource == "systems" ? "name" : "title";
        yield return CreateSourcesCommand(resource);
        yield return CreateSearchCommand(resource, fallback);
        yield return CreateFetchCommand(resource, fallback);
    }

    private static Option<string> IdOption(string resource) =>
        new("--id") { Description = resource == "systems" ? "System ID" : "Book ID", Required = true };

    private static Command CreateSourcesCommand(string resource)
    {
        var idOption = IdOption(resource);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("metadata-sources", "List add-ons that can supply metadata")
        {
            idOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Add-ons that can answer for this resource. Empty until one is installed,",
            "enabled and runnable (addons list) and targets this resource type — a",
            "book source never appears here for a system.",
            "",
            "supports_paste false means metadata-fetch --paste is a 400 for that",
            "source; search for an identity instead.");
        command.AddExamples($"grimoire-cli {resource} metadata-sources --id <id>");
        command.AddResponseExample<MetadataSourceList>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new MetadataService(client, resource);
            var result = await service.SourcesAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.MetadataSourceList);
            return 0;
        });
        return command;
    }

    private static Command CreateSearchCommand(string resource, string fallback)
    {
        var idOption = IdOption(resource);
        var sourceIdOption = new Option<string>("--source-id")
        {
            Description = "Source add-on ID, from metadata-sources",
            Required = true,
        };
        var queryOption = new Option<string?>("--query") { Description = $"Search text; defaults to the {fallback}" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("metadata-search", "Search one add-on for candidates")
        {
            idOption, sourceIdOption, queryOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Candidates only — identity, label, score, url. No field data; that is",
            "metadata-fetch.",
            "",
            $"An omitted --query defaults to the {fallback}; query echoes back what was",
            "actually searched. Pass the same value to metadata-fetch: search-backed",
            "sources answer per query, not from a catalogue.",
            "",
            "[] means the source matched nothing. 502 means it could not be reached",
            "or returned junk.");
        command.AddExamples($"grimoire-cli {resource} metadata-search --id <id> --source-id <source>");
        command.AddResponseExample<MetadataSearchResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new MetadataService(client, resource);
            var result = await service.SearchAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceIdOption)!,
                parseResult.GetValue(queryOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.MetadataSearchResult);
            return 0;
        });
        return command;
    }

    private static Command CreateFetchCommand(string resource, string fallback)
    {
        var idOption = IdOption(resource);
        var sourceIdOption = new Option<string>("--source-id")
        {
            Description = "Source add-on ID, from metadata-sources",
            Required = true,
        };
        var identityOption = new Option<string?>("--identity") { Description = "Candidate identity, from metadata-search" };
        var queryOption = new Option<string?>("--query") { Description = $"Query the candidate came from; defaults to the {fallback}" };
        var pasteOption = new Option<string?>("--paste") { Description = "Source URL or bare ID, instead of --identity" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("metadata-fetch", "Diff one candidate against this resource")
        {
            idOption, sourceIdOption, identityOption, queryOption, pasteOption, serverOption, tokenOption
        };
        command.AddRoleRequired("gm or admin");
        command.Validators.Add(result =>
        {
            var hasIdentity = result.GetValue(identityOption) is not null;
            var hasPaste = result.GetValue(pasteOption) is not null;
            if (hasIdentity == hasPaste)
                result.AddError("Pass exactly one of --identity or --paste.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            $"Writes nothing. Reports, per field, what this resource has now and what",
            $"the source offers; apply what you want with {resource} update.",
            "",
            "Exactly one of --identity (from metadata-search) or --paste (a source",
            "URL or bare ID, only where supports_paste is true).",
            "",
            "status is only_incoming (empty here), differs, or same, sorted in that",
            "order. A field the source has nothing for is omitted, so nothing is ever",
            "proposed to be blanked. incoming for urls and character_builder_urls is",
            "the union with the existing list, not a replacement.",
            "",
            "502 is a source failure, 400 a configuration one.");
        command.AddExamples($"grimoire-cli {resource} metadata-fetch --id <id> --source-id <source> --identity <identity>");
        command.AddResponseExample<MetadataFetchResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new MetadataService(client, resource);
            var result = await service.FetchAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceIdOption)!,
                parseResult.GetValue(identityOption),
                parseResult.GetValue(queryOption),
                parseResult.GetValue(pasteOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.MetadataFetchResult);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Wire the trio into both groups**

In `src/GrimoireCli/Commands/SystemsCommand.cs`, at the end of `Create()`'s `Subcommands.Add` run and before `return command;`:

```csharp
        foreach (var metadata in MetadataCommands.Create("systems"))
            command.Subcommands.Add(metadata);
```

Do the same in `src/GrimoireCli/Commands/BooksCommand.cs` with `"books"`.

- [ ] **Step 5: Run the command tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~MetadataCommandTests`
Expected: PASS.

If `FetchRendersItsResponseShape` fails on `"<any>"`, Task 2 Step 7 was skipped or the generator was not re-run.

- [ ] **Step 6: Run the whole suite, format, build**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: all green. `RootHelpTests` and `HelpOutputTests` may assert subcommand counts — update those counts if they fail, since six commands were added deliberately.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/ tests/GrimoireCli.Tests/Commands/MetadataCommandTests.cs
git commit -m "feat: add metadata lookup commands on systems and books"
```

---

### Task 5: Fixture catalogue and smoke test

**Files:**
- Create: `docker/addon-index/catalogue.json`
- Modify: `docker/addon-index/fixture-source.yml`, `docker/smoke-test.sh` (a new section between the `addons list` assertions and `addons update --enabled false`)

**Interfaces:**
- Consumes: the six commands from Task 4
- Produces: a fixture add-on that answers searches, so `metadata-search` and `metadata-fetch` run against a real source in CI

**Preconditions:** a running, seeded stack:

```bash
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- [ ] **Step 1: Write the catalogue**

Create `docker/addon-index/catalogue.json`. Hand-written and checked in — unlike `index.json` it carries no digest, so `make-addon-index.py` has no reason to generate it:

```json
[
  {
    "slug": "shadowrun-4-de",
    "name": "Shadowrun 4 DE",
    "family": "Shadowrun",
    "parent": "Shadowrun (fixture)",
    "description": "smoke fixture description"
  },
  {
    "slug": "fixture-explicit-rpg",
    "name": "Fixture Explicit RPG",
    "family": "Fixture",
    "parent": "Fixture (fixture)",
    "description": "a second record, so search has something to rank against"
  }
]
```

The three mapped fields are chosen so each diff status is deterministic against
`Shadowrun 4 DE`: `family` lands in an empty `system_family`, `description`
matches what the systems section wrote earlier in the run, and `parent` disagrees
with the folder-derived `parent_system`.

- [ ] **Step 2: Point the manifest at it and add the mapping**

Replace `docker/addon-index/fixture-source.yml` with:

```yaml
# Minimal valid add-on for the smoke test, answering from a catalogue served by
# the addon-index nginx service on the compose network. cache_ttl 0 keeps every
# lookup a fresh fetch, so editing catalogue.json takes effect on the next run
# without a cache to clear.
#
# The three mapped fields are chosen so a fetch against Shadowrun 4 DE produces
# one row of each status: system_family is empty there (the "--family Shadowrun
# should match 2" assertion depends on it staying empty), description is written
# by the systems section earlier in the same run, and parent_system is
# folder-derived and therefore disagrees with the value below. Fetching writes
# nothing, so mapping a field here cannot disturb an assertion elsewhere.
id: fixture-source
name: Fixture Source
version: 1.0.0
kind: scraper
target: game-system
description: Local fixture for the grimoire-cli smoke test.
attribution: Fixture data, not a real source.
source:
  url: http://addon-index/catalogue.json
  format: json
  cache_ttl: 0
records:
  root: "$"
search:
  fields:
    - field: name
  identity:
    from: slug
  label:
    from: name
  url:
    template: "https://fixture.test/systems/{slug}"
  identity_pattern: "fixture\\.test/systems/([a-z0-9-]+)$"
map:
  system_family:
    from: family
  description:
    from: description
  parent_system:
    from: parent
```

- [ ] **Step 3: Regenerate the index and verify the manifest loads**

```bash
python3 docker/make-addon-index.py
```

Then, with the stack up:

```bash
dotnet run --project src/GrimoireCli -- addons settings --index-url http://addon-index/index.json
dotnet run --project src/GrimoireCli -- addons refresh
dotnet run --project src/GrimoireCli -- addons install --id fixture-source
```
Expected: `install` exits 0 with `"runnable": true`. A validation error here means the manifest is malformed — `AddonManifest` in `temp/grimoire/backend/addons/manifest.py` rejects unknown keys. A digest mismatch means `make-addon-index.py` was not re-run.

- [ ] **Step 4: Verify the three statuses by hand before writing assertions**

```bash
SR4=$(dotnet run --project src/GrimoireCli -- systems list --include-children \
  | jq -r '.[] | select(.name == "Shadowrun 4 DE") | .id')
dotnet run --project src/GrimoireCli -- systems metadata-sources --id "$SR4"
dotnet run --project src/GrimoireCli -- systems metadata-search --id "$SR4" --source-id fixture-source
dotnet run --project src/GrimoireCli -- systems metadata-fetch --id "$SR4" \
  --source-id fixture-source --identity shadowrun-4-de
```
Expected: `sources` lists `fixture-source` with `supports_paste: true`; `search` returns `shadowrun-4-de` first; `fetch` returns `system_family` as `only_incoming`, `description` as `same`, `parent_system` as `differs`.

If `description` comes back `differs`, the systems section's write has not run in this stack — run `bash docker/smoke-test.sh` once first. If `parent_system` comes back `only_incoming`, the fixture library tree no longer gives that system a container parent; record the real value and adjust `catalogue.json` so the row still disagrees.

- [ ] **Step 5: Add the smoke-test section**

In `docker/smoke-test.sh`, insert after the `ok "addons list shows the fixture under both installed and available"` line and **before** `UPDATE_JSON=$("$CLI" addons update --id fixture-source --enabled false ...)`:

```bash
# --- metadata lookup ------------------------------------------------------
# Runs here, between install and the disable below: a disabled add-on is not
# runnable and drops out of metadata-sources. It also depends on the systems
# section above having written description — that write is what makes the
# description row "same" rather than "differs".
SOURCES_JSON=$("$CLI" systems metadata-sources --id "$SR4" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-sources exited non-zero"; }
echo "$SOURCES_JSON" | jq -e '.sources[] | select(.id == "fixture-source" and .supports_paste == true)' >/dev/null \
  || fail "fixture-source should offer itself with supports_paste true: $SOURCES_JSON"
ok "systems metadata-sources lists the installed fixture add-on"

# The fixture targets game-system, so an empty list here is target filtering
# working rather than the endpoint returning nothing.
BOOKSOURCES_JSON=$("$CLI" books metadata-sources --id "$SR4_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books metadata-sources exited non-zero"; }
[ "$(echo "$BOOKSOURCES_JSON" | jq '.sources | length')" -eq 0 ] \
  || fail "a game-system add-on must not appear as a book source: $BOOKSOURCES_JSON"
ok "books metadata-sources excludes a game-system add-on"

SEARCH_JSON=$("$CLI" systems metadata-search --id "$SR4" --source-id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-search exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq -r .query)" = "Shadowrun 4 DE" ] \
  || fail "an omitted --query should echo back the system's name: $SEARCH_JSON"
[ "$(echo "$SEARCH_JSON" | jq -r '.results[0].identity')" = "shadowrun-4-de" ] \
  || fail "the fixture record should rank first: $SEARCH_JSON"
ok "systems metadata-search defaults its query to the system name"

FETCH_JSON=$("$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --identity shadowrun-4-de 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-fetch exited non-zero"; }
[ "$(echo "$FETCH_JSON" | jq -r '.fields[] | select(.field == "system_family") | .status')" = "only_incoming" ] \
  || fail "system_family is empty on this fixture, so it must read only_incoming: $FETCH_JSON"
# same, not differs, only because the systems section wrote this description
# earlier in this run. A differs here means that write moved or stopped.
[ "$(echo "$FETCH_JSON" | jq -r '.fields[] | select(.field == "description") | .status')" = "same" ] \
  || fail "description should match what the systems section wrote; did that write move? $FETCH_JSON"
# parent_system is folder-derived, so it is populated and disagrees with the
# catalogue's value. only_incoming here means the fixture tree changed shape.
[ "$(echo "$FETCH_JSON" | jq -r '.fields[] | select(.field == "parent_system") | .status')" = "differs" ] \
  || fail "parent_system is folder-derived and should disagree with the fixture: $FETCH_JSON"
ok "systems metadata-fetch reports one row of each status"

PASTE_JSON=$("$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --paste "https://fixture.test/systems/shadowrun-4-de" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-fetch --paste exited non-zero"; }
[ "$(echo "$PASTE_JSON" | jq -r .identity)" = "shadowrun-4-de" ] \
  || fail "--paste should resolve to the same identity the search returned: $PASTE_JSON"
ok "systems metadata-fetch --paste resolves a source URL to an identity"

# Fetching is a read. The field it offered must still be empty afterwards —
# and the family filter assertion earlier depends on it.
sysget --id "$SR4"
[ -z "$(echo "$GET_JSON" | jq -r '.system_family // ""')" ] \
  || fail "metadata-fetch must not have written system_family: $(echo "$GET_JSON" | jq -r .system_family)"
ok "metadata-fetch left the system unchanged"

set +e
"$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source >/dev/null 2>"$WORK/fetchargs.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "metadata-fetch with neither --identity nor --paste should exit 1, got $rc"
grep -q -- "--identity" "$WORK/fetchargs.err" \
  || fail "no mention of --identity: $(cat "$WORK/fetchargs.err")"
set +e
"$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --identity shadowrun-4-de --paste "https://fixture.test/systems/shadowrun-4-de" \
  >/dev/null 2>"$WORK/fetchboth.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "metadata-fetch with both --identity and --paste should exit 1, got $rc"
ok "metadata-fetch requires exactly one of --identity and --paste"
```

- [ ] **Step 6: Run the whole smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```
Expected: both runs green. A second run that fails where the first passed means something in the new section is not idempotent — every call it makes is a read, so suspect ordering against the `addons` section rather than the assertions themselves.

- [ ] **Step 7: Commit**

```bash
git add docker/addon-index/catalogue.json docker/addon-index/fixture-source.yml docker/smoke-test.sh
git commit -m "test: cover metadata lookup against a fixture source"
```

---

### Task 6: Docs, verification and PR

**Files:**
- Modify: `README.md` (Commands table), `tools/generate-api-coverage.py:41-` (`IMPLEMENTED`), `docs/grimoire-api-coverage.md` (regenerated), `docs/grimoire-api-notes.md`, `docs/roadmap.md`
- Do not touch: `CHANGELOG.md`

**Interfaces:**
- Consumes: everything above
- Produces: a green PR

- [ ] **Step 1: Add the six rows to the README Commands table**

Insert beside the existing `systems` and `books` rows, matching the table's own column format:

```markdown
| `systems metadata-sources --id <id>` | Add-ons that can supply metadata for this system (gm or admin) |
| `systems metadata-search --id <id> --source-id <src> [--query]` | Ranked candidates from one add-on (gm or admin) |
| `systems metadata-fetch --id <id> --source-id <src> {--identity <i> \| --paste <url>} [--query]` | Diff a candidate against the system; writes nothing (gm or admin) |
| `books metadata-sources --id <id>` | Add-ons that can supply metadata for this book (gm or admin) |
| `books metadata-search --id <id> --source-id <src> [--query]` | Ranked candidates from one add-on (gm or admin) |
| `books metadata-fetch --id <id> --source-id <src> {--identity <i> \| --paste <url>} [--query]` | Diff a candidate against the book; writes nothing (gm or admin) |
```

- [ ] **Step 2: Update `IMPLEMENTED` and regenerate the coverage table**

In `tools/generate-api-coverage.py`, add to the `IMPLEMENTED` dict:

```python
    "GET /api/systems/{system_id}/metadata-sources": "`systems metadata-sources` ✅",
    "POST /api/systems/{system_id}/metadata-search": "`systems metadata-search` ✅",
    "POST /api/systems/{system_id}/metadata-fetch": "`systems metadata-fetch` ✅",
    "GET /api/books/{book_id}/metadata-sources": "`books metadata-sources` ✅",
    "POST /api/books/{book_id}/metadata-search": "`books metadata-search` ✅",
    "POST /api/books/{book_id}/metadata-fetch": "`books metadata-fetch` ✅",
```

Then regenerate:

```bash
python3 tools/generate-api-coverage.py
```
Expected: `docs/grimoire-api-coverage.md` shows the six commands and a higher covered count.

- [ ] **Step 3: Record verified behaviour in `docs/grimoire-api-notes.md`**

Add a `## Metadata lookup` section stating only what the live runs confirmed — the effective-query echo, the three statuses observed and their ordering, that a fetch left the resource unchanged, that a `game-system` add-on does not appear as a book source, and any 502/400 message text actually seen. Do not restate the design doc.

- [ ] **Step 4: Drop the shipped roadmap item**

In `docs/roadmap.md`, remove item 1 ("Metadata lookup, systems and books in one pass") and renumber the rest. The roadmap lists intended work; an item leaves when it ships. Keep the "first release is cut after this" sentence with whatever now carries it, or move it to the release item if that is where it belongs.

- [ ] **Step 5: Run all four pre-PR checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```
Expected: all four exit 0. Do not open the PR on a failure — fix it and re-run all four.

- [ ] **Step 6: Commit and push**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record the metadata lookup commands"
git push -u origin feat/metadata-lookup
```

- [ ] **Step 7: Open the PR**

```bash
gh pr create --title "feat: metadata lookup on systems and books" --body "$(cat <<'EOF'
Six commands over the add-on metadata endpoints — `metadata-sources`,
`metadata-search`, `metadata-fetch` on both `systems` and `books`.

All three are reads. Fetching reports, per field, what the resource has now and
what the source offers; applying stays `systems update` / `books update`.

- One command factory and one service, parameterised by resource, mirroring the
  server's shared `routers/_metadata_lookup.py`.
- `current` / `incoming` are `JsonElement?` — their type is decided per row by
  the field they describe.
- The smoke-test fixture add-on now answers from a catalogue served on the
  compose network, so search and fetch run end to end without scraping anyone.

Design: `docs/specs/2026-08-14-metadata-lookup-design.md`
EOF
)"
```

- [ ] **Step 8: Watch CI to a terminal state**

Run: `gh pr checks <num> --watch`
Expected: every check green. Report the result. A PR is done at "all checks green", not at "PR open".

---

## Notes for the implementer

- **`HelpRenderer`** (`tests/GrimoireCli.Tests/Commands/HelpRenderer.cs`) is the existing test helper for rendering a subcommand's help; `full: true` includes the response-shape block.
- **The generated request builders already exist** — `src/GrimoireCli/Generated/Api/{Systems,Books}/Item/{MetadataSources,MetadataSearch,MetadataFetch}/`. Nothing in this plan regenerates the API client.
- **The two search request models have different generated names** (`Backend__routers__systems___schemas__MetadataSearch` and `..._books__...`) because FastAPI names them per module; the fetch model is a single shared `MetadataFetch`. That asymmetry is why the service switches on resource rather than taking a builder.
- **Never point the smoke test's stack at the community add-on index**, and never run `addons refresh` while it is pointed there.
