# tags writes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the `tags` command group with `create`, `rename`, `delete` and `merge`, the four gm-or-admin writes behind [#42](https://github.com/thomaslazar/grimoire-cli/issues/42).

**Architecture:** Four thin pass-throughs. `TagsService` gains four sends against the generated Kiota builders; `TagsCommand` gains four subcommands whose help text carries the server behaviour that would otherwise surprise a caller. No new types — `TagCreate`, `TagDisplayUpdate` and `TagMerge` are already generated.

**Tech Stack:** C# / .NET, System.CommandLine, Kiota-generated client, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-18-tags-writes-design.md](../specs/2026-09-18-tags-writes-design.md)

## Global Constraints

- Branch: `feat/tags-writes`, cut from `main`. Never commit to `main`.
- Conventional Commits, imperative, lowercase, no period, ~72 chars. No `Co-Authored-By:` and no "Generated with Claude Code" lines.
- The spec and this plan are committed **on the feature branch**, in Task 1's commit — they are held out of `main` until the branch exists.
- Every write command calls `command.AddRoleRequired("gm or admin")` immediately after construction, and its service send passes `permissionHint: "the gm or admin role"`. The two must agree.
- Run `dotnet format GrimoireCli.sln` after touching any C# file.
- Help text is terse: one-liners, no prose, nothing already visible from the flags or the rendered response shape.
- Never touch `CHANGELOG.md` (release-process owned) or `docs/roadmap.md` (intended work only).
- Writes go to the local Docker stack only, never the live instance.

---

### Task 1: The four service sends

**Files:**
- Modify: `src/GrimoireCli/Services/TagsService.cs`
- Test: `tests/GrimoireCli.Tests/Services/TagsServiceTests.cs`
- Commit alongside: `docs/specs/2026-09-18-tags-writes-design.md`, `docs/plans/2026-09-18-tags-writes.md`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint, string? notFoundHint)`, already used by the two reads in this file.
- Produces: `TagsService.CreateAsync(string value, string? display)`, `RenameAsync(string tag, string display)`, `MergeAsync(string tag, string into)`, `DeleteAsync(string tag)` — all `Task<string>`; and `internal static Generated.Models.TagCreate BuildCreateBody(string value, string? display)`. Task 2 calls exactly these names.

- [ ] **Step 1: Cut the branch**

```bash
git checkout -b feat/tags-writes
```

- [ ] **Step 2: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Services/TagsServiceTests.cs`, inside the existing class (it already has the `Client()` and `Uri()` helpers and a `using GrimoireCli.Services;` is **not** yet present — add it to the file's using block):

```csharp
    [Fact]
    public void EachWriteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Tags;
        var create = new Generated.Models.TagCreate { Value = "dungeon" };
        Assert.Equal("http://example.test/api/tags", Uri(api.ToPostRequestInformation(create)));
        Assert.Contains("/api/tags/dungeon", Uri(api["dungeon"].ToDeleteRequestInformation()));
        Assert.Contains("/api/tags/dungeon", Uri(api["dungeon"].ToPatchRequestInformation(
            new Generated.Models.TagDisplayUpdate { Display = "Dungeon" })));
        Assert.Contains("/api/tags/dungeon/merge", Uri(api["dungeon"].Merge.ToPostRequestInformation(
            new Generated.Models.TagMerge { Into = "dungeons" })));
    }

    // display is a composed-type wrapper whose constructor sets nothing, so an
    // omitted --display must stay absent from the body rather than send null:
    // the server defaults it to the value's own trimmed casing.
    [Fact]
    public void OmittedDisplayLeavesTheCreateBodyWithoutIt()
    {
        var body = TagsService.BuildCreateBody("GM Screen", display: null);
        Assert.Equal("GM Screen", body.Value);
        Assert.Null(body.Display);
    }

    [Fact]
    public void GivenDisplayReachesTheCreateBodyThroughTheComposedWrapper()
    {
        var body = TagsService.BuildCreateBody("gm-screen", "GM Screen");
        Assert.Equal("GM Screen", body.Display!.String);
    }
```

The file's using block becomes:

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TagsServiceTests`
Expected: compile error — `BuildCreateBody` does not exist on `TagsService`.

- [ ] **Step 4: Add the four sends**

In `src/GrimoireCli/Services/TagsService.cs`, replace the class doc comment with:

```csharp
/// <summary>
/// The two tag reads and the four writes. The reads are guarded by
/// get_current_user (routers/tags/core.py), which carries no role; all four
/// writes are require_gm_or_admin, so each names a permissionHint. Every path
/// carrying a tag key 404s on one that matches nothing, so those sends name a
/// notFoundHint — including merge, whose 404 is about the source tag.
/// </summary>
```

Add beside the existing `NotFoundHint` constant:

```csharp
    private const string GmOrAdminHint = "the gm or admin role";
```

Append these methods to the class:

```csharp
    /// <summary>
    /// POST /api/tags. Answers 201. Idempotent by internal key: creating a tag
    /// that already exists returns the existing row rather than failing.
    /// </summary>
    public async Task<string> CreateAsync(string value, string? display)
    {
        var info = _client.Api.Api.Tags.ToPostRequestInformation(BuildCreateBody(value, display));
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint);
    }

    /// <summary>
    /// TagCreate.Display is a composed-type wrapper whose constructor sets
    /// nothing, so assigning through it only when --display was given leaves an
    /// omitted one absent from the body. Internal so a test can pin that a
    /// client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.TagCreate BuildCreateBody(string value, string? display)
    {
        var body = new Generated.Models.TagCreate { Value = value };
        if (display is not null)
            body.Display = new Generated.Models.TagCreate.TagCreate_display { String = display };
        return body;
    }

    /// <summary>
    /// PATCH /api/tags/{internal}. The internal key follows the new display
    /// when its normalized form changes, and the tag is merged into whatever
    /// already owns that key (services/tag_service/_admin.py:68).
    /// </summary>
    public async Task<string> RenameAsync(string tag, string display)
    {
        var body = new Generated.Models.TagDisplayUpdate { Display = display };
        var info = _client.Api.Api.Tags[tag].ToPatchRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// POST /api/tags/{internal}/merge. Creates the target if it does not
    /// exist; 400s on a self-merge.
    /// </summary>
    public async Task<string> MergeAsync(string tag, string into)
    {
        var body = new Generated.Models.TagMerge { Into = into };
        var info = _client.Api.Api.Tags[tag].Merge.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>DELETE /api/tags/{internal}. Answers 204, so the body is empty.</summary>
    public async Task<string> DeleteAsync(string tag)
    {
        var info = _client.Api.Api.Tags[tag].ToDeleteRequestInformation();
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint, notFoundHint: NotFoundHint);
    }
```

- [ ] **Step 5: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TagsServiceTests
```

Expected: build clean, all `TagsServiceTests` pass.

- [ ] **Step 6: Commit**

```bash
git add docs/specs/2026-09-18-tags-writes-design.md docs/plans/2026-09-18-tags-writes.md \
        src/GrimoireCli/Services/TagsService.cs tests/GrimoireCli.Tests/Services/TagsServiceTests.cs
git commit -m "feat: add the four tag write sends"
```

---

### Task 2: The four subcommands

**Files:**
- Modify: `src/GrimoireCli/Commands/TagsCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs`

**Interfaces:**
- Consumes: `TagsService.CreateAsync/RenameAsync/MergeAsync/DeleteAsync` from Task 1; `CommandHelper.BuildClient()`, `ConsoleOutput.WriteRawJson(string)`, `command.AddRoleRequired`, `AddHelpSection`, `AddExamples`, `AddResponseExample<T>` — all already used in this file.
- Produces: subcommands `create`, `rename`, `delete`, `merge` on the `tags` group, in that order after `list` and `items`.

- [ ] **Step 1: Write the failing tests**

In `tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs`, replace `TheGroupHostsBothReads` with:

```csharp
    [Fact]
    public void TheGroupHostsTheReadsThenTheWrites()
    {
        Assert.Equal(
            ["list", "items", "create", "rename", "delete", "merge"],
            TagsCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }
```

Change `EveryCommandCarriesAResponseShape`'s cases to `list`, `items`, `create`, `rename`, `merge` — `delete` answers 204 and registers no shape — and add:

```csharp
    // 204: a registered response shape would render an empty sample and read as
    // a body the command never prints.
    [Fact]
    public void DeleteRegistersNoResponseShape()
    {
        Assert.DoesNotContain("Response shape:", Help(["tags", "delete"], full: true));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("merge")]
    public void EveryWriteDeclaresTheGmOrAdminRole(string leaf)
    {
        var help = Help(["tags", leaf]);
        Assert.Contains("Role required:", help);
        Assert.Contains("gm or admin", help);
    }

    [Theory]
    [InlineData(new object[] { new[] { "create" } })]
    [InlineData(new object[] { new[] { "rename", "--tag", "a" } })]
    [InlineData(new object[] { new[] { "rename", "--display", "A" } })]
    [InlineData(new object[] { new[] { "delete" } })]
    [InlineData(new object[] { new[] { "merge", "--tag", "a" } })]
    [InlineData(new object[] { new[] { "merge", "--into", "b" } })]
    public void EveryWriteRequiresItsFlags(string[] args)
    {
        Assert.NotEmpty(TagsCommand.Create().Parse(args).Errors);
    }

    [Theory]
    [InlineData(new object[] { new[] { "create", "--value", "GM Screen" } })]
    [InlineData(new object[] { new[] { "create", "--value", "gm-screen", "--display", "GM Screen" } })]
    [InlineData(new object[] { new[] { "rename", "--tag", "a", "--display", "A" } })]
    [InlineData(new object[] { new[] { "delete", "--tag", "a" } })]
    [InlineData(new object[] { new[] { "merge", "--tag", "a", "--into", "b" } })]
    public void EveryWriteParsesWithItsFlags(string[] args)
    {
        Assert.Empty(TagsCommand.Create().Parse(args).Errors);
    }

    // The issue this group was written from claimed rename leaves the internal
    // key alone. It does not (services/tag_service/_admin.py:68), and a caller
    // who believes otherwise loses a tag to a silent merge.
    [Fact]
    public void RenameWarnsThatTheKeyFollowsAndMayMerge()
    {
        var help = Help(["tags", "rename"]);
        Assert.Contains("internal key follows", help);
        Assert.Contains("merged", help);
    }

    // Folder-derived carriers survive a merge, so the source tag reappears in
    // tags list with a count — otherwise unexplainable.
    [Fact]
    public void MergeWarnsThatFolderTagsAreLeftBehind()
    {
        Assert.Contains("folder", Help(["tags", "merge"]));
    }

    [Fact]
    public void DeleteWarnsThatItCannotBeUndone()
    {
        Assert.Contains("cannot be undone", Help(["tags", "delete"]));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TagsCommandTests`
Expected: FAIL — the subcommand list assertion fails and every write help render is empty.

- [ ] **Step 3: Register the four subcommands**

In `src/GrimoireCli/Commands/TagsCommand.cs`, extend `Create()`:

```csharp
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateItemsCommand());
        command.Subcommands.Add(CreateCreateCommand());
        command.Subcommands.Add(CreateRenameCommand());
        command.Subcommands.Add(CreateDeleteCommand());
        command.Subcommands.Add(CreateMergeCommand());
        return command;
```

- [ ] **Step 4: Implement the four builders**

Add to the class, after `CreateItemsCommand`:

```csharp
    private static Command CreateCreateCommand()
    {
        var valueOption = new Option<string>("--value") { Description = "The tag text; its internal key is this lowercased", Required = true };
        var displayOption = new Option<string?>("--display") { Description = "Display casing, when it must differ from --value" };
        var command = new Command("create", "Create a tag up front")
        {
            valueOption, displayOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Idempotent: an existing tag is returned unchanged. Tags are also",
            "created on first use, so this is only needed to reserve one.",
            "",
            "'/' and '\\' are rejected: the key addresses the tag in the path.",
            "",
            "category is the resource type the tag is first used on, so a tag",
            "created here and not yet applied reads as shared.");
        command.AddExamples("grimoire-cli tags create --value \"GM Screen\"");
        command.AddResponseExample<Generated.Models.TagCreatedResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.CreateAsync(
                parseResult.GetValue(valueOption)!,
                parseResult.GetValue(displayOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateRenameCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag's current internal key, from tags list", Required = true };
        var displayOption = new Option<string>("--display") { Description = "The new display value", Required = true };
        var command = new Command("rename", "Rename a tag")
        {
            tagOption, displayOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "The internal key follows the new display when its lowercased form",
            "changes, and folder tags are rewritten onto the new key. If another",
            "tag already owns that key, the two are merged and the survivor is",
            "returned — there is no warning and no undo.",
            "",
            "A tag that exists only on a folder is materialised first, so the new",
            "display survives a rescan.",
            "",
            "'/' and '\\' are rejected in the new display.");
        command.AddExamples(
            "grimoire-cli tags rename --tag gm-screen --display \"GM Screen\"",
            "grimoire-cli tags rename --tag freinds --display friends");
        command.AddResponseExample<Generated.Models.TagRenamedResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.RenameAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(displayOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateDeleteCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag's internal key, from tags list", Required = true };
        var command = new Command("delete", "Delete a tag everywhere")
        {
            tagOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Unlinks the tag from every item and strips it from every folder.",
            "This cannot be undone, and there is no confirmation prompt.",
            "",
            "A folder tag that came from a tags.json returns on the next rescan;",
            "the library is read-only, so the file itself is not rewritten.",
            "",
            "Answers 204: stdout carries no body.");
        command.AddExamples("grimoire-cli tags delete --tag gm-screen");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.DeleteAsync(parseResult.GetValue(tagOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreateMergeCommand()
    {
        var tagOption = new Option<string>("--tag") { Description = "The tag to merge away, by internal key", Required = true };
        var intoOption = new Option<string>("--into") { Description = "The surviving tag's key; created if it does not exist", Required = true };
        var command = new Command("merge", "Merge one tag into another")
        {
            tagOption, intoOption
        };
        command.AddRoleRequired("gm or admin");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Re-points every item link, then deletes --tag. Items already",
            "carrying both keep one link.",
            "",
            "Folder tags are not re-pointed: a folder carrying --tag still",
            "carries it afterwards, so the merged tag can reappear in tags list.",
            "Use tags rename to move a folder-only tag.",
            "",
            "404 when --tag has no item links at all, even if a folder carries",
            "it. '/' and '\\' are rejected in --into but allowed in --tag, so a",
            "tag that predates that rule can be merged out of trouble.");
        command.AddExamples("grimoire-cli tags merge --tag \"D&D\" --into dungeons-and-dragons");
        command.AddResponseExample<Generated.Models.TagRenamedResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new TagsService(client);
            var result = await service.MergeAsync(
                parseResult.GetValue(tagOption)!,
                parseResult.GetValue(intoOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

- [ ] **Step 5: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 6: Read the rendered help, not the source**

```bash
dotnet run --project src/GrimoireCli -- tags create --help
dotnet run --project src/GrimoireCli -- tags rename --help
dotnet run --project src/GrimoireCli -- tags delete --help
dotnet run --project src/GrimoireCli -- tags merge --help
```

Check each: the Role required section renders "gm or admin"; no Notes line restates a flag description or a field already visible in the response sample; `delete` shows no response shape. Trim any line that fails that.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Commands/TagsCommand.cs tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs
git commit -m "feat: add tags create, rename, delete and merge"
```

---

### Task 3: Smoke coverage and the two live checks

**Files:**
- Modify: `docker/smoke-test.sh` (append a `--- tag writes ---` block before the final `echo "smoke: all checks passed"`)

**Interfaces:**
- Consumes: the four commands from Task 2, and the script's existing `$CLI`, `$WORK`, `ok`, `fail` helpers.
- Produces: nothing later tasks read; Task 4 records the two live findings this task produces.

**Constraint:** the block must converge on a re-run — fixed tag names, and it must delete what it creates. It touches only tags it invents, never a fixture's own.

- [ ] **Step 1: Bring up and seed the stack**

```bash
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

Under docker-outside-of-docker, export the host paths from `docker/env.example` and use `http://host.docker.internal:9481`.

- [ ] **Step 2: Run the two live checks by hand and record the answers**

```bash
CLI="dotnet run --project src/GrimoireCli --"
DSA=$($CLI systems list --include-children | jq -r '.[] | select(.name == "Das Schwarze Auge 5 DE") | .id')

# (a) merge against a folder-only tag. errata-smoke exists only because a book
# folder carries it, so it has no catalog row: expect a 404 naming the tag.
printf '{"path":"%s/core/errata","tags":["errata-smoke"]}' "$DSA" \
  | $CLI systems book-folders set --id "$DSA" --stdin
$CLI tags merge --tag errata-smoke --into errata-merged; echo "exit=$?"
$CLI tags rename --tag errata-smoke --display "Errata Smoke"; echo "exit=$?"   # expect success
$CLI systems book-folders delete --id "$DSA" --path "$DSA/core/errata"
$CLI tags delete --tag "errata smoke" 2>/dev/null; $CLI tags delete --tag errata-smoke 2>/dev/null

# (b) a slashed key survives the path encoding. create rejects one, so address
# an absent one and check the answer is the tags 404, not a routing error.
$CLI tags create --value "smoke/slash"; echo "exit=$?"     # expect 400, forbidden char
$CLI tags items --tag "smoke/slash"; echo "exit=$?"        # expect the 404 hint, not a 405
```

Write the observed exit codes and messages into the scratchpad; Task 4 turns them into `docs/grimoire-api-notes.md` lines. Leave no tag behind — `tags list` at the end must show neither `errata smoke` nor `errata-smoke`.

- [ ] **Step 3: Append the smoke block**

Insert before the final `echo "smoke: all checks passed" >&2`:

```bash
# --- tag writes --------------------------------------------------------------
# Fixed names, invented by this script and deleted at the end, so a re-run
# converges. Nothing here touches a fixture's own tags.
CREATE_JSON=$("$CLI" tags create --value "Smoke Alpha Tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags create exited non-zero"; }
[ "$(echo "$CREATE_JSON" | jq -r .internal)" = "smoke alpha tag" ] \
  || fail "create should lowercase the internal key: $CREATE_JSON"
[ "$(echo "$CREATE_JSON" | jq -r .display)" = "Smoke Alpha Tag" ] \
  || fail "create should keep the entered casing: $CREATE_JSON"
ok "tags create returns the new tag's key and display"

AGAIN_JSON=$("$CLI" tags create --value "smoke alpha tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags create is not idempotent"; }
[ "$(echo "$AGAIN_JSON" | jq -r .display)" = "Smoke Alpha Tag" ] \
  || fail "a second create should not rewrite the display: $AGAIN_JSON"
ok "tags create is idempotent by internal key"

# The rename that re-keys: the issue this was built from claimed it could not.
RENAME_JSON=$("$CLI" tags rename --tag "smoke alpha tag" --display "Smoke Renamed Tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags rename exited non-zero"; }
[ "$(echo "$RENAME_JSON" | jq -r .internal)" = "smoke renamed tag" ] \
  || fail "rename should re-key the tag: $RENAME_JSON"
ok "tags rename moves the internal key with the display"

"$CLI" tags create --value "Smoke Beta Tag" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "tags create exited non-zero for the merge source"; }
MERGE_JSON=$("$CLI" tags merge --tag "smoke beta tag" --into "smoke renamed tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags merge exited non-zero"; }
[ "$(echo "$MERGE_JSON" | jq -r .internal)" = "smoke renamed tag" ] \
  || fail "merge should return the survivor: $MERGE_JSON"
"$CLI" tags list 2>"$WORK/cli.err" | jq -e '[.tags[].internal] | index("smoke beta tag") == null' >/dev/null \
  || fail "the merged-away tag should be gone from the listing"
ok "tags merge folds the source into the target"

"$CLI" tags merge --tag "smoke renamed tag" --into "smoke renamed tag" >/dev/null 2>&1 \
  && fail "merging a tag into itself should fail"
ok "tags merge refuses a self-merge"

"$CLI" tags delete --tag "smoke renamed tag" >"$WORK/tagdel.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "tags delete exited non-zero"; }
[ ! -s "$WORK/tagdel.out" ] || fail "204 should print nothing: $(cat "$WORK/tagdel.out")"
"$CLI" tags list 2>"$WORK/cli.err" | jq -e '[.tags[].internal] | index("smoke renamed tag") == null' >/dev/null \
  || fail "the deleted tag should be gone from the listing"
ok "tags delete removes the tag and prints no body"

"$CLI" tags delete --tag "no-such-smoke-tag" >/dev/null 2>&1 \
  && fail "deleting a tag that does not exist should fail"
ok "tags delete refuses an unknown tag"
```

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed` both times. A second run that fails means the block does not converge — fix it here, not by loosening an assertion.

- [ ] **Step 5: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the tag writes in the smoke test"
```

---

### Task 4: Docs

**Files:**
- Modify: `src/GrimoireCli/Commands/TagsCommand.cs` (two wrong Notes lines — Step 0)
- Modify: `README.md` (Commands table, the `tags` rows near line 304)
- Modify: `tools/generate-api-coverage.py` (the `IMPLEMENTED` dict, near line 194)
- Modify: `docs/grimoire-api-coverage.md` (regenerated, never hand-edited)
- Modify: `docs/grimoire-api-notes.md`

**Interfaces:**
- Consumes: the command names from Task 2 and the two live findings from Task 3.
- Produces: the PR-ready docs set.

- [ ] **Step 0: Correct the two help lines Task 3 disproved**

Task 3 ran `tags merge` against a tag carried only by a book folder and it
**succeeded** (200, survivor returned), rather than the 404 the help text
claims. The cause is in the source: every folder tag gets a catalog row —
`register_folder_tags` calls `get_or_create_tag` (`services/tag_service/_folders.py:63`),
and the `tags.json` scan goes through the same function (`indexer/tags.py:81`).
So merge's 404 is about a missing catalog row, which a folder tag never has,
and it is not about item links at all.

In `CreateMergeCommand`, replace the third Notes paragraph:

```csharp
            "404 when --tag has no item links at all, even if a folder carries",
            "it. '/' and '\\' are rejected in --into but allowed in --tag, so a",
            "tag that predates that rule can be merged out of trouble.");
```

with:

```csharp
            "'/' and '\\' are rejected in --into but allowed in --tag, so a tag",
            "that predates that rule can be merged out of trouble.");
```

In `CreateRenameCommand`, delete this paragraph and the blank line before it —
a folder tag always has a catalog row, so there is nothing to materialise:

```csharp
            "",
            "A tag that exists only on a folder is materialised first, so the new",
            "display survives a rescan.",
```

Then `dotnet format GrimoireCli.sln` and run the suite:
`dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter TagsCommandTests`.
`MergeWarnsThatFolderTagsAreLeftBehind` asserts only on "folder", which the
surviving paragraph still carries, so it must still pass. If any test fails,
stop and report rather than editing the test.

- [ ] **Step 1: Add the README rows**

After the existing `tags items` row:

```markdown
| `tags create --value <value> [--display <text>]` | Create a tag up front; idempotent (gm or admin) |
| `tags rename --tag <key> --display <text>` | Rename a tag; the key follows and may merge (gm or admin) |
| `tags delete --tag <key>` | Delete a tag everywhere; no undo (gm or admin) |
| `tags merge --tag <key> --into <key>` | Merge one tag into another (gm or admin) |
```

- [ ] **Step 2: Add the coverage entries**

In `tools/generate-api-coverage.py`, beside the two existing tag lines:

```python
    "POST /api/tags": "`tags create` ✅",
    "PATCH /api/tags/{internal}": "`tags rename` ✅",
    "DELETE /api/tags/{internal}": "`tags delete` ✅",
    "POST /api/tags/{internal}/merge": "`tags merge` ✅",
```

- [ ] **Step 3: Regenerate the coverage table**

With the stack from Task 3 still up:

```bash
tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly the four rows change from unimplemented to the new commands. Any other row changing means the pin or the clone moved — stop and report it rather than committing the drift.

- [ ] **Step 4: Record the verified behaviour**

Add to `docs/grimoire-api-notes.md`, in the section its existing tag entries live in (match the file's own heading style):

```markdown
### Tag writes

- `PATCH /api/tags/{internal}` re-keys. The internal key follows the new
  display whenever its lowercased form changes, folder `tags.json` entries are
  rewritten onto the new key, and if another tag already owns that key the two
  are **merged**, with the survivor returned
  (`services/tag_service/_admin.py:68-113`, v1.7.1).
- `POST /api/tags/{internal}/merge` moves item links only. Folder tags are
  untouched (`routers/tags/core.py:208-237`), so a tag carried by a folder is
  still carried by it after the merge and reappears in `tags list`. The 404 is
  about a missing catalog row, not about having no links: **merging a
  folder-derived tag succeeds** — verified live against 1.7.1 — because every
  folder tag gets a catalog row, both when written through the API and when
  scanned from a `tags.json` (`services/tag_service/_folders.py:63`,
  `indexer/tags.py:81`). The target is created if missing; a self-merge 400s.
- `POST /api/tags` is idempotent by internal key and answers 201; `display`
  defaults to the value's own trimmed casing (`_catalog.py:22`).
- `DELETE /api/tags/{internal}` answers 204 and strips folder associations as
  well as item links; the library is read-only, so a `tags.json` tag returns on
  the next rescan (`routers/tags/core.py:239-257`).
- `/` and `\` are rejected in a created value, a new display and a merge
  *target*, but not in a merge *source* — merging is the documented way out of
  a tag created before that rule (`_catalog.py:40`, `routers/tags/_schemas.py`).
```

Replace any of these lines with what Task 3 actually observed if the stack disagrees, and say so in the PR body.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/TagsCommand.cs README.md \
        tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md docs/plans/2026-09-18-tags-writes.md
git commit -m "docs: record the tag write commands"
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
git push -u origin feat/tags-writes
gh pr create --title "feat: tags writes" --body "$(cat <<'EOF'
Completes the `tags` group with `create`, `rename`, `delete` and `merge`
(closes #42).

The issue's premise was wrong and the help text says so: `PATCH` does not
rename the display value alone — the internal key follows it, folder tags are
rewritten onto the new key, and a collision silently merges. `merge` moves item
links only, so folder-derived carriers keep the source tag.

- `dotnet format --verify-no-changes`, `build`, `test`, `docker/smoke-test.sh`: all pass
- README Commands table and `grimoire-api-coverage.md` updated in this change
- `grimoire-api-notes.md` records the rename and merge behaviour, cited to v1.7.1
EOF
)"
```

- [ ] **Step 3: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked. A PR is done at "all checks green", not at "PR open". Present the PR URL as a clickable link.
