# Configuration

## Config File

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

```json
{
  "server": "https://grimoire.example.com",
  "accessToken": "eyJhbG...",
  "refreshToken": "d4f1...",
  "lastVersionCheck": "2026-08-13T07:01:15+00:00",
  "lastServerVersion": "1.5.6"
}
```

Keys are camelCase in the file (`AppConfig` in
`src/GrimoireCli/Configuration/AppConfig.cs`, `[JsonPropertyName]`-mapped).
There is no `defaultLibrary` key — there's no equivalent of abs-cli's
default-library concept yet (single-system live instance; `systems` commands
take `--id` directly).

`refreshToken` is the `grimoire_refresh` cookie value from the last login or
refresh, written by `login` and rewritten by every renewal
(`ConfigManager.UpdateTokens`). See [authentication.md](authentication.md) for
the renewal rules.

`lastVersionCheck` and `lastServerVersion` are written by the CLI's own
24-hour version-check cadence (see
[grimoire-compatibility.md](grimoire-compatibility.md#runtime-check)), not by
the operator — `config set` does not accept either key. The check runs
against the token in the config file, and against whatever server the
command resolved — `GRIMOIRE_SERVER`, or the config file. It therefore runs
only after a `login` on this machine: with no stored access token,
`CommandHelper.BuildClient` exits 1 before any request is made.

## Reading and writing the file

The file is written by filling a temporary file beside it and replacing the target
with it, which is atomic within a directory, so an interrupted write leaves the
previous config intact. That matters here because the refresh token it holds is
what keeps the session alive: losing it to a torn write costs a login, and both
the version check and every renewal write outside login. A power loss is not
covered — the rename can land before the data — but a truncated file is then read
as absent rather than as an error.

The file is `0600`, readable only by its owner, and stays that way across writes
because the replacement carries the new file's mode.

A config file that is not valid JSON is **moved to `config.json.corrupt`** and
reported on stderr, then treated as absent. Moving it is what makes the token
recoverable: the file usually still contains it, and the next write would
otherwise replace the file wholesale. `GRIMOIRE_SERVER` still works in that
state, but with the stored token gone the command fails on its own terms;
`grimoire-cli login` writes a fresh config.

A write that fails — a read-only home, a full disk — is reported as an error, and
`login` and `config set` exit non-zero rather than claiming to have saved
anything. The version check's own write is the exception: it is a diagnostic, so a
failure there is a debug line and the check simply runs again next time.

## Precedence Order

Highest wins (`ConfigManager.Resolve`):

1. Environment variable — `GRIMOIRE_SERVER`
2. Config file (whichever one [resolved](#config-file))

`login` is the exception: its `--server` writes straight to the file rather than
going through this resolution, and falls back to `GRIMOIRE_SERVER` and then an
interactive prompt when the flag is absent. No other command takes a server flag.

The access and refresh tokens come from the config file alone, which is why a
per-command server flag could only ever re-address the instance the stored token
already belongs to.

## Config Commands

| Command | Description |
|---------|-------------|
| `grimoire-cli config get` | Shows current config (`accessToken` and `refreshToken` masked to `***`, plus `configPath`, `configSource`, `lastVersionCheck`, `lastServerVersion`) |
| `grimoire-cli config set <key> <value>` | Sets a config value |

`config set` accepts **only** `server` as a key — `ApplyConfigSet` in
`src/GrimoireCli/Commands/ConfigCommand.cs` rejects anything else with
`Unknown config key: '<key>'. Valid keys: server` and exits 1. There is no
generic setter for arbitrary keys the way abs-cli allows; the tokens are
written only by `login` and by automatic renewal.

## Error Messages

- No server → `No server configured. Run: grimoire-cli login` (exit 1)
- No token → `Not authenticated. Run: grimoire-cli login` (exit 1)
- 401 from API → `Not authenticated, or the token has expired. Run: grimoire-cli login` (exit 2)
- Renewal refused → `Session expired. Run: grimoire-cli login` (exit 2)

(`CommandHelper.BuildClient` for the first two; `GrimoireApiClient.EnsureSuccessAsync`
for the third and `RefreshAsync` for the fourth — see
[input-output.md](input-output.md) for the exit-code convention behind the 1 vs. 2
split.)

## Deliberately absent

- **No `--config` flag.** The file is chosen by `GRIMOIRE_CONFIG` or a
  `grimoire-cli.json` beside the binary (see [Config File](#config-file)). A
  per-command flag would be one more thing an agent has to pass on every call,
  and forgetting it would silently act as a different account.
- **`GRIMOIRE_DEBUG=1`** is a config-adjacent environment variable but does
  not live in `AppConfig` — it's read directly in `Program.cs` and mirrors
  `--debug`. See [input-output.md](input-output.md).
