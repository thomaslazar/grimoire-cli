# Books and library commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ten commands — `books list/get/update/batch-update/batch-tag/reindex/rescan` and `library rescan/scan-status/cancel-scan` — covering the workflow of copying files into the library by hand, having the server index them, and correcting their metadata.

**Architecture:** Follows the `systems` surface exactly: a `BooksService` / `LibraryService` over the generated Kiota builders, commands as thin declarations, response DTOs on `AppJsonContext`, request bodies validated against the generated models, shapes in `--help-full` from the two generators.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated client, xUnit, bash smoke test against a local Grimoire 1.5.6 stack.

Spec: [docs/specs/2026-08-13-books-and-library-commands-design.md](../specs/2026-08-13-books-and-library-commands-design.md)

## Global Constraints

- Conventional Commits: `type: subject`, imperative, lowercase, no period, ~72 chars. **No `Co-Authored-By:` lines. No "Generated with Claude Code" attribution.**
- Run `dotnet format GrimoireCli.sln` after writing or modifying C# files.
- No unnecessary blank lines inside method bodies: none between consecutive `AddCommand`/`AddOption` calls, none between consecutive variable declarations of the same kind, none before a `return` that follows setup calls.
- Every type crossing the JSON boundary MUST be registered on `AppJsonContext`, or it fails at runtime under Native AOT rather than at build time.
- Every command whose endpoint carries a non-default role dependency calls `AddRoleRequired` immediately after construction, and its service call's `permissionHint` mirrors the tag: tag `gm or admin` ↔ hint `"the gm or admin role"`, tag `admin` ↔ hint `"the admin role"`.
- `--server` and `--token` are declared per subcommand on all ten commands.
- Help text is terse. Notes text in this plan is verbatim — do not reword, expand, or add prose. Never state what a flag description, the subcommand list, or a request/response shape already shows.
- Comments say what the code does or why it must be so — never what was deliberately left out.
- **Anything that writes goes to the local stack, never the live instance.**
- Work happens on branch `feat/books-and-library-commands`. Never commit to `main`.

---

### Task 0: Branch and design docs

**Files:**
- Commit: `docs/specs/2026-08-13-books-and-library-commands-design.md`, `docs/plans/2026-08-13-books-and-library-commands.md`

- [ ] **Step 1: Create the branch and commit the docs**

```bash
git checkout -b feat/books-and-library-commands
git add docs/specs/2026-08-13-books-and-library-commands-design.md docs/plans/2026-08-13-books-and-library-commands.md
git commit -m "docs: design books and library commands"
```

---

### Task 1: Response DTOs

**Files:**
- Create: `src/GrimoireCli/Models/BookSummary.cs`, `BookDetail.cs`, `BookListResponse.cs`, `GameSystemRef.cs`, `ScanStatus.cs`, `ScanTriggerResult.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`
- Modify: `tools/GenerateResponseExamples/Program.cs` (`BuildPropertyOverrides`)
- Regenerate: `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Models/BookDtoTests.cs`

**Interfaces:**
- Produces: `GrimoireCli.Models.BookSummary`, `BookDetail`, `BookListResponse` (`Total`, `Books`), `GameSystemRef` (`Id`, `Name`, `Slug`), `ScanStatus`, `ScanTriggerResult` (`Status`). Tasks 3, 5 and 6 consume them, and `AppJsonContext.Default.<Type>` is how each is serialized.

`file_size` is `long?` because SQLite's INTEGER storage class holds 8 bytes whatever the declared width, so a multi-gigabyte book's size can exceed `int.MaxValue` and an `int?` would throw rather than truncate. The booleans split the way `Book.cs` already splits them: the ones the server coerces with `bool(...)` or a comparison are non-nullable, the ones it passes through raw stay nullable.

Field names below are the wire names from `temp/grimoire/backend/routers/books/core.py:46-119` and `backend/routers/library/_helpers.py:24-47`. Follow the existing style in `src/GrimoireCli/Models/Book.cs`: a public class, every property nullable, every property carrying `[JsonPropertyName("<wire name>")]`.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Models/BookDtoTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class BookDtoTests
{
    [Fact]
    public void BookListResponseRoundTripsTheEnvelope()
    {
        const string json = """
        {"total": 227, "books": [{"id": "b1", "title": "Core Rules", "category": "core",
         "game_system_id": "s1", "page_count": 320, "is_explicit": false, "is_missing": false}]}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookListResponse)!;
        Assert.Equal(227, result.Total);
        var book = Assert.Single(result.Books!);
        Assert.Equal("b1", book.Id);
        Assert.Equal("s1", book.GameSystemId);
        Assert.Equal(320, book.PageCount);
    }

    [Fact]
    public void BookDetailReadsItsNestedSystemAndTags()
    {
        const string json = """
        {"id": "b1", "title": "Core Rules", "authors": ["A"], "tags": ["crunchy"],
         "year": 2019, "month": 3, "day": 1, "ocr_pending": false,
         "game_system": {"id": "s1", "name": "Shadowrun 6 DE", "slug": "shadowrun-6-de"}}
        """;
        var book = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookDetail)!;
        Assert.Equal("A", Assert.Single(book.Authors!));
        Assert.Equal("crunchy", Assert.Single(book.Tags!));
        Assert.Equal(2019, book.Year);
        Assert.Equal("shadowrun-6-de", book.GameSystem!.Slug);
    }

    // A book with no system has game_system: null, not an empty object.
    [Fact]
    public void BookDetailAcceptsANullSystem()
    {
        var book = JsonSerializer.Deserialize("""{"id": "b1", "game_system": null}""",
            AppJsonContext.Default.BookDetail)!;
        Assert.Null(book.GameSystem);
    }

    [Fact]
    public void ScanStatusReadsTheCountersAndTheOcrQueue()
    {
        const string json = """
        {"running": true, "phase": "ocr", "total_books": 12, "scanned_books": 5,
         "new_books": 2, "updated_books": 1, "indexed": 4, "to_index": 8,
         "total_ocr": 3, "ocr_done": 1, "ocr_current": "scan.pdf"}
        """;
        var status = JsonSerializer.Deserialize(json, AppJsonContext.Default.ScanStatus)!;
        Assert.True(status.Running);
        Assert.Equal("ocr", status.Phase);
        Assert.Equal(5, status.ScannedBooks);
        Assert.Equal("scan.pdf", status.OcrCurrent);
    }

    // phase is null between scans; a non-nullable string would throw on it.
    [Fact]
    public void ScanStatusAcceptsANullPhase()
    {
        var status = JsonSerializer.Deserialize("""{"running": false, "phase": null}""",
            AppJsonContext.Default.ScanStatus)!;
        Assert.False(status.Running);
        Assert.Null(status.Phase);
    }

    [Fact]
    public void ScanTriggerResultReadsItsStatus()
    {
        var result = JsonSerializer.Deserialize("""{"status": "already_running"}""",
            AppJsonContext.Default.ScanTriggerResult)!;
        Assert.Equal("already_running", result.Status);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BookDtoTests`
Expected: FAIL — build error, none of these types exist.

- [ ] **Step 3: Write the DTOs**

`GameSystemRef.cs` — `id`, `name`, `slug`, all `string?`.

`BookSummary.cs` — exactly the fields `GET /api/books` returns per item:

| wire name | C# type |
|---|---|
| `id`, `title`, `filename`, `category`, `mime_type` | `string?` |
| `page_count` | `int?` |
| `file_size` | `long?` |
| `game_system_id` | `string?` |
| `has_thumbnail`, `indexed`, `index_failed` | `bool?` |
| `ocr_indexed`, `is_explicit`, `is_missing` | `bool` |

`BookDetail.cs` — exactly what `GET /api/books/{id}` returns:

| wire name | C# type |
|---|---|
| `id`, `title`, `filename`, `category`, `description`, `publisher`, `publisher_url`, `isbn`, `version`, `language`, `license`, `mime_type` | `string?` |
| `page_count`, `year`, `month`, `day`, `ocr_dpi` | `int?` |
| `file_size` | `long?` |
| `authors`, `artists`, `genres`, `tags` | `List<string>?` |
| `urls` | `List<LinkEntry>?` |
| `indexed`, `index_failed`, `has_thumbnail` | `bool?` |
| `ocr_indexed`, `ocr_pending`, `is_missing`, `is_explicit` | `bool` |
| `game_system` | `GameSystemRef?` |

`BookListResponse.cs` — `total` as `int?`, `books` as `List<BookSummary>?`.

`ScanStatus.cs` — `running` as `bool?`, `phase` and `ocr_current` as `string?`, and every one of these as `int?`: `total_books`, `scanned_books`, `total_maps`, `scanned_maps`, `total_tokens`, `scanned_tokens`, `total_audio`, `scanned_audio`, `new_books`, `new_maps`, `new_tokens`, `new_audio`, `updated_books`, `indexed`, `to_index`, `total_ocr`, `ocr_done`.

`ScanTriggerResult.cs` — `status` as `string?`, with a doc comment stating that `library rescan` reads it to choose exit 3 on `already_running`.

- [ ] **Step 4: Register every new type on `AppJsonContext`**

In `src/GrimoireCli/Models/JsonContext.cs`, add one `[JsonSerializable(typeof(T))]` line per new type: `BookSummary`, `BookDetail`, `BookListResponse`, `GameSystemRef`, `ScanStatus`, `ScanTriggerResult`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BookDtoTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Add response-example overrides and regenerate**

`GenerateResponseExamples` walks every type on `AppJsonContext`, so the new DTOs already have samples — but with `"<string>"` where a real vocabulary exists. In `tools/GenerateResponseExamples/Program.cs`'s `BuildPropertyOverrides`, add:

```csharp
        o.StringValues[(typeof(BookSummary), nameof(BookSummary.Category))] = "core";
        o.StringValues[(typeof(BookDetail), nameof(BookDetail.Category))] = "core";
        o.StringValues[(typeof(GameSystemRef), nameof(GameSystemRef.Name))] = "Shadowrun 6 DE";
        o.StringValues[(typeof(GameSystemRef), nameof(GameSystemRef.Slug))] = "shadowrun-6-de";
        o.StringValues[(typeof(ScanStatus), nameof(ScanStatus.Phase))] = "scanning";
```

Then regenerate, or `ResponseExamplesDriftTest` fails:

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
```

- [ ] **Step 7: Format, run the full suite, commit**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli/Models tools/GenerateResponseExamples/Program.cs src/GrimoireCli/Commands/ResponseExamples.g.cs tests/GrimoireCli.Tests/Models/BookDtoTests.cs
git commit -m "feat: add book and scan-status response dtos"
```

---

### Task 2: Extract the shared command and test helpers

**Files:**
- Create: `src/GrimoireCli/Commands/OptionHelpers.cs`
- Modify: `src/GrimoireCli/Commands/JsonBodyInput.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs`
- Create: `tests/GrimoireCli.Tests/Commands/HelpRenderer.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs`

**Interfaces:**
- Produces: `OptionHelpers.Choice(string name, string description, string[] allowed)` → `Option<string?>`; `JsonBodyInput.RequireExactlyOneSource(Command command, Option<string?> inputOption, Option<bool> stdinOption)` → `void`; `HelpRenderer.Render(Command command, string[] path, bool full)` → `string`. Tasks 3, 4 and 6 and their tests call all three.

Three helpers currently live as private members of `SystemsCommand` and `SystemsCommandTests`. Books and library need all three, so they move to shared homes now rather than being duplicated into each new file.

**This task changes no behaviour.** It is a move plus call-site updates, and the existing suite is what proves it: every systems test must still pass, unchanged, at the end.

- [ ] **Step 1: Confirm the suite is green before touching anything**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: PASS. Record the count — it must be identical after the move, since no test is added or removed.

- [ ] **Step 2: Move `ChoiceOption` to `OptionHelpers.Choice`**

Create `src/GrimoireCli/Commands/OptionHelpers.cs` holding the body of `SystemsCommand.ChoiceOption` verbatim, renamed to `Choice` and made public:

```csharp
using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>Option factories shared across command groups.</summary>
public static class OptionHelpers
{
    /// <summary>
    /// A string option restricted to a fixed value set, rejected at parse time and
    /// offered as shell completions. The rendered help lists the values itself, so
    /// the description must not repeat them.
    /// </summary>
    public static Option<string?> Choice(string name, string description, string[] allowed)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !allowed.Contains(value))
                result.AddError($"'{value}' is not a valid value for {name}. Must be one of: {string.Join(", ", allowed)}");
        });
        option.CompletionSources.Add(allowed);
        return option;
    }
}
```

Delete `SystemsCommand.ChoiceOption` and point its four call sites at `OptionHelpers.Choice`.

- [ ] **Step 3: Move `RequireExactlyOneBodySource` onto `JsonBodyInput`**

`JsonBodyInput` already owns `BothSourcesMessage` and `NeitherSourceMessage`, so the validator that emits them belongs beside them. Move `SystemsCommand.RequireExactlyOneBodySource` there verbatim as a public static `RequireExactlyOneSource`, keeping its doc comment, and update its three call sites in `SystemsCommand`.

- [ ] **Step 4: Move `RenderHelp` to a shared test helper**

Create `tests/GrimoireCli.Tests/Commands/HelpRenderer.cs`. The existing version hard-codes `SystemsCommand.Create()`; the shared one takes the command under test:

```csharp
using System.CommandLine;
using System.CommandLine.Invocation;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

internal static class HelpRenderer
{
    /// <summary>
    /// Renders a subcommand's help exactly as the CLI would, including the custom
    /// sections, so tests assert on what a user sees rather than on registration.
    /// </summary>
    public static string Render(Command command, string[] path, bool full)
    {
        var root = new RootCommand("test") { command };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        root.Parse([.. path, full ? "--help-full" : "--help"])
            .Invoke(new InvocationConfiguration { Output = output });
        return output.ToString();
    }
}
```

Delete `SystemsCommandTests.RenderHelp` and replace its calls with `HelpRenderer.Render(SystemsCommand.Create(), path, full)` — keeping a one-line private wrapper in the test class is fine if it keeps the call sites readable.

- [ ] **Step 5: Prove nothing changed**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: the same test count as Step 1, all passing. `ResponseExamplesDriftTest` and `RequestExamplesDriftTest` must also still pass — neither generator reads these files, so a failure there means something unrelated broke.

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands tests/GrimoireCli.Tests/Commands
git commit -m "refactor: share the option, body-source and help-render helpers"
```

---

### Task 3: `books list` and `books get`

**Files:**
- Create: `src/GrimoireCli/Services/BooksService.cs`
- Create: `src/GrimoireCli/Commands/BooksCommand.cs`
- Modify: `src/GrimoireCli/Program.cs`
- Test: `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`

**Interfaces:**
- Consumes: `BookListResponse`, `BookDetail` from Task 1.
- Produces: `BooksService.ListAsync(string? systemId, string? category, int limit, int? offset)` → `Task<BookListResponse>`; `BooksService.GetAsync(string id)` → `Task<BookDetail>`; `BooksCommand.Create()` → `Command`. Tasks 4 and 5 add methods to both classes.

Read `src/GrimoireCli/Services/SystemsService.cs` and `src/GrimoireCli/Commands/SystemsCommand.cs` first — this task mirrors their structure, and later tasks extend what you create here. Task 2 moved the shared helpers out of `SystemsCommand`; use `OptionHelpers.Choice`, `JsonBodyInput.RequireExactlyOneSource` and `HelpRenderer.Render` rather than writing local copies.

The generated builders are `client.Api.Api.Books` (GET, query parameters `SystemId`, `Category`, `Limit`, `Offset`) and `client.Api.Api.Books[id]` (GET). The builder also exposes a `Token` query parameter for media access; it is unrelated to the JWT and this command does not set it.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`. Its `RenderHelp` is a one-line wrapper over the shared helper from Task 2 — `private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(BooksCommand.Create(), path, full);` — then:

```csharp
    [Fact]
    public void ListDocumentsThePagingDefaultAndCap()
    {
        var output = RenderHelp(["books", "list"], full: false);
        Assert.Contains("--limit", output);
        Assert.Contains("default 100, max 500", output);
        Assert.Contains("--offset", output);
    }

    [Fact]
    public void ListShowsTheEnvelopeNotABareArray()
    {
        var output = RenderHelp(["books", "list"], full: true);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"total\":", output);
        Assert.Contains("\"books\":", output);
    }

    [Fact]
    public void GetShowsTheDetailShapeWithItsNestedSystem()
    {
        var output = RenderHelp(["books", "get"], full: true);
        Assert.Contains("\"game_system\":", output);
        Assert.Contains("\"authors\":", output);
    }

    // Both reads are guarded by require_not_guest or nothing at all, which per
    // CLAUDE.md is the default and carries no tag.
    [Fact]
    public void ReadsCarryNoRoleTag()
    {
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "list"], full: false));
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "get"], full: false));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests`
Expected: FAIL — build error, `BooksCommand` does not exist.

- [ ] **Step 3: Write the service**

```csharp
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class BooksService
{
    private readonly GrimoireApiClient _client;

    public BooksService(GrimoireApiClient client) => _client = client;

    public async Task<BookListResponse> ListAsync(string? systemId, string? category, int limit, int? offset)
    {
        var info = _client.Api.Api.Books.ToGetRequestInformation(c =>
        {
            c.QueryParameters.SystemId = systemId;
            c.QueryParameters.Category = category;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info, AppJsonContext.Default.BookListResponse);
    }

    public async Task<BookDetail> GetAsync(string id)
    {
        var info = _client.Api.Api.Books[id].ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BookDetail,
            notFoundHint: "No book with that ID. List them with: grimoire-cli books list");
    }
}
```

- [ ] **Step 4: Write the two commands**

`BooksCommand.Create()` returns `new Command("books", "Read and edit book metadata")` with the subcommands added. `list` takes `--system-id`, `--category`, `--limit`, `--offset`, `--server`, `--token`:

```csharp
        var limitOption = new Option<int>("--limit")
        {
            Description = "Results per page (default 100, max 500)",
            DefaultValueFactory = _ => 100,
        };
        var offsetOption = new Option<int?>("--offset") { Description = "Items to skip" };
```

`list`'s command description is `"List books (defaults to 100 results)"`, naming the default a second time as abs-cli does. Its Notes, verbatim:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--limit defaults to 100 and 422s above 500; page with --offset against",
            "the total in the response.",
            "",
            "--category is the normalised value, not the folder name ('supplement',",
            "not 'supplements'), and is case-sensitive: Core matches nothing.",
            "",
            "The account's explicit permission filters the list server-side.");
        command.AddExamples(
            "grimoire-cli books list",
            "grimoire-cli books list --system-id <system-id> --category core",
            "grimoire-cli books list --limit 500 --offset 500");
        command.AddResponseExample<BookListResponse>();
```

`get` takes `--id` (`Required = true`), `--server`, `--token`, description `"Get one book"`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "403 if the book is explicit and the account disallows explicit content.");
        command.AddExamples("grimoire-cli books get --id <book-id>");
        command.AddResponseExample<BookDetail>();
```

Neither read calls `AddRoleRequired`. Both write with `ConsoleOutput.WriteJson(result, AppJsonContext.Default.<Type>)` and return 0.

- [ ] **Step 5: Register the command**

In `src/GrimoireCli/Program.cs`, beside the existing `rootCommand.Subcommands.Add(SystemsCommand.Create());`:

```csharp
rootCommand.Subcommands.Add(BooksCommand.Create());
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests`
Expected: PASS.

- [ ] **Step 7: Format, run the full suite, commit**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add books list and books get"
```

---

### Task 4: `books update`, `batch-update` and `batch-tag`

**Files:**
- Modify: `src/GrimoireCli/Services/BooksService.cs`
- Modify: `src/GrimoireCli/Commands/BooksCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`

**Interfaces:**
- Consumes: `BooksService` and `BooksCommand` from Task 3; `BulkUpdateResult`, `BulkTagResult`, `BulkExit`, `JsonBodyInput` as they already exist.
- Produces: `BooksService.UpdateAsync(string id, string rawBody)` → `Task<string>`; `BatchUpdateAsync(string rawBody)` → `Task<BulkUpdateResult>`; `BatchTagAsync(string rawBody)` → `Task<BulkTagResult>`.

This mirrors `SystemsCommand`'s three write commands almost exactly. Read them. The `--input`/`--stdin` validator is `JsonBodyInput.RequireExactlyOneSource`, moved there in Task 2 — call it, do not reimplement it.

Generated builders: `client.Api.Api.Books[id]` (PATCH, model `Generated.Models.BookUpdate`), `client.Api.Api.Books.Bulk` (POST, `Generated.Models.BookBulkUpdate`), `client.Api.Api.Books.Bulk.Tags` (POST, `Generated.Models.BulkAddTags`).

- [ ] **Step 1: Write the failing tests**

Add to `BooksCommandTests.cs`:

```csharp
    [Fact]
    public void WritesCarryTheGmOrAdminTag()
    {
        foreach (var verb in new[] { "update", "batch-update", "batch-tag" })
            Assert.Contains("gm or admin", RenderHelp(["books", verb], full: false));
    }

    [Fact]
    public void UpdateShowsItsRequestShapeAndTheClearingRule()
    {
        var output = RenderHelp(["books", "update"], full: true);
        Assert.Contains("Request shape:", output);
        Assert.Contains("\"title\":", output);
        Assert.Contains("year, month and day cannot be cleared", output);
    }

    // The bulk body is an envelope, and the sample is the model
    // JsonBodyInput.Validate parses against.
    [Fact]
    public void BatchUpdateShowsTheItemsEnvelope()
    {
        var output = RenderHelp(["books", "batch-update"], full: true);
        Assert.Contains("\"items\":", output);
        Assert.Contains("Each item requires id", output);
    }

    [Fact]
    public void BatchTagShowsTheSharedIdsAndTagsBody()
    {
        var output = RenderHelp(["books", "batch-tag"], full: true);
        Assert.Contains("\"ids\":", output);
        Assert.Contains("\"tags\":", output);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests`
Expected: FAIL — the three subcommands do not exist.

- [ ] **Step 3: Add the three service methods**

Copy the shape of `SystemsService.UpdateAsync` / `BatchUpdateAsync` / `BatchTagAsync` exactly, including the `SetStreamContent` approach — the generated request model is used for URL, method and path parameter only, because it is an `IAdditionalDataHolder` and would transmit unknown keys. Every one passes `permissionHint: "the gm or admin role"`; `UpdateAsync` also passes `notFoundHint: "No book with that ID. List them with: grimoire-cli books list"`.

- [ ] **Step 4: Add the three commands**

Each takes `--input`/`--stdin` (validated by `RequireExactlyOneBodySource`), `--server`, `--token`; `update` additionally takes `--id` (`Required = true`). Each calls `AddRoleRequired("gm or admin")` immediately after construction and validates its body with `JsonBodyInput.Validate` against the matching generated model before building a client.

`update` — description `"Update one book's metadata"`, `AddRequestShape<Generated.Models.BookUpdate>()`, no response shape, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Clear a field with \"\"; an explicit null does nothing.",
            "",
            "year, month and day cannot be cleared at all: null is dropped and \"\"",
            "fails coercion with a 422.",
            "",
            "tags replace the set. To add without removing, use batch-tag.",
            "",
            "Responds {\"status\": \"ok\"} and echoes nothing — read back with:",
            "grimoire-cli books get --id <id>");
```

Its action writes `ConsoleOutput.WriteRawJson(response)` and returns 0. `JsonBodyInput.Validate`'s second argument is `"pass it with --id"`.

`batch-update` — description `"Update many books in one transaction"`, `AddRequestShape<Generated.Models.BookBulkUpdate>()`, `AddResponseExample<BulkUpdateResult>()`, validated against `BookBulkUpdate.CreateFromDiscriminatorValue` with `"put it in each item"`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "At most 1000 items. Each item requires id.",
            "",
            "Skip-and-continue: a bad id or item lands in errors, the rest apply.",
            "Exit 3 is HTTP 200 with a non-empty errors list — a partial write.",
            "updated lists the ids that resolved, not the fields that changed.",
            "",
            "\"\" not null clears a field, and year/month/day cannot be cleared — see",
            "books update.");
```

Its action returns `BulkExit.CodeFor(result.Errors)`.

`batch-tag` — description `"Add tags to many books"`, `AddRequestShape<Generated.Models.BulkAddTags>()`, `AddResponseExample<BulkTagResult>()`, validated against `BulkAddTags.CreateFromDiscriminatorValue` with `"put it in ids"`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "ids and tags are both required and non-empty; max 1000 ids.",
            "",
            "Additive only: merges with existing tags, never removes one. To replace",
            "a set, use batch-update with tags.",
            "",
            "Exit 3 is HTTP 200 with a non-empty errors list — some ids did not",
            "resolve while the rest were tagged.");
```

Its action returns `BulkExit.CodeFor(result.Errors)`.

Give each an `AddExamples` block of two lines in the style of the systems equivalents — one `--input`, one piped `--stdin`.

- [ ] **Step 5: Run the tests, format, run the full suite, commit**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add books write commands"
```

---

### Task 5: `books reindex` and `books rescan`

**Files:**
- Modify: `src/GrimoireCli/Services/BooksService.cs`
- Modify: `src/GrimoireCli/Commands/BooksCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`

**Interfaces:**
- Produces: `BooksService.ReindexAsync(string id, int? ocrDpi)` → `Task<string>`; `RescanAsync(string id)` → `Task<string>`. Both return the raw response body.

Generated builders: `client.Api.Api.Books[id].Reindex` (POST, query parameter `OcrDpi`) and `client.Api.Api.Books[id].Rescan` (POST, no parameters). Neither takes a request body — use the parameterless `ToPostRequestInformation` overloads, with a configuration lambda for `OcrDpi` on reindex.

Both return raw JSON rather than a DTO: the payload is one or two fields whose values are the information, and no response shape is registered for either.

- [ ] **Step 1: Write the failing tests**

Add to `BooksCommandTests.cs`:

```csharp
    [Fact]
    public void MaintenanceCommandsCarryTheGmOrAdminTag()
    {
        foreach (var verb in new[] { "reindex", "rescan" })
            Assert.Contains("gm or admin", RenderHelp(["books", verb], full: false));
    }

    // The DPI range belongs on the flag, so the Notes must not repeat it.
    [Fact]
    public void ReindexStatesItsDpiRangeOnceOnTheFlag()
    {
        var output = RenderHelp(["books", "reindex"], full: false);
        Assert.Contains("72-600", output);
        Assert.Equal(1, output.Split("72-600").Length - 1);
    }

    [Fact]
    public void ReindexSaysItIsOcrOnlyAndPointsAtScanStatus()
    {
        var output = RenderHelp(["books", "reindex"], full: false);
        Assert.Contains("OCR only", output);
        Assert.Contains("library scan-status", output);
    }

    [Fact]
    public void RescanWarnsThatALibraryScanAbsorbsIt()
    {
        var output = RenderHelp(["books", "rescan"], full: false);
        Assert.Contains("rescan_queued either way", output);
    }

    // Status-only responses name their values in Notes instead of registering a
    // shape, which would render them as "<string>".
    [Fact]
    public void MaintenanceCommandsRegisterNoResponseShape()
    {
        foreach (var verb in new[] { "reindex", "rescan" })
            Assert.DoesNotContain("Response shape:", RenderHelp(["books", verb], full: true));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests`
Expected: FAIL — the two subcommands do not exist.

- [ ] **Step 3: Add the service methods and commands**

Both service methods pass `permissionHint: "the gm or admin role"` and `notFoundHint: "No book with that ID. List them with: grimoire-cli books list"`.

`reindex` — description `"Re-run OCR on one book"`, `--id` (`Required = true`), and:

```csharp
        var dpiOption = new Option<int?>("--ocr-dpi")
        {
            Description = "OCR resolution for this book (72-600); omit for the server default",
        };
```

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "OCR only: 400 unless the book is an image-only PDF. A book with a real",
            "text layer has nothing to re-OCR.",
            "",
            "Clears the book's search index and re-queues it from page 1. The OCR",
            "runs in the background — watch it with:",
            "grimoire-cli library scan-status");
```

`rescan` — description `"Re-read one book from disk and rebuild its index"`, `--id` (`Required = true`), and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Re-reads the file and rebuilds the index, refreshing page count and",
            "thumbnail if the file changed. PDFs only: 400 on an epub or djvu, 404",
            "if the file is gone from disk.",
            "",
            "Absorbed into a library scan already in progress; the response is",
            "rescan_queued either way. Watch it with:",
            "grimoire-cli library scan-status");
```

Both call `AddRoleRequired("gm or admin")`, write `ConsoleOutput.WriteRawJson(response)`, return 0, and carry a one-line `AddExamples`.

- [ ] **Step 4: Run the tests, format, run the full suite, commit**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add books reindex and books rescan"
```

---

### Task 6: The `library` command group

**Files:**
- Create: `src/GrimoireCli/Services/LibraryService.cs`
- Create: `src/GrimoireCli/Commands/LibraryCommand.cs`
- Modify: `src/GrimoireCli/Program.cs`
- Test: `tests/GrimoireCli.Tests/Commands/LibraryCommandTests.cs`

**Interfaces:**
- Consumes: `ScanStatus`, `ScanTriggerResult` from Task 1.
- Produces: `LibraryService.RescanAsync(string? scope, string? metadataMode)` → `Task<ScanTriggerResult>`; `ScanStatusAsync()` → `Task<ScanStatus>`; `CancelScanAsync()` → `Task<string>`; `LibraryCommand.Create()` → `Command`.

Generated builders: `client.Api.Api.Rescan` (POST, body `Generated.Models.RescanRequest`), `client.Api.Api.ScanStatus` (GET), `client.Api.Api.CancelScan` (POST, no body).

These are the first `admin`-tagged commands in this CLI, so all three call `AddRoleRequired("admin")` and pass `permissionHint: "the admin role"`.

`library rescan` composes its body from flags rather than `--input`/`--stdin`, so set the generated model's properties directly and let Kiota serialize it — there is no raw body to validate and **no** `AddRequestShape` call.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/LibraryCommandTests.cs`, wrapping the shared helper as in Task 3 — `private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(LibraryCommand.Create(), path, full);` — then:

```csharp
    [Fact]
    public void AllThreeCarryTheAdminTag()
    {
        foreach (var verb in new[] { "rescan", "scan-status", "cancel-scan" })
            Assert.Contains("admin", RenderHelp(["library", verb], full: false));
    }

    // The body is composed from flags, so a request shape would document a body
    // the caller never writes.
    [Fact]
    public void RescanRegistersNoRequestShape()
    {
        var output = RenderHelp(["library", "rescan"], full: true);
        Assert.DoesNotContain("Request shape:", output);
        Assert.Contains("--scope", output);
        Assert.Contains("--metadata-mode", output);
    }

    [Fact]
    public void RescanExplainsWhereAScopePathComesFrom()
    {
        var output = RenderHelp(["library", "rescan"], full: false);
        Assert.Contains("relative_path", output);
        Assert.Contains("already_running", output);
    }

    // A ChoiceOption renders its own value set, so the description must not.
    [Fact]
    public void MetadataModeListsItsValuesOnce()
    {
        var output = RenderHelp(["library", "rescan"], full: false);
        Assert.Equal(1, output.Split("missing").Length - 1);
    }

    [Fact]
    public void ScanStatusWarnsAboutTheLooseFileTrap()
    {
        var output = RenderHelp(["library", "scan-status"], full: true);
        Assert.Contains("never becomes true", output);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"running\":", output);
    }

    [Fact]
    public void CancelScanSaysItExitsZeroEitherWay()
    {
        Assert.Contains("whether or not one was running",
            RenderHelp(["library", "cancel-scan"], full: false));
    }
}
```

Also add, to a suitable existing or new test file, the exit-code mapping — it is logic, not help text:

```csharp
public class ScanExitTests
{
    [Theory]
    [InlineData("already_running", 3)]
    [InlineData("scan_started", 0)]
    [InlineData(null, 0)]
    public void RescanExitsThreeOnlyWhenTheScanDidNotStart(string? status, int expected)
        => Assert.Equal(expected, ScanExit.CodeFor(new ScanTriggerResult { Status = status }));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "LibraryCommandTests|ScanExitTests"`
Expected: FAIL — build error, none of these types exist.

- [ ] **Step 3: Write `ScanExit`**

Create `src/GrimoireCli/Commands/ScanExit.cs`, beside the existing `BulkExit.cs` and modelled on it:

```csharp
using GrimoireCli.Models;

namespace GrimoireCli.Commands;

/// <summary>
/// Maps a scan-trigger response to an exit code. 3 means the request succeeded
/// (HTTP 200) and reported already_running — a scan was in flight, so the
/// requested one never started. stdout still carries the status, so a caller
/// that polls scan-status can tell it is watching someone else's scan.
/// </summary>
public static class ScanExit
{
    public static int CodeFor(ScanTriggerResult result) => result.Status == "already_running" ? 3 : 0;
}
```

- [ ] **Step 4: Write the service**

```csharp
    public async Task<ScanTriggerResult> RescanAsync(string? scope, string? metadataMode)
    {
        var body = new Generated.Models.RescanRequest();
        // Kiota models the optional fields as composed types; assign through the
        // wrapper so an omitted flag stays absent from the body.
        if (scope is not null)
            body.Scope = new Generated.Models.RescanRequest.RescanRequest_scope { String = scope };
        if (metadataMode is not null)
            body.MetadataMode = ParseMetadataMode(metadataMode);
        var info = _client.Api.Api.Rescan.ToPostRequestInformation(body);
        return await _client.SendAsync(
            info, AppJsonContext.Default.ScanTriggerResult, permissionHint: "the admin role");
    }
```

Both member names are verified against `src/GrimoireCli/Generated/Models/RescanRequest.cs` and `RescanRequest_metadata_mode.cs`: `RescanRequest.Scope` is the composed wrapper `RescanRequest.RescanRequest_scope` whose value branch is `String`, and `RescanRequest.MetadataMode` is the enum `RescanRequest_metadata_mode` with members `New`, `Missing`, `Replace` carrying `[EnumMember]` values `new`, `missing`, `replace`. `ParseMetadataMode` maps the flag's string onto that enum; the flag is an `OptionHelpers.Choice` over the same three values, so an unrecognised one is already a parse error before the service is reached.

`ScanStatusAsync` is a plain GET returning `AppJsonContext.Default.ScanStatus`. `CancelScanAsync` posts with no body and returns the raw response string.

- [ ] **Step 5: Write the three commands**

`LibraryCommand.Create()` returns `new Command("library", "Scan and index the library")`.

`rescan` — description `"Scan the library for new and changed files"`, `--scope`, `--metadata-mode` built with `OptionHelpers.Choice` over `new`, `missing`, `replace`, description `"Re-apply OPF sidecar metadata"`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The only command that finds a file copied into the library by hand;",
            "books rescan re-reads a book the server already knows.",
            "",
            "--scope is a path from the library root beginning books/, maps/,",
            "tokens/ or audio/ — a system's path is the relative_path of its books",
            "in systems get.",
            "",
            "Exit 3 is HTTP 200 with already_running: a scan was already in flight",
            "and this one did not start.");
```

Its action writes the result with `AppJsonContext.Default.ScanTriggerResult` and returns `ScanExit.CodeFor(result)`.

`scan-status` — description `"Show the running scan's progress"`, `AddResponseExample<ScanStatus>()`, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "phase is scanning, indexing or ocr; the counters belong to the scan in",
            "flight.",
            "",
            "A loose file directly under books/ counts toward total_books but is",
            "never scanned, so scanned_books >= total_books never becomes true. Poll",
            "running instead.");
```

`cancel-scan` — description `"Stop the running scan"`, no response shape, and:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Requests a graceful stop; the scan ends at its next checkpoint. Exits 0",
            "whether or not one was running.");
```

Register with `rootCommand.Subcommands.Add(LibraryCommand.Create());` in `Program.cs`.

- [ ] **Step 6: Run the tests, format, run the full suite, commit**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "LibraryCommandTests|ScanExitTests"
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add the library scan commands"
```

---

### Task 7: Fixtures and smoke test

**Files:**
- Modify: `docker/seed.sh`
- Modify: `docker/smoke-test.sh`

Read both scripts first, and `CLAUDE.md`'s smoke-test rules: the stack must already be running and seeded, the test must be idempotent, and **writes go only to `Shadowrun 4 DE` with values fixed in the script** so a second run converges rather than drifting.

- [ ] **Step 1: Extend the fixtures**

`docker/seed.sh` must produce enough books in one system that `--limit` below the total proves paging rather than coincidence — at least three books in a single system, across at least two categories. `docker/make-fixtures.py` generates the PDFs; follow its existing pattern rather than adding real files.

- [ ] **Step 2: Bring the stack up from clean and re-seed**

Because the fixture tree changed, a database-only reset leaves stale rows that survive as `is_missing` and still count toward `book_count`:

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- [ ] **Step 3: Add the books section to the smoke test**

Assert, in the style of the existing systems section:

- `books list` returns a `total` and a `books` array.
- `books list --limit 2` returns 2 books while `total` stays the full count — the paging assertion.
- `books list --offset 2 --limit 2` returns a different first id than the unoffset call.
- `books list --category core` returns only core books, and `--category Core` returns none — the case-sensitivity trap.
- `books get --id <id>` returns the detail shape with `game_system` populated.
- `books update` sets a fixed value on one `Shadowrun 4 DE` book, then `books get` reads it back.
- `books batch-tag` adds a fixed tag; re-running converges because the endpoint is additive.
- `books batch-update` with one good and one unknown id exits 3 and reports the bad id in `errors`.
- `books reindex` on a fixture book exits non-zero with a 400 — the fixtures have a text layer, so rejection is the assertable behaviour.
- `books rescan` on a fixture book returns `rescan_queued`.
- `library rescan --scope "books/Shadowrun 4 DE"` returns `scan_started`, and `library scan-status` returns valid JSON with a `running` field.
- `library cancel-scan` exits 0 and returns a `status`.

**Never call `library rescan` without `--scope`.**

- [ ] **Step 4: Run it twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Both runs must pass. A second-run failure means an assertion depends on prior state — fix the assertion, not the ordering.

- [ ] **Step 5: Commit**

```bash
git add docker/
git commit -m "test: cover books and library commands in the smoke test"
```

---

### Task 8: Documentation

**Files:**
- Modify: `README.md` (Commands table)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/input-output.md` (exit codes)
- Modify: `docs/roadmap.md`
- Modify: `docs/grimoire-api-notes.md` (only if the smoke test verified something the source did not settle)

- [ ] **Step 1: README Commands table**

Ten rows in the existing style — flags in the left column, one-line description on the right, `(gm or admin)` or `(admin)` suffixed where a role is required, and `exit 3 if partial` where it applies.

- [ ] **Step 2: API coverage**

Add the ten endpoints to `IMPLEMENTED` in `tools/generate-api-coverage.py` with their command names, then regenerate. The stack must be running, since the script fetches the spec live:

```bash
python3 tools/generate-api-coverage.py
```

Never hand-edit `docs/grimoire-api-coverage.md`.

- [ ] **Step 3: Exit codes**

`docs/input-output.md`'s list stops at 2. Add the missing entry, covering both uses:

```markdown
- `3` — the request succeeded (HTTP 200) but did not do what was asked: a bulk
  call with a non-empty `errors` list (a partial write), or `library rescan`
  reporting `already_running`, where a scan was already in flight and the
  requested one never started. stdout carries the full response either way.
```

- [ ] **Step 4: Roadmap**

Delete item 1 (books metadata and maintenance) and renumber. The roadmap lists intended work only — do not replace it with a note that the work happened.

- [ ] **Step 5: Commit**

```bash
git add README.md docs/ tools/generate-api-coverage.py
git commit -m "docs: record the books and library commands"
```

---

### Task 9: Pre-PR verification and PR

- [ ] **Step 1: Run all four checks**

The stack must be running and seeded from Task 7.

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

- [ ] **Step 2: Read the help output**

```bash
for c in list get update batch-update batch-tag reindex rescan; do
  dotnet run --project src/GrimoireCli -- books $c --help-full
done
for c in rescan scan-status cancel-scan; do
  dotnet run --project src/GrimoireCli -- library $c --help-full
done
```

Confirm by eye: role tags on the eight commands that need them and on none of the two reads; a Request shape only on the three body-taking books commands; a Response shape only on `list`, `get`, `batch-update`, `batch-tag` and `scan-status`; and no Notes line that merely repeats a flag description or a shape.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin feat/books-and-library-commands
gh pr create --title "feat: books and library commands" --body "$(cat <<'EOF'
Ten commands covering the workflow the CLI exists for: copy files into the
library by hand, have the server find and index them, then correct their
metadata.

`books list/get/update/batch-update/batch-tag/reindex/rescan` mirror the
`systems` surface. `library rescan/scan-status/cancel-scan` are the first
admin-tagged commands here, and `library rescan` is the only one that finds a
file copied in by hand — `books rescan` re-reads a book the server already
knows.

`library rescan` exits 3 on `already_running`: a scan was already in flight and
the requested one never started, which an agent that then polls `scan-status`
would otherwise mistake for its own work finishing.

Design: `docs/specs/2026-08-13-books-and-library-commands-design.md`
EOF
)"
```

- [ ] **Step 4: Watch CI to a terminal state**

`gh pr checks <num> --watch`. Report the result without being asked, and present the PR URL as a clickable link.

---

## Self-Review

**Spec coverage:** shared helpers extracted ahead of their second and third callers → Task 2; ten commands → Tasks 3-6; three book DTOs plus `ScanStatus`/`ScanTriggerResult`/`GameSystemRef` → Task 1; paging with a client-side default of 100 → Task 3 Step 4; the shape-block table including all three deliberate absences → Tasks 3 (`AddResponseExample<BookListResponse>` not the array helper), 5 (no response shape on the maintenance pair) and 6 (no request shape on `library rescan`); every Notes block verbatim → Tasks 3-6; role tags and permission hints → Tasks 3-6 plus the Global Constraints; exit 3 for `already_running` → Task 6's `ScanExit`; fixtures and smoke test → Task 7; README, coverage, exit-3 doc gap, roadmap → Task 8.

**Type consistency:** `BooksService` is created in Task 3 and extended in Tasks 4 and 5; `BooksCommand.Create()` likewise. `BookListResponse.Total`/`.Books`, `BookDetail.GameSystem`, `ScanStatus.Running`/`.Phase`/`.ScannedBooks`/`.OcrCurrent` and `ScanTriggerResult.Status` are defined in Task 1 and used with those exact names in Tasks 3, 6 and their tests. `ScanExit.CodeFor(ScanTriggerResult)` takes the DTO, not a string, in both its definition and its test.
