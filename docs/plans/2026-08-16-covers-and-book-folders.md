# Covers, Book Folders and Binary Output Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Six commands — `systems cover get|upload|delete`, `systems book-folders list|set`, `books thumbnail` — closing the remaining `systems` endpoints and settling how this CLI emits binary bodies.

**Architecture:** Two nested subgroups (`cover`, `book-folders`) plus one flat leaf (`books thumbnail`). Binary responses go through a new `GrimoireApiClient.SendStreamAsync` and a shared `ConsoleOutput.WriteStreamAsync`, which writes bytes to `-` (stdout) or to a path followed by a `{path, bytes}` JSON receipt. The cover upload is the CLI's first non-JSON request body, built with `MultipartFormDataContent` and attached with the existing `SetStreamContent` precedent.

**Tech Stack:** C# / .NET 10, System.CommandLine, Kiota-generated request builders (all six endpoints already generated), xUnit, bash smoke test, PyMuPDF for the fixture image.

**Design doc:** [docs/specs/2026-08-16-covers-and-book-folders-design.md](../specs/2026-08-16-covers-and-book-folders-design.md)

## Global Constraints

- **Branch:** `feat/covers-and-book-folders` off `main`. Never commit to `main`.
- **Conventional Commits**, imperative, lowercase, no period, ≤72 chars. No `Co-Authored-By`, no tool attribution.
- **Run `dotnet format GrimoireCli.sln` after any C# edit.** CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** in method bodies — none between consecutive option declarations or `Subcommands.Add` calls, none before a `return` following setup calls.
- **Role tags and permission hints agree**: `cover upload`, `cover delete` and `book-folders set` are `gm or admin` ↔ `"the gm or admin role"`. `cover get`, `book-folders list` and `books thumbnail` are `require_not_guest`, the default, and get **no** tag and no hint.
- **`--server` and `--token`** are declared per subcommand and threaded into `CommandHelper.BuildClient`.
- **Thin pass-through**: no client-side validation of file size, image validity or accepted image types — the server owns all three.
- **The smoke test stays idempotent.** No assertion whose expected value depends on the fixture's prior state.
- **Do not touch `CHANGELOG.md`.**

---

### Task 1: Branch, spec and plan

**Files:** branch `feat/covers-and-book-folders`; commit `docs/specs/2026-08-16-covers-and-book-folders-design.md`, `docs/plans/2026-08-16-covers-and-book-folders.md`

- [ ] **Step 1: Branch**

```bash
git checkout main && git pull --ff-only
git checkout -b feat/covers-and-book-folders
```

- [ ] **Step 2: Commit**

```bash
git add docs/specs/2026-08-16-covers-and-book-folders-design.md docs/plans/2026-08-16-covers-and-book-folders.md
git commit -m "docs: design covers, book folders and binary output"
```

---

### Task 2: Binary output plumbing and `books thumbnail`

The first binary consumer ships with the plumbing, so the convention is proven end to end rather than asserted.

**Files:**
- Create: `src/GrimoireCli/Models/SavedFile.cs`
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs`, `src/GrimoireCli/Output/ConsoleOutput.cs`, `src/GrimoireCli/Models/JsonContext.cs`, `src/GrimoireCli/Services/BooksService.cs`, `src/GrimoireCli/Commands/BooksCommand.cs`
- Regenerate: `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Output/WriteStreamTests.cs`, `tests/GrimoireCli.Tests/Models/SavedFileTests.cs`, additions to `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`

**Interfaces:**
- Produces: `GrimoireApiClient.SendStreamAsync(RequestInformation, string? permissionHint, string? notFoundHint, TimeSpan? timeout) → Task<Stream>`; `ConsoleOutput.WriteStreamAsync(Stream, string output) → Task` (writes bytes to stdout when `output` is `-`, otherwise writes the file and prints a `SavedFile` receipt); `GrimoireCli.Models.SavedFile { Path, Bytes }`; `BooksService.ThumbnailAsync(string id) → Task<Stream>`

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/Output/WriteStreamTests.cs`:

```csharp
using System.Text;
using GrimoireCli.Output;

namespace GrimoireCli.Tests.Output;

public class WriteStreamTests
{
    [Fact]
    public async Task WritesBytesToTheNamedFileAndReportsTheCount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grimoire-write-{Guid.NewGuid():N}.bin");
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var stdout = new StringWriter();
        var original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using var source = new MemoryStream(payload);
            await ConsoleOutput.WriteStreamAsync(source, path);
        }
        finally { Console.SetOut(original); }

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        var receipt = stdout.ToString();
        Assert.Contains("\"bytes\": 5", receipt);
        Assert.Contains(path, receipt);
        File.Delete(path);
    }

    // "-" is the documented escape hatch: raw bytes, and no JSON at all, so the
    // output can be redirected into a file or piped to another tool.
    [Fact]
    public async Task DashWritesNothingButTheBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grimoire-dash-{Guid.NewGuid():N}.bin");
        var payload = Encoding.UTF8.GetBytes("not json");
        await using (var captured = File.Create(path))
        {
            var original = Console.OpenStandardOutput();
            // Redirect the process stdout handle so the helper's own write lands in the file.
            using var source = new MemoryStream(payload);
            await ConsoleOutput.WriteStreamAsync(source, "-", captured);
        }
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        File.Delete(path);
    }
}
```

Note the second test passes an explicit destination stream. Give `WriteStreamAsync` an optional final parameter `Stream? stdout = null`, defaulting to `Console.OpenStandardOutput()`, so stdout is injectable — without it the `-` branch is untestable.

`tests/GrimoireCli.Tests/Models/SavedFileTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class SavedFileTests
{
    [Fact]
    public void SavedFileSerialisesTheWireNames()
    {
        var json = JsonSerializer.Serialize(
            new SavedFile { Path = "/tmp/cover.png", Bytes = 4096 },
            AppJsonContext.Default.SavedFile);
        Assert.Contains("\"path\":", json);
        Assert.Contains("\"bytes\":", json);
    }
}
```

Add to `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`, following that file's existing `RenderHelp` helper:

```csharp
    [Fact]
    public void ThumbnailRequiresAnOutput()
    {
        var result = BooksCommand.Create().Parse(["thumbnail", "--id", "1"]);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ThumbnailDocumentsTheDashAndTheScanOrigin()
    {
        var output = RenderHelp(["books", "thumbnail"], full: false);
        Assert.Contains("generated from the file during a scan", output);
        Assert.Contains("has_thumbnail", output);
        Assert.Contains("--output -", output);
    }

    // No role tag: the endpoint is require_not_guest, the router default.
    [Fact]
    public void ThumbnailCarriesNoRoleTag()
    {
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "thumbnail"], full: false));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~WriteStreamTests|FullyQualifiedName~SavedFileTests|FullyQualifiedName~BooksCommandTests"`
Expected: build failure — `ConsoleOutput.WriteStreamAsync`, `SavedFile` and the `thumbnail` subcommand do not exist.

- [ ] **Step 3: Add the DTO**

`src/GrimoireCli/Models/SavedFile.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// Receipt for a binary body written to disk. Local to the CLI — no endpoint
/// returns this shape; it exists so a download still answers on stdout with JSON.
/// </summary>
public class SavedFile
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }
}
```

Register it in `src/GrimoireCli/Models/JsonContext.cs`:

```csharp
[JsonSerializable(typeof(SavedFile))]
```

- [ ] **Step 4: Add the client's stream path**

In `src/GrimoireCli/Api/GrimoireApiClient.cs`, beside the existing `SendAsync`:

```csharp
    /// <summary>
    /// A response whose body is bytes rather than JSON. Identical to SendAsync
    /// through preflight, permission hints and error handling; only the read
    /// differs. The caller owns the returned stream.
    /// </summary>
    public async Task<Stream> SendStreamAsync(RequestInformation info, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)
    {
        await PreflightAsync();
        using var cts = new CancellationTokenSource(timeout ?? DefaultRequestTimeout);
        var request = await _adapter.ConvertToNativeRequestAsync<HttpRequestMessage>(info, cts.Token)
            ?? throw new InvalidOperationException($"Failed to build request for {info.URI.AbsolutePath}");
        var response = await _http.SendAsync(request, cts.Token);
        await EnsureSuccessAsync(response, permissionHint, notFoundHint);
        return await response.Content.ReadAsStreamAsync(cts.Token);
    }
```

- [ ] **Step 5: Add the output helper**

In `src/GrimoireCli/Output/ConsoleOutput.cs`:

```csharp
    /// <summary>
    /// Writes a binary body. "-" sends the bytes to stdout and prints nothing
    /// else; any other value is a file path, written and then reported as JSON so
    /// stdout stays parseable in the default case. The stdout parameter exists so
    /// the "-" branch is testable.
    /// </summary>
    public static async Task WriteStreamAsync(Stream source, string output, Stream? stdout = null)
    {
        if (output == "-")
        {
            await using var target = stdout ?? Console.OpenStandardOutput();
            await source.CopyToAsync(target);
            return;
        }
        long bytes;
        await using (var file = new FileStream(output, FileMode.Create, FileAccess.Write))
        {
            await source.CopyToAsync(file);
            bytes = file.Length;
        }
        WriteJson(new Models.SavedFile { Path = output, Bytes = bytes }, Models.AppJsonContext.Default.SavedFile);
    }
```

- [ ] **Step 6: Add the service method**

In `src/GrimoireCli/Services/BooksService.cs`:

```csharp
    /// <summary>
    /// GET /api/books/{id}/thumbnail. Bytes, not JSON: the thumbnail generated
    /// from the file during a scan. 404 when the book has none.
    /// </summary>
    public async Task<Stream> ThumbnailAsync(string id)
    {
        var info = _client.Api.Api.Books[id].Thumbnail.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }
```

- [ ] **Step 7: Add the command**

In `src/GrimoireCli/Commands/BooksCommand.cs`, add `command.Subcommands.Add(CreateThumbnailCommand());` in `Create()` and:

```csharp
    private static Command CreateThumbnailCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("thumbnail", "Download the book's cover thumbnail")
        {
            idOption, outputOption, serverOption, tokenOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The cover thumbnail generated from the file during a scan, not an",
            "uploaded image. 404 when has_thumbnail is false in books list.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples(
            "grimoire-cli books thumbnail --id <id> --output cover.jpg",
            "grimoire-cli books thumbnail --id <id> --output - > cover.jpg");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new BooksService(client);
            await using var stream = await service.ThumbnailAsync(parseResult.GetValue(idOption)!);
            await ConsoleOutput.WriteStreamAsync(stream, parseResult.GetValue(outputOption)!);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 8: Regenerate examples, format, build, full suite**

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: all green.

- [ ] **Step 9: Commit**

```bash
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add binary output and books thumbnail"
```

---

### Task 3: `systems cover get|upload|delete`

**Files:**
- Create: `src/GrimoireCli/Models/CoverUploadResult.cs`, `src/GrimoireCli/Commands/CoverCommands.cs`
- Modify: `src/GrimoireCli/Services/SystemsService.cs`, `src/GrimoireCli/Commands/SystemsCommand.cs`, `src/GrimoireCli/Models/JsonContext.cs`
- Regenerate: `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Commands/CoverCommandTests.cs`, `tests/GrimoireCli.Tests/Models/CoverDtoTests.cs`

**Interfaces:**
- Consumes: `SendStreamAsync`, `WriteStreamAsync`, `SavedFile` from Task 2
- Produces: `CoverCommands.Create()` returning the `cover` subgroup; `SystemsService.CoverAsync(id) → Task<Stream>`, `UploadCoverAsync(id, filePath) → Task<CoverUploadResult>`, `DeleteCoverAsync(id) → Task<string>`; `SystemsService.MimeForExtension(string path)` (internal, so a test can pin it)

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/Models/CoverDtoTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class CoverDtoTests
{
    [Fact]
    public void CoverUploadResultCarriesTheStoredFilename()
    {
        const string json = """{"cover_image": "8f3c-1d2e.png"}""";
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.CoverUploadResult)!;
        Assert.Equal("8f3c-1d2e.png", result.CoverImage);
    }
}
```

`tests/GrimoireCli.Tests/Commands/CoverCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Commands;

public class CoverCommandTests
{
    private static string Help(string leaf, bool full) =>
        HelpRenderer.Render(SystemsCommand.Create(), ["systems", "cover", leaf], full);

    [Theory]
    [InlineData("get")]
    [InlineData("upload")]
    [InlineData("delete")]
    public void EveryCoverVerbExists(string leaf) => Assert.Contains(leaf, Help(leaf, full: false));

    [Theory]
    [InlineData("upload")]
    [InlineData("delete")]
    public void WritesAreGmOrAdmin(string leaf) => Assert.Contains("gm or admin", Help(leaf, full: false));

    [Fact]
    public void ReadCarriesNoRoleTag() =>
        Assert.DoesNotContain("Role required:", Help("get", full: false));

    // Folder art beating an upload is the caveat that makes an apparently
    // successful upload look like it did nothing.
    [Fact]
    public void UploadWarnsThatFolderArtWins()
    {
        Assert.Contains("Folder cover art still wins", Help("upload", full: false));
    }

    [Fact]
    public void GetExplainsThe404Fallback()
    {
        var output = Help("get", full: false);
        Assert.Contains("cover_book_id", output);
        Assert.Contains("books thumbnail", output);
    }

    [Fact]
    public void DeleteSaysFolderArtSurvives()
    {
        Assert.Contains("library-managed", Help("delete", full: false));
    }

    [Fact]
    public void GetRequiresAnOutputAndUploadRequiresAFile()
    {
        Assert.NotEmpty(SystemsCommand.Create().Parse(["cover", "get", "--id", "1"]).Errors);
        Assert.NotEmpty(SystemsCommand.Create().Parse(["cover", "upload", "--id", "1"]).Errors);
    }

    // The server rejects on content type, so the CLI must send one. Unknown
    // extensions fall through to octet-stream rather than being refused here —
    // deciding which types are acceptable is the server's job.
    [Theory]
    [InlineData("art.png", "image/png")]
    [InlineData("art.jpg", "image/jpeg")]
    [InlineData("art.JPEG", "image/jpeg")]
    [InlineData("art.webp", "image/webp")]
    [InlineData("art.gif", "image/gif")]
    [InlineData("art.bmp", "application/octet-stream")]
    [InlineData("art", "application/octet-stream")]
    public void MimeComesFromTheExtension(string file, string expected) =>
        Assert.Equal(expected, SystemsService.MimeForExtension(file));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~CoverCommandTests|FullyQualifiedName~CoverDtoTests"`
Expected: build failure — no `cover` subgroup, no `CoverUploadResult`, no `MimeForExtension`.

- [ ] **Step 3: DTO**

`src/GrimoireCli/Models/CoverUploadResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/systems/{id}/cover response — the stored filename.</summary>
public class CoverUploadResult
{
    [JsonPropertyName("cover_image")]
    public string? CoverImage { get; set; }
}
```

Register `[JsonSerializable(typeof(CoverUploadResult))]` on `AppJsonContext`.

- [ ] **Step 4: Service methods**

In `src/GrimoireCli/Services/SystemsService.cs` (add `using System.Net.Http.Headers;`):

```csharp
    /// <summary>GET /api/systems/{id}/cover. Bytes: folder art if the library has
    /// any, otherwise the uploaded cover; 404 when it has neither.</summary>
    public async Task<Stream> CoverAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].Cover.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>
    /// POST /api/systems/{id}/cover. The CLI's only multipart body: the generated
    /// builder supplies the URL, method and path parameter, and the content is
    /// replaced with a form part named "file" — the name FastAPI binds.
    /// </summary>
    public async Task<CoverUploadResult> UploadCoverAsync(string id, string filePath)
    {
        var info = _client.Api.Api.Systems[id].Cover.ToPostRequestInformation(
            new Microsoft.Kiota.Abstractions.MultipartBody());
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
        part.Headers.ContentType = new MediaTypeHeaderValue(MimeForExtension(filePath));
        content.Add(part, "file", Path.GetFileName(filePath));
        info.SetStreamContent(await content.ReadAsStreamAsync(), content.Headers.ContentType!.ToString());
        return await _client.SendStreamJsonAsync(info);
    }

    /// <summary>
    /// The content type the server checks `file.content_type` against. Unknown
    /// extensions send octet-stream and let the server refuse — which types are
    /// acceptable is its policy, not ours. Internal so a test can pin the map.
    /// </summary>
    internal static string MimeForExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>DELETE /api/systems/{id}/cover. Removes the upload only; folder
    /// art is library-managed. Raw {"status":"ok"}.</summary>
    public async Task<string> DeleteCoverAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].Cover.ToDeleteRequestInformation();
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }
```

**Implementer note.** `SendStreamJsonAsync` above is a placeholder for whichever existing overload fits: the upload's *response* is JSON, so use
`_client.SendAsync(info, AppJsonContext.Default.CoverUploadResult, permissionHint: "the gm or admin role")`. Replace the call before building; it is written this way to flag that the multipart request and the JSON response are separate concerns. If `ToPostRequestInformation` rejects an empty `MultipartBody`, build the `RequestInformation` the way `BooksService.UpdateAsync` does and report what you had to change.

- [ ] **Step 5: The command group**

Create `src/GrimoireCli/Commands/CoverCommands.cs` with a `Create()` returning a `new Command("cover", "The system's cover image")` holding three subcommands, each declaring `--server`/`--token`, `--id` (`"System ID"`, required):

- **`get`** — `--output` required, `"Output file path, or '-' for binary to stdout"`. No role tag. `AddResponseExample<SavedFile>()`. Notes verbatim from the design doc's `systems cover get` block. Action: `service.CoverAsync(id)` then `ConsoleOutput.WriteStreamAsync(stream, output)`.
- **`upload`** — `--file` required, `"Path to a PNG, JPEG, WebP or GIF"`. `AddRoleRequired("gm or admin")`. `AddResponseExample<CoverUploadResult>()`. Notes verbatim from the design doc. Action: `service.UploadCoverAsync(id, file)` then `ConsoleOutput.WriteJson(result, AppJsonContext.Default.CoverUploadResult)`.
- **`delete`** — `AddRoleRequired("gm or admin")`, no response example (Notes name `{"status": "ok"}`). Notes verbatim. Action: `service.DeleteCoverAsync(id)` then `ConsoleOutput.WriteRawJson(response)`.

Wire it into `SystemsCommand.Create()` with `command.Subcommands.Add(CoverCommands.Create());`.

- [ ] **Step 6: Regenerate, format, build, test**

```bash
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli tests/GrimoireCli.Tests
git commit -m "feat: add systems cover get, upload and delete"
```

---

### Task 4: `systems book-folders list|set`

**Files:**
- Create: `src/GrimoireCli/Models/BookFolder.cs`, `src/GrimoireCli/Models/BookFolderList.cs`, `src/GrimoireCli/Models/BookFolderUpdated.cs`, `src/GrimoireCli/Commands/BookFolderCommands.cs`
- Modify: `src/GrimoireCli/Services/SystemsService.cs`, `src/GrimoireCli/Commands/SystemsCommand.cs`, `src/GrimoireCli/Models/JsonContext.cs`
- Test: `tests/GrimoireCli.Tests/Models/BookFolderDtoTests.cs`, `tests/GrimoireCli.Tests/Commands/BookFolderCommandTests.cs`

**Interfaces:**
- Produces: `BookFolderCommands.Create()`; `SystemsService.BookFoldersAsync(id) → Task<BookFolderList>`, `SetBookFolderAsync(id, rawBody) → Task<BookFolderUpdated>`

- [ ] **Step 1: Failing tests**

DTO test — a folder with no tags must survive, since an empty list is how a folder reads after being cleared:

```csharp
const string json = """
{"folders": [{"path": "5/core/Curse of Strahd", "tags": ["Horror", "Ravenloft"]},
             {"path": "5/adventure/One Shots", "tags": []}]}
""";
var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.BookFolderList)!;
Assert.Equal(2, result.Folders!.Count);
Assert.Equal(["Horror", "Ravenloft"], result.Folders[0].Tags);
Assert.Empty(result.Folders[1].Tags!);
```

Command tests, in the style of `CoverCommandTests`: both verbs exist under `systems book-folders`; `set` is `gm or admin` and `list` carries no role tag; `set` requires exactly one of `--input`/`--stdin`; and the Notes carry the three caveats — `Assert.Contains("Replaces the folder's tag list", …)`, `Assert.Contains("ignores the --id", …)`, `Assert.Contains("internal keys", …)` — plus `list` stating that folder tags never appear in a book's own tags.

- [ ] **Step 2: Run to verify they fail.** Expected: no `book-folders` subgroup, no DTOs.

- [ ] **Step 3: DTOs**

`BookFolder { [JsonPropertyName("path")] string? Path; [JsonPropertyName("tags")] List<string>? Tags; }`,
`BookFolderList { [JsonPropertyName("folders")] List<BookFolder>? Folders; }`,
`BookFolderUpdated { [JsonPropertyName("path")] string? Path; [JsonPropertyName("tags")] List<string>? Tags; }` — with a doc comment on the last saying its `tags` are internal keys, which is why it is not `BookFolder`.

Register all three on `AppJsonContext`.

- [ ] **Step 4: Service methods**

```csharp
    /// <summary>GET /api/systems/{id}/book-folders. Tags come back in display casing.</summary>
    public async Task<BookFolderList> BookFoldersAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToGetRequestInformation();
        return await _client.SendAsync(info, AppJsonContext.Default.BookFolderList,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }

    /// <summary>
    /// PATCH /api/systems/{id}/book-folders. Replaces the folder's tags. The
    /// server ignores the id in the URL and writes whatever path the body names;
    /// the validated raw body reaches it byte-for-byte, as the update commands do.
    /// </summary>
    public async Task<BookFolderUpdated> SetBookFolderAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToPatchRequestInformation(
            new Generated.Models.BookFolderUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, AppJsonContext.Default.BookFolderUpdated,
            permissionHint: "the gm or admin role");
    }
```

- [ ] **Step 5: Commands**

`BookFolderCommands.Create()` returns `new Command("book-folders", "Subcategory folders and their tags")` with:

- **`list`** — `--id` required, no role tag, `AddResponseExample<BookFolderList>()`, Notes verbatim from the design doc.
- **`set`** — `--id` required plus `--input`/`--stdin` via `JsonBodyInput.RequireExactlyOneSource`, `AddRoleRequired("gm or admin")`, `AddRequestShape<Generated.Models.BookFolderUpdate>()`, `AddResponseExample<BookFolderUpdated>()`, Notes verbatim. The action reads and validates the body exactly as `systems update` does (`JsonBodyInput.Read` then `JsonBodyInput.Validate(body, Generated.Models.BookFolderUpdate.CreateFromDiscriminatorValue, "pass it with --id")`), catching `BodyInputException` to log and return 1.

Wire into `SystemsCommand.Create()`.

- [ ] **Step 6: Regenerate, format, build, test.** Same four commands as Task 3 Step 6.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat: add systems book-folders list and set"
```

---

### Task 5: Fixture image and smoke coverage

**Files:** `docker/make-fixtures.py`, `docker/seed.sh` (only if the fixture needs generating there), `docker/smoke-test.sh`

**Preconditions:** stack up and seeded, `dotnet build GrimoireCli.sln` run so `src/GrimoireCli/bin/Debug/net10.0/grimoire-cli` exists.

- [ ] **Step 1: Generate a fixture PNG**

Add to `docker/make-fixtures.py` a function writing a small valid PNG with PyMuPDF (already imported there as `fitz`; Pillow is **not** installed):

```python
def make_png(path: str) -> None:
    """A tiny valid PNG for the cover-upload smoke check.

    PyMuPDF is already a fixture dependency; Pillow is not installed in the
    devcontainer. The server decodes this with PIL.Image.verify(), so it has to
    be a real image, not bytes with a .png name.
    """
    pix = fitz.Pixmap(fitz.csRGB, fitz.IRect(0, 0, 16, 16))
    pix.clear_with(200)
    pix.save(path)
```

Call it from wherever the script writes its fixtures, into a path the smoke test can read — `docker/fixture-cover.png` is fine and should be gitignored if generated, or checked in if the generator is not run on every seed. Decide by reading how `make-fixtures.py` is invoked from `seed.sh`, and say which you chose.

- [ ] **Step 2: Add the smoke section**

Insert after the systems section's last `ok:` line and before `# --- books ---`. Use a system that is **not** `Shadowrun 4 DE` — that fixture is already carrying the description write and three metadata diff assertions. Pick one from `systems list --include-children` by name and fail loudly if it is absent.

```bash
# --- system covers --------------------------------------------------------
# A different system from Shadowrun 4 DE on purpose: that one already carries
# the description write above and the metadata diff assertions below, and a
# cover write would couple a third assertion to the same fixture.
syslist --include-children
COVER_SYS=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Fixture Explicit RPG") | .id')
[ -n "$COVER_SYS" ] || fail "no Fixture Explicit RPG to attach a cover to"

# 404 first: this system has neither folder art nor an upload.
set +e
"$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/none.png" >/dev/null 2>"$WORK/cover404.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "cover get on a system with no cover should exit 2, got $rc"
ok "systems cover get 404s when the system has no cover"

UPLOAD_JSON=$("$CLI" systems cover upload --id "$COVER_SYS" --file docker/fixture-cover.png 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover upload exited non-zero"; }
echo "$UPLOAD_JSON" | jq -e '.cover_image | endswith(".png")' >/dev/null \
  || fail "upload should report a .png cover_image: $UPLOAD_JSON"
ok "systems cover upload stores a png"

GET_JSON=$("$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/cover.png" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover get exited non-zero"; }
[ "$(echo "$GET_JSON" | jq -r .bytes)" -eq "$(wc -c < "$WORK/cover.png")" ] \
  || fail "the receipt's byte count should match the file: $GET_JSON"
ok "systems cover get writes the file and reports its size"

"$CLI" systems cover get --id "$COVER_SYS" --output - > "$WORK/cover-dash.png" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems cover get --output - exited non-zero"; }
cmp -s "$WORK/cover.png" "$WORK/cover-dash.png" \
  || fail "--output - and --output <file> should produce identical bytes"
ok "systems cover get --output - streams the same bytes to stdout"

DEL_JSON=$("$CLI" systems cover delete --id "$COVER_SYS" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover delete exited non-zero"; }
[ "$(echo "$DEL_JSON" | jq -r .status)" = "ok" ] || fail "delete should answer ok: $DEL_JSON"
set +e
"$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/gone.png" >/dev/null 2>&1; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "cover get after delete should exit 2 again, got $rc"
ok "systems cover delete removes the upload"

# --- book folders ---------------------------------------------------------
FOLDER_PATH="$SR4/core/smoke-fixture-folder"
SET_JSON=$(printf '{"path":"%s","tags":["smoke"]}' "$FOLDER_PATH" \
  | "$CLI" systems book-folders set --id "$SR4" --stdin 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders set exited non-zero"; }
[ "$(echo "$SET_JSON" | jq -r .path)" = "$FOLDER_PATH" ] \
  || fail "set should echo the path it wrote: $SET_JSON"
ok "systems book-folders set writes a folder's tags"

FOLDERS_JSON=$("$CLI" systems book-folders list --id "$SR4" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero"; }
echo "$FOLDERS_JSON" | jq -e --arg p "$FOLDER_PATH" '.folders[] | select(.path == $p)' >/dev/null \
  || fail "the folder just written should be listed: $FOLDERS_JSON"
ok "systems book-folders list shows the written folder"
```

Fixed path and fixed tags, so a second run converges.

- [ ] **Step 3: Add the thumbnail check**

In the books section, after a book id is already in hand, assert on `has_thumbnail` first and only download when it is true:

```bash
if [ "$(echo "$GET_JSON" | jq -r .has_thumbnail)" = "true" ]; then
  THUMB_JSON=$("$CLI" books thumbnail --id "$SR4_BOOK" --output "$WORK/thumb.jpg" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "books thumbnail exited non-zero"; }
  [ "$(echo "$THUMB_JSON" | jq -r .bytes)" -gt 0 ] || fail "thumbnail should have bytes: $THUMB_JSON"
  ok "books thumbnail downloads the scan-generated image"
else
  ok "books thumbnail skipped — the server generated no thumbnail for this fixture"
fi
```

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```
Both green. Report whether the thumbnail branch ran or skipped.

- [ ] **Step 5: Commit**

```bash
git commit -m "test: cover system covers, book folders and binary output"
```

---

### Task 6: Docs and verification

- [ ] **Step 1: README** — six rows in the Commands table, beside the existing `systems` and `books` rows.

- [ ] **Step 2: Coverage** — add to `IMPLEMENTED` in `tools/generate-api-coverage.py`:

```python
    "GET /api/systems/{system_id}/cover": "`systems cover get` ✅",
    "POST /api/systems/{system_id}/cover": "`systems cover upload` ✅",
    "DELETE /api/systems/{system_id}/cover": "`systems cover delete` ✅",
    "GET /api/systems/{system_id}/book-folders": "`systems book-folders list` ✅",
    "PATCH /api/systems/{system_id}/book-folders": "`systems book-folders set` ✅",
    "GET /api/books/{book_id}/thumbnail": "`books thumbnail` ✅",
```

Then `python3 tools/generate-api-coverage.py`.

- [ ] **Step 3: `docs/cli-design.md`** — two additions. The nesting rule: several HTTP methods on one path become a nested subgroup; distinct sibling paths stay flat with leaf names mirroring the path segment, citing `items cover get` versus `items batch-update-progress` in abs-cli and noting that the metadata trio is flat for that reason, not because commands are capped at two levels. And the binary-output convention: `--output` required, `-` for stdout, a `SavedFile` receipt otherwise.

- [ ] **Step 4: `docs/input-output.md`** — the binary case beside the existing stdout/stderr contract: stdout is JSON except when `--output -` is given, which is the only way bytes reach stdout.

- [ ] **Step 5: `docs/grimoire-api-notes.md`** — a covers section (the three-source precedence chain, that an upload can be shadowed by folder art, that delete leaves folder art) and a book-folders section (the ignored `{system_id}` on PATCH, replace-not-add, display-versus-internal tags, and that folder tags never reach a book's own `tags`). Only what the live runs and the source confirm.

- [ ] **Step 6: `docs/roadmap.md`** — remove item 1 and renumber. In the binary-endpoints item, note that the convention is settled and what remains is applying it to book files, page images and map/token thumbnails.

- [ ] **Step 7: All four pre-PR checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

- [ ] **Step 8: Commit and push**

```bash
git commit -m "docs: record covers, book folders and the binary convention"
git push -u origin feat/covers-and-book-folders
```

- [ ] **Step 9: Stop.** The PR is opened after a whole-branch review, not from this task.

## Notes for the implementer

- All six endpoints are already generated: `src/GrimoireCli/Generated/Api/Systems/Item/{Cover,BookFolders}/` and `.../Books/Item/Thumbnail/`. Nothing regenerates the API client.
- `HelpRenderer` renders a subcommand's help in tests; `full: true` includes the response-shape block. For a nested group the path is `["systems", "cover", "get"]`.
- The multipart upload is the one place the plan may not survive contact — if `ToPostRequestInformation(new MultipartBody())` throws or produces a body the server rejects, fall back to the `BooksService.UpdateAsync` shape and report what changed.
