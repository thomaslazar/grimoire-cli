# `logs` Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a top-level `grimoire-cli logs` command reading `GET /api/logs`, so an agent can diagnose the background work that other commands only report as started.

**Architecture:** One command file and one service, matching the one-file-per-command-group rule. The service wraps the generated Kiota builder; the command declares four query flags and an admin role tag. No polling loop — the command exposes the server's `after_seq` cursor and the help text teaches the caller to advance it.

**Tech Stack:** C# / .NET 10, System.CommandLine, xUnit, the generated Kiota client under `src/GrimoireCli/Generated/`.

**Spec:** [docs/specs/2026-09-15-logs-command-design.md](../specs/2026-09-15-logs-command-design.md)
**Issue:** [#45](https://github.com/thomaslazar/grimoire-cli/issues/45)
**Branch:** `feat/logs-command` (already exists, spec already committed)

## Global Constraints

- **Thin pass-through.** One command, one endpoint. No client-side polling, no derived warnings, no reshaping of the response.
- **JSON in, JSON out.** stdout is the server's bytes unmodified via `ConsoleOutput.WriteRawJson`; everything else goes to stderr.
- **Run `dotnet format GrimoireCli.sln`** after writing or modifying any C# file. CI fails on `--verify-no-changes`.
- **No unnecessary blank lines** inside method bodies: none between consecutive `AddX` calls, none before `return` after setup calls, none between consecutive variable declarations of the same kind.
- **The build has 0 warnings today.** Keep it that way — in particular the generated `Level` query property is `[Obsolete]`, so use `LevelAsGetLevelQueryParameterType`.
- **Conventional Commits**, imperative, lowercase, no period, ~72 chars. No `Co-Authored-By`, no generated-with attribution.
- **Comment what the code does or why it must be this way** — never what was deliberately left out.

## File Structure

| File | Responsibility |
|---|---|
| `src/GrimoireCli/Services/LogsService.cs` | **Create.** One method wrapping `GET /api/logs`; owns the admin permission hint and the level enum parse. |
| `src/GrimoireCli/Commands/LogsCommand.cs` | **Create.** Flag declarations, validation, role tag, help sections, action. |
| `src/GrimoireCli/Program.cs` | **Modify.** One registration line. |
| `tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs` | **Create.** Surface and help-claim assertions. |
| `docker/smoke-test.sh` | **Modify.** One block at the end, before the final echo. |
| `README.md` | **Modify.** One Commands-table row. |
| `tools/generate-api-coverage.py` | **Modify.** One `IMPLEMENTED` entry. |
| `docs/grimoire-api-coverage.md` | **Regenerate.** Never hand-edit. |
| `docs/grimoire-api-notes.md` | **Modify.** New `## Logs` section. |
| `docs/roadmap.md` | **Modify.** Remove the shipped item. |

---

### Task 1: Service, command and registration

**Files:**
- Create: `src/GrimoireCli/Services/LogsService.cs`
- Create: `src/GrimoireCli/Commands/LogsCommand.cs`
- Modify: `src/GrimoireCli/Program.cs` (after the `SearchCommand` line, keeping the existing grouping)
- Test: `tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint, string? notFoundHint, TimeSpan?)`; `CommandHelper.BuildClient(string? serverOverride)`; `OptionHelpers.Choice(string, string, string[])`; `OptionHelpers.Range(string, string, int min, int? max)`; `ConsoleOutput.WriteRawJson(string)`.
- Produces: `LogsService.ReadAsync(string? level, int? limit, int? offset, int? afterSeq) → Task<string>`; `LogsCommand.Create() → Command`. Task 2 edits the same command file; Task 3 invokes the built binary.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LogsCommandTests
{
    private static string Help(bool full = false) =>
        HelpRenderer.Render(LogsCommand.Create(), ["logs"], full);

    [Fact]
    public void LogsParsesWithNoArguments()
    {
        Assert.Empty(LogsCommand.Create().Parse([]).Errors);
    }

    [Fact]
    public void LogsIsALeafWithNoSubcommands()
    {
        Assert.Empty(LogsCommand.Create().Subcommands);
    }

    // The route is require_admin, so the tag and the 403 message have to agree.
    [Fact]
    public void LogsDeclaresTheAdminRole()
    {
        var output = Help();
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("critical")]
    public void LogsAcceptsEveryServerLevel(string level)
    {
        Assert.Empty(LogsCommand.Create().Parse(["--level", level]).Errors);
    }

    // A level the server does not declare would 422 after a paid round-trip.
    [Fact]
    public void LogsRejectsAnUnknownLevel()
    {
        Assert.NotEmpty(LogsCommand.Create().Parse(["--level", "trace"]).Errors);
    }

    // The server declares limit as Query(200, ge=1, le=20000) and 422s outside
    // it; rejecting here saves the round-trip.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("20001")]
    public void LogsRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(LogsCommand.Create().Parse(["--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("20000")]
    public void LogsAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(LogsCommand.Create().Parse(["--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("--offset")]
    [InlineData("--after-seq")]
    public void LogsRejectsANegativeCursor(string flag)
    {
        Assert.NotEmpty(LogsCommand.Create().Parse([flag, "-1"]).Errors);
    }

    [Theory]
    [InlineData("--limit")]
    [InlineData("--offset")]
    [InlineData("--after-seq")]
    public void LogsReportsANonNumericValueAsAParseError(string flag)
    {
        Assert.NotEmpty(LogsCommand.Create().Parse([flag, "abc"]).Errors);
    }

    [Fact]
    public void LogsShowsTheResponseShapeWithTheCursorFields()
    {
        var output = Help(full: true);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"entries\":", output);
        Assert.Contains("\"max_seq\":", output);
        Assert.Contains("\"total\":", output);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LogsCommandTests
```

Expected: compile error — `LogsCommand` does not exist.

- [ ] **Step 3: Write the service**

Create `src/GrimoireCli/Services/LogsService.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The single log read. Guarded by require_admin (routers/logs/core.py), so the
/// send names a permissionHint. The route takes no path segment, so there is no
/// notFoundHint to give.
/// </summary>
public class LogsService
{
    private const string AdminHint = "the admin role";

    private readonly GrimoireApiClient _client;

    public LogsService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// GET /api/logs. The level is sent through the generated enum rather than
    /// the string property beside it, which Kiota marks obsolete.
    /// </summary>
    public async Task<string> ReadAsync(string? level, int? limit, int? offset, int? afterSeq)
    {
        var info = _client.Api.Api.Logs.ToGetRequestInformation(c =>
        {
            if (level is not null)
                c.QueryParameters.LevelAsGetLevelQueryParameterType =
                    Enum.Parse<Generated.Api.Logs.GetLevelQueryParameterType>(level, true);
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
            c.QueryParameters.AfterSeq = afterSeq;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }
}
```

- [ ] **Step 4: Write the command**

Create `src/GrimoireCli/Commands/LogsCommand.cs`. Leave the `Notes` section out — Task 2 adds it:

```csharp
using System.CommandLine;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class LogsCommand
{
    private static readonly string[] Levels = ["debug", "info", "warning", "error", "critical"];

    public static Command Create()
    {
        var levelOption = OptionHelpers.Choice("--level", "Minimum level to return; default info", Levels);
        var limitOption = OptionHelpers.Range("--limit", "Entries to return; default 200, max 20000", 1, 20000);
        var offsetOption = OptionHelpers.Range("--offset", "Entries to skip from the newest end", 0);
        var afterSeqOption = OptionHelpers.Range("--after-seq", "Return only entries newer than this seq", 0);
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
        var command = new Command("logs", "Read the server's application log")
        {
            levelOption, limitOption, offsetOption, afterSeqOption, serverOption
        };
        command.AddRoleRequired("admin");
        command.AddExamples(
            "grimoire-cli logs --level error",
            "grimoire-cli logs --after-seq 1423 --level warning");
        command.AddResponseExample<Generated.Models.LogsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
            var result = await new LogsService(client).ReadAsync(
                parseResult.GetValue(levelOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(afterSeqOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 5: Register the command**

In `src/GrimoireCli/Program.cs`, add one line immediately after the `SearchCommand` registration:

```csharp
rootCommand.Subcommands.Add(LogsCommand.Create());
```

- [ ] **Step 6: Format, build, and run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LogsCommandTests
```

Expected: build succeeds with **0 warnings**, all `LogsCommandTests` pass. A `CS0618` obsolete warning means Step 3 used `QueryParameters.Level` instead of `LevelAsGetLevelQueryParameterType`.

- [ ] **Step 7: Confirm the command is reachable**

```bash
dotnet run --project src/GrimoireCli -- logs --help
```

Expected: the help renders with `Role required: admin` and the five flags. `--level` must list its own five values without the description repeating them.

- [ ] **Step 8: Commit**

```bash
git add src/GrimoireCli/Services/LogsService.cs src/GrimoireCli/Commands/LogsCommand.cs \
        src/GrimoireCli/Program.cs tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs
git commit -m "feat: add the logs command"
```

---

### Task 2: Help text

The caveats are the deliverable here. Every line is a measured fact from the spec's "Verified server behaviour", and each changes what a caller reads or does.

**Files:**
- Modify: `src/GrimoireCli/Commands/LogsCommand.cs` (add a `Notes` section after `AddRoleRequired`)
- Test: `tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs` (append)

**Interfaces:**
- Consumes: `LogsCommand.Create()` from Task 1; `HelpExtensions.AddHelpSection(this Command, string title, HelpSectionPosition position, params string[] lines)`.
- Produces: nothing new. Task 3 asserts the same behaviours against a live server.

- [ ] **Step 1: Write the failing tests**

Append to `LogsCommandTests.cs`, inside the class:

```csharp
    // The buffer is fed at DEBUG whatever LOG_LEVEL is set to, so --level debug
    // returns detail an operator cannot see in docker logs. Issue #45 recorded
    // this backwards, which is why it is pinned.
    [Fact]
    public void LogsSaysDebugIsAvailableRegardlessOfServerLogLevel()
    {
        var output = Help();
        // Not "20000": --limit's own description carries that number too, so the
        // assertion would pass with no Notes section at all.
        Assert.Contains("Ring buffer", output);
        Assert.Contains("LOG_LEVEL", output);
    }

    // Measured: eight info entries at seq [1,2,7,9,10,100,102,103] answered
    // limit=2 with [102,103] and limit=2&offset=2 with [10,100].
    [Fact]
    public void LogsSaysHowAPageIsSelectedAndOrdered()
    {
        var output = Help();
        Assert.Contains("newest end", output);
        Assert.Contains("oldest-first", output);
    }

    // Measured: after_seq=100 and after_seq=100&offset=2 returned the same page.
    [Fact]
    public void LogsSaysOffsetIsIgnoredWithAfterSeq()
    {
        Assert.Contains("ignored when --after-seq", Help());
    }

    // Measured: level=error returned entries [] with max_seq 107. Without this,
    // a caller filtering narrowly cannot tell the cursor still advanced.
    [Fact]
    public void LogsTeachesTheCursorAndItsBufferWideScope()
    {
        var output = Help();
        Assert.Contains("max_seq", output);
        Assert.Contains("whole buffer", output);
    }

    // total is the count at that level: 107 for debug, 8 for info, 0 for error.
    [Fact]
    public void LogsSaysWhatTotalCounts()
    {
        Assert.Contains("total counts what matches --level", Help());
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LogsCommandTests
```

Expected: the five new tests FAIL on missing substrings. The Task 1 tests still pass.

- [ ] **Step 3: Add the Notes section**

In `LogsCommand.cs`, between `command.AddRoleRequired("admin");` and `command.AddExamples(`:

```csharp
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Ring buffer of the last 20000 entries; anything older is gone. DEBUG is",
            "available here whatever the server's LOG_LEVEL is set to.",
            "",
            "--level is a minimum: error returns error and critical.",
            "",
            "A page is taken from the newest end and returned oldest-first. --offset",
            "skips from the newest end too, and is ignored when --after-seq is given.",
            "",
            "To poll, pass the previous response's max_seq back as --after-seq. max_seq",
            "tracks the whole buffer rather than the filtered set, so a --level that",
            "matches nothing still advances the cursor.",
            "",
            "total counts what matches --level, not what this page holds.");
```

- [ ] **Step 4: Format and run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter LogsCommandTests
```

Expected: all `LogsCommandTests` pass.

- [ ] **Step 5: Review the rendered help, not the source**

```bash
dotnet run --project src/GrimoireCli -- logs --help
```

Read the output. Confirm no line exceeds the width the other commands wrap at (compare `dotnet run --project src/GrimoireCli -- search --help`), and that nothing restates a flag's own description or a field already visible in the response sample.

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/LogsCommand.cs tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs
git commit -m "docs: document the logs buffer, paging and cursor in help"
```

---

### Task 3: Smoke coverage

The help claims are about live behaviour, so they are asserted against a live server too.

**Files:**
- Modify: `docker/smoke-test.sh` (append a block immediately before the final `echo "smoke: all checks passed" >&2`)

**Interfaces:**
- Consumes: the script's existing `"$CLI"`, `"$WORK"`, `fail`, and `ok` helpers. The script has already logged in as `admin` by this point.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Bring up a seeded stack**

```bash
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

Skip the fixture copy only if `docker/data` already exists from an earlier run.

- [ ] **Step 2: Write the smoke block**

In `docker/smoke-test.sh`, immediately before the final `echo "smoke: all checks passed" >&2`:

```bash
# logs: the cursor contract is the whole point of the command, so the idle poll
# is asserted rather than just a non-empty page. max_seq is buffer-wide, so a
# filter that matches nothing still advances it — which is what lets a caller
# poll on --level error without losing its place.
LOGS_JSON=$("$CLI" logs --limit 5 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs exited non-zero"; }
echo "$LOGS_JSON" | jq -e '.entries | length > 0' >/dev/null \
  || fail "logs should return entries on a seeded stack: $LOGS_JSON"
echo "$LOGS_JSON" | jq -e '.max_seq > 0' >/dev/null \
  || fail "logs should report a cursor: $LOGS_JSON"
ok "logs returns a page and a cursor"

# A page is emitted oldest-first, so seq ascends within it.
echo "$LOGS_JSON" | jq -e '[.entries[].seq] == ([.entries[].seq] | sort)' >/dev/null \
  || fail "a logs page should be ordered oldest-first: $LOGS_JSON"
ok "logs orders a page oldest-first"

# level is a minimum and hierarchical, so debug totals at least as much as info.
DEBUG_TOTAL=$("$CLI" logs --level debug --limit 1 2>/dev/null | jq -r .total)
INFO_TOTAL=$("$CLI" logs --level info --limit 1 2>/dev/null | jq -r .total)
[ "$DEBUG_TOTAL" -ge "$INFO_TOTAL" ] \
  || fail "--level debug should total at least --level info: $DEBUG_TOTAL < $INFO_TOTAL"
ok "logs --level narrows the total"

# The idle poll: nothing is newer than max_seq, and the cursor does not move.
MAX_SEQ=$(echo "$LOGS_JSON" | jq -r .max_seq)
POLL_JSON=$("$CLI" logs --after-seq "$MAX_SEQ" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs --after-seq exited non-zero"; }
echo "$POLL_JSON" | jq -e '.entries == []' >/dev/null \
  || fail "--after-seq max_seq should return no entries: $POLL_JSON"
ok "logs --after-seq at the cursor returns an empty page"

"$CLI" logs --level trace >/dev/null 2>&1 \
  && fail "logs should refuse a level the server does not declare"
ok "logs refuses an unknown level"
```

- [ ] **Step 3: Run the smoke test**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed`, with the five new `ok:` lines present.

- [ ] **Step 4: Run it a second time**

```bash
bash docker/smoke-test.sh
```

Expected: identical result. The block is read-only, so it must converge — a second run that fails means an assertion depends on prior state.

- [ ] **Step 5: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the logs cursor contract in the smoke test"
```

---

### Task 4: Documentation

**Files:**
- Modify: `README.md` (Commands table)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`)
- Regenerate: `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md` (new `## Logs` section)
- Modify: `docs/roadmap.md` (remove the shipped item)

**Interfaces:**
- Consumes: a running stack, because `tools/generate-api-coverage.py` reads the spec and the router source from the container.
- Produces: nothing other tasks depend on. This is the last task.

- [ ] **Step 1: Add the README row**

In `README.md`, in the Commands table, add after the `search fields` row:

```markdown
| `logs [--level <l>] [--limit <n>] [--offset <n>] [--after-seq <n>]` | Read the server's application log (admin) |
```

- [ ] **Step 2: Mark the endpoint implemented**

In `tools/generate-api-coverage.py`, add to the `IMPLEMENTED` dict, after the `GET /api/search/fields` entry:

```python
    "GET /api/logs": "`logs` ✅",
```

- [ ] **Step 3: Regenerate the coverage table**

```bash
docker compose -f docker/docker-compose.yml up -d --wait
python3 tools/generate-api-coverage.py
git diff docs/grimoire-api-coverage.md
```

Expected: the `/api/logs` row changes from `—` to `` `logs` ✅ ``, and the covered count rises by one. Never hand-edit this file.

- [ ] **Step 4: Record the verified behaviour**

In `docs/grimoire-api-notes.md`, add a `## Logs` section (place it after `## Search`, keeping the file's existing order):

```markdown
## Logs

Read from `backend/routers/logs/core.py` and `_schemas.py` at tag `v1.6.2`, and
measured against the running 1.6.2 stack.

- **An in-memory ring buffer of 20 000 entries**, with no disk history behind it.
  Anything older is gone.
- **DEBUG is always available regardless of `LOG_LEVEL`.** The env var governs
  console output; the handler feeding this buffer is installed at DEBUG. So the
  endpoint returns detail an operator cannot see in `docker logs`.
- **`level` is a minimum and hierarchical.** On a freshly booted stack: `debug`
  totalled 107, `info` 8, `error` 0.
- **A page is taken from the newest end and returned oldest-first.** With the
  eight `info` entries at seq `[1, 2, 7, 9, 10, 100, 102, 103]`, `limit=2` gave
  `[102, 103]`, `limit=2&offset=2` gave `[10, 100]`, and `limit=3&offset=5` gave
  `[1, 2, 7]`.
- **`offset` is ignored once `after_seq` is set** — `after_seq=100` and
  `after_seq=100&offset=2` returned the identical page.
- **`max_seq` tracks the whole buffer, not the filtered set.** `level=error`
  returned `entries: []` with `max_seq: 107`, so a poll filtered to a level that
  matches nothing still advances the cursor.
- **`total` counts what matches `level`**, not what the page holds.
```

- [ ] **Step 5: Remove the shipped roadmap item**

In `docs/roadmap.md`, delete the numbered `logs` entry from `## Next` and renumber the remaining items so they run 1–6. The roadmap records intent, and this has shipped.

- [ ] **Step 6: Verify everything**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

Expected: all four clean. Then confirm no stale roadmap link remains:

```bash
grep -n "issues/45" docs/roadmap.md || echo "roadmap item removed"
```

- [ ] **Step 7: Commit**

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md \
        docs/grimoire-api-notes.md docs/roadmap.md
git commit -m "docs: record the logs command and its verified behaviour"
```

---

## Done when

- `grimoire-cli logs` reads the endpoint, with `--level`, `--limit`, `--offset`, `--after-seq` and `--server`
- the admin role tag and the 403 hint agree
- every help claim is pinned by a unit test and asserted live in the smoke test
- README, the coverage table, the API notes and the roadmap all reflect the shipped command
- all four verification commands pass, and the smoke test is idempotent
