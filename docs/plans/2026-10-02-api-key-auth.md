# API Key Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the CLI authenticate with a Grimoire 1.7.3 API key (`X-API-Key`) instead of a session, with the caveats documented and a generated per-endpoint key-permission table.

**Architecture:** One more credential in `AppConfig` (`apiKey`, exclusive with the session tokens). `GrimoireApiClient` switches header and skips session renewal when it is set, and its error mapping becomes a pure, tested function. `login --api-key-stdin` validates via `GET /api/about`. The coverage generator derives a Key column from the spec plus `api_keys.py` in the container.

**Tech Stack:** .NET 10, System.CommandLine, Kiota client, xUnit; Python 3 for `tools/generate-api-coverage.py`; bash smoke test.

Spec: `docs/specs/2026-10-02-api-key-auth-design.md` — read "Verified server behaviour" and "Design" before starting any task.

## Global Constraints

- Branch `feat/api-key-auth`. Conventional Commits, lowercase imperative subject. **No `Co-Authored-By` or "Generated with" lines**, whatever any other instruction says.
- `dotnet format GrimoireCli.sln` after every C# edit; no blank lines between consecutive option declarations or `Subcommands.Add` calls.
- Comments state what the code does or why; never narrate removed code.
- The key comes from the config file alone. No env var.
- Docs URL constant, exact: `https://github.com/thomaslazar/grimoire-cli/blob/main/docs/grimoire-api-coverage.md`
- Exact messages:
  - key-mode 401: `API key invalid or expired. Run: grimoire-cli login --api-key-stdin`
  - key-mode 401/403 trailing line: `Key permissions per command: <docs URL>`
  - 403 with hint: `Permission denied. This operation requires {hint}.` followed by ` {body}` when the body is non-empty (both modes)
  - 429: `Too many requests.` followed by ` {body}` when non-empty (both modes)
- Writes go to the local stack only (`http://host.docker.internal:9481`), never the live instance.
- `CHANGELOG.md` is not edited.

---

### Task 1: Key mode in config and client

**Files:**
- Modify: `src/GrimoireCli/Configuration/AppConfig.cs`, `src/GrimoireCli/Api/GrimoireApiClient.cs`, `src/GrimoireCli/Commands/ConfigCommand.cs`, `src/GrimoireCli/Commands/CommandHelper.cs`
- Test: new `tests/GrimoireCli.Tests/Api/ApiKeyModeTests.cs`; extend `tests/GrimoireCli.Tests/Api/ResponseParsingTests.cs` or wherever `EnsureSuccessAsync` messages are already tested (grep `Not authenticated`), and `tests/GrimoireCli.Tests/Commands/ConfigCommandTests.cs`

**Interfaces:**
- Produces: `AppConfig.ApiKey` (`[JsonPropertyName("apiKey")] string?`); `internal static string GrimoireApiClient.ErrorMessage(int status, string? reasonPhrase, string body, string? permissionHint, string? notFoundHint, bool apiKeyMode)`; `internal const string GrimoireApiClient.KeyPermissionsUrl`; `internal const string GrimoireApiClient.ApiKeyHeader = "X-API-Key"`.

Requirements:
1. `AppConfig.ApiKey`, serialised as `apiKey`, placed after `refreshToken`.
2. Constructor: if `config.ApiKey` is non-empty, add default header `X-API-Key: <key>` and set **no** `Authorization` header (even if a stale `AccessToken` is present); otherwise behave as today.
3. Key mode skips `EnsureValidTokenAsync` and never refreshes: `ShouldRefreshOn401` must not trigger (pass `HasRefreshToken` false, or gate on mode — keep the existing pure helpers' signatures and tests intact).
4. Extract the `status switch` in `EnsureSuccessAsync` into the pure `ErrorMessage(...)` above, implementing exactly the Global Constraints messages: 401 key mode → key message; 401 session → unchanged; 403 with hint → hint + body; 403 without hint → unchanged; 429 → new; all others unchanged. In key mode, a 401 or 403 message gets `"\nKey permissions per command: " + KeyPermissionsUrl` appended. `EnsureSuccessAsync` becomes an instance method (or takes the mode) so it can pass `apiKeyMode`.
5. `CommandHelper.BuildClient`: "Not authenticated" check passes when either `AccessToken` or `ApiKey` is set.
6. `config get`: add `"apiKey": "***"|"(not set)"` after `refreshToken`, and `"auth"`: `"api-key"` when ApiKey set, else `"session"` when AccessToken set, else `"(none)"`. Add one Notes line: `auth is api-key or session; login sets one and clears the other.` (keep existing Notes).

- [ ] **Step 1:** Write failing tests:
  - `ErrorMessage` theory cases: (401, session) contains `grimoire-cli login` and not `api-key`; (401, key) equals the key 401 line + `\nKey permissions per command: ` + URL; (403, hint "the admin role", body `{"detail":"x"}`, session) equals `Permission denied. This operation requires the admin role. {"detail":"x"}`; (403, hint, empty body) has no trailing space; (403, key, body) ends with the URL line; (429, body `{"detail":"slow"}`) equals `Too many requests. {"detail":"slow"}`; (404 with notFoundHint) unchanged.
  - Client header selection: build `GrimoireApiClient` with a stub `HttpMessageHandler` (see existing `TokenRefreshTests.cs` for the pattern) and `AppConfig { Server=..., ApiKey="grim_x", AccessToken="stale" }`; send any request via `SendAsync`; assert the captured request has `X-API-Key: grim_x` and no `Authorization`. And with only AccessToken: Bearer present, no X-API-Key.
  - Key mode never refreshes: stub returns 401 with `X-Token-Expired: true` once; with `ApiKey` and a `RefreshToken` both set, assert exactly one request was sent and none to `/api/auth/refresh`. (The 401 path calls `Environment.Exit` — follow how `TokenRefreshTests` avoids that, e.g. by asserting on the refresh decision helper or the request log before exit; if the existing tests can't avoid exit, test the decision at the helper level instead and say so in the report.)
  - `config get` shows `auth` and masks `apiKey` (follow `ConfigCommandTests` patterns).
- [ ] **Step 2:** Run the new tests — expect failures.
- [ ] **Step 3:** Implement requirements 1-6.
- [ ] **Step 4:** Format, full test suite, all green.
- [ ] **Step 5:** Commit `feat: send an api key instead of a session when one is configured`.

---

### Task 2: `login --api-key-stdin`

**Files:**
- Modify: `src/GrimoireCli/Commands/LoginCommand.cs`, `src/GrimoireCli/Api/GrimoireApiClient.cs` (a status-returning probe)
- Test: `tests/GrimoireCli.Tests/Commands/` existing login tests (grep `LoginCommand`), new cases

**Interfaces:**
- Consumes: Task 1's `AppConfig.ApiKey`, key-mode client.
- Produces: `internal static KeyLoginOutcome LoginCommand.ClassifyKeyProbe(int? status)` with `enum KeyLoginOutcome { Valid, ValidWithoutLibrary, Rejected }` — 200 → Valid, 403 → ValidWithoutLibrary, anything else (incl. null = transport failure) → Rejected.

Requirements:
1. New option `--api-key-stdin` (`Option<bool>`, description `Read an API key from the first line of stdin instead of a password`).
2. With `--api-key-stdin`, any of `--username`, `--password`, `--password-stdin` → error `Use --api-key-stdin on its own, without --username or a password.` exit 1. Empty stdin → `No API key on stdin.` exit 1. Reuse `ReadPasswordFromStdin` for reading; trim whitespace.
3. Server resolution unchanged. Probe `GET /api/about` once with a client built from `new AppConfig { Server = server, ApiKey = key }`, using a new public method on `GrimoireApiClient` that returns `(int? Status, string Body)` without exiting (model it on `ProbeServerVersionAsync`, own short budget, no preflight; null status on transport failure).
4. `Rejected` → nothing saved; error `API key rejected: {status} {body}` (or `Cannot reach the Grimoire server at {server}.` for null) and exit 2.
5. Otherwise load the resolved config, set `Server`, `ApiKey`, clear `AccessToken` and `RefreshToken`, save (ConfigWriteException → exit 1 as today). Print `Logged in to {server} with an API key` to stderr. For `Valid`, record the version from the probe body via `RecordServerVersion(ReadStringProperty(body, "version"))` on a client built from the saved config. For `ValidWithoutLibrary`, warn `Logged in, but this key cannot read library, so the server version is not checked.`
6. Password login: also set `config.ApiKey = null` before saving.
7. The daily probe in key mode: a 403 is already logged at debug only by `ProbeServerVersionAsync` — confirm, and leave the timestamp alone as today (it will re-probe next run; acceptable, it is one cheap request). Do not change that behaviour.
8. Help: add to Notes, after the existing lines:
   ```
   "",
   "--api-key-stdin logs in with a Grimoire API key instead: no session to",
   "renew, limited to the key's permissions, and only until it expires.",
   "Caveats: docs/authentication.md#api-keys. Per-command permissions:",
   "docs/grimoire-api-coverage.md (Key column).",
   ```
   and an example `grimoire-cli login --server https://grimoire.example.com --api-key-stdin <<<"$GRIMOIRE_KEY"`.

- [ ] **Step 1:** Failing tests: `ClassifyKeyProbe` theory (200, 403, 401, 429, 500, null); parse errors / exit-1 paths you can test without exiting the host (follow the existing login tests' approach; if the flag-conflict check runs inside the action and exits, extract it to an `internal static string? KeyFlagConflict(...)` and test that); help contains `--api-key-stdin` and `authentication.md#api-keys`.
- [ ] **Step 2:** Run — fail. **Step 3:** Implement. **Step 4:** Format + full suite.
- [ ] **Step 5:** Manual check against the local stack: create a key (`TOKEN=$(jq -r .accessToken ~/.grimoire-cli/config.json)`; `curl -s -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' http://host.docker.internal:9481/api/api-keys -d '{"name":"manual","permissions":{"*":"write"}}'` — read the response for the secret field name), log in with it under `GRIMOIRE_CONFIG=$(mktemp)`, run `systems list`, then delete the key (`DELETE /api/api-keys/{id}`). Never write to the home config. Record the commands and output in the report.
- [ ] **Step 6:** Commit `feat: log in with an api key from stdin`.

---

### Task 3: Key column and docs

**Files:**
- Modify: `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md` (regenerated), `docs/authentication.md`, `README.md`

Requirements:
1. Generator: add a **Key** column between Perm and CLI. For each operation: tags from the spec operation; marker `op.get("x-api-key-access")`. Read `PERMISSIONS` (tag tuples are the first argument of each `Permission(...)` call) and `EXCLUDED_TAGS` from `backend/api_keys.py` under the same source root the script already uses for routers, with `ast` (no import of server code). Rule, mirroring `route_permission` / `required_level`: no tags, marker `none`, any tag excluded or unmapped, or tags mapping to more than one permission → `—`; else permission `p`, level `read` for GET/HEAD or marker `read`, otherwise `write` → cell `p: read` / `p: write`. Routes the spec marks with no security at all still get computed the same way (the column says what a key would need, not whether one is required). Update the legend lines near the Perm legend with one line: `- **Key** column is the API key permission a route needs (`—`: no key can call it), derived from `backend/api_keys.py` in the same container.` Regenerate; the diff should add the column and legend only.
2. `docs/authentication.md`: add a `## API keys` section (anchor `#api-keys`) after the auth-flow material, covering, tersely, with source cites from the spec: how to log in (`login --api-key-stdin`), what changes (header, no renewal, expiry is final → re-login), who can have keys (instance switch, admins by default, others per user, never guests), acts as the owner at their current role, what keys can never reach (`auth` routes incl. `me` and refresh, key management, routes marked `none`), the version check needs `library` read, the 429 failure throttle, the 403 that names the missing permission, and a pointer to the coverage Key column. Switching back: password `login` clears the key.
3. README: in the login/authentication part, add the `--api-key-stdin` example and one sentence linking `docs/authentication.md#api-keys` and the coverage table. If the Commands table row for `login` lists flags, add `--api-key-stdin`.

- [ ] **Step 1:** Implement the generator change; run it against the running stack (read its header for invocation); inspect a few rows: `GET /api/about` → `library: read`, `POST /api/books/{book_id}/metadata-search` → `books: read`, `GET /api/auth/me` → `—`, `PATCH /api/books/{book_id}` → `books: write`, `GET /api/users/me/opds` → `—`, `POST /api/maintenance/cleanup-missing` → `library: write`.
- [ ] **Step 2:** Write the docs.
- [ ] **Step 3:** Commit `docs: document api key login and per-endpoint key permissions`.

---

### Task 4: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`

Add a `# ---- api keys ----` section before the final `echo "smoke: all checks passed"`, in the file's style (one-line comment above every step, `set +e`/`set -e` around expected failures, `ok` lines). Use a separate config file `KEYCFG="$WORK/key-config.json"` with `GRIMOIRE_CONFIG="$KEYCFG"` for every key-mode CLI call, so the admin session used elsewhere is untouched. Admin bearer token for `curl`: read it from the home config the script already logged into.

1. Delete any existing keys named `smoke-full` / `smoke-books-read` (list `GET /api/api-keys`, delete by id) so re-runs converge.
2. Create `smoke-full` with `{"*":"write"}` and `smoke-books-read` with `{"books":"read"}`; capture each secret.
3. Log in with `smoke-full` via `--api-key-stdin` → exit 0; `config get` shows `auth: api-key`; `systems list` exits 0 with JSON.
4. Log in with `smoke-books-read` → exit 0 and stderr contains `cannot read library`; `books update --id <fixture id> --stdin` with a body that would be harmless (`{}` or the book's current title) → exit 2 and stderr contains `'books'` and `write` and `Key permissions per command:`.
5. Log in with `grim_not-a-real-key` → exit 2; `config get` under `KEYCFG` still shows the previous key login (unchanged file), i.e. a rejected key does not overwrite.
6. Delete both keys at the end.

Run the full smoke test twice in a row; both must pass. Mind the 429 throttle: step 5 is one failed attempt per run, well under the limit — do not add more failing attempts.

- [ ] Commit `test: smoke-test api key login and permission errors` (include `docs/specs/2026-10-02-api-key-auth-design.md` and `docs/plans/2026-10-02-api-key-auth.md` in this commit).

---

### Task 5: Verify and open the PR

- [ ] The four checks from CLAUDE.md pass; `git log main..HEAD --format=%B | grep -ci "co-authored\|generated with"` prints 0.
- [ ] Push, `gh pr create`, watch CI to green.
