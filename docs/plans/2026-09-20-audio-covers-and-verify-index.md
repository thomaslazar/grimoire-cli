# audio covers and verify-index Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the four audio cover commands ([#64](https://github.com/thomaslazar/grimoire-cli/issues/64)) and `addons verify-index`, the last uncovered addons endpoint.

**Architecture:** Four thin pass-throughs mirroring the shipped `systems cover` subgroup, in a new `AudioCoverCommands.cs`, plus one GET on `addons`. No new patterns.

**Tech Stack:** C# / .NET, System.CommandLine, Kiota-generated client, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-20-audio-covers-and-verify-index-design.md](../specs/2026-09-20-audio-covers-and-verify-index-design.md)

## Global Constraints

- Branch: `feat/audio-covers`, cut from `main`. Never commit to `main`.
- **Commit messages carry NO attribution.** No `Co-Authored-By:` line of any kind, and no "Generated with Claude Code" line — in commit messages, in the PR body, or in any file this change touches. This applies to every commit including fix-up commits, and to amended commits. Before opening the PR, Task 5 greps the branch to prove it.
- Conventional Commits, imperative, lowercase, no period, ~72 chars.
- The spec and this plan are committed **on the feature branch**, in Task 1's commit.
- All four audio cover commands carry `AddRoleRequired("gm or admin")` and a `permissionHint` of `"the gm or admin role"`. `addons verify-index` is guarded by `get_current_user` and carries **neither**.
- **`CoverFromSourceAsync` passes no `notFoundHint`** — see Task 1 Step 6 for why.
- `audio cover get` follows the settled `--output` convention: required flag, `-` for stdout, `SavedFile` response example, `SendStreamAsync`, `BodyInputException` caught and mapped to exit 1. The other three are JSON on stdout and take no `--output`.
- **No shared helper with `SystemsService`/`CoverCommands`.** Near-identical per-group code is the convention here; `AudioService` gets its own `MimeForExtension`.
- Run `dotnet format GrimoireCli.sln` after touching any C# file.
- Help text is terse: nothing restating a flag's own description or a field visible in the rendered response sample.
- Never touch `CHANGELOG.md`. `docs/roadmap.md` is not touched at all in this change — its `## Next` is empty and this work was never listed there.
- Writes go to the local Docker stack only, never any other instance.

---

### Task 1: The five service methods

**Files:**
- Modify: `src/GrimoireCli/Services/AudioService.cs`, `src/GrimoireCli/Services/AddonsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/AudioServiceTests.cs`, `AddonsServiceTests.cs`
- Commit alongside: `docs/specs/2026-09-20-audio-covers-and-verify-index-design.md`, `docs/plans/2026-09-20-audio-covers-and-verify-index.md`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync` / `SendStreamAsync`, and the shapes already in these files.
- Produces: `AudioService.CoverAsync(string id)` → `Task<Stream>`; `UploadCoverAsync(string id, string filePath)`, `DeleteCoverAsync(string id)`, `CoverFromSourceAsync(string id, string sourceType, string sourceId)` → `Task<string>`; `AddonsService.VerifyIndexAsync(string url)` → `Task<string>`. Task 2 calls exactly these names.

- [ ] **Step 1: Cut the branch**

```bash
git checkout -b feat/audio-covers
```

- [ ] **Step 2: Write the failing tests**

Append inside the existing `AudioServiceTests` class, reusing whatever client/URI helpers that file already defines — read it first rather than adding duplicates:

```csharp
    [Fact]
    public void EachCoverRouteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Audio["a1"];
        Assert.Contains("/api/audio/a1/cover", Uri(api.Cover.ToGetRequestInformation()));
        Assert.Contains("/api/audio/a1/cover", Uri(api.Cover.ToDeleteRequestInformation()));
        Assert.Contains("/api/audio/a1/cover/from-source", Uri(api.Cover.FromSource.ToPostRequestInformation(
            new Generated.Models.AudioCoverSourceIn { SourceType = "book", SourceId = "b1" })));
    }

    // The server reads the part by name; a rename would upload nothing and the
    // failure would look like a validation error rather than a client bug.
    [Fact]
    public void TheUploadPartIsNamedFile()
    {
        var body = AudioService.BuildCoverUploadBody(new byte[] { 1, 2, 3 }, "cover.png");
        Assert.NotNull(body.GetPart("file"));
    }
```

Append inside `AddonsServiceTests`:

```csharp
    [Fact]
    public void VerifyIndexSendsUrlAsAQueryParameter()
    {
        var info = new AddonsService(Client()).VerifyIndexRequest("https://example.test/index.json");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Contains("url=", info.URI.AbsoluteUri);
    }
```

If `AddonsServiceTests` has no client helper, add one matching the shape used in `TagsServiceTests`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "AudioServiceTests|AddonsServiceTests"`
Expected: compile error — the new members do not exist.

- [ ] **Step 4: Add `CoverAsync` and `DeleteCoverAsync` to `AudioService.cs`**

```csharp
    /// <summary>
    /// GET /api/audio/{id}/cover. Serves only the deliberately-set cover and
    /// 404s when there is none (routers/audio/covers.py:149-165); ArtworkAsync
    /// is the one that resolves folder and embedded art too.
    /// </summary>
    public async Task<Stream> CoverAsync(string id)
        => await _client.SendStreamAsync(
            _client.Api.Api.Audio[id].Cover.ToGetRequestInformation(),
            permissionHint: "the gm or admin role");

    /// <summary>
    /// DELETE /api/audio/{id}/cover. Clears the set cover, then recomputes
    /// has_artwork from folder and embedded art, so a track can still serve
    /// artwork afterwards (routers/audio/covers.py:128-146).
    /// </summary>
    public async Task<string> DeleteCoverAsync(string id)
        => await _client.SendAsync(
            _client.Api.Api.Audio[id].Cover.ToDeleteRequestInformation(),
            permissionHint: "the gm or admin role");
```

Check whether `SendStreamAsync` in this repo accepts a `permissionHint` argument before using it that way; if it does not, match how the shipped `ArtworkAsync` calls it.

- [ ] **Step 5: Add `UploadCoverAsync` and its helpers to `AudioService.cs`**

Model this on `SystemsService.UploadCoverAsync` — read it first and match its error handling exactly.

```csharp
    /// <summary>POST /api/audio/{id}/cover. Multipart; the server checks the content type, then the size.</summary>
    public async Task<string> UploadCoverAsync(string id, string filePath)
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
        var info = _client.Api.Api.Audio[id].Cover.ToPostRequestInformation(BuildCoverUploadBody(bytes, filePath));
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// Internal so a test can pin the part name the server reads the file from.
    /// </summary>
    internal static Microsoft.Kiota.Abstractions.MultipartBody BuildCoverUploadBody(byte[] bytes, string filePath)
    {
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("file", MimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        return body;
    }
```

Copy `MimeForExtension` from `SystemsService` into `AudioService` as its own private/internal member, with the same comment explaining that unknown extensions send octet-stream and let the server refuse. Do not extract a shared helper.

- [ ] **Step 6: Add `CoverFromSourceAsync` to `AudioService.cs`**

```csharp
    /// <summary>
    /// POST /api/audio/{id}/cover/from-source. Same validator as the systems
    /// verb: map, token, book or audio, with campaign_file excluded by the
    /// route's own schema (routers/audio/_schemas.py:100-118), so sending it is
    /// a 422.
    /// </summary>
    // No notFoundHint: this route has two independent 404 sources — the track
    // lookup, and load_source_image's per-source messages ("Book not found",
    // "That book has no cover thumbnail", etc.) — and a hint would replace the
    // server's discriminating body with one that cannot tell them apart.
    public async Task<string> CoverFromSourceAsync(string id, string sourceType, string sourceId)
    {
        var body = new Generated.Models.AudioCoverSourceIn { SourceType = sourceType, SourceId = sourceId };
        var info = _client.Api.Api.Audio[id].Cover.FromSource.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }
```

- [ ] **Step 7: Add `VerifyIndexAsync` to `AddonsService.cs`**

```csharp
    /// <summary>
    /// GET /api/addons/verify-index. Guarded by get_current_user, so it names no
    /// permissionHint. The server normalizes both the given URL and every
    /// trusted URL before comparing (addons/constants.py:42-49), which is why
    /// the CLI does not compare against addons list's trusted_index_urls itself.
    /// </summary>
    public async Task<string> VerifyIndexAsync(string url)
        => await _client.SendAsync(VerifyIndexRequest(url));

    /// <summary>Internal so a test can pin the query parameter's wire name.</summary>
    internal RequestInformation VerifyIndexRequest(string url)
        => _client.Api.Api.Addons.VerifyIndex.ToGetRequestInformation(c => c.QueryParameters.Url = url);
```

`AddonsService.cs` may need `using Microsoft.Kiota.Abstractions;` for `RequestInformation`.

- [ ] **Step 8: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build clean, everything passes.

- [ ] **Step 9: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add docs/specs/2026-09-20-audio-covers-and-verify-index-design.md \
        docs/plans/2026-09-20-audio-covers-and-verify-index.md \
        src/GrimoireCli/Services/ tests/GrimoireCli.Tests/Services/
git commit -m "feat: add the audio cover and verify-index sends"
```

---

### Task 2: The five commands

**Files:**
- Create: `src/GrimoireCli/Commands/AudioCoverCommands.cs`
- Modify: `src/GrimoireCli/Commands/AudioCommand.cs`, `src/GrimoireCli/Commands/AddonsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/AudioCommandTests.cs`, `AddonsCommandTests.cs`

**Interfaces:**
- Consumes: the five methods from Task 1, plus `ConsoleOutput.WriteStreamAsync`, `ConsoleOutput.WriteRawJson`, `CommandHelper.BuildClient`, `AddHelpSection`, `AddExamples`, `AddResponseExample<T>`, `AddRoleRequired`.
- Produces: `AudioCoverCommands.Create()` returning the `cover` subgroup, registered by `AudioCommand.Create()`; `verify-index` on the `addons` group.

**Reference shape.** `src/GrimoireCli/Commands/CoverCommands.cs` is the systems cover subgroup and the model for this file — same usings, same `_logger` field, same `Create()` shape, same streaming `get`. Read it before writing.

- [ ] **Step 1: Write the failing tests**

In `AudioCommandTests.cs`:

```csharp
    [Fact]
    public void TheGroupHostsTheCoverSubgroup()
    {
        Assert.Contains("cover", AudioCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("upload")]
    [InlineData("delete")]
    [InlineData("from-source")]
    public void TheCoverSubgroupHostsItsFourVerbs(string leaf)
    {
        var cover = AudioCommand.Create().Subcommands.Single(c => c.Name == "cover");
        Assert.Contains(leaf, cover.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void OnlyCoverGetTakesAnOutput()
    {
        var audio = AudioCommand.Create();
        Assert.NotEmpty(audio.Parse(["cover", "get", "--id", "a1"]).Errors);
        Assert.Empty(audio.Parse(["cover", "get", "--id", "a1", "--output", "-"]).Errors);
        Assert.NotEmpty(audio.Parse(["cover", "delete", "--id", "a1", "--output", "-"]).Errors);
    }

    [Fact]
    public void CoverWritesRequireTheirFlags()
    {
        var audio = AudioCommand.Create();
        Assert.NotEmpty(audio.Parse(["cover", "upload", "--id", "a1"]).Errors);
        Assert.Empty(audio.Parse(["cover", "upload", "--id", "a1", "--file", "c.png"]).Errors);
        Assert.NotEmpty(audio.Parse(["cover", "from-source", "--id", "a1", "--source-type", "book"]).Errors);
        Assert.Empty(audio.Parse(
            ["cover", "from-source", "--id", "a1", "--source-type", "book", "--source-id", "b1"]).Errors);
        Assert.Empty(audio.Parse(["cover", "delete", "--id", "a1"]).Errors);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("upload")]
    [InlineData("delete")]
    [InlineData("from-source")]
    public void EveryCoverVerbDeclaresTheGmOrAdminRole(string leaf)
    {
        var help = HelpRenderer.Render(AudioCommand.Create(), ["audio", "cover", leaf], full: false);
        Assert.Contains("Role required:", help);
        Assert.Contains("gm or admin", help);
    }

    // The whole reason both exist: artwork resolves the precedence chain, cover
    // get serves only what was deliberately set.
    [Fact]
    public void CoverGetDistinguishesItselfFromArtwork()
    {
        var help = HelpRenderer.Render(AudioCommand.Create(), ["audio", "cover", "get"], full: false);
        Assert.Contains("audio artwork", help);
    }

    // Deleting the set cover does not necessarily leave the track without art.
    [Fact]
    public void CoverDeleteWarnsArtworkCanSurvive()
    {
        var help = HelpRenderer.Render(AudioCommand.Create(), ["audio", "cover", "delete"], full: false);
        Assert.Contains("has_artwork", help);
    }
```

In `AddonsCommandTests.cs`:

```csharp
    [Fact]
    public void TheGroupHostsVerifyIndex()
    {
        Assert.Contains("verify-index", AddonsCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void VerifyIndexRequiresAUrlAndDeclaresNoRole()
    {
        Assert.NotEmpty(AddonsCommand.Create().Parse(["verify-index"]).Errors);
        Assert.Empty(AddonsCommand.Create().Parse(["verify-index", "--url", "https://example.test/i.json"]).Errors);
        Assert.DoesNotContain("Role required:",
            HelpRenderer.Render(AddonsCommand.Create(), ["addons", "verify-index"], full: true));
    }
```

If either test file renders help through its own helper rather than `HelpRenderer.Render`, use that file's helper.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: the new tests fail; nothing else does.

- [ ] **Step 3: Create `src/GrimoireCli/Commands/AudioCoverCommands.cs`**

```csharp
public static class AudioCoverCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("cover", "The track's deliberately-set cover image");
        command.Subcommands.Add(CreateGetCommand());
        command.Subcommands.Add(CreateUploadCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateFromSourceCommand());
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var outputOption = new Option<string>("--output")
        {
            Description = "Output file path, or '-' for binary to stdout",
            Required = true,
        };
        var command = new Command("get", "Download the track's set cover image")
        {
            idOption, outputOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Serves only a cover set through this group; 404 when the track has",
            "none, even if folder or embedded art exists. audio artwork resolves",
            "all three instead.",
            "",
            "--output - writes the image to stdout; a path writes the file and",
            "prints {path, bytes}.");
        command.AddExamples("grimoire-cli audio cover get --id <audio-id> --output cover.png");
        command.AddResponseExample<SavedFile>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            await using var stream = await service.CoverAsync(parseResult.GetValue(idOption)!);
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

    private static Command CreateUploadCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var fileOption = new Option<string>("--file") { Description = "Path to a PNG, JPEG, WebP or GIF", Required = true };
        var command = new Command("upload", "Upload a cover image for the track")
        {
            idOption, fileOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Replaces any cover already set. The server checks the content type",
            "first, then the size, so an oversized image answers 413 and an",
            "unsupported one 400.");
        command.AddExamples("grimoire-cli audio cover upload --id <audio-id> --file cover.png");
        command.AddResponseExample<Generated.Models.AudioCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            try
            {
                var result = await service.UploadCoverAsync(
                    parseResult.GetValue(idOption)!,
                    parseResult.GetValue(fileOption)!);
                ConsoleOutput.WriteRawJson(result);
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

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var command = new Command("delete", "Remove the track's set cover image")
        {
            idOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folder and embedded art are untouched and take over, so has_artwork",
            "can stay true and audio artwork keep serving an image.",
            "",
            "Responds {\"status\": \"ok\"} whether or not a cover was set.");
        command.AddExamples("grimoire-cli audio cover delete --id <audio-id>");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            var result = await service.DeleteCoverAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateFromSourceCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Audio ID", Required = true };
        var sourceTypeOption = new Option<string>("--source-type") { Description = "The kind of library item to copy the image from", Required = true };
        var sourceIdOption = new Option<string>("--source-id") { Description = "That item's ID", Required = true };
        var command = new Command("from-source", "Set the track's cover from an image already in the library")
        {
            idOption, sourceTypeOption, sourceIdOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--source-type takes map, token, book or audio; campaign_file is not a",
            "valid value here (422) since a track has no campaign to resolve it",
            "against.",
            "",
            "Replaces any cover already set.");
        command.AddExamples(
            "grimoire-cli audio cover from-source --id <audio-id> --source-type book --source-id <book-id>");
        command.AddResponseExample<Generated.Models.AudioCoverResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AudioService(client);
            var result = await service.CoverFromSourceAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(sourceTypeOption)!,
                parseResult.GetValue(sourceIdOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Register the subgroup**

In `AudioCommand.Create()`, add `command.Subcommands.Add(AudioCoverCommands.Create());` alongside the existing subcommand registrations, placed after `artwork` so the two image commands sit together.

- [ ] **Step 5: Add `addons verify-index`**

Register `command.Subcommands.Add(CreateVerifyIndexCommand());` in `AddonsCommand.Create()`, then:

```csharp
    private static Command CreateVerifyIndexCommand()
    {
        var urlOption = new Option<string>("--url") { Description = "The index URL to check", Required = true };
        var command = new Command("verify-index", "Check whether an add-on index URL is trusted")
        {
            urlOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Both sides are normalized before comparing, so a URL differing only",
            "in trailing slash still verifies — do not compare against",
            "trusted_index_urls yourself.");
        command.AddExamples("grimoire-cli addons verify-index --url https://example.test/index.json");
        command.AddResponseExample<Generated.Models.VerifyIndexResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new AddonsService(client);
            var result = await service.VerifyIndexAsync(parseResult.GetValue(urlOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 6: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 7: Read the rendered help, not the source**

```bash
for c in "audio cover get" "audio cover upload" "audio cover delete" \
         "audio cover from-source" "addons verify-index"; do
  echo "### $c"; dotnet run --project src/GrimoireCli -- $c --help
done
```

Check: the four audio commands show `Role required: gm or admin` and `addons verify-index` shows none; only `cover get` has `--output`; no Notes line restates a flag description. Trim anything that fails, except a line a Step 1 assertion depends on.

- [ ] **Step 8: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add src/GrimoireCli/Commands/ tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add audio cover commands and addons verify-index"
```

---

### Task 3: Smoke coverage and the live checks

**Files:**
- Modify: `docker/smoke-test.sh` (append a `--- audio covers and verify-index ---` block before the final `echo "smoke: all checks passed"`)

**Constraint:** the block writes a cover to a fixture track and must restore it. It verifies the fixture is clean **before** writing, the way the `systems cover from-source` block already added in this file does — read that block first and mirror its shape.

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

# (a) a fixture track's starting state, and what cover get does with no cover set.
TRACK=$($CLI audio list | jq -r '.audio[0].id')
$CLI audio get --id "$TRACK" | jq '{cover_image, has_artwork}'
$CLI audio cover get --id "$TRACK" --output /dev/null; echo "cover get exit=$? (expect non-zero)"

# (b) the write, and what has_artwork reads after the cover is removed again.
BOOK=$($CLI books list | jq -r '.books[0].id')
$CLI audio cover from-source --id "$TRACK" --source-type book --source-id "$BOOK"; echo "exit=$?"
$CLI audio get --id "$TRACK" | jq '{cover_image, has_artwork}'
$CLI audio cover get --id "$TRACK" --output /dev/null; echo "cover get exit=$? (expect 0 now)"
$CLI audio cover delete --id "$TRACK"; echo "delete exit=$?"
$CLI audio get --id "$TRACK" | jq '{cover_image, has_artwork}'   # has_artwork may stay true

# (c) verify-index, both ways.
TRUSTED=$($CLI addons list | jq -r '.trusted_index_urls[0] // ""')
$CLI addons verify-index --url "$TRUSTED"
$CLI addons verify-index --url "https://example.invalid/not-an-index.json"
```

Record every exit code and body. Two things decide what the smoke block can assert, so state both explicitly in your report:

- whether the fixture track starts with `cover_image` empty, and whether it is back to that after `cover delete`;
- what `has_artwork` reads at each of the three points, since the spec's claim that it can stay true is the one most likely to be wrong.

If `addons list` returns no `trusted_index_urls` on this stack, say so — the smoke block then asserts only the negative case.

- [ ] **Step 3: Append the smoke block**

Insert before the final `echo "smoke: all checks passed" >&2`. Adjust every `jq` path to what the responses actually contain — read one of each rather than assuming.

```bash
# --- audio covers and verify-index -------------------------------------------
# The cover write goes to a fixture track that carries no set cover, checked
# here rather than assumed, and is removed again at the end so a re-run
# converges.
SC_TRACK=$("$CLI" audio list 2>"$WORK/cli.err" | jq -r '.audio[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "audio list exited non-zero"; }
[ -n "$SC_TRACK" ] && [ "$SC_TRACK" != "null" ] || fail "no audio fixture for the cover checks"
"$CLI" audio get --id "$SC_TRACK" >"$WORK/track.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "audio get exited non-zero"; }
[ "$(jq -r '.cover_image // ""' "$WORK/track.out")" = "" ] \
  || fail "the audio fixture already has a set cover — the smoke fixture has drifted, pick another track: $(cat "$WORK/track.out")"

"$CLI" audio cover get --id "$SC_TRACK" --output "$WORK/nocover.bin" >/dev/null 2>&1 \
  && fail "audio cover get should 404 on a track with no set cover"
ok "audio cover get refuses a track with no set cover"

SC_BOOK=$("$CLI" books list 2>/dev/null | jq -r '.books[0].id')
[ -n "$SC_BOOK" ] && [ "$SC_BOOK" != "null" ] || fail "no book fixture to copy a cover from"
SET_JSON=$("$CLI" audio cover from-source --id "$SC_TRACK" --source-type book --source-id "$SC_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "audio cover from-source exited non-zero"; }
[ -n "$(echo "$SET_JSON" | jq -r '.cover_image // ""')" ] \
  || fail "from-source should report the stored cover filename: $SET_JSON"
ok "audio cover from-source copies a book's image onto the track"

"$CLI" audio cover get --id "$SC_TRACK" --output "$WORK/cover.bin" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "audio cover get exited non-zero after a cover was set"; }
[ -s "$WORK/cover.bin" ] || fail "audio cover get wrote an empty file"
ok "audio cover get serves the cover once one is set"

"$CLI" audio cover from-source --id "$SC_TRACK" --source-type campaign_file --source-id "$SC_BOOK" >/dev/null 2>&1 \
  && fail "audio cover from-source should refuse source-type campaign_file"
ok "audio cover from-source refuses source-type campaign_file"

"$CLI" audio cover delete --id "$SC_TRACK" >"$WORK/coverdel.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "audio cover delete exited non-zero"; }
"$CLI" audio get --id "$SC_TRACK" >"$WORK/track.out" 2>&1
[ "$(jq -r '.cover_image // ""' "$WORK/track.out")" = "" ] \
  || fail "the track's cover should be cleared again, so the run converges: $(cat "$WORK/track.out")"
ok "audio cover delete clears the set cover, so the run converges"

# Both sides are normalized server-side, so a client-side comparison against
# trusted_index_urls is exactly what this endpoint exists to replace.
VI_JSON=$("$CLI" addons verify-index --url "https://example.invalid/not-an-index.json" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons verify-index exited non-zero"; }
[ "$(echo "$VI_JSON" | jq -r .verified)" = "false" ] \
  || fail "an untrusted URL should not verify: $VI_JSON"
ok "addons verify-index rejects an untrusted index URL"
```

If Step 2 found a usable `trusted_index_urls` entry, add the positive case too, asserting `.verified` is `true` for it. If it found none, leave it out and say so in your report rather than asserting something the stack cannot support.

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed` both times. Run them strictly one after another, never concurrently — the script rewrites `$HOME/.grimoire-cli/config.json`.

- [ ] **Step 5: Confirm the fixture is back**

```bash
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli audio get --id "$SC_TRACK" | jq '{cover_image, has_artwork}'
```

`cover_image` must be empty. Report `has_artwork` as observed.

- [ ] **Step 6: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the audio cover commands in the smoke test"
```

---

### Task 4: Docs

**Files:**
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`

`docs/roadmap.md` is **not** touched: this work was never listed there.

- [ ] **Step 1: Add the README rows**

Place the audio rows with the audio group and the addons row with addons:

```markdown
| `audio cover get --id <id> --output <path\|->` | Download the track's set cover image (gm or admin) |
| `audio cover upload --id <id> --file <path>` | Upload a cover image for the track (gm or admin) |
| `audio cover delete --id <id>` | Remove the track's set cover image (gm or admin) |
| `audio cover from-source --id <id> --source-type <type> --source-id <id>` | Set a track cover from a library image (gm or admin) |
| `addons verify-index --url <url>` | Check whether an add-on index URL is trusted |
```

Check the `--output <path\|->` spelling against the existing streaming rows — that column has a house format and the last change got it wrong by dropping the `\|-` half.

- [ ] **Step 2: Add the coverage entries**

```python
    "GET /api/audio/{audio_id}/cover": "`audio cover get` ✅",
    "POST /api/audio/{audio_id}/cover": "`audio cover upload` ✅",
    "DELETE /api/audio/{audio_id}/cover": "`audio cover delete` ✅",
    "POST /api/audio/{audio_id}/cover/from-source": "`audio cover from-source` ✅",
    "GET /api/addons/verify-index": "`addons verify-index` ✅",
```

Check each path-parameter name against the existing rows in `docs/grimoire-api-coverage.md`.

- [ ] **Step 3: Regenerate the coverage table**

```bash
tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly the five rows change, plus the derived `addons`, `audio` and total lines. Any other row moving means the pin or the clone moved — stop and report rather than committing the drift.

- [ ] **Step 4: Record the verified behaviour**

Add to `docs/grimoire-api-notes.md`, matching the file's existing heading style and placement:

```markdown
### Audio covers

- `GET /api/audio/{id}/cover` serves only the deliberately-set cover and 404s
  when there is none, even on a track with folder or embedded art
  (`routers/audio/covers.py:149-165`, v1.7.1). `GET /api/audio/{id}/artwork` is
  the one that resolves all three in order. The split is deliberate upstream: it
  is how an editor tells "a cover was set here" apart from "the folder happens
  to have one".
- `DELETE /api/audio/{id}/cover` clears the set cover and then **recomputes**
  `has_artwork` from folder art and embedded art (`covers.py:128-146`), so a
  track can still report and serve artwork after its cover is removed.
- `POST /api/audio/{id}/cover` checks `content_type` first, then the size
  ceiling, then decodes: an unsupported type is 400, an oversized file 413, an
  empty one 400 (`covers.py:91-109`).
- `AudioCoverSourceIn` excludes `campaign_file` from its allowed set exactly as
  `SystemCoverSourceIn` does (`routers/audio/_schemas.py:100-118`), so the two
  `from-source` verbs behave identically, 422 included.

### Trusted add-on index URLs

- `GET /api/addons/verify-index` normalizes both the supplied URL and every
  trusted URL before comparing (`addons/constants.py:42-49`), so a URL differing
  only in normalization still verifies — which is why comparing against
  `addons list`'s `trusted_index_urls` client-side gives the wrong answer. It is
  guarded by `get_current_user` and carries no role; an omitted `url` yields
  `verified: false` rather than an error.
```

Replace any line with what Task 3 actually observed if the stack disagrees, and say so in the PR body.

- [ ] **Step 5: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md
git commit -m "docs: record the audio cover and verify-index commands"
```

---

### Task 5: Pre-PR verification and the PR

- [ ] **Step 1: Prove the branch carries no attribution**

```bash
git log --format='%B' $(git merge-base main HEAD)..HEAD | grep -niE '^Co-Authored-By:|Generated with \[Claude|🤖'
```

Expected: **no output.** If anything matches, stop and report it — the fix is a history rewrite on the branch before it is pushed, which is cheap now and expensive after the merge.

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
git push -u origin feat/audio-covers
gh pr create --title "feat: audio covers and verify-index" --body "…"
```

The body says what the five commands are, that `cover` and `artwork` are deliberately different reads, that `cover delete` can leave `has_artwork` true, and what was verified. **The body carries no attribution line of any kind** — no "Generated with Claude Code", no robot emoji, nothing naming a model or tool.

- [ ] **Step 4: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked, and present the PR URL as a clickable link.
