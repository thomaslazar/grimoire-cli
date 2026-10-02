# API key authentication

**Status:** approved
**Verified against:** `hunterreadca/grimoire:1.7.3`, the pinned stack

## Problem

Grimoire 1.7.3 adds personal API keys. For an unattended agent a key beats a
session: no 30-minute access token to renew, no 30-day refresh cookie to lose,
and permissions that narrow what the agent can touch. The CLI can only log in
with a password today.

This covers **authenticating with a key**. Managing keys (`/api/api-keys`) stays
out: keys can never manage keys (`routers/api_keys/_helpers.py:14-24`), so those
commands would always need a session, and the web UI already does it well.

## Verified server behaviour

Read from `backend/api_keys.py`, `backend/auth.py` and `backend/routers/` at
tag `v1.7.3`.

- **Sent as `X-API-Key`, and it wins over any session** (`auth.py:303`).
- **A key acts as its owner, at the owner's current role**, narrowed by its
  permissions (`api_keys.py` `authenticate`, module docstring). Role guards
  run as for the owner's own session, so the CLI's role tags stay right.
- **Permission per route**: the route's OpenAPI tag picks the permission
  through `PERMISSIONS`; `GET`/`HEAD` need `read`, anything else `write`, except
  a route marked `x-api-key-access: read` (body-only POSTs such as
  `metadata-search`). A route marked `none`, a tag in `EXCLUDED_TAGS`
  (`auth`, `api-keys`), or an unmapped tag is refused (`route_permission`,
  `required_level`). `"*"` grants every permission at a floor level.
- **Refusals** (`authenticate`):
  - 403 `API keys are disabled on this server` — `API_KEYS_ENABLED` off
    (`config.py:322`).
  - 429 — too many failed key attempts from this IP (`security.py`), and while
    blocked even a correct key is refused.
  - 401 `Invalid API key` / `API key has expired`.
  - 403 `API keys are not enabled for your account` — only admins have keys by
    default; anyone else needs `api_keys_enabled` set by an admin
    (`user_keys_allowed`). Guests never.
  - 403 `API keys cannot be used for this endpoint`.
  - 403 `This API key needs 'write' access to 'books' (it has 'read')`.
- **Unreachable with any key**: `GET /api/auth/me` and every other `auth` route,
  including `POST /api/auth/refresh`; key management; routes marked `none`
  (own password, account deletion, OPDS and calendar tokens).
- **`GET /api/about` is tagged `library`** (`routers/library/core.py:162`), so the
  CLI's version check needs `library` read.
- The spec carries every route's tag and its `x-api-key-access` marker.

## Design

### Credential

- `login --api-key-stdin` reads the key from the first line of stdin, validates
  it (below) and writes `apiKey` to the resolved config, clearing
  `accessToken` and `refreshToken`. `--server` resolves as today. It refuses
  `--username`, `--password` and `--password-stdin` alongside it (exit 1).
- Password `login` clears `apiKey`. A config holds one kind of credential.
- The key comes from the config file alone, as the session token does. No
  environment variable.
- `config get` gains `"apiKey": "***" | "(not set)"` and
  `"auth": "api-key" | "session" | "(none)"`.

### Client in key mode

When `apiKey` is set, every request sends `X-API-Key` and no `Authorization`
header. There is no proactive renewal and no `X-Token-Expired` retry: a key
cannot reach `/api/auth/refresh`.

### Validation and the version check

Both are one `GET /api/about` sent with the key:

- 200 — valid; the version is recorded as today.
- 403 naming the missing level (`This API key needs 'read' access to 'library'
  (it has 'none')`, `api_keys.py:387`) — valid, but without `library` read.
  Login saves the key and warns that the version check needs `library` read;
  the daily probe logs it at debug only.
- 401, or any other 403 (keys disabled, not enabled for the account, not
  usable on this endpoint — `api_keys.py:351-352, 376-381`) — login saves
  nothing and exits 2 with the server's detail.
- 429, any other status or a transport failure — login saves nothing and exits
  2.

### Errors

The message mapping (`EnsureSuccessAsync`) changes in both modes and in key mode:

- **403 with a role hint now also carries the server's detail**:
  `Permission denied. This operation requires the gm or admin role. <body>`.
  Under a key the detail is what names the missing permission.
- **401 in key mode**: `API key invalid or expired. Run: grimoire-cli login --api-key-stdin`.
- **429**: `Too many requests. <body>` in both modes.
- **Key mode 401 and 403 end with one line** pointing to the docs:
  `Key permissions per command: https://github.com/thomaslazar/grimoire-cli/blob/main/docs/grimoire-api-coverage.md`.
  The pointer lives in the error, where an agent needs it, rather than in every
  command's help.

### Docs

- `docs/authentication.md` gains **API keys**: how to log in with one, and the
  differences from a session (the refusals above, what keys cannot reach,
  expiry is final, the version check needs `library` read, the owner's current
  role applies).
- `docs/grimoire-api-coverage.md` gains a generated **Key** column:
  `<permission>: read|write`, or `—` where no key can call the route.
  `tools/generate-api-coverage.py` reads the tag and marker from the spec and
  `PERMISSIONS` / `EXCLUDED_TAGS` from `api_keys.py` in the same container, as
  it already reads roles from router source.
- `login` help gets a Notes paragraph; README gets the key login example and
  a link to both docs. No other command's help changes.

## Testing

- Unit: config credential exclusivity; header selection; no refresh in key
  mode; the error messages (pure function); login flag exclusivity.
- Smoke, on the local stack, against a separate `GRIMOIRE_CONFIG` file so the
  admin session the rest of the script uses is untouched. Keys are created with
  `curl` on `/api/api-keys` as admin and deleted by name first, so re-runs
  converge:
  - a `*: write` key logs in and runs `systems list`;
  - a `books: read` key gets a 403 on `books update` naming `books` and `write`,
    and logs in with the version-check warning;
  - a garbage key exits 2 at login and leaves the file unchanged.

## Scope

Not included: key management commands, an env-var credential. The roadmap
needs no line: this ships in the PR that decides it.
