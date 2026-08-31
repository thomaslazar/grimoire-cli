# Controlled-vocabulary read commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `genres list`, `licenses list`, `parent-systems list`,
`system-families list` and `dice-materials list` — the five controlled-vocabulary
reads that make the already-shipped `systems update` and `books update` usable
without guesswork.

**Architecture:** One table-driven command file yielding five top-level groups,
each with a single `list` verb, over one service whose only job is to pick a
generated request builder. Every endpoint is a parameterless `GET` with no role
dependency, so the five commands differ only in the vocabulary they name and one
line of Notes.

**Tech Stack:** C# / .NET 10, `System.CommandLine`, Kiota-generated API client,
xUnit, bash smoke test.

**Design spec:** [docs/specs/2026-08-31-lookup-commands-design.md](../specs/2026-08-31-lookup-commands-design.md)

## Global Constraints

- **Branch:** `feat/lookup-commands`. Never commit to `main`.
- **Conventional Commits**, `type: subject`, imperative, lowercase, no period,
  max ~72 chars. **No `Co-Authored-By:` lines. No "Generated with Claude Code"
  attribution** anywhere.
- **No `AddRoleRequired` on any command in this plan.** All five reads are
  `Depends(get_current_user)` in `temp/grimoire/backend/routers/lookups/core.py`,
  which carries no role and gets no tag. Adding one would contradict the router.
- **No `permissionHint` on any `SendAsync` call in this plan** — the hint must
  agree with the tag, and there is no tag.
- **Thin pass-through.** No command reads a response, derives a warning,
  validates a value against a vocabulary, or nests the flat genre list into a
  tree.
- **stdout is the server's bytes** via `ConsoleOutput.WriteRawJson`. Logs go to
  stderr.
- **Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.**
  CI fails on `--verify-no-changes`.
- **No blank lines inside method bodies** between consecutive `Subcommands.Add`
  or option declarations, or before a `return` that follows setup calls.
- **`CHANGELOG.md` is owned by the release process.** Do not touch it.
- **`docs/grimoire-api-coverage.md` is generated.** Edit `IMPLEMENTED` in
  `tools/generate-api-coverage.py` and regenerate; never hand-edit the markdown.
- Verified against `hunterreadca/grimoire:1.6.0`, source clone at tag `v1.6.0`.

---

## File Structure

- **Create** `src/GrimoireCli/Commands/LookupCommands.cs` — the vocabulary table
  and the `list` command builder. Sole owner of the help text.
- **Create** `src/GrimoireCli/Services/LookupsService.cs` — maps a vocabulary
  name to its generated request builder and sends.
- **Create** `tests/GrimoireCli.Tests/Services/LookupsServiceTests.cs` — pins each
  vocabulary to its request path.
- **Create** `tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs` — help
  rendering, the no-role-section assertion, and parsing.
- **Modify** `src/GrimoireCli/Program.cs` — register the five groups.
- **Modify** `src/GrimoireCli/Commands/SystemsCommand.cs` — one Notes line on
  `update`.
- **Modify** `src/GrimoireCli/Commands/BooksCommand.cs` — one Notes line on
  `update`.
- **Modify** `docs/grimoire-api-notes.md`, `docs/cli-design.md`, `README.md`,
  `tools/generate-api-coverage.py`, `docker/smoke-test.sh`.

No file in this plan exceeds ~120 lines.

---

### Task 0: Commit the docs already on the branch

The spec and the roadmap augmentation were written during design and are
uncommitted. They land first so later commits are code-only.

**Files:**
- Commit: `docs/specs/2026-08-31-lookup-commands-design.md` (new),
  `docs/roadmap.md` (modified), `docs/plans/2026-08-31-lookup-commands.md` (new)

- [ ] **Step 1: Confirm the branch and the working tree**

```bash
git rev-parse --abbrev-ref HEAD   # must print feat/lookup-commands
git status --short
```

Expected: `?? docs/specs/2026-08-31-lookup-commands-design.md`,
`?? docs/plans/2026-08-31-lookup-commands.md`, ` M docs/roadmap.md`. Nothing else.

- [ ] **Step 2: Commit**

```bash
git add docs/specs/2026-08-31-lookup-commands-design.md \
        docs/plans/2026-08-31-lookup-commands.md \
        docs/roadmap.md
git commit -m "docs: design the vocabulary read commands"
```

---

### Task 1: `LookupsService`

**Files:**
- Create: `src/GrimoireCli/Services/LookupsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/LookupsServiceTests.cs`

**Interfaces:**
- Consumes: `GrimoireCli.Api.GrimoireApiClient` — `.Api.Api.{Genres, Licenses,
  ParentSystems, SystemFamilies, DiceMaterials}.ToGetRequestInformation()` and
  `Task<string> SendAsync(RequestInformation info, string? permissionHint = null,
  string? notFoundHint = null, TimeSpan? timeout = null)`.
- Produces: `LookupsService(GrimoireApiClient client)`,
  `Task<string> ListAsync(string vocabulary)`, and
  `internal RequestInformation RequestFor(string vocabulary)`. The five accepted
  vocabulary strings are exactly `"genres"`, `"licenses"`, `"parent-systems"`,
  `"system-families"`, `"dice-materials"`.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Services/LookupsServiceTests.cs`:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins each vocabulary to the path its generated builder produces. A client
/// regeneration that moves a builder would otherwise silently read the wrong
/// vocabulary, which no help text or response assertion would catch.
/// </summary>
public class LookupsServiceTests
{
    private static LookupsService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    [Theory]
    [InlineData("genres", "/api/genres")]
    [InlineData("licenses", "/api/licenses")]
    [InlineData("parent-systems", "/api/parent-systems")]
    [InlineData("system-families", "/api/system-families")]
    [InlineData("dice-materials", "/api/dice-materials")]
    public void EachVocabularyResolvesToItsOwnPath(string vocabulary, string expectedPath)
    {
        var info = Service().RequestFor(vocabulary);
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test" + expectedPath, info.URI.AbsoluteUri);
    }

    // The reads take no query parameters at all — the only one the spec declares
    // is `token`, the alternative auth scheme, which the CLI never uses because
    // the bearer header is set on the HttpClient.
    [Theory]
    [InlineData("genres")]
    [InlineData("dice-materials")]
    public void NoQueryStringIsSent(string vocabulary)
    {
        var info = Service().RequestFor(vocabulary);
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.DoesNotContain("?", info.URI.AbsoluteUri);
    }

    [Fact]
    public void AnUnknownVocabularyThrows()
    {
        Assert.Throws<ArgumentException>(() => Service().RequestFor("tags"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LookupsServiceTests`

Expected: build failure — `LookupsService` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/GrimoireCli/Services/LookupsService.cs`:

```csharp
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The five controlled-vocabulary reads. Every one is a parameterless GET guarded
/// only by get_current_user (routers/lookups/core.py), so there is no
/// permissionHint to name, and no id appears in any path, so there is no
/// notFoundHint either.
/// </summary>
public class LookupsService
{
    private readonly GrimoireApiClient _client;

    public LookupsService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync(string vocabulary)
        => await _client.SendAsync(RequestFor(vocabulary));

    /// <summary>
    /// Internal (not private) so a test can pin each vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation RequestFor(string vocabulary) => vocabulary switch
    {
        "genres" => _client.Api.Api.Genres.ToGetRequestInformation(),
        "licenses" => _client.Api.Api.Licenses.ToGetRequestInformation(),
        "parent-systems" => _client.Api.Api.ParentSystems.ToGetRequestInformation(),
        "system-families" => _client.Api.Api.SystemFamilies.ToGetRequestInformation(),
        "dice-materials" => _client.Api.Api.DiceMaterials.ToGetRequestInformation(),
        _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
    };
}
```

- [ ] **Step 4: Format, then run the test to verify it passes**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LookupsServiceTests
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Services/LookupsService.cs \
        tests/GrimoireCli.Tests/Services/LookupsServiceTests.cs
git commit -m "feat: add the vocabulary read service"
```

---

### Task 2: The five `list` commands

**Files:**
- Create: `src/GrimoireCli/Commands/LookupCommands.cs`
- Modify: `src/GrimoireCli/Program.cs`
- Test: `tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs`

**Interfaces:**
- Consumes: `LookupsService.ListAsync(string vocabulary)` from Task 1;
  `CommandHelper.BuildClient(string? serverOverride = null)` returning
  `(GrimoireApiClient client, AppConfig config)`;
  `command.AddHelpSection(string title, HelpSectionPosition position, params string[] lines)`;
  `command.AddExamples(params string[] examples)`;
  `command.AddResponseExample<T>()`; `ConsoleOutput.WriteRawJson(string)`.
- Produces: `LookupCommands.Create()` returning `IEnumerable<Command>` — five
  groups named `genres`, `licenses`, `parent-systems`, `system-families`,
  `dice-materials`, each holding one `list` subcommand.

- [ ] **Step 1: Write the failing test**

Create `tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// The five vocabulary groups. The no-role-section assertion is the point: these
/// are the first commands whose route is deliberately untagged
/// (Depends(get_current_user), not require_not_guest), so a later reflexive
/// AddRoleRequired must fail here.
/// </summary>
public class LookupCommandTests
{
    public static TheoryData<string> Vocabularies() =>
        new("genres", "licenses", "parent-systems", "system-families", "dice-materials");

    private static Command Group(string name) =>
        LookupCommands.Create().Single(c => c.Name == name);

    [Fact]
    public void CreateYieldsTheFiveVocabularyGroups()
    {
        Assert.Equal(
            ["genres", "licenses", "parent-systems", "system-families", "dice-materials"],
            LookupCommands.Create().Select(c => c.Name).ToArray());
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachGroupHasExactlyOneListSubcommand(string name)
    {
        var group = Group(name);
        Assert.Equal(["list"], group.Subcommands.Select(c => c.Name).ToArray());
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpRendersNotesThenExamplesThenOptions(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        var notes = output.IndexOf("Notes:", StringComparison.Ordinal);
        var examples = output.IndexOf("Examples:", StringComparison.Ordinal);
        var options = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(notes >= 0, "Notes section missing");
        Assert.True(options > notes, "Notes must render before Options");
        Assert.True(examples > options, "Examples must render after Options");
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpCarriesTheSharedCaveats(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        Assert.Contains("Submit name, not id", output);
        Assert.Contains("Nothing validates a written value", output);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpCarriesAResponseShape(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: true);
        Assert.Contains("Response shape:", output);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpHasNoRoleSection(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: true);
        Assert.DoesNotContain("Role required:", output);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListParsesAndAcceptsServer(string name)
    {
        var group = Group(name);
        Assert.Empty(group.Parse(["list"]).Errors);
        Assert.Empty(group.Parse(["list", "--server", "http://example.test"]).Errors);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void AnUnknownSubcommandErrors(string name)
    {
        Assert.NotEmpty(Group(name).Parse(["create", "--name", "x"]).Errors);
    }

    // The response shape is the only place the id/name distinction the Notes warn
    // about is visible, so it must actually show both.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ResponseShapeShowsBothIdAndName(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: true);
        var start = output.IndexOf("Response shape:", StringComparison.Ordinal);
        var block = output[start..];
        Assert.Contains("\"id\"", block);
        Assert.Contains("\"name\"", block);
    }

    [Fact]
    public void GenresNoteTheirTiering()
    {
        var output = HelpRenderer.Render(Group("genres"), ["genres", "list"], full: false);
        Assert.Contains("parent_id", output);
    }

    [Fact]
    public void ParentSystemsWarnTheyShipEmpty()
    {
        var output = HelpRenderer.Render(Group("parent-systems"), ["parent-systems", "list"], full: false);
        Assert.Contains("ships empty", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiceMaterialsNoteTheirGroupField()
    {
        var output = HelpRenderer.Render(Group("dice-materials"), ["dice-materials", "list"], full: false);
        Assert.Contains("group", output);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LookupCommandTests`

Expected: build failure — `LookupCommands` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/GrimoireCli/Commands/LookupCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

/// <summary>
/// The five controlled-vocabulary reads, one top-level group each — the shape
/// abs-cli settled for its own genres / tags / narrators. Every endpoint is a
/// parameterless GET with no role dependency, so the five commands differ only
/// in the vocabulary they name and one line of Notes; hence one table rather
/// than five near-identical files.
/// </summary>
public static class LookupCommands
{
    /// <summary>
    /// Caveats shared by all five. Neither is recoverable from the response
    /// sample, which shows id and name side by side without saying which one a
    /// write takes and says nothing about validation.
    /// </summary>
    private static readonly string[] SharedNotes =
    [
        "Submit name, not id — systems and books store the name. id addresses the",
        "vocabulary entry itself.",
        "",
        "Nothing validates a written value against this list: an unmatched string",
        "is stored as written and stops matching systems list --genre.",
        "",
    ];

    private sealed record Vocabulary(
        string Name,
        string GroupDescription,
        string ListDescription,
        string[] Notes,
        Action<Command> AddResponseExample);

    private static readonly Vocabulary[] Vocabularies =
    [
        new("genres", "The genre vocabulary", "List all genres (tiered)",
            ["parent_id links a child to its parent. Ordered by sort_order, then name."],
            command => command.AddResponseExample<Generated.Models.GenresResponse>()),
        new("licenses", "The license vocabulary", "List all licenses",
            ["is_default false is a custom entry."],
            command => command.AddResponseExample<Generated.Models.LicensesResponse>()),
        new("parent-systems", "The parent-system vocabulary", "List all parent systems",
            [
                "Ships empty: Grimoire seeds no defaults, and a container child's",
                "parent_system is folder-derived, so a value in use need not appear here.",
            ],
            command => command.AddResponseExample<Generated.Models.ParentSystemsResponse>()),
        new("system-families", "The system-family vocabulary", "List all system families",
            ["is_default false is a custom entry."],
            command => command.AddResponseExample<Generated.Models.SystemFamiliesResponse>()),
        new("dice-materials", "The dice/material vocabulary", "List all dice/materials",
            ["group buckets the entry, and is Custom when unset."],
            command => command.AddResponseExample<Generated.Models.DiceMaterialsResponse>()),
    ];

    public static IEnumerable<Command> Create()
    {
        foreach (var vocabulary in Vocabularies)
        {
            var group = new Command(vocabulary.Name, vocabulary.GroupDescription);
            group.Subcommands.Add(CreateListCommand(vocabulary));
            yield return group;
        }
    }

    private static Command CreateListCommand(Vocabulary vocabulary)
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", vocabulary.ListDescription) { serverOption };
        command.AddHelpSection("Notes", HelpSectionPosition.Top, [.. SharedNotes, .. vocabulary.Notes]);
        command.AddExamples($"grimoire-cli {vocabulary.Name} list");
        vocabulary.AddResponseExample(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new LookupsService(client);
            var result = await service.ListAsync(vocabulary.Name);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 4: Register the groups in `Program.cs`**

`src/GrimoireCli/Program.cs` currently reads, around line 48:

```csharp
rootCommand.Subcommands.Add(MeCommand.Create());
rootCommand.Subcommands.Add(ConfigCommand.Create());
rootCommand.Subcommands.Add(SystemsCommand.Create());
rootCommand.Subcommands.Add(BooksCommand.Create());
rootCommand.Subcommands.Add(LibraryCommand.Create());
rootCommand.Subcommands.Add(AddonsCommand.Create());
rootCommand.Subcommands.Add(SelfTestCommand.Create());
```

Insert the loop after the `AddonsCommand` line and before `SelfTestCommand`, so
`self-test` stays last:

```csharp
rootCommand.Subcommands.Add(AddonsCommand.Create());
foreach (var lookup in LookupCommands.Create())
    rootCommand.Subcommands.Add(lookup);
rootCommand.Subcommands.Add(SelfTestCommand.Create());
```

- [ ] **Step 5: Format, then run the full test suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build clean, all tests pass. `JsonExamplesDriftTest` must still pass —
the five response samples already exist in `JsonExamples.g.cs`, so no generator
run is needed; if it fails, stop and report rather than regenerating.

- [ ] **Step 6: Verify the help by hand**

```bash
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli genres list --help-full
src/GrimoireCli/bin/Debug/net10.0/grimoire-cli --help
```

Expected: the first prints Notes (shared caveats + the `parent_id` line),
Options with `--server`, Examples, and a Response shape — and **no** "Role
required". The second lists all five new groups.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/LookupCommands.cs \
        src/GrimoireCli/Program.cs \
        tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs
git commit -m "feat: add vocabulary list commands"
```

---

### Task 3: Point the update commands at the vocabularies

Cross-references run consumer → producer only, so the pointer lives on `update`.
Without this the new commands ship with nothing referring to them.

**Files:**
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs` (the `update` Notes block,
  near line 152)
- Modify: `src/GrimoireCli/Commands/BooksCommand.cs` (the `update` Notes block)
- Modify: `docs/grimoire-api-notes.md`

- [ ] **Step 1: Write the failing test**

Append to `tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs`:

```csharp
    // Cross-references are one-way, consumer -> producer, so `update` is where the
    // pointer at the vocabularies has to live.
    [Fact]
    public void SystemsUpdateNamesTheVocabularyCommands()
    {
        var output = HelpRenderer.Render(SystemsCommand.Create(), ["systems", "update"], full: false);
        Assert.Contains("genres list", output);
        Assert.Contains("dice-materials list", output);
        Assert.Contains("stored as written", output);
    }

    [Fact]
    public void BooksUpdateNamesOnlyTheVocabulariesItAccepts()
    {
        var output = HelpRenderer.Render(BooksCommand.Create(), ["books", "update"], full: false);
        Assert.Contains("genres list", output);
        Assert.Contains("licenses list", output);
        Assert.DoesNotContain("dice-materials list", output);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LookupCommandTests`

Expected: the two new tests FAIL — the help text does not name the commands yet.

- [ ] **Step 3: Add the Notes line to `systems update`**

In `src/GrimoireCli/Commands/SystemsCommand.cs`, inside the `update` command's
existing `AddHelpSection("Notes", …)` call, add these lines after the existing
`"Prefer genres and character_builder_urls; the singles are legacy."` line,
separated by an empty string:

```csharp
            "",
            "genres, license, parent_system, system_family and dice_materials draw",
            "on vocabularies: genres list, licenses list, parent-systems list,",
            "system-families list, dice-materials list. Nothing validates against",
            "them — an unmatched value is stored as written.",
```

- [ ] **Step 4: Add the Notes line to `books update`**

Open `src/GrimoireCli/Commands/BooksCommand.cs` and find the `update` command's
`AddHelpSection("Notes", …)` call. Add these lines at the end of its argument
list, preceded by an empty string:

```csharp
            "",
            "genres and license draw on vocabularies: genres list, licenses list.",
            "Nothing validates against them — an unmatched value is stored as",
            "written.",
```

`BookUpdate` carries no `parent_system`, `system_family` or `dice_materials`, so
those three must not be named here.

- [ ] **Step 5: Record the verified behaviour in `docs/grimoire-api-notes.md`**

Append a section, matching the file's existing heading level and prose style:

```markdown
## Controlled vocabularies

Read from `backend/routers/lookups/` at tag `v1.6.0`.

- **Systems and books store the vocabulary `name`, not the `id`.** Every usage
  count in `_helpers.py` matches on `name`, case-insensitively and with
  surrounding whitespace stripped (`_matches`). The `id` a lookup read returns
  addresses the vocabulary entry itself, which only `DELETE` needs.
- **No write path validates a value against a vocabulary.**
  `services/bulk_service.py:apply_updates` is a blind `setattr` loop over the
  payload; no lookup table is consulted by `PATCH /api/systems/{id}`,
  `PATCH /api/books/{id}` or either `bulk` endpoint. An unmatched string is
  stored as written, and merely stops matching `?genre=` and the server's own
  usage counts. The five lists are conventions to agree with, not enforced sets.
- **`parent-systems` ships empty.** `models/lookup_defaults.py` seeds genres,
  system families, licenses and dice materials, but `DEFAULT_PARENT_SYSTEMS` is
  `()`. A container child's `parent_system` is folder-derived, so values in use
  and values in the vocabulary diverge freely.
- **All five reads are `Depends(get_current_user)`** — no role, guests included.
  Only the `POST` and `DELETE` on each path are `require_admin`.
- **A `DELETE` strips nothing.** It removes the vocabulary row only; every system
  and book carrying that name keeps it, because the value is a string rather than
  a foreign key. The response field is named `removed_usage` but reports the
  count that *would* have blocked the delete. Deleting a genre cascades to its
  child genres.
```

- [ ] **Step 6: Format, then run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all tests pass, including the two added in Step 1. `HelpOutputTests`
and any `GetExampleCount` assertions must still pass — if a help-length
assertion breaks, report it rather than trimming the new caveat.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/SystemsCommand.cs \
        src/GrimoireCli/Commands/BooksCommand.cs \
        tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs \
        docs/grimoire-api-notes.md
git commit -m "docs: point the update commands at the vocabularies"
```

---

### Task 4: README, coverage table and CLI design notes

**Files:**
- Modify: `README.md` (the Commands table)
- Modify: `tools/generate-api-coverage.py` (the `IMPLEMENTED` dict, from line 94)
- Regenerate: `docs/grimoire-api-coverage.md`
- Modify: `docs/cli-design.md`

- [ ] **Step 1: Add the five rows to the README Commands table**

Insert after the last `addons` row (`addons settings …`):

```markdown
| `genres list` | List the genre vocabulary (tiered via `parent_id`) |
| `licenses list` | List the license vocabulary |
| `parent-systems list` | List the parent-system vocabulary (ships empty) |
| `system-families list` | List the system-family vocabulary |
| `dice-materials list` | List the dice/material vocabulary |
```

No role suffix on any row — these carry no role requirement.

- [ ] **Step 2: Add the five entries to `IMPLEMENTED`**

In `tools/generate-api-coverage.py`, add to the `IMPLEMENTED` dict:

```python
    "GET /api/genres": "`genres list` ✅",
    "GET /api/licenses": "`licenses list` ✅",
    "GET /api/parent-systems": "`parent-systems list` ✅",
    "GET /api/system-families": "`system-families list` ✅",
    "GET /api/dice-materials": "`dice-materials list` ✅",
```

- [ ] **Step 3: Bring up the stack and regenerate the coverage table**

```bash
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: the `lookups` count moves from `0 / 15` to `5 / 15`, and the five
`GET` rows gain their CLI column. If any other row changes, stop and report —
that would mean the running stack disagrees with the committed table.

- [ ] **Step 4: Add the five to `docs/cli-design.md` and record the deviation**

Add the five groups to that file's resource list, then append to its
"Deviations from abs-cli" section:

```markdown
- **Five top-level vocabulary groups, not one umbrella noun.** `genres`,
  `licenses`, `parent-systems`, `system-families` and `dice-materials` each get
  their own group, which is the shape abs-cli settled for `genres` / `tags` /
  `narrators`. It sits against this file's "resource surface is short by design"
  line above, and the parity wins: the API's own tag is `lookups`, but grouping
  five distinct endpoints behind one noun would have made a flag select the
  endpoint, which no other command here does.
```

- [ ] **Step 5: Verify the docs build nothing and nothing else drifted**

```bash
git status --short
```

Expected: exactly `README.md`, `tools/generate-api-coverage.py`,
`docs/grimoire-api-coverage.md`, `docs/cli-design.md` modified.

- [ ] **Step 6: Commit**

```bash
git add README.md tools/generate-api-coverage.py \
        docs/grimoire-api-coverage.md docs/cli-design.md
git commit -m "docs: record the vocabulary commands in the README and coverage"
```

---

### Task 5: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`

The block is read-only, so it is idempotent by construction — a re-run converges
because it writes nothing.

- [ ] **Step 1: Add the block**

Insert after the existing `systems list` JSON assertion (the block ending
`ok "systems list returned JSON on stdout"`), keeping the file's existing
`fail` / `ok` helpers and `$WORK` scratch directory:

```bash
# The five controlled-vocabulary reads. Read-only, so this block is idempotent.
# parent-systems is asserted present but allowed to be empty: Grimoire's
# DEFAULT_PARENT_SYSTEMS is (), so a non-empty assertion would fail on a fresh
# stack, while the other four are seeded.
for pair in "genres:genres" "licenses:licenses" "parent-systems:parent_systems" \
            "system-families:families" "dice-materials:dice_materials"; do
  cmd="${pair%%:*}"
  key="${pair##*:}"
  "$CLI" "$cmd" list >"$WORK/$cmd.out" 2>"$WORK/$cmd.err" \
    || { cat "$WORK/$cmd.err" >&2; fail "$cmd list exited non-zero"; }
  jq -e "has(\"$key\")" "$WORK/$cmd.out" >/dev/null \
    || fail "$cmd list did not return a .$key envelope: $(cat "$WORK/$cmd.out")"
  if [ "$cmd" != "parent-systems" ]; then
    jq -e ".$key | length > 0" "$WORK/$cmd.out" >/dev/null \
      || fail "$cmd list should return the seeded defaults: $(cat "$WORK/$cmd.out")"
    jq -e ".$key[0] | has(\"id\") and has(\"name\")" "$WORK/$cmd.out" >/dev/null \
      || fail "$cmd list entries should carry id and name: $(cat "$WORK/$cmd.out")"
  fi
  ok "$cmd list returned a .$key envelope"
done
```

- [ ] **Step 2: Bring up a seeded stack and run the smoke test**

```bash
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: every existing check still passes, plus five new `ok` lines. Under
docker-outside-of-docker, set `GRIMOIRE_LIBRARY`, `GRIMOIRE_DATA` and
`GRIMOIRE_ADDON_INDEX` to host paths per `docker/env.example` and reach the stack
at `http://host.docker.internal:9481`.

- [ ] **Step 3: Run it a second time to prove convergence**

```bash
bash docker/smoke-test.sh
```

Expected: identical result. A differing second run means the block writes
something and must be fixed, not accommodated.

- [ ] **Step 4: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: smoke-test the vocabulary read commands"
```

---

### Task 6: Full pre-PR verification and the PR

**Files:** none modified unless a check fails.

- [ ] **Step 1: Run all four pre-PR checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report actual output; do not summarise a failure as a pass.

- [ ] **Step 2: Remove the shipped roadmap item**

MVP item 1 leaves the roadmap now that it ships. In `docs/roadmap.md`, delete the
whole numbered item **1. Vocabularies** and renumber items 2–4 to 1–3, fixing the
"block 3 lands" cross-reference in what is now item 2 to say "block 2". Leave the
**Vocabulary writes** block under "Then" in place — it has not shipped.

- [ ] **Step 3: Commit and push**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
git add docs/roadmap.md
git commit -m "docs: drop the shipped vocabularies roadmap item"
git push -u origin feat/lookup-commands
```

- [ ] **Step 4: Open the PR**

```bash
gh pr create --title "feat: add controlled-vocabulary read commands" --body "$(cat <<'BODY'
Adds `genres list`, `licenses list`, `parent-systems list`, `system-families list`
and `dice-materials list` — the five controlled-vocabulary reads, so the fields
`systems update` and `books update` already accept stop being set by guesswork.

Five top-level groups with a `list` verb each, the shape abs-cli settled for its
own `genres` / `tags` / `narrators`. One table-driven command file over one
service; no role tags, because all five routes are `Depends(get_current_user)`.

Verified against `hunterreadca/grimoire:1.6.0` and the `v1.6.0` source, and
recorded in `docs/grimoire-api-notes.md`:

- systems and books store the vocabulary **name**, not the `id`;
- no write path validates a value against a vocabulary — `apply_updates` is a
  blind `setattr`, so an unmatched string is stored as written;
- `parent-systems` ships empty, since `DEFAULT_PARENT_SYSTEMS` is `()`.

The first two are why `systems update` and `books update` gained a Notes line
pointing here. Roadmap MVP item 1 drops; **Vocabulary writes** is promoted to a
decided block under "Then".
BODY
)"
```

- [ ] **Step 5: Present the PR URL as a clickable link, then watch CI**

```bash
gh pr checks --watch
```

A PR is done at "all checks green", not at "PR open". Report the terminal result
without being asked.

---

## Self-Review

**Spec coverage.** Command shape → Task 2. Service → Task 1. Help text, including
all four per-vocabulary lines → Task 2. Edits to shipped commands → Task 3.
`grimoire-api-notes.md` → Task 3. Tests, all three groups → Tasks 1, 2, 3.
Smoke test → Task 5. All four documentation items → Tasks 3, 4 and 6 (the
roadmap removal is deferred to Task 6 because the item leaves when it ships).
Out-of-scope items are constraints in Global Constraints, not tasks.

**Type consistency.** `LookupsService.ListAsync(string)` and `RequestFor(string)`
are named identically in Tasks 1 and 2. `LookupCommands.Create()` returns
`IEnumerable<Command>` in both its definition (Task 2 Step 3) and its consumers
(Task 2 Steps 1 and 4). The five vocabulary strings are the same list in the
service switch, the command table, the test theory data and the smoke-test loop.
