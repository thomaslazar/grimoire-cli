# Per-install config resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the config file be chosen per install — `GRIMOIRE_CONFIG`, then `grimoire-cli.json` beside the binary if it exists, then `~/.grimoire-cli/config.json` — so separate harnesses can act as separate Grimoire accounts.

**Architecture:** One pure resolution function on `ConfigManager` produces a path and the tier that produced it; the argument-less constructor uses it, so every existing call site picks it up unchanged, and all file behaviour (atomic write, `0600`, `.corrupt` rename, token renewal) applies at the resolved path as before. `config get` reports the resolved path and tier.

**Tech Stack:** C# / .NET 10 with NativeAOT, System.CommandLine, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-29-config-resolution-design.md](../specs/2026-09-29-config-resolution-design.md)

## Global Constraints

- Resolution order, verbatim: 1. `GRIMOIRE_CONFIG` if set and non-empty; 2. `grimoire-cli.json` in the directory holding the running executable, **only if that file exists**; 3. `~/.grimoire-cli/config.json`.
- The executable path comes from `Environment.ProcessPath`; null falls through to tier 3, never throws.
- The CLI never creates a tier-2 file implicitly.
- Nothing about what the config file contains, or how it is read, written, permissioned or repaired, changes.
- Do **not** add a `GRIMOIRE_TOKEN` environment variable, working-directory discovery, or a `login --local` flag.
- Run `dotnet format GrimoireCli.sln` after writing or modifying any C# file.
- No blank lines between consecutive variable declarations of the same kind, or before a `return` that follows setup calls.
- Help text is terse: one-liners, no "useful when…" framing, never restating what is already visible.
- Comments say what the code does or why it must be this way, never what was deliberately left out.
- Commit messages: Conventional Commits, imperative, lowercase, no trailing period. **No `Co-Authored-By` line and no "Generated with Claude Code" line** — this repo's CLAUDE.md forbids both. Verify every commit with `git log -1 --format='%B'`.
- Do not edit `CHANGELOG.md` or `docs/roadmap.md`.
- The spec file `docs/specs/2026-09-29-config-resolution-design.md` and this plan are written but uncommitted; Task 1 commits them alongside its code.

---

### Task 1: Resolve the config path, and prove tier 2 under AOT

**Files:**
- Modify: `src/GrimoireCli/Configuration/ConfigManager.cs` (the constructor at line 22 and `DefaultConfigPath` at lines 24-28)
- Create: `tests/GrimoireCli.Tests/Configuration/ConfigLocationTests.cs`
- Commit: `docs/specs/2026-09-29-config-resolution-design.md`, `docs/plans/2026-09-29-config-resolution.md`

**Interfaces:**
- Produces: `public sealed record ConfigLocation(string Path, string Source);` in namespace `GrimoireCli.Configuration`, declared in `ConfigManager.cs`. `Source` is exactly one of the strings `"env"`, `"binary"`, `"home"`.
- Produces: `public static ConfigLocation Locate()` on `ConfigManager`, reading the real environment, executable path, filesystem and home directory.
- Produces: `internal static ConfigLocation Locate(Func<string, string?> envLookup, string? executablePath, Func<string, bool> fileExists, string home)` — the pure overload the tests drive.
- Produces: `DefaultConfigPath()` now returns `Locate().Path`. It keeps its name so `ConfigCommand` still compiles; Task 2 changes that caller.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Configuration/ConfigLocationTests.cs`:

```csharp
using GrimoireCli.Configuration;

namespace GrimoireCli.Tests.Configuration;

public class ConfigLocationTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "home");
    private static readonly string BinDir = Path.Combine(Path.GetTempPath(), "proj", "bin");
    private static readonly string Exe = Path.Combine(BinDir, "grimoire-cli");
    private static readonly string Sibling = Path.Combine(BinDir, "grimoire-cli.json");
    private static readonly string HomeDefault = Path.Combine(Home, ".grimoire-cli", "config.json");

    private static Func<string, string?> Env(string? value) =>
        name => name == "GRIMOIRE_CONFIG" ? value : null;

    private static Func<string, bool> Exists(params string[] paths) => path => paths.Contains(path);

    [Fact]
    public void TheEnvironmentVariableWins()
    {
        var target = Path.Combine(Path.GetTempPath(), "gm.json");
        var location = ConfigManager.Locate(Env(target), Exe, Exists(Sibling), Home);
        Assert.Equal(new ConfigLocation(target, "env"), location);
    }

    // The override names the file to use, so whether it exists yet does not
    // matter: login is what creates it.
    [Fact]
    public void TheEnvironmentVariableIsHonouredBeforeTheFileExists()
    {
        var target = Path.Combine(Path.GetTempPath(), "not-yet.json");
        var location = ConfigManager.Locate(Env(target), Exe, Exists(), Home);
        Assert.Equal(new ConfigLocation(target, "env"), location);
    }

    [Fact]
    public void ARelativeEnvironmentPathIsMadeAbsolute()
    {
        var location = ConfigManager.Locate(Env("gm.json"), Exe, Exists(), Home);
        Assert.Equal(Path.GetFullPath("gm.json"), location.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyEnvironmentVariableIsIgnored(string value)
    {
        var location = ConfigManager.Locate(Env(value), Exe, Exists(), Home);
        Assert.Equal(new ConfigLocation(HomeDefault, "home"), location);
    }

    [Fact]
    public void AFileBesideTheBinaryIsUsedWhenPresent()
    {
        var location = ConfigManager.Locate(Env(null), Exe, Exists(Sibling), Home);
        Assert.Equal(new ConfigLocation(Sibling, "binary"), location);
    }

    // An absent sibling must not claim the path, or every install would move
    // its config beside itself on first login.
    [Fact]
    public void AnAbsentFileBesideTheBinaryFallsThroughToHome()
    {
        var location = ConfigManager.Locate(Env(null), Exe, Exists(), Home);
        Assert.Equal(new ConfigLocation(HomeDefault, "home"), location);
    }

    [Fact]
    public void AnUnknownExecutablePathFallsThroughToHome()
    {
        var location = ConfigManager.Locate(Env(null), null, Exists(Sibling), Home);
        Assert.Equal(new ConfigLocation(HomeDefault, "home"), location);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "ConfigLocationTests"`
Expected: FAIL to compile — `ConfigLocation` and `ConfigManager.Locate` do not exist.

- [ ] **Step 3: Implement the resolution**

In `src/GrimoireCli/Configuration/ConfigManager.cs`, add the record after the `ConfigWriteException` class and before `public class ConfigManager`:

```csharp
/// <summary>
/// The config file in use and the tier that chose it: <c>env</c> for
/// GRIMOIRE_CONFIG, <c>binary</c> for grimoire-cli.json beside the executable,
/// <c>home</c> for ~/.grimoire-cli/config.json.
/// </summary>
public sealed record ConfigLocation(string Path, string Source);
```

Replace the argument-less constructor and `DefaultConfigPath` (lines 22-28) with:

```csharp
    public ConfigManager() : this(DefaultConfigPath()) { }

    public static string DefaultConfigPath() => Locate().Path;

    /// <summary>
    /// Resolves the config file: GRIMOIRE_CONFIG if set; else grimoire-cli.json
    /// beside the running executable, but only if it already exists, so an
    /// install never claims a config it was not given; else the home default.
    /// The token must live in a file the CLI can write renewals back to, which is
    /// why the choice is of a file and never of a token.
    /// </summary>
    public static ConfigLocation Locate() => Locate(
        Environment.GetEnvironmentVariable,
        Environment.ProcessPath,
        File.Exists,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static ConfigLocation Locate(
        Func<string, string?> envLookup, string? executablePath, Func<string, bool> fileExists, string home)
    {
        var fromEnv = envLookup("GRIMOIRE_CONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return new ConfigLocation(Path.GetFullPath(fromEnv), "env");
        var exeDir = executablePath is null ? null : Path.GetDirectoryName(executablePath);
        if (exeDir is not null)
        {
            var sibling = Path.Combine(exeDir, "grimoire-cli.json");
            if (fileExists(sibling))
                return new ConfigLocation(sibling, "binary");
        }
        return new ConfigLocation(Path.Combine(home, ".grimoire-cli", "config.json"), "home");
    }
```

`InternalsVisibleTo` for the test project must already be in place for the tests to call the `internal` overload. Confirm with:

```bash
grep -rn "InternalsVisibleTo" src/GrimoireCli/
```

If it is absent, stop and report rather than making the overload public.

- [ ] **Step 4: Run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: 0 warnings, 0 errors; all tests pass, including the eight new ones.

- [ ] **Step 5: Prove tier 2 against the published AOT binary — this is a gate**

`dotnet run` and the test host report the host process, not the app, so only a published NativeAOT binary shows whether `Environment.ProcessPath` really names the binary itself. `config get` already prints `configPath` from `DefaultConfigPath()`, which now resolves, so it is the probe.

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
env -u GRIMOIRE_CONFIG ./publish/grimoire-cli config get | jq -r .configPath
echo '{}' > ./publish/grimoire-cli.json
env -u GRIMOIRE_CONFIG ./publish/grimoire-cli config get | jq -r .configPath
GRIMOIRE_CONFIG="$PWD/publish/override.json" ./publish/grimoire-cli config get | jq -r .configPath
(cd /tmp && env -u GRIMOIRE_CONFIG "$OLDPWD/publish/grimoire-cli" config get | jq -r .configPath)
rm -rf ./publish
```

Expected, in order:
1. `$HOME/.grimoire-cli/config.json` — no sibling yet, so home.
2. the absolute path of `./publish/grimoire-cli.json` — the sibling now exists.
3. the absolute path of `./publish/override.json` — the environment wins.
4. the absolute path of `./publish/grimoire-cli.json` again — invoked from another working directory, which is the whole point of tier 2.

Paste all four lines into your report. **If line 2 or line 4 does not name the sibling, stop and report `BLOCKED`.** Tier 2 would then never fire in a real install, and shipping it would be a tier that silently does nothing. Do not work around it with `AppContext.BaseDirectory` or anything else without reporting first.

- [ ] **Step 6: Commit**

```bash
git add docs/specs/2026-09-29-config-resolution-design.md docs/plans/2026-09-29-config-resolution.md src/GrimoireCli/Configuration/ConfigManager.cs tests/GrimoireCli.Tests/Configuration/ConfigLocationTests.cs
git commit -m "feat: resolve the config file per install"
```

---

### Task 2: Report the resolved file, and document it

**Files:**
- Modify: `src/GrimoireCli/Commands/ConfigCommand.cs` (`CreateGetCommand`, lines 19-40)
- Modify: `src/GrimoireCli/Commands/LoginCommand.cs` (the Notes section, lines 21-26)
- Create: `tests/GrimoireCli.Tests/Commands/ConfigCommandTests.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/LoginCommandTests.cs` if it exists; otherwise put the login fact in `ConfigCommandTests.cs`
- Modify: `docs/configuration.md` (the "Config File" section)
- Modify: `README.md` (the "Configuration" section, around line 185)
- Modify: `docker/smoke-test.sh`

**Interfaces:**
- Consumes: `ConfigManager.Locate()` and `ConfigLocation(string Path, string Source)` from Task 1, `Source` being `"env"`, `"binary"` or `"home"`.
- Produces: `config get` output gains a `configSource` key beside the existing `configPath`.

- [ ] **Step 1: Write the failing tests**

Create `tests/GrimoireCli.Tests/Commands/ConfigCommandTests.cs`:

```csharp
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ConfigCommandTests
{
    // "Why is this the wrong account?" is the question per-install config
    // creates, so the command that answers it must teach the order.
    [Fact]
    public void GetTeachesTheResolutionOrder()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("GRIMOIRE_CONFIG", output);
        Assert.Contains("grimoire-cli.json beside the binary, if it exists", output);
        Assert.Contains("~/.grimoire-cli/config.json", output);
    }

    [Fact]
    public void GetTeachesHowToBootstrapAnInstallsOwnAccount()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("echo '{}' > bin/grimoire-cli.json", output);
    }

    [Fact]
    public void GetWarnsThatASymlinkResolvesToTheRealFile()
    {
        var output = HelpRenderer.Render(ConfigCommand.Create(), ["config", "get"], false);
        Assert.Contains("symlinked binary", output);
    }

    [Fact]
    public void LoginSaysWhichFileItWrites()
    {
        var output = HelpRenderer.Render(LoginCommand.Create(), ["login"], false);
        Assert.Contains("Writes the resolved config file; config get reports which one.", output);
    }
}
```

Check how `HelpRenderer.Render` is called for a root-level command before relying on the path arrays above:

```bash
grep -rn "HelpRenderer.Render" tests/GrimoireCli.Tests/Commands/RootHelpTests.cs tests/GrimoireCli.Tests/Commands/HelpRenderer.cs | head
```

If it expects a different path shape for a top-level command, match what the existing tests do and say so in your report.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "ConfigCommandTests"`
Expected: FAIL — neither command carries the new text yet.

- [ ] **Step 3: Make `config get` report the resolved file**

In `src/GrimoireCli/Commands/ConfigCommand.cs`, replace `CreateGetCommand` with:

```csharp
    private static Command CreateGetCommand()
    {
        var command = new Command("get", "Show current configuration");
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "configPath is the file in use, resolved in order: GRIMOIRE_CONFIG;",
            "grimoire-cli.json beside the binary, if it exists;",
            "~/.grimoire-cli/config.json.",
            "",
            "To give an install its own account, create the sibling file before",
            "logging in — echo '{}' > bin/grimoire-cli.json — then login writes",
            "into it.",
            "",
            "A symlinked binary resolves to the real file's directory.");
        command.AddExamples("grimoire-cli config get");
        command.SetAction(parseResult =>
        {
            var location = ConfigManager.Locate();
            var config = new ConfigManager(location.Path).Load();
            var display = new Dictionary<string, string>
            {
                ["server"] = config.Server ?? "(not set)",
                ["accessToken"] = config.AccessToken != null ? "***" : "(not set)",
                ["refreshToken"] = config.RefreshToken != null ? "***" : "(not set)",
                ["lastVersionCheck"] = config.LastVersionCheck?.ToString("u") ?? "(never)",
                ["lastServerVersion"] = config.LastServerVersion ?? "(unknown)",
                ["configPath"] = location.Path,
                ["configSource"] = location.Source
            };
            ConsoleOutput.WriteJson(display);
            return 0;
        });
        return command;
    }
```

Resolving once and loading from that path is what makes the reported path the one actually read — two separate resolutions could, in principle, disagree.

- [ ] **Step 4: Add the line to `login`**

In `src/GrimoireCli/Commands/LoginCommand.cs`, append one line to the existing Notes section so it ends:

```csharp
            "OIDC accounts cannot log in here — this is the local password path.",
            "Writes the resolved config file; config get reports which one.");
```

- [ ] **Step 5: Run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 6: Read the rendered help**

```bash
dotnet run --project src/GrimoireCli -- config get --help
dotnet run --project src/GrimoireCli -- login --help
```

Paste both into your report. Confirm the Notes read cleanly and nothing is said twice.

- [ ] **Step 7: Update `docs/configuration.md`**

Replace the line `Location: \`~/.grimoire-cli/config.json\`` under "## Config File" with:

````markdown
Location, resolved once per command, first match wins:

| Tier | Path | Applies when |
|---|---|---|
| `env` | `$GRIMOIRE_CONFIG` | the variable is set and non-empty |
| `binary` | `grimoire-cli.json` beside the executable | that file already exists |
| `home` | `~/.grimoire-cli/config.json` | otherwise |

`config get` reports the resolved file as `configPath` and the tier as
`configSource`. Everything else on this page — the atomic write, the `0600`
mode, the `.corrupt` rename — applies at whichever path resolved.

**Giving an install its own account.** The CLI never creates a `binary`-tier
file; creating it is what opts an install in. For a harness that installs to
`./bin/grimoire-cli`:

```bash
echo '{}' > bin/grimoire-cli.json
./bin/grimoire-cli login --server https://grimoire.example.com
```

From then on `./bin/grimoire-cli` is that account, from any working directory,
with nothing to export. The executable's directory comes from
`Environment.ProcessPath`, which on Linux follows symlinks, so a symlinked binary
looks beside the real file rather than beside the link.

**There is no token environment variable, and there should not be one.** The
access token lives 30 minutes and every renewal is written back to the config
file ([authentication.md](authentication.md)). A token supplied through the
environment would have nowhere to be renewed into and would die at the first
expiry, which is why the choice is of a file, never of a token.
````

- [ ] **Step 8: Update the README's Configuration section**

In `README.md`, replace the line

```markdown
Config is stored at `~/.grimoire-cli/config.json`. Values resolve in this order:
```

with:

```markdown
Config is stored at `$GRIMOIRE_CONFIG` if set, else `grimoire-cli.json` beside the binary if that file exists, else `~/.grimoire-cli/config.json`; `config get` reports which. Create the sibling file (`echo '{}' > bin/grimoire-cli.json`) before `login` to give one install its own account — see [docs/configuration.md](docs/configuration.md). Within the file, values resolve in this order:
```

Leave the numbered list and the rest of the section unchanged.

- [ ] **Step 9: Add the smoke check**

In `docker/smoke-test.sh`, find the block that ends `ok "config has server and token"` (around line 71). Insert directly after it:

```bash
# GRIMOIRE_CONFIG chooses the file outright, so config get must report that
# path and not the home default — the answer to "why is this the wrong account".
GRIMOIRE_CONFIG="$WORK/alt-config.json" "$CLI" config get >"$WORK/config-alt.out" 2>&1 \
  || fail "config get under GRIMOIRE_CONFIG exited non-zero"
jq -e --arg p "$WORK/alt-config.json" '.configPath == $p and .configSource == "env"' \
  "$WORK/config-alt.out" >/dev/null \
  || fail "config get should report the GRIMOIRE_CONFIG file: $(cat "$WORK/config-alt.out")"
ok "GRIMOIRE_CONFIG chooses the config file"
```

The check reads only; it writes no file, so it converges on a re-run.

`$WORK` must be an absolute path for the comparison to hold, since the CLI reports an absolute path. Confirm how `WORK` is defined near the top of the script and say so in your report.

- [ ] **Step 10: Run the smoke test**

The stack is up and seeded at `http://host.docker.internal:9481`. Run:

```bash
bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed`, including the new `ok:` line. Run it a second time to confirm it converges.

- [ ] **Step 11: Run the full pre-PR set**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass.

- [ ] **Step 12: Commit**

```bash
git add src/GrimoireCli/Commands/ConfigCommand.cs src/GrimoireCli/Commands/LoginCommand.cs tests/GrimoireCli.Tests/Commands/ConfigCommandTests.cs docs/configuration.md README.md docker/smoke-test.sh
git commit -m "feat: report the resolved config file in config get"
```

Add `tests/GrimoireCli.Tests/Commands/LoginCommandTests.cs` to the `git add` if you modified it instead.
