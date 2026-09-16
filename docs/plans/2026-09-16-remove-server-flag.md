# Remove `--server` from Non-Login Commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete `--server` from the 72 commands that are not `login`, drop the now-unused flag tier from the config layer, and give `login` a `GRIMOIRE_SERVER` fallback so the environment variable works everywhere.

**Architecture:** The removal is mechanical and wide — 22 command files lose one option declaration and one argument each. It is split so the bulk edit lands first and compiles, then the dead parameters are deleted, then `login` gains its fallback. Server resolution ends as `GRIMOIRE_SERVER` > config file.

**Tech Stack:** C# / .NET 10, System.CommandLine, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-16-remove-server-flag-design.md](../specs/2026-09-16-remove-server-flag-design.md)
**Issue:** [#54](https://github.com/thomaslazar/grimoire-cli/issues/54)
**Branch:** `refactor/remove-server-flag` (exists; spec already committed)

## Global Constraints

- **`login` keeps `--server`.** It is the only command that does, and it produces the config rather than overriding it.
- **Never edit `src/GrimoireCli/Generated/`** — it is generated from the OpenAPI spec.
- **Run `dotnet format GrimoireCli.sln`** after modifying any C# file. CI fails on `--verify-no-changes`.
- **The build has 0 warnings today.** Keep it there — an unused private field or parameter will show up.
- **No unnecessary blank lines** inside method bodies: none between consecutive `AddX` calls, none before `return` after setup calls, none between consecutive variable declarations of the same kind.
- **The smoke test must be idempotent** — two consecutive runs must both pass.
- **Conventional Commits**, imperative, lowercase, no period, ~72 chars. No `Co-Authored-By`, no generated-with attribution.
- **Comments say what the code does or why it must be this way** — never what was deliberately left out.

## File Structure

| File | Responsibility |
|---|---|
| 22 files in `src/GrimoireCli/Commands/` | **Modify.** Drop one `serverOption` declaration, its entry in the command's option list, and its `serverOverride:` argument. `LoginCommand.cs` is NOT one of them. |
| `src/GrimoireCli/Commands/CommandHelper.cs` | **Modify.** Drop the `serverOverride` parameter. |
| `src/GrimoireCli/Configuration/ConfigManager.cs` | **Modify.** Drop the `flagServer` parameter from `Resolve`. |
| `src/GrimoireCli/Commands/LoginCommand.cs` | **Modify.** Add the `GRIMOIRE_SERVER` fallback. |
| `tests/.../Commands/HelpOutputTests.cs` | **Modify.** Two assertions invert. |
| `tests/.../Commands/VocabularyCommandTests.cs` | **Modify.** One assertion inverts. |
| `tests/.../Commands/MeCommandTests.cs` | **Modify.** One test inverts. |
| `tests/.../Configuration/ConfigManagerTests.cs` | **Modify.** Delete the flag-tier case. |
| `docker/smoke-test.sh` | **Modify.** Retarget two blocks, add one. |
| `README.md`, `docs/configuration.md`, `CLAUDE.md` | **Modify.** Precedence and convention. |

---

### Task 1: Remove the option from all 22 command files

The bulk mechanical change. `CommandHelper.BuildClient`'s parameter keeps its `= null` default, so every call site compiles after dropping the argument.

**Files:**
- Modify: every `*.cs` in `src/GrimoireCli/Commands/` that contains `"--server"` **except `LoginCommand.cs`** — 22 files
- Test: `tests/GrimoireCli.Tests/Commands/HelpOutputTests.cs`, `tests/GrimoireCli.Tests/Commands/VocabularyCommandTests.cs`, `tests/GrimoireCli.Tests/Commands/MeCommandTests.cs`

**Interfaces:**
- Consumes: `CommandHelper.BuildClient(string? serverOverride = null)` — call it with no arguments.
- Produces: no command except `login` declares `--server`. Task 2 deletes the now-unused parameters.

- [ ] **Step 1: Write the failing tests**

In `tests/GrimoireCli.Tests/Commands/MeCommandTests.cs`, replace the `AcceptsAServerOverride` test with:

```csharp
    // --server was removed from every command but login: the config file and
    // GRIMOIRE_SERVER are the only ways to name a server. A parse error is what
    // proves the option is gone rather than merely hidden from help.
    [Fact]
    public void RejectsAServerOverride()
    {
        var root = new RootCommand { MeCommand.Create() };
        Assert.NotEmpty(root.Parse("me --server http://x").Errors);
    }
```

In `tests/GrimoireCli.Tests/Commands/VocabularyCommandTests.cs`, in `ListParsesAndAcceptsServer`, rename it and replace its two assertions:

```csharp
    public void ListParsesAndRejectsServer(string name)
    {
        var group = Group(name);
        Assert.Empty(group.Parse(["list"]).Errors);
        Assert.NotEmpty(group.Parse(["list", "--server", "http://example.test"]).Errors);
    }
```

In `tests/GrimoireCli.Tests/Commands/HelpOutputTests.cs`, both tests assert `Assert.Contains("--server", output);`. Change each to:

```csharp
        Assert.DoesNotContain("--server", output);
```

Leave their `Assert.DoesNotContain("--token", output);` lines alone — those guard a different removed flag. Rename each test method from `..._ShowsServerOption_AndNoTokenOption` to `..._HasNoServerOrTokenOption`.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "MeCommandTests|VocabularyCommandTests|HelpOutputTests"
```

Expected: the four changed assertions FAIL — the option still parses and still renders.

- [ ] **Step 3: Remove the option from all 22 files**

In each command file except `LoginCommand.cs`, for every occurrence:

1. Delete the declaration line, which reads exactly:

```csharp
        var serverOption = new Option<string?>("--server") { Description = "Server URL override" };
```

2. Remove `serverOption` from the command's option list. It appears either alone on its own line or last in a comma-separated list — delete the trailing comma on the preceding entry when it becomes last.

3. Change the call site from:

```csharp
            var (client, _) = CommandHelper.BuildClient(serverOverride: parseResult.GetValue(serverOption));
```

to:

```csharp
            var (client, _) = CommandHelper.BuildClient();
```

Some files bind it differently (e.g. `parseResult.GetValue(serverOption)` passed into a variable first). Follow the same rule: the argument goes, the call keeps its other arguments.

Find every site with:

```bash
grep -rn '"--server"\|serverOverride:\|serverOption' src/GrimoireCli/Commands/ | grep -v LoginCommand
```

That command must return **no output** when the step is done.

- [ ] **Step 4: Format, build, and run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build succeeds with **0 warnings**, all tests pass. A warning about an unused variable means a `serverOption` declaration survived somewhere.

- [ ] **Step 5: Confirm the flag is gone and login still has it**

```bash
dotnet run --project src/GrimoireCli -- me --help | grep -c -- --server        # expect 0
dotnet run --project src/GrimoireCli -- login --help | grep -c -- --server     # expect 1
dotnet run --project src/GrimoireCli -- me --server http://x 2>&1 | head -2    # expect an unrecognised-option error
```

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands tests/GrimoireCli.Tests/Commands
git commit -m "refactor: drop --server from every command but login"
```

---

### Task 2: Delete the dead parameters

With no caller passing them, `serverOverride` and `flagServer` are flexibility nothing feeds.

**Files:**
- Modify: `src/GrimoireCli/Commands/CommandHelper.cs`
- Modify: `src/GrimoireCli/Configuration/ConfigManager.cs`
- Test: `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs`

**Interfaces:**
- Consumes: `CommandHelper.BuildClient()` called with no arguments everywhere (Task 1).
- Produces: `CommandHelper.BuildClient()` and `ConfigManager.Resolve(Func<string, string?>? envLookup = null)`. Task 3 does not use either.

- [ ] **Step 1: Delete the flag-tier test**

In `tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs`, delete the entire `ResolvePrefersFlagOverEnvAndFile` test, including its `[Fact]` attribute. `ResolvePrefersEnvOverFile` stays and now covers the top of the chain.

- [ ] **Step 2: Run the suite to confirm it still compiles**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter ConfigManagerTests
```

Expected: PASS. The deleted test was the only one passing `flagServer`.

- [ ] **Step 3: Remove the parameter from `BuildClient`**

In `src/GrimoireCli/Commands/CommandHelper.cs`, change:

```csharp
    public static (GrimoireApiClient client, AppConfig config) BuildClient(
        string? serverOverride = null)
    {
        var configManager = new ConfigManager();
        var config = configManager.Resolve(flagServer: serverOverride);
```

to:

```csharp
    public static (GrimoireApiClient client, AppConfig config) BuildClient()
    {
        var configManager = new ConfigManager();
        var config = configManager.Resolve();
```

- [ ] **Step 4: Remove the parameter from `Resolve`**

In `src/GrimoireCli/Configuration/ConfigManager.cs`, change:

```csharp
    public AppConfig Resolve(
        string? flagServer = null,
        Func<string, string?>? envLookup = null)
    {
        envLookup ??= Environment.GetEnvironmentVariable;
        var fileConfig = Load();
        return new AppConfig
        {
            Server = flagServer
                ?? envLookup("GRIMOIRE_SERVER")
                ?? fileConfig.Server,
```

to:

```csharp
    public AppConfig Resolve(Func<string, string?>? envLookup = null)
    {
        envLookup ??= Environment.GetEnvironmentVariable;
        var fileConfig = Load();
        return new AppConfig
        {
            Server = envLookup("GRIMOIRE_SERVER") ?? fileConfig.Server,
```

Read the XML doc comment above `Resolve` and any comment mentioning a flag tier, and update the prose so it describes two tiers rather than three. Do not leave a comment describing the parameter that was removed.

- [ ] **Step 5: Format, build, run the suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: 0 warnings, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/GrimoireCli/Commands/CommandHelper.cs src/GrimoireCli/Configuration/ConfigManager.cs \
        tests/GrimoireCli.Tests/Configuration/ConfigManagerTests.cs
git commit -m "refactor: drop the flag tier from server resolution"
```

---

### Task 3: `login` falls back to `GRIMOIRE_SERVER`

**Files:**
- Modify: `src/GrimoireCli/Commands/LoginCommand.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks. `LoginCommand` reads `Environment.GetEnvironmentVariable` directly; it does not call `ConfigManager.Resolve`.
- Produces: `login` resolves `--server`, then `GRIMOIRE_SERVER`, then the prompt. Task 4 asserts this live.

- [ ] **Step 1: Add the fallback**

In `src/GrimoireCli/Commands/LoginCommand.cs`, change:

```csharp
            var server = parseResult.GetValue(serverOption);
            var configManager = new ConfigManager();
            if (server == null)
            {
                Console.Error.Write("Server URL: ");
                server = Console.ReadLine()?.Trim();
            }
```

to:

```csharp
            // GRIMOIRE_SERVER before the prompt, so the variable that serves every
            // other command also serves the one that establishes the server — and so
            // an unattended login has a way in that is not the flag. The prompt is
            // last because a non-interactive caller gets null from ReadLine and the
            // empty-answer guard below turns that into a readable exit.
            var server = parseResult.GetValue(serverOption)
                ?? Environment.GetEnvironmentVariable("GRIMOIRE_SERVER");
            var configManager = new ConfigManager();
            if (server == null)
            {
                Console.Error.Write("Server URL: ");
                server = Console.ReadLine()?.Trim();
            }
```

Leave the `string.IsNullOrEmpty(server)` guard below it untouched — it still catches an empty prompt answer and exits 1.

- [ ] **Step 2: Update the flag's description**

The option is declared as:

```csharp
        var serverOption = new Option<string?>("--server") { Description = "Grimoire server URL" };
```

Change the description to:

```csharp
        var serverOption = new Option<string?>("--server") { Description = "Grimoire server URL; falls back to GRIMOIRE_SERVER, then prompts" };
```

- [ ] **Step 3: Format, build, run the suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: 0 warnings, all tests pass.

- [ ] **Step 4: Verify the help renders**

```bash
dotnet run --project src/GrimoireCli -- login --help | grep -- --server
```

Expected: the new description appears.

- [ ] **Step 5: Commit**

```bash
git add src/GrimoireCli/Commands/LoginCommand.cs
git commit -m "feat: let login take its server from GRIMOIRE_SERVER"
```

---

### Task 4: Smoke test

Two blocks use the removed flag and would now fail. One block is added for the capability Task 3 introduced.

**Files:**
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: the script's existing `"$CLI"`, `"$WORK"`, `"$CONFIG"`, `"$SERVER"`, `syslist`, `fail` and `ok` helpers.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Build the binary the smoke test runs**

```bash
dotnet build GrimoireCli.sln
```

`"$CLI"` defaults to `src/GrimoireCli/bin/Debug/net10.0/grimoire-cli`, so a stale binary would still carry the flag.

- [ ] **Step 2: Retarget the override block**

In `docker/smoke-test.sh`, replace this block:

```bash
# --- override flags -----------------------------------------------------------
# --server is the flag tier of ConfigManager.Resolve — the tested precedence
# logic (ConfigManagerTests.cs) is otherwise unreachable through the CLI. The
# stored server is deliberately made unreachable first so a config-file fallback
# can't mask a broken flag. The token has no flag tier and stays in the file.
cp "$CONFIG" "$WORK/config.saved"
jq '.server = "http://127.0.0.1:1"' "$WORK/config.saved" >"$CONFIG"

syslist --server "$SERVER"
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "systems list --server returned $COUNT over an unreachable stored server, expected $EXPECTED_SYSTEMS"
ok "systems list --server overrides the stored server"
```

with:

```bash
# --- server resolution --------------------------------------------------------
# GRIMOIRE_SERVER is the only tier above the config file now that --server is
# gone from everything but login. The stored server is deliberately made
# unreachable first so a config-file fallback can't mask a broken env tier. The
# token has no env tier and stays in the file.
cp "$CONFIG" "$WORK/config.saved"
jq '.server = "http://127.0.0.1:1"' "$WORK/config.saved" >"$CONFIG"

LIST_JSON=$(GRIMOIRE_SERVER="$SERVER" "$CLI" systems list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems list with GRIMOIRE_SERVER exited non-zero"; }
COUNT=$(echo "$LIST_JSON" | jq 'length')
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "GRIMOIRE_SERVER returned $COUNT over an unreachable stored server, expected $EXPECTED_SYSTEMS"
ok "GRIMOIRE_SERVER overrides the stored server"

# The flag is gone: a caller still passing it gets a parse error, not a silent
# ignore that would send the request somewhere it did not intend.
"$CLI" systems list --server "$SERVER" >/dev/null 2>&1 \
  && fail "systems list should no longer accept --server"
ok "systems list refuses the removed --server flag"
```

`syslist` is not reused here because it cannot carry an environment variable for one invocation.

- [ ] **Step 3: Retarget the bogus-token block**

Immediately below, replace:

```bash
"$CLI" systems list --server "$SERVER" >/dev/null 2>"$WORK/badtoken.err"; rc=$?
```

with:

```bash
GRIMOIRE_SERVER="$SERVER" "$CLI" systems list >/dev/null 2>"$WORK/badtoken.err"; rc=$?
```

and change its `ok` line from:

```bash
ok "a bogus stored token against a correct --server exits 2"
```

to:

```bash
ok "a bogus stored token against a reachable server exits 2"
```

- [ ] **Step 4: Add the login-from-environment block**

Immediately after the `ok "login repairs a corrupt config"` line, add:

```bash
# login is the one command that still takes --server, and now the one that also
# reads GRIMOIRE_SERVER. Without the variable an unattended login has no way in:
# the prompt's ReadLine returns null and the command exits 1.
cp "$CONFIG" "$WORK/config.beforeenvlogin"
printf 'admin' | GRIMOIRE_SERVER="$SERVER" "$CLI" login --username admin --password-stdin \
  >/dev/null 2>"$WORK/envlogin.err" \
  || { cat "$WORK/envlogin.err" >&2; fail "login should take its server from GRIMOIRE_SERVER"; }
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "login did not store the server from GRIMOIRE_SERVER: $(cat "$CONFIG")"
ok "login takes its server from GRIMOIRE_SERVER"
```

- [ ] **Step 5: Run the smoke test twice**

```bash
bash docker/smoke-test.sh
bash docker/smoke-test.sh
```

Both must print `smoke: all checks passed`. A second run that fails means an assertion depends on state the first run left behind.

If the stack is not running, start and seed it first:

```bash
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

Skip the fixture copy if `docker/data` already exists.

- [ ] **Step 6: Run it once with `GRIMOIRE_SERVER` exported**

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9481 bash docker/smoke-test.sh
```

CI exports `GRIMOIRE_SERVER` for the whole job while locally it is unset, so an assertion that passes only in one condition passes only in one place. This must also print `smoke: all checks passed`.

- [ ] **Step 7: Commit**

```bash
git add docker/smoke-test.sh
git commit -m "test: cover server resolution through the environment tier"
```

---

### Task 5: Documentation

**Files:**
- Modify: `README.md` (the precedence list, line ~189)
- Modify: `docs/configuration.md` (the precedence sentence, line ~33)
- Modify: `docs/grimoire-compatibility.md` (the "one server, one slot" note, line ~71)
- Modify: `CLAUDE.md` (the convention bullet, and a stale sentence at line ~116)

**Interfaces:**
- Consumes: nothing. This task is prose only.
- Produces: nothing. Last task.

- [ ] **Step 1: Update the README precedence list**

In `README.md`, replace:

```markdown
1. **CLI flags** (`--server`)
2. **Environment variables** (`GRIMOIRE_SERVER`)
3. **Config file**
```

with:

```markdown
1. **Environment variables** (`GRIMOIRE_SERVER`)
2. **Config file**

`login` is the exception: it takes `--server`, falls back to `GRIMOIRE_SERVER`, then prompts. No other command takes a server flag — the access token lives in the config file alone, so there is nothing for a per-command flag to switch to.
```

The Commands table needs no change: its only `--server` mention is the `login` row, which is still accurate.

- [ ] **Step 2: Update `docs/configuration.md`**

One sentence names all three tiers:

```
resolved — file, `GRIMOIRE_SERVER`, or `--server`. It therefore runs only after
```

Drop `--server` from that list. Then find the file's own precedence description and bring it to two tiers, adding the reason — the token has no env or flag tier, so a per-command server flag could only ever re-address the instance the stored token belongs to. Match the file's existing voice; do not paste the README wording verbatim.

- [ ] **Step 2b: Update `docs/grimoire-compatibility.md`**

The "Known limitation: one server, one slot" note says:

```
file, not keyed by server. Pointing `--server` or `GRIMOIRE_SERVER` at a
```

Only `GRIMOIRE_SERVER` can do this now. Change that clause to name the environment variable alone. The limitation itself is unchanged and still worth stating — pointing the env var at a second instance still records that instance's version into the single slot.

- [ ] **Step 3: Rewrite the `CLAUDE.md` convention bullet**

Find the bullet beginning:

```markdown
- **`--server` is declared per-subcommand on commands that consume a saved token**
```

Replace the whole bullet with:

```markdown
- **Only `login` takes `--server`.** It produces the config rather than overriding it, and resolves `--server`, then `GRIMOIRE_SERVER`, then an interactive prompt. Every other command resolves the server from `GRIMOIRE_SERVER` or the config file, and the token comes from the file alone — which is why a per-command server flag could only re-address the instance the stored token belongs to, never switch to another. `config` and `self-test` make no API call and take neither.
```

The old bullet justified the per-subcommand pattern as matching abs-cli. That was false — abs-cli declares `--server` three times, not per-subcommand — so the claim goes with the pattern.

- [ ] **Step 3b: Remove the stale sentence in `CLAUDE.md`**

Under the role-tagging notes, one sentence ends:

```
The first write command is what will use it for real, and is also the first to decide whether write commands take `--server`.
```

Write commands shipped long ago and the decision is now made for every command at once. Trim the sentence to end after "use it for real", dropping the `--server` clause.

- [ ] **Step 4: Check nothing else still documents the flag**

```bash
grep -rn '\-\-server' README.md docs/*.md CLAUDE.md | grep -v "login"
```

Expected: **no output**. Committed specs and plans under `docs/specs/` and `docs/plans/` are records of decisions and are deliberately not matched by this glob — leave them alone.

- [ ] **Step 5: Verify**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four clean.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/configuration.md docs/grimoire-compatibility.md CLAUDE.md
git commit -m "docs: record that only login takes --server"
```

---

## Done when

- `--server` parses only on `login`; every other command rejects it with an unrecognised-option error
- `BuildClient` and `Resolve` carry no flag parameter, and precedence is `GRIMOIRE_SERVER` > config file
- `login` resolves `--server`, then `GRIMOIRE_SERVER`, then the prompt
- the smoke test proves the env tier, the flag's refusal, and the unattended login, and passes twice and with `GRIMOIRE_SERVER` exported
- README, `configuration.md` and `CLAUDE.md` describe two tiers and the `login` exception
- all four verification commands pass
