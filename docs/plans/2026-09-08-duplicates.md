# Duplicate Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap the whole `duplicates` router — thirteen admin endpoints — as thin pass-through commands under one `duplicates` group, closing issue #30.

**Architecture:** One service holding all thirteen calls, two command files over it (resolution verbs; detection and dismissals), following the shipped `FilesCommand` / `FilesFolderCommands` split over `FilesService`. Every response passes through unmodified.

**Tech Stack:** C# / .NET 10, `System.CommandLine`, Kiota-generated client (already generated — no regeneration needed), xUnit.

Design: [docs/specs/2026-09-08-duplicates-design.md](../specs/2026-09-08-duplicates-design.md).

## Global Constraints

- **Help text is transcribed from this plan, not composed.** Every command
  description, flag description, Notes line and Example is given verbatim below.
  Use them exactly. Do not add a line that is not in this plan. Where a flag is
  listed with no description string, it ships without one — except
  `OptionHelpers.Choice`, whose `description` parameter is not optional.
- **Do not state anything that does not belong to the command being written.** No
  narration of what a sibling command does, no framing ("useful when…"), no
  restating a flag's description in Notes, no explaining what is already visible
  in the flag list or the response-shape sample. If a fact is not in the given
  text, it was deliberately left out.
- **All thirteen commands are `require_admin`.** Every command calls
  `command.AddRoleRequired("admin")` immediately after construction, and every
  service call passes `permissionHint: "the admin role"`.
- Responses are emitted with `ConsoleOutput.WriteRawJson(result)`.
- Exit codes: `link` returns `BulkExit.CodeFor(...)`; `scan` returns
  `ScanExit.CodeFor(...)`; **every other command returns `0`**. In particular
  `merge-metadata` returns 0 — its `skipped` list is normal behaviour, not
  failure — and `cancel-scan` returns 0, matching `library cancel-scan`.
- `--server` is declared per-subcommand as
  `new Option<string?>("--server") { Description = "Server URL override" }` and
  threaded into `CommandHelper.BuildClient(serverOverride: …)`.
- Array-valued body and query fields take a repeatable flag named after the
  field, with `AllowMultipleArgumentsPerToken = true`, matching
  `files move --sources`. No comma-splitting anywhere.
- Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.
- No blank lines inside method bodies between consecutive `Subcommands.Add`
  calls, between consecutive variable declarations of the same kind, or before a
  `return` that follows setup calls.
- Never hand-edit `src/GrimoireCli/Generated/`. Nothing here needs it
  regenerated.
- Conventional Commits: `type: subject`, imperative, lowercase, no period. No
  `Co-Authored-By:` and no generated-with attribution.
- `CHANGELOG.md` is owned by the release process. Do not touch it.
- Work on the existing branch `feat/duplicates`. Never commit to `main`.

---

### Task 1: `DuplicatesService`

**Files:**
- Create: `src/GrimoireCli/Services/DuplicatesService.cs`
- Create: `tests/GrimoireCli.Tests/Services/DuplicatesServiceTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint = null, string? notFoundHint = null)`.
- Produces: the thirteen methods below. Tasks 2 and 3 call them and nothing else.

The generated accessors and property names below were read from the committed
client and are correct as written. Transcribe them; do not substitute.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Services/DuplicatesServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Thirteen near-identical sends is exactly where a copy-paste reaches the wrong
/// endpoint, so every path is pinned. The query names are pinned too: a client
/// regeneration that renamed one would leave the server ignoring the filter and
/// answering 200 with unfiltered data.
/// </summary>
public class DuplicatesServiceTests
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
        var api = Client().Api.Api.Duplicates;
        Assert.Equal("http://example.test/api/duplicates/link",
            Uri(api.Link.ToPostRequestInformation(new Generated.Models.LinkRequest())));
        Assert.Equal("http://example.test/api/duplicates/promote",
            Uri(api.Promote.ToPostRequestInformation(new Generated.Models.PromoteRequest())));
        Assert.Equal("http://example.test/api/duplicates/unlink",
            Uri(api.Unlink.ToPostRequestInformation(new Generated.Models.UnlinkRequest())));
        Assert.Equal("http://example.test/api/duplicates/merge-metadata",
            Uri(api.MergeMetadata.ToPostRequestInformation(new Generated.Models.MergeMetadataRequest())));
        Assert.Equal("http://example.test/api/duplicates/scan",
            Uri(api.Scan.ToPostRequestInformation(new Generated.Models.ScanRequest())));
        Assert.Equal("http://example.test/api/duplicates/cancel-scan",
            Uri(api.CancelScan.ToPostRequestInformation()));
        Assert.Equal("http://example.test/api/duplicates/scan-status",
            Uri(api.ScanStatus.ToGetRequestInformation()));
        Assert.Equal("http://example.test/api/duplicates/dismiss",
            Uri(api.Dismiss.ToPostRequestInformation(new Generated.Models.DismissRequest())));
    }

    [Fact]
    public void TheItemDeleteCarriesBothPathParameters()
    {
        var info = Client().Api.Api.Duplicates.Items["book"]["abc"]
            .ToDeleteRequestInformation(new Generated.Models.DeleteItemRequest());
        Assert.Equal("http://example.test/api/duplicates/items/book/abc", Uri(info));
    }

    [Fact]
    public void TheDismissalDeleteCarriesItsPathParameter()
    {
        var info = Client().Api.Api.Duplicates.Dismissals["d1"].ToDeleteRequestInformation();
        Assert.Equal("http://example.test/api/duplicates/dismissals/d1", Uri(info));
    }

    [Fact]
    public void CompareSendsResourceTypeAndRepeatedIds()
    {
        var info = Client().Api.Api.Duplicates.Compare.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = "book";
            c.QueryParameters.Ids = ["a", "b"];
        });
        var uri = Uri(info);
        Assert.Contains("resource_type=book", uri);
        Assert.Contains("ids=a", uri);
        Assert.Contains("ids=b", uri);
    }

    [Fact]
    public void GroupsSendsEveryQueryParameterByItsWireName()
    {
        var info = Client().Api.Api.Duplicates.Groups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = "book";
            c.QueryParameters.MinConfidence = 0.5;
            c.QueryParameters.Limit = 10;
            c.QueryParameters.Offset = 20;
        });
        var uri = Uri(info);
        Assert.Contains("resource_type=book", uri);
        Assert.Contains("min_confidence=0.5", uri);
        Assert.Contains("limit=10", uri);
        Assert.Contains("offset=20", uri);
    }

    [Fact]
    public void DismissalsSendsResourceType()
    {
        var info = Client().Api.Api.Duplicates.Dismissals.ToGetRequestInformation(c =>
            c.QueryParameters.ResourceType = "book");
        Assert.Contains("resource_type=book", Uri(info));
    }

    // reparent_to is a composed-type wrapper because it is Optional upstream.
    // Assigning through the wrapper only when the flag was given is what keeps
    // --reparent-to "" distinguishable from the flag being absent: "" promotes
    // every variant to standalone, absent means "refuse if there are any".
    [Fact]
    public void TheDeleteBodyOmitsReparentToUnlessGiven()
    {
        Assert.Null(GrimoireCli.Services.DuplicatesService
            .BuildDeleteItemBody(true, null).ReparentTo);
        var cleared = GrimoireCli.Services.DuplicatesService
            .BuildDeleteItemBody(true, "").ReparentTo;
        Assert.NotNull(cleared);
        Assert.Equal("", cleared!.String);
    }

    [Fact]
    public void TheUnlinkBodyOmitsParentIdUnlessGiven()
    {
        Assert.Null(GrimoireCli.Services.DuplicatesService
            .BuildUnlinkBody("book", ["a"], null).ParentId);
        var parent = GrimoireCli.Services.DuplicatesService
            .BuildUnlinkBody("book", [], "p1").ParentId;
        Assert.NotNull(parent);
        Assert.Equal("p1", parent!.String);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter DuplicatesServiceTests`
Expected: FAIL — `DuplicatesService` does not exist.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/DuplicatesService.cs`:

```csharp
using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The thirteen duplicate-resolution endpoints, every one require_admin
/// (routers/duplicates/__init__.py registers no router-level dependency; each
/// handler takes require_admin itself). resource_type and accuracy reach the
/// generated models as enums because both are Literal upstream, so each is
/// parsed from the flag's string here. Enum.Parse cannot throw on that string:
/// every caller declares the flag as OptionHelpers.Choice over the same set, so
/// an unaccepted value is a parse error before any service call is made.
/// </summary>
public class DuplicatesService
{
    private const string AdminHint = "the admin role";
    private const string NotFoundHint =
        "No such item. List the candidate groups with: grimoire-cli duplicates groups";

    private readonly GrimoireApiClient _client;

    public DuplicatesService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// POST /api/duplicates/link. The body is validated and sent unchanged, so
    /// Kiota's own deserializer never sees it and the resource_type enum needs no
    /// mapping here.
    /// </summary>
    public async Task<string> LinkAsync(string rawBody)
    {
        var info = _client.Api.Api.Duplicates.Link.ToPostRequestInformation(
            new Generated.Models.LinkRequest());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>POST /api/duplicates/promote. kind and label describe the old parent.</summary>
    public async Task<string> PromoteAsync(
        string resourceType, string newParentId, string oldParentId, string? kind, string? label)
    {
        var body = new Generated.Models.PromoteRequest
        {
            ResourceType = Enum.Parse<Generated.Models.PromoteRequest_resource_type>(resourceType, true),
            NewParentId = newParentId,
            OldParentId = oldParentId,
            Kind = kind,
            Label = label,
        };
        var info = _client.Api.Api.Duplicates.Promote.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/duplicates/unlink. parentId wins over ids server-side.</summary>
    public async Task<string> UnlinkAsync(string resourceType, string[] ids, string? parentId)
    {
        var info = _client.Api.Api.Duplicates.Unlink.ToPostRequestInformation(
            BuildUnlinkBody(resourceType, ids, parentId));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/duplicates/merge-metadata.</summary>
    public async Task<string> MergeMetadataAsync(
        string resourceType, string sourceId, string targetId, string[] fields, bool overwrite)
    {
        var body = new Generated.Models.MergeMetadataRequest
        {
            ResourceType = Enum.Parse<Generated.Models.MergeMetadataRequest_resource_type>(resourceType, true),
            SourceId = sourceId,
            TargetId = targetId,
            Fields = [.. fields],
            Overwrite = overwrite,
        };
        var info = _client.Api.Api.Duplicates.MergeMetadata.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// DELETE /api/duplicates/items/{resource_type}/{item_id}. deleteFile decides
    /// only whether the file leaves the disk; the row and its references go either
    /// way.
    /// </summary>
    public async Task<string> DeleteItemAsync(
        string resourceType, string itemId, bool deleteFile, string? reparentTo)
    {
        var info = _client.Api.Api.Duplicates.Items[resourceType][itemId]
            .ToDeleteRequestInformation(BuildDeleteItemBody(deleteFile, reparentTo));
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/duplicates/compare. Two to four ids, enforced server-side.</summary>
    public async Task<string> CompareAsync(string resourceType, string[] ids)
    {
        var info = _client.Api.Api.Duplicates.Compare.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = resourceType;
            c.QueryParameters.Ids = ids;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/duplicates/scan. 409 while a library scan is running.</summary>
    public async Task<string> ScanAsync(string[] resourceTypes, string? accuracy)
    {
        var body = new Generated.Models.ScanRequest();
        if (resourceTypes.Length > 0)
            body.ResourceTypes = [.. resourceTypes.Select(t =>
                (Generated.Models.ScanRequest_resource_types?)Enum.Parse<Generated.Models.ScanRequest_resource_types>(t, true))];
        if (accuracy is not null)
            body.Accuracy = Enum.Parse<Generated.Models.ScanRequest_accuracy>(accuracy, true);
        var info = _client.Api.Api.Duplicates.Scan.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>GET /api/duplicates/scan-status.</summary>
    public async Task<string> ScanStatusAsync()
    {
        var info = _client.Api.Api.Duplicates.ScanStatus.ToGetRequestInformation();
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>POST /api/duplicates/cancel-scan.</summary>
    public async Task<string> CancelScanAsync()
    {
        var info = _client.Api.Api.Duplicates.CancelScan.ToPostRequestInformation();
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>GET /api/duplicates/groups.</summary>
    public async Task<string> GroupsAsync(
        string? resourceType, double? minConfidence, int? limit, int? offset)
    {
        var info = _client.Api.Api.Duplicates.Groups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = resourceType;
            c.QueryParameters.MinConfidence = minConfidence;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>POST /api/duplicates/dismiss. At least two member ids, enforced server-side.</summary>
    public async Task<string> DismissAsync(string resourceType, string[] memberIds, string? note)
    {
        var body = new Generated.Models.DismissRequest
        {
            ResourceType = Enum.Parse<Generated.Models.DismissRequest_resource_type>(resourceType, true),
            MemberIds = [.. memberIds],
            Note = note,
        };
        var info = _client.Api.Api.Duplicates.Dismiss.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/duplicates/dismissals.</summary>
    public async Task<string> DismissalsAsync(string? resourceType)
    {
        var info = _client.Api.Api.Duplicates.Dismissals.ToGetRequestInformation(c =>
            c.QueryParameters.ResourceType = resourceType);
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }

    /// <summary>DELETE /api/duplicates/dismissals/{dismissal_id}.</summary>
    public async Task<string> UndismissAsync(string dismissalId)
    {
        var info = _client.Api.Api.Duplicates.Dismissals[dismissalId].ToDeleteRequestInformation();
        return await _client.SendAsync(info, permissionHint: AdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// parent_id is a composed-type wrapper because it is Optional upstream.
    /// Internal (not private) so a test can pin that a client regeneration cannot
    /// silently change it.
    /// </summary>
    internal static Generated.Models.UnlinkRequest BuildUnlinkBody(
        string resourceType, string[] ids, string? parentId)
    {
        var body = new Generated.Models.UnlinkRequest
        {
            ResourceType = Enum.Parse<Generated.Models.UnlinkRequest_resource_type>(resourceType, true),
            Ids = [.. ids],
        };
        if (parentId is not null)
            body.ParentId = new Generated.Models.UnlinkRequest.UnlinkRequest_parent_id { String = parentId };
        return body;
    }

    /// <summary>
    /// reparent_to is a composed-type wrapper; assigning it only when the flag was
    /// given is what keeps "" (promote every variant to standalone) distinct from
    /// omitted (refuse if the item has any).
    /// </summary>
    internal static Generated.Models.DeleteItemRequest BuildDeleteItemBody(
        bool deleteFile, string? reparentTo)
    {
        var body = new Generated.Models.DeleteItemRequest { DeleteFile = deleteFile };
        if (reparentTo is not null)
            body.ReparentTo = new Generated.Models.DeleteItemRequest.DeleteItemRequest_reparent_to { String = reparentTo };
        return body;
    }
}
```

If a generated property name differs from the above, read the builder or model
under `src/GrimoireCli/Generated/` and use what it declares — but do not change
the method signatures, which Tasks 2 and 3 depend on.

- [ ] **Step 4: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: build clean, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/DuplicatesService.cs tests/GrimoireCli.Tests/Services/DuplicatesServiceTests.cs
git commit -m "feat: add the duplicates service"
```

---

### Task 2: the resolution commands

**Files:**
- Create: `src/GrimoireCli/Commands/DuplicatesCommand.cs`
- Create: `tests/GrimoireCli.Tests/Commands/DuplicatesCommandTests.cs`
- Modify: `src/GrimoireCli/Program.cs` (registration)

**Interfaces:**
- Consumes: `DuplicatesService` from Task 1 — `LinkAsync(string rawBody)`, `PromoteAsync(string, string, string, string?, string?)`, `UnlinkAsync(string, string[], string?)`, `MergeMetadataAsync(string, string, string, string[], bool)`, `DeleteItemAsync(string, string, bool, string?)`, `CompareAsync(string, string[])`. Also `OptionHelpers.Choice(string name, string description, string[] allowed)`, `JsonBodyInput.Read/Validate/RequireExactlyOneSource`, `BulkExit.CodeFor(bool)` and `GrimoireApiClient.HasItems(string json, string property)` — `books batch-update` ends `return BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"));` (`BooksCommand.cs:192`), and `link` uses exactly that call. Both helpers are `internal`, which the test project already sees.
- Produces: `DuplicatesCommand.Create()` returning the `duplicates` command with its six resolution leaves, and `internal static readonly string[] ResourceTypes = ["book", "map", "token", "audio"]`. Task 3 adds seven more leaves to this same `Create()`, so leave it easy to extend — but do **not** reference `DuplicatesScanCommands` in this task; that type does not exist yet.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/DuplicatesCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class DuplicatesCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(DuplicatesCommand.Create(), path, full);

    [Theory]
    [InlineData("link")]
    [InlineData("promote")]
    [InlineData("unlink")]
    [InlineData("merge-metadata")]
    [InlineData("delete")]
    [InlineData("compare")]
    public void EveryResolutionCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["duplicates", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("link")]
    [InlineData("promote")]
    [InlineData("unlink")]
    [InlineData("merge-metadata")]
    [InlineData("delete")]
    [InlineData("compare")]
    public void EveryResolutionCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["duplicates", leaf], full: true));
    }

    [Fact]
    public void LinkRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["link"]).Errors);
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["link", "--stdin", "--input", "a.json"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["link", "--stdin"]).Errors);
    }

    // The generated models type resource_type as an enum, so the model cannot
    // hold anything else — the allowed set comes from the generator, not from a
    // hand-written mirror of server policy.
    [Theory]
    [InlineData("book")]
    [InlineData("map")]
    [InlineData("token")]
    [InlineData("audio")]
    public void CompareAcceptsEveryResourceType(string type)
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["compare", "--resource-type", type, "--ids", "a", "b"]).Errors);
    }

    [Fact]
    public void CompareRejectsAnUnknownResourceType()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["compare", "--resource-type", "spellbook", "--ids", "a", "b"]).Errors);
    }

    [Fact]
    public void CompareTakesRepeatableIds()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["compare", "--resource-type", "book", "--ids", "a", "--ids", "b"]).Errors);
    }

    // With neither flag the server answers 200 {"unlinked": []} — a silent
    // no-op, so the refusal has to happen here.
    [Fact]
    public void UnlinkRequiresIdsOrAParent()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["unlink", "--resource-type", "book"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["unlink", "--resource-type", "book", "--ids", "a"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["unlink", "--resource-type", "book", "--parent-id", "p"]).Errors);
    }

    // The server defaults delete_file to true and a bodyless call destroys the
    // file, while `files delete` spells the same intent and defaults to soft.
    // Requiring the value is what stops the two verbs being confused.
    [Fact]
    public void DeleteRequiresTheDeleteFileValue()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a", "--delete-file", "false"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a", "--delete-file", "true"]).Errors);
    }

    [Fact]
    public void DeleteTakesAnEmptyReparentTo()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["delete", "--resource-type", "book", "--id", "a", "--delete-file", "true", "--reparent-to", ""]).Errors);
    }

    [Fact]
    public void PromoteRequiresBothParents()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["promote", "--resource-type", "book", "--new-parent-id", "n"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["promote", "--resource-type", "book", "--new-parent-id", "n", "--old-parent-id", "o"]).Errors);
    }

    [Fact]
    public void MergeMetadataRequiresSourceTargetAndFields()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["merge-metadata", "--resource-type", "book", "--source-id", "s", "--target-id", "t"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["merge-metadata", "--resource-type", "book", "--source-id", "s", "--target-id", "t", "--fields", "title"]).Errors);
    }

    // Nothing exposes the copyable set: /compare declares response_model
    // CompareResult, which omits mergeable_fields.
    [Fact]
    public void MergeMetadataListsTheBookFields()
    {
        var output = Help(["duplicates", "merge-metadata"]);
        Assert.Contains("publisher_url", output);
        Assert.Contains("is_explicit", output);
    }

    // The kind vocabulary is closed and scoped by collection, and the server
    // reports a bad one per child rather than failing the request.
    [Fact]
    public void LinkListsTheScopedKindVocabulary()
    {
        var output = Help(["duplicates", "link"]);
        Assert.Contains("form-fillable", output);
        Assert.Contains("universal-vtt", output);
        Assert.Contains("color-variation", output);
        Assert.Contains("sped-up", output);
    }

    [Fact]
    public void DeleteDocumentsWhatItRemovesAndTheReparentRule()
    {
        var output = Help(["duplicates", "delete"]);
        Assert.Contains("bookmarks", output);
        Assert.Contains("--reparent-to", output);
        Assert.Contains("409", output);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter DuplicatesCommandTests`
Expected: FAIL — `DuplicatesCommand` does not exist.

- [ ] **Step 3: Write the command file**

Create `src/GrimoireCli/Commands/DuplicatesCommand.cs`. Use these strings **verbatim**.

Structure: a `Create()` that builds the group, adds the six resolution leaves,
then adds Task 3's seven leaves; a `static readonly string[] ResourceTypes =
["book", "map", "token", "audio"]` shared with Task 3 (declare it `internal` so
`DuplicatesScanCommands` can use it); and one private factory per leaf.

```csharp
var command = new Command("duplicates", "Find and resolve duplicate library items");
```

`link`:
```csharp
var command = new Command("link", "File items under a parent as its variants");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "At most 20 children. Each requires id and kind; label is optional.",
    "",
    "Per child: a rejected one lands in errors and the rest are linked, so a",
    "partial exits 3. Re-sending a fixed batch re-reports the children that",
    "already linked.",
    "",
    "A child cannot be its own parent, cannot be in another collection, and",
    "cannot already be a variant — variants are two levels deep, never three.",
    "",
    "kind is a closed set, scoped by collection. version and other apply to",
    "every collection; each adds its own:",
    "  book   printer-friendly, form-fillable, spreads, single-page,",
    "         black-and-white",
    "  map    printer-friendly, black-and-white, gridded, gridless,",
    "         universal-vtt, video, image",
    "  token  black-and-white, color-variation",
    "  audio  remix, slowed, sped-up",
    "",
    "label is free text, trimmed to 120 characters without warning.");
command.AddExamples(
    "grimoire-cli duplicates link --input link.json",
    "echo '{\"resource_type\":\"book\",\"parent_id\":\"<id>\",\"children\":[{\"id\":\"<id>\",\"kind\":\"printer-friendly\",\"label\":\"A4 print\"}]}' | grimoire-cli duplicates link --stdin");
command.AddRequestShape<Generated.Models.LinkRequest>();
command.AddResponseExample<Generated.Models.LinkResult>();
```
Its action reads the body with `JsonBodyInput.Read`, validates it with
`JsonBodyInput.Validate<Generated.Models.LinkRequest>(...)`, calls `LinkAsync`,
writes the JSON, and returns `BulkExit.CodeFor(GrimoireApiClient.HasItems(result, "errors"))` —
exactly as `books batch-update` does. Catch `BodyInputException`, log it,
and return 1, matching `books batch-update`.

`promote`:
```csharp
var command = new Command("promote", "Make a different copy the main version of a family");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "--kind and --label describe the old parent, which becomes a variant.",
    "",
    "One indivisible change: the old parent and every child move together.",
    "--new-parent-id must not already be a variant of something else, and",
    "--old-parent-id must not itself be a variant.");
command.AddExamples(
    "grimoire-cli duplicates promote --resource-type book --new-parent-id <id> --old-parent-id <id> --kind version --label \"2015 printing\"");
command.AddResponseExample<Generated.Models.PromoteResult>();
```
Flags: `--resource-type` (Choice over `ResourceTypes`, `"Collection to act on"`, Required),
`--new-parent-id` (`"Item to promote"`, Required), `--old-parent-id`
(`"Item to demote"`, Required), `--kind`
(`"The old parent's kind once demoted; default other"`), `--label`
(`"The old parent's label once demoted"`), `--server`.

`unlink`:
```csharp
var command = new Command("unlink", "Promote variants back to standalone entries");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "--parent-id frees every variant of that parent and wins if both are given.",
    "",
    "The files are untouched; only the parent link goes.");
command.AddExamples(
    "grimoire-cli duplicates unlink --resource-type book --ids <id> <id>",
    "grimoire-cli duplicates unlink --resource-type book --parent-id <id>");
command.AddResponseExample<Generated.Models.UnlinkResult>();
```
Flags: `--resource-type` (Choice, `"Collection to act on"`, Required), `--ids`
(`"Variants to free; repeatable"`, `AllowMultipleArgumentsPerToken = true`),
`--parent-id` (`"Free every variant of this parent"`), `--server`. Add a command
validator refusing when neither `--ids` nor `--parent-id` is given, with the
error `"Provide --ids or --parent-id."`

`merge-metadata`:
```csharp
var command = new Command("merge-metadata", "Copy metadata fields from one copy onto another");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "Without --overwrite a field is copied only where the target's is empty;",
    "everything else comes back in skipped, which is not an error. An empty",
    "source field is skipped either way. tags are always additive.",
    "",
    "--fields for a book: title, description, authors, artists, publisher,",
    "publisher_url, urls, genres, isbn, version, language, license, year, month,",
    "day, category, is_explicit, tags. map: description, map_type, grid_size,",
    "tags. token: description, is_explicit, tags. audio: description, title,",
    "artist, album, tags. Anything else is refused with the collection's set.");
command.AddExamples(
    "grimoire-cli duplicates merge-metadata --resource-type book --source-id <id> --target-id <id> --fields title description",
    "grimoire-cli duplicates merge-metadata --resource-type book --source-id <id> --target-id <id> --fields tags --overwrite");
command.AddResponseExample<Generated.Models.MergeMetadataResult>();
```
Flags: `--resource-type` (Choice, `"Collection to act on"`, Required),
`--source-id` (`"Item to copy from"`, Required), `--target-id`
(`"Item to copy onto"`, Required), `--fields` (`"Fields to copy; repeatable"`, Required,
`AllowMultipleArgumentsPerToken = true`), `--overwrite`
(`"Replace values already set on the target"`), `--server`. Returns `0`.

`delete`:
```csharp
var command = new Command("delete", "Delete one duplicate's record, and optionally its file");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "The record goes either way, with its bookmarks, favorites, tags, campaign",
    "links, indexed page text and thumbnail. --delete-file decides only whether",
    "the file leaves the disk, with its sidecars.",
    "",
    "--delete-file has no default here: files delete spells the same flag and",
    "defaults to keeping the file, so this one is stated every time.",
    "",
    "An item that has variants answers 409 unless --reparent-to is given: \"\"",
    "frees them all, an id names which of them inherits the rest.",
    "",
    "A record whose file is already gone still deletes, reporting",
    "file_deleted: false. A read-only library answers 409 and changes nothing.");
command.AddExamples(
    "grimoire-cli duplicates delete --resource-type book --id <id> --delete-file true",
    "grimoire-cli duplicates delete --resource-type book --id <id> --delete-file true --reparent-to \"\"");
command.AddResponseExample<Generated.Models.DeleteItemResult>();
```
Flags: `--resource-type` (Choice, `"Collection to act on"`, Required), `--id` (`"Item to delete"`,
Required), `--delete-file` (`Option<bool>`, **Required**,
`"Also delete the file from disk; irreversible"`), `--reparent-to`
(`"Which variant inherits the rest; \"\" frees them all"`), `--server`.

`compare`:
```csharp
var command = new Command("compare", "Compare two to four copies side by side");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "Two to four --ids; anything else is refused.",
    "",
    "reference_counts per item is the user work attached to that copy, and",
    "suggested_parent_id is the server's pick for which to keep.");
command.AddExamples(
    "grimoire-cli duplicates compare --resource-type book --ids <id> <id>");
command.AddResponseExample<Generated.Models.CompareResult>();
```
Flags: `--resource-type` (Choice, `"Collection to act on"`, Required), `--ids` (`"Items to compare;
repeatable"`, Required, `AllowMultipleArgumentsPerToken = true`), `--server`.

- [ ] **Step 4: Register the command**

In `src/GrimoireCli/Program.cs`, after the `TagsCommand` line:

```csharp
rootCommand.Subcommands.Add(DuplicatesCommand.Create());
```

- [ ] **Step 5: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: build clean, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/DuplicatesCommand.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/DuplicatesCommandTests.cs
git commit -m "feat: add the duplicate resolution commands"
```

---

### Task 3: the detection and dismissal commands

**Files:**
- Create: `src/GrimoireCli/Commands/DuplicatesScanCommands.cs`
- Modify: `src/GrimoireCli/Commands/DuplicatesCommand.cs` (register the seven leaves)
- Create: `tests/GrimoireCli.Tests/Commands/DuplicatesScanCommandTests.cs`

**Interfaces:**
- Consumes: `DuplicatesService` from Task 1 — `ScanAsync(string[], string?)`, `ScanStatusAsync()`, `CancelScanAsync()`, `GroupsAsync(string?, double?, int?, int?)`, `DismissAsync(string, string[], string?)`, `DismissalsAsync(string?)`, `UndismissAsync(string)`. Also `DuplicatesCommand.ResourceTypes` (the shared `internal static readonly string[]`), `OptionHelpers.Choice`, `OptionHelpers.Range`, `ScanExit.CodeFor(string?)`.
- Produces: `DuplicatesScanCommands.Create()` returning the seven leaves in order: `scan`, `scan-status`, `cancel-scan`, `groups`, `dismiss`, `dismissals`, `undismiss`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/DuplicatesScanCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class DuplicatesScanCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(DuplicatesCommand.Create(), path, full);

    [Fact]
    public void TheGroupHostsAllThirteenLeaves()
    {
        Assert.Equal(
            ["link", "promote", "unlink", "merge-metadata", "delete", "compare",
             "scan", "scan-status", "cancel-scan", "groups", "dismiss", "dismissals", "undismiss"],
            DuplicatesCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan-status")]
    [InlineData("cancel-scan")]
    [InlineData("groups")]
    [InlineData("dismiss")]
    [InlineData("dismissals")]
    [InlineData("undismiss")]
    public void EveryDetectionCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["duplicates", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan-status")]
    [InlineData("cancel-scan")]
    [InlineData("groups")]
    [InlineData("dismiss")]
    [InlineData("dismissals")]
    [InlineData("undismiss")]
    public void EveryDetectionCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["duplicates", leaf], full: true));
    }

    [Fact]
    public void ScanAndStatusAndCancelParseBare()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(["scan"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["scan-status"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["cancel-scan"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["groups"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["dismissals"]).Errors);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    public void ScanAcceptsEveryAccuracy(string accuracy)
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(["scan", "--accuracy", accuracy]).Errors);
    }

    [Fact]
    public void ScanRejectsAnUnknownAccuracy()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["scan", "--accuracy", "perfect"]).Errors);
    }

    [Fact]
    public void ScanTakesRepeatableResourceTypes()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["scan", "--resource-types", "book", "map"]).Errors);
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["scan", "--resource-types", "spellbook"]).Errors);
    }

    // The server declares limit as Query(50, le=200): the ceiling is guarded,
    // the floor is not, and a negative slices to an empty page at HTTP 200.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("201")]
    public void GroupsRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["groups", "--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("200")]
    public void GroupsAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(["groups", "--limit", limit]).Errors);
    }

    [Fact]
    public void GroupsReportsANonNumericLimitAsAParseError()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["groups", "--limit", "abc"]).Errors);
    }

    [Fact]
    public void DismissRequiresResourceTypeAndMemberIds()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["dismiss", "--resource-type", "book"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["dismiss", "--resource-type", "book", "--member-ids", "a", "b"]).Errors);
    }

    [Fact]
    public void UndismissRequiresAnId()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["undismiss"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["undismiss", "--id", "d1"]).Errors);
    }

    // A duplicate scan already in flight is a 200 the caller must not read as
    // "started", and a library scan running is a hard refusal instead.
    [Fact]
    public void ScanDocumentsBothConflictPaths()
    {
        var output = Help(["duplicates", "scan"]);
        Assert.Contains("already_running", output);
        Assert.Contains("409", output);
        Assert.Contains("exit 3", output);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter DuplicatesScanCommandTests`
Expected: FAIL — `Create()` returns no leaves.

- [ ] **Step 3: Write the seven leaves**

Create `src/GrimoireCli/Commands/DuplicatesScanCommands.cs`:

```csharp
using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>
/// The detection and dismissal leaves of the `duplicates` group. Split from
/// DuplicatesCommand for the same reason FilesFolderCommands is split from
/// FilesCommand: thirteen leaves in one file would be the largest command file
/// in the repo by half.
/// </summary>
public static class DuplicatesScanCommands
{
    public static IEnumerable<Command> Create() => [ /* the seven below */ ];
}
```

Then in `src/GrimoireCli/Commands/DuplicatesCommand.cs`, register them at the end
of `Create()`, immediately before the `return`:

```csharp
foreach (var leaf in DuplicatesScanCommands.Create())
    command.Subcommands.Add(leaf);
```

Fill `Create()` with
`[CreateScanCommand(), CreateScanStatusCommand(), CreateCancelScanCommand(), CreateGroupsCommand(), CreateDismissCommand(), CreateDismissalsCommand(), CreateUndismissCommand()]`
and add one private factory each. Use these strings **verbatim**.

`scan`:
```csharp
var command = new Command("scan", "Start a duplicate-detection pass");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "Runs in the background; poll scan-status.",
    "",
    "409 while a library scan is running. A duplicate scan already in flight",
    "answers 200 with already_running and exit 3, having started nothing.",
    "",
    "--accuracy trades certainty for reach: exact matches only byte-identical",
    "files and never guesses; the looser levels take longer and return matches",
    "that need judging.",
    "",
    "Omitting --resource-types scans all four.");
command.AddExamples(
    "grimoire-cli duplicates scan",
    "grimoire-cli duplicates scan --resource-types book --accuracy exact");
command.AddResponseExample<Generated.Models.ScanTriggerResult>();
```
Flags: `--resource-types` (Choice over `DuplicatesCommand.ResourceTypes` is not
usable for an array, so declare
`new Option<string[]>("--resource-types") { Description = "Collections to scan; repeatable", AllowMultipleArgumentsPerToken = true }`
and add a command validator rejecting any value outside
`DuplicatesCommand.ResourceTypes` with
`$"'{value}' is not a valid value for --resource-types. Must be one of: book, map, token, audio"`),
`--accuracy` (`OptionHelpers.Choice("--accuracy", "Detection accuracy; default medium", ["exact", "high", "medium", "low"])`),
`--server`. Returns `ScanExit.CodeFor(GrimoireApiClient.ReadStringProperty(result, "status"))`.

`scan-status`:
```csharp
var command = new Command("scan-status", "Show the duplicate scan's progress");
command.AddExamples("grimoire-cli duplicates scan-status");
command.AddResponseExample<Generated.Models.ScanStatus>();
```
Flags: `--server` only. No Notes. Returns `0`.

`cancel-scan`:
```csharp
var command = new Command("cancel-scan", "Stop the running duplicate scan");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "Requests a stop rather than waiting for one; poll scan-status. Reports",
    "not_running and exits 0 when no scan is in flight.");
command.AddExamples("grimoire-cli duplicates cancel-scan");
command.AddResponseExample<Generated.Models.ScanTriggerResult>();
```
Flags: `--server` only. Returns `0`.

`groups`:
```csharp
var command = new Command("groups", "List candidate duplicate groups from the last scan");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "Groups whose members were deleted or already resolved are dropped, so a",
    "short page is not the end of the listing — page with --offset.",
    "",
    "suggested_kind and suggested_label per member are the server's guess, and",
    "are what link would take as-is.");
command.AddExamples(
    "grimoire-cli duplicates groups",
    "grimoire-cli duplicates groups --resource-type book --min-confidence 0.8 --limit 20");
command.AddResponseExample<Generated.Models.GroupListResponse>();
```
Flags: `--resource-type` (Choice over `DuplicatesCommand.ResourceTypes`, not
required), `--min-confidence` (`Option<double?>`, `"Drop groups below this score"`),
`--limit` (`OptionHelpers.Range("--limit", "Groups to return; default 50, max 200", 1, 200)`),
`--offset` (`Option<int?>`, `"Groups to skip"`), `--server`. Returns `0`.

`dismiss`:
```csharp
var command = new Command("dismiss", "Mark a group as not duplicates");
command.AddHelpSection("Notes", HelpSectionPosition.Top,
    "At least two --member-ids. Reversible with undismiss.");
command.AddExamples(
    "grimoire-cli duplicates dismiss --resource-type book --member-ids <id> <id> --note \"different editions\"");
command.AddResponseExample<Generated.Models.DismissalOut>();
```
Flags: `--resource-type` (Choice, Required), `--member-ids` (`"Items that are not duplicates of each other; repeatable"`, Required, `AllowMultipleArgumentsPerToken = true`), `--note` (`"Why they are not duplicates"`), `--server`. Returns `0`.

`dismissals`:
```csharp
var command = new Command("dismissals", "List dismissed groups");
command.AddExamples("grimoire-cli duplicates dismissals");
command.AddResponseExample<Generated.Models.DismissalListResponse>();
```
Flags: `--resource-type` (Choice, not required), `--server`. No Notes. Returns `0`.

`undismiss`:
```csharp
var command = new Command("undismiss", "Undo a dismissal, so the group can be found again");
command.AddExamples("grimoire-cli duplicates undismiss --id <dismissal-id>");
command.AddResponseExample<Generated.Models.ScanTriggerResult>();
```
Flags: `--id` (`"Dismissal to undo, from dismissals"`, Required), `--server`. No Notes. Returns `0`.

- [ ] **Step 4: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: build clean, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/DuplicatesScanCommands.cs src/GrimoireCli/Commands/DuplicatesCommand.cs tests/GrimoireCli.Tests/Commands/DuplicatesScanCommandTests.cs
git commit -m "feat: add the duplicate detection and dismissal commands"
```

---

### Task 4: Smoke-test coverage

**Files:**
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: the thirteen commands from Tasks 1-3.

The stack is already running and seeded at `http://host.docker.internal:9481`
(NOT localhost). Verify with `docker compose -f docker/docker-compose.yml ps`.
**Do not start, stop, tear down or reseed it.**

- [ ] **Step 1: Add the block**

Append a new block at the end of the script, before any final summary line
(check the tail with `tail -20 docker/smoke-test.sh` and place it so the script's
closing lines still run last).

The block must be **idempotent**: it does one reversible write (link, then unlink
the same child) and nothing else. `delete` is deliberately never exercised — it
is irreversible and no fixture item's loss could be undone by a re-run.

```sh
# --- duplicates -------------------------------------------------------------
# Read-only except for one link/unlink round trip on two fixture books, which
# returns the fixture to its prior state, so a re-run converges. `delete` is
# never exercised here: it is irreversible.
DUP_JSON=$("$CLI" duplicates scan-status 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates scan-status exited non-zero"; }
echo "$DUP_JSON" | jq -e 'has("running")' >/dev/null \
  || fail "scan-status should report running: $DUP_JSON"
ok "duplicates scan-status reports the detection state"

DUP_JSON=$("$CLI" duplicates cancel-scan 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates cancel-scan exited non-zero"; }
[ "$(echo "$DUP_JSON" | jq -r .status)" = "not_running" ] \
  || fail "cancel-scan should report not_running on an idle stack: $DUP_JSON"
ok "duplicates cancel-scan reports an idle scanner"

DUP_JSON=$("$CLI" duplicates groups 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates groups exited non-zero"; }
echo "$DUP_JSON" | jq -e 'has("groups")' >/dev/null \
  || fail "groups should return a listing: $DUP_JSON"
ok "duplicates groups lists candidate groups"

# The server clamps nothing here: le=200 guards the ceiling, so the floor is
# ours to refuse.
"$CLI" duplicates groups --limit 0 >/dev/null 2>&1 \
  && fail "--limit 0 should be rejected before the request"
"$CLI" duplicates groups --limit -1 >/dev/null 2>&1 \
  && fail "--limit -1 should be rejected before the request"
ok "duplicates groups refuses a limit the server would not"

DUP_JSON=$("$CLI" duplicates dismissals 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates dismissals exited non-zero"; }
echo "$DUP_JSON" | jq -e 'has("dismissals")' >/dev/null \
  || fail "dismissals should return a listing: $DUP_JSON"
ok "duplicates dismissals lists dismissed groups"

# Two fixture books, chosen by title so the pair is stable across runs.
booklist
DUP_PARENT=$(echo "$LIST_JSON" | jq -r '.books[] | select(.title == "DSA5 Regelwerk") | .id')
DUP_CHILD=$(echo "$LIST_JSON" | jq -r '.books[] | select(.title == "DSA5 Errata") | .id')
[ -n "$DUP_PARENT" ] && [ -n "$DUP_CHILD" ] \
  || fail "the DSA5 fixture books are needed for the variant round trip"

DUP_JSON=$("$CLI" duplicates compare --resource-type book --ids "$DUP_PARENT" "$DUP_CHILD" \
  2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates compare exited non-zero"; }
[ "$(echo "$DUP_JSON" | jq '.items | length')" -eq 2 ] \
  || fail "compare should return both items: $DUP_JSON"
ok "duplicates compare returns two copies side by side"

"$CLI" duplicates compare --resource-type book --ids "$DUP_PARENT" >/dev/null 2>&1 \
  && fail "compare should refuse a single id"
ok "duplicates compare refuses fewer than two items"

# A bogus child is reported per-child, so the request succeeds and exits 3.
printf '{"resource_type":"book","parent_id":"%s","children":[{"id":"no-such-id","kind":"other","label":""}]}' \
  "$DUP_PARENT" >"$WORK/dup-bad.json"
set +e
"$CLI" duplicates link --input "$WORK/dup-bad.json" >"$WORK/dup-bad.out" 2>"$WORK/cli.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "a rejected child should exit 3, got $rc: $(cat "$WORK/cli.err")"
jq -e '.errors | length == 1 and .[0].id == "no-such-id"' "$WORK/dup-bad.out" >/dev/null \
  || fail "the bogus child should be the only error: $(cat "$WORK/dup-bad.out")"
ok "duplicates link reports a rejected child and exits 3"

printf '{"resource_type":"book","parent_id":"%s","children":[{"id":"%s","kind":"version","label":"smoke variant"}]}' \
  "$DUP_PARENT" "$DUP_CHILD" >"$WORK/dup-link.json"
DUP_JSON=$("$CLI" duplicates link --input "$WORK/dup-link.json" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates link exited non-zero"; }
echo "$DUP_JSON" | jq -e --arg id "$DUP_CHILD" '.linked | index($id) != null' >/dev/null \
  || fail "link should name the child it linked: $DUP_JSON"
[ "$(echo "$DUP_JSON" | jq '.errors | length')" -eq 0 ] \
  || fail "link should report no errors: $DUP_JSON"
ok "duplicates link files a book under a parent as its variant"

DUP_JSON=$("$CLI" duplicates unlink --resource-type book --ids "$DUP_CHILD" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates unlink exited non-zero"; }
echo "$DUP_JSON" | jq -e --arg id "$DUP_CHILD" '.unlinked | index($id) != null' >/dev/null \
  || fail "unlink should free the child again: $DUP_JSON"
ok "duplicates unlink promotes the variant back to standalone"

# The refusal is the CLI's: with neither flag the server answers 200 {"unlinked":[]}.
"$CLI" duplicates unlink --resource-type book >/dev/null 2>&1 \
  && fail "unlink with neither --ids nor --parent-id should be refused"
ok "duplicates unlink refuses a call the server would answer as a no-op"
```

`booklist` and `LIST_JSON` are the script's existing helper and variable for a
book listing — check what the script actually calls (`grep -n 'booklist\|LIST_JSON' docker/smoke-test.sh`)
and use the real names. If no book-listing helper exists, call
`"$CLI" books list --limit 500` into `LIST_JSON` the way the neighbouring blocks
fetch what they need.

**A leftover link would break the next run**: the fixture's `variant_count` and
the systems/books listings would differ, and `link` on an already-linked child
errors. The block unlinks what it links, immediately after asserting. If a
failure between the two could leave the link in place, add a trap in the same
shape as the files block's (`grep -n "trap '" docker/smoke-test.sh`) that unlinks
`$DUP_CHILD` on exit.

- [ ] **Step 2: Run the smoke test**

```bash
bash docker/smoke-test.sh
```
Expected: exit 0. Then count the checks and report the number the command prints:
```bash
bash docker/smoke-test.sh 2>&1 | grep -c 'ok:'
```
The baseline before this task is **119**. Report the measured number — do not
compute it by adding.

- [ ] **Step 3: Run it a second time**

```bash
bash docker/smoke-test.sh
```
Expected: exit 0 again with the same count. A block that only converges on the
first run is a defect. Diff the two runs' output if the counts differ.

- [ ] **Step 4: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the duplicates commands in the smoke test"
```

---

### Task 5: Documentation

**Files:**
- Modify: `tools/generate-api-coverage.py`
- Modify: `docs/grimoire-api-coverage.md` (regenerated, never hand-edited)
- Modify: `README.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Add the thirteen routes to the coverage generator**

In `tools/generate-api-coverage.py`, add to the `IMPLEMENTED` dict:

```python
    "POST /api/duplicates/link": "`duplicates link` ✅",
    "POST /api/duplicates/promote": "`duplicates promote` ✅",
    "POST /api/duplicates/unlink": "`duplicates unlink` ✅",
    "POST /api/duplicates/merge-metadata": "`duplicates merge-metadata` ✅",
    "DELETE /api/duplicates/items/{resource_type}/{item_id}": "`duplicates delete` ✅",
    "GET /api/duplicates/compare": "`duplicates compare` ✅",
    "POST /api/duplicates/scan": "`duplicates scan` ✅",
    "GET /api/duplicates/scan-status": "`duplicates scan-status` ✅",
    "POST /api/duplicates/cancel-scan": "`duplicates cancel-scan` ✅",
    "GET /api/duplicates/groups": "`duplicates groups` ✅",
    "POST /api/duplicates/dismiss": "`duplicates dismiss` ✅",
    "GET /api/duplicates/dismissals": "`duplicates dismissals` ✅",
    "DELETE /api/duplicates/dismissals/{dismissal_id}": "`duplicates undismiss` ✅",
}
```
(without the trailing brace — place them beside the other routes.)

- [ ] **Step 2: Regenerate the coverage table**

The generator reads the spec from the running stack, not a file. Read the top of
`tools/generate-api-coverage.py` to see how it resolves the address before
running it.

```bash
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```
Expected: exactly thirteen rows change from `—` to a command name, the
`duplicates` tag count moves to 13/13, and the total moves by thirteen. **If any
row did not change, the `IMPLEMENTED` key does not match the key the generator
builds — fix the key, never the markdown.**

- [ ] **Step 3: Add the README rows**

Thirteen rows in the Commands table, matching the neighbouring rows' format
including how they escape `|` inside `[a\|b]` alternations:

```
| `duplicates link {--input <file> \| --stdin}` | File items under a parent as its variants; exit 3 if partial (admin) |
| `duplicates promote --resource-type <t> --new-parent-id <id> --old-parent-id <id> [--kind <k>] [--label <l>]` | Make a different copy the main version of a family (admin) |
| `duplicates unlink --resource-type <t> (--ids <id>... \| --parent-id <id>)` | Promote variants back to standalone entries (admin) |
| `duplicates merge-metadata --resource-type <t> --source-id <id> --target-id <id> --fields <f>... [--overwrite]` | Copy metadata fields from one copy onto another (admin) |
| `duplicates delete --resource-type <t> --id <id> --delete-file true\|false [--reparent-to <id>]` | Delete one duplicate's record, and optionally its file (admin) |
| `duplicates compare --resource-type <t> --ids <id>...` | Compare two to four copies side by side (admin) |
| `duplicates scan [--resource-types <t>...] [--accuracy exact\|high\|medium\|low]` | Start a duplicate-detection pass; exit 3 if already running (admin) |
| `duplicates scan-status` | Show the duplicate scan's progress (admin) |
| `duplicates cancel-scan` | Stop the running duplicate scan (admin) |
| `duplicates groups [--resource-type <t>] [--min-confidence <n>] [--limit <1-200>] [--offset <n>]` | List candidate duplicate groups from the last scan (admin) |
| `duplicates dismiss --resource-type <t> --member-ids <id>... [--note <text>]` | Mark a group as not duplicates (admin) |
| `duplicates dismissals [--resource-type <t>]` | List dismissed groups (admin) |
| `duplicates undismiss --id <id>` | Undo a dismissal (admin) |
```

- [ ] **Step 4: Record the verified behaviour in the API notes**

Append a `## Duplicates` section to `docs/grimoire-api-notes.md`. **Match the
file's house style** — read the `## Backups` section first: every section opens
with a provenance line naming its sources and tag, and formats each finding as a
`- **bold lead.** …` list item.

Provenance: read from `backend/routers/duplicates/__init__.py`, `core.py`,
`detection.py`, `_helpers.py`, `backend/models/variants.py`,
`backend/services/variants.py` and `backend/services/library_fs/deletes.py` at
tag `v1.6.1`, and verified against the running 1.6.1 stack.

The findings to record, each as one bullet:

- **`DELETE /items/{resource_type}/{item_id}` deletes the file by default.**
  `DeleteItemRequest.delete_file` is `True`, and an omitted body becomes
  `DeleteItemRequest()`, so a bodyless call removes the file. That is the inverse
  of `POST /api/files/delete`, which is soft by default — and deliberate:
  `delete_file: false` keeps the file, which the next library scan re-indexes,
  putting the duplicate back. The CLI requires the value rather than defaulting
  it either way.
- **What `delete` removes does not depend on the flag.** The row goes either way,
  and `purge_references` takes the `book_search` page rows, bookmarks, favorites,
  tags, and campaign resource links with their shares; `_purge_derived` drops the
  thumbnail and page cache. `--delete-file` decides only whether the file leaves
  the disk, with its sidecars. `ENOENT` is tolerated, so a record whose file is
  already gone still deletes and reports `file_deleted: false`; `EROFS` is a 409
  with nothing committed.
- **`/link` validates per child, not per request.** `validate_kind` and every
  structural guard run inside the loop, so a bad id, a repeated id or an
  unaccepted kind lands in `errors` while the remaining children commit at
  HTTP 200 — the exit-3 contract. Capped at 20 children.
- **`mergeable_fields` never reaches the client.** `/compare` declares
  `response_model=CompareResult`, which omits it; the `CompareResponse` model
  that carries it is unused by the route. Verified: a compare response holds only
  `differences`, `items`, `page_count_min`, `resource_type` and
  `suggested_parent_id`. So nothing exposes the copyable set, and the only way to
  learn it is the 400 from `/merge-metadata`, which lists it.
- **`/unlink` with neither `ids` nor `parent_id` is a silent no-op**, answering
  `200 {"unlinked": []}`. `parent_id` wins when both are given.
- **`variant_label` is trimmed to 120 characters silently**
  (`services/variants.py:215`), on both `/link` and `/promote`.
- **`/groups` has no lower bound on `limit`.** Declared `Query(50, le=200)`; a
  negative value slices to an empty page and answers 200.
- **`/scan` has two conflict paths.** A *library* scan in flight is a 409; a
  *duplicate* scan in flight is `200 {"status": "already_running"}` with nothing
  started. `/cancel-scan` answers `not_running` or `stop_requested`, both 200.
- **`resource_type` is a body or query field on twelve of the thirteen routes** —
  only the item delete carries it in the path. So one command group covers books,
  maps, tokens and audio without those command groups existing.

- [ ] **Step 5: Update the roadmap**

Delete the whole **Duplicate handling** item from `## Next`, including its bullet
lists and the closing "The verbs carry `resource_type` in their paths…"
paragraph. The roadmap lists intended work only; a shipped item leaves. **Do not
add any line recording that it shipped.** Leave every other item byte-identical,
and check the section still reads coherently afterwards (the item is the first
under `## Next`).

- [ ] **Step 6: Verify nothing else drifted**

```bash
git diff --stat
```
Expected: exactly the five files above.

- [ ] **Step 7: Commit**

```bash
git add tools/generate-api-coverage.py docs/grimoire-api-coverage.md README.md docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record the duplicates commands and their verified behaviour"
```

---

## Final verification

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```
