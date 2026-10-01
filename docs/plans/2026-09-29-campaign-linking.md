# Campaign Linking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `campaigns` (list/get/create/update) with `resources`, `categories` and `files upload` subgroups — the 17-endpoint linking slice of #51.

**Architecture:** Four command files and four services, one per group, each command a thin pass-through to one endpoint via the generated Kiota builders. Raw JSON out. Spec: `docs/specs/2026-09-29-campaign-linking-design.md` — read its "Verified server behaviour" section before writing help text.

**Tech Stack:** .NET 10, System.CommandLine, Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit.

## Global Constraints

- Branch: `feat/campaign-linking`. Commits: Conventional Commits, lowercase imperative subject, **no `Co-Authored-By` or "Generated with" lines**.
- **No command gets `AddRoleRequired`** and no service call passes `permissionHint`: campaign writes are owner-gated, not role-gated (`_helpers.py:304-317`).
- Ids are flags: `--id` is always the campaign id.
- Help text is terse (calibrate against `src/GrimoireCli/Commands/SystemsCommand.cs`); a `ChoiceOption` description must not list its values.
- No blank lines between consecutive option declarations or `Subcommands.Add` calls; run `dotnet format GrimoireCli.sln` after every C# edit.
- Every write command carries this Notes block first (exact text):
  ```
  "Owner only, no admin override. 403 also when the owner's campaign",
  "access is disabled or the campaign is archived.",
  ```
  Declare it per file as `private static readonly string[] OwnerOnly = [ ... ];` and pass it with `command.AddHelpSection("Notes", HelpSectionPosition.Top, [.. OwnerOnly, "", ...more lines])`, or alone when there are no more lines.
- Deletes answer 204: their Notes end with `"Answers 204: stdout carries no body."` and they register no response example.
- Build: `dotnet build GrimoireCli.sln`. Tests: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`.

---

### Task 1: `campaigns` group — list, get, create, update

**Files:**
- Create: `src/GrimoireCli/Commands/CampaignsCommand.cs`, `src/GrimoireCli/Services/CampaignsService.cs`, `tests/GrimoireCli.Tests/Commands/CampaignsCommandTests.cs`
- Modify: `src/GrimoireCli/Program.cs` (add `rootCommand.Subcommands.Add(CampaignsCommand.Create());` after the `DuplicatesCommand` line)

**Interfaces:**
- Produces: `CampaignsCommand.Create()` — later tasks add `CampaignResourcesCommands.Create()`, `CampaignCategoriesCommands.Create()`, `CampaignFilesCommands.Create()` to its subcommands. Until those exist, do not reference them.

- [ ] **Step 1: Write failing tests** in `CampaignsCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignsCommandTests
{
    private static string RenderHelp(string[] path, bool full = false) =>
        HelpRenderer.Render(CampaignsCommand.Create(), path, full);

    [Fact]
    public void NoCampaignCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "get", "create", "update" })
            Assert.DoesNotContain("Role required:", RenderHelp(["campaigns", verb]));
    }

    [Fact]
    public void UpdateCarriesTheOwnerCaveat()
        => Assert.Contains("Owner only, no admin override", RenderHelp(["campaigns", "update"]));

    [Fact]
    public void CreateSaysGmCampaignsNeedARole()
        => Assert.Contains("is_gm_campaign", RenderHelp(["campaigns", "create"]));

    [Fact]
    public void ListSaysArchivedAreHidden()
        => Assert.Contains("--include-archived", RenderHelp(["campaigns", "list"]));

    [Fact]
    public void UpdateRequiresId()
        => Assert.NotEmpty(CampaignsCommand.Create().Parse(["update", "--stdin"]).Errors);
}
```

- [ ] **Step 2:** Run `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter CampaignsCommandTests` — expect a compile failure (`CampaignsCommand` missing).

- [ ] **Step 3: Write the service** `CampaignsService.cs`:

```csharp
using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// Campaign records. Writes are gated by ownership, not role
/// (routers/campaigns/_helpers.py:304-317), so no call carries a permission hint.
/// </summary>
public class CampaignsService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns. Owned and joined; archived only when asked.</summary>
    public async Task<string> ListAsync(bool includeArchived)
    {
        var info = _client.Api.Api.Campaigns.ToGetRequestInformation(c =>
            // Sent only when true: the server default is false.
            c.QueryParameters.IncludeArchived = includeArchived ? true : null);
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/campaigns/{id}.</summary>
    public async Task<string> GetAsync(string id)
        => await _client.SendAsync(_client.Api.Api.Campaigns[id].ToGetRequestInformation(), notFoundHint: NotFound);

    /// <summary>
    /// POST /api/campaigns. The validated raw body replaces the generated model's
    /// content so it reaches the server byte-for-byte.
    /// </summary>
    public async Task<string> CreateAsync(string rawBody)
    {
        var info = _client.Api.Api.Campaigns.ToPostRequestInformation(new Generated.Models.CampaignCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/campaigns/{id}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[id].ToPatchRequestInformation(new Generated.Models.CampaignUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, notFoundHint: NotFound);
    }
}
```

- [ ] **Step 4: Write the command** `CampaignsCommand.cs`. Follow `SystemsCommand.cs`'s shape exactly (logger field, `CommandHelper.BuildClient()`, `ConsoleOutput.WriteRawJson`, `JsonBodyInput.RequireExactlyOneSource`/`Read`/`Validate` with `BodyInputException` → exit 1).

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] OwnerOnly =
    [
        "Owner only, no admin override. 403 also when the owner's campaign",
        "access is disabled or the campaign is archived.",
    ];

    public static Command Create()
    {
        var command = new Command("campaigns", "Campaigns and the library items linked into them");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var archivedOption = new Option<bool>("--include-archived") { Description = "Also list archived campaigns" };
        var command = new Command("list", "List campaigns you own or have joined") { archivedOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Archived campaigns are hidden unless --include-archived; they are",
            "read-only, even for the owner.");
        command.AddExamples("grimoire-cli campaigns list", "grimoire-cli campaigns list --include-archived");
        command.AddResponseExampleArray<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).ListAsync(parseResult.GetValue(archivedOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var command = new Command("get", "Get one campaign") { idOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, "Owner or accepted member only.");
        command.AddExamples("grimoire-cli campaigns get --id <campaign-id>");
        command.AddResponseExample<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).GetAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateCreateCommand()
    {
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Create a campaign you own") { inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Only name is required. The caller becomes the owner, and only the",
            "owner can write to it afterwards.",
            "",
            "is_gm_campaign true needs the gm or admin role; guests cannot create.",
            "",
            "resources links items at creation, as resources bulk does.");
        command.AddExamples(
            "grimoire-cli campaigns create --input campaign.json",
            "echo '{\"name\":\"Curse of Strahd\"}' | grimoire-cli campaigns create --stdin");
        command.AddRequestShape<Generated.Models.CampaignCreate>();
        command.AddResponseExample<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.CampaignCreate.CreateFromDiscriminatorValue, "it is assigned by the server");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).CreateAsync(body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Campaign ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update a campaign's details") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. OwnerOnly, "", "system_name \"\" clears it."]);
        command.AddExamples("echo '{\"description\":\"Session zero\"}' | grimoire-cli campaigns update --id <id> --stdin");
        command.AddRequestShape<Generated.Models.CampaignUpdate>();
        command.AddResponseExample<Generated.Models.CampaignOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.CampaignUpdate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignsService(client).UpdateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }
}
```

Check `AddHelpSection`'s signature in `HelpExtensions.cs`; if it is `params string[]`, the `[.. OwnerOnly, ...]` spread compiles as a collection expression — if not, build the array first. Check `JsonBodyInput.Validate`'s `idHint` meaning (read `JsonBodyInput.cs:95`) and adjust the hint strings so they read correctly when the body contains `id`.

- [ ] **Step 5:** Register in `Program.cs`. Run `dotnet format GrimoireCli.sln`, then the filtered tests — expect PASS. Run `dotnet run --project src/GrimoireCli -- campaigns create --help` and read the rendered help.
- [ ] **Step 6:** Run the full test suite (the root-help and drift tests may assert on the command list; update them if they enumerate commands). Commit:
  `git add -A src tests && git commit -m "feat: add campaigns list, get, create and update"`

---

### Task 2: `campaigns resources`

**Files:**
- Create: `src/GrimoireCli/Commands/CampaignResourcesCommands.cs`, `src/GrimoireCli/Services/CampaignResourcesService.cs`, `tests/GrimoireCli.Tests/Commands/CampaignResourcesCommandTests.cs`, `tests/GrimoireCli.Tests/Services/CampaignResourcesServiceTests.cs`
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs` (add `command.Subcommands.Add(CampaignResourcesCommands.Create());` after the update subcommand)

**Interfaces:**
- Produces: `CampaignResourcesCommands.Create()` → `Command("resources")`; `internal static bool CampaignResourcesCommands.SkippedAny(string body, string response)`; `internal static Generated.Models.ResourceUpdate CampaignResourcesService.BuildUpdateBody(string? visibility, string? categoryId)`.

- [ ] **Step 1: Write failing tests.**

`CampaignResourcesServiceTests.cs`:
```csharp
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignResourcesServiceTests
{
    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = CampaignResourcesService.BuildUpdateBody(visibility: null, categoryId: null);
        Assert.Null(body.Visibility);
        Assert.Null(body.CategoryId);
    }

    // "" is the server's sentinel for "back to the built-in type group".
    [Fact]
    public void EmptyCategoryIdIsSentToClear()
        => Assert.Equal("", CampaignResourcesService.BuildUpdateBody(null, "").CategoryId!.String);

    [Fact]
    public void GivenVisibilityReachesTheBody()
        => Assert.Equal("public", CampaignResourcesService.BuildUpdateBody("public", null).Visibility!.String);
}
```

`CampaignResourcesCommandTests.cs`:
```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignResourcesCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "resources", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["resources", .. args]);

    [Theory]
    [InlineData("{\"resources\":[{},{}]}", "[{}]", true)]
    [InlineData("{\"resources\":[{},{}]}", "[{},{}]", false)]
    [InlineData("{\"resources\":[]}", "[]", false)]
    public void SkippedAnyComparesSentWithCreated(string body, string response, bool expected)
        => Assert.Equal(expected, CampaignResourcesCommands.SkippedAny(body, response));

    [Fact]
    public void AddRejectsFileAsAResourceType()
        => Assert.NotEmpty(Parse("add", "--id", "c", "--resource-type", "file", "--resource-id", "x").Errors);

    [Fact]
    public void AddRejectsAnUnknownVisibility()
        => Assert.NotEmpty(Parse("add", "--id", "c", "--resource-type", "book", "--resource-id", "x", "--visibility", "players").Errors);

    [Fact]
    public void AddAcceptsAModel()
        => Assert.Empty(Parse("add", "--id", "c", "--resource-type", "model", "--resource-id", "x").Errors);

    [Fact]
    public void UpdateNeedsSomethingToChange()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--link-id", "l").Errors);

    [Fact]
    public void NoResourcesCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "add", "bulk", "update", "remove", "reorder" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void BulkDocumentsExitThree() => Assert.Contains("Exit 3", RenderHelp("bulk"));

    [Fact]
    public void RemoveWarnsThatAFileLinkDeletesTheUpload() => Assert.Contains("deletes the upload", RenderHelp("remove"));

    [Fact]
    public void ReorderSaysToPassEveryId() => Assert.Contains("every link id", RenderHelp("reorder"));
}
```

- [ ] **Step 2:** Run the two filtered test classes — expect compile failure.

- [ ] **Step 3: Write the service** `CampaignResourcesService.cs`:

```csharp
using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// A campaign's resource links (routers/campaigns/resources.py). The path's
/// resource id is the link's own id, not the linked library item's.
/// </summary>
public class CampaignResourcesService
{
    private const string NotFound = "No such campaign or link. List links with: grimoire-cli campaigns resources list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignResourcesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/resources, filtered to what the caller may see.</summary>
    public async Task<string> ListAsync(string campaignId)
        => await _client.SendAsync(_client.Api.Api.Campaigns[campaignId].Resources.ToGetRequestInformation(), notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/resources. 409 when the item is already linked.</summary>
    public async Task<string> AddAsync(string campaignId, string resourceType, string resourceId, string? visibility, string? categoryId)
    {
        var body = new Generated.Models.ResourceAdd { ResourceType = resourceType, ResourceId = resourceId };
        if (visibility is not null)
            body.Visibility = visibility;
        if (categoryId is not null)
            body.CategoryId = new Generated.Models.ResourceAdd.ResourceAdd_category_id { String = categoryId };
        return await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources.ToPostRequestInformation(body), notFoundHint: NotFound);
    }

    /// <summary>
    /// POST /api/campaigns/{id}/resources/bulk. Skips duplicates and unknown types
    /// silently and returns only the rows it created (resources.py:189-235).
    /// </summary>
    public async Task<string> BulkAsync(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Resources.Bulk.ToPostRequestInformation(new Generated.Models.ResourceBulkAdd());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, notFoundHint: NotFound);
    }

    /// <summary>PATCH /api/campaigns/{id}/resources/{linkId}.</summary>
    public async Task<string> UpdateAsync(string campaignId, string linkId, string? visibility, string? categoryId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources[linkId].ToPatchRequestInformation(BuildUpdateBody(visibility, categoryId)),
            notFoundHint: NotFound);

    /// <summary>
    /// Only the flags given reach the body, so an omitted one is left alone.
    /// Internal so a test can pin that.
    /// </summary>
    internal static Generated.Models.ResourceUpdate BuildUpdateBody(string? visibility, string? categoryId)
    {
        var body = new Generated.Models.ResourceUpdate();
        if (visibility is not null)
            body.Visibility = new Generated.Models.ResourceUpdate.ResourceUpdate_visibility { String = visibility };
        if (categoryId is not null)
            body.CategoryId = new Generated.Models.ResourceUpdate.ResourceUpdate_category_id { String = categoryId };
        return body;
    }

    /// <summary>DELETE /api/campaigns/{id}/resources/{linkId}. 204; a `file` link's upload goes with it.</summary>
    public async Task<string> RemoveAsync(string campaignId, string linkId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources[linkId].ToDeleteRequestInformation(), notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/resources/reorder. Unknown ids are skipped.</summary>
    public async Task<string> ReorderAsync(string campaignId, string[] orderedIds)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources.Reorder.ToPutRequestInformation(
                new Generated.Models.ResourceReorder { OrderedIds = [.. orderedIds] }),
            notFoundHint: NotFound);
}
```

Confirm the wrapper property is named `String` in `Generated/Models/ResourceUpdate.cs` and `ResourceAdd.cs` (it is at `ResourceUpdate.cs:100`); adjust if the generator named it differently.

- [ ] **Step 4: Write the command** `CampaignResourcesCommands.cs`. Structure like `CampaignsCommand.cs` (same logger, `OwnerOnly` array, `BuildClient`, `WriteRawJson`). Declarations and help per subcommand:

```csharp
private static readonly string[] ResourceTypes = ["book", "map", "token", "audio", "model"];
private static readonly string[] Visibilities = ["gm", "public", "private"];

public static Command Create()
{
    var command = new Command("resources", "Library items linked into a campaign");
    command.Subcommands.Add(CreateListCommand());
    command.Subcommands.Add(CreateAddCommand());
    command.Subcommands.Add(CreateBulkCommand());
    command.Subcommands.Add(CreateUpdateCommand());
    command.Subcommands.Add(CreateRemoveCommand());
    command.Subcommands.Add(CreateReorderCommand());
    return command;
}

/// <summary>
/// Whether bulk created fewer rows than it was sent. The endpoint has no errors
/// field — it drops duplicates and unknown types silently — so the counts are
/// the only signal. Internal so a test can pin it.
/// </summary>
internal static bool SkippedAny(string body, string response)
{
    using var sent = JsonDocument.Parse(body);
    using var created = JsonDocument.Parse(response);
    return created.RootElement.GetArrayLength() < sent.RootElement.GetProperty("resources").GetArrayLength();
}
```

Subcommands (each `--id` is `new Option<string>("--id") { Description = "Campaign ID", Required = true }`):

- `list` — "List a campaign's linked items". Notes:
  ```
  "Ordered by visibility (public, private, gm), then sort_order, then name.",
  "id is the link id that update, remove and reorder take.",
  "",
  "Owner or accepted member; members see only what visibility allows."
  ```
  Example `grimoire-cli campaigns resources list --id <campaign-id>`. `AddResponseExampleArray<Generated.Models.LinkedResourceOut>()`.
- `add` — "Link one library item into a campaign". Options: `--resource-type` = `OptionHelpers.Choice("--resource-type", "Kind of library item", ResourceTypes)` with `.Required = true`; `--resource-id` (`Option<string>`, "The library item's ID", Required); `--visibility` = `OptionHelpers.Choice("--visibility", "Who sees it; default gm (owner only)", Visibilities)`; `--category-id` (`Option<string?>`, "Resource category to file it under"). Notes:
  ```
  [.. OwnerOnly, "",
  "The item id is not checked: an unknown id links a row named by the id.",
  "A model link's name is always its id.",
  "",
  "A restricted book is forced to gm whatever --visibility says. private",
  "shows it to the owner alone; share lists go through bulk.",
  "",
  "409 when the item is already linked. Uploaded files link themselves:",
  "grimoire-cli campaigns files upload."]
  ```
  Example `grimoire-cli campaigns resources add --id <campaign-id> --resource-type book --resource-id <book-id> --visibility public`. `AddResponseExample<Generated.Models.LinkedResourceOut>()`.
- `bulk` — "Link many library items in one call". `--input`/`--stdin` with `RequireExactlyOneSource`; validate against `Generated.Models.ResourceBulkAdd.CreateFromDiscriminatorValue` with hint `"put it in each item"`. Notes:
  ```
  [.. OwnerOnly, "",
  "Duplicates and unknown resource_type values are skipped silently; the",
  "response lists only the links created. Exit 3 when fewer come back than",
  "were sent; stdout still carries them.",
  "",
  "A visibility outside gm|public|private becomes gm. file is not linkable",
  "here. Item ids are not checked."]
  ```
  Example `echo '{"resources":[{"resource_type":"book","resource_id":"<id>"}]}' | grimoire-cli campaigns resources bulk --id <campaign-id> --stdin`. `AddRequestShape<Generated.Models.ResourceBulkAdd>()`, `AddResponseExampleArray<Generated.Models.LinkedResourceOut>()`. Action returns `BulkExit.CodeFor(SkippedAny(body, result))`.
- `update` — "Change a link's visibility or category". Options `--id`, `--link-id` (Required, "Link ID from resources list"), `--visibility` (Choice as above, description "Who sees it"), `--category-id` ("Resource category; \"\" moves it back to its type group"). Validator: error `"Provide --visibility or --category-id."` when both are null (pattern: `DuplicatesCommand.cs` unlink validator). Notes:
  ```
  [.. OwnerOnly, "",
  "Any visibility but private clears the share list. A restricted book",
  "stays gm."]
  ```
  `AddResponseExample<Generated.Models.LinkedResourceOut>()`.
- `remove` — "Unlink an item from a campaign". `--id`, `--link-id`. Notes:
  ```
  [.. OwnerOnly, "",
  "The library item is untouched, but removing a file link deletes the upload.",
  "",
  "Answers 204: stdout carries no body."]
  ```
- `reorder` — "Set the manual order of a campaign's links". `--id`; `--ordered-ids` = `new Option<string[]>("--ordered-ids") { Description = "Link IDs in the new order", AllowMultipleArgumentsPerToken = true, Required = true }`. Notes:
  ```
  [.. OwnerOnly, "",
  "Pass every link id: unknown ones are skipped and unlisted ones keep their",
  "old position. Order holds only within a visibility group."]
  ```
  Example `grimoire-cli campaigns resources reorder --id <campaign-id> --ordered-ids <link-id> <link-id>`. No response example (it answers `{"ok": true}`, already typed by the generated `OkResponse` — if `AddResponseExample<Generated.Models.Backend_routers_campaigns__response_schemas_OkResponse>` or similar exists, use it; find the class with `ls src/GrimoireCli/Generated/Models | grep -i okresponse`).

- [ ] **Step 5:** Register under `CampaignsCommand.Create()`. `dotnet format GrimoireCli.sln`; run the filtered tests — expect PASS. Render `campaigns resources add --help` and `bulk --help` and read them.
- [ ] **Step 6:** Full test suite, then commit:
  `git add -A src tests && git commit -m "feat: add campaigns resources commands"`

---

### Task 3: `campaigns categories`

**Files:**
- Create: `src/GrimoireCli/Commands/CampaignCategoriesCommands.cs`, `src/GrimoireCli/Services/CampaignCategoriesService.cs`, `tests/GrimoireCli.Tests/Commands/CampaignCategoriesCommandTests.cs`, `tests/GrimoireCli.Tests/Services/CampaignCategoriesServiceTests.cs`
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs` (add `CampaignCategoriesCommands.Create()` after resources)

**Interfaces:**
- Produces: `CampaignCategoriesCommands.Create()` → `Command("categories")`; `internal static Generated.Models.CategoryUpdate CampaignCategoriesService.BuildUpdateBody(string? name, string? icon, string? iconColor)`.

- [ ] **Step 1: Write failing tests.**

`CampaignCategoriesServiceTests.cs`:
```csharp
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignCategoriesServiceTests
{
    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = CampaignCategoriesService.BuildUpdateBody(null, null, null);
        Assert.Null(body.Name);
        Assert.Null(body.Icon);
        Assert.Null(body.IconColor);
    }

    [Fact]
    public void EmptyIconIsSentToClear()
        => Assert.Equal("", CampaignCategoriesService.BuildUpdateBody(null, "", null).Icon!.String);

    [Fact]
    public void CreateBodyIsAlwaysAResourceCategory()
        => Assert.Equal("resource", CampaignCategoriesService.BuildCreateBody("Handouts", null, null).Kind);
}
```

`CampaignCategoriesCommandTests.cs`:
```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignCategoriesCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "categories", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["categories", .. args]);

    [Fact]
    public void DeleteRejectsAnUnknownMode()
        => Assert.NotEmpty(Parse("delete", "--id", "c", "--category-id", "k", "--mode", "purge").Errors);

    [Fact]
    public void UpdateNeedsSomethingToChange()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--category-id", "k").Errors);

    [Fact]
    public void NoCategoriesCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "list", "create", "update", "delete", "reorder", "group-order" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void GroupOrderTeachesItsKeys()
    {
        var help = RenderHelp("group-order");
        Assert.Contains("cat:<category-id>", help);
        Assert.Contains("type:model", help);
    }

    [Fact]
    public void DeleteWarnsAboutDeleteItems() => Assert.Contains("orphans", RenderHelp("delete"));
}
```

- [ ] **Step 2:** Run filtered tests — expect compile failure.

- [ ] **Step 3: Write the service** `CampaignCategoriesService.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// A campaign's resource categories (routers/campaigns/categories.py). Only the
/// resource kind is handled: note categories are legacy wiki data and creating
/// one is a 400 (categories.py:70-71).
/// </summary>
public class CampaignCategoriesService
{
    private const string NotFound = "No such campaign or category. List categories with: grimoire-cli campaigns categories list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignCategoriesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/categories?kind=resource.</summary>
    public async Task<string> ListAsync(string campaignId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories.ToGetRequestInformation(c => c.QueryParameters.Kind = "resource"),
            notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/categories.</summary>
    public async Task<string> CreateAsync(string campaignId, string name, string? icon, string? iconColor)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories.ToPostRequestInformation(BuildCreateBody(name, icon, iconColor)),
            notFoundHint: NotFound);

    internal static Generated.Models.CategoryCreate BuildCreateBody(string name, string? icon, string? iconColor)
    {
        var body = new Generated.Models.CategoryCreate { Name = name, Kind = "resource" };
        if (icon is not null)
            body.Icon = new Generated.Models.CategoryCreate.CategoryCreate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.CategoryCreate.CategoryCreate_icon_color { String = iconColor };
        return body;
    }

    /// <summary>PATCH /api/campaigns/{id}/categories/{categoryId}.</summary>
    public async Task<string> UpdateAsync(string campaignId, string categoryId, string? name, string? icon, string? iconColor)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories[categoryId].ToPatchRequestInformation(BuildUpdateBody(name, icon, iconColor)),
            notFoundHint: NotFound);

    /// <summary>Only the flags given reach the body. Internal so a test can pin that.</summary>
    internal static Generated.Models.CategoryUpdate BuildUpdateBody(string? name, string? icon, string? iconColor)
    {
        var body = new Generated.Models.CategoryUpdate();
        if (name is not null)
            body.Name = new Generated.Models.CategoryUpdate.CategoryUpdate_name { String = name };
        if (icon is not null)
            body.Icon = new Generated.Models.CategoryUpdate.CategoryUpdate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.CategoryUpdate.CategoryUpdate_icon_color { String = iconColor };
        return body;
    }

    /// <summary>DELETE /api/campaigns/{id}/categories/{categoryId}. 204, including for an unknown id.</summary>
    public async Task<string> DeleteAsync(string campaignId, string categoryId, string? mode)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories[categoryId].ToDeleteRequestInformation(c => c.QueryParameters.Mode = mode),
            notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/categories/reorder. Unknown ids are skipped.</summary>
    public async Task<string> ReorderAsync(string campaignId, string[] orderedIds)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories.Reorder.ToPutRequestInformation(
                new Generated.Models.CategoryReorder { OrderedIds = [.. orderedIds] }),
            notFoundHint: NotFound);

    /// <summary>
    /// PUT /api/campaigns/{id}/resource-group-order. Keeps only known type keys and
    /// this campaign's resource categories, and echoes what it kept
    /// (categories.py:163-183).
    /// </summary>
    public async Task<string> GroupOrderAsync(string campaignId, string[] orderedKeys)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].ResourceGroupOrder.ToPutRequestInformation(
                new Generated.Models.ResourceGroupOrder { OrderedKeys = [.. orderedKeys] }),
            notFoundHint: NotFound);
}
```

Verify the wrapper type and property names against `Generated/Models/CategoryCreate.cs` / `CategoryUpdate.cs` before compiling.

- [ ] **Step 4: Write the command** `CampaignCategoriesCommands.cs` (same structure). `private static readonly string[] DeleteModes = ["uncategorize", "delete_items"];`. Subcommands:

- `list` — "List a campaign's resource categories". Notes: `"Owner or accepted member."`. `AddResponseExampleArray<Generated.Models.CampaignCategoryOut>()`.
- `create` — "Create a resource category". `--id`, `--name` (Required, "Category name"), `--icon` ("Lucide icon key or an emoji"), `--icon-color` ("Preset colour token or #rrggbb"). Notes: `OwnerOnly`. `AddResponseExample<Generated.Models.CampaignCategoryOut>()`.
- `update` — "Rename or restyle a resource category". `--id`, `--category-id` (Required, "Category ID"), `--name`, `--icon` ("…; \"\" clears it"), `--icon-color` ("…; \"\" clears it"). Validator: `"Provide --name, --icon or --icon-color."` when all null. Notes: `OwnerOnly`. `AddResponseExample<Generated.Models.CampaignCategoryOut>()`.
- `delete` — "Delete a resource category". `--id`, `--category-id`, `--mode` = `OptionHelpers.Choice("--mode", "What happens to its links; default uncategorize", DeleteModes)`. Notes:
  ```
  [.. OwnerOnly, "",
  "uncategorize moves its links back to their type groups. delete_items",
  "unlinks them, and orphans any uploaded files among them on disk.",
  "",
  "An unknown category id also answers 204: stdout carries no body."]
  ```
- `reorder` — "Set the order of a campaign's resource categories". `--id`, `--ordered-ids` (`string[]`, AllowMultipleArgumentsPerToken, Required, "Category IDs in the new order"). Notes:
  ```
  [.. OwnerOnly, "",
  "Pass every category id: unknown ones are skipped and unlisted ones keep",
  "their old position."]
  ```
- `group-order` — "Set the order of the resource panel's groups". `--id`, `--ordered-keys` (`string[]`, AllowMultipleArgumentsPerToken, Required, "Group keys in the new order"). Notes:
  ```
  [.. OwnerOnly, "",
  "Keys are type:book, type:map, type:token, type:audio, type:file and",
  "cat:<category-id>. Anything else is dropped without an error,",
  "type:model included; the response lists the keys kept."]
  ```
  Example `grimoire-cli campaigns categories group-order --id <campaign-id> --ordered-keys cat:<category-id> type:book type:map`. `AddResponseExample<Generated.Models.ResourceGroupOrderOut>()`.

- [ ] **Step 5:** Register, format, run filtered tests — PASS. Render `campaigns categories group-order --help`.
- [ ] **Step 6:** Full suite, commit:
  `git add -A src tests && git commit -m "feat: add campaigns categories commands"`

---

### Task 4: `campaigns files upload`

**Files:**
- Create: `src/GrimoireCli/Commands/CampaignFilesCommands.cs`, `src/GrimoireCli/Services/CampaignFilesService.cs`, `tests/GrimoireCli.Tests/Commands/CampaignFilesCommandTests.cs`, `tests/GrimoireCli.Tests/Services/CampaignFilesServiceTests.cs`
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs` (add `CampaignFilesCommands.Create()` after categories)

**Interfaces:**
- Produces: `CampaignFilesCommands.Create()` → `Command("files")`; `internal static string CampaignFilesService.MimeForExtension(string path)`.

- [ ] **Step 1: Write failing tests.**

`CampaignFilesServiceTests.cs`:
```csharp
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignFilesServiceTests
{
    // The server sets is_image from this type (uploads.py:589).
    [Theory]
    [InlineData("map.PNG", "image/png")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("handout.pdf", "application/pdf")]
    [InlineData("scene.uvtt", "application/octet-stream")]
    public void MimeForExtension(string path, string expected)
        => Assert.Equal(expected, CampaignFilesService.MimeForExtension(path));
}
```

`CampaignFilesCommandTests.cs`:
```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignFilesCommandTests
{
    private static string RenderHelp() =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "files", "upload"], full: false);

    [Fact]
    public void UploadRefusesBothCategoryFlags()
        => Assert.NotEmpty(CampaignsCommand.Create().Parse(
            ["files", "upload", "--id", "c", "--file", "f", "--category-id", "k", "--new-category-name", "n"]).Errors);

    [Fact]
    public void UploadCarriesNoRoleTag() => Assert.DoesNotContain("Role required:", RenderHelp());

    [Fact]
    public void UploadSaysItLinksAtGm() => Assert.Contains("gm", RenderHelp());
}
```

- [ ] **Step 2:** Run filtered tests — expect compile failure.

- [ ] **Step 3: Write the service** `CampaignFilesService.cs`, following `SystemsService.UploadCoverAsync` for the multipart body:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Commands;

namespace GrimoireCli.Services;

/// <summary>Campaign file uploads (routers/campaigns/uploads.py:534-617).</summary>
public class CampaignFilesService
{
    private readonly GrimoireApiClient _client;

    public CampaignFilesService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// POST /api/campaigns/{id}/files. Stores the file in the campaign, not the
    /// library, and links it as a `file` resource in the same call. Form fields
    /// are named as FastAPI binds them.
    /// </summary>
    public async Task<string> UploadAsync(string campaignId, string filePath, string? categoryId, string? newCategoryName)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyInputException($"Could not read {filePath}: {ex.Message}");
        }
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("file", MimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        if (categoryId is not null)
            body.AddOrReplacePart("category_id", "text/plain", categoryId);
        if (newCategoryName is not null)
            body.AddOrReplacePart("new_category_name", "text/plain", newCategoryName);
        return await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Files.ToPostRequestInformation(body),
            notFoundHint: "No campaign with that ID. List them with: grimoire-cli campaigns list");
    }

    /// <summary>
    /// The content type the server stores and derives is_image from. Unknown
    /// extensions send octet-stream. Internal so a test can pin the map.
    /// </summary>
    internal static string MimeForExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}
```

- [ ] **Step 4: Write the command** `CampaignFilesCommands.cs`: group `Command("files", "Files uploaded into a campaign")` with one subcommand `upload` — "Upload a file into a campaign and link it". Options: `--id`; `--file` (Required, "Local file to upload"); `--category-id` ("Resource category to file it under"); `--new-category-name` ("Create a resource category and file it there"). Command validator: `"Pass --category-id or --new-category-name, not both."` when both are non-null. Notes:
  ```
  [.. OwnerOnly, "",
  "Stored in the campaign, not the library, and linked at gm visibility;",
  "change it with campaigns resources update. Removing the link deletes the",
  "file.",
  "",
  "Max 200 MB. An admin may set lower per-file or per-campaign limits (413)",
  "or disable uploads (403); admins are exempt."]
  ```
  Example `grimoire-cli campaigns files upload --id <campaign-id> --file handout.pdf --new-category-name Handouts`. `AddResponseExample<Generated.Models.LinkedResourceOut>()`. Action catches `BodyInputException` → log + exit 1, as `CoverCommands.cs` upload does.

- [ ] **Step 5:** Register, format, filtered tests PASS, render `campaigns files upload --help`.
- [ ] **Step 6:** Full suite, commit:
  `git add -A src tests && git commit -m "feat: add campaigns files upload"`

---

### Task 5: Docs, coverage and smoke test

**Files:**
- Modify: `README.md` (Commands table), `tools/generate-api-coverage.py` (`IMPLEMENTED`), `docs/grimoire-api-coverage.md` (regenerated), `docs/grimoire-api-notes.md`, `docs/roadmap.md`, `docker/smoke-test.sh`

- [ ] **Step 1: README.** Add rows to the Commands table in the same style as the `sidecars` rows (`README.md:260-262`), one per subcommand, using the command lines from the spec's "Commands" block. No role suffix on any row.
- [ ] **Step 2: IMPLEMENTED.** Add the 17 keys from the spec's endpoint table, e.g. `"GET /api/campaigns": "`campaigns list` ✅"`, `"PUT /api/campaigns/{campaign_id}/resource-group-order": "`campaigns categories group-order` ✅"`, `"POST /api/campaigns/{campaign_id}/files": "`campaigns files upload` ✅"`. Path params must match the spec's names exactly: `{campaign_id}`, `{resource_id}`, `{category_id}` — confirm with `curl -s http://host.docker.internal:9481/api/openapi.json | jq -r '.paths | keys[]' | grep campaigns`. Regenerate: `python3 tools/generate-api-coverage.py` (read its header for the exact invocation) and confirm the diff shows only campaign rows plus the summary counts.
- [ ] **Step 3: API notes.** Add a `## Campaigns` section to `docs/grimoire-api-notes.md`, in its existing style, carrying the spec's "Verified server behaviour" facts that help text does not: ownership vs roles (and the upload route description's wrong "GM or admin"), `resource-group-order`'s docstring/code mismatch and missing `type:model`, the add/bulk visibility fallback vs update's 400, bulk's missing errors field, list's visibility-first sort, reorder's skip-and-keep behaviour, and `delete_items` orphaning uploads. Cite source lines as the spec does.
- [ ] **Step 4: Roadmap.** Replace the Campaigns open-question line in `docs/roadmap.md` with:
  `- **[Campaign play side](https://github.com/thomaslazar/grimoire-cli/issues/51)** — wiki, sessions, members, guests, sheets, invites, calendar, banners, templates. Deferred; the linking slice has shipped.`
- [ ] **Step 5: Smoke test.** Add a `# ---- campaigns ----` section before the final `echo "smoke: all checks passed"` in `docker/smoke-test.sh`, in the file's existing style (`"$CLI" … >"$WORK/x.out" 2>"$WORK/x.err" || fail …`, `jq -e`, `ok`). It runs under the admin session the script already holds. Every value is fixed so re-runs converge:
  ```bash
  # ---- campaigns ---------------------------------------------------------------
  CAMPAIGN=$("$CLI" campaigns list | jq -r '[.[] | select(.name == "Smoke Campaign")][0].id')
  if [ -z "$CAMPAIGN" ] || [ "$CAMPAIGN" = "null" ]; then
    CAMPAIGN=$(echo '{"name":"Smoke Campaign"}' | "$CLI" campaigns create --stdin | jq -r .id)
  fi
  [ -n "$CAMPAIGN" ] && [ "$CAMPAIGN" != "null" ] || fail "campaigns create should return an id"
  ok "campaigns list/create find or make the smoke campaign"

  CATEGORY=$("$CLI" campaigns categories list --id "$CAMPAIGN" | jq -r '[.[] | select(.name == "Smoke Handouts")][0].id')
  if [ -z "$CATEGORY" ] || [ "$CATEGORY" = "null" ]; then
    CATEGORY=$("$CLI" campaigns categories create --id "$CAMPAIGN" --name "Smoke Handouts" | jq -r .id)
  fi
  ok "campaigns categories list/create find or make the smoke category"

  SMOKE_BOOK=$("$CLI" books list | jq -r '.[0].id')   # adjust to the books list shape used elsewhere in this file
  "$CLI" campaigns resources add --id "$CAMPAIGN" --resource-type book --resource-id "$SMOKE_BOOK" \
    >"$WORK/cr-add.out" 2>&1 || grep -q 409 "$WORK/cr-add.out" \
    || fail "resources add should link or 409: $(cat "$WORK/cr-add.out")"
  LINK=$("$CLI" campaigns resources list --id "$CAMPAIGN" | jq -r --arg b "$SMOKE_BOOK" '[.[] | select(.resource_id == $b)][0].id')
  "$CLI" campaigns resources update --id "$CAMPAIGN" --link-id "$LINK" --visibility public --category-id "$CATEGORY" \
    | jq -e --arg c "$CATEGORY" '.visibility == "public" and .category_id == $c' >/dev/null \
    || fail "resources update should set visibility and category"
  ok "campaigns resources add/list/update link and file a book"

  echo "{\"resources\":[{\"resource_type\":\"book\",\"resource_id\":\"$SMOKE_BOOK\"}]}" \
    | "$CLI" campaigns resources bulk --id "$CAMPAIGN" --stdin >"$WORK/cr-bulk.out" 2>&1; rc=$?
  [ "$rc" -eq 3 ] || fail "resources bulk of an already-linked book should exit 3, got $rc"
  jq -e '. == []' "$WORK/cr-bulk.out" >/dev/null || fail "bulk should create nothing: $(cat "$WORK/cr-bulk.out")"
  ok "campaigns resources bulk exits 3 when it skips"

  "$CLI" campaigns categories group-order --id "$CAMPAIGN" --ordered-keys "cat:$CATEGORY" type:book type:model \
    | jq -e --arg c "cat:$CATEGORY" '.resource_group_order == [$c, "type:book"]' >/dev/null \
    || fail "group-order should keep the category and type:book and drop type:model"
  ok "campaigns categories group-order drops type:model"

  printf 'smoke handout\n' >"$WORK/smoke-handout.txt"
  UPLOAD=$("$CLI" campaigns files upload --id "$CAMPAIGN" --file "$WORK/smoke-handout.txt" --category-id "$CATEGORY")
  echo "$UPLOAD" | jq -e '.resource_type == "file" and .visibility == "gm"' >/dev/null \
    || fail "files upload should link a gm file: $UPLOAD"
  "$CLI" campaigns resources remove --id "$CAMPAIGN" --link-id "$(echo "$UPLOAD" | jq -r .id)" \
    || fail "resources remove should unlink the upload"
  ok "campaigns files upload links a file, and remove takes it away again"
  ```
  Check how existing sections pick a book id (grep `books list` in the file) and reuse that, rather than `.[0]`. Confirm how the CLI reports a 409 on stderr (read `GrimoireApiClient.SendAsync`'s error path) and match the `grep` to it; if exit codes carry it better, test `rc` instead.
- [ ] **Step 6:** Build, then bring up and seed the stack per CLAUDE.md if it is not already running, and run `bash docker/smoke-test.sh` twice in a row — both must pass.
- [ ] **Step 7:** Commit (include spec and plan):
  `git add -A README.md tools docs docker && git commit -m "docs: record campaign linking coverage, behaviour and smoke checks"`

---

### Task 6: Verify and open the PR

- [ ] Run all four checks from CLAUDE.md: `dotnet format GrimoireCli.sln --verify-no-changes`, `dotnet build GrimoireCli.sln`, `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`, `bash docker/smoke-test.sh`.
- [ ] `git log main..HEAD --format=%B | grep -i "co-authored\|generated with"` must print nothing.
- [ ] Push, `gh pr create` (title `feat: add campaign linking commands`, body closes nothing — #51 stays open for the play side; body mentions `Refs #51`), watch CI to green, comment on #51 recording the slice shipped and the play side deferred.
