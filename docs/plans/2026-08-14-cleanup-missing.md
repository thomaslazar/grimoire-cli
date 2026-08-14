# `library cleanup-missing` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One command, `library cleanup-missing`, over `POST /api/maintenance/cleanup-missing` — the endpoint the add-on branch deferred and the metadata branch never carried.

**Architecture:** A fourth subcommand on the existing `library` group, one method on the existing `LibraryService`, and two small DTOs. No new files beyond the DTOs and one test file. The command is a pure pass-through: the warning it carries lives entirely in help text, with no prompt gate.

**Tech Stack:** C# / .NET 8, System.CommandLine, Kiota-generated request builders (`src/GrimoireCli/Generated/Api/Maintenance/CleanupMissing/` already exists), xUnit, bash smoke test.

**Design doc:** [docs/specs/2026-08-14-cleanup-missing-design.md](../specs/2026-08-14-cleanup-missing-design.md)

## Global Constraints

- **Branch:** `feat/cleanup-missing`, already created. Never commit to `main`.
- **Conventional Commits**, imperative, lowercase, no period, ≤72 chars. No `Co-Authored-By`, no tool attribution.
- **Run `dotnet format GrimoireCli.sln` after any C# edit.** CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** in method bodies — none between consecutive option declarations or `Subcommands.Add` calls, none before a `return` following setup calls.
- **Role tag and hint agree:** `AddRoleRequired("admin")` ↔ `permissionHint: "the admin role"`, matching `require_admin`.
- **Thin pass-through:** no prompt, no confirmation flag, no reading the response to derive a warning, no pre-fetching to decide anything.
- **Exit 0 on HTTP 200 regardless of counts.** Exit 2 on the 409 and every other HTTP error, carrying the server's own message.
- **The smoke test stays idempotent.** No assertion whose expected value depends on the fixture's prior state.
- **Do not touch `CHANGELOG.md`** or `docs/roadmap.md`.

---

### Task 1: Spec, plan, and the two DTOs

**Files:**
- Commit: `docs/specs/2026-08-14-cleanup-missing-design.md`, `docs/plans/2026-08-14-cleanup-missing.md`
- Create: `src/GrimoireCli/Models/CleanupCounts.cs`, `src/GrimoireCli/Models/CleanupResult.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`
- Regenerate: `src/GrimoireCli/Commands/ResponseExamples.g.cs`
- Test: `tests/GrimoireCli.Tests/Models/CleanupDtoTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `GrimoireCli.Models.CleanupResult` (`Removed`), `CleanupCounts` (`Books`, `Maps`, `Tokens`, `Audio`, `Systems`), registered as `AppJsonContext.Default.CleanupResult` / `.CleanupCounts`

- [ ] **Step 1: Commit the design documents**

```bash
git add docs/specs/2026-08-14-cleanup-missing-design.md docs/plans/2026-08-14-cleanup-missing.md
git commit -m "docs: design library cleanup-missing"
```

- [ ] **Step 2: Write the failing test**

Create `tests/GrimoireCli.Tests/Models/CleanupDtoTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class CleanupDtoTests
{
    // Shape from routers/maintenance/_helpers.py:113 — five fixed keys, one per
    // resource the sweep covers.
    [Fact]
    public void CleanupResultCarriesEveryCount()
    {
        const string json = """
        {"removed": {"books": 41, "maps": 2, "tokens": 0, "audio": 1, "systems": 3}}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.CleanupResult)!;
        Assert.Equal(41, result.Removed!.Books);
        Assert.Equal(2, result.Removed.Maps);
        Assert.Equal(0, result.Removed.Tokens);
        Assert.Equal(1, result.Removed.Audio);
        // Nothing in the request names a system: systems are pruned as a
        // consequence of the book sweep, so this is the count a caller is least
        // likely to expect and the one most worth pinning.
        Assert.Equal(3, result.Removed.Systems);
    }

    [Fact]
    public void AllZeroIsTheHealthyResponse()
    {
        const string json = """
        {"removed": {"books": 0, "maps": 0, "tokens": 0, "audio": 0, "systems": 0}}
        """;
        var result = JsonSerializer.Deserialize(json, AppJsonContext.Default.CleanupResult)!;
        Assert.Equal(0, result.Removed!.Books);
        Assert.Equal(0, result.Removed.Systems);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~CleanupDtoTests`
Expected: build failure — `AppJsonContext.Default.CleanupResult` does not exist.

- [ ] **Step 4: Write the DTOs**

Create `src/GrimoireCli/Models/CleanupCounts.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// What one cleanup removed, per resource (routers/maintenance/_helpers.py:113).
/// systems counts folders pruned for having no books left, not systems asked for.
/// </summary>
public class CleanupCounts
{
    [JsonPropertyName("books")]
    public int Books { get; set; }

    [JsonPropertyName("maps")]
    public int Maps { get; set; }

    [JsonPropertyName("tokens")]
    public int Tokens { get; set; }

    [JsonPropertyName("audio")]
    public int Audio { get; set; }

    [JsonPropertyName("systems")]
    public int Systems { get; set; }
}
```

Create `src/GrimoireCli/Models/CleanupResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>POST /api/maintenance/cleanup-missing response.</summary>
public class CleanupResult
{
    [JsonPropertyName("removed")]
    public CleanupCounts? Removed { get; set; }
}
```

- [ ] **Step 5: Register both on `AppJsonContext`**

In `src/GrimoireCli/Models/JsonContext.cs`, beneath the existing registrations:

```csharp
[JsonSerializable(typeof(CleanupCounts))]
[JsonSerializable(typeof(CleanupResult))]
```

- [ ] **Step 6: Run the test**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~CleanupDtoTests`
Expected: PASS, 2 tests.

- [ ] **Step 7: Regenerate the response examples**

Run: `dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs`
Expected: exit 0; `git diff` shows two new entries. `ResponseExamplesDriftTest` fails until this runs.

- [ ] **Step 8: Format, build, full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: all green.

- [ ] **Step 9: Commit**

```bash
git add src/GrimoireCli/Models/ src/GrimoireCli/Commands/ResponseExamples.g.cs \
        tests/GrimoireCli.Tests/Models/CleanupDtoTests.cs
git commit -m "feat: add cleanup-missing response models"
```

---

### Task 2: Service method and command

**Files:**
- Modify: `src/GrimoireCli/Services/LibraryService.cs`, `src/GrimoireCli/Commands/LibraryCommand.cs:12-19`
- Test: `tests/GrimoireCli.Tests/Commands/LibraryCommandTests.cs`

**Interfaces:**
- Consumes: `CleanupResult` from Task 1; `GrimoireApiClient.SendAsync(info, jsonTypeInfo, permissionHint:, notFoundHint:)`
- Produces: `LibraryService.CleanupMissingAsync()` returning `Task<CleanupResult>`; a `cleanup-missing` subcommand on the `library` group

- [ ] **Step 1: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Commands/LibraryCommandTests.cs`, following that file's existing style:

```csharp
    [Fact]
    public void CleanupMissingIsAdminOnly()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: false);
        Assert.Contains("admin", output);
    }

    // The two facts this command exists to warn about. A help block that loses
    // either has lost the point of the command.
    [Fact]
    public void CleanupMissingWarnsAboutBookmarksAndAbsentMounts()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: false);
        Assert.Contains("bookmarks", output);
        Assert.Contains("absent rather than hung", output);
    }

    [Fact]
    public void CleanupMissingSaysItLeavesFilesAlone()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: false);
        Assert.Contains("Never touches", output);
    }

    [Fact]
    public void CleanupMissingNamesTheScanConflict()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: false);
        Assert.Contains("409", output);
    }

    [Fact]
    public void CleanupMissingRendersItsCounts()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: true);
        Assert.Contains("\"removed\":", output);
        Assert.Contains("\"systems\":", output);
    }

    // No prompt, no --yes: the decision recorded in the design doc is that this
    // CLI's callers are agents, so the warning is help text and nothing else.
    [Fact]
    public void CleanupMissingTakesNoConfirmationFlag()
    {
        var output = RenderHelp(["library", "cleanup-missing"], full: true);
        Assert.DoesNotContain("--yes", output);
        Assert.DoesNotContain("--force", output);
    }
```

If `LibraryCommandTests.cs` has no `RenderHelp` helper, copy the one-line form the other command test files use:
`private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(LibraryCommand.Create(), path, full);`

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~LibraryCommandTests`
Expected: the six new tests fail — there is no `cleanup-missing` subcommand.

- [ ] **Step 3: Add the service method**

In `src/GrimoireCli/Services/LibraryService.cs`, after `CancelScanAsync`:

```csharp
    /// <summary>
    /// POST /api/maintenance/cleanup-missing. Deletes DB rows whose files are
    /// gone, committing per row, so a failure part-way leaves earlier removals
    /// applied. 409 while a scan runs; the server's message names that state, so
    /// no hint replaces it.
    /// </summary>
    public async Task<CleanupResult> CleanupMissingAsync()
    {
        var info = _client.Api.Api.Maintenance.CleanupMissing.ToPostRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.CleanupResult, permissionHint: "the admin role");
    }
```

- [ ] **Step 4: Add the command**

In `src/GrimoireCli/Commands/LibraryCommand.cs`, add to `Create()` after the `cancel-scan` line:

```csharp
        command.Subcommands.Add(CreateCleanupMissingCommand());
```

And the builder, after `CreateCancelScanCommand()`:

```csharp
    private static Command CreateCleanupMissingCommand()
    {
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var tokenOption = new Option<string?>("--token") { Description = "Token override; not stored" };
        var command = new Command("cleanup-missing", "Remove DB entries for files no longer on disk")
        {
            serverOption, tokenOption
        };
        command.AddRoleRequired("admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Deletes DB rows for files no longer on disk, each book's search index",
            "and bookmarks with it, then prunes systems left with no books. Never",
            "touches files.",
            "",
            "Normally a no-op. Run it after restructuring the library on disk.",
            "",
            "A library directory that is absent rather than hung reads as wholly",
            "deleted, and a rescan does not restore hand-entered metadata or",
            "bookmarks. A hung mount is safe — the server treats a timed-out path",
            "as present.",
            "",
            "409 while a scan is running; commits per row, so a failure part-way",
            "leaves earlier removals applied.");
        command.AddExamples("grimoire-cli library cleanup-missing");
        command.AddResponseExample<CleanupResult>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(
                serverOverride: parseResult.GetValue(serverOption),
                tokenOverride: parseResult.GetValue(tokenOption));
            var service = new LibraryService(client);
            var result = await service.CleanupMissingAsync();
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.CleanupResult);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter FullyQualifiedName~LibraryCommandTests`
Expected: PASS.

- [ ] **Step 6: Format, build, full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: all green. `RoleSectionTests` may enumerate role-tagged commands — if it fails, a fourth admin command is a deliberate addition, so update the expectation after reading what the assertion protects.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Services/LibraryService.cs src/GrimoireCli/Commands/LibraryCommand.cs \
        tests/GrimoireCli.Tests/Commands/LibraryCommandTests.cs
git commit -m "feat: add library cleanup-missing command"
```

---

### Task 3: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh` — a new block between the `library cancel-scan` assertion that ends the scan section and the `# --- addons ---` header

**Interfaces:**
- Consumes: the command from Task 2
- Produces: live coverage of the endpoint

**Preconditions:** a running, seeded stack (`docker compose -f docker/docker-compose.yml up -d --wait`, `bash docker/seed.sh`) and `dotnet build GrimoireCli.sln`, since the script's default `CLI` is `src/GrimoireCli/bin/Debug/net10.0/grimoire-cli`.

- [ ] **Step 1: Add the block**

Insert after `ok "library cancel-scan exits 0 and reports not_running"` and before the `# --- addons ---` header:

```bash
# --- cleanup-missing ------------------------------------------------------
# Placed after the scan section so nothing is running (the endpoint answers 409
# while a scan is) and after the EXPECTED_BOOKS assertions above, so a cleanup
# that removes stale is_missing rows cannot invalidate a count asserted earlier
# in the same run.
#
# The fixture library is fully present, so the honest assertion is the contract
# rather than the first call's numbers: whatever the first call removes, the
# second must find nothing left to remove. Asserting zero on the first call
# would be asserting this stack's history — a database-only reset leaves stale
# is_missing rows behind (see CLAUDE.md).
CLEANUP_JSON=$("$CLI" library cleanup-missing 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cleanup-missing exited non-zero"; }
for key in books maps tokens audio systems; do
  echo "$CLEANUP_JSON" | jq -e --arg k "$key" '.removed[$k] | type == "number"' >/dev/null \
    || fail "removed.$key should be a number: $CLEANUP_JSON"
done
ok "library cleanup-missing reports a count for every resource"

CLEANUP_JSON=$("$CLI" library cleanup-missing 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cleanup-missing exited non-zero on the second call"; }
echo "$CLEANUP_JSON" | jq -e '[.removed[]] | add == 0' >/dev/null \
  || fail "a second cleanup should find nothing left to remove: $CLEANUP_JSON"
ok "a second library cleanup-missing removes nothing"
```

- [ ] **Step 2: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```
Expected: both runs green.

- [ ] **Step 3: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover library cleanup-missing"
```

---

### Task 4: Docs and verification

**Files:**
- Modify: `README.md` (Commands table), `tools/generate-api-coverage.py` (`IMPLEMENTED`), `docs/grimoire-api-coverage.md` (regenerated), `docs/grimoire-api-notes.md`, `CLAUDE.md`
- Do not touch: `CHANGELOG.md`, `docs/roadmap.md`

- [ ] **Step 1: README row**

Beside the other `library` rows, in the table's own format:

```markdown
| `library cleanup-missing` | Remove DB entries for files no longer on disk (admin; deletes bookmarks too) |
```

- [ ] **Step 2: Coverage**

Add to `IMPLEMENTED` in `tools/generate-api-coverage.py`:

```python
    "POST /api/maintenance/cleanup-missing": "`library cleanup-missing` ✅",
```

Then `python3 tools/generate-api-coverage.py`. Expected: `maintenance` reads `1 / 2`.

- [ ] **Step 3: api-notes**

Add a `## Maintenance` section recording only verified behaviour, in that file's register: the `_path_exists` asymmetry (a hung path times out after 5s and is treated as present; an absent one is not), the per-row commit and what a part-way failure leaves behind, that a book's FTS rows and bookmarks go with it, that systems are pruned when the sweep empties them, and the 409 while a scan runs. Cite `backend/routers/maintenance/_helpers.py` line numbers as the file's existing entries do.

- [ ] **Step 4: Close the confirm-gate question in `CLAUDE.md`**

The "Deliberate deviations today" list carries:

> **No confirm-gated command.** abs-cli exempts `libraries delete` from thin pass-through with a type-the-name prompt. Nothing here is destructive enough to need one yet; the first delete command decides whether to adopt it.

Rewrite it to record the decision rather than the open question: `library cleanup-missing` is that command, it takes no prompt and no `--yes`, and the reasons are that the callers are agents (a prompt is either boilerplate-bypassed or hangs them), that the operation is a near-no-op in normal use, and that the warning belongs in help text where an agent reads it. Keep it to the length of the neighbouring entries.

- [ ] **Step 5: All four pre-PR checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```
Expected: all four exit 0.

- [ ] **Step 6: Commit and push**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/grimoire-api-notes.md CLAUDE.md
git commit -m "docs: record library cleanup-missing"
git push -u origin feat/cleanup-missing
```

- [ ] **Step 7: Stop.** The PR is opened after a whole-branch review, not from this task.

## Notes for the implementer

- The Kiota builder already exists: `_client.Api.Api.Maintenance.CleanupMissing.ToPostRequestInformation()`. Nothing regenerates the API client.
- `HelpRenderer` (`tests/GrimoireCli.Tests/Commands/HelpRenderer.cs`) renders a subcommand's help; `full: true` includes the response-shape block.
- The endpoint takes no body, so no request shape is registered and `JsonBodyInput` is not involved.
