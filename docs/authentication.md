# Authentication

## Token Model (Grimoire 1.5.6)

Grimoire uses `HTTPBearer` with a single JWT. There is no access/refresh pair:

- **Token** — 30 days expiry, issued by `POST /api/auth/login`. Not configurable
  per-request; it's whatever the server mints.
- **No refresh endpoint.** This is the sharpest divergence from abs-cli, which
  refreshes transparently. In Grimoire, an expired token has exactly one remedy:
  `grimoire-cli login` again.

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
   but the live key is `token`.
2. **Pre-request check:** Before every API call, `WarnIfTokenExpired` decodes
   the JWT payload (`TokenHelper.GetExpiration`, base64 + `exp` claim, no
   crypto) and logs a warning if the token expires within 60 seconds. This
   is a warning only — there is nothing to refresh to, so the request still
   goes out and the server will answer with its own 401 if the token is
   actually dead.
3. **No proactive or fallback refresh.** Unlike abs-cli, a 401 is terminal:
   `EnsureSuccessAsync` maps it straight to
   `Not authenticated, or the token has expired. Run: grimoire-cli login`
   and exits 2.
4. **Re-login:** Same command either way —
   `grimoire-cli login --server https://grimoire.example.com`.

This is *not* transparent to the user the way abs-cli's refresh is. Every 30
days (or whenever the server revokes a token), the next command fails with
the message above and a fresh `login` is required.

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
  `WarnIfTokenExpired`, `EnsureVersionCheckedAsync`, `RecordServerVersion`.
- `src/GrimoireCli/Api/TokenHelper.cs` — JWT `exp` decoding, no signature
  verification (the CLI trusts its own token; verification is the server's
  job).
- `src/GrimoireCli/Commands/LoginCommand.cs` — prompts, stdin/flag password
  handling, config write.
- Grimoire server: `docs/grimoire-api-notes.md` "Auth" section, verified
  against `temp/grimoire` at v1.5.6.

## Diagnostic logging

Run any command with `--debug` (root option, before the subcommand) or set
`GRIMOIRE_DEBUG=1` to emit token-expiry checks and server-version comparisons
to stderr. See [input-output.md](input-output.md) for full logging details.
The bearer token itself is never logged, at any verbosity.
