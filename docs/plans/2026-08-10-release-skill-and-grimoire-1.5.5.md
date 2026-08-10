# Release Skill and Grimoire 1.5.5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `abs-cli`'s gated release skill into this repo, and make the existing commands correct against Grimoire v1.5.5 — new response fields, two new query flags, and a fixture library restructured onto system containers.

**Architecture:** Three independent surfaces that share one branch. The skill is a standalone markdown file with no code dependency. The 1.5.5 support is a DTO widening plus two flags threaded through the existing `Command → Service → QueryBuilder → ApiClient` chain. The fixture work is entirely in `docker/seed.sh` and `docker/smoke-test.sh`, which are bash and are verified by running them against a live local stack.

**Tech Stack:** C# / .NET 10, `System.CommandLine`, source-generated `System.Text.Json` (Native AOT), xUnit, bash, Docker Compose.

**Design spec:** [`docs/specs/2026-08-10-release-skill-and-grimoire-1.5.5-design.md`](../specs/2026-08-10-release-skill-and-grimoire-1.5.5-design.md). Read it before starting — it carries the verified upstream citations behind every number in this plan.

## Global Constraints

- **Target Grimoire v1.5.5 only.** `MinSupportedVersion` and `MaxTestedVersion` are both `"1.5.5"`.
- **`temp/grimoire` is already checked out at `v1.5.5`.** Verify with `git -C temp/grimoire describe --tags`. It is the authoritative reference; the OpenAPI spec types nearly every response as `{}`.
- **No new commands.** The 29 routes v1.5.5 adds (bulk, add-ons, covers, metadata lookup) get no CLI surface in this change.
- **`CHANGELOG.md` is never touched** — it is owned by the release process, and this is a feature branch.
- **Conventional Commits**, imperative, lowercase, no trailing period, max ~72 chars. No `Co-Authored-By:` lines. No "Generated with Claude Code" attribution.
- **Run `dotnet format GrimoireCli.sln` after modifying any C# file.** CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** inside method bodies: none between consecutive `AddCommand`/`AddOption` calls, none before a `return` that follows setup calls, none between consecutive variable declarations of the same kind.
- **Comments state what the code does or why it must be so** — never what was deliberately left out.
- Branch is `feat/grimoire-1.5.5-and-release-skill`, already created off `main`.

## The one behaviour that drives most of this plan

`GET /api/systems` applies its **child-hiding check before every metadata filter** (`temp/grimoire/backend/routers/systems/core.py:78-95`). After Task 5 restructures the fixtures, every system carrying `edition`, `genre`, `family` or `license` metadata is a container child. So:

```
grimoire-cli systems list --edition "6 DE"                     → []   exit 0
grimoire-cli systems list --edition "6 DE" --include-children  → 1 system
```

An empty array with exit 0 is indistinguishable from "no matches". This is why Task 4's help text is not optional, and why every filter assertion in Task 6 gains `--include-children`.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `.claude/skills/release/SKILL.md` | **Create.** The gated release workflow | 1 |
| `docs/releasing.md` | Prerequisite table, pointer to the skill | 1 |
| `src/GrimoireCli/Api/GrimoireApiClient.cs` | Version constants | 2 |
| `docker/docker-compose.yml` | Grimoire image tag | 2 |
| `docs/grimoire-compatibility.md`, `README.md`, `CLAUDE.md` | Version-facing docs | 2 |
| `src/GrimoireCli/Models/GameSystemSummary.cs` | +7 container fields | 3 |
| `src/GrimoireCli/Models/GameSystemDetail.cs` | +`children` | 3 |
| `src/GrimoireCli/Commands/ResponseExamples.g.cs` | Regenerated, drift-tested | 3 |
| `tests/GrimoireCli.Tests/Models/GameSystemDtoTests.cs` | DTO field coverage | 3 |
| `src/GrimoireCli/Services/SystemsService.cs` | Two new query params | 4 |
| `src/GrimoireCli/Commands/SystemsCommand.cs` | Two new flags, help text | 4 |
| `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs` | Flag parsing | 4 |
| `docker/seed.sh` | Container fixture tree, trimmed PATCH bodies | 5 |
| `docker/smoke-test.sh` | Container assertions, `--include-children` on filters | 6 |
| `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md` | Coverage regeneration | 7 |
| `docs/grimoire-api-notes.md`, `docs/roadmap.md` | Verified behaviour, deferred work | 7 |

---

## Task 1: Port the release skill

Standalone — no dependency on any other task. The source is `abs-cli`'s `.claude/skills/release/SKILL.md`, fetched with `gh`.

**Files:**
- Create: `.claude/skills/release/SKILL.md`
- Modify: `docs/releasing.md`
- Modify: `CLAUDE.md` (the "Relationship to abs-cli" deviations list)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Fetch the reference skill**

```bash
mkdir -p /tmp/release-port
gh api repos/thomaslazar/abs-cli/contents/.claude/skills/release/SKILL.md \
  --jq '.content' | base64 -d > /tmp/release-port/abs-SKILL.md
wc -l /tmp/release-port/abs-SKILL.md   # expect 286
```

Read it in full before writing the port. Keep its eight-step structure, every `**GATE:**` line, and the `## Rules` section verbatim in shape.

- [ ] **Step 2: Write the ported skill**

Create `.claude/skills/release/SKILL.md`. Frontmatter:

```yaml
---
name: release
description: Create a new grimoire-cli release with human review gates. Creates release branch, generates changelog, opens PR for CI validation, then tags and publishes after merge.
disable-model-invocation: true
allowed-tools:
  - Bash
  - Read
  - Write
  - Glob
  - Grep
  - Edit
  - AskUserQuestion
---
```

Port the eight steps with these substitutions and additions. **Everything not listed here stays as abs-cli has it.**

*Step 1 (Preflight)* — replace the build/test block with:

```bash
BRANCH=$(git branch --show-current)
[ "$BRANCH" = "main" ] || { echo "ERROR: must be on main, currently on $BRANCH"; exit 1; }
git diff --quiet && git diff --cached --quiet || { echo "ERROR: working tree not clean"; git status --short; exit 1; }
git pull
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
./publish/grimoire-cli self-test
```

and the smoke-test block with this — the fixture copy is **required before the first boot**; skip it and the stack comes up with no users, whose only symptom is a 401:

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
docker compose -f docker/docker-compose.yml down
rm -rf publish/
```

Add this note under that block:

> Under docker-outside-of-docker the daemon runs on the host: reach the stack at
> `http://host.docker.internal:9481`, not `localhost`, and set
> `GRIMOIRE_LIBRARY_LOCAL` if the library lives outside the repo. See `CLAUDE.md`.

*Step 2 (Version bump)* — `src/GrimoireCli/GrimoireCli.csproj`, binary `grimoire-cli`. Replace the `--version` check with one that asserts **bare** output:

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
./publish/grimoire-cli --version
# Must print exactly: {VERSION_NUM} — with no "+pr-<n>.<sha7>" suffix.
# Release builds pass no BuildId; a suffix here means the build picked up PR
# metadata and the artifact would self-report a PR version. See docs/build.md.
rm -rf publish/
```

*New Step 3 — Reconcile the supported server range.* Insert as step 3 and renumber the rest (the old steps 3–8 become 4–9):

```markdown
## Step 3: Reconcile the Supported Server Range

`MinSupportedVersion` and `MaxTestedVersion` in
`src/GrimoireCli/Api/GrimoireApiClient.cs` gate the login-time warning. They
must agree with the matrix in `docs/grimoire-compatibility.md` and the
compatibility line in `README.md` before a tag is cut.

```bash
grep -n "MinSupportedVersion\|MaxTestedVersion" src/GrimoireCli/Api/GrimoireApiClient.cs
grep -n "Tested against Grimoire" README.md
sed -n '/## Matrix/,/^$/p' docs/grimoire-compatibility.md
```

All three must name the same Grimoire version. If this release adds support for
a newer Grimoire, they move together, and `docs/grimoire-compatibility.md`
gains a matrix row.

**GATE: Show the human all three values and confirm they agree.**
```

*Release notes / changelog step* — `CHANGELOG.md` header text becomes:

```markdown
# Changelog

All notable changes to grimoire-cli are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/).
```

Add a line noting that this is the **only** place `CHANGELOG.md` is written; feature branches never touch it.

*Verify step* — tap repo is `thomaslazar/homebrew-grimoire-cli`, asset pattern `grimoire-cli-linux-x64`.

*Rules section* — keep all six of abs-cli's rules verbatim, including that the skill may commit without asking.

- [ ] **Step 3: Verify the frontmatter parses and the skill is discoverable**

```bash
head -14 .claude/skills/release/SKILL.md
grep -c "GATE:" .claude/skills/release/SKILL.md   # expect >= 5
grep -n "disable-model-invocation: true" .claude/skills/release/SKILL.md
```

Expected: frontmatter block is well-formed YAML between `---` fences, at least five gates survive the port, and model invocation is disabled.

- [ ] **Step 4: Update `docs/releasing.md`**

In the "What a first release still needs" table, change the `HOMEBREW_TAP_TOKEN` row's state from `**outstanding**` to `**done** — set 2026-08-09`. Then add immediately below the table:

```markdown
The process below is automated by the `release` skill
(`.claude/skills/release/SKILL.md`), which is invoked by name and never
model-initiated. The prose is kept because a reader looking for the process
should not have to know the skill exists.
```

- [ ] **Step 5: Record the deviations in `CLAUDE.md`**

In the "Relationship to abs-cli" section, under "Deliberate deviations today", add:

```markdown
- **The `release` skill carries an extra step reconciling the supported server
  range.** `MinSupportedVersion` / `MaxTestedVersion`, the compatibility matrix
  and the README line must agree before a tag is cut. abs-cli has no counterpart
  because it has no login-time version gate. Its preflight also differs: the
  `docker/users.json.example` fixture must be copied before first boot, and the
  `--version` check asserts bare output because PR builds carry a
  `+pr-<n>.<sha7>` suffix.
```

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/release/SKILL.md docs/releasing.md CLAUDE.md
git commit -m "ci: port the abs-cli release skill"
```

---

## Task 2: Move the supported version to 1.5.5

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs:202-203`
- Modify: `docker/docker-compose.yml:31-33`
- Modify: `docs/grimoire-compatibility.md`, `README.md:151`, `CLAUDE.md:109-110`
- Test: `tests/GrimoireCli.Tests/Api/CompareVersionsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a local stack running `hunterreadca/grimoire:1.5.5`, which Tasks 5 and 6 require.

- [ ] **Step 1: Write the failing test**

Append to `tests/GrimoireCli.Tests/Api/CompareVersionsTests.cs`, inside the existing class:

```csharp
    [Fact]
    public void OneFiveFiveIsNewerThanOneFiveFour()
    {
        Assert.True(GrimoireApiClient.CompareVersions("1.5.5", "1.5.4") > 0);
    }
```

- [ ] **Step 2: Run it and confirm it passes already**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~CompareVersions"
```

Expected: PASS. `CompareVersions` is version-agnostic, so this pins the ordering rather than driving new code — it is the guard that the constants below are comparable at all. Do not add production code for it.

- [ ] **Step 3: Bump the constants**

In `src/GrimoireCli/Api/GrimoireApiClient.cs`, change both lines 202-203:

```csharp
    private static readonly string MinSupportedVersion = "1.5.5";
    private static readonly string MaxTestedVersion = "1.5.5";
```

- [ ] **Step 4: Bump the image tag**

In `docker/docker-compose.yml`, change the pinned image to `hunterreadca/grimoire:1.5.5` and update the comment above it to read:

```yaml
    # Pinned to the release this CLI targets (v1.5.5) — bump deliberately
```

- [ ] **Step 5: Update the version-facing docs**

`docs/grimoire-compatibility.md` — add a matrix row beneath the existing one:

```markdown
| 0.1.x | 1.5.5 | system containers; `parent_id` / `include_children` |
```

and change the runtime-check paragraph's `both currently `"1.5.4"`` to `both currently `"1.5.5"``, and `(1.5.4–1.5.4 today)` in `docs/authentication.md:57` to `(1.5.5–1.5.5 today)`.

`README.md:151` — change `Tested against Grimoire **v1.5.4**.` to `Tested against Grimoire **v1.5.5**.`

`CLAUDE.md:109-110` — change the clone pin:

```bash
  # Match MinSupportedVersion / MaxTestedVersion in src/GrimoireCli/Api/GrimoireApiClient.cs
  git clone --depth 1 --branch v1.5.5 https://github.com/hunter-read/grimoire.git temp/grimoire
```

- [ ] **Step 6: Recreate the stack on the new image**

The database must be reset, not re-seeded: the image changes, and `is_explicit` is never cleared on an existing system row, so a stale flag would survive a re-seed.

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
curl -sf http://host.docker.internal:9481/api/openapi.json | jq -r .info.version
```

Expected: prints `1.5.5`. If it prints `1.5.4`, stop — every container assertion downstream would produce a wrong shelf rather than an error.

- [ ] **Step 7: Run the tests and format**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build succeeds, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Api/GrimoireApiClient.cs docker/docker-compose.yml \
  docs/grimoire-compatibility.md docs/authentication.md README.md CLAUDE.md \
  tests/GrimoireCli.Tests/Api/CompareVersionsTests.cs
git commit -m "feat: target grimoire 1.5.5"
```

---

## Task 3: Widen the system DTOs

**Files:**
- Modify: `src/GrimoireCli/Models/GameSystemSummary.cs`
- Modify: `src/GrimoireCli/Models/GameSystemDetail.cs`
- Modify: `src/GrimoireCli/Commands/ResponseExamples.g.cs` (regenerated, not hand-edited)
- Test: `tests/GrimoireCli.Tests/Models/GameSystemDtoTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GameSystemSummary` with properties `HasCover` (bool), `ContainerKind` (string?), `ParentId` (string?), `ParentName` (string?), `ParentIsOnePage` (bool), `NameIsCustom` (bool), `ChildCount` (int); `GameSystemDetail.Children` (`List<GameSystemSummary>?`). Task 6 asserts on the JSON names `container_kind`, `parent_id`, `parent_name`, `child_count`, `children`.

`AppJsonContext` already registers `List<GameSystemSummary>` (`JsonContext.cs:11`), so `Children` needs **no** new `[JsonSerializable]` attribute. Do not add one.

- [ ] **Step 1: Write the failing tests**

In `tests/GrimoireCli.Tests/Models/GameSystemDtoTests.cs`, add these two constants and two facts to the existing class. Note `edition` is `"6 DE"` — v1.5.5 derives it from the child folder name verbatim.

```csharp
    // A container child as v1.5.5 serializes it: the seven container fields are
    // additions to the 1.5.4 shape, and absent them the CLI silently drops them.
    private const string ChildJson = """
    {"id":"child","name":"Shadowrun 6 DE","slug":"shadowrun-6-de","description":null,
     "publishers":[],"character_builder_url":null,"character_builder_urls":[],
     "urls":[],"tags":[],"genre":"","genres":["Cyberpunk"],"dice_materials":[],
     "system_family":"Shadowrun","parent_system":"Shadowrun","edition":"6 DE",
     "license":"","year":2019,"book_count":3,"total_page_count":26,
     "cover_image":null,"cover_book_id":null,"has_cover":true,"is_explicit":false,
     "is_system_agnostic":false,"is_one_page":false,"container_kind":"",
     "parent_id":"container","parent_name":"Shadowrun","parent_is_one_page":false,
     "name_is_custom":false,"child_count":0}
    """;

    private const string ContainerDetailJson = """
    {"id":"container","name":"Shadowrun","slug":"shadowrun","container_kind":"parent",
     "parent_id":null,"parent_name":"","parent_is_one_page":false,
     "name_is_custom":false,"child_count":1,"has_cover":false,"book_count":0,
     "total_page_count":0,"is_explicit":false,"is_system_agnostic":false,
     "is_one_page":false,"books":[],
     "children":[{"id":"child","name":"Shadowrun 6 DE","edition":"6 DE",
                  "parent_id":"container","child_count":0}]}
    """;

    [Fact]
    public void SummaryDeserializesTheContainerFields()
    {
        var s = JsonSerializer.Deserialize(ChildJson, AppJsonContext.Default.GameSystemSummary)!;
        Assert.True(s.HasCover);
        Assert.Equal("", s.ContainerKind);
        Assert.Equal("container", s.ParentId);
        Assert.Equal("Shadowrun", s.ParentName);
        Assert.False(s.ParentIsOnePage);
        Assert.False(s.NameIsCustom);
        Assert.Equal(0, s.ChildCount);
        Assert.Equal("6 DE", s.Edition);
    }

    [Fact]
    public void DetailDeserializesNestedChildren()
    {
        var d = JsonSerializer.Deserialize(ContainerDetailJson, AppJsonContext.Default.GameSystemDetail)!;
        Assert.Equal("parent", d.ContainerKind);
        Assert.Equal(1, d.ChildCount);
        Assert.Null(d.ParentId);
        Assert.NotNull(d.Children);
        Assert.Single(d.Children!);
        Assert.Equal("Shadowrun 6 DE", d.Children![0].Name);
        Assert.Equal("6 DE", d.Children![0].Edition);
    }
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~GameSystemDto"
```

Expected: compile errors — `GameSystemSummary` has no definition for `HasCover`, `ContainerKind`, `ParentId`, `ParentName`, `ParentIsOnePage`, `NameIsCustom`, `ChildCount`; `GameSystemDetail` has no `Children`.

- [ ] **Step 3: Add the seven fields to `GameSystemSummary`**

Append inside the class in `src/GrimoireCli/Models/GameSystemSummary.cs`, after `IsOnePage`:

```csharp

    [JsonPropertyName("has_cover")]
    public bool HasCover { get; set; }

    // System containers (upstream #261/#262). A container is a folder whose
    // immediate children are systems rather than categories: "" for an ordinary
    // system, "parent" for a parent-system container whose subfolders are
    // editions, "one-page" for a one-page collection.
    [JsonPropertyName("container_kind")]
    public string? ContainerKind { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("parent_name")]
    public string? ParentName { get; set; }

    [JsonPropertyName("parent_is_one_page")]
    public bool ParentIsOnePage { get; set; }

    // True once a user renames the system in the UI, after which the scanner
    // stops overwriting the name on rescan.
    [JsonPropertyName("name_is_custom")]
    public bool NameIsCustom { get; set; }

    [JsonPropertyName("child_count")]
    public int ChildCount { get; set; }
```

- [ ] **Step 4: Add `Children` to `GameSystemDetail`**

In `src/GrimoireCli/Models/GameSystemDetail.cs`, update the doc comment and add the property:

```csharp
/// <summary>
/// GET /api/systems/{id} — the summary shape plus the system's books, and its
/// child systems when it is a container. Filters on that endpoint apply to the
/// book list, and book_count / total_page_count are recomputed from the
/// filtered list.
/// </summary>
public class GameSystemDetail : GameSystemSummary
{
    [JsonPropertyName("books")]
    public List<Book>? Books { get; set; }

    [JsonPropertyName("children")]
    public List<GameSystemSummary>? Children { get; set; }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~GameSystemDto"
```

Expected: both new facts PASS.

- [ ] **Step 6: Regenerate the response examples**

`ResponseExamples.g.cs` backs `--help-full` and is guarded by a drift test that regenerates it and diffs. It must never be hand-edited.

```bash
dotnet run --project tools/GenerateResponseExamples/GenerateResponseExamples.csproj \
  -- src/GrimoireCli/Commands/ResponseExamples.g.cs
dotnet format GrimoireCli.sln
git diff --stat src/GrimoireCli/Commands/ResponseExamples.g.cs
```

Expected: the file changes to include the new fields. If the tool takes different arguments, run it with no arguments first and read its usage output.

- [ ] **Step 7: Run the full suite**

```bash
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all tests pass, including `ResponseExamplesDriftTest` and `ResponseExamplesJsonValidTest`.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Models/ src/GrimoireCli/Commands/ResponseExamples.g.cs \
  tests/GrimoireCli.Tests/Models/GameSystemDtoTests.cs
git commit -m "feat: surface the 1.5.5 system container fields"
```

---

## Task 4: Add `--parent-id` and `--include-children`

**Files:**
- Modify: `src/GrimoireCli/Services/SystemsService.cs:12-26`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs:39-86`
- Test: `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs`

**Interfaces:**
- Consumes: `GameSystemSummary` from Task 3.
- Produces: `SystemsService.ListAsync(string? sort, bool desc, string? genre, string? family, string? parentSystem, string? edition, string? license, bool? isExplicit, string? parentId, bool includeChildren)`. The two new parameters go **last**, so existing positional call sites keep their meaning.

- [ ] **Step 1: Write the failing test**

Read `tests/GrimoireCli.Tests/Commands/SystemsCommandTests.cs` first and follow its existing style for building a parse result. Add:

```csharp
    [Fact]
    public void ListAcceptsParentIdAndIncludeChildren()
    {
        var command = SystemsCommand.Create();
        var result = command.Parse("list --parent-id abc123 --include-children");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ListHelpDocumentsThatChildrenAreHiddenByDefault()
    {
        var command = SystemsCommand.Create();
        var help = HelpText(command, "list");
        Assert.Contains("--include-children", help);
        Assert.Contains("hidden", help, StringComparison.OrdinalIgnoreCase);
    }
```

If the test class has no `HelpText` helper, use the one in `tests/GrimoireCli.Tests/Commands/HelpOutputTests.cs` — read it and reuse its approach rather than inventing a second one.

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~SystemsCommand"
```

Expected: FAIL — unrecognised options `--parent-id` and `--include-children`.

- [ ] **Step 3: Thread the parameters through the service**

In `src/GrimoireCli/Services/SystemsService.cs`, change `ListAsync`:

```csharp
    public async Task<List<GameSystemSummary>> ListAsync(
        string? sort, bool desc, string? genre, string? family,
        string? parentSystem, string? edition, string? license, bool? isExplicit,
        string? parentId, bool includeChildren)
    {
        var query = QueryBuilder.Build(
            ("sort", sort),
            ("order", desc ? "desc" : null),
            ("genre", genre),
            ("family", family),
            ("parent_system", parentSystem),
            ("edition", edition),
            ("license", license),
            ("explicit", isExplicit?.ToString().ToLowerInvariant()),
            ("parent_id", parentId),
            ("include_children", includeChildren ? "true" : null));
        return await _client.GetAsync(ApiEndpoints.Systems + query, AppJsonContext.Default.ListGameSystemSummary);
    }
```

`include_children` is sent only when true, matching how `--desc` is handled: the server default is `false`, so an omitted parameter and an explicit `false` mean the same thing and the shorter URL is preferred.

- [ ] **Step 4: Add the flags to the command**

In `src/GrimoireCli/Commands/SystemsCommand.cs`, inside `CreateListCommand`, add after `explicitOption`:

```csharp
        var parentIdOption = new Option<string?>("--parent-id") { Description = "List only the children of this container" };
        var includeChildrenOption = new Option<bool>("--include-children") { Description = "Include container children, which are hidden by default" };
```

Add both to the command's option list, so it reads:

```csharp
        var command = new Command("list", "List all game systems")
        {
            sortOption, descOption, genreOption, familyOption,
            parentOption, editionOption, licenseOption, explicitOption,
            parentIdOption, includeChildrenOption,
            serverOption, tokenOption
        };
```

and pass them as the last two arguments to `service.ListAsync(...)`:

```csharp
                parseResult.GetValue(explicitOption),
                parseResult.GetValue(parentIdOption),
                parseResult.GetValue(includeChildrenOption));
```

- [ ] **Step 5: Document the filter interaction in help text**

Replace the existing `AddHelpSection("Notes", ...)` call in `CreateListCommand` with:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Filters are case-insensitive exact matches, not substrings: --edition 5",
            "does not match 5e. They test stored metadata, which the scanner leaves",
            "empty — a freshly imported system matches no filter at all.",
            "",
            "Container children are hidden by default, and that check runs BEFORE",
            "every filter. On a library using containers the metadata lives on the",
            "children, so --edition/--genre/--family/--license return [] with exit 0",
            "unless --include-children is also passed. --parent-id lists one",
            "container's children and implies them.");
```

Update the examples to demonstrate it:

```csharp
        command.AddExamples(
            "grimoire-cli systems list",
            "grimoire-cli systems list --sort book_count --desc",
            "grimoire-cli systems list --include-children --family Shadowrun",
            "grimoire-cli systems list --parent-id <container-id>",
            "grimoire-cli systems list --explicit false");
```

The `--edition 6` example is removed: under containers the edition is `6 DE`, and an example that returns `[]` is worse than no example.

- [ ] **Step 6: Run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all pass. `HelpOutputTests` may assert on the old Notes text — if it fails, update the expectation to match the new wording; do not weaken the assertion.

- [ ] **Step 7: Commit**

```bash
git add src/GrimoireCli/Services/SystemsService.cs src/GrimoireCli/Commands/SystemsCommand.cs \
  tests/GrimoireCli.Tests/Commands/
git commit -m "feat: add --parent-id and --include-children to systems list"
```

---

## Task 5: Restructure the fixture library onto containers

**Files:**
- Modify: `docker/seed.sh`

**Interfaces:**
- Consumes: a stack on `hunterreadca/grimoire:1.5.5` (Task 2).
- Produces: 7 top-level systems, 9 children, 16 total, 15 books. Task 6 asserts these exact numbers.

Target tree, with every edition-bearing system becoming a container child. `one-page-rpgs` is **not** given a marker: its name is already a reserved slug, so v1.5.5 makes it a one-page container on its own.

```
books/
├── Shadowrun/                     .parent-system-container   → 4 DE, 5 DE, 6 DE
├── Das Schwarze Auge/             .parent-system-container   → 5 DE
├── The Dark Eye/                  .parent-system-container   → 5 EN
├── !!Dungeons & Dragons/          .parent-system-container   → 5e EN
├── Vampire The Masquerade/        .parent-system-container   → 5 EN
├── Fixture Explicit RPG (nsfw)/   flat
└── one-page-rpgs/                 reserved slug → one-page container
```

- [ ] **Step 1: Add a `container` helper and restructure the fixture calls**

In `docker/seed.sh`, immediately after the existing `book()` function, add:

```bash
container() {  # container <folder> — mark a folder as a parent-system container
  mkdir -p "$LIBRARY/books/$1"
  touch "$LIBRARY/books/$1/.parent-system-container"
}
```

Replace the whole block of `book "..."` calls (currently lines 58-70) with:

```bash
container "Shadowrun"
container "Das Schwarze Auge"
container "The Dark Eye"
container "!!Dungeons & Dragons"
container "Vampire The Masquerade"

book "Shadowrun/6 DE"                  core         "SR6 Grundregelwerk"      12
book "Shadowrun/6 DE"                  core         "SR6 Kreuzfeuer"           8
book "Shadowrun/6 DE"                  supplements  "SR6 Strassengrimoire"     6
book "Shadowrun/5 DE"                  core         "SR5 Grundregelwerk"      10
book "Shadowrun/5 DE"                  core         "SR5 Datenpfade"           5
book "Shadowrun/4 DE"                  core         "SR4 Grundregelwerk"       7
book "!!Dungeons & Dragons/5e EN"      core         "Players Handbook"        14
book "!!Dungeons & Dragons/5e EN"      adventures   "Lost Mine of Phandelver"  9
book "Das Schwarze Auge/5 DE"          core         "DSA5 Regelwerk"          11
book "Das Schwarze Auge/5 DE"          core         "DSA5 Aventurien"          4
book "The Dark Eye/5 EN"               core         "TDE5 Core Rules"         11
book "Vampire The Masquerade/5 EN"     core         "V5 Corebook"             13
book "Fixture Explicit RPG (nsfw)"     core         "Fixture RPG Core Rules"   3
```

The container generates `"<container> <folder>"`, so every resulting system name is byte-identical to the pre-restructure fixture — `Das Schwarze Auge/5 DE` is still `Das Schwarze Auge 5 DE`. That is what keeps the name-keyed PATCH lookups below working.

- [ ] **Step 2: Correct the one-page comment**

Replace the comment block above the `one-page-rpgs` fixtures (currently lines 73-75) with:

```bash
# one-page-rpgs is a reserved slug, so v1.5.5 treats it as a one-page CONTAINER
# with no marker file: each loose PDF becomes its own single-book system, named
# by prettify_collection_name — which capitalises any word with no uppercase in
# it, so "Lasers and Feelings" indexes as "Lasers And Feelings". On v1.5.4 the
# same folder produced ONE system with its subfolders as categories. A loose PDF
# at the books root is still skipped entirely (scan.py requires a directory).
```

- [ ] **Step 3: Trim the PATCH bodies**

`edition` and `parent_system` are folder-derived under a container (`scan.py:490`, `scan.py:331`). Leaving them in the PATCH would overwrite derived values with hand-set ones and hide whether derivation works. Replace the six `patch_system` calls (currently lines 112-117) with:

```bash
patch_system "Shadowrun 6 DE" '{"system_family":"Shadowrun","genres":["Cyberpunk"],"year":2019,"publishers":[{"name":"Pegasus Spiele","url":""}]}'
patch_system "Shadowrun 5 DE" '{"system_family":"Shadowrun","genres":["Cyberpunk"],"year":2013,"publishers":[{"name":"Pegasus Spiele","url":""}]}'
patch_system "Dungeons & Dragons 5e EN" '{"system_family":"D&D","genres":["Fantasy"],"license":"OGL","year":2014,"publishers":[{"name":"Wizards of the Coast","url":""}]}'
patch_system "Das Schwarze Auge 5 DE" '{"system_family":"The Dark Eye","genres":["Fantasy"],"year":2015,"publishers":[{"name":"Ulisses Spiele","url":""}]}'
patch_system "The Dark Eye 5 EN" '{"system_family":"The Dark Eye","genres":["Fantasy"],"year":2016,"publishers":[{"name":"Ulisses North America","url":""}]}'
patch_system "Vampire The Masquerade 5 EN" '{"system_family":"World of Darkness","genres":["Horror"],"year":2018,"publishers":[{"name":"Renegade Game Studios","url":""}]}'
```

Also update the comment above `patch_system` so it states what still needs a PATCH:

```bash
# 5. Apply the metadata folders cannot express. edition and parent_system are
#    folder-derived under a container, so they are deliberately absent here —
#    patching them would mask whether derivation works. system_family has no
#    folder route on v1.5.5 (.system-family-container is main-only). Shadowrun
#    4 DE is left raw: it mirrors a fresh import and is the fixture the future
#    metadata commands will target.
```

- [ ] **Step 4: Update the closing assertion**

The system lookup inside `patch_system` reads `/api/systems`, which now hides children — so it must ask for them. Change the `id=$(...)` line in `patch_system` to:

```bash
  id=$(curl -sf "$SERVER/api/systems?include_children=true" -H "$AUTH" \
       | jq -r --arg n "$name" '.[] | select(.name == $n) | .id')
```

Then replace the final count block (currently lines 119-121) with:

```bash
TOP=$(curl -sf "$SERVER/api/systems" -H "$AUTH" | jq 'length')
ALL=$(curl -sf "$SERVER/api/systems?include_children=true" -H "$AUTH" | jq 'length')
say "seed complete — $TOP top-level systems, $ALL including children"
[ "$TOP" -eq 7 ] || fail "expected 7 top-level systems, got $TOP"
[ "$ALL" -eq 16 ] || fail "expected 16 systems including children, got $ALL"
```

- [ ] **Step 5: Reset the stack and run the seed**

A re-seed is not enough — renaming fixture folders leaves a stale `is_explicit` on the old rows, which only a database reset clears.

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

Expected: `seed complete — 7 top-level systems, 16 including children`, and no `SEED FAIL`.

- [ ] **Step 6: Verify the derived values by hand**

```bash
TOKEN=$(curl -sf -X POST http://host.docker.internal:9481/api/auth/login \
  -H 'content-type: application/json' -d '{"username":"admin","password":"admin"}' | jq -r .token)
curl -sf "http://host.docker.internal:9481/api/systems?include_children=true" \
  -H "Authorization: Bearer $TOKEN" \
  | jq -r '.[] | "\(.name)\t\(.container_kind)\t\(.edition)\t\(.parent_name)\t\(.child_count)"' | sort
```

Expected, among the 16 rows:

- `Shadowrun` with `container_kind=parent`, empty edition, `child_count=3`
- `Shadowrun 6 DE` with empty `container_kind`, `edition=6 DE`, `parent_name=Shadowrun`
- `one-page-rpgs` with `container_kind=one-page`, `child_count=2`
- `Honey Heist` and `Lasers And Feelings` with `parent_name=one-page-rpgs`
- `Fixture Explicit RPG` with empty `container_kind` and empty `parent_name`

If `Shadowrun 6 DE` does not appear, or a suffixed variant like `Shadowrun 6 DE (2)` does, stop: the folder name failed to reproduce the expected system name, and adoption cannot be retried once wrong rows exist.

- [ ] **Step 7: Commit**

```bash
git add docker/seed.sh
git commit -m "test: restructure the fixture library onto system containers"
```

---

## Task 6: Update the smoke test

**Files:**
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: the fixture set from Task 5, and the flags from Task 4.
- Produces: nothing.

Every filter assertion gains `--include-children`, because the child-hiding check runs before the filters and all filterable metadata now lives on children.

- [ ] **Step 1: Update the expected system count**

Change line 107 and add a companion:

```bash
EXPECTED_SYSTEMS=7
EXPECTED_ALL_SYSTEMS=16
```

Update the comment above them to read:

```bash
# --- seeded data -------------------------------------------------------------
# Requires docker/seed.sh to have run. Counts mirror the fixture set defined
# there; changing a fixture must change these numbers. EXPECTED_SYSTEMS is the
# top-level listing: container children are hidden unless asked for.
```

- [ ] **Step 2: Add the container assertions**

After the existing `ok "systems list returns $EXPECTED_SYSTEMS systems"` line, insert:

```bash
syslist --include-children
[ "$COUNT" -eq "$EXPECTED_ALL_SYSTEMS" ] \
  || fail "--include-children should return $EXPECTED_ALL_SYSTEMS, got $COUNT"
ok "--include-children returns $EXPECTED_ALL_SYSTEMS systems"

# The container is a shelf of systems: kind "parent", three editions, no books.
syslist
CONTAINER=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun")')
[ "$(echo "$CONTAINER" | jq -r .container_kind)" = "parent" ] \
  || fail "Shadowrun should be a parent container"
[ "$(echo "$CONTAINER" | jq -r .child_count)" -eq 3 ] \
  || fail "Shadowrun should hold 3 editions"
ok "the Shadowrun container reports kind=parent and child_count=3"

# A child carries the folder-derived edition and a link back to its container.
syslist --include-children
CHILD=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 6 DE")')
[ -n "$CHILD" ] || fail "Shadowrun 6 DE missing — the container did not adopt it"
[ "$(echo "$CHILD" | jq -r .edition)" = "6 DE" ] \
  || fail "edition should be folder-derived as '6 DE', got '$(echo "$CHILD" | jq -r .edition)'"
[ "$(echo "$CHILD" | jq -r .parent_name)" = "Shadowrun" ] \
  || fail "parent_name should be Shadowrun"
ok "a container child carries a derived edition and parent_name"

# --parent-id selects exactly one container's children.
CONTAINER_ID=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun") | .id')
syslist --parent-id "$CONTAINER_ID"
[ "$COUNT" -eq 3 ] || fail "--parent-id should return the 3 Shadowrun editions, got $COUNT"
ok "--parent-id lists one container's children"

# The reserved slug one-page-rpgs becomes a one-page container with no marker
# file, and each loose PDF becomes its own system.
syslist
ONEPAGE=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "one-page-rpgs")')
[ "$(echo "$ONEPAGE" | jq -r .container_kind)" = "one-page" ] \
  || fail "one-page-rpgs should be a one-page container"
[ "$(echo "$ONEPAGE" | jq -r .child_count)" -eq 2 ] \
  || fail "one-page-rpgs should hold 2 games"
syslist --include-children
echo "$LIST_JSON" | jq -e '.[] | select(.name == "Lasers And Feelings")' >/dev/null \
  || fail "expected 'Lasers And Feelings' — prettify_collection_name capitalises 'and'"
ok "one-page-rpgs is a container holding 2 single-book systems"
```

- [ ] **Step 3: Pin the filter/child interaction**

Immediately before the existing `syslist --genre Cyberpunk` line, insert:

```bash
# The child-hiding check runs BEFORE the filters, so a filter on metadata that
# only children carry returns [] with exit 0 — indistinguishable from a genuine
# miss. This asserts the trap exists rather than working around it.
syslist --genre Cyberpunk
[ "$COUNT" -eq 0 ] \
  || fail "--genre without --include-children should return 0, got $COUNT"
ok "a filter without --include-children returns [] on a containerised library"
```

- [ ] **Step 4: Add `--include-children` to every filter assertion**

Replace the existing filter block (currently lines 157-184) with:

```bash
syslist --include-children --genre Cyberpunk
[ "$COUNT" -eq 2 ] || fail "--genre Cyberpunk should match 2"
syslist --include-children --edition "6 DE"
[ "$COUNT" -eq 1 ] || fail "--edition '6 DE' should match 1"
syslist --include-children --edition "5 DE"
[ "$COUNT" -eq 2 ] || fail "--edition '5 DE' should match 2 across families"
syslist --include-children --edition "5 EN"
[ "$COUNT" -eq 2 ] || fail "--edition '5 EN' should match 2 across families"
syslist --include-children --license OGL
[ "$COUNT" -eq 1 ] || fail "--license OGL should match 1"
syslist --include-children --genre nope
[ "$COUNT" -eq 0 ] || fail "an unmatched filter should return []"
ok "filters narrow the result set"

# Shadowrun 4 DE is seeded raw, so a family filter must exclude it.
syslist --include-children --family Shadowrun
[ "$COUNT" -eq 2 ] \
  || fail "--family Shadowrun should match 2, not the raw Shadowrun 4 DE"
ok "systems with empty metadata are excluded by filters"

# The (nsfw) folder marker, not a PATCH, is what sets this. The system is flat,
# so it needs no --include-children.
syslist --explicit true
EXPLICIT=$(echo "$LIST_JSON" | jq -r '.[].name')
[ "$EXPLICIT" = "Fixture Explicit RPG" ] \
  || fail "--explicit true returned '$EXPLICIT'"
ok "--explicit true matches the nsfw-marked system"

# Filter values with an ampersand must survive URL encoding. parent_system is
# now folder-derived from the container name, with its !! sort prefix stripped.
syslist --include-children --parent-system "Dungeons & Dragons"
[ "$COUNT" -eq 1 ] || fail "a filter value containing '&' did not round-trip"
ok "ampersand in a filter value round-trips"
```

- [ ] **Step 5: Fix the descending-sort assertion**

Containers hold no books directly, so a top-level `book_count` sort is almost all zeros. Change the sort block to run over children:

```bash
syslist --include-children --sort book_count --desc
COUNTS=$(echo "$LIST_JSON" | jq '[.[].book_count]')
echo "$COUNTS" | jq -e '. == (. | sort | reverse)' >/dev/null \
  || fail "--sort book_count --desc was not descending: $COUNTS"
ok "--sort book_count --desc is ordered"
```

- [ ] **Step 6: Run the smoke test against the Debug binary**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: every check prints `ok:`, exit 0. If a count is off by one, print the actual listing before changing the expected number — the fixture is the source of truth, not the assertion.

- [ ] **Step 7: Run it against the published AOT binary**

This is the only check that catches a missing `[JsonSerializable]` registration.

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
./publish/grimoire-cli self-test
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
rm -rf publish/
```

Expected: identical output to Step 6.

- [ ] **Step 8: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: assert container behaviour in the smoke test"
```

---

## Task 7: Update the documentation set

**Files:**
- Modify: `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md`, `docs/roadmap.md`, `README.md`
- Modify: `docker/seed.sh` header comment (the v1.5.4 citation)
- Modify: `docs/testing.md:105` (the v1.5.4 citation)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Refresh the OpenAPI snapshot**

```bash
curl -sf "http://host.docker.internal:9481/api/openapi.json" -o temp/grimoire-openapi.json
jq -r '.info.version' temp/grimoire-openapi.json
jq '.paths | length' temp/grimoire-openapi.json
```

Expected: `1.5.5`, and a path count higher than the 130 recorded for 1.5.4.

- [ ] **Step 2: Regenerate the coverage doc**

`IMPLEMENTED` in `tools/generate-api-coverage.py:32-37` is unchanged — this change adds no endpoints. The 29 new routes appear as uncovered.

```bash
python3 tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: the table gains rows for the add-on, bulk, cover and metadata-lookup tags, and the reference line updates to 1.5.5. The doc is generated — never hand-edit it.

- [ ] **Step 3: Correct the one-page claims in `docs/grimoire-api-notes.md`**

The file lists `micro-rpgs` among four claims it corrected as fabricated. That was right for v1.5.4 and is wrong from v1.5.5, where upstream #262 added it. Find the one-page section and rewrite it to:

```markdown
### One-page collections

Verified against v1.5.5 (`backend/indexer/constants.py:95-105`,
`backend/indexer/categories.py::detect_container_kind`).

The reserved slugs are `one-page-rpgs`, `single-page-rpgs`, `one-shot-rpgs` and
`micro-rpgs`. **`micro-rpgs` is new in v1.5.5** (upstream #262) — it did not
exist in v1.5.4, where recording it as fabricated was correct.

A reserved slug **declares a one-page container on its own**, with no marker
file: `detect_container_kind` returns `"one-page"` for it. Each loose file
directly under such a folder becomes its own single-book system, named by
`prettify_collection_name`, which capitalises any word containing no uppercase
letter — so `Lasers and Feelings.pdf` indexes as `Lasers And Feelings`.

Marker files are tested first, so a `.parent-system-container` in a
reserved-slug folder overrides the one-page flavour.

On v1.5.4 the same folder produced one system whose immediate subfolders became
category labels. Any claim about this behaviour must name a version.
```

- [ ] **Step 4: Add the container mechanics to `docs/grimoire-api-notes.md`**

Append a new section:

```markdown
### System containers

Verified against v1.5.5. Every citation is a file in `temp/grimoire`.

- A folder becomes a container via a `.parent-system-container` /
  `.one-page-container` marker file, a `(parent-system)` / `(one-page)` name
  suffix, or a reserved one-page slug (`indexer/categories.py::detect_container_kind`).
- A child's display name is `"<container> <folder>"` (`indexer/scan.py:443`), so
  `Shadowrun` + `6 DE` is `Shadowrun 6 DE`.
- `edition` is the child folder name **verbatim** (`indexer/scan.py:490`): a
  folder called `6 DE` yields edition `6 DE`, not `6`.
- `parent_system` is still a free-text column, auto-set to the container's name
  on child creation (`indexer/scan.py:331`). Both it and the real `parent_id`
  foreign key are returned.
- Sort prefixes are stripped before the container name is used
  (`indexer/scan.py:188`), so `!!Dungeons & Dragons` yields `Dungeons & Dragons`.
- `system_depth` is the constant 3 (`indexer/scan.py:504`), so a category folder
  must sit exactly one level below the edition folder. Nesting containers is
  unavailable until `.system-family-container` reaches a tagged release.
- **`GET /api/systems` hides container children before applying any filter**
  (`routers/systems/core.py:78-95`). A filter on metadata only children carry
  returns `[]` with exit 0 unless `include_children=true` is also sent.
- `GET /api/systems/{id}` returns the summary shape plus `books` **and**
  `children` (`routers/systems/core.py:186-196`).
- `Book.category` is assigned in exactly two places: the new-book insert
  (`indexer/scan.py:782`) and a re-home, guarded by
  `if existing.game_system_id != system.id` (`indexer/scan.py:730`). An ordinary
  rescan does **not** re-derive it, so a `PATCH category` holds — but converting
  a folder into a container re-homes every book in it and does reset it.
```

- [ ] **Step 5: Update `README.md`**

The Commands table line 83 must list the two new flags:

```markdown
| `systems list [--sort name\|book_count\|page_count\|year] [--desc] [--genre <g>] [--family <f>] [--parent-system <p>] [--edition <e>] [--license <l>] [--explicit true\|false] [--parent-id <id>] [--include-children]` | List all game systems |
```

- [ ] **Step 6: Update `docs/roadmap.md`**

Add under the open-work section:

```markdown
### Reopened by Grimoire v1.5.5

- **Bulk endpoints shipped.** `POST /api/{books,systems,maps,tokens,audio}/bulk`
  and `/bulk/tags` are released, not unreleased `main` work as
  `docs/plans/2026-08-06-login-and-smoke-test.md` recorded. The parked metadata
  command design question — typed flags plus a `--json` escape hatch, versus a
  raw-JSON body — now has a third option, and needs deciding before that command
  is built.
- **29 new routes are uncovered**: 13 bulk, 7 add-on management, 6 metadata
  lookup (`metadata-sources` / `metadata-search` / `metadata-fetch` on books and
  systems), 3 system cover. See `docs/grimoire-api-coverage.md`.
- **Metadata add-ons** (`backend/addons/`) fetch server-side with a per-field
  diff review, and ship with DriveThruRPG and TTRPG Wiki sources. They cover
  `isbn`, `artists` and `genres` on books and `system_family` on systems — work
  previously assumed to be CLI-only.
```

- [ ] **Step 7: Fix the remaining v1.5.4 citations**

Two comments cite v1.5.4 for behaviour re-verified at v1.5.5:

- `docker/seed.sh:18` — change `in temp/grimoire @ v1.5.4` to `in temp/grimoire @ v1.5.5`, after confirming the line numbers it cites still hold:
  ```bash
  sed -n '225,240p' temp/grimoire/backend/indexer/scan.py
  ```
  If the cited line numbers moved, update them to the v1.5.5 positions rather than leaving a stale citation.
- `docs/testing.md:105` — same change, same verification.

- [ ] **Step 8: Run the full verification suite**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

Expected: all four clean.

- [ ] **Step 9: Commit**

```bash
git add docs/ README.md tools/generate-api-coverage.py docker/seed.sh temp/grimoire-openapi.json
git commit -m "docs: record the 1.5.5 container mechanics and coverage"
```

Note: `temp/` is gitignored, so `temp/grimoire-openapi.json` will not stage. That is expected — drop it from the `git add` if git complains.

---

## Task 8: Commit the spec and plan, and open the PR

**Files:**
- Add: `docs/specs/2026-08-10-release-skill-and-grimoire-1.5.5-design.md` (already written, uncommitted)
- Add: `docs/plans/2026-08-10-release-skill-and-grimoire-1.5.5.md` (this file)

- [ ] **Step 1: Commit the design documents**

Per `CLAUDE.md`, spec and plan commits are held until the implementation branch exists so design and delivery are reviewed as one unit.

```bash
git add docs/specs/2026-08-10-release-skill-and-grimoire-1.5.5-design.md \
        docs/plans/2026-08-10-release-skill-and-grimoire-1.5.5.md
git commit -m "docs: add the 1.5.5 and release-skill design and plan"
```

- [ ] **Step 2: Final pre-PR verification**

All four gates, from a clean stack, per `CLAUDE.md`. The library tree must be
removed alongside the database: the boot scan indexes whatever is on disk, so
a database-only reset leaves a stale folder that survives as an `is_missing`
row and still counts toward `book_count`.

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

Expected: four clean runs. Do not open the PR on a partial pass.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin feat/grimoire-1.5.5-and-release-skill
gh pr create --base main \
  --title "feat: target grimoire 1.5.5 and port the release skill" \
  --body "$(cat <<'EOF'
Targets Grimoire v1.5.5 and ports abs-cli's gated release skill.

## Grimoire 1.5.5

- `MinSupportedVersion` / `MaxTestedVersion` move to 1.5.5; compose tag and the
  `temp/grimoire` reference clone are re-pinned.
- `GameSystemSummary` gains the seven container fields (`has_cover`,
  `container_kind`, `parent_id`, `parent_name`, `parent_is_one_page`,
  `name_is_custom`, `child_count`); `GameSystemDetail` gains `children`.
- `systems list` gains `--parent-id` and `--include-children`.

## Fixtures

The fixture library moves onto system containers, mirroring the grammar the real
library is adopting: every system with an edition token becomes a container
child. 7 top-level systems, 9 children, 15 books.

`one-page-rpgs` is a reserved slug, so v1.5.5 converts it to a one-page
container with no marker file and no edit from us — each loose PDF becomes its
own system. The old fixture would have broken on upgrade regardless.

## The trap this surfaces

`GET /api/systems` hides container children **before** applying any filter. All
filterable metadata now lives on children, so `--edition "6 DE"` returns `[]`
with exit 0 unless `--include-children` is passed. Documented in the `list` help
text and asserted in the smoke test.

## Release skill

`.claude/skills/release/SKILL.md`, ported with an added step reconciling the
supported server range, our preflight, and a bare-`--version` assertion.
No release is cut here.
EOF
)"
```

- [ ] **Step 4: Watch CI to a terminal state**

A PR is done at "all checks green", not at "PR open".

```bash
gh pr checks --watch
```

Report the result without being asked. If a check fails, `gh run view <run-id> --log-failed`, fix on the branch, push, and re-watch.

- [ ] **Step 5: Present the PR URL as a clickable link**

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: §3 release skill → Task 1; §4.1 range and §4.4 pins → Task 2; §4.2 models → Task 3; §4.3 command surface → Task 4; §5 fixtures and §5.2 seed → Task 5; §5.4 smoke test → Task 6; §6 documentation → Task 7. Spec §7 (out of scope) is enforced by the Global Constraints. Spec §9's open question is deferred by Task 7 Step 6, which records it on the roadmap rather than answering it.

**Type consistency.** `ListAsync`'s two new parameters are `string? parentId, bool includeChildren`, in that order and last, in both Task 4 Step 3 (definition) and Step 4 (call site). The DTO property names in Task 3 Step 3 match the assertions in Task 3 Step 1 and the `jq` keys in Task 6 Steps 2–3. The fixture counts 7 / 9 / 16 / 15 are identical in spec §5.3, Task 5 Step 4, and Task 6 Step 1.

**Known ordering dependency.** Task 5 must precede Task 6 — the smoke test asserts against the restructured fixtures. Task 2 must precede both, since containers do not exist on the 1.5.4 image. Tasks 1 and 3 are independent of everything else and may run in any order.
