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
are now in place; what remains is the prerequisites a first release needs, which
are external to this repo:

- a `thomaslazar/homebrew-grimoire-cli` tap repository, and a `HOMEBREW_TAP_TOKEN`
  secret, or the `update-homebrew` job fails after the binaries are already
  attached. See [releasing.md](releasing.md).
- `install.sh` / `install.ps1` resolve GitHub release assets, so they do nothing
  until the first tag exists.
