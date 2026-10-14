# Campaign Wiki Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `campaigns wiki` subgroup — `list | get | create | update | delete | search | titles | reorder | hide | unhide` — over Grimoire 1.8.0's ten campaign wiki routes, with help that teaches page bodies and visibility.

**Architecture:** One command file (`CampaignWikiCommands.cs`) and one service (`CampaignWikiService.cs`), following `campaigns resources` / `categories`. The service exposes an internal `*Request` method per route (so a test can pin it) and a public `*Async` that sends it. A small `WikiPageHelp` adds the shared "Page body" and "Visibility" sections to `create` and `update`. Page bodies travel as JSON via `--input` / `--stdin`, validated against the generated `WikiPageCreate` / `WikiPageUpdate`.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client (`src/GrimoireCli/Generated/`), xUnit, bash smoke test against the local Grimoire 1.8.0 stack.

**Spec:** [docs/specs/2026-10-14-campaign-wiki-pages-design.md](../specs/2026-10-14-campaign-wiki-pages-design.md) · **Tracking:** #102 (piece A1)

## Global Constraints

- Target Grimoire **1.8.0**; `MinSupportedVersion` / `MaxTestedVersion` do not change.
- Commands under `campaigns wiki`; every command takes the campaign as `--id <campaign-id>` and a page as `--page-id`.
- No role tags: none of the ten routes carries a role dependency.
- `create` / `update` take JSON only, via `--input` / `--stdin` (exactly one), validated with `JsonBodyInput.Validate` against `WikiPageCreate` / `WikiPageUpdate`. No `--body-file`, no front matter.
- stdout is the server's bytes unmodified (`ConsoleOutput.WriteRawJson`).
- One file per command group: the subgroup gets its own command file and its own service.
- Never hand-edit `src/GrimoireCli/Generated/`. Run `dotnet format GrimoireCli.sln` after editing C#; no unnecessary blank lines in method bodies.
- Conventional Commits, lowercase imperative subject, **no `Co-Authored-By:` or any Claude/"Generated with" attribution**.
- Do not edit `CHANGELOG.md` or `docs/roadmap.md`.
- Anything that writes goes to the local stack only (`GRIMOIRE_SERVER=http://host.docker.internal:9481`); smoke additions converge on re-run.

## Server facts the code and help rely on

From the spec (verified at `v1.8.0`, the starred ones live on 2026-10-14):

- Routes under `/api/campaigns/{campaign_id}/wiki`: `GET` list (`mine`, `include_hidden`), `POST` create, `GET search?q=` (1–200 chars), `GET titles`, `PUT reorder` (`{ordered_ids}`), `GET|PATCH|DELETE {page_id}`, `POST|DELETE {page_id}/hide`.
- Create/update/delete/reorder are refused while the campaign is archived; hide/unhide are not.
- Delete: author only; children move up to the deleted page's parent; an unknown page id is a silent 204.
- Hide is a 409 in a personal campaign; unhide is always allowed. Hiding a parent hides its subtree.
- \*Stubs: an unknown unpinned `[[Title]]` (incl. `[[Title|label]]`) creates an empty page with the saving page's visibility, authored by the saver; a pinned `[[T:id-<missing>]]` creates none; an unreserved prefix like `[[npc:goblin]]` is a title.
- \*Embeds never create stubs: `[[book:<id>]]`, `[[book:<id>:<page>]]`, `[[map:<id>]]`, `[[token:<id>]]`, `[[audio:<id>]]`, `[[model:<id>]]` (library ids); `[[image:<file-id>]]`, `[[file:<file-id>]]` (campaign file ids).
- A rename rewrites `[[Old Title]]` links in other pages.
- `||text||` is visible to the campaign owner only; a non-owner's `||…||` is stored literally; `get` strips secrets for non-owners without a marker.

## File Structure

| File | Change |
|---|---|
| `src/GrimoireCli/Services/CampaignWikiService.cs` | **new** — the ten routes |
| `src/GrimoireCli/Commands/CampaignWikiCommands.cs` | **new** — `campaigns wiki` subgroup |
| `src/GrimoireCli/Commands/WikiPageHelp.cs` | **new** — shared "Page body" / "Visibility" sections (Task 2) |
| `src/GrimoireCli/Commands/CampaignsCommand.cs` | register the subgroup |
| `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs` | **new** — pins every request |
| `tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs` | **new** |
| `docker/smoke-test.sh`, `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md` | Task 3 |

---

### Task 0: Branch and commit the design

- [ ] **Step 1:** `git checkout main && git pull && git checkout -b feat/campaign-wiki-pages`
- [ ] **Step 2:** Commit spec and plan (both untracked):

```bash
git add docs/specs/2026-10-14-campaign-wiki-pages-design.md docs/plans/2026-10-14-campaign-wiki-pages.md
git commit -m "docs: design campaign wiki page commands"
```

---

### Task 1: Service for all ten routes, and the read commands

**Files:**
- Create: `src/GrimoireCli/Services/CampaignWikiService.cs`
- Create: `src/GrimoireCli/Commands/CampaignWikiCommands.cs` (`list`, `get`, `search`, `titles`)
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs`
- Create: `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs`
- Create: `tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs`

**Interfaces:**
- Produces: `CampaignWikiService(GrimoireApiClient)` with public `ListAsync(string campaignId, bool mine, bool includeHidden)`, `GetAsync(string campaignId, string pageId)`, `CreateAsync(string campaignId, string rawBody)`, `UpdateAsync(string campaignId, string pageId, string rawBody)`, `DeleteAsync(string campaignId, string pageId)`, `SearchAsync(string campaignId, string query)`, `TitlesAsync(string campaignId)`, `ReorderAsync(string campaignId, string[] orderedIds)`, `HideAsync(string campaignId, string pageId)`, `UnhideAsync(string campaignId, string pageId)` (all `Task<string>`), each with an internal `…Request` counterpart taking the same parameters and returning `RequestInformation`. `CampaignWikiCommands.Create()` returns the `wiki` `Command`; Task 2 adds the write commands to it.

- [ ] **Step 1: Write the failing service test** — create `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each of the ten wiki requests to its route and method. Several share a
/// path and differ only in method (get/update/delete a page; hide/unhide), so a
/// builder call swapped for its neighbour would hit the right URL with the
/// wrong verb — which nothing else here would catch.
/// </summary>
public class CampaignWikiServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/wiki";

    private static CampaignWikiService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "list", "GET", Base },
        { "get", "GET", Base + "/p-1" },
        { "create", "POST", Base },
        { "update", "PATCH", Base + "/p-1" },
        { "delete", "DELETE", Base + "/p-1" },
        { "search", "GET", Base + "/search?q=ruins" },
        { "titles", "GET", Base + "/titles" },
        { "reorder", "PUT", Base + "/reorder" },
        { "hide", "POST", Base + "/p-1/hide" },
        { "unhide", "DELETE", Base + "/p-1/hide" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "list" => s.ListRequest("c-1", false, false),
            "get" => s.GetRequest("c-1", "p-1"),
            "create" => s.CreateRequest("c-1", "{\"title\":\"T\"}"),
            "update" => s.UpdateRequest("c-1", "p-1", "{\"title\":\"T\"}"),
            "delete" => s.DeleteRequest("c-1", "p-1"),
            "search" => s.SearchRequest("c-1", "ruins"),
            "titles" => s.TitlesRequest("c-1"),
            "reorder" => s.ReorderRequest("c-1", ["p-2", "p-1"]),
            "hide" => s.HideRequest("c-1", "p-1"),
            "unhide" => s.UnhideRequest("c-1", "p-1"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void ListSendsNoFlagsByDefault()
        => Assert.DoesNotContain("?", Of(Service().ListRequest("c-1", false, false)).Uri);

    [Fact]
    public void ListSendsMineAndIncludeHiddenWhenSet()
    {
        var uri = Of(Service().ListRequest("c-1", true, true)).Uri;
        Assert.Contains("mine=true", uri);
        Assert.Contains("include_hidden=true", uri);
    }

    [Fact]
    public void CreateSendsTheRawBodyUnchanged()
    {
        var info = Service().CreateRequest("c-1", "{\"title\":\"Baron\",\"body\":\"# Hi\"}");
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"title\":\"Baron\",\"body\":\"# Hi\"}", reader.ReadToEnd());
    }

    [Fact]
    public void ReorderSendsTheIdsInOrder()
    {
        var info = Service().ReorderRequest("c-1", ["p-2", "p-1"]);
        using var reader = new StreamReader(info.Content);
        Assert.Contains("\"ordered_ids\":[\"p-2\",\"p-1\"]", reader.ReadToEnd().Replace(" ", ""));
    }
}
```

- [ ] **Step 2: Write the failing command tests** — create `tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignWikiCommandTests
{
    private static string Help(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "wiki", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["wiki", .. args]);

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("search")]
    [InlineData("titles")]
    public void TheWikiSubgroupHostsTheReadCommands(string verb)
        => Assert.Contains(verb, CampaignsCommand.Create().Subcommands.Single(c => c.Name == "wiki").Subcommands.Select(c => c.Name));

    [Fact]
    public void ListNeedsTheCampaign()
        => Assert.NotEmpty(Parse("list").Errors);

    [Fact]
    public void ListTakesMineAndIncludeHidden()
        => Assert.Empty(Parse("list", "--id", "c", "--mine", "--include-hidden").Errors);

    [Fact]
    public void GetNeedsThePage()
        => Assert.NotEmpty(Parse("get", "--id", "c").Errors);

    [Theory]
    [InlineData("")]
    public void SearchRejectsAnEmptyQuery(string query)
        => Assert.NotEmpty(Parse("search", "--id", "c", "--query", query).Errors);

    [Fact]
    public void SearchRejectsAQueryOver200Characters()
        => Assert.NotEmpty(Parse("search", "--id", "c", "--query", new string('x', 201)).Errors);

    [Fact]
    public void SearchAcceptsA200CharacterQuery()
        => Assert.Empty(Parse("search", "--id", "c", "--query", new string('x', 200)).Errors);

    [Fact]
    public void GetSaysNonOwnersGetSecretsStripped()
        => Assert.Contains("GM secrets removed", Help("get"));

    [Fact]
    public void SearchSaysItIsAPlainSubstring()
        => Assert.Contains("plain substring", Help("search"));

    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("search")]
    [InlineData("titles")]
    public void TheReadCommandsCarryNoRoleTag(string verb)
        => Assert.DoesNotContain("Role required:", Help(verb));
}
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: build errors — `CampaignWikiService` / `CampaignWikiCommands` do not exist.

- [ ] **Step 4: Create `src/GrimoireCli/Services/CampaignWikiService.cs`:**

```csharp
using System.Text;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The ten routes behind `campaigns wiki`: a campaign's wiki pages. Each request
/// is built by an internal method so a test can pin its route and method; the
/// public methods send it. Access is checked inside the server's handlers, not by
/// role, so no permission hint is passed — the server's own 403 detail explains.
/// </summary>
public class CampaignWikiService
{
    private const string CampaignNotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";
    private const string PageNotFound = "No such campaign or page. List pages with: grimoire-cli campaigns wiki list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignWikiService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/wiki. Visible pages, hidden ones excluded unless asked for.</summary>
    public async Task<string> ListAsync(string campaignId, bool mine, bool includeHidden)
        => await _client.SendAsync(ListRequest(campaignId, mine, includeHidden), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/{page}. Non-owners get the body with GM secrets removed.</summary>
    public async Task<string> GetAsync(string campaignId, string pageId)
        => await _client.SendAsync(GetRequest(campaignId, pageId), notFoundHint: PageNotFound);

    /// <summary>POST /api/campaigns/{id}/wiki. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string campaignId, string rawBody)
        => await _client.SendAsync(CreateRequest(campaignId, rawBody), notFoundHint: CampaignNotFound);

    /// <summary>PATCH /api/campaigns/{id}/wiki/{page}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string campaignId, string pageId, string rawBody)
        => await _client.SendAsync(UpdateRequest(campaignId, pageId, rawBody), notFoundHint: PageNotFound);

    /// <summary>DELETE /api/campaigns/{id}/wiki/{page}. An unknown page is a silent 204.</summary>
    public async Task<string> DeleteAsync(string campaignId, string pageId)
        => await _client.SendAsync(DeleteRequest(campaignId, pageId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/search. Plain substring over title and body.</summary>
    public async Task<string> SearchAsync(string campaignId, string query)
        => await _client.SendAsync(SearchRequest(campaignId, query), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/wiki/titles. Ids, titles and headings for links.</summary>
    public async Task<string> TitlesAsync(string campaignId)
        => await _client.SendAsync(TitlesRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>PUT /api/campaigns/{id}/wiki/reorder. Pages the caller cannot edit keep their slots.</summary>
    public async Task<string> ReorderAsync(string campaignId, string[] orderedIds)
        => await _client.SendAsync(ReorderRequest(campaignId, orderedIds), notFoundHint: CampaignNotFound);

    /// <summary>POST /api/campaigns/{id}/wiki/{page}/hide. Only the caller's own view.</summary>
    public async Task<string> HideAsync(string campaignId, string pageId)
        => await _client.SendAsync(HideRequest(campaignId, pageId), notFoundHint: PageNotFound);

    /// <summary>DELETE /api/campaigns/{id}/wiki/{page}/hide.</summary>
    public async Task<string> UnhideAsync(string campaignId, string pageId)
        => await _client.SendAsync(UnhideRequest(campaignId, pageId), notFoundHint: CampaignNotFound);

    internal RequestInformation ListRequest(string campaignId, bool mine, bool includeHidden)
        => _client.Api.Api.Campaigns[campaignId].Wiki.ToGetRequestInformation(c =>
        {
            // Sent only when set: the server defaults both to false.
            if (mine) c.QueryParameters.Mine = true;
            if (includeHidden) c.QueryParameters.IncludeHidden = true;
        });

    internal RequestInformation GetRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].ToGetRequestInformation();

    internal RequestInformation CreateRequest(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Wiki.ToPostRequestInformation(new Generated.Models.WikiPageCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string campaignId, string pageId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Wiki[pageId].ToPatchRequestInformation(new Generated.Models.WikiPageUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].ToDeleteRequestInformation();

    internal RequestInformation SearchRequest(string campaignId, string query)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Search.ToGetRequestInformation(c => c.QueryParameters.Q = query);

    internal RequestInformation TitlesRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Titles.ToGetRequestInformation();

    internal RequestInformation ReorderRequest(string campaignId, string[] orderedIds)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Reorder.ToPutRequestInformation(
            new Generated.Models.WikiReorder { OrderedIds = [.. orderedIds] });

    internal RequestInformation HideRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Hide.ToPostRequestInformation();

    internal RequestInformation UnhideRequest(string campaignId, string pageId)
        => _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Hide.ToDeleteRequestInformation();
}
```

- [ ] **Step 5: Create `src/GrimoireCli/Commands/CampaignWikiCommands.cs`** with the read commands:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class CampaignWikiCommands
{
    public static Command Create()
    {
        var command = new Command("wiki", "A campaign's wiki pages");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateSearchCommand());
        command.Subcommands.Add(CreateTitlesCommand());
        return command;
    }

    private static Option<string> CampaignIdOption() =>
        new("--id") { Description = "Campaign ID", Required = true };

    private static Option<string> PageIdOption() =>
        new("--page-id") { Description = "Wiki page ID", Required = true };

    private static Command CreateListCommand()
    {
        var idOption = CampaignIdOption();
        var mineOption = new Option<bool>("--mine") { Description = "Only pages you authored" };
        var includeHiddenOption = new Option<bool>("--include-hidden") { Description = "Include pages you hid" };
        var command = new Command("list", "List the wiki pages you can see")
        {
            idOption, mineOption, includeHiddenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ordered by the manual order, then title. Each row says whether you can",
            "edit or delete it; nesting is by parent_id.");
        command.AddExamples(
            "grimoire-cli campaigns wiki list --id <campaign-id>",
            "grimoire-cli campaigns wiki list --id <campaign-id> --mine");
        command.AddResponseExampleArray<Generated.Models.WikiPageListItem>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).ListAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(mineOption),
                parseResult.GetValue(includeHiddenOption)));
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("get", "Get one wiki page with its body and backlinks")
        {
            idOption, pageIdOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Anyone but the campaign owner gets the body with GM secrets removed,",
            "with no marker where they were.");
        command.AddExamples("grimoire-cli campaigns wiki get --id <campaign-id> --page-id <page-id>");
        command.AddResponseExample<Generated.Models.WikiPageDetailOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).GetAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateSearchCommand()
    {
        var idOption = CampaignIdOption();
        var queryOption = new Option<string>("--query") { Description = "Text to find, 1-200 characters", Required = true };
        queryOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>() ?? "";
            if (value.Length is < 1 or > 200)
                result.AddError("--query must be 1-200 characters.");
        });
        var command = new Command("search", "Search wiki page titles and bodies")
        {
            idOption, queryOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A plain substring, case-insensitive, over title and body — no query",
            "syntax. Hidden pages are left out; non-owners never match secret text.");
        command.AddExamples("grimoire-cli campaigns wiki search --id <campaign-id> --query Thornwall");
        command.AddResponseExample<Generated.Models.WikiSearchOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).SearchAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(queryOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateTitlesCommand()
    {
        var idOption = CampaignIdOption();
        var command = new Command("titles", "Page ids, titles and headings for writing links")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "ambiguous is true when another visible page shares the title; link",
            "such a page as [[Title:id-<page-id>]]. Headings feed [[Title:#Heading]].");
        command.AddExamples("grimoire-cli campaigns wiki titles --id <campaign-id>");
        command.AddResponseExampleArray<Generated.Models.WikiPageTitleOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).TitlesAsync(parseResult.GetValue(idOption)!));
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 6: Register the subgroup** — in `src/GrimoireCli/Commands/CampaignsCommand.cs`, after `command.Subcommands.Add(CampaignFilesCommands.Create());` add `command.Subcommands.Add(CampaignWikiCommands.Create());`.

- [ ] **Step 7: Format, test, check help**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet build GrimoireCli.sln
for v in list get search titles; do src/GrimoireCli/bin/Debug/net10.0/grimoire-cli campaigns wiki $v --help; done
```

Expected: all tests pass (the service theory covers all ten routes already); help renders with the Notes above. If `CreateSearchCommand`'s validator reads the value differently in this System.CommandLine version, match how `OptionHelpers.Range` reads values in `src/GrimoireCli/Commands/OptionHelpers.cs`.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Services/CampaignWikiService.cs src/GrimoireCli/Commands/CampaignWikiCommands.cs \
  src/GrimoireCli/Commands/CampaignsCommand.cs tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs \
  tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs
git commit -m "feat: add campaigns wiki list, get, search and titles"
```

---

### Task 2: The write commands and the shared page help

**Files:**
- Create: `src/GrimoireCli/Commands/WikiPageHelp.cs`
- Modify: `src/GrimoireCli/Commands/CampaignWikiCommands.cs` (add `create`, `update`, `delete`, `reorder`, `hide`, `unhide`)
- Modify: `tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs`

**Interfaces:**
- Consumes: `CampaignWikiService.CreateAsync / UpdateAsync / DeleteAsync / ReorderAsync / HideAsync / UnhideAsync` and the private `CampaignIdOption()` / `PageIdOption()` helpers from Task 1.
- Produces: `WikiPageHelp.AddTo(Command command)`.

- [ ] **Step 1: Write the failing tests** — append inside `CampaignWikiCommandTests`:

```csharp
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("reorder")]
    [InlineData("hide")]
    [InlineData("unhide")]
    public void TheWikiSubgroupHostsTheWriteCommands(string verb)
        => Assert.Contains(verb, CampaignsCommand.Create().Subcommands.Single(c => c.Name == "wiki").Subcommands.Select(c => c.Name));

    [Fact]
    public void CreateNeedsExactlyOneBodySource()
    {
        Assert.NotEmpty(Parse("create", "--id", "c").Errors);
        Assert.NotEmpty(Parse("create", "--id", "c", "--stdin", "--input", "p.json").Errors);
        Assert.Empty(Parse("create", "--id", "c", "--stdin").Errors);
    }

    [Fact]
    public void UpdateNeedsThePageAndABody()
    {
        Assert.NotEmpty(Parse("update", "--id", "c", "--stdin").Errors);
        Assert.Empty(Parse("update", "--id", "c", "--page-id", "p", "--stdin").Errors);
    }

    [Fact]
    public void AnUnknownCreateFieldIsRejected()
        => Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"title\":\"T\",\"content\":\"x\"}",
                Generated.Models.WikiPageCreate.CreateFromDiscriminatorValue, "pass it with --id"));

    [Fact]
    public void AnUnknownUpdateFieldIsRejected()
        => Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"name\":\"T\"}",
                Generated.Models.WikiPageUpdate.CreateFromDiscriminatorValue, "pass it with --page-id"));

    [Fact]
    public void ReorderNeedsTheIds()
        => Assert.NotEmpty(Parse("reorder", "--id", "c").Errors);

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public void TheBodyCommandsTeachLinksEmbedsAndSecrets(string verb)
    {
        var help = Help(verb);
        Assert.Contains("Page body:", help);
        Assert.Contains("creates an empty stub", help);
        Assert.Contains("[[model:<id>]]", help);
        Assert.Contains("[[image:<file-id>]]", help);
        Assert.Contains("||text|| is a GM secret", help);
        Assert.Contains("Visibility:", help);
    }

    [Fact]
    public void DeleteSaysChildrenMoveUp()
        => Assert.Contains("children move up", Help("delete"));

    [Fact]
    public void HideSaysItIsRefusedInAPersonalCampaign()
        => Assert.Contains("personal campaign", Help("hide"));

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("reorder")]
    [InlineData("hide")]
    [InlineData("unhide")]
    public void TheWriteCommandsCarryNoRoleTag(string verb)
        => Assert.DoesNotContain("Role required:", Help(verb));
```

Add `using GrimoireCli.Commands;` is already present; `BodyInputException` and `JsonBodyInput` live in `GrimoireCli.Commands` (check `src/GrimoireCli/Commands/JsonBodyInput.cs` for the exact namespace and add a `using` if it differs).

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~CampaignWikiCommandTests`
Expected: the new tests FAIL (commands missing).

- [ ] **Step 3: Create `src/GrimoireCli/Commands/WikiPageHelp.cs`:**

```csharp
using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>
/// The page-body and visibility rules campaigns wiki create and update share:
/// the link and embed grammar, GM secrets, the stubs a save creates, and who can
/// see and edit each visibility level (routers/campaigns/wiki.py,
/// wikilinks.py, _wiki_helpers.py at v1.8.0).
/// </summary>
internal static class WikiPageHelp
{
    public static void AddTo(Command command)
    {
        command.AddHelpSection("Page body", HelpSectionPosition.Top,
            "body is markdown. [[Title]] links a page; [[Title|label]] shows label;",
            "[[Title:#Heading]] jumps to a heading; [[Title:id-<page-id>]] pins the",
            "page so the link survives renames (titles from campaigns wiki titles).",
            "Saving creates an empty stub for each unknown unpinned [[Title]], with",
            "this page's visibility; a pinned link to a missing page creates none.",
            "Renaming a page rewrites links to its old title.",
            "",
            "Embeds are not links: [[book:<id>]], [[book:<id>:<page>]], [[map:<id>]],",
            "[[token:<id>]], [[audio:<id>]], [[model:<id>]] take library ids;",
            "[[image:<file-id>]] and [[file:<file-id>]] take campaign file ids. Any",
            "other prefix is a page title. The web editor offers only items linked",
            "to the campaign.",
            "",
            "||text|| is a GM secret: only the campaign owner sees it. Anyone else's",
            "||…|| is stored as literal text.");
        command.AddHelpSection("Visibility", HelpSectionPosition.Top,
            "gm — the author alone (the GM's private pages, or a player's self-only",
            "note); group — every member, and every member can edit; members — the",
            "author plus shared_user_ids (read) and shared_write_user_ids (read and",
            "write). Only the author changes visibility or sharing. A personal",
            "campaign stores every page as gm.");
    }
}
```

- [ ] **Step 4: Add the write commands** — in `CampaignWikiCommands.Create()`, after the `titles` line add, in this order:

```csharp
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateUpdateCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateReorderCommand());
        command.Subcommands.Add(CreateHideCommand());
        command.Subcommands.Add(CreateUnhideCommand());
```

add the logger the body-reading commands use, as the first member of the class:

```csharp
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
```

and add these methods to the class:

```csharp
    private static readonly string[] Archived =
    [
        "Refused while the campaign is archived.",
    ];

    private static Command CreateCreateCommand()
    {
        var idOption = CampaignIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("create", "Create a wiki page") { idOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        WikiPageHelp.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Needs campaign membership. An empty title becomes Untitled; parent_id",
            "must be a page you can edit."]);
        command.AddExamples(
            "echo '{\"title\":\"Baron Valdric\",\"body\":\"Rules [[Thornwall]].\",\"visibility\":\"group\"}' | grimoire-cli campaigns wiki create --id <campaign-id> --stdin",
            "jq -n --rawfile body npc.md '{title:\"Baron Valdric\",body:$body}' | grimoire-cli campaigns wiki create --id <campaign-id> --stdin");
        command.AddRequestShape<Generated.Models.WikiPageCreate>();
        command.AddResponseExample<Generated.Models.WikiPageSummaryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.WikiPageCreate.CreateFromDiscriminatorValue, "pass it with --id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).CreateAsync(parseResult.GetValue(idOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateUpdateCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var command = new Command("update", "Update a wiki page") { idOption, pageIdOption, inputOption, stdinOption };
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        WikiPageHelp.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Send only the fields to change. parent_id \"\" moves the page to the top",
            "level; \"\" clears icon and icon_color. Needs edit access (see",
            "Visibility)."]);
        command.AddExamples(
            "echo '{\"title\":\"Baron Valdric the Grey\"}' | grimoire-cli campaigns wiki update --id <campaign-id> --page-id <page-id> --stdin");
        command.AddRequestShape<Generated.Models.WikiPageUpdate>();
        command.AddResponseExample<Generated.Models.WikiPageSummaryOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.WikiPageUpdate.CreateFromDiscriminatorValue, "pass it with --page-id");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).UpdateAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!, body));
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("delete", "Delete a wiki page") { idOption, pageIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Author only. The page's children move up to its parent. An unknown",
            "page id succeeds silently."]);
        command.AddExamples("grimoire-cli campaigns wiki delete --id <campaign-id> --page-id <page-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).DeleteAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateReorderCommand()
    {
        var idOption = CampaignIdOption();
        var orderedIdsOption = new Option<string[]>("--ordered-ids")
        {
            Description = "Page IDs in the new order",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var command = new Command("reorder", "Set the manual order of wiki pages") { idOption, orderedIdsOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            [.. Archived, "",
            "Re-deals the slots of the listed pages you can edit; unknown pages and",
            "pages you cannot edit are skipped and keep their slots."]);
        command.AddExamples("grimoire-cli campaigns wiki reorder --id <campaign-id> --ordered-ids <page-id> <page-id>");
        command.AddResponseExample<Generated.Models.Backend__routers__campaigns___response_schemas__OkResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).ReorderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(orderedIdsOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateHideCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("hide", "Hide a wiki page from your own view") { idOption, pageIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Changes only your view; nobody else's. Hiding a parent hides its",
            "subtree. Refused (409) in a personal campaign.");
        command.AddExamples("grimoire-cli campaigns wiki hide --id <campaign-id> --page-id <page-id>");
        command.AddResponseExample<Generated.Models.WikiPageHiddenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).HideAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }

    private static Command CreateUnhideCommand()
    {
        var idOption = CampaignIdOption();
        var pageIdOption = PageIdOption();
        var command = new Command("unhide", "Bring back a wiki page you hid") { idOption, pageIdOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "A page hidden because its parent is hidden comes back only when the",
            "parent does.");
        command.AddExamples("grimoire-cli campaigns wiki unhide --id <campaign-id> --page-id <page-id>");
        command.AddResponseExample<Generated.Models.WikiPageHiddenOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            ConsoleOutput.WriteRawJson(await new CampaignWikiService(client).UnhideAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pageIdOption)!));
            return 0;
        });
        return command;
    }
```

Check `HelpExtensions.AddHelpSection`'s signature accepts a `string[]` built with a collection expression (`CampaignsCommand.cs` uses `[.. OwnerOnly, "", "…"]` the same way). If `AddRequestShape<T>` is named differently, match `CampaignsCommand.CreateUpdateCommand`.

- [ ] **Step 5: Format, test, check help**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet build GrimoireCli.sln
for v in create update delete reorder hide unhide; do src/GrimoireCli/bin/Debug/net10.0/grimoire-cli campaigns wiki $v --help; done
```

Expected: all tests pass; `create` / `update` help shows "Page body", "Visibility" and "Notes"; no "Role required".

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/WikiPageHelp.cs src/GrimoireCli/Commands/CampaignWikiCommands.cs \
  tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs
git commit -m "feat: add campaigns wiki create, update, delete, reorder, hide and unhide"
```

---

### Task 3: Smoke test and docs

**Files:**
- Modify: `docker/smoke-test.sh` (insert directly before the line `# ---- api keys ----`)
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`

- [ ] **Step 1: Smoke section** — insert directly before `# ---- api keys ----`. `$WORK/maps.out` (written by the earlier `maps list` step) supplies a seeded map id.

```bash
# ---- campaign wiki ----------------------------------------------------------
# A dedicated GM campaign: Smoke Campaign is personal, where every page is stored
# as gm and hide is refused. Every page the section creates is deleted again, and
# any page left by an interrupted run is cleared first, so a re-run converges.
# listing campaigns to find, or creating, the Smoke Wiki GM campaign
WIKI=$("$CLI" campaigns list 2>"$WORK/cli.err" | jq -r '[.[] | select(.name == "Smoke Wiki")][0].id') \
  || { cat "$WORK/cli.err" >&2; fail "campaigns list exited non-zero"; }
if [ -z "$WIKI" ] || [ "$WIKI" = "null" ]; then
  WIKI=$(echo '{"name":"Smoke Wiki","is_gm_campaign":true}' | "$CLI" campaigns create --stdin 2>"$WORK/cli.err" | jq -r .id) \
    || { cat "$WORK/cli.err" >&2; fail "creating the Smoke Wiki campaign exited non-zero"; }
fi
[ -n "$WIKI" ] && [ "$WIKI" != "null" ] || fail "no Smoke Wiki campaign id"
# clearing pages an interrupted run left behind, expecting an empty wiki
for p in $("$CLI" campaigns wiki list --id "$WIKI" --include-hidden | jq -r '.[].id'); do
  "$CLI" campaigns wiki delete --id "$WIKI" --page-id "$p" >/dev/null 2>&1 || true
done
[ "$("$CLI" campaigns wiki list --id "$WIKI" --include-hidden | jq 'length')" -eq 0 ] \
  || fail "the Smoke Wiki should start empty"
WIKI_MAP=$(jq -r '.maps[0].id' "$WORK/maps.out")

# creating a parent page, then a child under it that links a missing page and embeds a map
PARENT=$(echo '{"title":"Smoke Lore","visibility":"group"}' \
  | "$CLI" campaigns wiki create --id "$WIKI" --stdin 2>"$WORK/cli.err" | jq -r .id) \
  || { cat "$WORK/cli.err" >&2; fail "wiki create (parent) exited non-zero"; }
CHILD_BODY="Ward of [[Smoke Lore]], rival of [[Smoke Stub]]. smokeneedle [[map:$WIKI_MAP]]"
CHILD=$(jq -n --arg b "$CHILD_BODY" --arg p "$PARENT" '{title:"Smoke Child",body:$b,visibility:"group",parent_id:$p}' \
  | "$CLI" campaigns wiki create --id "$WIKI" --stdin 2>"$WORK/cli.err" | jq -r .id) \
  || { cat "$WORK/cli.err" >&2; fail "wiki create (child) exited non-zero"; }
"$CLI" campaigns wiki list --id "$WIKI" >"$WORK/wiki-list.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki list exited non-zero"; }
jq -e '[.[].title] | sort == ["Smoke Child", "Smoke Lore", "Smoke Stub"]' "$WORK/wiki-list.out" >/dev/null \
  || fail "the unknown [[Smoke Stub]] link should create a stub, the map embed none: $(cat "$WORK/wiki-list.out")"
ok "campaigns wiki create stubs an unknown link and leaves the embed alone"

# reading the child back expecting its body unchanged
"$CLI" campaigns wiki get --id "$WIKI" --page-id "$CHILD" \
  | jq -e --arg b "$CHILD_BODY" --arg p "$PARENT" '.body == $b and .parent_id == $p' >/dev/null \
  || fail "wiki get should return the child's body and parent"
ok "campaigns wiki get round-trips the body"

# searching for a word only the child holds, and listing titles
"$CLI" campaigns wiki search --id "$WIKI" --query smokeneedle \
  | jq -e '[.results[].title] == ["Smoke Child"]' >/dev/null \
  || fail "wiki search should find only the child"
"$CLI" campaigns wiki titles --id "$WIKI" | jq -e 'length == 3' >/dev/null \
  || fail "wiki titles should list three pages"
ok "campaigns wiki search and titles"

# renaming the parent expecting the child's link to follow
echo '{"title":"Smoke Lore Renamed"}' | "$CLI" campaigns wiki update --id "$WIKI" --page-id "$PARENT" --stdin >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki update exited non-zero"; }
"$CLI" campaigns wiki get --id "$WIKI" --page-id "$CHILD" | jq -e '.body | contains("[[Smoke Lore Renamed]]")' >/dev/null \
  || fail "renaming the parent should rewrite the child's link"
ok "campaigns wiki update renames and rewrites inbound links"

# reordering, then hiding and un-hiding the child
"$CLI" campaigns wiki reorder --id "$WIKI" --ordered-ids "$CHILD" "$PARENT" | jq -e '.ok == true' >/dev/null \
  || fail "wiki reorder should answer ok"
"$CLI" campaigns wiki hide --id "$WIKI" --page-id "$CHILD" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki hide exited non-zero"; }
"$CLI" campaigns wiki list --id "$WIKI" | jq -e --arg c "$CHILD" 'all(.[]; .id != $c)' >/dev/null \
  || fail "a hidden page should leave the default list"
"$CLI" campaigns wiki unhide --id "$WIKI" --page-id "$CHILD" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki unhide exited non-zero"; }
"$CLI" campaigns wiki list --id "$WIKI" | jq -e --arg c "$CHILD" 'any(.[]; .id == $c)' >/dev/null \
  || fail "an un-hidden page should be listed again"
ok "campaigns wiki reorder, hide and unhide"

# deleting the parent expecting the child to move to the top level
"$CLI" campaigns wiki delete --id "$WIKI" --page-id "$PARENT" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki delete exited non-zero"; }
"$CLI" campaigns wiki get --id "$WIKI" --page-id "$CHILD" | jq -e '.parent_id == null' >/dev/null \
  || fail "deleting the parent should move the child to the top level"
ok "campaigns wiki delete moves children up"

# deleting the remaining pages expecting an empty wiki again
for p in $("$CLI" campaigns wiki list --id "$WIKI" --include-hidden | jq -r '.[].id'); do
  "$CLI" campaigns wiki delete --id "$WIKI" --page-id "$p" >/dev/null 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "wiki delete (cleanup) exited non-zero"; }
done
[ "$("$CLI" campaigns wiki list --id "$WIKI" --include-hidden | jq 'length')" -eq 0 ] \
  || fail "the Smoke Wiki should be empty after cleanup"
ok "campaigns wiki pages cleaned up"

```

- [ ] **Step 2: Run the smoke test twice**

```bash
dotnet build GrimoireCli.sln
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

Expected: both end `smoke: all checks passed`. If an assertion fails against the live server, report the actual output rather than loosening it.

- [ ] **Step 3: README Commands table** — insert these rows directly after the `campaigns files upload` row:

```markdown
| `campaigns wiki list --id <campaign-id> [--mine] [--include-hidden]` | List the wiki pages you can see |
| `campaigns wiki get --id <campaign-id> --page-id <page-id>` | Get one wiki page with its body and backlinks |
| `campaigns wiki create --id <campaign-id> {--input <file> \| --stdin}` | Create a wiki page |
| `campaigns wiki update --id <campaign-id> --page-id <page-id> {--input <file> \| --stdin}` | Update a wiki page |
| `campaigns wiki delete --id <campaign-id> --page-id <page-id>` | Delete a wiki page (author only) |
| `campaigns wiki search --id <campaign-id> --query <text>` | Search wiki page titles and bodies |
| `campaigns wiki titles --id <campaign-id>` | Page ids, titles and headings for writing links |
| `campaigns wiki reorder --id <campaign-id> --ordered-ids <page-id>...` | Set the manual order of wiki pages |
| `campaigns wiki hide --id <campaign-id> --page-id <page-id>` | Hide a wiki page from your own view |
| `campaigns wiki unhide --id <campaign-id> --page-id <page-id>` | Bring back a wiki page you hid |
```

- [ ] **Step 4: API coverage** — add to `IMPLEMENTED` in `tools/generate-api-coverage.py`, next to the other campaign entries:

```python
    "GET /api/campaigns/{campaign_id}/wiki": "`campaigns wiki list` ✅",
    "POST /api/campaigns/{campaign_id}/wiki": "`campaigns wiki create` ✅",
    "GET /api/campaigns/{campaign_id}/wiki/search": "`campaigns wiki search` ✅",
    "GET /api/campaigns/{campaign_id}/wiki/titles": "`campaigns wiki titles` ✅",
    "PUT /api/campaigns/{campaign_id}/wiki/reorder": "`campaigns wiki reorder` ✅",
    "GET /api/campaigns/{campaign_id}/wiki/{page_id}": "`campaigns wiki get` ✅",
    "PATCH /api/campaigns/{campaign_id}/wiki/{page_id}": "`campaigns wiki update` ✅",
    "DELETE /api/campaigns/{campaign_id}/wiki/{page_id}": "`campaigns wiki delete` ✅",
    "POST /api/campaigns/{campaign_id}/wiki/{page_id}/hide": "`campaigns wiki hide` ✅",
    "DELETE /api/campaigns/{campaign_id}/wiki/{page_id}/hide": "`campaigns wiki unhide` ✅",
```

Regenerate: `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`
Expected: `360 operations, 185 covered, 2 internal-only`. If a key does not match a path in the spec (the generator reports it or the count is short), use the exact path string the generated markdown shows for that row.

- [ ] **Step 5: API notes** — in `docs/grimoire-api-notes.md`, add a `## Campaign wiki` section directly after the `## Campaigns` section (before the next `## ` heading after it):

```markdown
## Campaign wiki

Read from `backend/routers/campaigns/wiki.py`, `_wiki_helpers.py`, `_helpers.py`
and `wikilinks.py`, and from `frontend/src/components/campaigns/EmbedCard.jsx`,
at tag `v1.8.0`; starred items verified against the 1.8.0 stack. Backs
`campaigns wiki`.

- **No route carries a role dependency**; access is decided per page in the
  handlers. Reading needs campaign membership. Editing: the author; any member
  for a `group` page; write-shared users for a `members` page — campaign
  ownership alone grants nothing (`_wiki_helpers.py:67-87`). Deleting: the
  author only (`:89-97`). Only the author changes `visibility` or sharing.
- **A personal campaign stores every page as `gm`** whatever is sent, and
  refuses `hide` with a 409 (`wiki.py:261-327`, `:518-552`).
- **Archived campaigns refuse create, update, delete and reorder**
  (`assert_not_archived`); hide and unhide still work.
- **Saving re-parses the body.\*** An unknown *unpinned* `[[Title]]` (including
  `[[Title|label]]`) creates an empty stub page, authored by the saver, with the
  saving page's visibility (`_wiki_helpers.py:205-215`). A pinned
  `[[T:id-<missing>]]` creates none. A prefix that is not reserved —
  `[[npc:goblin]]` — is a title and gets a stub.
- **Embeds are never links.\*** `[[book:<id>]]`, `[[book:<id>:<page>]]`,
  `[[map:<id>]]`, `[[token:<id>]]`, `[[audio:<id>]]`, `[[model:<id>]]` take
  library ids; `[[image:<file-id>]]` and `[[file:<file-id>]]` take campaign file
  ids. The frontend renders them (`EmbedCard.jsx`); its picker offers only items
  linked to the campaign (`GrimoireEmbedPicker.jsx:26-28`).
- **A rename rewrites inbound `[[Old Title]]` links** in other pages
  (`rewrite_inbound_titles`).
- **Delete moves children up to the deleted page's parent**, and an unknown page
  id is a silent 204 (`wiki.py:466-515`).
- **`||text||` belongs to the campaign owner** (`_helpers.py:157-190`). `get`
  strips it for anyone else, leaving no marker; a non-owner's `||…||` is stored
  as literal text; a non-owner's save re-weaves stored secrets by position
  (`merge_gm_secrets`, `:225`).
- **Search is a plain case-insensitive substring** over title and body; hidden
  pages are excluded, and non-owners do not match on secret text
  (`wiki.py:578-617`).
- **Reorder re-deals only the slots of pages the caller can edit**; unknown,
  invisible and read-only ids are dropped and keep their slots
  (`wiki.py:99-166`).
```

- [ ] **Step 6: Final verification**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

Expected: clean format, 0 warnings / 0 errors, all tests pass, `smoke: all checks passed`.

- [ ] **Step 7: Commit**

```bash
git add docker/smoke-test.sh README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md
git commit -m "docs: document campaigns wiki and cover it in the smoke test"
```
