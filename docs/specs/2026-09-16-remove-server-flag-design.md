# Remove `--server` from every command except `login`

**Status:** approved
**Issue:** [#54](https://github.com/thomaslazar/grimoire-cli/issues/54)
**Verified against:** the repo at `6737eb1`, and `hunterreadca/grimoire:1.6.2`

## Problem

`--server` is declared 73 times across 23 command files. On the 72 that are not
`login`, it has no demonstrated user, and it cannot do the one thing that would
justify it.

### It cannot switch servers

The config file is a single slot for server *and* token. Point `--server` at a
second instance and the CLI sends the first instance's token: 401. Reaching
instance B means `login --server B`, which overwrites both fields — after which
`--server` on later commands is redundant, because the config already names B.

The token is instance-scoped, not URL-scoped: the JWT carries `sub`, `username`,
`role`, `iat`, `jti`, `exp` and `sid`, with no audience or issuer claim.
Verified — logging in against `http://host.docker.internal:9481` and then running
`me --server http://0.250.250.254:9481`, the same instance by IP, works. So the
flag can re-address one instance, which is a job `login` already does once.

### Nothing uses it

The only non-login use of `--server` in the repository is one smoke-test block,
whose own comment states the circularity:

> `--server` is the flag tier of `ConfigManager.Resolve` — the tested precedence
> logic (`ConfigManagerTests.cs`) is otherwise unreachable through the CLI.

The block has to sabotage the config first (`jq '.server = "http://127.0.0.1:1"'`)
to make the override observable, because with a correct config the flag has no
visible effect. Two lines above it, where the script genuinely needs to target a
configurable server, it uses the environment variable instead:

```bash
SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
```

`GRIMOIRE_SERVER` already covers "override without mutating the config file", and
is what actually gets used — including by CI, which exports it for the whole job.

### The stated justification is false

`CLAUDE.md` justifies the per-subcommand declaration as *"matching abs-cli"*:

| | `--server` declarations |
|---|---|
| abs-cli | 3 (login ×1, libraries ×2) |
| grimoire-cli | 73, across 23 files |

Parity argues for removal.

## What the exploration established

- **`login` is on a separate path entirely.** It reads `--server` directly,
  prompts if absent, and errors if the answer is empty. It never consults
  `GRIMOIRE_SERVER` or the config file, and never calls
  `ConfigManager.Resolve`.
- **Therefore the flag tier has exactly one class of feeder** — the 72 non-login
  options. Remove them and `Resolve(flagServer:)` has no callers at all.
- **The README Commands table needs no edit.** Its only `--server` mention is the
  `login` row.

## Design

### 1. What is removed

| | count |
|---|---|
| `--server` `Option` declarations, in 22 command files | 72 |
| `serverOverride:` arguments at `CommandHelper.BuildClient` call sites | 72 |
| `CommandHelper.BuildClient`'s `serverOverride` parameter | 1 |
| `ConfigManager.Resolve`'s `flagServer` parameter | 1 |

Precedence becomes **`GRIMOIRE_SERVER` > config file**. The flag tier goes with
its feeders rather than surviving as a parameter nothing supplies — dead
flexibility is what gets re-wired by mistake later.

### 2. What is kept

- **`login --server`** — load-bearing. It produces the config.
- **`GRIMOIRE_SERVER`** — the tier with real users, including CI.
- **`config set server <url>`** — the explicit way to change the stored value.

### 3. `login` gains an environment fallback

Without it, `GRIMOIRE_SERVER` would work for the 72 commands that resolve a
server through `CommandHelper.BuildClient` and not for the one you run first, which reads as an oversight rather than a decision. It also unblocks
an unattended login: today a non-interactive caller must pass `--server`, because
the prompt's `Console.ReadLine()` returns null and the command exits 1.

```csharp
var server = parseResult.GetValue(serverOption)
    ?? Environment.GetEnvironmentVariable("GRIMOIRE_SERVER");
if (server == null)
{
    Console.Error.Write("Server URL: ");
    server = Console.ReadLine()?.Trim();
}
```

The existing empty-answer guard is untouched and still exits 1.

Resolution order for `login` alone: `--server`, then `GRIMOIRE_SERVER`, then the
prompt. It deliberately does **not** fall back to the config file's stored
server — that would change when the prompt appears, and re-authenticating
against a server you did not name is a different feature.

### 4. Testing

- **`HelpOutputTests`** — two tests assert `--server` appears in `systems list`
  and `systems get` help. They invert to `DoesNotContain`, keeping their existing
  `--token` assertions, which guard the flag removed in 0.2.0.
- **`VocabularyCommandTests`, `MeCommandTests`** — each parses `--server`
  expecting no errors. They invert to expecting a parse error, which is what
  proves the option is gone rather than merely hidden from help.
- **`ConfigManagerTests`** — the flag-tier cases are deleted. The `env > file`
  and file-only cases remain and now cover the whole chain.
- **Smoke** — `login` resolving from `GRIMOIRE_SERVER` is asserted here rather
  than in a unit test. `LoginCommand` reads the environment directly, and adding
  an injection seam for one call site would be more machinery than the assertion
  it enables.

### 5. Smoke test changes

Two existing blocks use `--server` and must retarget:

- **The override block** proves flag-beats-file by sabotaging the config. It
  becomes env-beats-file: break the file, pass `GRIMOIRE_SERVER` inline for the
  one invocation. That works whether or not the outer environment already exports
  it, which CI does.
- **The bogus-token block** passes `--server` alongside a deliberately broken
  token. It drops the flag and resolves from the config.

One block is added: an unattended `login` with only `GRIMOIRE_SERVER` set
succeeds and writes that server to the config. This is the capability the change
introduces, so it is the one that must be proven live.

The unreachable-server block added for
[#55](https://github.com/thomaslazar/grimoire-cli/issues/55) already drives
`GRIMOIRE_SERVER` and needs no change.

### 6. Documentation

- **README** — the precedence list drops from three tiers to two. The Commands
  table is untouched.
- **`docs/configuration.md`** — the same, plus the reason the flag tier is gone:
  the token lives in the config file alone, so there is nothing for a per-command
  flag to switch to.
- **`docs/grimoire-compatibility.md`** — the "one server, one slot" note names
  `--server` alongside `GRIMOIRE_SERVER`; only the variable can do this now. The
  limitation itself stands.
- **`CLAUDE.md`** — rewrite the `--server` convention bullet. It currently
  mandates the per-subcommand pattern and justifies it as matching abs-cli, which
  is false. It becomes: `login` takes `--server` because it produces the config;
  every other command resolves the server from the environment or the file. A
  second, stale sentence saying the first write command would decide whether
  write commands take `--server` is trimmed — write commands shipped long ago,
  and the decision is now made for every command at once.

## Out of scope

- **Per-server token storage.** A servers map in the config would make
  multi-instance use work properly, and is the only thing that would justify a
  per-command server flag. It is a feature, not part of this removal.
- **Making `--server` recursive** rather than removing it. Considered and
  rejected: it keeps a flag nobody uses, forces one shared description
  (`login`'s is "Grimoire server URL", the rest "Server URL override" — the
  distinction is correct and would be lost), and needs a static `Option`
  reference that all 23 command files reach into. System.CommandLine 2.0.7 has
  `Hidden`, but it is per-`Option` rather than per-command, so it cannot exempt
  `config` and `self-test`; there is no `Exclude` or `Inherit` member at all.
- **The release itself.** This is a user-visible removal and therefore breaking,
  which `0.x` permits. It belongs in a `0.3.0`, but cutting that release is a
  separate decision from landing this change.

## Migration

Anything scripting `--server` against the same instance keeps working through
`GRIMOIRE_SERVER` or the stored config, but the invocation needs editing:

```bash
# before
grimoire-cli systems list --server https://grimoire.example.com

# after — either
GRIMOIRE_SERVER=https://grimoire.example.com grimoire-cli systems list
# or, once
grimoire-cli config set server https://grimoire.example.com
grimoire-cli systems list
```

A caller that passes the removed flag gets a parse error naming the unknown
option, not a silent ignore.
