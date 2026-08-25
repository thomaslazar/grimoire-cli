# Transparent token refresh (Grimoire 1.6.0)

**Status:** approved
**Workstream:** A of [grimoire-1.6.0-migration.md](../grimoire-1.6.0-migration.md)
**Verified against:** `hunterreadca/grimoire:nightly`, commit
`7f5937071f51dfc65bc09f5e5e49d33c431f0a5d` (`GET /api/about` reports version
`nightly`)

## Problem

1.5.6 issued one bearer JWT valid 30 days. 1.6.0 issues a **30-minute access
token** plus a 30-day refresh token delivered as an HttpOnly cookie. The CLI
stores only the access token and has no refresh path, so against a stock 1.6.0
server every command fails half an hour after `login` until the operator logs in
again. This is the breaking change of the 1.6.0 migration.

## Verified server behaviour

Measured against the build above, not read from the published docs.

**Login** (`POST /api/auth/login`) returns `{"token": …, "user": {…}}` and sets
two cookies:

```
grimoire_session=<jwt>;      HttpOnly; Max-Age=2592000; Path=/;         SameSite=lax
grimoire_refresh=<opaque>;   HttpOnly; Max-Age=2592000; Path=/api/auth; SameSite=strict
```

- The access token is a JWT carrying `sub`, `username`, `role`, `iat`, `jti`,
  `exp`, `sid`. Observed `exp - iat` is 1800s, matching
  `ACCESS_TOKEN_EXPIRE_MINUTES` = 30 (`backend/sessions.py:58`, env-overridable).
- The refresh token is **opaque random text, not a JWT**, so its expiry cannot be
  inspected locally the way `TokenHelper.GetExpiration` inspects the access
  token. Only the server knows when it dies.
- Both cookies carry `Max-Age` 30 days regardless of the JWT's real 30-minute
  life, so **cookie lifetime is not a usable expiry signal**.

**Refresh** (`POST /api/auth/refresh`) accepts the cookie alone — no bearer
header, and the spec declares no bearer security on it. It returns the same
`{"token", "user"}` shape as login and re-sets both cookies. The `sid` claim is
unchanged across a refresh: the session persists and only the tokens rotate.
`rotate_session` also slides `expires_at` 30 days forward from now, so an
actively used session does not age out.

**Replay is fatal, with no grace window.** `rotate_session` moves the old hash
into `previous_token_hash`, and `get_active_session` treats a hit on that column
as evidence of theft and revokes the session
(`backend/sessions.py:124-150`). Verified end to end: after rotating `T0 → T1`,
replaying `T0` returned `401 {"detail":"Invalid or expired refresh token"}` *and
killed `T1`*, which had been valid seconds earlier. There is no grace column, no
grace timestamp and no configuration knob — confirmed in the model
(`backend/models/users.py:85`) and the migration
(`0020_auth_sessions.py:38`).

**Expiry-specific 401s are distinguishable.** `get_current_user` answers an
expired JWT with `401`, `{"detail":"Token expired - please log in again"}` and
the header `X-Token-Expired: 1`, whose source comment states it "must stay
distinguishable from other 401s". Verified with a locally minted
expired-but-validly-signed token. A missing or malformed token instead yields
`{"detail":"Not authenticated"}` or `{"detail":"Invalid token"}` with **no** such
header.

**Access tokens are not checked against the session table.** `get_current_user`
only decodes the JWT (`backend/auth.py:286-296`). Revoking a session therefore
does not kill outstanding access tokens; they remain good until their own `exp`.
The blast radius of a revocation is the next refresh, not the command in flight.

**Revocation is routine, not exceptional.** The server revokes sessions on a
password change (all others — `backend/routers/users/me.py:59`), on an admin
edit carrying a `revoke_reason` (`routers/users/core.py:169`), and on guest
promotion or removal from a campaign. The web UI also exposes session
management directly. A dead session is an ordinary event and must not be
reported as a fault or a security incident.

**Refresh is rate-limited** by `AUTH_RATE_LIMIT` (default `10/minute`) like the
other credential-checking endpoints, because the cookie is a bearer credential.
The dev stack sets `RATE_LIMIT_ENABLED=false`; a production deployment may not.

## Design

### 1. Credential storage and scope

`AppConfig` gains `refreshToken`, spelled as abs-cli spells it:

```csharp
[JsonPropertyName("refreshToken")]
public string? RefreshToken { get; set; }
```

`ConfigManager.Resolve` carries it through from the file exactly as it already
carries `LastVersionCheck` and `LastServerVersion` — those fields are dropped
today unless explicitly copied, and the same trap applies here.

**The refresh token is used only when the access token also came from the file.**
A token supplied via `--token` or `GRIMOIRE_TOKEN` belongs to some other
session; refreshing it against the file's cookie would silently swap identity
mid-run and write a token to disk the operator chose to keep out of it. So
`Resolve` leaves `RefreshToken` null whenever the access token was overridden by
a flag or the environment. Those callers keep the current terminal-401
behaviour, which is also the 1.5.6 behaviour.

Writes go through a new `ConfigManager.UpdateTokens(access, refresh)` that
read-modify-writes via `Load()`, mirroring `UpdateVersionCheck` for the same
documented reason: never persist a *resolved* config, or an environment-supplied
token lands on disk.

### 2. Capturing the cookie at login

`HttpClientHandler.UseCookies` is set to `false`, so no in-memory cookie
container holds a credential the CLI believes it is managing explicitly. The
CLI reads the cookie off the response's `Set-Cookie` headers itself, so
ownership is unambiguous and nothing the CLI relies on is discarded —
authentication rides the bearer header.

A pure static locates the value, following the existing `ExtractToken` idiom so
it is unit-testable without a server:

```csharp
internal static string? ExtractCookie(IEnumerable<string> setCookieHeaders, string name)
```

It matches `name=` at the start of a header and returns everything up to the
first `;`. `LoginAsync` returns the body and the cookie value together;
`LoginCommand` saves both.

### 3. The refresh call

`Api.Api.Auth.Refresh.ToPostRequestInformation()` (exists, takes no body),
converted to a native request via the adapter, with `Cookie:
grimoire_refresh=<value>` added and **no** bearer header. The response body
yields the new access token through the existing `ExtractToken`, and the rotated
cookie through `ExtractCookie`. Both are persisted with `UpdateTokens`, written
back onto the in-memory `_config`, and the client's `Authorization` header is
updated in place, so the retry and every later request in the same process use
the new token.

### 4. When refresh happens

**Proactively**, in `PreflightAsync`: if the access token expires within 60
seconds and a refresh token is present, refresh before sending. The 60-second
threshold is abs-cli's, harvested. With no refresh token the existing warning
stands unchanged.

**Reactively**, in `SendAsync` and `SendStreamAsync`: if the response is `401`
**and** carries `X-Token-Expired` **and** a refresh token is present, refresh,
then send the request once more before handing the result to
`EnsureSuccessAsync`.

Keying the fallback on the header rather than on any 401 is a deliberate
divergence from abs-cli, which retries on every 401. Grimoire hands us an exact
signal, and refreshing on a permissions 401 would spend a request against a
rate-limited endpoint to no purpose.

**The retry must not lose the request body.** A native `HttpRequestMessage`
cannot be resent, so the retry re-converts the `RequestInformation`. Its
`Content` is a `Stream` that the first send has already read, so it is rewound
to position 0 before reconversion. This is safe for every request the CLI makes:
audited, every request body is a seekable `MemoryStream`: the six
`SetStreamContent` call sites in `Services/` all wrap `new MemoryStream(...)`
over an in-memory byte array, cover upload serialises an in-memory
`MultipartBody`, and the generated builders use `SetContentFromParsable`. A
body that ever reports `CanSeek == false` is not replayed at all — the original
401 is returned rather than a retry that would send a truncated body. abs-cli's
own retry rebuilds the request as `new HttpRequestMessage(method, endpoint)` and
drops the body; that bug is not to be carried over, and a test pins the
behaviour.

### 5. Failure, and 1.5.6 compatibility

Any refresh failure — 401, transport error, unparseable body — reports
`Session expired. Run: grimoire-cli login` and exits 2. The wording is neutral
because a revoked session is routine (see above); the triggering status lands on
the `--debug` line, not in the operator-facing message.

No version sniffing is required. A 1.5.6 server sets no `grimoire_refresh`
cookie, so `refreshToken` stays null, every branch above degrades to current
behaviour, and the 30-day token keeps working. `MinSupportedVersion` and
`MaxTestedVersion` are untouched — the supported-range reconciliation is
workstream C and waits for a released 1.6.0 tag.

### 6. Concurrency: deliberately not solved

Two invocations that both hold `T0` and both refresh will revoke the session,
because replay is theft to this server. That is not mitigated, for three
reasons:

- Nothing in the CLI issues concurrent requests: there is no `Task.WhenAll` or
  `Parallel` anywhere outside the generated tree, and every command path sends
  exactly one request — bulk commands send one batch request, not N. A single
  invocation therefore cannot refresh twice at once whatever it is doing.
- The consuming skills invoke the CLI serially, so the window never opens.
- Recovery is one `login`, and nothing is lost.

The alternative was a cross-process file lock around the read-check-refresh-write
stretch, with the waiter re-reading the config rather than refreshing. That
buys a wait-timeout policy, a new "another grimoire-cli is refreshing" failure
mode and concurrency tests, against a hazard that fires only under a usage
pattern this CLI does not have.

Note that abs-cli settles nothing here. ABS added a server-side grace period on
the previous refresh token — 60 seconds in 2.35, 10 minutes and configurable in
2.36 — precisely so concurrent refreshers cannot break each other, which is why
abs-cli's own documentation can dismiss the question. Grimoire has the opposite
policy. Recorded so a reader who knows abs-cli does not mistake the difference
for an oversight.

## Testing

**Unit** (stub handler, no sleeps, no live server):

- `ExtractCookie`: present, absent, several `Set-Cookie` headers, a name that is
  a prefix of another, no trailing `;`.
- Proactive refresh fires when the token is inside the 60-second threshold, and
  does not when it is not.
- Reactive refresh fires on `401` + `X-Token-Expired`, and **not** on a bare
  401, a 403, or a 401 when no refresh token is held.
- The retried request carries the original body — the abs-cli bug, pinned.
- No refresh is attempted when the access token came from a flag or the
  environment.
- Refresh failure exits 2 with the session-expired message.

**Smoke** (live, deterministic, no sleep): rotate the stored refresh token out
from under the CLI with a direct `curl`, then assert the next command exits 2
with a clean session-expired message and no stack trace.

**Known gap, stated rather than glossed.** CI never proves the happy path
against a live server: doing so needs either a sleep past a real expiry or a
shortened `ACCESS_TOKEN_EXPIRE_MINUTES` on the dev stack, both declined as not
worth the wall clock. The happy path rests on the unit tests above plus the
hand-run transcript recorded in
[grimoire-api-notes.md](../grimoire-api-notes.md).

## Documentation

- `docs/authentication.md` — rewrite the token model for 1.6.0, retire the "401
  is terminal" rule, and record the no-grace-window contrast with ABS.
- `docs/configuration.md` — the new field, and the note that a token supplied by
  flag or environment is good for 30 minutes on 1.6.0 with no renewal.
- `docs/grimoire-api-notes.md` — the verified refresh, rotation and replay
  behaviour above.
- `tools/generate-api-coverage.py` — mark `/api/auth/refresh` implemented and
  regenerate the table. Never hand-edit the markdown.
- `CLAUDE.md` — one line on the `X-Token-Expired` keying, so it is not "fixed"
  back to any-401.

No command is added or renamed and no user-visible flag changes, so the README
Commands table is unchanged.

## Out of scope

`POST /api/auth/logout`, `GET /api/auth/sessions` and the two revocation
endpoints. Logout was considered on the grounds that a refresh token sits on
disk for 30 days, but the capability already exists off-CLI: revoking the
session in the web UI or changing the password both kill it. It would be
convenience, not a missing remedy.
