# Roadmap

## Now

`src/` has `login`, `config get|set`, `systems list|get` and `self-test`, all
against endpoints tracked in [grimoire-api-coverage.md](grimoire-api-coverage.md).
`docker/seed.sh` generates fixture library content and `docker/smoke-test.sh`
covers the live HTTP path against it in CI.

## Next

1. **The metadata command surface.** The job this CLI exists for: read and fix
   metadata on existing entries — `PATCH /api/systems/{id}` (17 fields),
   `PATCH /api/books/{id}` (18 fields), and `POST /api/rescan` to reapply OPF
   sidecars. Design it before extending `src/`.

   One question is parked, not decided: typed flags for the flat fields plus a
   `--json` escape hatch for the three nested array-of-object fields
   (`publishers`, `urls`, `character_builder_urls`), versus a raw-JSON-body-only
   interface. What bears on it is in
   [grimoire-api-notes.md](grimoire-api-notes.md) — PATCH drops nulls, and
   unknown keys are silently ignored, so a typo'd field name in a raw body
   returns `{"status":"ok"}` having changed nothing.

### Reopened by Grimoire v1.5.5

- **Bulk endpoints shipped.** `POST /api/{books,systems,maps,tokens,audio}/bulk`
  and `/bulk/tags` are released, not unreleased `main` work as
  `docs/plans/2026-08-06-login-and-smoke-test.md` recorded. The parked metadata
  command design question — typed flags plus a `--json` escape hatch, versus a
  raw-JSON body — now has a third option, and needs deciding before that command
  is built.
- **29 new routes are uncovered**: 13 bulk, 7 add-on management, 6 metadata
  lookup (`metadata-sources` / `metadata-search` / `metadata-fetch` on books and
  systems), 3 system cover. See `docs/grimoire-api-coverage.md`.
- **Metadata add-ons** (`backend/addons/`) fetch server-side with a per-field
  diff review, and ship with DriveThruRPG and TTRPG Wiki sources. They cover
  `isbn`, `artists` and `genres` on books and `system_family` on systems — work
  previously assumed to be CLI-only.

## Known wrinkles

- CI pulls the Grimoire image unauthenticated, so a Docker Hub rate limit would
  surface as a red `smoke-test` job on an unrelated PR. Needs a repository secret.
- `main` is unprotected: GitHub Free allows neither branch protection nor
  rulesets on a private repo. Apply protection when the repo goes public,
  requiring the `unit-test` and `smoke-test` checks with zero required approvals
  (a solo maintainer cannot approve their own PR).

## Resolved on the systems-commands branch

Kept because each one records behaviour that is easy to reintroduce.

- `LoginCommand` used to wrap the post-save `/api/about` version check in
  the same `try` as the login itself, so a transient failure there reported
  `Login failed:` and exited 2 *after* the token was already written. Grimoire's
  login response carries no version field (confirmed against `temp/grimoire`
  v1.5.4 and the live login body), so abs-cli's read-it-off-the-login-body trick
  isn't available; the probe now runs after login's `try` in its own `try`, and
  its failure is a stderr warning that exits 0 — login genuinely succeeded.
- `systems get --id ""`, `--id .`, and `--id ../about` used to crash —
  an unhandled `JsonException` and a raw stack trace at exit 1, because each
  misses the `/api/systems/{system_id}` route and falls through to Grimoire's
  SPA catch-all, which answers with an HTML 200. The typed client overloads
  (`GrimoireApiClient.GetAsync<T>` etc.) now catch `JsonException` during
  deserialization and route it through the same log-and-exit(2) path as any
  other API error, so all three exit 2 with a readable "not JSON" message
  instead. Covered by `docker/smoke-test.sh`.

## Parity with abs-cli

`abs-cli` is the reference (see CLAUDE.md). The docs set and the release plumbing
are now in place, and both first-release prerequisites external to this repo are
done: the `thomaslazar/homebrew-grimoire-cli` tap repository exists, and the
`HOMEBREW_TAP_TOKEN` secret was set 2026-08-09. See [releasing.md](releasing.md).
What remains is narrower:

- `install.sh` / `install.ps1` resolve GitHub release assets, so they do nothing
  until the first tag exists.

### Unblocked by Grimoire v1.5.6

- **Nested containers shipped** (upstream #301). `.system-family-container`,
  `.publisher-container` and a generic `.container` now exist, containers
  recurse, and `system_depth` follows the nesting (`2 + depth`) instead of the
  old constant 3. A family container fills in each child's `system_family`;
  a publisher fills in `publishers`.
- This unblocks the DSA layout that `grimoire-management`'s library-structure
  design recorded as waiting on a tagged release, and it means `system_family`
  is no longer PATCH-only for shelves that adopt a family container.
- The fixtures deliberately use no family container yet, so `seed.sh` still
  PATCHes `system_family`. Exercising a nested shelf is worth doing when the
  real library adopts one.
