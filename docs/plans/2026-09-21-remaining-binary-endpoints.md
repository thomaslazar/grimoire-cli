# remaining binary endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `books file`, `books page` and `downloads archive` ([#48](https://github.com/thomaslazar/grimoire-cli/issues/48)), finishing the `books` group.

**Architecture:** Three streaming downloads on the settled `--output` convention. Two extend `BooksCommand`; the third needs a new `DownloadsCommand` and `DownloadsService`, and is the only one with real design surface — eleven scope types selected by `--type`, whose flag combinations the help text must teach.

**Tech Stack:** C# / .NET, System.CommandLine, Kiota-generated client, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-21-remaining-binary-endpoints-design.md](../specs/2026-09-21-remaining-binary-endpoints-design.md)

## Global Constraints

- Branch: `feat/binary-endpoints`, cut from `main`. Never commit to `main`.
- **Commit messages carry NO attribution.** No `Co-Authored-By:` line of any kind, no "Generated with Claude Code" line, no naming of any model or tool — in commit messages, in the PR body, or in any file this change touches. Every commit, fix-ups included. Task 5 greps the branch before the PR.
- Conventional Commits, imperative, lowercase, no period, ~72 chars.
- The spec and this plan are committed **on the feature branch**, in Task 1's commit.
- **None of the three commands carries a role tag or a `permissionHint`.** All three routes are guarded by `get_current_user`. `downloads archive`'s `library_folder` scope is admin-gated *inside* the handler, which is server policy the CLI passes through — it is not a reason to tag the command.
- **None passes a `notFoundHint`.** Every 404 these routes raise carries a real message; a hint would replace it.
- All three use the streaming shape: `--output` required with the description `"Output file path, or '-' for binary to stdout"`, `SavedFile` response example, `SendStreamAsync`, `BodyInputException` caught and mapped to exit 1. Copy it from `BooksCommand.CreateThumbnailCommand`.
- No client-side validation of `--type` or `--fmt`; the server owns both sets.
- Run `dotnet format GrimoireCli.sln` after touching any C# file.
- Never touch `CHANGELOG.md` or `docs/roadmap.md`.

---

### Task 1: The three service sends

**Files:**
- Modify: `src/GrimoireCli/Services/BooksService.cs`
- Create: `src/GrimoireCli/Services/DownloadsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/BooksServiceTests.cs`, create `tests/GrimoireCli.Tests/Services/DownloadsServiceTests.cs`
- Commit alongside: `docs/specs/2026-09-21-remaining-binary-endpoints-design.md`, `docs/plans/2026-09-21-remaining-binary-endpoints.md`

**Interfaces:**
- Produces: `BooksService.FileAsync(string id)` and `PageAsync(string id, int page, int? width)`, both `Task<Stream>`; `DownloadsService.ArchiveAsync(string type, string? fmt, string? id, string? category, string? tag, string? resourceType, string? folder)` → `Task<Stream>`, plus `internal RequestInformation ArchiveRequest(...)` with the same parameters. Task 2 calls exactly these.

- [ ] **Step 1: Cut the branch**

```bash
git checkout -b feat/binary-endpoints
```

- [ ] **Step 2: Write the failing tests**

Append to the existing `BooksServiceTests` class, reusing its `Client()` and `Uri()` helpers:

```csharp
    [Fact]
    public void TheBinaryGettersResolveToTheirOwnPaths()
    {
        var api = Client().Api.Api.Books["b1"];
        Assert.Contains("/api/books/b1/file", Uri(api.File.ToGetRequestInformation()));
        Assert.Contains("/api/books/b1/page/7", Uri(api.Page[7].ToGetRequestInformation()));
    }

    // width is the only query parameter books page takes; a rename would render
    // at the server's default while the caller believes otherwise.
    [Fact]
    public void BooksPageSendsWidthAsAQueryParameter()
    {
        var info = Client().Api.Api.Books["b1"].Page[7].ToGetRequestInformation(
            c => c.QueryParameters.Width = 900);
        Assert.Contains("width=900", Uri(info));
    }

    [Fact]
    public void OmittedWidthSendsNoQueryString()
    {
        Assert.DoesNotContain("width=", Uri(Client().Api.Api.Books["b1"].Page[7].ToGetRequestInformation()));
    }
```

Create `tests/GrimoireCli.Tests/Services/DownloadsServiceTests.cs`, modelling its helpers on `BooksServiceTests`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// The archive endpoint selects among eleven scopes through seven query
/// parameters. A regeneration that renamed one would send a parameter the
/// server ignores — and for a scope flag that means silently exporting a wider
/// slice of the library than the caller asked for, which no response shape
/// would reveal.
/// </summary>
public class DownloadsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ArchiveResolvesToItsOwnPath()
    {
        var info = new DownloadsService(Client()).ArchiveRequest(
            "system", null, null, null, null, null, null);
        Assert.Contains("/api/downloads/archive", Uri(info));
    }

    [Fact]
    public void EveryScopeParameterKeepsItsWireName()
    {
        var info = new DownloadsService(Client()).ArchiveRequest(
            "tag_folder", "tar.gz", "s1", "core", "session-prep", "book", "errata");
        var uri = Uri(info);
        Assert.Contains("type=tag_folder", uri);
        Assert.Contains("fmt=tar.gz", uri);
        Assert.Contains("id=s1", uri);
        Assert.Contains("category=core", uri);
        Assert.Contains("tag=session-prep", uri);
        Assert.Contains("resource_type=book", uri);
        Assert.Contains("folder=errata", uri);
    }

    // An omitted scope flag must not appear at all: the server picks its own
    // default for fmt, and a stray empty parameter could change the scope.
    [Fact]
    public void OmittedScopeParametersAreAbsent()
    {
        var uri = Uri(new DownloadsService(Client()).ArchiveRequest(
            "system", null, "s1", null, null, null, null));
        Assert.Contains("type=system", uri);
        Assert.Contains("id=s1", uri);
        Assert.DoesNotContain("fmt=", uri);
        Assert.DoesNotContain("category=", uri);
        Assert.DoesNotContain("tag=", uri);
        Assert.DoesNotContain("resource_type=", uri);
        Assert.DoesNotContain("folder=", uri);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "BooksServiceTests|DownloadsServiceTests"`
Expected: compile error — `DownloadsService` does not exist.

- [ ] **Step 4: Add the two book sends to `BooksService.cs`**

Place them beside `ThumbnailAsync`:

```csharp
    /// <summary>
    /// GET /api/books/{id}/file. Serves the book as stored. Both 404s carry a
    /// detail ("Book not found", "File not found on disk"), so no notFoundHint.
    /// A missing file also flips the book's is_missing to true before the 404
    /// (routers/books/core.py:388-392).
    /// </summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Books[id].File.ToGetRequestInformation());

    /// <summary>
    /// GET /api/books/{id}/page/{n}. Renders PDF, EPUB and DjVu to WebP; serves
    /// a comic archive's page as the image member it already is, and a
    /// single-page image book as stored (routers/books/pages.py:126-159). width
    /// defaults to 1200 server-side, not the 1600 maps uses, and is left unset
    /// when the flag is omitted.
    /// </summary>
    public async Task<Stream> PageAsync(string id, int page, int? width)
        => await _client.SendStreamAsync(
            _client.Api.Api.Books[id].Page[page].ToGetRequestInformation(c => c.QueryParameters.Width = width));
```

Check the generated builder names against `src/GrimoireCli/Generated/Api/Books/Item/` — `File` may be generated under a `FileNamespace` folder as it is for maps, in which case the property is still `File`. Correct the snippet if it differs.

- [ ] **Step 5: Create `src/GrimoireCli/Services/DownloadsService.cs`**

Model the file header on `src/GrimoireCli/Services/LogsService.cs`, which is the repo's other single-send service.

```csharp
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The archive download. Guarded by get_current_user
/// (routers/downloads/core.py), so the send names no permissionHint — the
/// library_folder scope's admin check happens inside the handler and surfaces
/// as a 403 with the server's own message. Its 404 ("No files found for the
/// requested scope") is informative, so there is no notFoundHint either.
/// </summary>
public class DownloadsService
{
    private readonly GrimoireApiClient _client;

    public DownloadsService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// GET /api/downloads/archive. Which scope flags are required depends on
    /// type; the server validates the combination and answers 400 naming the
    /// missing one.
    /// </summary>
    public async Task<Stream> ArchiveAsync(
        string type, string? fmt, string? id, string? category, string? tag, string? resourceType, string? folder)
        => await _client.SendStreamAsync(
            ArchiveRequest(type, fmt, id, category, tag, resourceType, folder));

    /// <summary>
    /// Internal so a test can pin all seven query-parameter wire names. A
    /// renamed scope parameter would be dropped by the server and silently
    /// widen the exported slice, which no response shape would reveal.
    /// </summary>
    internal RequestInformation ArchiveRequest(
        string type, string? fmt, string? id, string? category, string? tag, string? resourceType, string? folder)
        => _client.Api.Api.Downloads.Archive.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Type = type;
            c.QueryParameters.Fmt = fmt;
            c.QueryParameters.Id = id;
            c.QueryParameters.Category = category;
            c.QueryParameters.Tag = tag;
            c.QueryParameters.ResourceType = resourceType;
            c.QueryParameters.Folder = folder;
        });
}
```

Verify the generated query-parameter property names against `src/GrimoireCli/Generated/Api/Downloads/Archive/` before writing — the wire names are `type`, `fmt`, `id`, `category`, `tag`, `resource_type`, `folder`, and Kiota's C# properties for them are expected to be `Type`, `Fmt`, `Id`, `Category`, `Tag`, `ResourceType`, `Folder`. Correct the snippet where it differs.

- [ ] **Step 6: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build clean, everything passes.

- [ ] **Step 7: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add docs/specs/2026-09-21-remaining-binary-endpoints-design.md \
        docs/plans/2026-09-21-remaining-binary-endpoints.md \
        src/GrimoireCli/Services/ tests/GrimoireCli.Tests/Services/
git commit -m "feat: add the book file, page and archive sends"
```

---

### Task 2: The three commands

**Files:**
- Modify: `src/GrimoireCli/Commands/BooksCommand.cs`, `src/GrimoireCli/Program.cs`
- Create: `src/GrimoireCli/Commands/DownloadsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`, create `tests/GrimoireCli.Tests/Commands/DownloadsCommandTests.cs`

**Interfaces:**
- Consumes: `BooksService.FileAsync`, `BooksService.PageAsync`, `DownloadsService.ArchiveAsync` from Task 1.
- Produces: `file` and `page` on the `books` group; `DownloadsCommand.Create()` returning a `downloads` group with one `archive` subcommand, registered in `Program.cs`.

- [ ] **Step 1: Write the failing tests**

Append to `BooksCommandTests.cs`, using that file's own help-render helper:

```csharp
    [Theory]
    [InlineData("file")]
    [InlineData("page")]
    public void TheGroupHostsTheBinaryGetters(string leaf)
    {
        Assert.Contains(leaf, BooksCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void TheBinaryGettersRequireAnOutput()
    {
        var books = BooksCommand.Create();
        Assert.NotEmpty(books.Parse(["file", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["file", "--id", "b1", "--output", "-"]).Errors);
        Assert.NotEmpty(books.Parse(["page", "--id", "b1", "--page", "1"]).Errors);
        Assert.Empty(books.Parse(["page", "--id", "b1", "--page", "1", "--output", "-"]).Errors);
    }

    [Fact]
    public void BooksPageRequiresAPageAndTakesAWidth()
    {
        var books = BooksCommand.Create();
        Assert.NotEmpty(books.Parse(["page", "--id", "b1", "--output", "-"]).Errors);
        Assert.Empty(books.Parse(["page", "--id", "b1", "--page", "2", "--width", "800", "--output", "-"]).Errors);
    }

    // 1200, not the 1600 maps page uses — a caller who assumes parity gets a
    // different image and no error.
    [Fact]
    public void BooksPageStatesItsOwnWidthDefault()
    {
        var help = HelpRenderer.Render(BooksCommand.Create(), ["books", "page"], full: false);
        Assert.Contains("1200", help);
        Assert.Contains("3000", help);
    }

    // The branch that makes this the complement to page-text and page-words.
    [Fact]
    public void BooksPageSaysItServesComics()
    {
        Assert.Contains("comic",
            HelpRenderer.Render(BooksCommand.Create(), ["books", "page"], full: false));
    }

    // A GET that writes: both flip is_missing when the file is gone.
    [Theory]
    [InlineData("file")]
    [InlineData("page")]
    public void TheBinaryGettersWarnTheyMarkAMissingFile(string leaf)
    {
        Assert.Contains("is_missing",
            HelpRenderer.Render(BooksCommand.Create(), ["books", leaf], full: false));
    }
```

Create `tests/GrimoireCli.Tests/Commands/DownloadsCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// The archive command's help is the only place the eleven scopes and their
/// required flags are written down, so the tests treat that table as part of
/// the interface rather than as prose.
/// </summary>
public class DownloadsCommandTests
{
    private static string Help(bool full = false) =>
        HelpRenderer.Render(DownloadsCommand.Create(), ["downloads", "archive"], full);

    [Fact]
    public void TheGroupHostsArchive()
    {
        Assert.Equal(["archive"], DownloadsCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void ArchiveRequiresATypeAndAnOutput()
    {
        var downloads = DownloadsCommand.Create();
        Assert.NotEmpty(downloads.Parse(["archive", "--output", "a.zip"]).Errors);
        Assert.NotEmpty(downloads.Parse(["archive", "--type", "system"]).Errors);
        Assert.Empty(downloads.Parse(["archive", "--type", "system", "--id", "s1", "--output", "a.zip"]).Errors);
    }

    // Every scope flag is optional at parse time: which ones a type needs is
    // the server's rule, and it answers 400 naming the missing one.
    [Fact]
    public void EveryScopeFlagParsesAndNoneIsValidatedClientSide()
    {
        Assert.Empty(DownloadsCommand.Create().Parse([
            "archive", "--type", "tag_folder", "--fmt", "tar.gz", "--id", "s1",
            "--category", "core", "--tag", "session-prep", "--resource-type", "book",
            "--folder", "errata", "--output", "-"]).Errors);
        // A type the server will reject still parses — no client-side set.
        Assert.Empty(DownloadsCommand.Create().Parse(
            ["archive", "--type", "sausage", "--output", "-"]).Errors);
        Assert.Empty(DownloadsCommand.Create().Parse(
            ["archive", "--type", "system", "--fmt", "sausage", "--output", "-"]).Errors);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("system_category")]
    [InlineData("book_folder")]
    [InlineData("map_folder")]
    [InlineData("token_folder")]
    [InlineData("audio_folder")]
    [InlineData("model_folder")]
    [InlineData("library_folder")]
    [InlineData("tag")]
    [InlineData("tag_type")]
    [InlineData("tag_folder")]
    public void TheHelpNamesEveryScope(string scope)
    {
        Assert.Contains(scope, Help());
    }

    [Fact]
    public void TheHelpNamesEveryFormat()
    {
        var help = Help();
        foreach (var fmt in new[] { "zip", "tar", "tar.gz", "tar.bz2" })
            Assert.Contains(fmt, help);
    }

    // library_folder is the one scope with a role rule, enforced inside the
    // handler rather than on the route, so the command carries no role tag.
    [Fact]
    public void TheHelpFlagsTheAdminOnlyScopeButTheCommandHasNoRole()
    {
        Assert.Contains("admin", Help());
        Assert.DoesNotContain("Role required:", Help(full: true));
    }

    [Fact]
    public void ArchiveCarriesTheSavedFileShape()
    {
        Assert.Contains("Response shape:", Help(full: true));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: compile error — `DownloadsCommand` does not exist.

- [ ] **Step 3: Add `books file` and `books page`**

Register both in `BooksCommand.Create()` after `thumbnail`, then:

```csharp
    private static Command CreateFileCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("file", "Download the book file as stored")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The original file in whatever format the library holds — use books",
            "page to render a page as an image.",
            "",
            "A file missing from disk is 404, and the book's is_missing is set",
            "to true on the way out.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli books file --id <book-id> --output handbook.pdf",
            "grimoire-cli books file --id <book-id> --output - > handbook.pdf");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            await using var stream = await service.FileAsync(parseResult.GetValue(idOption)!);
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }

    private static Command CreatePageCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var pageOption = new Option<int>("--page") { Description = "1-based page number", Required = true };
        var widthOption = new Option<int?>("--width") { Description = "Target pixel width" };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("page", "Render or extract one page as an image")
        {
            idOption, pageOption, widthOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "PDF, EPUB and DjVu render to WebP. A comic archive's page is served",
            "as the image it already is, and a single-page image book as stored,",
            "page 1 only. Anything else is 404 — which makes this the one page",
            "read that works on a comic, where page-text and page-words do not.",
            "",
            "--width defaults to 1200 and is capped at 3000. maps page defaults",
            "to 1600, so the two are not interchangeable.",
            "",
            "A file missing from disk is 404, and the book's is_missing is set",
            "to true on the way out.",
            "",
            "--output - writes the image to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli books page --id <book-id> --page 241 --output p241.webp",
            "grimoire-cli books page --id <book-id> --page 1 --width 800 --output -");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            await using var stream = await service.PageAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(pageOption),
                parseResult.GetValue(widthOption));
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }
```

- [ ] **Step 4: Create `src/GrimoireCli/Commands/DownloadsCommand.cs`**

Model the file header on `src/GrimoireCli/Commands/CoverCommands.cs` — same usings, same `_logger` field.

```csharp
public static class DownloadsCommand
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("downloads", "Bulk downloads");
        command.Subcommands.Add(CreateArchiveCommand());
        return command;
    }

    private static Command CreateArchiveCommand()
    {
        var typeOption = new Option<string>("--type") { Description = "Which slice of the library to archive", Required = true };
        var fmtOption = new Option<string?>("--fmt") { Description = "Archive format; the server uses zip when omitted" };
        var idOption = new Option<string?>("--id") { Description = "System ID" };
        var categoryOption = new Option<string?>("--category") { Description = "Book category slug" };
        var tagOption = new Option<string?>("--tag") { Description = "Tag internal key, from tags list" };
        var resourceTypeOption = new Option<string?>("--resource-type") { Description = "Restrict a tag scope to one resource type" };
        var folderOption = new Option<string?>("--folder") { Description = "Folder path" };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("archive", "Download a slice of the library as one archive")
        {
            typeOption, fmtOption, idOption, categoryOption, tagOption,
            resourceTypeOption, folderOption, outputOption
        };
        command.AddHelpSection("Scopes", HelpSectionPosition.Top,
            "--type selects the slice, and decides which other flags it needs:",
            "",
            "  system            --id",
            "  system_category   --id --category",
            "  book_folder       --id --folder",
            "  map_folder        --folder",
            "  token_folder      --folder",
            "  audio_folder      --folder",
            "  model_folder      --folder",
            "  library_folder    --folder   (admin; any folder on disk, indexed",
            "                                or not, unfiltered by book access)",
            "  tag               --tag",
            "  tag_type          --tag --resource-type",
            "  tag_folder        --tag --resource-type --folder",
            "",
            "A missing one is 400 naming the flag and the type.");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--fmt takes zip, tar, tar.gz or tar.bz2.",
            "",
            "A scope that resolves to no files is 404, including a container",
            "system that holds no books of its own.",
            "",
            "--output - writes the archive to stdout; a path writes it and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli downloads archive --type system --id <system-id> --output system.zip",
            "grimoire-cli downloads archive --type tag --tag session-prep --output prep.zip",
            "grimoire-cli downloads archive --type tag_type --tag session-prep --resource-type map --fmt tar.gz --output maps.tar.gz");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new DownloadsService(client);
            await using var stream = await service.ArchiveAsync(
                parseResult.GetValue(typeOption)!,
                parseResult.GetValue(fmtOption),
                parseResult.GetValue(idOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(tagOption),
                parseResult.GetValue(resourceTypeOption),
                parseResult.GetValue(folderOption));
            try
            {
                await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 5: Register the group in `Program.cs`**

Add `rootCommand.Subcommands.Add(DownloadsCommand.Create());` beside the other group registrations, in the order the file already uses.

- [ ] **Step 6: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 7: Read the rendered help, not the source**

```bash
dotnet run --project src/GrimoireCli -- books file --help
dotnet run --project src/GrimoireCli -- books page --help
dotnet run --project src/GrimoireCli -- downloads archive --help
dotnet run --project src/GrimoireCli -- --help | head -30
```

Check: none shows a Role required section; the scope table renders as a readable block rather than reflowed prose; `downloads` appears in the root help. Trim anything that restates a flag description, except a line a Step 1 assertion depends on.

- [ ] **Step 8: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add src/GrimoireCli/Commands/ src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add books file, books page and downloads archive"
```

---

### Task 3: Smoke coverage and the live checks

**Files:**
- Modify: `docker/smoke-test.sh` (append a `--- binary endpoints ---` block before the final `echo "smoke: all checks passed"`)

**Constraint:** reads only. Downloads go to `$WORK`, a fresh mktemp dir per run, so nothing needs restoring and nothing can drift.

- [ ] **Step 1: Bring up the stack if it is not already up**

```bash
docker compose -f docker/docker-compose.yml ps
# only if down:
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- [ ] **Step 2: Run the live checks by hand and record the answers**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh >/dev/null   # logs in as admin
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
PROBE=$(mktemp -d)

BOOK=$($CLI books list | jq -r '.books[0].id')
SYS=$($CLI systems list | jq -r '.[0].id')

$CLI books file --id "$BOOK" --output "$PROBE/book.pdf"; echo "file exit=$?"
$CLI books page --id "$BOOK" --page 1 --output "$PROBE/p1.webp"; echo "page1 exit=$?"
$CLI books page --id "$BOOK" --page 1 --width 400 --output "$PROBE/p1w.webp"; echo "width exit=$?"
ls -l "$PROBE"/p1.webp "$PROBE"/p1w.webp    # does --width change the bytes?
$CLI books page --id "$BOOK" --page 99 --output "$PROBE/p99.webp"; echo "page99 exit=$? (expect non-zero)"

$CLI downloads archive --type system --id "$SYS" --output "$PROBE/sys.zip"; echo "archive exit=$?"
head -c 2 "$PROBE/sys.zip" | xxd | head -1        # expect the zip magic PK
unzip -l "$PROBE/sys.zip" | tail -3

$CLI downloads archive --type system --id "no-such-system" --output "$PROBE/x.zip"; echo "bad id exit=$?"
$CLI downloads archive --type system --id "$SYS" --fmt sausage --output "$PROBE/x.zip"; echo "bad fmt exit=$?"
$CLI downloads archive --type sausage --output "$PROBE/x.zip"; echo "bad type exit=$?"

# Is there a tag that resolves to files? If so, --type tag is worth asserting.
$CLI tags list | jq -r '.tags[] | select(.count > 0) | "\(.internal)\t\(.count)"' | head -5
# And does a container system with no direct books really 404, as #48 claims?
$CLI systems list --include-children | jq -r '.[] | "\(.name)\t\(.id)\t\(.book_count)"' | head -10
```

Record every exit code and body. Three findings decide what the smoke block asserts, and your report must state all three:

- whether `--width` visibly changes the rendered bytes on a fixture page;
- whether a tag resolves to a non-empty archive — if so, assert `--type tag`; if not, say so and leave it out;
- whether a container system holding no books directly 404s. If the fixtures have such a system, assert it; that is #48's own claim and it is currently unverified.

- [ ] **Step 3: Append the smoke block**

Insert before the final `echo "smoke: all checks passed" >&2`. Adjust every `jq` path to what the responses actually carry.

```bash
# --- binary endpoints --------------------------------------------------------
# Reads only; downloads land in $WORK, which is fresh each run.
BE_BOOK=$("$CLI" books list 2>"$WORK/cli.err" | jq -r '.books[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "books list exited non-zero"; }
[ -n "$BE_BOOK" ] && [ "$BE_BOOK" != "null" ] || fail "no book fixture for the binary checks"

"$CLI" books file --id "$BE_BOOK" --output "$WORK/book.bin" >"$WORK/bookdl.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books file exited non-zero"; }
[ -s "$WORK/book.bin" ] || fail "books file wrote an empty file"
[ "$(jq -r .bytes "$WORK/bookdl.out")" -gt 0 ] \
  || fail "books file should report a byte count: $(cat "$WORK/bookdl.out")"
ok "books file downloads the book and reports its size"

"$CLI" books page --id "$BE_BOOK" --page 1 --output "$WORK/bookpage.webp" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books page exited non-zero"; }
[ -s "$WORK/bookpage.webp" ] || fail "books page wrote an empty file"
ok "books page renders page 1"

"$CLI" books page --id "$BE_BOOK" --page 99 --output "$WORK/nope.webp" >/dev/null 2>&1 \
  && fail "books page should refuse a page past the end"
ok "books page refuses a page past the end"

# The archive is the one endpoint that exports a whole slice in a call.
BE_SYS=$("$CLI" systems list 2>/dev/null | jq -r '.[0].id')
[ -n "$BE_SYS" ] && [ "$BE_SYS" != "null" ] || fail "no system fixture for the archive check"
"$CLI" downloads archive --type system --id "$BE_SYS" --output "$WORK/sys.zip" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "downloads archive exited non-zero"; }
[ -s "$WORK/sys.zip" ] || fail "downloads archive wrote an empty file"
[ "$(head -c 2 "$WORK/sys.zip")" = "PK" ] \
  || fail "a zip archive should start with the PK magic: $(head -c 16 "$WORK/sys.zip" | od -c | head -1)"
ok "downloads archive exports a system as a zip"

"$CLI" downloads archive --type system --id "no-such-system" --output "$WORK/x.zip" >/dev/null 2>&1 \
  && fail "an archive scope that resolves to nothing should fail"
ok "downloads archive refuses an empty scope"

"$CLI" downloads archive --type system --id "$BE_SYS" --fmt sausage --output "$WORK/x.zip" >/dev/null 2>&1 \
  && fail "downloads archive should refuse an unknown format"
ok "downloads archive refuses an unknown format"

"$CLI" downloads archive --type sausage --output "$WORK/x.zip" >/dev/null 2>&1 \
  && fail "downloads archive should refuse an unknown scope type"
ok "downloads archive refuses an unknown scope type"
```

Add the `--type tag` and container-system assertions only if Step 2 found fixtures that support them. If it did not, leave them out and record the gap in your report rather than asserting something the stack cannot back.

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed` both times. Strictly sequential, never concurrent — the script rewrites `$HOME/.grimoire-cli/config.json`.

- [ ] **Step 5: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the binary endpoints in the smoke test"
```

---

### Task 4: Docs

**Files:**
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`

`docs/roadmap.md` is not touched.

- [ ] **Step 1: Add the README rows**

The two book rows go with the books group; the archive row needs its own placement — put it where the table's ordering puts a new top-level group, and match the surrounding style.

```markdown
| `books file --id <id> --output <path\|->` | Download the book file as stored |
| `books page --id <id> --page <n> [--width <px>] --output <path\|->` | Render or extract one page as an image |
| `downloads archive --type <type> [scope flags] [--fmt <fmt>] --output <path\|->` | Download a slice of the library as one archive |
```

Check the `<path\|->` spelling against the existing streaming rows.

- [ ] **Step 2: Add the coverage entries**

```python
    "GET /api/books/{book_id}/file": "`books file` ✅",
    "GET /api/books/{book_id}/page/{page_num}": "`books page` ✅",
    "GET /api/downloads/archive": "`downloads archive` ✅",
```

Check the path-parameter names against the existing rows.

- [ ] **Step 3: Regenerate the coverage table**

```bash
tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly the three rows change, plus the derived `books`, `downloads` and total lines. **`books` should reach 16/16 and `downloads` 1/1.** Any other row moving means the pin or the clone moved — stop and report.

- [ ] **Step 4: Record the verified behaviour**

Add to `docs/grimoire-api-notes.md`, matching the file's heading style and placement. The book bullets belong with the existing book-reading material; the archive warrants its own section.

```markdown
- `GET /api/books/{id}/file` and `GET /api/books/{id}/page/{n}` both **write on
  a missing file**: they set the book's `is_missing` to true and commit before
  raising 404 (`routers/books/core.py:388-392`, `pages.py:130,141`, v1.7.1). A
  read that mutates is worth knowing about when a scripted sweep hits a library
  whose files have moved.
- `GET /api/books/{id}/page/{n}` defaults `width` to **1200**, where the maps
  equivalent defaults to 1600; both cap at 3000 (`pages.py:106`). It renders
  PDF, EPUB and DjVu, serves a comic archive's page as the stored image member
  without rendering (`pages.py:148-156`), and serves a single-page image book
  as stored on page 1 only (`pages.py:126-134`). That makes it the one page
  read that works on a comic.

## Archive downloads

- `GET /api/downloads/archive` selects among eleven scopes through `type`, each
  requiring a different combination of `id`, `category`, `tag`,
  `resource_type` and `folder` (`routers/downloads/core.py:27-144`). A missing
  one is 400 naming both the flag and the type.
- `library_folder` is admin-only, checked inside the handler rather than on the
  route, and reads a folder as it sits on disk — including files the scanner
  never indexed, unfiltered by book visibility, because nothing in it resolves
  through a book row (`core.py:131-141`).
- An empty scope is 404 "No files found for the requested scope"; an unknown
  `fmt` is 400 listing the valid ones (`routers/downloads/_helpers.py:98-101`).
- The response streams as the archive is built, so the first byte does not wait
  for the whole archive.
```

Replace any line with what Task 3 actually observed if the stack disagrees, and say so in the PR body.

- [ ] **Step 5: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md
git commit -m "docs: record the binary endpoint commands"
```

---

### Task 5: Pre-PR verification and the PR

- [ ] **Step 1: Prove the branch carries no attribution**

```bash
git log --format='%B' $(git merge-base main HEAD)..HEAD | grep -niE '^Co-Authored-By:|Generated with \[Claude|🤖|noreply@anthropic'
```

Expected: **no output.** If anything matches, stop and report — fixing it before the push is cheap and after the merge is not.

- [ ] **Step 2: Run all four checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report the actual output; do not claim a pass that was not run.

- [ ] **Step 3: Open the PR**

```bash
git push -u origin feat/binary-endpoints
gh pr create --title "feat: remaining binary endpoints" --body "…"
```

The body names the three commands, says this finishes the `books` group, explains the archive's scope table and why it is one command rather than eleven, notes the `is_missing` write side effect and the 1200-versus-1600 width difference, and records the verification that was run. **No attribution line of any kind.**

- [ ] **Step 4: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked, and present the PR URL as a clickable link.
