# Campaign wiki import and export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `campaigns wiki export` and `campaigns wiki import`. The spec is `docs/specs/2026-10-14-campaign-wiki-import-export-design.md`.

**Architecture:**
- Two methods on `CampaignWikiService` and two commands in `CampaignWikiCommands.cs`.
- Export streams bytes through `ConsoleOutput.WriteStreamAsync`, like `backups download`.
- Import sends one multipart `file` part, like `CampaignFilesService.UploadAsync`.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, and a bash smoke test.

## Global Constraints

- Branch `feat/campaign-wiki-import-export`. Conventional Commits, and **no `Co-Authored-By:` and no "Generated with" lines** in any commit.
- Run `dotnet format GrimoireCli.sln` after editing C#. Keep blank lines out of method bodies.
- Neither command gets a role tag, and no `permissionHint` is passed. The server checks campaign membership and ownership itself.
- Help text is terse. Don't restate flag descriptions or response fields. Check the rendered `--help`.
- The local stack is at `http://host.docker.internal:9481`, with logins `admin/admin`, `gm/gm`, `player/player`.

---

### Task 1: Service, commands, unit tests, README, coverage

**Files:**
- Modify: `src/GrimoireCli/Services/CampaignWikiService.cs`
- Modify: `src/GrimoireCli/Commands/CampaignWikiCommands.cs`
- Modify: `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/CampaignWikiCommandTests.cs`
- Modify: `README.md` (add two rows after the `campaigns wiki unhide` row, ~line 396)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`, next to the other wiki entries ~line 272-281)
- Regenerate: `docs/grimoire-api-coverage.md`

**Interfaces:**
- Produces:
  - `CampaignWikiService.ExportAsync(string campaignId, string? format) : Task<Stream>`
  - `internal ExportRequest(string campaignId, string? format) : RequestInformation`
  - `CampaignWikiService.ImportAsync(string campaignId, string filePath) : Task<string>`
  - `internal ImportRequest(string campaignId, byte[] bytes, string fileName) : RequestInformation`

- [ ] **Step 1: Write failing service tests** in `CampaignWikiServiceTests.cs`:
  - Add `{ "export", "GET", Base + "/export" }` and `{ "import", "POST", Base + "/import" }` to `Routes()`.
  - Add `"export" => s.ExportRequest("c-1", null)` and `"import" => s.ImportRequest("c-1", [1], "vault.zip")` to `Request()`.
  - Add these facts:

```csharp
    [Fact]
    public void ExportSendsTheFormatOnlyWhenSet()
    {
        Assert.DoesNotContain("?", Of(Service().ExportRequest("c-1", null)).Uri);
        Assert.EndsWith("/export?format=json", Of(Service().ExportRequest("c-1", "json")).Uri);
    }

    [Fact]
    public void ImportSendsOneFilePartNamedAfterTheLocalFile()
    {
        var info = Service().ImportRequest("c-1", "hello"u8.ToArray(), "vault.zip");
        using var reader = new StreamReader(info.Content);
        var body = reader.ReadToEnd();
        Assert.Contains("name=\"file\"", body);
        Assert.Contains("filename=\"vault.zip\"", body);
    }
```

  If `info.Content` is not populated without a live adapter, find how Kiota serializes `MultipartBody` (`ToPostRequestInformation` calls `SetContentFromParsable` with the client's request adapter). Then assert on what is available, and say in your report what you did.

- [ ] **Step 2: Run the tests and confirm they fail to compile.**
  Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter CampaignWikiServiceTests`

- [ ] **Step 3: Implement in `CampaignWikiService.cs`**:
  - Update the class summary from "The ten routes" to "The twelve routes".
  - Add the public methods next to the others:

```csharp
    /// <summary>GET /api/campaigns/{id}/wiki/export. A zip, a markdown file or a JSON bundle, as bytes.</summary>
    public async Task<Stream> ExportAsync(string campaignId, string? format)
        => await _client.SendStreamAsync(ExportRequest(campaignId, format), notFoundHint: CampaignNotFound);

    /// <summary>
    /// POST /api/campaigns/{id}/wiki/import. The part's filename is the local
    /// basename: the server picks its parser by extension.
    /// </summary>
    public async Task<string> ImportAsync(string campaignId, string filePath)
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
        return await _client.SendAsync(ImportRequest(campaignId, bytes, Path.GetFileName(filePath)), notFoundHint: CampaignNotFound);
    }
```

  - Add the internal builders next to the others:

```csharp
    internal RequestInformation ExportRequest(string campaignId, string? format)
        => _client.Api.Api.Campaigns[campaignId].Wiki.Export.ToGetRequestInformation(c =>
        {
            // Sent only when set: the server defaults to md.
            if (format is not null) c.QueryParameters.Format = format;
        });

    internal RequestInformation ImportRequest(string campaignId, byte[] bytes, string fileName)
    {
        var body = new MultipartBody();
        body.AddOrReplacePart("file", "application/octet-stream", bytes, fileName);
        return _client.Api.Api.Campaigns[campaignId].Wiki.Import.ToPostRequestInformation(body);
    }
```

  - Add `using GrimoireCli.Commands;` for `BodyInputException`.

- [ ] **Step 4: Run the service tests and confirm they pass.**

- [ ] **Step 5: Write failing command tests** in `CampaignWikiCommandTests.cs`:

```csharp
    [Fact]
    public void ExportNeedsAnOutput()
        => Assert.NotEmpty(Parse("export", "--id", "c").Errors);

    [Fact]
    public void ExportRejectsAnUnknownFormat()
        => Assert.NotEmpty(Parse("export", "--id", "c", "--format", "pdf", "--output", "-").Errors);

    [Fact]
    public void ExportTakesFormatAndOutput()
        => Assert.Empty(Parse("export", "--id", "c", "--format", "mdfile", "--output", "-").Errors);

    [Fact]
    public void ImportNeedsAFile()
        => Assert.NotEmpty(Parse("import", "--id", "c").Errors);

    [Fact]
    public void ImportSaysItNeverOverwrites()
        => Assert.Contains("never overwrites", Help("import"));
```

- [ ] **Step 6: Implement both commands** in `CampaignWikiCommands.cs`:
  - Register them after `unhide` in `Create()`.
  - Model `export` on `BackupsCommand.CreateDownloadCommand`, including its `BodyInputException` catch and `AddResponseExample<SavedFile>()`.
  - Model `import` on `CampaignFilesCommands.CreateUploadCommand`, minus the category options, and with `AddResponseExample<Generated.Models.WikiImportOut>()`.
  - Use `CampaignIdOption()` for `--id`. `--format` is `OptionHelpers.Choice("--format", "Export shape; default md", ["md", "mdfile", "json"])`.
  - `--output` is `Required = true`, with the description "Output file path, or '-' for the bytes to stdout".
  - Descriptions: `export` is "Export the wiki pages you can see", and `import` is "Import pages from a markdown, zip or JSON file".
  - Notes for `export`, verbatim:

```
md: a zip of one <slug>.md per page, frontmatter carrying title, visibility,
parent, icon, page_type and session_date. json: a Grimoire bundle. Both
re-import with the tree intact. mdfile: one markdown document for reading;
it re-imports as a single page.

Holds only pages you can view; GM secrets are removed unless you own the
campaign. Works on an archived campaign.

--output - writes the bytes to stdout; a path writes the file and prints
{path, bytes}.
```

  - Notes for `import`, verbatim:

```
Campaign owner only; an archived campaign answers 409.

Import never overwrites: every page in the file becomes a new page, so
importing twice duplicates them. Unresolved [[links]] get stubs, as on create.

Reads .md/.markdown/.txt, Grimoire JSON, LegendKeeper JSON or .lk, or a .zip
of any of these. A zip's folders become nested pages; Places/Places.md or
Places/index.md becomes the Places page itself. To import a folder, zip it.
Max 25 MB.
```

  - Examples:
    - `grimoire-cli campaigns wiki export --id <campaign-id> --output wiki.zip`
    - `grimoire-cli campaigns wiki export --id <campaign-id> --format json --output - > wiki.json`
    - `grimoire-cli campaigns wiki import --id <campaign-id> --file vault.zip`

- [ ] **Step 7: Run all tests, format, and check the rendered help.**
  - `dotnet format GrimoireCli.sln`
  - `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj` must pass in full (`HelpCompletenessTests` and the like included).
  - `dotnet run --project src/GrimoireCli -- campaigns wiki export --help`, and the same for `import`. Read both.

- [ ] **Step 8: README and coverage.**
  - README rows:

```
| `campaigns wiki export --id <campaign-id> [--format <md\|mdfile\|json>] --output <path\|->` | Export the wiki pages you can see |
| `campaigns wiki import --id <campaign-id> --file <path>` | Import pages from a markdown, zip or JSON file |
```

  - `IMPLEMENTED` entries:

```python
    "GET /api/campaigns/{campaign_id}/wiki/export": "`campaigns wiki export` ✅",
    "POST /api/campaigns/{campaign_id}/wiki/import": "`campaigns wiki import` ✅",
```

  - Regenerate with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py` (check the script header for its exact invocation). The count goes from 185 to 187.

- [ ] **Step 9: Commit**

```bash
git add src tests README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md
git commit -m "feat: add campaigns wiki export and import"
```

### Task 2: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`, in the `campaign wiki` section, after the `campaigns wiki delete moves children up` check and before `# deleting the remaining pages expecting an empty wiki again` (~line 2834).

**Interfaces:** Consumes the `campaigns wiki export` and `campaigns wiki import` commands from Task 1.

- [ ] **Step 1: Add the checks.** Follow the section's style: a `# …` narration line before each step, `2>"$WORK/cli.err"`, and `fail`/`ok`. At this point the wiki holds exactly two pages, `Smoke Child` and `Smoke Stub`.

```bash
# exporting the wiki as a JSON bundle expecting the remaining pages
"$CLI" campaigns wiki export --id "$WIKI" --format json --output "$WORK/wiki.json" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki export (json) exited non-zero"; }
jq -e '[.pages[].title] | sort == ["Smoke Child", "Smoke Stub"]' "$WORK/wiki.json" >/dev/null \
  || fail "the json export should hold the two remaining pages: $(cat "$WORK/wiki.json")"
# exporting the wiki as a markdown zip to stdout expecting one file per page
"$CLI" campaigns wiki export --id "$WIKI" --output - >"$WORK/wiki.zip" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki export (md) exited non-zero"; }
[ "$(unzip -Z1 "$WORK/wiki.zip" | wc -l)" -eq 2 ] \
  || fail "the md export should be a zip of two files: $(unzip -Z1 "$WORK/wiki.zip" 2>&1)"
ok "campaigns wiki export as json and as a markdown zip"

# importing the JSON bundle back expecting two new pages beside the originals
"$CLI" campaigns wiki import --id "$WIKI" --file "$WORK/wiki.json" 2>"$WORK/cli.err" \
  | jq -e '.imported == 2 and .format == "grimoire-json"' >/dev/null \
  || { cat "$WORK/cli.err" >&2; fail "wiki import should create two pages from the bundle"; }
"$CLI" campaigns wiki list --id "$WIKI" | jq -e '[.[] | select(.title == "Smoke Child")] | length == 2' >/dev/null \
  || fail "import should add pages, never overwrite"
ok "campaigns wiki import adds pages from a bundle"
```

  The child's body still links `[[Smoke Lore Renamed]]`, whose page is deleted by now, so the import may also create a stub. That is why the list check counts the `Smoke Child` copies rather than the total.

  Check `unzip` is available in the CI smoke job (`.github/workflows/`). If it isn't, use `python3 -m zipfile -l` or `jq`-free equivalents that are available, and say which in your report. The existing cleanup loop deletes all four pages afterwards, so the section still converges.

- [ ] **Step 2: Run the smoke test against the local stack, twice.**
  Run: `GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh` (check the script header for the env vars it reads). Both runs must pass.

- [ ] **Step 3: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover campaigns wiki export and import in the smoke test"
```
