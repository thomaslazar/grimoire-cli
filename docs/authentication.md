# Authentication

## Token Model (Grimoire 1.6.0)

`POST /api/auth/login` issues a pair:

- **Access token** — a JWT, 30 minutes (`ACCESS_TOKEN_EXPIRE_MINUTES`,
  env-overridable server-side), returned in the response body. This is the
  bearer credential on every request.
- **Refresh token** — 30 days, delivered only as the `grimoire_refresh` cookie
  (`HttpOnly`, `Path=/api/auth`, `SameSite=strict`). It is opaque text, not a
  JWT, so its expiry cannot be inspected locally the way the access token's
  can; only the server knows when it dies.

Both cookies carry `Max-Age=2592000` regardless of the JWT's real 30-minute
life, so cookie lifetime is not an expiry signal.

`POST /api/auth/refresh` authenticates on the cookie alone and returns a new
pair, rotating both.

## HTTP Headers

All requests include `User-Agent: grimoire-cli/{version}` (`GrimoireApiClient`
constructor). The version string carries CI's build stamp
(`0.1.0+pr-1.a1b2c3d` for a PR build, bare `0.1.0` for a release), so server
logs can identify which build called.

## Auth Flow

1. **Login:** `POST /api/auth/login` with `{username, password}`. The response
   body is untyped in the spec (FastAPI without `response_model`), so the
   token is located by inspection rather than a generated model — see
   `ExtractToken` in `src/GrimoireCli/Api/GrimoireApiClient.cs`. Verified shape
   is `{"token": ..., "user": {...}}`; `ExtractToken` also tries
   `access_token` and `accessToken` in case a future response changes key,
   but the live key is `token`. The refresh token is not in the body at all —
   `ExtractCookie` reads it off the `Set-Cookie` headers, and `LoginCommand`
   saves both.
2. **Proactive renewal:** before every API call, `EnsureValidTokenAsync`
   decodes the JWT payload (`TokenHelper.GetExpiration`, base64 + `exp` claim,
   no crypto). Within 60 seconds of expiry, and with a stored refresh token, it
   refreshes first and sends with the new token. With no refresh token it warns
   instead and the request goes out as it stands.
3. **Fallback on `X-Token-Expired`:** a `401` carrying that header means the
   access token expired, so the CLI refreshes and sends the request once more.
   The retry rebuilds the native request from the same `RequestInformation` —
   an `HttpRequestMessage` cannot be resent — and rewinds the body stream
   first, so the second attempt carries the same content. A body that cannot be
   rewound is not replayed; the original 401 stands. Every other 401 is
   terminal: `EnsureSuccessAsync` maps it to
   `Not authenticated, or the token has expired. Run: grimoire-cli login`
   and exits 2.
4. **Refresh failure:** any failure — 401, transport error, unparseable body —
   reports `Session expired. Run: grimoire-cli login` and exits 2. The
   triggering status goes to the `--debug` line, not the operator-facing
   message, because a dead session is routine: the server revokes all other
   sessions on a password change, all of a user's on an admin edit carrying a
   `revoke_reason`, and on guest promotion or removal from a campaign. The web
   UI also manages sessions directly.
5. **Re-login:** Same command either way —
   `grimoire-cli login --server https://grimoire.example.com`.

A refreshed pair is written back to the config file, so renewal persists across
invocations.

### Concurrency, and the contrast with abs-cli

Grimoire treats a replayed refresh token as theft: `rotate_session` keeps the
old hash in `previous_token_hash`, and a hit there revokes the session —
killing the token that replaced it. There is no grace window and no knob. ABS
instead grants a grace period on the previous refresh token (60s in 2.35, 10
minutes and configurable in 2.36), which is why abs-cli can dismiss the
question of two processes refreshing at once.

The CLI does not mitigate this. Every command path sends exactly one request —
bulk commands send one batch request, not N — so a single invocation cannot
refresh twice at once, and the consuming skills invoke the CLI serially.
Recovery is one `login`.

## Auth Commands

| Command | Description |
|---------|-------------|
| `grimoire-cli login --server <url>` | Server via flag, prompts for username/password. |
| `grimoire-cli login --server <url> --username <u> --password-stdin` | Password piped via stdin (first line only) — the scriptable path. |
| `grimoire-cli login --server <url> --username <u> --password <pw>` | Password via flag — visible in process list and shell history, discouraged in help text. |

`login` also calls `GET /api/about` right after saving the token and compares
the reported server version against `MinSupportedVersion` /
`MaxTestedVersion` (`GrimoireApiClient.RecordServerVersion`), warning on
stderr if the server is older or newer than the tested range (1.5.6–1.5.6
today). This is a forced check; see
[grimoire-compatibility.md](grimoire-compatibility.md#runtime-check) for the
24-hour cadence that runs on every other command.

The confirmation line on stderr reads `Logged in to {server} (session renews
automatically)` when a refresh token was stored, and `(token expires
yyyy-MM-dd)` when the server issued none.

OIDC accounts cannot log in through this command — Grimoire exposes OIDC on
the server, but `grimoire-cli login` only ever calls the local
username/password path (`POST /api/auth/login`).

## Roles and guest mode

Grimoire has three account roles — `admin`, `gm`, `player` — plus a
guest-login mode (`POST /api/auth/guest-login`). Reads generally require a
non-guest account; writes generally require `gm` or `admin`. The per-endpoint
breakdown is generated, not hand-maintained, in
[grimoire-api-coverage.md](grimoire-api-coverage.md).

Commands whose endpoint needs a non-default role are tagged with
`command.AddRoleRequired("<role>")`, which surfaces a "Role required" section
at the top of that command's `--help`. `systems list` / `systems get` carry
no tag — any authenticated non-guest can read them.

## Rate limiting

Auth endpoints are rate-limited server-side: `AUTH_RATE_LIMIT` defaults to
`10/minute`, and `RATE_LIMIT_ENABLED=false` disables it entirely
(`backend/security.py` in the Grimoire source). The local dev stack
(`docker/docker-compose.yml`) sets `RATE_LIMIT_ENABLED=false`, so repeated
`login` calls during development or smoke tests don't trip the limit. A
production deployment may not — a burst of failed logins there can return
429s that look like an outage.

## Source Reference

- `src/GrimoireCli/Api/GrimoireApiClient.cs` — `LoginAsync`, `ExtractToken`,
  `ExtractCookie`, `ShouldRefreshProactively`, `ShouldRefreshOn401`,
  `RefreshAsync`, `SendWithRefreshAsync`, `EnsureValidTokenAsync`,
  `EnsureVersionCheckedAsync`, `RecordServerVersion`.
- `src/GrimoireCli/Api/TokenHelper.cs` — JWT `exp` decoding, no signature
  verification (the CLI trusts its own token; verification is the server's
  job).
- `src/GrimoireCli/Configuration/ConfigManager.cs` — `Resolve` scoping and
  `UpdateTokens`.
- `src/GrimoireCli/Commands/LoginCommand.cs` — prompts, stdin/flag password
  handling, config write.
- Grimoire server: `docs/grimoire-api-notes.md` "Auth" section.

## Diagnostic logging

Run any command with `--debug` (before or after the subcommand) or set
`GRIMOIRE_DEBUG=1` to emit token-expiry checks and server-version comparisons
to stderr. See [input-output.md](input-output.md) for full logging details.
The bearer token itself is never logged, at any verbosity.
