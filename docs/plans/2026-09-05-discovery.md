# Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the four read-only Discovery endpoints — `search`, `search fields`, `tags list`, `tags items` — as thin pass-through commands.

**Architecture:** Two command groups, each with its own command file and its own service, following the shipped groups exactly. Every response is passed through unmodified with `ConsoleOutput.WriteRawJson`. One parse-time validator, on `search --limit`.

**Tech Stack:** C# / .NET 10, `System.CommandLine`, Kiota-generated client (already generated — no regeneration needed), xUnit.

Design: [docs/specs/2026-09-05-discovery-design.md](../specs/2026-09-05-discovery-design.md).

## Global Constraints

- **Help text is transcribed from this plan, not composed.** Every command's
  description, flag descriptions, Notes lines and Examples are given verbatim
  below. Use them exactly. Do not add a line that is not in this plan.
- **Do not state anything that does not belong to the command being written.**
  No narration of what a sibling command does, no framing ("useful when…"), no
  restating a flag's description in Notes, no explaining what is already visible
  in the flag list or the response-shape sample. If a fact is not in the given
  text, it was deliberately left out.
- **No command in this plan gets `AddRoleRequired`, and no service call passes a
  `permissionHint`.** `GET /api/search` and `/api/search/fields` are
  `require_not_guest`; the two tags reads are `get_current_user`. Both are
  no-tag cases.
- **No `Choice` on any flag in this plan.** `in_use_by` and `resource_type` are
  validated server-side with a 400 naming the value set; mirroring that
  client-side is forbidden by thin pass-through.
- Responses are emitted with `ConsoleOutput.WriteRawJson(result)`; the action
  returns `0`.
- `--server` is declared per-subcommand as `new Option<string?>("--server") { Description = "Server URL override" }`
  and threaded into `CommandHelper.BuildClient(serverOverride: …)`.
- Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.
- No blank lines inside method bodies between consecutive `Subcommands.Add`
  calls, between consecutive variable declarations of the same kind, or before
  a `return` that follows setup calls.
- Conventional Commits: `type: subject`, imperative, lowercase, no period. No
  `Co-Authored-By:` and no generated-with attribution.
- `CHANGELOG.md` is owned by the release process. Do not touch it.
- Work on the existing branch `feat/discovery`. Never commit to `main`.

---

### Task 1: `search` and `search fields`

**Files:**
- Create: `src/GrimoireCli/Services/SearchService.cs`
- Create: `src/GrimoireCli/Commands/SearchCommand.cs`
- Create: `tests/GrimoireCli.Tests/Commands/SearchCommandTests.cs`
- Modify: `src/GrimoireCli/Program.cs` (registration)

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint = null, string? notFoundHint = null)`; `CommandHelper.BuildClient(string? serverOverride = null)`; `OptionHelpers.Range(string name, string description, int min, int? max = null)` returning `Option<int?>`.
- Produces: `SearchCommand.Create()` returning the `search` command.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/SearchCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SearchCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(SearchCommand.Create(), path, full);

    [Fact]
    public void SearchRequiresAQuery()
    {
        Assert.NotEmpty(SearchCommand.Create().Parse([]).Errors);
        Assert.Empty(SearchCommand.Create().Parse(["--query", "dragon"]).Errors);
    }

    [Fact]
    public void TheGroupHostsTheFieldsLeaf()
    {
        Assert.Equal(["fields"], SearchCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void FieldsParsesWithNoArguments()
    {
        Assert.Empty(SearchCommand.Create().Parse(["fields"]).Errors);
    }

    // The server declares limit as Query(50, le=200): it 422s above 200 but has
    // no lower bound, and the value reaches SQLite as a bare LIMIT, where -1
    // means unlimited. Verified live: --limit -1 returned all 126 indexed pages
    // with a 200, so the whole index comes back for a value the server accepted.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("201")]
    public void SearchRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(SearchCommand.Create().Parse(["--query", "a", "--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("200")]
    public void SearchAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(SearchCommand.Create().Parse(["--query", "a", "--limit", limit]).Errors);
    }

    // Range reads the raw token: an unconvertible one must surface as a parse
    // error rather than throwing out of Parse.
    [Fact]
    public void SearchReportsANonNumericLimitAsAParseError()
    {
        Assert.NotEmpty(SearchCommand.Create().Parse(["--query", "a", "--limit", "abc"]).Errors);
    }

    [Theory]
    [InlineData(new object[] { new[] { "search" } })]
    [InlineData(new object[] { new[] { "search", "fields" } })]
    public void EveryCommandCarriesAResponseShape(string[] path)
    {
        Assert.Contains("Response shape:", Help(path, full: true));
    }

    // Both routes are require_not_guest, which is the CLI's no-tag default.
    [Theory]
    [InlineData(new object[] { new[] { "search" } })]
    [InlineData(new object[] { new[] { "search", "fields" } })]
    public void NoCommandDeclaresARole(string[] path)
    {
        Assert.DoesNotContain("Role required:", Help(path));
    }

    // The three caveats a caller cannot infer: the other result sets ignore
    // --limit, a filter switches off page-text search, and a typo'd prefix is
    // searched literally instead of refused.
    [Fact]
    public void SearchDocumentsTheCapsTheFiltersAndTheSilentTypo()
    {
        var output = Help(["search"]);
        Assert.Contains("book_matches", output);
        Assert.Contains("text:", output);
        Assert.Contains("literally", output);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter SearchCommandTests`
Expected: FAIL — `SearchCommand` does not exist.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/SearchService.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The two search reads. Both are guarded by require_not_guest
/// (routers/search/__init__.py), so neither send names a permissionHint, and
/// neither path carries an id, so neither names a notFoundHint.
/// </summary>
public class SearchService
{
    private readonly GrimoireApiClient _client;

    public SearchService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// GET /api/search. limit bounds the page-text results only; the book,
    /// map, token and audio result sets are capped at 50 each server-side.
    /// </summary>
    public async Task<string> SearchAsync(string query, int? limit, string? bookId, string? systemId)
    {
        var info = _client.Api.Api.Search.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Q = query;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.BookId = bookId;
            c.QueryParameters.SystemId = systemId;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/search/fields.</summary>
    public async Task<string> FieldsAsync()
    {
        var info = _client.Api.Api.Search.Fields.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }
}
```

The accessor chain and every property name above were read from the generated
client and are correct as written: `Api.Api.Search` (`ApiRequestBuilder.cs:190`),
`Api.Api.Search.Fields` (`SearchRequestBuilder.cs:22`), and the query parameters
`Q`, `Limit`, `BookId`, `SystemId`. Transcribe them; do not substitute.

- [ ] **Step 4: Write the command**

Create `src/GrimoireCli/Commands/SearchCommand.cs`. Use these strings **verbatim**:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SearchCommand
{
    public static Command Create()
    {
        var queryOption = new Option<string>("--query") { Description = "Search text (2+ characters)", Required = true };
        var limitOption = OptionHelpers.Range("--limit", "Page-text results; default 50, max 200", 1, 200);
        var bookIdOption = new Option<string?>("--book-id") { Description = "Restrict to one book" };
        var systemIdOption = new Option<string?>("--system-id") { Description = "Restrict to one game system" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("search", "Search page text and metadata across the library")
        {
            queryOption, limitOption, bookIdOption, systemIdOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "book_matches, maps, tokens and audio are capped at 50 each; --limit",
            "does not raise them.",
            "",
            "field:value filters go inside --query — title:, author:, tag:,",
            "year:>2010. Any filter switches off the page-text search; text:",
            "switches it back on. search fields lists them.",
            "",
            "An unrecognised prefix is searched literally rather than refused, so",
            "a typo returns nothing. The response's fields echoes what was read",
            "as a filter.",
            "",
            "snippet carries literal <mark> HTML.",
            "",
            "--book-id and --system-id drop the map, token and audio results;",
            "--book-id also empties book_matches.");
        command.AddExamples(
            "grimoire-cli search --query \"dragon\"",
            "grimoire-cli search --query \"author:'Ben Robbins' year:>2010\"",
            "grimoire-cli search --query \"text:fireball\" --system-id <system-id>");
        command.AddResponseExample<Generated.Models.SearchResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SearchService(client);
            var result = await service.SearchAsync(
                parseResult.GetValue(queryOption)!,
                parseResult.GetValue(limitOption),
                parseResult.GetValue(bookIdOption),
                parseResult.GetValue(systemIdOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        command.Subcommands.Add(CreateFieldsCommand());
        return command;
    }

    private static Command CreateFieldsCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("fields", "The field: prefixes a search query accepts, with their aliases") { serverOption };
        command.AddExamples("grimoire-cli search fields");
        command.AddResponseExample<Generated.Models.SearchFieldsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SearchService(client);
            var result = await service.FieldsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

`fields` gets **no Notes section**. Its response documents itself.

- [ ] **Step 5: Register the command**

In `src/GrimoireCli/Program.cs`, after the `FilesCommand` line:

```csharp
rootCommand.Subcommands.Add(SearchCommand.Create());
```

- [ ] **Step 6: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: build clean, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/SearchCommand.cs src/GrimoireCli/Services/SearchService.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/SearchCommandTests.cs
git commit -m "feat: add search and search fields commands"
```

---

### Task 2: `tags list` and `tags items`

**Files:**
- Create: `src/GrimoireCli/Services/TagsService.cs`
- Create: `src/GrimoireCli/Commands/TagsCommand.cs`
- Create: `tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs`
- Modify: `src/GrimoireCli/Program.cs` (registration)

**Interfaces:**
- Consumes: the same client and helper signatures as Task 1, plus
  `command.AddShapeSection(string title, params string[] lines)` from
  `HelpExtensions`, and `JsonExamples.For(Type)` returning a newline-separated
  sample string.
- Produces: `TagsCommand.Create()` returning the `tags` command.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TagsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TagsCommand.Create(), path, full);

    [Fact]
    public void TheGroupHostsBothReads()
    {
        Assert.Equal(
            ["list", "items"],
            TagsCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(TagsCommand.Create().Parse(["list"]).Errors);
    }

    [Fact]
    public void ItemsRequiresATag()
    {
        Assert.NotEmpty(TagsCommand.Create().Parse(["items"]).Errors);
        Assert.Empty(TagsCommand.Create().Parse(["items", "--tag", "dungeon"]).Errors);
    }

    // The server validates both type flags itself, answering 400 with the value
    // set, so the CLI declares no client-side set to mirror it.
    [Theory]
    [InlineData(new object[] { new[] { "list", "--in-use-by", "sausage" } })]
    [InlineData(new object[] { new[] { "items", "--tag", "a", "--resource-type", "sausage" } })]
    public void TheTypeFlagsAreNotValidatedClientSide(string[] args)
    {
        Assert.Empty(TagsCommand.Create().Parse(args).Errors);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("items")]
    public void EveryCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["tags", leaf], full: true));
    }

    // Both reads are guarded by get_current_user, which carries no role.
    [Theory]
    [InlineData("list")]
    [InlineData("items")]
    public void NoCommandDeclaresARole(string leaf)
    {
        Assert.DoesNotContain("Role required:", Help(["tags", leaf]));
    }

    // A count with no shared-tag row behind it is otherwise unexplainable.
    [Fact]
    public void ListDocumentsTheMergedFolderTags()
    {
        Assert.Contains("Folder-derived tags", Help(["tags", "list"]));
    }

    // The generated sample renders the union as a bare list of type names, so
    // the five shapes have to be spelled out or the response is unreadable.
    [Theory]
    [InlineData("book")]
    [InlineData("map")]
    [InlineData("token")]
    [InlineData("audio")]
    [InlineData("system")]
    public void ItemsSpellsOutEveryItemShape(string itemType)
    {
        Assert.Contains($"item_type \"{itemType}\"", Help(["tags", "items"], full: true));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TagsCommandTests`
Expected: FAIL — `TagsCommand` does not exist.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/TagsService.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The two tag reads. Both are guarded by get_current_user
/// (routers/tags/core.py), which carries no role, so neither send names a
/// permissionHint. The items path carries a tag key and 404s on one that
/// matches nothing, so that send names a notFoundHint.
/// </summary>
public class TagsService
{
    private const string NotFoundHint =
        "No such tag. List the tags with: grimoire-cli tags list";

    private readonly GrimoireApiClient _client;

    public TagsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/tags. Folder-derived tags are merged into the listing server-side.</summary>
    public async Task<string> ListAsync(string? inUseBy)
    {
        var info = _client.Api.Api.Tags.ToGetRequestInformation(c =>
            c.QueryParameters.InUseBy = inUseBy);
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/tags/{internal}/items.</summary>
    public async Task<string> ItemsAsync(string tag, string? resourceType)
    {
        var info = _client.Api.Api.Tags[tag].Items.ToGetRequestInformation(c =>
            c.QueryParameters.ResourceType = resourceType);
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }
}
```

The accessor chain and every property name above were read from the generated
client and are correct as written: `Api.Api.Tags` (`ApiRequestBuilder.cs:215`),
the string indexer `Tags[tag]` (`TagsRequestBuilder.cs:24`), `.Items`
(`WithInternalItemRequestBuilder.cs:23`), and the query parameters `InUseBy` and
`ResourceType`. Transcribe them; do not substitute. The path template is
`/api/tags/{internal}/items`.

- [ ] **Step 4: Write the command**

Create `src/GrimoireCli/Commands/TagsCommand.cs`. Use these strings **verbatim**:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class TagsCommand
{
    public static Command Create()
    {
        var command = new Command("tags", "Tags across every resource type");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateItemsCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var inUseByOption = new Option<string?>("--in-use-by") { Description = "Restrict to tags used on this resource type: system, book, map, token, audio" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List tags with their usage counts")
        {
            inUseByOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folder-derived tags are merged in and counted, and category is the",
            "effective one across every type a tag appears on. --in-use-by scopes",
            "the counts to that type.");
        command.AddExamples(
            "grimoire-cli tags list",
            "grimoire-cli tags list --in-use-by book");
        command.AddResponseExample<Generated.Models.TagsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new TagsService(client);
            var result = await service.ListAsync(parseResult.GetValue(inUseByOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateItemsCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag's internal key, from tags list; matched case-insensitively", Required = true };
        var resourceTypeOption = new Option<string?>("--resource-type") { Description = "Restrict to this resource type: system, book, map, token, audio" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("items", "Items and folders carrying a tag")
        {
            tagOption, resourceTypeOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Items carrying the tag directly are in items; those inheriting it",
            "from a folder tag are under folders.");
        command.AddExamples("grimoire-cli tags items --tag dungeon");
        command.AddResponseExample<Generated.Models.TagItemsResponse>();
        AddTaggedItemShapes(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new TagsService(client);
            var result = await service.ItemsAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(resourceTypeOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    /// <summary>
    /// Spells out the five shapes the response's items arrays hold. The
    /// generated sample renders the union as a bare list of type names, which
    /// names the branches without showing any of their fields. Private rather
    /// than a HelpExtensions helper: one command calls it.
    /// </summary>
    private static void AddTaggedItemShapes(Command command)
    {
        (string Type, Type Model)[] shapes =
        [
            ("book", typeof(Generated.Models.TaggedBookItem)),
            ("map", typeof(Generated.Models.TaggedMapItem)),
            ("token", typeof(Generated.Models.TaggedTokenItem)),
            ("audio", typeof(Generated.Models.TaggedAudioItem)),
            ("system", typeof(Generated.Models.TaggedSystemItem)),
        ];
        foreach (var (type, model) in shapes)
            command.AddShapeSection($"Item shape (item_type \"{type}\")", JsonExamples.For(model).Split('\n'));
    }
}
```

- [ ] **Step 5: Register the command**

In `src/GrimoireCli/Program.cs`, after the `SearchCommand` line:

```csharp
rootCommand.Subcommands.Add(TagsCommand.Create());
```

- [ ] **Step 6: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: build clean, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/TagsCommand.cs src/GrimoireCli/Services/TagsService.cs src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs
git commit -m "feat: add tags list and tags items commands"
```

---

### Task 3: Smoke-test coverage

**Files:**
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: `search`, `search fields`, `tags list`, `tags items` from Tasks 1-2.
- Produces: nothing consumed by later tasks.

The stack is already running and seeded at `http://host.docker.internal:9481`.
Verify with `docker compose -f docker/docker-compose.yml ps` before running; do
not tear it down or reseed.

- [ ] **Step 1: Replace the raw-curl tag-items read**

`docker/smoke-test.sh` reads `/api/tags/errata-smoke/items` with a hand-rolled
`curl` and `Authorization` header, in the book-folders block — the only place in
the script where a missing command forced a workaround. Find it:

```bash
grep -n 'api/tags/errata-smoke/items' docker/smoke-test.sh
```

Replace **only** the command substitution, leaving the `jq -e` assertion and its
`ok` line exactly as they are:

```sh
TAG_ITEMS=$("$CLI" tags items --tag errata-smoke 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags items exited non-zero"; }
```

- [ ] **Step 2: Add the Discovery block**

Append a new block **after** the books batch-tag block that creates
`smoke-book-alpha` (find it with `grep -n 'smoke-book-alpha' docker/smoke-test.sh`
and place the new block after the last of those lines), so the tags it reads
already exist. Every check is read-only, which is what makes the block
idempotent.

```sh
# --- discovery ---------------------------------------------------------------
# Read-only throughout: nothing here writes, so a re-run converges trivially.
# The fixture's indexed pages all read "grimoire-cli fixture · page N", which is
# what makes an exact page-text count assertable.
SEARCH_JSON=$("$CLI" search --query "text:fixture" --limit 3 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq '.results | length')" -eq 3 ] \
  || fail "--limit should bound the page-text results: $SEARCH_JSON"
ok "search bounds page-text results with --limit"

# The rule the field syntax exists for: a metadata filter switches off the
# page-text search rather than narrowing it.
SEARCH_JSON=$("$CLI" search --query "title:dsa" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search with a filter exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq '.results | length')" -eq 0 ] \
  || fail "a metadata filter should suppress page-text results: $SEARCH_JSON"
[ "$(echo "$SEARCH_JSON" | jq '.book_matches | length')" -gt 0 ] \
  || fail "title:dsa should match the DSA fixture books: $SEARCH_JSON"
echo "$SEARCH_JSON" | jq -e '.fields | index("title") != null' >/dev/null \
  || fail "the response should echo the filter it read: $SEARCH_JSON"
ok "a metadata filter suppresses page-text search and is echoed in fields"

# A typo'd prefix is deliberately not an error, so the only signal is an empty
# fields — this is the case the help text warns about.
SEARCH_JSON=$("$CLI" search --query "titel:dsa" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search with an unknown prefix exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq '.fields | length')" -eq 0 ] \
  || fail "an unrecognised prefix should not be read as a filter: $SEARCH_JSON"
ok "an unrecognised field prefix is searched literally"

# LIMIT -1 is unlimited in SQLite and the server has no lower bound, so the
# guard has to be here.
"$CLI" search --query "fixture" --limit -1 >/dev/null 2>&1 \
  && fail "--limit -1 should be rejected before the request"
ok "search rejects a limit the server would read as unlimited"

FIELDS_JSON=$("$CLI" search fields 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search fields exited non-zero"; }
echo "$FIELDS_JSON" | jq -e '.fields[] | select(.field == "title")' >/dev/null \
  || fail "search fields should list title: $FIELDS_JSON"
ok "search fields lists the filterable fields"

TAGS_JSON=$("$CLI" tags list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags list exited non-zero"; }
echo "$TAGS_JSON" | jq -e '.tags[] | select(.internal == "smoke-book-alpha")' >/dev/null \
  || fail "tags list should include the tag just written: $TAGS_JSON"
ok "tags list includes a tag written earlier in this run"

TAGS_JSON=$("$CLI" tags list --in-use-by book 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags list --in-use-by exited non-zero"; }
echo "$TAGS_JSON" | jq -e '.tags[] | select(.internal == "smoke-book-alpha")' >/dev/null \
  || fail "--in-use-by book should keep a book tag: $TAGS_JSON"
ok "tags list narrows to one resource type"

"$CLI" tags items --tag no-such-tag-smoke >/dev/null 2>&1 \
  && fail "tags items should exit non-zero for an unknown tag"
ok "tags items reports an unknown tag"
```

- [ ] **Step 3: Run the smoke test**

```bash
bash docker/smoke-test.sh
```
Expected: exits 0. Count the checks yourself and report the number:
```bash
bash docker/smoke-test.sh 2>&1 | grep -c 'ok:'
```
The baseline before this task is 111. Report the actual number the command
prints — do not compute it by adding.

- [ ] **Step 4: Run it a second time**

```bash
bash docker/smoke-test.sh
```
Expected: exits 0 again with the same count. A block that only converges on the
first run is a defect.

- [ ] **Step 5: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the discovery commands in the smoke test"
```

---

### Task 4: Documentation

**Files:**
- Modify: `tools/generate-api-coverage.py`
- Modify: `docs/grimoire-api-coverage.md` (regenerated, never hand-edited)
- Modify: `README.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docs/roadmap.md`

**Interfaces:**
- Consumes: the command names from Tasks 1-2.
- Produces: nothing.

- [ ] **Step 1: Add the four routes to the coverage generator**

In `tools/generate-api-coverage.py`, add to the `IMPLEMENTED` dict, placed
beside the other routes of their tag:

```python
    "GET /api/search": "`search` ✅",
    "GET /api/search/fields": "`search fields` ✅",
    "GET /api/tags": "`tags list` ✅",
    "GET /api/tags/{internal}/items": "`tags items` ✅",
```

- [ ] **Step 2: Regenerate the coverage table**

```bash
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```
Expected: exactly the four rows change from `—` to a command name, and the
counts in the summary move by four. Do not edit the markdown by hand.

- [ ] **Step 3: Add the README rows**

In the Commands table in `README.md`, matching the existing row format:

```
| `search --query <q> [--limit <n>] [--book-id <id>] [--system-id <id>]` | Search page text and metadata across the library |
| `search fields` | The `field:` prefixes a search query accepts |
| `tags list [--in-use-by <type>]` | List tags with their usage counts |
| `tags items --tag <key> [--resource-type <type>]` | Items and folders carrying a tag |
```

- [ ] **Step 4: Record the verified behaviour in the API notes**

Append a `## Search` section to `docs/grimoire-api-notes.md`, matching the
existing sections' style:

```markdown
## Search

`GET /api/search` is not full-text-only. As of 1.6.1 it returns five
independently-populated arrays — `results` (page text), `book_matches`, `maps`,
`tokens`, `audio` — and `total` counts every row across all of them, so a book
matching by title *and* by page text is counted twice.

**`limit` bounds `results` alone.** `book_matches` is capped at
`TITLE_MATCH_LIMIT = 50` and each media set at a literal `.limit(50)`, neither
reachable from the query string. There is no offset, so those four are a hard
ceiling rather than a page.

**`limit` has no lower bound, and `-1` means unlimited.** The route declares
`Query(50, le=200)`, which 422s above 200, but the value reaches SQLite as a bare
`LIMIT :limit`. Verified against a 126-page index: `limit=-1` returned all 126
rows with a 200, and `limit=0` returned none. The CLI guards this with
`OptionHelpers.Range(1, 200)`.

**An unrecognised `field:` prefix is not an error.** It falls through to free
text and is searched literally, because a colon is ordinary punctuation in a
title. `q=titel:dsa` answers 200 with every array empty and `fields: []`, which
is the only way to tell a typo from a genuine miss. A recognised metadata filter
suppresses the page-text search; `text:` forces it back.

**`campaigns/resources/search` has no command, deliberately.** It is a resource
picker: `q` is optional so it enumerates a whole type, and its cap is 20000 per
type rather than 50. That makes it the only way to list maps, tokens or audio
without a search term — but those resource types have no commands yet, and each
will arrive with its own paginated `list`. For books, `books list` already
enumerates with `--offset` and `search` already filters. Do not add a command for
it on a coverage-gap sweep; revisit it with the maps, tokens and audio blocks.
```

- [ ] **Step 5: Update the roadmap**

Two edits to `docs/roadmap.md`:

1. Delete the whole `1. **Discovery** — …` item from the MVP list, including its
   explanatory paragraph. The roadmap lists intended work only; a shipped item
   leaves.
2. Delete the `**search-full-text**` bullet from the `## Later` section — that is
   what shipped.

Leave everything else in both sections untouched. Do not add a line noting that
Discovery shipped: that belongs in git, not the roadmap. Do not touch
`CHANGELOG.md`.

- [ ] **Step 6: Verify nothing else drifted**

```bash
git diff --stat
```
Expected: exactly the five files above.

- [ ] **Step 7: Commit**

```bash
git add tools/generate-api-coverage.py docs/grimoire-api-coverage.md README.md docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record the discovery commands and their verified behaviour"
```

---

## Final verification

After Task 4, run all four pre-PR checks:

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```
