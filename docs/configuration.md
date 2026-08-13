# Configuration

## Config File

Location: `~/.grimoire-cli/config.json`

```json
{
  "server": "https://grimoire.example.com",
  "accessToken": "eyJhbG...",
  "lastVersionCheck": "2026-08-13T07:01:15+00:00",
  "lastServerVersion": "1.5.6"
}
```

Keys are camelCase in the file (`AppConfig` in
`src/GrimoireCli/Configuration/AppConfig.cs`, `[JsonPropertyName]`-mapped).
There is no `refreshToken` or `defaultLibrary` key — Grimoire issues no
refresh token, and there's no equivalent of abs-cli's default-library concept
yet (single-system live instance; `systems` commands take `--id` directly).

`lastVersionCheck` and `lastServerVersion` are written by the CLI's own
24-hour version-check cadence (see
[grimoire-compatibility.md](grimoire-compatibility.md#runtime-check)), not by
the operator — `config set` does not accept either key. The check runs
against whatever server and token the command resolved — file, environment,
or `--server`/`--token` flags — so it can run before any `login` on this
machine: on a machine with no config file, the first check via `--server`/
`--token` creates one with only `lastVersionCheck` and `lastServerVersion`
populated (`server` and `accessToken` stay unset until `login` writes them).

## Reading and writing the file

The file is written by filling a temporary file beside it and replacing the target
with it, which is atomic within a directory, so an interrupted write leaves the
previous config intact. That matters here because the token it holds is valid for
30 days with no refresh: losing it to a torn write costs a login, and the version
check writes daily rather than only at login. A power loss is not covered — the
rename can land before the data — but a truncated file is then read as absent
rather than as an error.

The file is `0600`, readable only by its owner, and stays that way across writes
because the replacement carries the new file's mode.

A config file that is not valid JSON is **moved to `config.json.corrupt`** and
reported on stderr, then treated as absent. Moving it is what makes the token
recoverable: the file usually still contains it, and the next write would
otherwise replace the file wholesale. `GRIMOIRE_SERVER` / `GRIMOIRE_TOKEN` still
work in that state, and `grimoire-cli login` writes a fresh config.

A write that fails — a read-only home, a full disk — is reported as an error, and
`login` and `config set` exit non-zero rather than claiming to have saved
anything. The version check's own write is the exception: it is a diagnostic, so a
failure there is a debug line and the check simply runs again next time.

## Precedence Order

Highest wins (`ConfigManager.Resolve`):

1. Command-line flags — `CommandHelper.BuildClient(serverOverride, tokenOverride)`
   accepts a per-call override; `systems` and `me` wire `--server`/`--token`
   through to it. `login`'s own `--server` writes straight to the file
   instead of going through this resolution.
2. Environment variables — `GRIMOIRE_SERVER`, `GRIMOIRE_TOKEN`
3. Config file (`~/.grimoire-cli/config.json`)

## Config Commands

| Command | Description |
|---------|-------------|
| `grimoire-cli config get` | Shows current config (`accessToken` masked to `***`, plus `configPath`, `lastVersionCheck`, `lastServerVersion`) |
| `grimoire-cli config set <key> <value>` | Sets a config value |

`config set` accepts **only** `server` as a key — `ApplyConfigSet` in
`src/GrimoireCli/Commands/ConfigCommand.cs` rejects anything else with
`Unknown config key: '<key>'. Valid keys: server` and exits 1. There is no
generic setter for arbitrary keys the way abs-cli allows; the token is only
ever written by `login`.

## Error Messages

- No server → `No server configured. Run: grimoire-cli login` (exit 1)
- No token → `Not authenticated. Run: grimoire-cli login` (exit 1)
- 401 from API → `Not authenticated, or the token has expired. Run: grimoire-cli login` (exit 2)

(`CommandHelper.BuildClient` for the first two; `GrimoireApiClient.EnsureSuccessAsync`
for the third — see [input-output.md](input-output.md) for the exit-code
convention behind the 1 vs. 2 split.)

## Deliberately absent

- **No `--config` flag or `GRIMOIRE_CONFIG` env var.** abs-cli doesn't have
  this either, but it's worth stating for grimoire-cli specifically: PR
  builds are installed and tested against a real server rather than a
  config-path override, and the dev container's `HOME` isn't the host's, so
  a per-invocation config path wouldn't buy test isolation the way it might
  elsewhere. If a real need for it shows up, it's a deliberate decision to
  revisit, not an oversight.
- **`GRIMOIRE_DEBUG=1`** is a config-adjacent environment variable but does
  not live in `AppConfig` — it's read directly in `Program.cs` and mirrors
  `--debug`. See [input-output.md](input-output.md).
