# small completions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three leftovers in [#44](https://github.com/thomaslazar/grimoire-cli/issues/44) — `library stats`, `systems cover from-source`, and the binary getters the collection layers shipped without — as ten commands.

**Architecture:** Seven of the ten are applications of the settled `--output` streaming convention; two are ordinary JSON pass-throughs and one is a small JSON write. `maps vtt` becomes a subgroup in its own file, beside `CoverCommands.cs`.

**Tech Stack:** C# / .NET, System.CommandLine, Kiota-generated client, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-20-small-completions-design.md](../specs/2026-09-20-small-completions-design.md)

## Global Constraints

- Branch: `feat/small-completions`, cut from `main`. Never commit to `main`.
- Conventional Commits, imperative, lowercase, no period, ~72 chars. No `Co-Authored-By:` and no "Generated with Claude Code" lines.
- The spec and this plan are committed **on the feature branch**, in Task 1's commit.
- Only `systems cover from-source` carries a role: `AddRoleRequired("gm or admin")` plus `permissionHint: "the gm or admin role"`. Every other command here is guarded by `get_current_user` and gets **no** role tag and **no** permissionHint.
- The streaming commands follow the existing convention exactly: `--output` required, `-` for stdout, `SavedFile` response example, `SendStreamAsync`, `BodyInputException` caught and mapped to exit 1. Copy the shape from `MapsCommand.CreateThumbnailCommand`.
- `maps vtt data` is the one JSON command among the maps additions: no `--output`, no `SavedFile`.
- Run `dotnet format GrimoireCli.sln` after touching any C# file.
- Help text is terse: no line restating a flag's own description or a field visible in the rendered response sample.
- Never touch `CHANGELOG.md`. `docs/roadmap.md` is edited only in Task 4, as specified there.
- Writes go to the local Docker stack only, never any other instance.

---

### Task 1: The ten service methods

**Files:**
- Modify: `src/GrimoireCli/Services/LibraryService.cs`, `SystemsService.cs`, `MapsService.cs`, `AudioService.cs`, `TokensService.cs`, `ModelsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/MapsServiceTests.cs`, `LibraryServiceTests.cs`
- Commit alongside: `docs/specs/2026-09-20-small-completions-design.md`, `docs/plans/2026-09-20-small-completions.md`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, ...)` and `SendStreamAsync(RequestInformation, ...)`, both already used across these services.
- Produces: `LibraryService.StatsAsync()`, `SystemsService.CoverFromSourceAsync(string id, string sourceType, string sourceId)`, `MapsService.FileAsync(string id)`, `PageAsync(string id, int page, int? width)`, `VttImageAsync(string id)`, `VttDataAsync(string id)`, `VttExportAsync(string id)`, `AudioService.FileAsync(string id)`, `TokensService.FileAsync(string id)`, `ModelsService.FileAsync(string id)`. Everything but `StatsAsync`, `CoverFromSourceAsync` and `VttDataAsync` returns `Task<Stream>`; those three return `Task<string>`.

- [ ] **Step 1: Cut the branch**

```bash
git checkout -b feat/small-completions
```

- [ ] **Step 2: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Services/MapsServiceTests.cs`, inside the existing class. It already has a client helper and a URI helper — reuse whatever they are named there rather than adding duplicates; read the file first.

```csharp
    [Fact]
    public void EachBinaryGetterResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Maps["m1"];
        Assert.Contains("/api/maps/m1/file", Uri(api.File.ToGetRequestInformation()));
        Assert.Contains("/api/maps/m1/page/2", Uri(api.Page[2].ToGetRequestInformation()));
        Assert.Contains("/api/maps/m1/vtt/image", Uri(api.Vtt.Image.ToGetRequestInformation()));
        Assert.Contains("/api/maps/m1/vtt/data", Uri(api.Vtt.Data.ToGetRequestInformation()));
        Assert.Contains("/api/maps/m1/export.uvtt", Uri(api.ExportUvtt.ToGetRequestInformation()));
    }

    // width is the only query parameter these getters take; a regeneration that
    // renamed it would silently render at the server's default instead.
    [Fact]
    public void PageSendsWidthAsAQueryParameter()
    {
        var info = Client().Api.Api.Maps["m1"].Page[2].ToGetRequestInformation(
            c => c.QueryParameters.Width = 800);
        Assert.Contains("width=800", Uri(info));
    }

    // An omitted --width must send no width at all, so the server applies its
    // own default rather than the CLI pinning one.
    [Fact]
    public void OmittedWidthSendsNoQueryString()
    {
        Assert.DoesNotContain("width=", Uri(Client().Api.Api.Maps["m1"].Page[2].ToGetRequestInformation()));
    }
```

Append to `tests/GrimoireCli.Tests/Services/LibraryServiceTests.cs`, again reusing that file's existing helpers:

```csharp
    [Fact]
    public void StatsResolvesToTheStatsPath()
    {
        var info = Client().Api.Api.Stats.ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test/api/stats", info.URI.AbsoluteUri);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "MapsServiceTests|LibraryServiceTests"`
Expected: FAIL or compile error, depending on which helpers the files already provide.

- [ ] **Step 4: Add `StatsAsync` to `LibraryService.cs`**

```csharp
    /// <summary>
    /// GET /api/stats. Guarded by get_current_user, so it names no
    /// permissionHint. total_size_mb is books only; library_size_mb covers every
    /// collection (routers/library/_schemas.py:77-79).
    /// </summary>
    public async Task<string> StatsAsync()
        => await _client.SendAsync(_client.Api.Api.Stats.ToGetRequestInformation());
```

- [ ] **Step 5: Add `CoverFromSourceAsync` to `SystemsService.cs`**

Place it beside the existing cover methods:

```csharp
    /// <summary>
    /// POST /api/systems/{id}/cover/from-source. Copies the chosen bytes in as
    /// an upload does, so a folder cover.* still wins
    /// (routers/systems/covers.py:161-181).
    /// </summary>
    public async Task<string> CoverFromSourceAsync(string id, string sourceType, string sourceId)
    {
        var body = new Generated.Models.SystemCoverSourceIn { SourceType = sourceType, SourceId = sourceId };
        var info = _client.Api.Api.Systems[id].Cover.FromSource.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: "the gm or admin role", notFoundHint: NotFoundHint);
    }
```

Use whatever the file's existing system-not-found hint constant is named; read it rather than inventing one.

- [ ] **Step 6: Add the five map getters to `MapsService.cs`**

Place them beside `ThumbnailAsync`:

```csharp
    /// <summary>GET /api/maps/{id}/file. Streams the map file as stored.</summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Maps[id].File.ToGetRequestInformation());

    /// <summary>
    /// GET /api/maps/{id}/page/{n}. Renders a PDF page to WebP; an image map
    /// streams as-is and accepts page 1 only. width is left unset when the flag
    /// is omitted so the server applies its own default.
    /// </summary>
    public async Task<Stream> PageAsync(string id, int page, int? width)
        => await _client.SendStreamAsync(
            _client.Api.Api.Maps[id].Page[page].ToGetRequestInformation(c => c.QueryParameters.Width = width));

    /// <summary>
    /// GET /api/maps/{id}/vtt/image. Decodes the base64 battlemap out of a
    /// .uvtt/.dd2vtt; 400 for any other map.
    /// </summary>
    public async Task<Stream> VttImageAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Maps[id].Vtt.Image.ToGetRequestInformation());

    /// <summary>
    /// GET /api/maps/{id}/vtt/data. JSON, not a download: the grid and feature
    /// counts with the embedded image omitted.
    /// </summary>
    public async Task<string> VttDataAsync(string id)
        => await _client.SendAsync(_client.Api.Api.Maps[id].Vtt.Data.ToGetRequestInformation());

    /// <summary>
    /// GET /api/maps/{id}/export.uvtt. A download whose payload is JSON carrying
    /// a base64 image, so it goes through the stream path like any other file.
    /// </summary>
    public async Task<Stream> VttExportAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Maps[id].ExportUvtt.ToGetRequestInformation());
```

If `MapsService` names a not-found hint for its keyed routes, pass it on all five exactly as `ThumbnailAsync` does; match that method rather than these snippets where they differ.

- [ ] **Step 7: Add `FileAsync` to `AudioService.cs`, `TokensService.cs` and `ModelsService.cs`**

One per file, each beside that service's existing thumbnail or artwork method, and each matching its hint usage:

```csharp
    /// <summary>GET /api/audio/{id}/file. Streams the audio file.</summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Audio[id].File.ToGetRequestInformation());
```

```csharp
    /// <summary>GET /api/tokens/{id}/file. Streams the token image as stored.</summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Tokens[id].File.ToGetRequestInformation());
```

```csharp
    /// <summary>GET /api/models/{id}/file. Streams the 3D model file as stored.</summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.ModelsRequests[id].File.ToGetRequestInformation());
```

The models builder is `ModelsRequests`, not `Models` — the generator renames it to avoid colliding with the models namespace. Check each of the three against its own generated builder before writing, and correct the snippet if the property differs.

- [ ] **Step 8: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build clean, everything passes.

- [ ] **Step 9: Commit**

```bash
git add docs/specs/2026-09-20-small-completions-design.md docs/plans/2026-09-20-small-completions.md \
        src/GrimoireCli/Services/ tests/GrimoireCli.Tests/Services/
git commit -m "feat: add the stats, cover-source and binary getter sends"
```

---

### Task 2: The ten commands

**Files:**
- Create: `src/GrimoireCli/Commands/MapVttCommands.cs`
- Modify: `src/GrimoireCli/Commands/LibraryCommand.cs`, `CoverCommands.cs`, `MapsCommand.cs`, `AudioCommand.cs`, `TokensCommand.cs`, `ModelsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/LibraryCommandTests.cs`, `CoverCommandTests.cs`, `MapsCommandTests.cs`, `AudioCommandTests.cs`, `TokensCommandTests.cs`, `ModelsCommandTests.cs`

**Interfaces:**
- Consumes: every method Task 1 produced, plus `ConsoleOutput.WriteStreamAsync(Stream, string)`, `ConsoleOutput.WriteRawJson(string)`, `CommandHelper.BuildClient()`, `AddHelpSection`, `AddExamples`, `AddResponseExample<T>`, `AddRoleRequired` — all already used in these files.
- Produces: `MapVttCommands.Create()` returning the `vtt` subgroup command, registered by `MapsCommand.Create()`.

**The streaming shape.** Seven commands share it. Read `MapsCommand.CreateThumbnailCommand` first and copy it exactly: the `--output` option's description is `"Output file path, or '-' for binary to stdout"`, it is `Required = true`, the action catches `BodyInputException`, logs `ex.Message` and returns 1, and the command registers `AddResponseExample<SavedFile>()`. Every file that gains one of these needs `GrimoireCli.Models` in its usings for `SavedFile`, and a `_logger` field if it has none.

- [ ] **Step 1: Write the failing tests**

In `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`:

```csharp
    [Fact]
    public void TheGroupHostsTheNewGetters()
    {
        var names = MapsCommand.Create().Subcommands.Select(c => c.Name).ToArray();
        Assert.Contains("file", names);
        Assert.Contains("page", names);
        Assert.Contains("vtt", names);
    }

    [Fact]
    public void FileAndPageRequireAnOutput()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["file", "--id", "m1"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(["file", "--id", "m1", "--output", "m.png"]).Errors);
        Assert.NotEmpty(MapsCommand.Create().Parse(["page", "--id", "m1", "--page", "1"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(["page", "--id", "m1", "--page", "1", "--output", "-"]).Errors);
    }

    [Fact]
    public void PageRequiresAPageNumberAndTakesAWidth()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["page", "--id", "m1", "--output", "-"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(
            ["page", "--id", "m1", "--page", "2", "--width", "800", "--output", "-"]).Errors);
    }

    // An image map accepts page 1 only, and --width has a server-side ceiling;
    // both are things a caller cannot read off the flags.
    [Fact]
    public void PageDocumentsTheImageMapAndWidthLimits()
    {
        var help = HelpRenderer.Render(MapsCommand.Create(), ["maps", "page"], full: false);
        Assert.Contains("page 1", help);
        Assert.Contains("3000", help);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("data")]
    [InlineData("export")]
    public void TheVttSubgroupHostsItsThreeVerbs(string leaf)
    {
        var vtt = MapsCommand.Create().Subcommands.Single(c => c.Name == "vtt");
        Assert.Contains(leaf, vtt.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void VttImageAndExportStreamButDataDoesNot()
    {
        var maps = MapsCommand.Create();
        Assert.Empty(maps.Parse(["vtt", "image", "--id", "m1", "--output", "-"]).Errors);
        Assert.Empty(maps.Parse(["vtt", "export", "--id", "m1", "--output", "-"]).Errors);
        Assert.Empty(maps.Parse(["vtt", "data", "--id", "m1"]).Errors);
        // data is JSON on stdout; an --output flag would imply a file it never writes.
        Assert.NotEmpty(maps.Parse(["vtt", "data", "--id", "m1", "--output", "-"]).Errors);
    }

    // Both refuse a map that is not a Universal VTT, which is most maps.
    [Theory]
    [InlineData("image")]
    [InlineData("data")]
    public void TheVttGettersWarnTheyNeedAUniversalVttMap(string leaf)
    {
        var help = HelpRenderer.Render(MapsCommand.Create(), ["maps", "vtt", leaf], full: false);
        Assert.Contains("400", help);
    }

    [Fact]
    public void VttExportDocumentsWhatItRefuses()
    {
        var help = HelpRenderer.Render(MapsCommand.Create(), ["maps", "vtt", "export"], full: false);
        Assert.Contains("PDF", help);
    }

    [Theory]
    [InlineData(new[] { "maps", "file" })]
    [InlineData(new[] { "maps", "page" })]
    [InlineData(new[] { "maps", "vtt", "image" })]
    [InlineData(new[] { "maps", "vtt", "export" })]
    public void EveryStreamingGetterCarriesTheSavedFileShape(string[] path)
    {
        Assert.Contains("Response shape:", HelpRenderer.Render(MapsCommand.Create(), path, full: true));
    }

    [Theory]
    [InlineData(new[] { "maps", "file" })]
    [InlineData(new[] { "maps", "page" })]
    [InlineData(new[] { "maps", "vtt", "image" })]
    [InlineData(new[] { "maps", "vtt", "data" })]
    [InlineData(new[] { "maps", "vtt", "export" })]
    public void NoNewMapGetterDeclaresARole(string[] path)
    {
        Assert.DoesNotContain("Role required:", HelpRenderer.Render(MapsCommand.Create(), path, full: true));
    }
```

In `AudioCommandTests.cs`, `TokensCommandTests.cs` and `ModelsCommandTests.cs`, one pair each — substitute the group's own command class and name:

```csharp
    [Fact]
    public void TheGroupHostsFile()
    {
        Assert.Contains("file", AudioCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void FileRequiresAnOutput()
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["file", "--id", "a1"]).Errors);
        Assert.Empty(AudioCommand.Create().Parse(["file", "--id", "a1", "--output", "-"]).Errors);
    }
```

In `LibraryCommandTests.cs`:

```csharp
    [Fact]
    public void TheGroupHostsStats()
    {
        Assert.Contains("stats", LibraryCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void StatsParsesWithNoArgumentsAndDeclaresNoRole()
    {
        Assert.Empty(LibraryCommand.Create().Parse(["stats"]).Errors);
        Assert.DoesNotContain("Role required:",
            HelpRenderer.Render(LibraryCommand.Create(), ["library", "stats"], full: true));
    }

    // The two size fields differ and nothing in the response says so.
    [Fact]
    public void StatsExplainsTheTwoSizeFields()
    {
        var help = HelpRenderer.Render(LibraryCommand.Create(), ["library", "stats"], full: false);
        Assert.Contains("total_size_mb", help);
        Assert.Contains("library_size_mb", help);
    }
```

In `CoverCommandTests.cs`:

```csharp
    [Fact]
    public void TheCoverGroupHostsFromSource()
    {
        Assert.Contains("from-source", CoverCommands.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void FromSourceRequiresBothSourceFlags()
    {
        var cover = CoverCommands.Create();
        Assert.NotEmpty(cover.Parse(["from-source", "--id", "s1"]).Errors);
        Assert.NotEmpty(cover.Parse(["from-source", "--id", "s1", "--source-type", "book"]).Errors);
        Assert.Empty(cover.Parse(
            ["from-source", "--id", "s1", "--source-type", "book", "--source-id", "b1"]).Errors);
    }

    [Fact]
    public void FromSourceDeclaresTheGmOrAdminRole()
    {
        var help = HelpRenderer.Render(CoverCommands.Create(), ["cover", "from-source"], full: false);
        Assert.Contains("Role required:", help);
        Assert.Contains("gm or admin", help);
    }

    // Folder art wins over what this sets, and the API's fifth source type is
    // unreachable here — neither is visible from the flags.
    [Fact]
    public void FromSourceDocumentsFolderPrecedenceAndItsUsableTypes()
    {
        var help = HelpRenderer.Render(CoverCommands.Create(), ["cover", "from-source"], full: false);
        Assert.Contains("folder", help);
        Assert.Contains("audio", help);
    }
```

If any of those test files renders help through a different helper than `HelpRenderer.Render`, use that file's own helper.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: the new tests fail; nothing else does.

- [ ] **Step 3: Add `library stats`**

Register `command.Subcommands.Add(CreateStatsCommand());` in `LibraryCommand.Create()`, then:

```csharp
    private static Command CreateStatsCommand()
    {
        var command = new Command("stats", "Counts and sizes across the whole library");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "total_size_mb is books only; library_size_mb adds maps, tokens,",
            "audio and models. Both scope the book portion to what the caller",
            "may see, so a restricted book's bytes stay out of the totals.");
        command.AddExamples("grimoire-cli library stats");
        command.AddResponseExample<Generated.Models.StatsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new LibraryService(client);
            var result = await service.StatsAsync();
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 4: Add `systems cover from-source`**

Register it in `CoverCommands.Create()` after the delete command, then:

```csharp
    private static Command CreateFromSourceCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var sourceTypeOption = new Option<string>("--source-type") { Description = "The kind of library item to copy the image from", Required = true };
        var sourceIdOption = new Option<string>("--source-id") { Description = "That item's ID", Required = true };
        var command = new Command("from-source", "Set the system's cover from an image already in the library")
        {
            idOption, sourceTypeOption, sourceIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--source-type takes map, token, book or audio. The API also declares",
            "campaign_file, which needs a campaign this route never sends, so it",
            "always 400s here.",
            "",
            "Copies the bytes in as an upload does, so a folder cover.* or folder.*",
            "image still wins over what this sets.",
            "",
            "Useful for a container system, which has no books of its own to take a",
            "thumbnail from.");
        command.AddExamples(
            "grimoire-cli systems cover from-source --id <system-id> --source-type book --source-id <book-id>");
        command.AddResponseExample<Generated.Models.SystemCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            var result = await service.CoverFromSourceAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceTypeOption)!,
                parseResult.GetValue(sourceIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

`SystemCoverResponse` is the model the sibling `upload` command already registers (`CoverCommands.cs:79`), and the handler returns the same `{"cover_image": filename}` shape — use it rather than looking for a from-source-specific model.

- [ ] **Step 5: Add `maps file` and `maps page`**

Register both in `MapsCommand.Create()` alongside `thumbnail`, plus `command.Subcommands.Add(MapVttCommands.Create());`.

```csharp
    private static Command CreateFileCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("file", "Download the map file as stored")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The original file, whatever its format — use maps page to render a",
            "PDF page as an image.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli maps file --id <id> --output tavern.png",
            "grimoire-cli maps file --id <id> --output - > tavern.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
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
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var pageOption = new Option<int>("--page") { Description = "1-based page number", Required = true };
        var widthOption = new Option<int?>("--width") { Description = "Target pixel width" };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("page", "Render one page of a PDF map as WebP")
        {
            idOption, pageOption, widthOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "An image map is streamed as stored and accepts page 1 only.",
            "",
            "--width defaults to 1600 and is capped at 3000.",
            "",
            "--output - writes the image to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples(
            "grimoire-cli maps page --id <id> --page 1 --output page1.webp",
            "grimoire-cli maps page --id <id> --page 1 --width 800 --output -");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
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

- [ ] **Step 6: Create `src/GrimoireCli/Commands/MapVttCommands.cs`**

Model the file's header on `CoverCommands.cs` — same usings, same `_logger` field, same `Create()` shape.

```csharp
public static class MapVttCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("vtt", "Universal VTT data and exports");
        command.Subcommands.Add(CreateImageCommand());
        command.Subcommands.Add(CreateDataCommand());
        command.Subcommands.Add(CreateExportCommand());
        return command;
    }

    private static Command CreateImageCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("image", "Download the battlemap embedded in a Universal VTT file")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "400 unless the map is a .uvtt or .dd2vtt carrying an image; the",
            "base64 envelope is decoded server-side.",
            "",
            "--output - writes the image to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli maps vtt image --id <id> --output battlemap.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            await using var stream = await service.VttImageAsync(parseResult.GetValue(idOption)!);
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

    private static Command CreateDataCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var command = new Command("data", "Grid and feature counts from a Universal VTT file")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "400 unless the map is a .uvtt or .dd2vtt. The embedded image is",
            "omitted — maps vtt image serves it.");
        command.AddExamples("grimoire-cli maps vtt data --id <id>");
        command.AddResponseExample<Generated.Models.VttDataResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            var result = await service.VttDataAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateExportCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Map ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("export", "Export a raster map as a Universal VTT file")
        {
            idOption, outputOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Builds the .uvtt on demand: the image as base64 WebP, the grid, and",
            "any walls, portals and lights authored in the app. Nothing is written",
            "into the library.",
            "",
            "400 for a PDF, video or archive map, and for a raster already linked",
            "to a Universal VTT file — that sibling carries the geometry.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
        command.AddExamples("grimoire-cli maps vtt export --id <id> --output tavern.uvtt");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new MapsService(client);
            await using var stream = await service.VttExportAsync(parseResult.GetValue(idOption)!);
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

- [ ] **Step 7: Add `file` to audio, tokens and models**

Each is the streaming shape with that group's own id description, service, command description and examples. Write all three out; do not generate two from one.

`AudioCommand.cs` — description "Download the audio file", id description "Audio ID", service `AudioService`, Notes:

```csharp
            "The original track as stored — audio artwork serves the embedded",
            "cover image instead.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
```

`TokensCommand.cs` — description "Download the token image as stored", id description "Token ID", service `TokensService`, Notes:

```csharp
            "The original image, whatever its size — tokens thumbnail serves the",
            "generated preview.",
            "",
            "--output - writes the image to stdout; a path writes it and prints",
            "{path, bytes}.");
```

`ModelsCommand.cs` — description "Download the 3D model file", id description "Model ID", service `ModelsService`, Notes:

```csharp
            "The original model file, whatever its format — models thumbnail",
            "serves the rendered preview.",
            "",
            "--output - writes the file to stdout; a path writes it and prints",
            "{path, bytes}.");
```

- [ ] **Step 8: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 9: Read the rendered help, not the source**

```bash
for c in "library stats" "systems cover from-source" "maps file" "maps page" \
         "maps vtt image" "maps vtt data" "maps vtt export" \
         "audio file" "tokens file" "models file"; do
  echo "### $c"; dotnet run --project src/GrimoireCli -- $c --help
done
```

Check each: only `systems cover from-source` shows a Role required section; `maps vtt data` shows no `--output` and no `{path, bytes}` sample; no Notes line restates a flag description. Trim anything that fails, except a line a Step 1 assertion depends on.

- [ ] **Step 10: Commit**

```bash
git add src/GrimoireCli/Commands/ tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add library stats, cover from-source and the file getters"
```

---

### Task 3: Smoke coverage and the two live checks

**Files:**
- Modify: `docker/smoke-test.sh` (append a `--- small completions ---` block before the final `echo "smoke: all checks passed"`)

**Interfaces:**
- Consumes: the ten commands from Task 2, and the script's existing `$CLI`, `$WORK`, `ok`, `fail` helpers.
- Produces: the live findings Task 4 records in the docs.

**Constraint:** the block must converge on a re-run. Downloads go to `$WORK`, which is a fresh mktemp dir per run, so they need no cleanup — but the one write (`systems cover from-source`) must restore the prior state.

- [ ] **Step 1: Bring up the stack if it is not already up**

```bash
docker compose -f docker/docker-compose.yml ps
# only if down:
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

The seeded fixtures include 3 maps, 2 tokens, 3 models and 2 audio tracks, all confirmed present while this plan was written.

- [ ] **Step 2: Run the two live checks by hand and record the answers**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh >/dev/null   # logs in as admin
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli

# (a) maps page against the fixtures. Every fixture map is a PNG, so page 1 is
# expected to stream as-is and page 2 to be refused. Confirm both, and record
# whether --width changes the bytes on an image map or is ignored.
MAP=$($CLI maps list | jq -r '.maps[0].id // .[0].id')
PROBE=$(mktemp -d)
$CLI maps page --id "$MAP" --page 1 --output "$PROBE/p1.webp"; echo "page1 exit=$?"
$CLI maps page --id "$MAP" --page 2 --output "$PROBE/p2.webp"; echo "page2 exit=$? (expect non-zero)"
$CLI maps page --id "$MAP" --page 1 --width 400 --output "$PROBE/p1w.webp"; echo "width exit=$?"
ls -l "$PROBE"/p1.webp "$PROBE"/p1w.webp 2>/dev/null

# (b) systems cover from-source. Read the system's current cover_image first;
# the restore at the end depends on it.
SYS=$($CLI systems list | jq -r '.[0].id')
$CLI systems get --id "$SYS" | jq -r '.cover_image // ""'   # note this down
BOOK=$($CLI books list | jq -r '.books[0].id // .[0].id')
$CLI systems cover from-source --id "$SYS" --source-type book --source-id "$BOOK"; echo "exit=$?"
$CLI systems get --id "$SYS" | jq -r '.cover_image // ""'
$CLI systems cover from-source --id "$SYS" --source-type campaign_file --source-id "$BOOK"; echo "campaign_file exit=$? (expect non-zero)"
```

Then restore: if the system had no uploaded cover before, `$CLI systems cover delete --id "$SYS"`. If it had one, say so in your report — the fixtures may make a clean restore impossible, and that decides whether the smoke block can safely include this write at all.

Record every exit code and body. Report what you observed, including anything that contradicts the predictions above.

- [ ] **Step 3: Append the smoke block**

Insert before the final `echo "smoke: all checks passed" >&2`. Adjust the jq paths to whatever each `list` response actually uses — read one of each rather than assuming.

```bash
# --- small completions -------------------------------------------------------
# Downloads land in $WORK, a fresh mktemp dir per run, so nothing needs cleaning
# up and a re-run starts from the same place.
STATS_JSON=$("$CLI" library stats 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library stats exited non-zero"; }
echo "$STATS_JSON" | jq -e 'has("game_systems") and has("books") and has("total_size_mb") and has("library_size_mb")' >/dev/null \
  || fail "library stats is missing a documented key: $STATS_JSON"
echo "$STATS_JSON" | jq -e '.total_size_mb <= .library_size_mb' >/dev/null \
  || fail "total_size_mb is books only and cannot exceed library_size_mb: $STATS_JSON"
echo "$STATS_JSON" | jq -e '.books > 0 and .maps > 0' >/dev/null \
  || fail "the seeded fixtures should show books and maps: $STATS_JSON"
ok "library stats reports both size fields"

# The four asset downloads, one per collection.
SC_MAP=$("$CLI" maps list 2>/dev/null | jq -r '.maps[0].id')
SC_TOKEN=$("$CLI" tokens list 2>/dev/null | jq -r '.tokens[0].id')
SC_MODEL=$("$CLI" models list 2>/dev/null | jq -r '.models[0].id')
SC_AUDIO=$("$CLI" audio list 2>/dev/null | jq -r '.audio[0].id')
for pair in "maps:$SC_MAP" "tokens:$SC_TOKEN" "models:$SC_MODEL" "audio:$SC_AUDIO"; do
  GROUP=${pair%%:*}; ITEM=${pair#*:}
  [ -n "$ITEM" ] && [ "$ITEM" != "null" ] || fail "no $GROUP fixture to download"
  "$CLI" "$GROUP" file --id "$ITEM" --output "$WORK/$GROUP.bin" >"$WORK/dl.out" 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "$GROUP file exited non-zero"; }
  [ -s "$WORK/$GROUP.bin" ] || fail "$GROUP file wrote an empty file"
  [ "$(jq -r .bytes "$WORK/dl.out")" -gt 0 ] \
    || fail "$GROUP file should report a byte count: $(cat "$WORK/dl.out")"
  ok "$GROUP file downloads the asset and reports its size"
done

"$CLI" maps page --id "$SC_MAP" --page 1 --output "$WORK/page1.webp" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "maps page exited non-zero"; }
[ -s "$WORK/page1.webp" ] || fail "maps page wrote an empty file"
ok "maps page renders page 1"

# Every fixture map is a raster, so the export is the success path and the two
# /vtt/ getters are the refusal path.
"$CLI" maps vtt export --id "$SC_MAP" --output "$WORK/map.uvtt" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "maps vtt export exited non-zero"; }
jq -e 'has("image") and has("resolution")' "$WORK/map.uvtt" >/dev/null \
  || fail "a .uvtt export should carry an image and a resolution: $(head -c 200 "$WORK/map.uvtt")"
ok "maps vtt export builds a Universal VTT file from a raster map"

"$CLI" maps vtt data --id "$SC_MAP" >/dev/null 2>&1 \
  && fail "maps vtt data should refuse a map that is not a Universal VTT"
ok "maps vtt data refuses a non-VTT map"

"$CLI" maps vtt image --id "$SC_MAP" --output - >/dev/null 2>&1 \
  && fail "maps vtt image should refuse a map that is not a Universal VTT"
ok "maps vtt image refuses a non-VTT map"
```

Whether the `systems cover from-source` write joins this block depends on Step 2's restore finding. If the fixture system can be restored cleanly, add it; if not, leave it out and say so in your report rather than writing a check that drifts.

Confirm the two `jq -e` keys on the `.uvtt` export against what Step 2 actually downloaded — a Universal VTT file's field names are the format's, not ours, and guessing them is how this assertion ends up vacuous.

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed` both times. Run them one after another, never concurrently — the script rewrites `$HOME/.grimoire-cli/config.json`, so two at once race each other.

- [ ] **Step 5: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the stats and binary getters in the smoke test"
```

---

### Task 4: Docs

**Files:**
- Modify: `README.md` (Commands table)
- Modify: `tools/generate-api-coverage.py` (the `IMPLEMENTED` dict)
- Modify: `docs/grimoire-api-coverage.md` (regenerated, never hand-edited)
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Add the README rows**

Place each row in its own group's section, beside the sibling it belongs with:

```markdown
| `library stats` | Counts and sizes across the whole library |
| `systems cover from-source --id <id> --source-type <type> --source-id <id>` | Set a system cover from a library image (gm or admin) |
| `maps file --id <id> --output <path>` | Download the map file as stored |
| `maps page --id <id> --page <n> [--width <px>] --output <path>` | Render one page of a PDF map as WebP |
| `maps vtt image --id <id> --output <path>` | Download the battlemap inside a Universal VTT file |
| `maps vtt data --id <id>` | Grid and feature counts from a Universal VTT file |
| `maps vtt export --id <id> --output <path>` | Export a raster map as a Universal VTT file |
| `audio file --id <id> --output <path>` | Download the audio file |
| `tokens file --id <id> --output <path>` | Download the token image as stored |
| `models file --id <id> --output <path>` | Download the 3D model file |
```

- [ ] **Step 2: Add the coverage entries**

```python
    "GET /api/stats": "`library stats` ✅",
    "POST /api/systems/{system_id}/cover/from-source": "`systems cover from-source` ✅",
    "GET /api/maps/{map_id}/file": "`maps file` ✅",
    "GET /api/maps/{map_id}/page/{page_num}": "`maps page` ✅",
    "GET /api/maps/{map_id}/vtt/image": "`maps vtt image` ✅",
    "GET /api/maps/{map_id}/vtt/data": "`maps vtt data` ✅",
    "GET /api/maps/{map_id}/export.uvtt": "`maps vtt export` ✅",
    "GET /api/audio/{audio_id}/file": "`audio file` ✅",
    "GET /api/tokens/{token_id}/file": "`tokens file` ✅",
    "GET /api/models/{model_id}/file": "`models file` ✅",
```

Check every path-parameter name against the existing rows in `docs/grimoire-api-coverage.md` rather than trusting this list.

- [ ] **Step 3: Regenerate the coverage table**

```bash
tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly the ten rows change, plus the derived per-tag and total lines. Any other row moving means the pin or the clone moved — stop and report rather than committing the drift.

- [ ] **Step 4: Record the verified behaviour**

Add to `docs/grimoire-api-notes.md`, matching the file's existing heading style and placement:

```markdown
### Library statistics

- `GET /api/stats` carries two size fields that are not the same number:
  `total_size_mb` is books only, while `library_size_mb` adds maps, tokens,
  audio and models (`routers/library/_schemas.py:77-79`, `core.py:150`, v1.7.1).
  Both scope the book portion to what the caller may see, so a restricted
  book's bytes stay out of either total.

### Universal VTT routes

- `GET /api/maps/{id}/vtt/data` is JSON, not a download: it is registered with
  `response_model=VttDataResponse` and returns the grid resolution and
  wall/portal/light counts with the embedded image omitted
  (`routers/maps/__init__.py:115-126`). Its sibling `vtt/image` serves the
  picture. Both 400 unless the map is a `.uvtt`/`.dd2vtt`.
- `GET /api/maps/{id}/export.uvtt` is a download whose payload is JSON: the
  handler returns a `Response` carrying the image as base64 WebP plus the grid
  and any authored geometry (`routers/maps/core.py:340-460`). A raster map is
  the normal case — verified live, a plain PNG fixture map exported 200
  `application/octet-stream`. It 400s for PDF, video and archive maps, and for
  a raster already linked to a Universal VTT sibling.

### Cover from-source

- `source_type` is validated against `("map", "token", "book", "audio",
  "campaign_file")` (`services/image_source.py:32`), but `campaign_file` also
  requires a campaign id that `POST /api/systems/{id}/cover/from-source` never
  sends, so it always 400s there (`image_source.py:120-122`). The bytes are
  copied in exactly as an upload does, so folder art still takes precedence
  (`routers/systems/covers.py:161-181`).
```

Replace any line with what Task 3 actually observed if the stack disagrees, and say so in the PR body.

- [ ] **Step 5: Remove the shipped roadmap item**

`docs/roadmap.md`'s `## Next` holds only the `Small completions` paragraph, which this change ships. Remove it. That empties `## Next` — report that plainly rather than inventing a replacement item, and check whether the lead-in paragraph above it still reads correctly with nothing beneath. Read `git show 872ba5c -- docs/roadmap.md` for the shape previous removals took. The file records intended work only; add no note that this shipped.

- [ ] **Step 6: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record the stats and binary getter commands"
```

---

### Task 5: Pre-PR verification and the PR

- [ ] **Step 1: Run all four checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report the actual output; do not claim a pass that was not run.

- [ ] **Step 2: Open the PR**

```bash
git push -u origin feat/small-completions
gh pr create --title "feat: small completions" --body "…"
```

The body lists the ten commands, names the two findings that changed the design (`vtt/data` is JSON rather than a download; `export.uvtt` is a download whose payload is JSON), says which commands ship without live success coverage and why, and records the verification that was run. End it with the attribution line this session's instructions require.

- [ ] **Step 3: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked, and present the PR URL as a clickable link.
