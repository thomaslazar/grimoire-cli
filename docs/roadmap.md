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

- `LoginCommand` wraps the post-save `/api/about` version check in the same `try`
  as the login itself, so a transient failure there reports `Login failed:` and
  exits 2 *after* the token was already written. Worth fixing when the login path
  is next touched.
- CI pulls the Grimoire image unauthenticated, so a Docker Hub rate limit would
  surface as a red `smoke-test` job on an unrelated PR. Needs a repository secret.
- `main` is unprotected: GitHub Free allows neither branch protection nor
  rulesets on a private repo. Apply protection when the repo goes public,
  requiring the `unit-test` and `smoke-test` checks with zero required approvals
  (a solo maintainer cannot approve their own PR).
- `systems get --id ""`, `--id .`, and `--id ../about` all crash the same
  way — an unhandled `JsonException` and a raw stack trace at exit 1,
  instead of the exit-2 "not found" that ordinary unknown ids produce. Only
  the `../about` case changed: `Uri.EscapeDataString` on the path segment
  stopped it from reaching `/api/about` and printing a full but bogus system
  object (every field null, exit 0) as if it were real data — that
  cross-endpoint read is genuinely closed. But Grimoire's ASGI layer decodes
  `%2F` before routing, so the encoded segment still misses the single-segment
  `{system_id}` route and falls through to the SPA's HTML catch-all, landing
  on the same crash as the empty and dot cases. So this is one failure mode
  with three triggers, not two separate bugs — a guard scoped to empty-or-dot
  ids would leave `../about` crashing. The durable fix is to treat a non-JSON
  response body as an API error instead of letting the deserializer throw,
  with an id guard as an optional extra. (The encoding fix's test,
  `tests/GrimoireCli.Tests/ApiEndpointsTests.cs:12`, only checks the built
  path contains no literal `../` and never round-trips against a server —
  which is how this went from one bug to another unnoticed.)
