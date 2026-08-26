# `systems book-folders` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring back `systems book-folders list|set` and add `delete`, now that the
server's folder-depth defect is fixed.

**Architecture:** Three subcommands under a `book-folders` group on `systems`,
each mapping to one verb on `/api/systems/{id}/book-folders`. Responses are the
server's bytes, unread. `set` validates its body against the generated
`BookFolderUpdate`; `delete` sends the folder path as a query parameter.

**Tech Stack:** C# / .NET 10, System.CommandLine 2.0.7, Kiota-generated request
builders, NLog, xUnit, Native AOT publish.

**Spec:** [docs/specs/2026-08-26-book-folders-design.md](../specs/2026-08-26-book-folders-design.md)

**Already done, do not redo:** the fixture task. `docker/seed.sh` gained
`Das Schwarze Auge/5 DE/core/errata/DSA5 Errata.pdf` and `EXPECTED_BOOKS` went
17 → 18 (commit `40bdd1a`). The stack is seeded with it.

## Global Constraints

- **Never hand-edit `src/GrimoireCli/Generated/` or any `*.g.cs`.** Regenerate.
- **Never edit `CHANGELOG.md`** (release-process owned) or
  `docs/grimoire-api-coverage.md` by hand (generated from
  `tools/generate-api-coverage.py`).
- **`docs/roadmap.md` records intended work only** — never a status note. Item 3
  is *removed* by this work, not annotated.
- **Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.**
  CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** in method bodies: none between consecutive
  `AddCommand`/`AddOption` calls, none before a `return` that follows setup, none
  between consecutive variable declarations of the same kind.
- **Comments and help text say what the code does or why it must be this way** —
  never what was deliberately left out. State requirements positively.
- **Help text is the primary interface for the AI agents consuming this CLI and
  every word costs tokens.** Terse, one-liners over prose, and never restate what
  a flag description or a response sample already shows.
- **There is no `--token` flag and no `GRIMOIRE_TOKEN`.** Commands take `--server`
  alone. The cut code this plan references predates that removal.
- **Anything that writes goes to the local stack** (`http://host.docker.internal:9481`),
  never a live instance.
- **Commit per task**, Conventional Commits (`type: subject`, imperative,
  lowercase, no period). **No `Co-Authored-By` and no tool-attribution lines.**

## Verified server facts these tasks rely on

Measured against `hunterreadca/grimoire:nightly` commit
`7f5937071f51dfc65bc09f5e5e49d33c431f0a5d`. Do not re-derive; do not "correct"
code that matches these.

- `GET` → `{"folders":[{"path","tags"}]}`; no role beyond a non-guest account.
- `PATCH` body `{"path","tags"}` → the same shape; needs `gm or admin`.
- `DELETE` takes `path` as a **query parameter**, returns `{"status":"deleted"}`,
  needs `gm or admin`, and answers `404 {"detail":"Book folder not found"}` for a
  path with no row. **It is not idempotent.**
- The path form is `{system_id}/{category}/{subfolder…}` and **must belong to the
  system in the URL** — otherwise `400 {"detail":"path must be
  '{system_id}/{category}/{subfolder...}' for this system"}`. The cut code's help
  text claims the opposite; that claim was true of 1.5.6 and is now false.
- `PATCH` **replaces** the tag list. `{"tags":[]}` clears it **but keeps the
  row** — clearing is not deleting.
- `PATCH` echoes internal tag keys (`"Errata Fixture"` → `"errata fixture"`);
  `GET` returns display casing. A round trip does not match byte-for-byte.
- A row exists only once tagged. `core/errata` exists on disk with a book in it
  and `GET` returned `{"folders":[]}` until a `PATCH` created the row.
- Inherited tags never reach the book: `GET /api/books/{id}` reported
  `"tags": []` while the folder was tagged.
- The fixture's nested book is `DSA5 Errata` in `Das Schwarze Auge 5 DE`,
  category `core`, subfolder `errata`.

## Generated type names — exact

- `GrimoireCli.Generated.Models.BookFoldersResponse` — `GET` response
- `GrimoireCli.Generated.Models.BookFolderUpdate` — `PATCH` request
- `GrimoireCli.Generated.Models.BookFolderOut` — `PATCH` response
- `GrimoireCli.Generated.Models.Backend__routers__systems___schemas__StatusResponse`
  — `DELETE` response. The double underscores are real; it already has a sample
  in `JsonExamples.g.cs`.

Builders: `client.Api.Api.Systems[id].BookFolders.ToGetRequestInformation()`,
`.ToPatchRequestInformation(new Generated.Models.BookFolderUpdate())`,
`.ToDeleteRequestInformation(c => c.QueryParameters.Path = path)`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/GrimoireCli/Commands/BookFolderCommands.cs` | **new** — the three subcommands |
| `src/GrimoireCli/Services/SystemsService.cs` | three methods returning raw bodies |
| `src/GrimoireCli/Commands/SystemsCommand.cs` | wire the group in |
| `tests/GrimoireCli.Tests/Commands/BookFolderCommandTests.cs` | **new** — parse-level coverage |
| `docker/smoke-test.sh` | the live round trip |
| `docs/grimoire-api-notes.md` | the re-verified Book folders section |
| `README.md`, `tools/generate-api-coverage.py`, `docs/roadmap.md` | surface docs |

---

## Task 1: The three commands

**Files:**
- Create: `src/GrimoireCli/Commands/BookFolderCommands.cs`
- Modify: `src/GrimoireCli/Services/SystemsService.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs:22`
- Test: `tests/GrimoireCli.Tests/Commands/BookFolderCommandTests.cs` (create)

**Interfaces:**
- Consumes: `CommandHelper.BuildClient(string? serverOverride = null)`;
  `JsonBodyInput.Read`, `.Validate`, `.RequireExactlyOneSource`;
  `ConsoleOutput.WriteRawJson(string)`; `command.AddRoleRequired`,
  `.AddHelpSection`, `.AddExamples`, `.AddRequestShape<T>`,
  `.AddResponseExample<T>`.
- Produces: `BookFolderCommands.Create()` returning the `book-folders` `Command`;
  `SystemsService.BookFoldersAsync(string id)`,
  `.SetBookFolderAsync(string id, string rawBody)`,
  `.DeleteBookFolderAsync(string id, string path)`, all
  `Task<string>`.

**Read `src/GrimoireCli/Commands/CoverCommands.cs` first.** It is the closest
model: a subcommand group under `systems`, one file, three verbs, mixed roles.
Follow its structure. For the `set` body handling, follow
`SystemsCommand.cs:143-180` (`systems update`).

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/BookFolderCommandTests.cs`. These are
parse-level: they build the root command and assert on `Parse(...).Errors`, so
they need no server. Check how `tests/GrimoireCli.Tests/Commands/MeCommandTests.cs`
builds its root command and copy that construction exactly.

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BookFolderCommandTests
{
    // Matches how the other command tests build a root — see MeCommandTests.
    private static RootCommand Root() => new() { SystemsCommand.Create() };

    [Fact]
    public void ListAcceptsAnId()
        => Assert.Empty(Root().Parse("systems book-folders list --id sys-1").Errors);

    [Fact]
    public void ListRequiresAnId()
        => Assert.NotEmpty(Root().Parse("systems book-folders list").Errors);

    [Fact]
    public void SetAcceptsAnIdAndStdin()
        => Assert.Empty(Root().Parse("systems book-folders set --id sys-1 --stdin").Errors);

    [Fact]
    public void SetAcceptsAnIdAndInput()
        => Assert.Empty(Root().Parse("systems book-folders set --id sys-1 --input body.json").Errors);

    // Exactly one body source: RequireExactlyOneSource rejects both and neither.
    [Fact]
    public void SetRejectsBothBodySources()
        => Assert.NotEmpty(Root().Parse("systems book-folders set --id sys-1 --stdin --input body.json").Errors);

    [Fact]
    public void SetRejectsNoBodySource()
        => Assert.NotEmpty(Root().Parse("systems book-folders set --id sys-1").Errors);

    [Fact]
    public void DeleteAcceptsAnIdAndPath()
        => Assert.Empty(Root().Parse("systems book-folders delete --id sys-1 --path sys-1/core/errata").Errors);

    // The path is the only thing that identifies the row, so it cannot default.
    [Fact]
    public void DeleteRequiresAPath()
        => Assert.NotEmpty(Root().Parse("systems book-folders delete --id sys-1").Errors);

    [Fact]
    public void DeleteRequiresAnId()
        => Assert.NotEmpty(Root().Parse("systems book-folders delete --path sys-1/core/errata").Errors);

    // There is no --token tier anywhere in this CLI any more.
    [Fact]
    public void NoTokenOverride()
        => Assert.NotEmpty(Root().Parse("systems book-folders list --id sys-1 --token t").Errors);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~BookFolderCommandTests"
```

Expected: every test fails — `systems` has no `book-folders` subcommand yet, so
the parse produces errors (and the two `Assert.NotEmpty` cases pass for the wrong
reason; that is fine, they are pinned by the end of the task).

- [ ] **Step 3: Add the three service methods**

Append to `src/GrimoireCli/Services/SystemsService.cs`, following the raw-body
pattern its neighbours already use (`UpdateAsync` at `:59` is the model):

```csharp
    /// <summary>
    /// GET /api/systems/{id}/book-folders. Lists folders that have been tagged;
    /// a folder on disk with no tags has no record and does not appear. Tags come
    /// back in display casing.
    /// </summary>
    public async Task<string> BookFoldersAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }

    /// <summary>
    /// PATCH /api/systems/{id}/book-folders. Replaces the folder's tags, creating
    /// the record if the path has none. The validated raw body reaches the server
    /// byte-for-byte, as the update commands do. Tags echo back as internal keys.
    /// </summary>
    public async Task<string> SetBookFolderAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToPatchRequestInformation(
            new Generated.Models.BookFolderUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// DELETE /api/systems/{id}/book-folders. The path travels as a query
    /// parameter rather than a body. Removes the record; a path with no record is
    /// a 404.
    /// </summary>
    public async Task<string> DeleteBookFolderAsync(string id, string path)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToDeleteRequestInformation(
            c => c.QueryParameters.Path = path);
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No book folder at that path. List them with: grimoire-cli systems book-folders list --id <id>");
    }
```

`Encoding` and `MemoryStream` are already used in this file; add no usings unless
the build asks for them.

- [ ] **Step 4: Write the command file**

Create `src/GrimoireCli/Commands/BookFolderCommands.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class BookFolderCommands
{
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

    public static Command Create()
    {
        var command = new Command("book-folders", "Subcategory folders and their tags");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateSetCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        return command;
    }

    private static Command CreateListCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("list", "List a system's tagged subcategory folders")
        {
            idOption, serverOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Folders that have been tagged, not the folders on disk — a record is",
            "created only by book-folders set. Nothing enumerates the tree.",
            "",
            "A folder's tags apply to every book at or below its path and never",
            "appear in a book's own tags, so books get will not show them.",
            "",
            "Tags read back in display casing; set echoes internal keys.");
        command.AddExamples("grimoire-cli systems book-folders list --id <system-id>");
        command.AddResponseExample<Generated.Models.BookFoldersResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SystemsService(client);
            var result = await service.BookFoldersAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateSetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var inputOption = new Option<string?>("--input") { Description = "Read the body from this file" };
        var stdinOption = new Option<bool>("--stdin") { Description = "Read the body from stdin" };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("set", "Set a subcategory folder's tags")
        {
            idOption, inputOption, stdinOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        JsonBodyInput.RequireExactlyOneSource(command, inputOption, stdinOption);
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Replaces the folder's tag list; batch-tag adds. An empty tags array",
            "clears the folder but keeps it — book-folders delete removes it.",
            "",
            "path is {system-id}/{category}/{subfolder}, where subfolder is the",
            "segments of a book's relative_path between the category directory and",
            "the filename. Its first segment must be the same system as --id.",
            "",
            "Creates the folder record if the path has none.");
        command.AddExamples(
            "grimoire-cli systems book-folders set --id <system-id> --input folder.json",
            "echo '{\"path\":\"<id>/core/errata\",\"tags\":[\"errata\"]}' | grimoire-cli systems book-folders set --id <system-id> --stdin");
        command.AddRequestShape<Generated.Models.BookFolderUpdate>();
        command.AddResponseExample<Generated.Models.BookFolderOut>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string body;
            try
            {
                body = JsonBodyInput.Read(parseResult.GetValue(inputOption), parseResult.GetValue(stdinOption));
                JsonBodyInput.Validate(body, Generated.Models.BookFolderUpdate.CreateFromDiscriminatorValue,
                    "the folder is addressed by path");
            }
            catch (BodyInputException ex)
            {
                _logger.Error(ex.Message);
                return 1;
            }
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SystemsService(client);
            var result = await service.SetBookFolderAsync(parseResult.GetValue(idOption)!, body);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var pathOption = new Option<string>("--path") { Description = "Folder path to remove", Required = true };
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("delete", "Remove a subcategory folder's record")
        {
            idOption, pathOption, serverOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Removes the record, so the books below the path stop inheriting its",
            "tags. Clearing the tags with book-folders set leaves the record.",
            "",
            "A path with no record is a 404, so this is not repeatable.");
        command.AddExamples(
            "grimoire-cli systems book-folders delete --id <system-id> --path <system-id>/core/errata");
        command.AddResponseExample<Generated.Models.Backend__routers__systems___schemas__StatusResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var service = new SystemsService(client);
            var result = await service.DeleteBookFolderAsync(
                parseResult.GetValue(idOption)!, parseResult.GetValue(pathOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

If `AddRequestShape`, `AddResponseExample` or `HelpSectionPosition` are spelled
differently, use the spelling `SystemsCommand.cs` and `CoverCommands.cs` actually
use. Do **not** change those helpers to fit this file.

- [ ] **Step 5: Wire the group in**

In `src/GrimoireCli/Commands/SystemsCommand.cs`, beside the existing
`command.Subcommands.Add(CoverCommands.Create());`:

```csharp
        command.Subcommands.Add(BookFolderCommands.Create());
```

- [ ] **Step 6: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green. Baseline before this task is **349 passing**; this adds 12
(ten here plus the two role cases in Step 6a). The whole suite must pass, not just
the filter — the help-rendering tests walk the command tree and will notice a
malformed one.

- [ ] **Step 6a: Extend the role-section coverage to the nested subcommands**

`tests/GrimoireCli.Tests/Commands/RoleSectionTests.cs` asserts that a write
command's `--help` carries a "Role required: gm or admin" section, but its
`[Theory]` takes a single subcommand name and parses
`["systems", subcommand, "--help"]`. `book-folders set` and `book-folders delete`
are two levels deep, so widen the theory to accept a path and split it:

```csharp
    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    [InlineData("book-folders set")]
    [InlineData("book-folders delete")]
    public void SystemsWriteCommandHasTheGmOrAdminRoleSection(string subcommand)
    {
        var root = new RootCommand { SystemsCommand.Create() };
        root.UseCustomHelpSections();
        var output = new StringWriter();
        root.Parse(["systems", .. subcommand.Split(' '), "--help"])
            .Invoke(new InvocationConfiguration { Output = output });
        Assert.Contains("Role required:", output.ToString());
        Assert.Contains("gm or admin", output.ToString());
    }
```

If the collection-expression spread does not compile on this target, build the
array explicitly — the point is the two new cases, not the syntax. Leave the
three existing cases exactly as they are.

`systems cover upload` / `cover delete` are also nested and also `gm or admin`,
and this widening would let them be covered too. **Do not add them** — that is a
pre-existing gap and not this feature's work.

- [ ] **Step 7: Exercise all three against the live stack**

The stack is up and seeded. Get the fixture system's id, then run the round trip:

```bash
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
DSA=$($CLI systems list --include-children | jq -r '.[] | select(.name == "Das Schwarze Auge 5 DE") | .id')
echo "{\"path\":\"$DSA/core/errata\",\"tags\":[\"errata\"]}" | $CLI systems book-folders set --id "$DSA" --stdin
$CLI systems book-folders list --id "$DSA"
$CLI systems book-folders delete --id "$DSA" --path "$DSA/core/errata"
$CLI systems book-folders list --id "$DSA"
```

Expected in order: the echoed `{"path":…,"tags":["errata"]}`; a `folders` array
containing it; `{"status":"deleted"}`; `{"folders":[]}`. Report the actual output.
Also confirm the error paths read well:

```bash
$CLI systems book-folders delete --id "$DSA" --path "$DSA/core/nope"; echo "rc=$?"
echo "{\"path\":\"wrong/core/x\",\"tags\":[]}" | $CLI systems book-folders set --id "$DSA" --stdin; echo "rc=$?"
```

Both should exit 2 with a readable message and no stack trace — the first a
not-found, the second the server's 400 about the path not belonging to the
system.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli tests/GrimoireCli.Tests/Commands/BookFolderCommandTests.cs
git commit -m "feat: add systems book-folders list, set and delete"
```

---

## Task 2: Live round trip in the smoke test

**Files:**
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: the three commands from Task 1.
- Produces: five new `ok` assertions, taking the count from 84 to 89.

**Read the whole of `docker/smoke-test.sh` first.** Conventions matter: the
`fail`/`ok` helpers, `$WORK` for scratch files, `$CONFIG`, `$SERVER`, and
`set -euo pipefail`. The nearest models are the `systems cover` block and the
`--- book folders ---` block that was cut in `5c566b4` (recover it with
`git show 5c566b4 -- docker/smoke-test.sh`), which is a useful shape but asserts
less than this task requires.

- [ ] **Step 1: Add the block**

Place it after the `systems batch-tag` assertions and before the books section,
so it sits with the other `systems` write coverage. Derive the system id rather
than hardcoding it.

```bash
# --- book folders ------------------------------------------------------------
# Fixed path and fixed tags, so a second run converges. The fixture's only book
# below a category directory lives here, which is what makes the inheritance
# assertion below possible at all.
DSA=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Das Schwarze Auge 5 DE") | .id')
[ -n "$DSA" ] || fail "no Das Schwarze Auge 5 DE fixture for book folders"
FOLDER_PATH="$DSA/core/errata"

SET_JSON=$(printf '{"path":"%s","tags":["errata-smoke"]}' "$FOLDER_PATH" \
  | "$CLI" systems book-folders set --id "$DSA" --stdin 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders set exited non-zero"; }
[ "$(echo "$SET_JSON" | jq -r .path)" = "$FOLDER_PATH" ] \
  || fail "set should echo the path it wrote: $SET_JSON"
ok "systems book-folders set writes a folder's tags"

FOLDERS_JSON=$("$CLI" systems book-folders list --id "$DSA" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero"; }
echo "$FOLDERS_JSON" | jq -e --arg p "$FOLDER_PATH" '.folders[] | select(.path == $p)' >/dev/null \
  || fail "the folder just written should be listed: $FOLDERS_JSON"
ok "systems book-folders list shows the written folder"

# The point of the feature: a book below the path inherits the tag. This is the
# round trip that upstream #357 broke — the server derived the folder's depth
# differently for a container child, and Das Schwarze Auge 5 DE is one.
TAG_ITEMS=$(curl -sf "$SERVER/api/tags/errata-smoke/items" \
  -H "Authorization: Bearer $(jq -r .accessToken "$CONFIG")") \
  || fail "could not read the tag's items"
echo "$TAG_ITEMS" | jq -e '.folders[] | select(.path == "errata") | .items[] | select(.title == "DSA5 Errata")' >/dev/null \
  || fail "the folder tag should reach the book below it: $TAG_ITEMS"
ok "a folder tag reaches the book below its path"

DEL_JSON=$("$CLI" systems book-folders delete --id "$DSA" --path "$FOLDER_PATH" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders delete exited non-zero"; }
[ "$(echo "$DEL_JSON" | jq -r .status)" = "deleted" ] \
  || fail "delete should report the deletion: $DEL_JSON"
ok "systems book-folders delete removes the folder"

FOLDERS_JSON=$("$CLI" systems book-folders list --id "$DSA" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero after delete"; }
[ "$(echo "$FOLDERS_JSON" | jq '.folders | length')" -eq 0 ] \
  || fail "the folder should be gone after delete: $FOLDERS_JSON"
ok "the deleted folder is no longer listed"
```

`$LIST_JSON` must hold a listing that includes container children. If the nearest
preceding `syslist` call did not pass `--include-children`, add an explicit
`syslist --include-children` immediately above this block rather than relying on
whatever ran last — `Das Schwarze Auge 5 DE` is a container child and is hidden
by default.

- [ ] **Step 2: Run the smoke test**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed`, with the five new `ok` lines. The stack must
be up and seeded; see CLAUDE.md's "Pre-PR verification" if it is not.

- [ ] **Step 3: Run it a second time**

```bash
bash docker/smoke-test.sh
```

Expected: identical. This script must converge on a re-run, which is why the
block deletes what it created — `DELETE` 404s on a path with no record, so a
block that only created would fail the second time.

- [ ] **Step 4: Check the assertion count went up**

```bash
grep -c '^\s*ok "' docker/smoke-test.sh
```

Expected: **89** (84 before). Use this grep, not `grep -c '^ok '` — assertions
nested inside an `if` are indented and the anchored form misses them.

- [ ] **Step 5: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the book-folders round trip end to end"
```

---

## Task 3: Documentation

**Files:**
- Modify: `docs/grimoire-api-notes.md` (the "Book folders" section)
- Modify: `README.md` (Commands table, after the `systems cover` rows)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`)
- Modify: `docs/roadmap.md` (remove item 3)
- Generated: `docs/grimoire-api-coverage.md` (never hand-edited)

- [ ] **Step 1: Re-verify the Book folders section against the running stack**

The section is labelled *Verified against v1.5.6* and two of its bullets are now
wrong. Every bullet gets re-checked against the stack before the section is
rewritten — the point is that the section carries measurements, not inherited
claims. The commands from Task 1 make this quick:

```bash
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
DSA=$($CLI systems list --include-children | jq -r '.[] | select(.name == "Das Schwarze Auge 5 DE") | .id')
TOKEN=$(jq -r .accessToken "$HOME/.grimoire-cli/config.json")
B=http://host.docker.internal:9481

$CLI systems book-folders list --id "$DSA"                     # untagged folder on disk is absent
echo "{\"path\":\"$DSA/core/errata\",\"tags\":[\"Errata Fixture\"]}" | $CLI systems book-folders set --id "$DSA" --stdin
$CLI systems book-folders list --id "$DSA"                     # display casing vs the echo above
echo "{\"path\":\"$DSA/core/errata\",\"tags\":[\"second\"]}" | $CLI systems book-folders set --id "$DSA" --stdin
echo "{\"path\":\"$DSA/core/errata\",\"tags\":[]}" | $CLI systems book-folders set --id "$DSA" --stdin
$CLI systems book-folders list --id "$DSA"                     # cleared but still present
curl -s "$B/api/books/$($CLI systems get --id "$DSA" | jq -r '.books[] | select(.title=="DSA5 Errata") | .id')" \
  -H "Authorization: Bearer $TOKEN" | jq '{title, tags}'       # inherited tags absent from the book
$CLI systems book-folders delete --id "$DSA" --path "$DSA/core/errata"
```

The two bullets that must change:

- The depth-mismatch bullet records upstream #357 as live behaviour. It is fixed:
  `system_category_depth` walks the whole container chain (`2 + <ancestor
  count>`) instead of hardcoding `parts[3:-1]`, so nested containers resolve too,
  and `GET /api/tags/{internal}/items` returns the book under a one-segment
  folder path for a container child.
- The bullet claiming the URL's `system_id` is ignored by the write, so a caller
  can write another system's folder through any system's URL, is false: the
  server answers `400 {"detail":"path must be
  '{system_id}/{category}/{subfolder...}' for this system"}`.

Also drop the sentence saying no CLI command exposes the endpoint, relabel the
section for the build it was measured against
(`hunterreadca/grimoire:nightly`, commit
`7f5937071f51dfc65bc09f5e5e49d33c431f0a5d`), and add the two behaviours the old
section did not record: an empty `tags` clears the folder but keeps the record,
and `DELETE` removes it and 404s on a path with none.

Keep the file's existing voice: short declaratives, a source citation or a
measurement per claim, no hedging.

- [ ] **Step 2: Add the README rows**

In the Commands table, immediately after the three `systems cover` rows:

```markdown
| `systems book-folders list --id <id>` | List a system's tagged subcategory folders |
| `systems book-folders set --id <id> {--input <file> \| --stdin}` | Replace a subcategory folder's tags (gm or admin) |
| `systems book-folders delete --id <id> --path <path>` | Remove a subcategory folder's record (gm or admin) |
```

Note the escaped pipe inside the braces — the surrounding rows do the same,
because an unescaped `|` would break the table.

- [ ] **Step 3: Mark the three endpoints implemented**

In `tools/generate-api-coverage.py`, in `IMPLEMENTED`, beside the other
`/api/systems/{system_id}` entries:

```python
    "GET /api/systems/{system_id}/book-folders": "`systems book-folders list` ✅",
    "PATCH /api/systems/{system_id}/book-folders": "`systems book-folders set` ✅",
    "DELETE /api/systems/{system_id}/book-folders": "`systems book-folders delete` ✅",
```

- [ ] **Step 4: Regenerate the coverage table**

```bash
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly three rows change, plus the covered count in the summary. The
generator reads the router source out of the running container, so the stack must
be up. If the diff is larger than that, the spec has moved under us — report it
rather than committing it silently.

- [ ] **Step 5: Remove roadmap item 3**

Delete the whole `3. **systems book-folders list|set**, once …#357… is fixed`
item from `docs/roadmap.md` and renumber the items after it. The roadmap lists
intended work only, so a delivered item is removed, never annotated as done.

- [ ] **Step 6: Full pre-PR verification**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass: 361 tests and `smoke: all checks passed`.

- [ ] **Step 7: Commit**

```bash
git add docs README.md tools/generate-api-coverage.py
git commit -m "docs: record book folders as measured against the 1.6.0 rc"
```

---

## Self-review notes

Spec sections mapped to tasks: §1 (re-implementation, not revert) → Task 1's
framing and the Global Constraints note about `--token`; §2 (command surface) →
Task 1; §3 (help text) → Task 1 Step 4; §4 (fixture) → **already committed as
`40bdd1a`**; §5 (smoke coverage) → Task 2; Testing → Task 1 Step 1; Documentation
→ Task 3. The "out of scope" item needs no task.

The verified-behaviour list in the spec is reproduced in this plan's own
"Verified server facts" section so a task implementer never has to open the spec
to know what the server does.
