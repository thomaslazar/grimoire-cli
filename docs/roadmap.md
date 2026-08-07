# Roadmap

## Now

Scaffolding plus a proven `login`. `src/` has `login`, `config get|set`,
`systems list|get` and `self-test`; `docker/smoke-test.sh` covers the live HTTP
path and runs in CI against a seeded local stack.

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

2. **Fixture generation** — `docker/seed.sh` and library content under
   `docker/library/`. Login needed no content, so this was deferred. The scanner
   behaviour it has to satisfy is in
   [grimoire-api-notes.md](grimoire-api-notes.md).

3. **`management-repo`** — the skills/rules repo, counterpart to
   `management-repo`. Not started.

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
