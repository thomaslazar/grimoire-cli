# Configuration

## Config File

Location: `~/.grimoire-cli/config.json`

```json
{
  "server": "https://grimoire.example.com",
  "accessToken": "eyJhbG..."
}
```

Keys are camelCase in the file (`AppConfig` in
`src/GrimoireCli/Configuration/AppConfig.cs`, `[JsonPropertyName]`-mapped).
There is no `refreshToken` or `defaultLibrary` key — Grimoire issues no
refresh token, and there's no equivalent of abs-cli's default-library concept
yet (single-system live instance; `systems` commands take `--id` directly).

## Precedence Order

Highest wins (`ConfigManager.Resolve`):

1. Command-line flags — `CommandHelper.BuildClient(serverOverride, tokenOverride)`
   accepts a per-call override, but no command currently wires a `--server`/
   `--token` flag through to it, so this tier is plumbing, not a usable flag,
   as of this writing. `login`'s own `--server` writes straight to the file
   instead of going through this resolution.
2. Environment variables — `GRIMOIRE_SERVER`, `GRIMOIRE_TOKEN`
3. Config file (`~/.grimoire-cli/config.json`)

## Config Commands

| Command | Description |
|---------|-------------|
| `grimoire-cli config get` | Shows current config (`accessToken` masked to `***`, plus `configPath`) |
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
