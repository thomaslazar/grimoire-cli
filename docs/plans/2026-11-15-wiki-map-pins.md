# Wiki Map Pins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `campaigns wiki pins create|update|delete`, and document the
map-page rules in the wiki help.

**Architecture:** A new command file and service under `campaigns wiki`. Each
command is a thin pass-through to one Grimoire 1.11.0 endpoint and takes its
body as flags, following `CampaignCategoriesCommands`. Docs and the smoke test
land in a second task.

**Tech Stack:** C#/.NET 10, System.CommandLine, the Kiota-generated client in
`src/GrimoireCli/Generated/` (never hand-edit it), xUnit, and bash/jq for the
smoke test.

Spec: `docs/specs/2026-11-15-wiki-map-pins-design.md`

## Global Constraints

- None of the three commands takes `AddRoleRequired` or a `permissionHint`.
  Every route depends on `get_current_user` (`wiki_pins.py:18`, v1.11.0).
- Verbs are `create`, `update` and `delete`. There is no `list`.
- Flags carry the body field names: `--x`, `--y`, `--label`, `--target-page-id`,
  `--target-title`, `--icon`, `--icon-color`, and `--visibility` as a choice of
  `page` or `gm`.
- On update, an unset flag is left out of the body and `""` is passed through.
- Help is terse. It must not repeat a choice option's values or what the
  response sample already shows.
- The version floor stays at 1.10.1.
- Commit messages: Conventional Commits, with no `Co-Authored-By` and no
  "Generated with" lines.
- Run `dotnet format GrimoireCli.sln` after editing C#.
- Smoke test writes go only to the local stack, and it must stay idempotent.

---

### Task 1: Pin commands, service, tests, and map-page help

**Files:**
- Create: `src/GrimoireCli/Services/CampaignWikiPinsService.cs`
- Create: `src/GrimoireCli/Commands/CampaignWikiPinsCommands.cs`
- Modify: `src/GrimoireCli/Commands/CampaignWikiCommands.cs` (register `pins` after the templates group)
- Modify: `src/GrimoireCli/Commands/WikiPageHelp.cs` (map-page rules)
- Test: `tests/GrimoireCli.Tests/Services/CampaignWikiPinsServiceTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/CampaignWikiPinsCommandTests.cs`

**Interfaces:**
- Produces:
  - `CampaignWikiPinsService(GrimoireApiClient)`
  - `CreateAsync(string campaignId, string pageId, Generated.Models.WikiMapPinCreate body)`
  - `UpdateAsync(string campaignId, string pageId, string pinId, Generated.Models.WikiMapPinUpdate body)`
  - `DeleteAsync(string campaignId, string pageId, string pinId)`, which returns `Task<string>`
  - the internal static helpers `BuildCreateBody` and `BuildUpdateBody`

- [ ] **Step 1: Write the failing service tests**

`tests/GrimoireCli.Tests/Services/CampaignWikiPinsServiceTests.cs`:

```csharp
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class CampaignWikiPinsServiceTests
{
    [Fact]
    public void CreateBodyWithOnlyCoordinatesLeavesTheRestUnset()
    {
        var body = CampaignWikiPinsService.BuildCreateBody(0.25, 0.5, null, null, null, null, null, null);
        Assert.Equal(0.25, body.X);
        Assert.Equal(0.5, body.Y);
        Assert.Null(body.Label);
        Assert.Null(body.TargetPageId);
        Assert.Null(body.TargetTitle);
        Assert.Null(body.Icon);
        Assert.Null(body.IconColor);
        Assert.Null(body.Visibility);
    }

    [Fact]
    public void CreateBodyCarriesEveryFlagGiven()
    {
        var body = CampaignWikiPinsService.BuildCreateBody(0, 1, "Gate", "p1", "Castle", "castle", "#ff0000", "gm");
        Assert.Equal("Gate", body.Label);
        Assert.Equal("p1", body.TargetPageId!.String);
        Assert.Equal("Castle", body.TargetTitle!.String);
        Assert.Equal("castle", body.Icon!.String);
        Assert.Equal("#ff0000", body.IconColor!.String);
        Assert.Equal("gm", body.Visibility);
    }

    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = CampaignWikiPinsService.BuildUpdateBody(null, null, null, null, null, null, null, null);
        Assert.Null(body.X);
        Assert.Null(body.Y);
        Assert.Null(body.Label);
        Assert.Null(body.TargetPageId);
        Assert.Null(body.TargetTitle);
        Assert.Null(body.Icon);
        Assert.Null(body.IconColor);
        Assert.Null(body.Visibility);
    }

    [Fact]
    public void UpdateBodyCarriesOnlyTheFlagsGiven()
    {
        var body = CampaignWikiPinsService.BuildUpdateBody(0.75, null, null, null, null, null, null, "gm");
        Assert.Equal(0.75, body.X!.Double);
        Assert.Null(body.Y);
        Assert.Equal("gm", body.Visibility!.String);
    }

    [Fact]
    public void EmptyTargetPageIdIsSentToUnlink()
        => Assert.Equal("", CampaignWikiPinsService.BuildUpdateBody(null, null, null, "", null, null, null, null).TargetPageId!.String);

    [Fact]
    public void EmptyIconIsSentToClear()
        => Assert.Equal("", CampaignWikiPinsService.BuildUpdateBody(null, null, null, null, null, "", null, null).Icon!.String);
}
```

- [ ] **Step 2: Write the failing command tests**

`tests/GrimoireCli.Tests/Commands/CampaignWikiPinsCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignWikiPinsCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", "pins", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["wiki", "pins", .. args]);

    [Fact]
    public void CreateNeedsBothCoordinates()
        => Assert.NotEmpty(Parse("create", "--id", "c", "--page-id", "p", "--x", "0.5").Errors);

    [Fact]
    public void CreateParsesWithCoordinates()
        => Assert.Empty(Parse("create", "--id", "c", "--page-id", "p", "--x", "0.5", "--y", "0.5").Errors);

    [Fact]
    public void VisibilityRejectsAnUnknownValue()
        => Assert.NotEmpty(Parse("create", "--id", "c", "--page-id", "p", "--x", "0", "--y", "0", "--visibility", "public").Errors);

    [Fact]
    public void VisibilityAcceptsGm()
        => Assert.Empty(Parse("update", "--id", "c", "--page-id", "p", "--pin-id", "k", "--visibility", "gm").Errors);

    [Fact]
    public void UpdateNeedsSomethingToChange()
        => Assert.NotEmpty(Parse("update", "--id", "c", "--page-id", "p", "--pin-id", "k").Errors);

    [Fact]
    public void UpdateAcceptsAnEmptyTargetPageId()
        => Assert.Empty(Parse("update", "--id", "c", "--page-id", "p", "--pin-id", "k", "--target-page-id", "").Errors);

    [Fact]
    public void DeleteNeedsAPinId()
        => Assert.NotEmpty(Parse("delete", "--id", "c", "--page-id", "p").Errors);

    [Fact]
    public void NoPinsCommandCarriesARoleTag()
    {
        foreach (var verb in new[] { "create", "update", "delete" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void HelpSaysOnlyTheGmMakesAPinGmOnly()
    {
        foreach (var verb in new[] { "create", "update" })
            Assert.Contains("Only the campaign GM", RenderHelp(verb));
    }

    [Fact]
    public void UpdateTeachesUnlinking() => Assert.Contains("--target-page-id \"\" unlinks", RenderHelp("update"));

    [Fact]
    public void WikiCreateHelpCarriesTheMapPageRules()
        => Assert.Contains("linked to the campaign", HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", "create"], full: false));
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~CampaignWikiPins"`
Expected: a build failure, because `CampaignWikiPinsService` does not exist yet.

- [ ] **Step 4: Write the service**

`src/GrimoireCli/Services/CampaignWikiPinsService.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The pins on a campaign map page (routers/campaigns/wiki_pins.py, v1.11.0).
/// Pins are read through the page itself: GET /api/campaigns/{id}/wiki/{page_id}
/// returns them under <c>pins</c>.
/// </summary>
public class CampaignWikiPinsService
{
    private const string PageNotFound = "No such campaign or page, or not one you can see. List pages with: grimoire-cli campaigns wiki list --id <campaign-id>";
    private const string PinNotFound = "No such campaign, page or pin, or not one you can see. Pin ids are under pins in: grimoire-cli campaigns wiki get --id <campaign-id> --page-id <page-id>";

    private readonly GrimoireApiClient _client;

    public CampaignWikiPinsService(GrimoireApiClient client) => _client = client;

    /// <summary>POST /api/campaigns/{id}/wiki/{page_id}/pins.</summary>
    public async Task<string> CreateAsync(string campaignId, string pageId, Generated.Models.WikiMapPinCreate body)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Pins.ToPostRequestInformation(body),
            notFoundHint: PageNotFound);

    internal static Generated.Models.WikiMapPinCreate BuildCreateBody(
        double x, double y, string? label, string? targetPageId, string? targetTitle,
        string? icon, string? iconColor, string? visibility)
    {
        var body = new Generated.Models.WikiMapPinCreate { X = x, Y = y, Label = label, Visibility = visibility };
        if (targetPageId is not null)
            body.TargetPageId = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_target_page_id { String = targetPageId };
        if (targetTitle is not null)
            body.TargetTitle = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_target_title { String = targetTitle };
        if (icon is not null)
            body.Icon = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_icon_color { String = iconColor };
        return body;
    }

    /// <summary>PATCH /api/campaigns/{id}/wiki/{page_id}/pins/{pin_id}.</summary>
    public async Task<string> UpdateAsync(string campaignId, string pageId, string pinId, Generated.Models.WikiMapPinUpdate body)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Pins[pinId].ToPatchRequestInformation(body),
            notFoundHint: PinNotFound);

    /// <summary>Only the flags given reach the body. Internal so a test can pin that.</summary>
    internal static Generated.Models.WikiMapPinUpdate BuildUpdateBody(
        double? x, double? y, string? label, string? targetPageId, string? targetTitle,
        string? icon, string? iconColor, string? visibility)
    {
        var body = new Generated.Models.WikiMapPinUpdate();
        if (x is not null)
            body.X = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_x { Double = x };
        if (y is not null)
            body.Y = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_y { Double = y };
        if (label is not null)
            body.Label = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_label { String = label };
        if (targetPageId is not null)
            body.TargetPageId = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_target_page_id { String = targetPageId };
        if (targetTitle is not null)
            body.TargetTitle = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_target_title { String = targetTitle };
        if (icon is not null)
            body.Icon = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_icon_color { String = iconColor };
        if (visibility is not null)
            body.Visibility = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_visibility { String = visibility };
        return body;
    }

    /// <summary>DELETE /api/campaigns/{id}/wiki/{page_id}/pins/{pin_id}. 204.</summary>
    public async Task<string> DeleteAsync(string campaignId, string pageId, string pinId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Pins[pinId].ToDeleteRequestInformation(),
            notFoundHint: PinNotFound);
}
```

If a generated property name differs from the one used here (for example
`WikiMapPinCreate.Label` being a wrapper rather than a `string`), check
`src/GrimoireCli/Generated/Models/WikiMapPinCreate.cs` and
`WikiMapPinUpdate.cs` and use the real name. Do not edit the generated files.

- [ ] **Step 5: Write the commands**

`src/GrimoireCli/Commands/CampaignWikiPinsCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// Pins on a campaign map page (routers/campaigns/wiki_pins.py,
/// _wiki_maps.py, v1.11.0).
/// </summary>
public static class CampaignWikiPinsCommands
{
    private static readonly string[] Visibilities = ["page", "gm"];

    private static readonly string[] Access =
    [
        "Needs edit rights on the map page, else 403; a page that is not a map",
        "page is a 400. Only the campaign GM sets gm visibility or lifts it; 403",
        "for anyone else.",
    ];

    private static readonly string[] Target =
    [
        "--target-title links the page with that title that you can see, else",
        "creates a stub with the map page's visibility, as [[Title]] does.",
        "--target-page-id must be a page you can see, not the map page itself;",
        "else 400.",
    ];

    private static readonly string[] Hidden =
    [
        "A pin you can't see is a 404: a gm pin when you are not the GM, or one",
        "linking a page you can't read. Pin ids are under pins in campaigns wiki get.",
    ];

    public static Command Create()
    {
        var command = new Command("pins", "Pins on a map page");
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() => new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> PageIdOption() => new("--page-id") { Description = "Map page ID", Required = true };

    private static Option<string> PinIdOption() => new("--pin-id") { Description = "Pin ID", Required = true };

    private static Option<string?> VisibilityOption(string description) =>
        OptionHelpers.Choice("--visibility", description, Visibilities);

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var xOption = new Option<double>("--x") { Description = "Fraction of the picture's width, 0 to 1", Required = true };
        var yOption = new Option<double>("--y") { Description = "Fraction of the picture's height, 0 to 1", Required = true };
        var labelOption = new Option<string?>("--label") { Description = "Pin label" };
        var targetPageIdOption = new Option<string?>("--target-page-id") { Description = "Page the pin links" };
        var targetTitleOption = new Option<string?>("--target-title") { Description = "Title of the page the pin links" };
        var iconOption = new Option<string?>("--icon") { Description = "Lucide icon key or an emoji" };
        var iconColorOption = new Option<string?>("--icon-color") { Description = "Preset colour token or #rrggbb" };
        var visibilityOption = VisibilityOption("page: whoever sees the map page; gm: the GM alone. Default page");
        var command = new Command("create", "Add a pin to a map page")
        {
            idOption, pageIdOption, xOption, yOption, labelOption, targetPageIdOption, targetTitleOption,
            iconOption, iconColorOption, visibilityOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, [.. Access, "", .. Target]);
        command.AddExamples("grimoire-cli campaigns wiki pins create --id <campaign-id> --page-id <page-id> --x 0.25 --y 0.5 --label Gate --target-title \"Thornwall Gate\"");
        command.AddResponseExample<Generated.Models.WikiMapPinOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var body = CampaignWikiPinsService.BuildCreateBody(
                parseResult.GetValue(xOption),
                parseResult.GetValue(yOption),
                parseResult.GetValue(labelOption),
                parseResult.GetValue(targetPageIdOption),
                parseResult.GetValue(targetTitleOption),
                parseResult.GetValue(iconOption),
                parseResult.GetValue(iconColorOption),
                parseResult.GetValue(visibilityOption));
            ConsoleOutput.WriteRawJson(await new CampaignWikiPinsService(client).CreateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var pinIdOption = PinIdOption();
        var xOption = new Option<double?>("--x") { Description = "Fraction of the picture's width, 0 to 1" };
        var yOption = new Option<double?>("--y") { Description = "Fraction of the picture's height, 0 to 1" };
        var labelOption = new Option<string?>("--label") { Description = "Pin label" };
        var targetPageIdOption = new Option<string?>("--target-page-id") { Description = "Page the pin links" };
        var targetTitleOption = new Option<string?>("--target-title") { Description = "Title of the page the pin links" };
        var iconOption = new Option<string?>("--icon") { Description = "Lucide icon key or an emoji; \"\" clears it" };
        var iconColorOption = new Option<string?>("--icon-color") { Description = "Preset colour token or #rrggbb; \"\" clears it" };
        var visibilityOption = VisibilityOption("page: whoever sees the map page; gm: the GM alone");
        var command = new Command("update", "Move or edit a pin on a map page")
        {
            idOption, pageIdOption, pinIdOption, xOption, yOption, labelOption, targetPageIdOption,
            targetTitleOption, iconOption, iconColorOption, visibilityOption
        };
        command.Validators.Add(result =>
        {
            if (result.GetValue(xOption) is null && result.GetValue(yOption) is null &&
                result.GetValue(labelOption) is null && result.GetValue(targetPageIdOption) is null &&
                result.GetValue(targetTitleOption) is null && result.GetValue(iconOption) is null &&
                result.GetValue(iconColorOption) is null && result.GetValue(visibilityOption) is null)
                result.AddError("Provide at least one field to change.");
        });
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Access, "", .. Target,
            "--target-title wins over --target-page-id; --target-page-id \"\" unlinks.",
            "", .. Hidden]);
        command.AddExamples("grimoire-cli campaigns wiki pins update --id <campaign-id> --page-id <page-id> --pin-id <pin-id> --x 0.75 --visibility gm");
        command.AddResponseExample<Generated.Models.WikiMapPinOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var body = CampaignWikiPinsService.BuildUpdateBody(
                parseResult.GetValue(xOption),
                parseResult.GetValue(yOption),
                parseResult.GetValue(labelOption),
                parseResult.GetValue(targetPageIdOption),
                parseResult.GetValue(targetTitleOption),
                parseResult.GetValue(iconOption),
                parseResult.GetValue(iconColorOption),
                parseResult.GetValue(visibilityOption));
            ConsoleOutput.WriteRawJson(await new CampaignWikiPinsService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, parseResult.GetValue(pinIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var pinIdOption = PinIdOption();
        var command = new Command("delete", "Remove a pin from a map page") { idOption, pageIdOption, pinIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            ["Needs edit rights on the map page, else 403.", "", .. Hidden]);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiPinsService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, parseResult.GetValue(pinIdOption)!));
            return 0;
        });
        return command;
    }
}
```

Check the `OptionHelpers.Choice`, `AddExamples` and `AddResponseExample`
signatures against `CampaignCategoriesCommands.cs`, and use whatever is
actually there. If `AddResponseExample<WikiMapPinOut>` needs a sample in
`JsonExamples.g.cs` that does not exist, regenerate the samples with
`dotnet run --project tools/GenerateJsonExamples`, the way the other commands
got theirs. Never write the sample by hand.

- [ ] **Step 6: Register `pins` and add the map-page help**

In `src/GrimoireCli/Commands/CampaignWikiCommands.cs`, after
`command.Subcommands.Add(CampaignWikiTemplateCommands.Create());`, add:

```csharp
        command.Subcommands.Add(CampaignWikiPinsCommands.Create());
```

In `src/GrimoireCli/Commands/WikiPageHelp.cs`, replace these lines:

```csharp
            "page_type is note, session or map (else note on create, ignored on",
            "update); map_id/map_file_id apply only to a map page.",
```

with:

```csharp
            "page_type is note, session or map (else note on create, ignored on",
            "update); map_id/map_file_id apply only to a map page. map_id must be",
            "an image map (png, jpg, gif, webp, bmp) already linked to the campaign;",
            "map_file_id one of its image files; else 400. Setting one clears the",
            "other, \"\" clears it; pins are kept when the picture changes.",
```

- [ ] **Step 7: Format, build and test**

Run:
```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: the build has 0 errors and every test passes. That includes the new
`CampaignWikiPins*` tests, and any help-snapshot or command-tree test that
lists the `wiki` subcommands. If such a test fails only because the `pins`
group is new, add `pins` to its expected list.

Render the help and read it:
`dotnet run --project src/GrimoireCli -- campaigns wiki pins create --help`
Do the same for `update` and `delete`. Check that nothing repeats a choice
option's value set.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Services/CampaignWikiPinsService.cs src/GrimoireCli/Commands/CampaignWikiPinsCommands.cs \
  src/GrimoireCli/Commands/CampaignWikiCommands.cs src/GrimoireCli/Commands/WikiPageHelp.cs \
  tests/GrimoireCli.Tests/Services/CampaignWikiPinsServiceTests.cs tests/GrimoireCli.Tests/Commands/CampaignWikiPinsCommandTests.cs
git add -u
git commit -m "feat: add campaigns wiki pins commands"
```

---

### Task 2: Docs and smoke test

**Files:**
- Modify: `README.md` (Commands table, after the `campaigns wiki templates use` row at about line 462)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md` (the `## Campaign wiki` section, at about line 1714)
- Modify: `docker/smoke-test.sh` (the campaign wiki block)

**Interfaces:**
- Consumes: the Task 1 commands, as named in the Global Constraints.

- [ ] **Step 1: README rows**

After the `campaigns wiki templates use` row, add:

```markdown
| `campaigns wiki pins create --id <campaign-id> --page-id <page-id> --x <0-1> --y <0-1> [--label <text>] [--target-page-id <page-id> \| --target-title <title>] [--icon <icon>] [--icon-color <colour>] [--visibility <page\|gm>]` | Add a pin to a map page |
| `campaigns wiki pins update --id <campaign-id> --page-id <page-id> --pin-id <pin-id> [--x <0-1>] [--y <0-1>] [--label <text>] [--target-page-id <page-id>] [--target-title <title>] [--icon <icon>] [--icon-color <colour>] [--visibility <page\|gm>]` | Move or edit a pin on a map page |
| `campaigns wiki pins delete --id <campaign-id> --page-id <page-id> --pin-id <pin-id>` | Remove a pin from a map page |
```

- [ ] **Step 2: Coverage**

Add these entries to `IMPLEMENTED` in `tools/generate-api-coverage.py`, after
`"DELETE /api/campaigns/{campaign_id}/wiki/{page_id}/hide"`:

```python
    "POST /api/campaigns/{campaign_id}/wiki/{page_id}/pins": "`campaigns wiki pins create` ✅",
    "PATCH /api/campaigns/{campaign_id}/wiki/{page_id}/pins/{pin_id}": "`campaigns wiki pins update` ✅",
    "DELETE /api/campaigns/{campaign_id}/wiki/{page_id}/pins/{pin_id}": "`campaigns wiki pins delete` ✅",
```

Run `python3 tools/generate-api-coverage.py`. It needs the running stack.
Expected: `255 covered`, up from 252.

- [ ] **Step 3: API notes**

In `docs/grimoire-api-notes.md`, under `## Campaign wiki`, add this bullet
directly after the `page_type` takes `map` bullet, which ends
`(wiki.py:527, _wiki_maps.py:248-285).`:

```markdown
- **A map page's picture must be an image the campaign already has**, as of
  1.11.0. `map_id` must be a library image map (png, jpg, jpeg, gif, webp,
  bmp) linked to the campaign, else 400 `Unknown map`, `A map page can only
  show an image map` or `Link the map to the campaign first`; `map_file_id`
  must be one of the campaign's image files, else 400 (`_wiki_maps.py:63-86`,
  v1.11.0). A map page needs no picture to take pins. `get` returns `map`,
  whose `source` is `library`, `file`, `missing` (the map left the library
  with no campaign copy) or `none` (never given one), and `pins`
  (`wiki.py:257-258`, `_wiki_maps.py:122-153`).
- **Pins have no list route and no role dependency**, as of 1.11.0
  (`wiki_pins.py`, v1.11.0). Whoever can edit the map page manages its pins,
  else 403, and a page that is not a map page is a 400 (`:33-42`). Only the
  campaign owner sets `gm` or lifts it, else 403 (`:45-50`). A pin the caller
  can't see answers 404, as for a missing one: a `gm` pin to a non-owner, or a
  pin linking a page the caller can't read (`:89-93`, `_wiki_maps.py:163-177`).
  `target_title` reuses a visible page of that slug or creates a stub with the
  map page's visibility; `target_page_id` must be a visible page other than
  the map page, else 400 (`_wiki_maps.py:288-334`). On PATCH a non-blank
  `target_title` wins over `target_page_id`, `target_page_id: ""` unlinks, and
  `""` clears `icon`/`icon_color` (`:110-125`). `x`/`y` are fractions of the
  picture, 0 to 1 (`_schemas.py:232-259`). GM pins are left out of backlinks
  (`_wiki_maps.py:220-245`).
```

- [ ] **Step 4: Smoke test**

In `docker/smoke-test.sh`, insert this block right after the line
`ok "campaigns wiki delete moves children up"` and before
`# exporting the wiki as a JSON bundle expecting the remaining pages`. The
block deletes its own page, so the export assertions further down are
unaffected:

```bash

# pinning a place on a map page, moving it, then removing pin and page
MAP=$(echo '{"title":"Smoke Map","page_type":"map"}' \
  | "$CLI" campaigns wiki create --id "$WIKI" --stdin 2>"$WORK/cli.err" | jq -r .id) \
  || { cat "$WORK/cli.err" >&2; fail "wiki create (map page) exited non-zero"; }
PIN=$("$CLI" campaigns wiki pins create --id "$WIKI" --page-id "$MAP" --x 0.25 --y 0.5 --label Gate 2>"$WORK/cli.err" | jq -r .id) \
  || { cat "$WORK/cli.err" >&2; fail "wiki pins create exited non-zero"; }
[ -n "$PIN" ] && [ "$PIN" != "null" ] || fail "wiki pins create returned no id"
"$CLI" campaigns wiki pins update --id "$WIKI" --page-id "$MAP" --pin-id "$PIN" --x 0.75 --visibility gm >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki pins update exited non-zero"; }
"$CLI" campaigns wiki get --id "$WIKI" --page-id "$MAP" >"$WORK/map-page.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki get (map page) exited non-zero"; }
jq -e '.pins | length == 1 and .[0].x == 0.75 and .[0].y == 0.5 and .[0].visibility == "gm" and .[0].label == "Gate"' \
  "$WORK/map-page.out" >/dev/null \
  || fail "the map page should hold one moved gm pin: $(cat "$WORK/map-page.out")"
ok "campaigns wiki pins create and update place and move a pin"
"$CLI" campaigns wiki pins delete --id "$WIKI" --page-id "$MAP" --pin-id "$PIN" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki pins delete exited non-zero"; }
"$CLI" campaigns wiki get --id "$WIKI" --page-id "$MAP" | jq -e '.pins == []' >/dev/null \
  || fail "the map page should have no pins after delete"
"$CLI" campaigns wiki delete --id "$WIKI" --page-id "$MAP" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki delete (map page) exited non-zero"; }
ok "campaigns wiki pins delete removes the pin"
```

Check how the surrounding block reads the `wiki create` id (`jq -r .id`) and
match it if it differs. If the `MAP` or `PIN` variable name is already used
elsewhere in the script, rename it.

- [ ] **Step 5: Verify**

Run:
```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
bash docker/smoke-test.sh
```
Expected: all clean and passing. The smoke test runs twice to show the re-run
converges. The stack is at `http://host.docker.internal:9481`; if the smoke
test cannot reach it, check `docker/env.example`.

- [ ] **Step 6: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md docker/smoke-test.sh
git commit -m "docs: cover wiki map pins"
```

The smoke test change goes in this commit too. It is test infrastructure for
the same feature.
