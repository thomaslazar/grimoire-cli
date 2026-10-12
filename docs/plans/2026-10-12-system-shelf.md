# System Shelf Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `systems books list | folder-counts | facets` over Grimoire 1.8.0's system shelf routes, move `systems book-folders` to `systems books folders` as a marked breaking change, and teach the release tooling to surface breaking changes.

**Architecture:** A new `systems books` subgroup (`SystemBookCommands.cs`) with its own `SystemBooksService`; the two filtered routes share one `ShelfBrowseOptions` (flags + help) and `ShelfBrowseFilter` (values), mirroring the server's single `book_browse_params`. The existing `BookFolderCommands` is re-registered under the subgroup as `folders`, its service methods untouched.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client (`src/GrimoireCli/Generated/`), xUnit, bash smoke test against the local Grimoire 1.8.0 stack.

**Spec:** [docs/specs/2026-10-12-system-shelf-design.md](../specs/2026-10-12-system-shelf-design.md)

## Global Constraints

- Target Grimoire **1.8.0**; `MinSupportedVersion` / `MaxTestedVersion` do not change.
- Commands: `systems books list --id`, `systems books folder-counts --id`, `systems books facets --id`, `systems books folders list|set|delete` (moved from `systems book-folders`, flags and behaviour unchanged). No alias for the old name.
- Shelf filter flags mirror `book_browse_params` exactly: `--query`, `--tags`, `--favorites`, `--added-since`, `--explicit true|false`, `--genre`, `--product-code`, `--category`, `--folder`, `--sort title|year|page_count|size|product_code|added_at`, `--order asc|desc`. `list` adds `--limit` (1–500, default 100) and `--offset`; `facets` takes `--id` only.
- No role tag on the three new commands (routes are `require_not_guest`, the default). The moved `folders set|delete` keep their `gm or admin` tag.
- stdout is the server's bytes unmodified (`ConsoleOutput.WriteRawJson`).
- One file per command group: new subgroup in its own command file and its own service.
- Never hand-edit `src/GrimoireCli/Generated/`.
- Run `dotnet format GrimoireCli.sln` after editing C#. No blank lines between consecutive `Subcommands.Add`/option declarations or before a `return` after setup.
- Conventional Commits, lowercase imperative subject, **no `Co-Authored-By:` or any Claude/"Generated with" attribution**. The move commit is exactly the `refactor!:` message with the `BREAKING CHANGE:` footer given in Task 3.
- Do not edit `CHANGELOG.md` or `docs/roadmap.md`.
- Smoke additions are read-only. Local stack: `GRIMOIRE_SERVER=http://host.docker.internal:9481`; do not reset or re-seed it.

## Server facts the code and help rely on

Verified in `temp/grimoire` at `v1.8.0` (`backend/`) and live on 2026-10-12, fixture system `Das Schwarze Auge 5 DE` (two books in `core/`, `DSA5 Errata` in `core/errata/`):

- `/books` → `{total, books[]}`, `limit` 1–500 default 100 (`routers/systems/books.py:401-402`). Live: total 3; `category=core&folder=errata` → `["DSA5 Errata"]`.
- `/book-groups` → `{total, groups:[{category, path, dir, count, first}]}`; `count` direct only; `first` the active sort's value for the folder's first book, null under `title` (`books.py:281-323`). Live: `core`/`""`=2, `core`/`errata`=1.
- `/book-facets` → `{categories, genres, product_code_prefixes, tags}` over every visible book, no filters (`books.py:338-380`); prefixes are the leading ASCII letters of each code, upper-cased. Live: `categories == ["core"]`.
- `folder` without `category` → 400 (`books.py:112`); unknown `sort` → 422 (`:110`).
- `--tags` matches the book's own tags only (`books.py:218`, `folder_tags=False`). `genre` exact case-insensitive, `product_code` prefix; both take `__grim:none__`/`__grim:any__` (`books.py:161-181`). `added_since`: naive = UTC, undated never match (`:208-212`). No-year / undated books sort last either way.
- Bare query text matches title, filename or product code (`routers/search/_books.py:137-149`); fields are the book search fields (`services/search_query.py:86-100`).

## File Structure

| File | Change |
|---|---|
| `.claude/skills/release/SKILL.md`, `docs/releasing.md`, `CLAUDE.md` | breaking-change detection and notes (Task 1) |
| `src/GrimoireCli/Services/ShelfBrowseFilter.cs` | **new** record of shelf filter values |
| `src/GrimoireCli/Services/SystemBooksService.cs` | **new** — the three shelf routes |
| `src/GrimoireCli/Commands/ShelfBrowseOptions.cs` | **new** — shelf flags + help sections |
| `src/GrimoireCli/Commands/SystemBookCommands.cs` | **new** — `systems books` subgroup |
| `src/GrimoireCli/Commands/SystemsCommand.cs` | register `books`; drop `book-folders` (Task 3) |
| `src/GrimoireCli/Commands/BookFolderCommands.cs` | group renamed `folders` (Task 3) |
| `tests/GrimoireCli.Tests/Commands/SystemBookCommandTests.cs` | **new** |
| `tests/GrimoireCli.Tests/Services/SystemBooksServiceTests.cs` | **new** |
| rename sweep (Task 3) | `BookFolderCommandTests.cs`, `RoleSectionTests.cs`, `SystemsService.cs`, `MapsService.cs`, `FilesFolderCommands.cs`, `MapFolderCommands.cs`, `README.md`, `docker/smoke-test.sh`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`, `docs/grimoire-compatibility.md` |
| `docker/smoke-test.sh`, `README.md`, coverage, `docs/grimoire-api-notes.md` | shelf docs + smoke (Task 4) |

---

### Task 0: Branch and commit the design

- [ ] **Step 1:** `git checkout main && git pull && git checkout -b feat/system-shelf`
- [ ] **Step 2:** Commit spec and plan (both untracked in the working tree):

```bash
git add docs/specs/2026-10-12-system-shelf-design.md docs/plans/2026-10-12-system-shelf.md
git commit -m "docs: design system shelf commands"
```

---

### Task 1: Release tooling recognises breaking changes

Docs-only. No C#.

**Files:**
- Modify: `.claude/skills/release/SKILL.md` (Step 1 "Determine the version number" block; Step 4 summary, the Changes `grep`, and the notes template)
- Modify: `docs/releasing.md` (`## Release notes` list, `## Versioning`)
- Modify: `CLAUDE.md` (`## Git Conventions`)

- [ ] **Step 1: SKILL.md, Step 1.** Replace this block:

```markdown
Determine the version number:
- Get the last tag: `git describe --tags --abbrev=0 2>/dev/null || echo "none"`
- Read commits since last tag
- Propose a version based on conventional commits:
  - Any `feat:` commits → bump MINOR
  - Only `fix:`, `docs:`, `test:`, `ci:`, `chore:` → bump PATCH
  - Grimoire compatibility bumps with no CLI changes → PATCH
  - See `docs/releasing.md` for versioning rules

**GATE: Ask the human to confirm the version number.** Show them the proposed
version and the commit summary. Wait for their response.
```

with:

````markdown
Determine the version number:
- Get the last tag: `git describe --tags --abbrev=0 2>/dev/null || echo "none"`
- Read commits since last tag
- Find breaking changes — a `type!:` subject or a `BREAKING CHANGE:` footer:

  ```bash
  LAST_TAG=$(git describe --tags --abbrev=0)
  git log "${LAST_TAG}..HEAD" --format='%h %s' | grep -E '^[0-9a-f]+ [a-z]+(\([^)]*\))?!:'
  git log "${LAST_TAG}..HEAD" --grep='^BREAKING CHANGE:' --format='%h %s%n%b'
  ```

- Propose a version based on conventional commits:
  - Any breaking change → bump MINOR at least; pre-1.0 a breaking change never
    ships in a patch
  - Any `feat:` commits → bump MINOR
  - Only `fix:`, `docs:`, `test:`, `ci:`, `chore:` → bump PATCH
  - Grimoire compatibility bumps with no CLI changes → PATCH
  - See `docs/releasing.md` for versioning rules

**GATE: Ask the human to confirm the version number.** Show them the proposed
version, the commit summary, and every breaking change found — or say there
are none. Wait for their response.
````

- [ ] **Step 2: SKILL.md, Step 4 summary.** Directly after the line `An opening paragraph naming the kind of release and its theme, then:` insert:

```markdown

**Breaking changes** — only when Step 1 found any, and placed before
Highlights: one bullet per breaking change, quoting its `BREAKING CHANGE:`
footer (or, for a `!` subject with no footer, the subject). The opening
paragraph then names the count — "Minor release with one breaking change."
```

- [ ] **Step 3: SKILL.md, Changes filter.** In the Changes bash block, change

```bash
git log --oneline $RANGE --pretty="- %s" | grep -E "^- (feat|fix|refactor|docs|test|ci|chore):" | sort
```

to

```bash
git log --oneline $RANGE --pretty="- %s" | grep -E "^- (feat|fix|refactor|docs|test|ci|chore)!?:" | sort
```

and directly after that code block's closing fence add the line:

```markdown
A `type!:` commit groups with its type and keeps its `!`.
```

- [ ] **Step 4: SKILL.md, notes template.** In the `temp/release-notes.md` template, between `{Kind of release, and its theme, in two or three lines.}` and `### Highlights`, insert:

```markdown
### Breaking changes

- `old form` is now `new form`; what a caller must change. (Only when Step 1
  found breaking changes — omit the section otherwise.)

```

- [ ] **Step 5: docs/releasing.md, Release notes.** In the numbered list under `## Release notes`, directly after item 1 (the `## v{version} — YYYY-MM-DD` paragraph item), insert a new item and renumber the following items by one:

```markdown
2. `### Breaking changes` — only when the release carries one: a bullet per
   breaking change quoting its `BREAKING CHANGE:` footer, before Highlights,
   and the opening paragraph names the count ("Minor release with one
   breaking change"). The release skill finds them; the reader should not
   have to.
```

- [ ] **Step 6: docs/releasing.md, Versioning.** Append to the `## Versioning` section:

```markdown

**Breaking changes** — anything that can break an existing caller: a renamed
or removed command or flag, or a changed output shape. Pre-1.0 one ships only
in a minor release, never a patch. Mark the commit with `!` after the type and
a `BREAKING CHANGE:` footer naming the old and the new form, as abs-cli does:

    refactor!: move systems book-folders under systems books

    BREAKING CHANGE: systems book-folders list|set|delete is now
    systems books folders list|set|delete; flags and behaviour are
    unchanged.

The release skill finds both markers, holds the version to at least a minor,
and lists each under Breaking changes in the release notes.
```

- [ ] **Step 7: CLAUDE.md, Git Conventions.** Directly after the line ``- Types: `feat`, `fix`, `docs`, `test`, `ci`, `refactor`, `chore` `` add:

```markdown
- **Breaking change:** `!` after the type (`refactor!: …`) and a `BREAKING CHANGE:` footer naming the old and the new form. The release skill reads both; see [docs/releasing.md](docs/releasing.md#versioning).
```

- [ ] **Step 8: Verify the detection commands on a throwaway repo.**

```bash
T=$(mktemp -d) && cd "$T" && git init -q && git commit -q --allow-empty -m "chore: start" && git tag v0.0.1 \
  && git commit -q --allow-empty -m "feat: plain" \
  && git commit -q --allow-empty -m "refactor!: bang only" \
  && git commit -q --allow-empty -m "fix: footer only" -m "BREAKING CHANGE: old is now new" \
  && LAST_TAG=$(git describe --tags --abbrev=0) \
  && git log "${LAST_TAG}..HEAD" --format='%h %s' | grep -E '^[0-9a-f]+ [a-z]+(\([^)]*\))?!:' \
  && git log "${LAST_TAG}..HEAD" --grep='^BREAKING CHANGE:' --format='%h %s%n%b' \
  && git log --oneline "${LAST_TAG}..HEAD" --pretty="- %s" | grep -E "^- (feat|fix|refactor|docs|test|ci|chore)!?:" | sort; cd - >/dev/null; rm -rf "$T"
```

Expected: the first grep prints the `refactor!: bang only` line; the `--grep` prints `fix: footer only` with its footer and not the others; the Changes filter prints all three (including `- refactor!: bang only`). If any differs, fix the command in the docs to match what works and note it in the report.

- [ ] **Step 9: Commit**

```bash
git add .claude/skills/release/SKILL.md docs/releasing.md CLAUDE.md
git commit -m "docs: surface breaking changes in the release process"
```

---

### Task 2: `systems books list | folder-counts | facets`

**Files:**
- Create: `src/GrimoireCli/Services/ShelfBrowseFilter.cs`
- Create: `src/GrimoireCli/Services/SystemBooksService.cs`
- Create: `src/GrimoireCli/Commands/ShelfBrowseOptions.cs`
- Create: `src/GrimoireCli/Commands/SystemBookCommands.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs` (registration)
- Create: `tests/GrimoireCli.Tests/Commands/SystemBookCommandTests.cs`
- Create: `tests/GrimoireCli.Tests/Services/SystemBooksServiceTests.cs`

**Interfaces:**
- Produces: `ShelfBrowseFilter(string? Query, string? Tags, bool? Favorites, string? AddedSince, bool? Explicit, string? Genre, string? ProductCode, string? Category, string? Folder, string? Sort, string? Order)`; `SystemBooksService` with `ListAsync(string systemId, int? limit, int? offset, ShelfBrowseFilter filter)`, `FolderCountsAsync(string systemId, ShelfBrowseFilter filter)`, `FacetsAsync(string systemId)` (all `Task<string>`) and internal `ListRequest`, `FolderCountsRequest`, `FacetsRequest` with the same parameters returning `RequestInformation`; `SystemBookCommands.Create()` returning the `books` `Command` (Task 3 adds `BookFolderCommands.Create()` to it).

- [ ] **Step 1: Write the failing command tests** — create `tests/GrimoireCli.Tests/Commands/SystemBookCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SystemBookCommandTests
{
    private static RootCommand Root() => new() { SystemsCommand.Create() };

    private static string Help(string[] path) =>
        HelpRenderer.Render(SystemsCommand.Create(), path, false);

    private static Command Books() =>
        SystemsCommand.Create().Subcommands.Single(c => c.Name == "books");

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    [InlineData("facets")]
    public void TheBooksSubgroupHostsTheShelfCommands(string name)
        => Assert.Contains(name, Books().Subcommands.Select(c => c.Name));

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    [InlineData("facets")]
    public void EachShelfCommandNeedsAnId(string name)
        => Assert.NotEmpty(Root().Parse(["systems", "books", name]).Errors);

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    public void TheFilteredCommandsTakeEveryShelfFilter(string name)
    {
        Assert.Empty(Root().Parse(
            ["systems", "books", name, "--id", "sys-1", "--query", "author:Kiesow", "--tags", "[\"x\"]",
             "--favorites", "--added-since", "2026-01-01T00:00:00Z", "--explicit", "false",
             "--genre", "__grim:none__", "--product-code", "PZO", "--category", "core",
             "--folder", "errata", "--sort", "product_code", "--order", "desc"]).Errors);
    }

    [Theory]
    [InlineData("list", "--sort", "name")]
    [InlineData("list", "--order", "up")]
    [InlineData("folder-counts", "--sort", "name")]
    [InlineData("folder-counts", "--order", "up")]
    public void AnUnknownSortOrOrderIsRejected(string name, string flag, string value)
        => Assert.NotEmpty(Root().Parse(["systems", "books", name, "--id", "sys-1", flag, value]).Errors);

    [Theory]
    [InlineData("0")]
    [InlineData("501")]
    public void ListBoundsTheLimit(string limit)
        => Assert.NotEmpty(Root().Parse(["systems", "books", "list", "--id", "sys-1", "--limit", limit]).Errors);

    [Fact]
    public void ListPagesWithLimitAndOffset()
        => Assert.Empty(Root().Parse(["systems", "books", "list", "--id", "sys-1", "--limit", "500", "--offset", "100"]).Errors);

    [Fact]
    public void FacetsTakesNoFilters()
        => Assert.NotEmpty(Root().Parse(["systems", "books", "facets", "--id", "sys-1", "--category", "core"]).Errors);

    [Fact]
    public void FolderNeedsCategoryIsSaid()
        => Assert.Contains("needs --category", Help(["systems", "books", "list"]));

    [Fact]
    public void TagsMatchOnlyTheBooksOwnTagsIsSaid()
        => Assert.Contains("book's own tags only", Help(["systems", "books", "list"]));

    [Fact]
    public void TheQuerySyntaxIsTaught()
        => Assert.Contains("Query syntax", Help(["systems", "books", "folder-counts"]));

    [Fact]
    public void FolderCountsSaysCountsAreDirectOnly()
        => Assert.Contains("not its subfolders", Help(["systems", "books", "folder-counts"]));

    [Fact]
    public void FacetsSaysItIgnoresFilters()
        => Assert.Contains("before any filter", Help(["systems", "books", "facets"]));

    [Theory]
    [InlineData("list")]
    [InlineData("folder-counts")]
    [InlineData("facets")]
    public void TheShelfCommandsCarryNoRoleTag(string name)
        => Assert.DoesNotContain("Role required:", Help(["systems", "books", name]));
}
```

- [ ] **Step 2: Write the failing service test** — create `tests/GrimoireCli.Tests/Services/SystemBooksServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins the three shelf requests to their own routes under the system, and
/// checks the shelf filters reach the query string under the server's names.
/// </summary>
public class SystemBooksServiceTests
{
    private static readonly ShelfBrowseFilter NoFilter =
        new(null, null, null, null, null, null, null, null, null, null, null);

    private static SystemBooksService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListResolvesToTheBooksRoute()
        => Assert.Equal("http://example.test/api/systems/sys-1/books",
            Uri(Service().ListRequest("sys-1", null, null, NoFilter)));

    [Fact]
    public void FolderCountsResolvesToTheBookGroupsRoute()
        => Assert.Equal("http://example.test/api/systems/sys-1/book-groups",
            Uri(Service().FolderCountsRequest("sys-1", NoFilter)));

    [Fact]
    public void FacetsResolvesToTheBookFacetsRoute()
        => Assert.Equal("http://example.test/api/systems/sys-1/book-facets",
            Uri(Service().FacetsRequest("sys-1")));

    [Fact]
    public void ListSendsPagingAndEveryFilter()
    {
        var uri = Uri(Service().ListRequest("sys-1", 50, 100, new ShelfBrowseFilter(
            "kiesow", null, true, null, false, "fantasy", "PZO", "core", "errata", "product_code", "desc")));
        foreach (var part in new[] { "limit=50", "offset=100", "q=kiesow", "favorites=true", "explicit=false",
                     "genre=fantasy", "product_code=PZO", "category=core", "folder=errata",
                     "sort=product_code", "order=desc" })
            Assert.Contains(part, uri);
    }

    [Fact]
    public void FolderCountsSendsTheSortThatDrivesFirst()
    {
        var uri = Uri(Service().FolderCountsRequest("sys-1", NoFilter with { Sort = "year", Order = "asc" }));
        Assert.Contains("sort=year", uri);
        Assert.Contains("order=asc", uri);
    }
}
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: build errors — `ShelfBrowseFilter` / `SystemBooksService` / no `books` subcommand.

- [ ] **Step 4: Create `src/GrimoireCli/Services/ShelfBrowseFilter.cs`:**

```csharp
namespace GrimoireCli.Services;

/// <summary>The query parameters a system's shelf routes share (<c>book_browse_params</c>).</summary>
public sealed record ShelfBrowseFilter(
    string? Query,
    string? Tags,
    bool? Favorites,
    string? AddedSince,
    bool? Explicit,
    string? Genre,
    string? ProductCode,
    string? Category,
    string? Folder,
    string? Sort,
    string? Order);
```

- [ ] **Step 5: Create `src/GrimoireCli/Services/SystemBooksService.cs`:**

```csharp
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The three routes behind `systems books`: a system's shelf, browsed a page at
/// a time. Each 404s when the system is missing or hidden from the caller.
/// </summary>
public class SystemBooksService
{
    private const string NotFoundHint = "No system with that ID. List them with: grimoire-cli systems list";

    private readonly GrimoireApiClient _client;

    public SystemBooksService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/systems/{id}/books. A page of the shelf, full book records.</summary>
    public async Task<string> ListAsync(string systemId, int? limit, int? offset, ShelfBrowseFilter filter)
        => await _client.SendAsync(ListRequest(systemId, limit, offset, filter), notFoundHint: NotFoundHint);

    /// <summary>GET /api/systems/{id}/book-groups. Each directory holding matching books, with its direct count.</summary>
    public async Task<string> FolderCountsAsync(string systemId, ShelfBrowseFilter filter)
        => await _client.SendAsync(FolderCountsRequest(systemId, filter), notFoundHint: NotFoundHint);

    /// <summary>GET /api/systems/{id}/book-facets. The filter values the whole visible shelf offers.</summary>
    public async Task<string> FacetsAsync(string systemId)
        => await _client.SendAsync(FacetsRequest(systemId), notFoundHint: NotFoundHint);

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation ListRequest(string systemId, int? limit, int? offset, ShelfBrowseFilter filter)
        => _client.Api.Api.Systems[systemId].Books.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
            c.QueryParameters.Q = filter.Query;
            c.QueryParameters.Tags = filter.Tags;
            c.QueryParameters.Favorites = filter.Favorites;
            c.QueryParameters.AddedSince = filter.AddedSince;
            c.QueryParameters.Explicit = filter.Explicit;
            c.QueryParameters.Genre = filter.Genre;
            c.QueryParameters.ProductCode = filter.ProductCode;
            c.QueryParameters.Category = filter.Category;
            c.QueryParameters.Folder = filter.Folder;
            c.QueryParameters.Sort = filter.Sort;
            if (filter.Order is not null)
                c.QueryParameters.OrderAsGetOrderQueryParameterType =
                    Enum.Parse<Generated.Api.Systems.Item.Books.GetOrderQueryParameterType>(filter.Order, true);
        });

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation FolderCountsRequest(string systemId, ShelfBrowseFilter filter)
        => _client.Api.Api.Systems[systemId].BookGroups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Q = filter.Query;
            c.QueryParameters.Tags = filter.Tags;
            c.QueryParameters.Favorites = filter.Favorites;
            c.QueryParameters.AddedSince = filter.AddedSince;
            c.QueryParameters.Explicit = filter.Explicit;
            c.QueryParameters.Genre = filter.Genre;
            c.QueryParameters.ProductCode = filter.ProductCode;
            c.QueryParameters.Category = filter.Category;
            c.QueryParameters.Folder = filter.Folder;
            c.QueryParameters.Sort = filter.Sort;
            if (filter.Order is not null)
                c.QueryParameters.OrderAsGetOrderQueryParameterType =
                    Enum.Parse<Generated.Api.Systems.Item.BookGroups.GetOrderQueryParameterType>(filter.Order, true);
        });

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation FacetsRequest(string systemId)
        => _client.Api.Api.Systems[systemId].BookFacets.ToGetRequestInformation();
}
```

- [ ] **Step 6: Create `src/GrimoireCli/Commands/ShelfBrowseOptions.cs`:**

```csharp
using System.CommandLine;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The filter and sort flags of systems books list and folder-counts. Shared
/// because the server declares them once for both routes, in
/// <c>routers/systems/books.py</c> (<c>book_browse_params</c>).
/// </summary>
internal sealed class ShelfBrowseOptions
{
    private static readonly string[] Sorts = ["title", "year", "page_count", "size", "product_code", "added_at"];
    private static readonly string[] Orders = ["asc", "desc"];

    private readonly Option<string?> _query = new("--query") { Description = "Search text; see Query syntax" };
    private readonly Option<string?> _tags = new("--tags") { Description = "Tag filter as JSON; see Filters" };
    private readonly Option<bool> _favorites = new("--favorites") { Description = "Only the caller's favorites" };
    private readonly Option<string?> _addedSince = new("--added-since") { Description = "Only books added at or after this ISO 8601 time" };
    private readonly Option<bool?> _explicit = new("--explicit") { Description = "Keep only books with this explicit flag (true | false)" };
    private readonly Option<string?> _genre = new("--genre") { Description = "Only books with this genre" };
    private readonly Option<string?> _productCode = new("--product-code") { Description = "Only books whose product code starts with this" };
    private readonly Option<string?> _category = new("--category") { Description = "Only books in this category (core, supplement, adventure, …)" };
    private readonly Option<string?> _folder = new("--folder") { Description = "Only books directly in this subfolder of --category" };
    private readonly Option<string?> _sort = OptionHelpers.Choice("--sort", "Row order; default title", Sorts);
    private readonly Option<string?> _order = OptionHelpers.Choice("--order", "Sort direction; default asc", Orders);

    /// <summary>Adds the flags and the help that teaches them.</summary>
    public void AddTo(Command command)
    {
        command.Options.Add(_query);
        command.Options.Add(_tags);
        command.Options.Add(_favorites);
        command.Options.Add(_addedSince);
        command.Options.Add(_explicit);
        command.Options.Add(_genre);
        command.Options.Add(_productCode);
        command.Options.Add(_category);
        command.Options.Add(_folder);
        command.Options.Add(_sort);
        command.Options.Add(_order);
        command.AddHelpSection("Filters", HelpSectionPosition.Top,
            "--tags matches the book's own tags only, not folder tags. A JSON list:",
            "flat strings must all match, [\"core\",\"pdf\"]; groups OR their tags",
            "and must all hold, exclude meaning none of:",
            "[{\"mode\":\"include\",\"tags\":[\"a\",\"b\"]},{\"mode\":\"exclude\",\"tags\":[\"c\"]}]",
            "",
            "--genre (exact, case-insensitive) and --product-code (a prefix) also",
            "take __grim:none__ / __grim:any__ for books with none / any.",
            "",
            "--category is the normalised value and case-sensitive. --folder is the",
            "subfolder below the category directory, \"\" for the category's own; it",
            "needs --category.",
            "",
            "Undated books never match --added-since; a time without an offset is",
            "UTC. Books with no year or date sort last either way.");
        command.AddHelpSection("Query syntax", HelpSectionPosition.Top,
            "Bare text matches the title, filename or product code. Fields, aliases",
            "in parens: title (name), filename (file), author (authors), artist",
            "(artists), publisher, system (game), category, tag (tags), year, isbn,",
            "code (sku, product_code), language (lang), description (desc).",
            "",
            "Different fields narrow, repeating one widens. Quote a multi-word",
            "value: author:\"Ulrich Kiesow\".");
    }

    public ShelfBrowseFilter Read(ParseResult parseResult) => new(
        parseResult.GetValue(_query),
        parseResult.GetValue(_tags),
        parseResult.GetValue(_favorites) ? true : null,
        parseResult.GetValue(_addedSince),
        parseResult.GetValue(_explicit),
        parseResult.GetValue(_genre),
        parseResult.GetValue(_productCode),
        parseResult.GetValue(_category),
        parseResult.GetValue(_folder),
        parseResult.GetValue(_sort),
        parseResult.GetValue(_order));
}
```

- [ ] **Step 7: Create `src/GrimoireCli/Commands/SystemBookCommands.cs`:**

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SystemBookCommands
{
    public static Command Create()
    {
        var command = new Command("books", "A system's books, a page at a time");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateFolderCountsCommand());
        command.Subcommands.Add(CreateFacetsCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var limitOption = OptionHelpers.Range("--limit", "Results per page", 1, 500);
        limitOption.DefaultValueFactory = _ => 100;
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var shelf = new ShelfBrowseOptions();
        var command = new Command("list", "A page of a system's books, with their metadata")
        {
            idOption, limitOption, offsetOption
        };
        shelf.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Page with --offset against total in the response.",
            "",
            "Variants are hidden; the account's explicit permission and access",
            "settings filter the shelf server-side.");
        command.AddExamples(
            "grimoire-cli systems books list --id <system-id>",
            "grimoire-cli systems books list --id <system-id> --category core --folder errata",
            "grimoire-cli systems books list --id <system-id> --genre __grim:none__ --limit 500");
        command.AddResponseExample<Generated.Models.SystemBooksPage>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemBooksService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                shelf.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFolderCountsCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var shelf = new ShelfBrowseOptions();
        var command = new Command("folder-counts", "Directories holding a system's matching books, with counts")
        {
            idOption
        };
        shelf.AddTo(command);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "One row per directory: count covers the books directly in it,",
            "not its subfolders. path is the subfolder below the category directory",
            "(\"\" for the category's own); dir is library-relative. Two directories",
            "can share a category and path.",
            "",
            "first is the active --sort's value for the folder's first book; null",
            "under title.",
            "",
            "The account's explicit permission and access settings filter the counts.");
        command.AddExamples(
            "grimoire-cli systems books folder-counts --id <system-id>",
            "grimoire-cli systems books folder-counts --id <system-id> --genre __grim:none__");
        command.AddResponseExample<Generated.Models.ShelfGroupsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemBooksService(client);
            var result = await service.FolderCountsAsync(
                parseResult.GetValue(idOption)!,
                shelf.Read(parseResult));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFacetsCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var command = new Command("facets", "The categories, genres, code prefixes and tags a system's books use")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Values come from every book on the system the account can see,",
            "before any filter. product_code_prefixes are each code's leading",
            "letters, upper-cased.");
        command.AddExamples("grimoire-cli systems books facets --id <system-id>");
        command.AddResponseExample<Generated.Models.ShelfFacetsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemBooksService(client);
            var result = await service.FacetsAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 8: Register the subgroup** — in `src/GrimoireCli/Commands/SystemsCommand.cs`, insert `command.Subcommands.Add(SystemBookCommands.Create());` directly before `command.Subcommands.Add(BookFolderCommands.Create());`.

- [ ] **Step 9: Format, test, check help**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet build GrimoireCli.sln
for c in list folder-counts facets; do src/GrimoireCli/bin/Debug/net10.0/grimoire-cli systems books $c --help; done
```

Expected: all tests pass; `list` and `folder-counts` show Notes, Filters and Query syntax sections and the eleven shelf flags; `facets` shows only `--id`. If a help-sample test (e.g. one that checks every `AddResponseExample` type has a sample) fails, the samples already exist in `JsonExamples.g.cs` for all three models — investigate rather than regenerating blindly.

- [ ] **Step 10: Commit**

```bash
git add src/GrimoireCli/Services/ShelfBrowseFilter.cs src/GrimoireCli/Services/SystemBooksService.cs \
  src/GrimoireCli/Commands/ShelfBrowseOptions.cs src/GrimoireCli/Commands/SystemBookCommands.cs \
  src/GrimoireCli/Commands/SystemsCommand.cs tests/GrimoireCli.Tests/Commands/SystemBookCommandTests.cs \
  tests/GrimoireCli.Tests/Services/SystemBooksServiceTests.cs
git commit -m "feat: add systems books list, folder-counts and facets"
```

---

### Task 3: Move `systems book-folders` to `systems books folders` (breaking)

**Files:** the rename sweep in File Structure. No behaviour change.

**Interfaces:**
- Consumes: `SystemBookCommands.Create()` from Task 2.

- [ ] **Step 1: Write the failing placement tests** — append to `SystemBookCommandTests` (inside the class):

```csharp
    [Fact]
    public void TheBooksSubgroupHostsFolders()
        => Assert.Contains("folders", Books().Subcommands.Select(c => c.Name));

    [Fact]
    public void SystemsNoLongerHostsBookFolders()
        => Assert.DoesNotContain("book-folders", SystemsCommand.Create().Subcommands.Select(c => c.Name));
```

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~SystemBookCommandTests`
Expected: both new tests FAIL.

- [ ] **Step 2: Move the registration.** In `SystemBookCommands.Create()`, add `command.Subcommands.Add(BookFolderCommands.Create());` after the `facets` line. In `SystemsCommand.cs`, delete the line `command.Subcommands.Add(BookFolderCommands.Create());`. In `BookFolderCommands.cs`, change `new Command("book-folders", "Subcategory folders and their tags")` to `new Command("folders", "Subcategory folders and their tags")`.

- [ ] **Step 3: Rename the command everywhere it is live.** First the full-path form:

```bash
sed -i 's/systems book-folders/systems books folders/g' \
  src/GrimoireCli/Commands/BookFolderCommands.cs src/GrimoireCli/Commands/FilesFolderCommands.cs \
  src/GrimoireCli/Commands/MapFolderCommands.cs src/GrimoireCli/Services/SystemsService.cs \
  src/GrimoireCli/Services/MapsService.cs tests/GrimoireCli.Tests/Commands/BookFolderCommandTests.cs \
  README.md docker/smoke-test.sh tools/generate-api-coverage.py \
  docs/grimoire-api-notes.md docs/grimoire-compatibility.md
```

Then the short forms:
- `src/GrimoireCli/Commands/BookFolderCommands.cs`: every remaining `book-folders set` → `folders set`, `book-folders delete` → `folders delete`.
- `tests/GrimoireCli.Tests/Commands/RoleSectionTests.cs`: `[InlineData("book-folders set")]` → `[InlineData("books folders set")]`, `[InlineData("book-folders delete")]` → `[InlineData("books folders delete")]`, and the comment `("book-folders set")` → `("books folders set")`.
- `docs/grimoire-api-notes.md`: `` `book-folders list` `` → `` `systems books folders list` ``.
- `docker/smoke-test.sh`: the comment `for book-folders tests` → `for book folder and shelf tests`, and the `fail` messages `book-folders set|list|delete exited …` → `books folders set|list|delete exited …`.

Then regenerate coverage so the labels follow:

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py
```

- [ ] **Step 4: Check nothing live still names the old command**

```bash
grep -rn "book-folders" src tests README.md docker tools docs/grimoire-api-notes.md docs/grimoire-api-coverage.md docs/grimoire-compatibility.md docs/cli-design.md \
  | grep -v "/book-folders" | grep -v "src/GrimoireCli/Generated/"
```

Expected: no output. Remaining hits containing `/book-folders` are the route path (`/api/systems/{id}/book-folders`), which does not change. `docs/specs/`, `docs/plans/`, `CHANGELOG.md` and `docs/grimoire-1.6.0-migration.md` are dated records and stay as written.

- [ ] **Step 5: Format, unit tests, smoke**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet build GrimoireCli.sln
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

Expected: all pass; smoke ends `smoke: all checks passed` with the book-folder steps running as `systems books folders`.

- [ ] **Step 6: Commit with both breaking markers** — exactly this message:

```bash
git add -A src tests README.md docker/smoke-test.sh tools/generate-api-coverage.py \
  docs/grimoire-api-coverage.md docs/grimoire-api-notes.md docs/grimoire-compatibility.md
git commit -F - <<'EOF'
refactor!: move systems book-folders under systems books

BREAKING CHANGE: systems book-folders list|set|delete is now
systems books folders list|set|delete; flags and behaviour are
unchanged.
EOF
```

---

### Task 4: Smoke test and docs for the shelf

**Files:**
- Modify: `docker/smoke-test.sh` (after `ok "the deleted folder is no longer listed"`)
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`

- [ ] **Step 1: Smoke assertions** — insert directly after the line `ok "the deleted folder is no longer listed"` (`$DSA` is already set by the book-folder section above):

```bash

# --- system shelf -------------------------------------------------------------
# Read-only. Das Schwarze Auge 5 DE holds two books in core/ and DSA5 Errata in
# core/errata/, so every count below is fixed by the fixture.
# listing the system's shelf expecting all three books
"$CLI" systems books list --id "$DSA" >"$WORK/shelf.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems books list exited non-zero"; }
jq -e '.total == 3' "$WORK/shelf.out" >/dev/null \
  || fail "systems books list should page all three books: $(cat "$WORK/shelf.out")"
ok "systems books list pages the system's books"

# listing one folder of the shelf expecting only the errata book
"$CLI" systems books list --id "$DSA" --category core --folder errata >"$WORK/shelf-folder.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems books list --folder exited non-zero"; }
jq -e '[.books[].title] == ["DSA5 Errata"]' "$WORK/shelf-folder.out" >/dev/null \
  || fail "--category core --folder errata should list only DSA5 Errata: $(cat "$WORK/shelf-folder.out")"
ok "systems books list --category with --folder lists one folder"

# listing a folder without its category expecting the server's 400 as a non-zero exit
if "$CLI" systems books list --id "$DSA" --folder errata >/dev/null 2>"$WORK/cli.err"; then
  fail "--folder without --category should exit non-zero"
fi
ok "systems books list refuses --folder without --category"

# counting the shelf's directories expecting direct counts per category and subfolder
"$CLI" systems books folder-counts --id "$DSA" >"$WORK/shelf-counts.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems books folder-counts exited non-zero"; }
jq -e '[.groups[] | {("\(.category)/\(.path)"): .count}] | add == {"core/": 2, "core/errata": 1}' \
  "$WORK/shelf-counts.out" >/dev/null \
  || fail "folder-counts should count each directory's own books: $(cat "$WORK/shelf-counts.out")"
ok "systems books folder-counts counts each directory's direct books"

# reading the shelf's facets expecting the one category the fixture uses
"$CLI" systems books facets --id "$DSA" >"$WORK/shelf-facets.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems books facets exited non-zero"; }
jq -e '.categories == ["core"]' "$WORK/shelf-facets.out" >/dev/null \
  || fail "facets should list the core category: $(cat "$WORK/shelf-facets.out")"
ok "systems books facets lists the shelf's categories"
```

- [ ] **Step 2: Run the smoke test twice**

```bash
dotnet build GrimoireCli.sln
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

Expected: both end with `smoke: all checks passed`.

- [ ] **Step 3: README Commands table** — insert directly before the `systems books folders list --id <id>` row:

```markdown
| `systems books list --id <id> [--limit <1-500>] [--offset <n>] [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--explicit true\|false] [--genre <g>] [--product-code <prefix>] [--category <c>] [--folder <path>] [--sort title\|year\|page_count\|size\|product_code\|added_at] [--order asc\|desc]` | A page of a system's books, with their metadata |
| `systems books folder-counts --id <id> [--query <q>] [--tags <json>] [--favorites] [--added-since <iso>] [--explicit true\|false] [--genre <g>] [--product-code <prefix>] [--category <c>] [--folder <path>] [--sort title\|year\|page_count\|size\|product_code\|added_at] [--order asc\|desc]` | Directories holding a system's matching books, with direct counts |
| `systems books facets --id <id>` | The categories, genres, code prefixes and tags a system's books use |
```

- [ ] **Step 4: API coverage** — in `tools/generate-api-coverage.py` `IMPLEMENTED`, add next to the book-folder entries:

```python
    "GET /api/systems/{system_id}/books": "`systems books list` ✅",
    "GET /api/systems/{system_id}/book-groups": "`systems books folder-counts` ✅",
    "GET /api/systems/{system_id}/book-facets": "`systems books facets` ✅",
```

Regenerate: `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`
Expected: `360 operations, 175 covered, 2 internal-only`.

- [ ] **Step 5: API notes** — in `docs/grimoire-api-notes.md`, insert this section directly before the `## Book folders` heading:

```markdown
## System shelf

Read from `backend/routers/systems/books.py` at tag `v1.8.0` and verified against
the 1.8.0 stack. Backs `systems books list`, `folder-counts` and `facets`.

- **Three routes share one filter set**, `book_browse_params` (`books.py:73-127`):
  `q`, `tags`, `favorites`, `added_since`, `explicit`, `genre`, `product_code`,
  `category`, `folder`, `sort`, `order`. `/books` adds `limit` (1–500, default
  100) and `offset` (`:401-402`); `/book-facets` takes none.
- **Everything starts from the visible shelf** (`ShelfBrowser.base`, `:149-155`):
  the system's books with variants hidden, explicit books dropped for an account
  without explicit permission, access control applied. The system itself 404s
  when hidden (`:385-396`).
- **`folder` needs `category`** — a 400 otherwise (`:112`). It is the subfolder
  below the category directory, `""` for the category's own.
- **`tags` matches the book's own tags only** (`folder_tags=False`, `:218`),
  unlike the media lists, where a folder's tags count for everything beneath it.
- **`genre` is exact and case-insensitive, `product_code` a prefix**; both take
  `__grim:none__` / `__grim:any__` (`:161-181`).
- **Bare `q` text matches title, filename or product code**
  (`routers/search/_books.py:137-149`); field prefixes are the book search
  fields (`services/search_query.py:86-100`), `text:` ignored.
- **`/book-groups` rows are directories, counted directly** (`:281-323`):
  `{category, path, dir, count, first}`. Live on `Das Schwarze Auge 5 DE`:
  `core`/`""` = 2, `core`/`errata` = 1. `first` is the active sort's value for
  the folder's first book, null under `title`, so `sort`/`order` change this
  response. Two directories can share a `(category, path)`.
- **`/book-facets` ignores every filter** (`:338-380`): categories, genres,
  upper-cased leading-letter product-code prefixes and tags, over the whole
  visible shelf.
```

- [ ] **Step 6: Final verification**

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
git commit -m "docs: document the system shelf and cover it in the smoke test"
```
