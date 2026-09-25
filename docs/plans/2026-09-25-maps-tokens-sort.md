# `--sort` on `maps list` and `tokens list` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry Grimoire 1.7.2's `sort=path|name` query parameter on `maps list` and `tokens list`, and move `MinSupportedVersion` to 1.7.2 because an older server drops the parameter silently.

**Architecture:** Each command group owns its own command file and its own service; the option is added to both independently rather than shared. The option sends nothing when omitted, leaving the server on its own default. The floor move is a separate commit whose only reason is this flag.

**Tech Stack:** C# / .NET 10, System.CommandLine, xUnit, bash smoke test against a Dockerised Grimoire 1.7.2.

**Spec:** [docs/specs/2026-09-25-maps-tokens-sort-design.md](../specs/2026-09-25-maps-tokens-sort-design.md)

## Global Constraints

- Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.
- No blank lines between consecutive `AddOption`/`Subcommands.Add` calls or consecutive variable declarations of the same kind.
- Help text is terse: one-liners, no prose. Never restate what a flag description or a rendered value set already says.
- Both `list` endpoints are `require_not_guest` reads, which carry **no** role tag. Do not add `AddRoleRequired`.
- Commit messages are Conventional Commits, imperative, lowercase, no period. No `Co-Authored-By` line, no "Generated with Claude Code".
- The spec file `docs/specs/2026-09-25-maps-tokens-sort-design.md` is already written but uncommitted; Task 1 commits it alongside the first code change.
- Do not edit `CHANGELOG.md`. It belongs to the release process.

---

### Task 1: `--sort` on both list commands

**Files:**
- Modify: `src/GrimoireCli/Services/MapsService.cs` (`ListAsync`, around line 21)
- Modify: `src/GrimoireCli/Services/TokensService.cs` (`ListAsync`, around line 21)
- Modify: `src/GrimoireCli/Commands/MapsCommand.cs` (`CreateListCommand`, around lines 11-66)
- Modify: `src/GrimoireCli/Commands/TokensCommand.cs` (`CreateListCommand`, around lines 11-57)
- Modify: `README.md` (Commands table rows for `maps list` and `tokens list`)
- Test: `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs`
- Commit: `docs/specs/2026-09-25-maps-tokens-sort-design.md`, `docs/plans/2026-09-25-maps-tokens-sort.md`

**Interfaces:**
- Produces: `MapsService.ListAsync(string? mapType, string? folder, int? limit, int? offset, string? sort)` and `TokensService.ListAsync(int? limit, int? offset, string? sort)`. Both send `c.QueryParameters.Sort = sort`, so a null leaves the parameter off the wire.
- Produces: a `private static readonly string[] SortOrders = ["path", "name"];` field on each of `MapsCommand` and `TokensCommand`, placed directly under the existing `_logger` field.
- Consumes: `OptionHelpers.Choice(string name, string description, string[] allowed)` returning `Option<string?>`, already in `src/GrimoireCli/Commands/OptionHelpers.cs`.

- [ ] **Step 1: Write the failing tests**

In `tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs`, insert directly above the existing `// The exact-match rule changes what a caller asks for, so it has to be said.` comment:

```csharp
    // The server's pattern makes an unknown value a 422, so the round-trip is
    // spent to learn what the value set already says.
    [Fact]
    public void ListRejectsAnUnknownSortOrder()
    {
        Assert.NotEmpty(MapsCommand.Create().Parse(["list", "--sort", "filename"]).Errors);
    }

    [Fact]
    public void ListAcceptsBothSortOrders()
    {
        Assert.Empty(MapsCommand.Create().Parse(["list", "--sort", "path"]).Errors);
        Assert.Empty(MapsCommand.Create().Parse(["list", "--sort", "name"]).Errors);
    }

    // Which column each order sorts on decides whether a page is a run of
    // folders or a slice of the whole tree.
    [Fact]
    public void ListSaysWhatEachSortOrderOrdersBy()
    {
        var output = Help(["maps", "list"]);
        Assert.Contains("orders by relative_path", output);
        Assert.Contains("name orders by filename", output);
    }

```

In `tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs`, insert directly above the existing `[Theory]` whose `[InlineData("0")]` / `[InlineData("-1")]` feed `ListRejectsALimitBelowOne`:

```csharp
    // The server's pattern makes an unknown value a 422, so the round-trip is
    // spent to learn what the value set already says.
    [Fact]
    public void ListRejectsAnUnknownSortOrder()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["list", "--sort", "filename"]).Errors);
    }

    [Fact]
    public void ListAcceptsBothSortOrders()
    {
        Assert.Empty(TokensCommand.Create().Parse(["list", "--sort", "path"]).Errors);
        Assert.Empty(TokensCommand.Create().Parse(["list", "--sort", "name"]).Errors);
    }

    // Which column each order sorts on decides whether a page is a run of
    // folders or a slice of the whole tree.
    [Fact]
    public void ListSaysWhatEachSortOrderOrdersBy()
    {
        var output = Help(["tokens", "list"]);
        Assert.Contains("orders by relative_path", output);
        Assert.Contains("name orders by filename", output);
    }

```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "SortOrder"`
Expected: FAIL — six tests, the parse ones because `--sort` is an unrecognised option so `Parse` reports an error for the *valid* values too, the help ones because the Notes line does not exist.

- [ ] **Step 3: Add the parameter to both services**

In `src/GrimoireCli/Services/MapsService.cs`, replace the signature and query-parameter block of `ListAsync`:

```csharp
    public async Task<string> ListAsync(string? mapType, string? folder, int? limit, int? offset, string? sort)
    {
        var info = _client.Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = mapType;
            c.QueryParameters.Folder = folder;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
            c.QueryParameters.Sort = sort;
        });
        return await _client.SendAsync(info);
    }
```

In `src/GrimoireCli/Services/TokensService.cs`:

```csharp
    public async Task<string> ListAsync(int? limit, int? offset, string? sort)
    {
        var info = _client.Api.Api.Tokens.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
            c.QueryParameters.Sort = sort;
        });
        return await _client.SendAsync(info);
    }
```

- [ ] **Step 4: Add the option to `MapsCommand`**

In `src/GrimoireCli/Commands/MapsCommand.cs`, add the value set directly under the `_logger` field:

```csharp
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] SortOrders = ["path", "name"];
```

In `CreateListCommand`, add the option after `offsetOption` and list it in the command's initialiser:

```csharp
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var sortOption = OptionHelpers.Choice("--sort", "Row order; default path", SortOrders);
        var command = new Command("list", "List maps")
        {
            mapTypeOption, folderOption, limitOption, offsetOption, sortOption
        };
```

Extend the Notes block — insert the new paragraph between the variants line and the paging line:

```csharp
            "Variants are hidden — only the main copy of a family is listed.",
            "",
            "--sort path orders by relative_path, so a page is a contiguous run of",
            "folders; name orders by filename across the whole tree.",
            "",
            "Page with --offset against total in the response.");
```

Pass it to the service in `SetAction`:

```csharp
            var result = await service.ListAsync(
                parseResult.GetValue(mapTypeOption),
                parseResult.GetValue(folderOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(sortOption));
```

- [ ] **Step 5: Add the option to `TokensCommand`**

In `src/GrimoireCli/Commands/TokensCommand.cs`, add the value set directly under the `_logger` field:

```csharp
    private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly string[] SortOrders = ["path", "name"];
```

In `CreateListCommand`:

```csharp
        var offsetOption = OptionHelpers.Range("--offset", "Items to skip", 0);
        var sortOption = OptionHelpers.Choice("--sort", "Row order; default path", SortOrders);
        var command = new Command("list", "List tokens")
        {
            limitOption, offsetOption, sortOption
        };
```

Extend the Notes block — insert between the explicit-permission line and the paging line:

```csharp
            "The account's explicit permission filters the list server-side.",
            "",
            "--sort path orders by relative_path, so a page is a contiguous run of",
            "folders; name orders by filename across the whole tree.",
            "",
            "Page with --offset against total in the response.");
```

Pass it to the service in `SetAction`:

```csharp
            var result = await service.ListAsync(
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(sortOption));
```

- [ ] **Step 6: Update the README Commands table**

In `README.md`, replace the two rows:

```markdown
| `maps list [--map-type <t>] [--folder <path>] [--limit <n>] [--offset <n>] [--sort <path\|name>]` | List maps (defaults to 100 results) |
```

```markdown
| `tokens list [--limit <n>] [--offset <n>] [--sort <path\|name>]` | List tokens (defaults to 100 results) |
```

The `\|` escape is required — an unescaped pipe would end the table cell.

- [ ] **Step 7: Format, build and run the tests**

Run:

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build with 0 warnings and 0 errors; all tests pass.

- [ ] **Step 8: Read the rendered help**

Run: `dotnet run --project src/GrimoireCli -- maps list --help`

Confirm by eye that the Notes paragraph reads as written, that `--sort` renders its own value set, and that the description does not repeat the values. Repeat for `tokens list --help`.

- [ ] **Step 9: Commit**

```bash
git add docs/specs/2026-09-25-maps-tokens-sort-design.md docs/plans/2026-09-25-maps-tokens-sort.md src/GrimoireCli/Services/MapsService.cs src/GrimoireCli/Services/TokensService.cs src/GrimoireCli/Commands/MapsCommand.cs src/GrimoireCli/Commands/TokensCommand.cs README.md tests/GrimoireCli.Tests/Commands/MapsCommandTests.cs tests/GrimoireCli.Tests/Commands/TokensCommandTests.cs
git commit -m "feat: carry --sort on maps list and tokens list"
```

---

### Task 2: Move the version floor to 1.7.2

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs:528` (`MinSupportedVersion`)
- Modify: `src/GrimoireCli/Commands/SearchCommand.cs` (Query syntax help section)
- Modify: `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs`
- Modify: `README.md` (Compatibility section)
- Modify: `docs/grimoire-compatibility.md`
- Regenerate: `docs/grimoire-api-coverage.md`

**Interfaces:**
- Consumes: `--sort`, added in Task 1. It is the sole reason for the floor move; nothing else in 1.7.2 requires it.
- Produces: `MinSupportedVersion == "1.7.2"`, read by `docs/grimoire-api-coverage.md`'s generator for its "Tested range" line.

- [ ] **Step 1: Write the failing tests**

In `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs`, change both facts that treat `1.7.0` as in range. Replace:

```csharp
    [Fact]
    public void AnInRangeVersionWarnsAboutNothing()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.7.0", previous: "1.7.0"));
```

with:

```csharp
    [Fact]
    public void AnInRangeVersionWarnsAboutNothing()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.7.2", previous: "1.7.2"));
```

and replace:

```csharp
    // An unchanged in-range version stays silent even across checks.
    [Fact]
    public void AnUnchangedInRangeVersionStaysSilent()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.7.0", previous: "1.7.0"));
```

with:

```csharp
    // An unchanged in-range version stays silent even across checks.
    [Fact]
    public void AnUnchangedInRangeVersionStaysSilent()
        => Assert.Null(GrimoireApiClient.VersionWarning("1.7.2", previous: "1.7.2"));
```

Then add, directly below `AnOlderServerWarnsAboutTheFloor`:

```csharp
    // 1.7.1 is below the floor as of --sort, and the warning is what tells an
    // operator their maps list --sort name was silently ignored.
    [Fact]
    public void TheLastPreSortServerIsBelowTheFloor()
    {
        var warning = GrimoireApiClient.VersionWarning("1.7.1", previous: null);
        Assert.NotNull(warning);
        Assert.Contains("older", warning);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "VersionCheckCadenceTests"`
Expected: FAIL — `TheLastPreSortServerIsBelowTheFloor` because 1.7.1 is still in range and `VersionWarning` returns null.

- [ ] **Step 3: Raise the floor**

In `src/GrimoireCli/Api/GrimoireApiClient.cs`, change the one line:

```csharp
    private static readonly string MinSupportedVersion = "1.7.2";
```

Leave `MaxTestedVersion` at `"1.7.2"`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "VersionCheckCadenceTests"`
Expected: PASS.

- [ ] **Step 5: Drop the now-redundant version annotation from `search --help`**

Every supported server has the `code:` field once the floor is 1.7.2, so the annotation is noise. In `src/GrimoireCli/Commands/SearchCommand.cs`, replace these two lines of the `"Query syntax"` section:

```csharp
            "category, year, isbn, code (sku, product_code; 1.7.2+), language",
            "(lang), description (desc) — any books-only field also",
```

with:

```csharp
            "category, year, isbn, code (sku, product_code), language (lang),",
            "description (desc) — any books-only field also",
```

Do not reflow the two lines that follow. `SearchCommandTests.SearchSaysWhichFieldsDropTheMediaResults` asserts the whole phrase `drops the map, token, audio and model results`, which must stay on one line.

- [ ] **Step 6: Update the README compatibility line**

In `README.md`, replace:

```markdown
Requires Grimoire **v1.7.0 – v1.7.2**. The CLI warns on login if the server reports anything else. See [docs/grimoire-compatibility.md](docs/grimoire-compatibility.md) for the version matrix and the bump procedure.
```

with:

```markdown
Requires Grimoire **v1.7.2**. The CLI warns on login if the server reports anything else. See [docs/grimoire-compatibility.md](docs/grimoire-compatibility.md) for the version matrix and the bump procedure.
```

- [ ] **Step 7: Update `docs/grimoire-compatibility.md`**

Four edits.

First, the matrix. Replace the row:

```markdown
| 0.3.x | 1.7.0 – 1.7.2 | current, on `main` |
```

with two rows:

```markdown
| 0.3.x | 1.7.0 – 1.7.1 | last release for that line; no support branch |
| unreleased | 1.7.2 | current, on `main` |
```

The `main` row carries no version number on purpose: whether this ships as a minor or a patch is decided when the release is cut.

Second, the paragraph under the matrix. Replace:

```markdown
1.7.0 because the CLI now offers a flag older servers silently drop (below).
```

with:

```markdown
1.7.0 and then 1.7.2 because each added a flag older servers silently drop
(below).
```

Third, the `sort` bullet in the 1.7.2 section. Replace:

```markdown
- **`GET /api/maps` and `GET /api/tokens` take `sort=path|name`.** Unexposed —
  a `--sort` flag is what forces a floor, and `path` (the default, and what both
  commands already get) is the order the CLI has always returned.
```

with:

```markdown
- **`GET /api/maps` and `GET /api/tokens` take `sort=path|name`**, carried by
  `maps list --sort` and `tokens list --sort`. Omitting the flag sends no
  parameter and leaves the server on `path`, the order the CLI has always
  returned. This is the flag that moved the floor (below).
```

Fourth, a new paragraph. The 1.7.2 section currently ends with the add-on bullet, directly above `## Runtime check`. Insert between them:

```markdown
**`MinSupportedVersion` moved to 1.7.2 for one reason: `--sort`.** Same shape as
the 1.7.0 move. `sort` is an unknown query parameter on 1.7.0 and 1.7.1, and
FastAPI ignores it rather than refusing the request, so `--sort name` there
answers 200 with path-ordered rows — the silent no-op the floor warning exists
to catch. Nothing else in 1.7.2 needed a floor: `product_code` rides the
generated body model, and every other command still reaches 1.7.0.
```

Also fix the two stale statements of the current target. Replace `` `main` targets Grimoire 1.7.0. `` with `` `main` targets Grimoire 1.7.2. ``, and in the "Runtime check" section replace `currently `"1.7.0"` and `"1.7.2"`` with `currently `"1.7.2"` and `"1.7.2"``.

- [ ] **Step 8: Regenerate the API coverage table**

The table's header line reads the version constants, so it moves with the floor.

Run:

```bash
python3 tools/generate-api-coverage.py
git diff -- docs/grimoire-api-coverage.md
```

Expected: the "Tested range" line changes from `` `1.7.0`-`1.7.2` `` to `` `1.7.2`-`1.7.2` ``, and nothing else. If other rows move, stop: the stack is not on the pinned image.

- [ ] **Step 9: Format, build and run the full suite**

Run:

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/GrimoireCli/Api/GrimoireApiClient.cs src/GrimoireCli/Commands/SearchCommand.cs tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs README.md docs/grimoire-compatibility.md docs/grimoire-api-coverage.md
git commit -m "chore: raise the supported grimoire floor to 1.7.2"
```

---

### Task 3: Prove the flag on the live HTTP path

**Files:**
- Modify: `docker/seed.sh` (token fixtures, around lines 139-149)
- Modify: `docker/smoke-test.sh` (maps section around line 855, tokens section around line 1066)

**Interfaces:**
- Consumes: `maps list --sort` and `tokens list --sort` from Task 1, and a stack on the image pinned in `docker/docker-compose.yml`.
- Produces: a third fixture token at `tokens/Monsters/Undead/Ghoul.png`, which later smoke checks may rely on for a token count of 3.

- [ ] **Step 1: Add the third fixture token**

Map fixtures already sort differently by path and by name, but the two tokens do not: `Monsters/Goblin.png` and `Monsters/Undead/Skeleton.png` come out in the same order either way, so an assertion on them would pass whatever the flag did.

A third token fixes that. Path order becomes Goblin, Ghoul, Skeleton (`Monsters/G` sorts before `Monsters/U`); name order becomes Ghoul, Goblin, Skeleton (`Gh` before `Go`).

In `docker/seed.sh`, add the new line after the Skeleton fixture:

```bash
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/tokens/Monsters/Goblin.png"
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/tokens/Monsters/Undead/Skeleton.png"
# Third token so path order (Goblin, Ghoul, Skeleton) differs from filename
# order (Ghoul, Goblin, Skeleton) — what tokens list --sort is asserted on.
python3 "$HERE/make-fixtures.py" --png "$LIBRARY/tokens/Monsters/Undead/Ghoul.png"
```

and update the count in the summary line:

```bash
say "wrote 3 fixture tokens and 2 fixture audio tracks"
```

- [ ] **Step 2: Add the maps assertion**

In `docker/smoke-test.sh`, insert after the `ok "maps list --limit bounds the page"` line and before the `--folder` comment block:

```bash
# The two orders are distinguishable on the fixtures: by path the caves map comes
# last (maps/battlemaps/* before maps/battlemaps/caves/*), by filename it comes
# second. An unknown value is a 422 the CLI refuses first, so this proves the
# parameter reaches the query string rather than that the server has a default.
"$CLI" maps list --sort name >"$WORK/maps-sort.out" 2>&1 \
  || fail "maps list --sort exited non-zero"
jq -e '[.maps[].filename] == ["Crossroads.png", "Deep Cave.png", "Tavern.png"]' \
  "$WORK/maps-sort.out" >/dev/null \
  || fail "--sort name should order by filename: $(cat "$WORK/maps-sort.out")"
jq -e '[.maps[].filename] == ["Crossroads.png", "Tavern.png", "Deep Cave.png"]' \
  "$WORK/maps.out" >/dev/null \
  || fail "the default should order by path: $(cat "$WORK/maps.out")"
ok "maps list --sort name orders by filename, not by path"
```

- [ ] **Step 3: Add the tokens assertion**

In `docker/smoke-test.sh`, insert after the `ok "tokens list --limit bounds the page"` line and before the `TOKEN_ID=` assignment:

```bash
# Goblin sits in Monsters/, Ghoul and Skeleton in Monsters/Undead/, so path order
# and filename order disagree — see the maps check above.
"$CLI" tokens list --sort name >"$WORK/tokens-sort.out" 2>&1 \
  || fail "tokens list --sort exited non-zero"
jq -e '[.tokens[].filename] == ["Ghoul.png", "Goblin.png", "Skeleton.png"]' \
  "$WORK/tokens-sort.out" >/dev/null \
  || fail "--sort name should order by filename: $(cat "$WORK/tokens-sort.out")"
jq -e '[.tokens[].filename] == ["Goblin.png", "Ghoul.png", "Skeleton.png"]' \
  "$WORK/tokens.out" >/dev/null \
  || fail "the default should order by path: $(cat "$WORK/tokens.out")"
ok "tokens list --sort name orders by filename, not by path"
```

- [ ] **Step 4: Rebuild the stack from scratch**

A third fixture token has to be indexed by a boot scan, and the existing database holds rows for the old tree. Reset it fully rather than reseeding over the top:

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books docker/library/maps docker/library/models docker/library/tokens docker/library/audio docker/addon-index/index.json
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

Copying the users fixture before the first boot is required; skip it and the only symptom is a 401.

- [ ] **Step 5: Run the smoke test**

Run: `bash docker/smoke-test.sh`
Expected: `smoke: all checks passed`, including the two new `ok:` lines.

If the ordering assertions fail, print the two files and compare against the fixture paths before changing the expected arrays — a wrong expectation and a broken flag look the same from the failure message.

- [ ] **Step 6: Run the full pre-PR set**

Run, in order:

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass before the PR is opened. The smoke test is idempotent, so a second run converges rather than drifting — confirm by running it twice.

- [ ] **Step 7: Commit**

```bash
git add docker/seed.sh docker/smoke-test.sh
git commit -m "test: assert --sort name orders maps and tokens by filename"
```
