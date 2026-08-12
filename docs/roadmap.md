# Roadmap

## Now

`src/` has `login`, `config get|set`, `me`, `self-test` and
`systems list|get|update|batch-update|batch-tag`, all against endpoints tracked
in [grimoire-api-coverage.md](grimoire-api-coverage.md), which owns the current
coverage counts. `docker/seed.sh` generates fixture library content
and `docker/smoke-test.sh` covers the live HTTP path — including the write
commands — against it in CI.

The metadata-write design question — typed flags versus a raw-JSON body — is
decided for systems: `systems update`/`batch-update`/`batch-tag` take a body
from `--input` or `--stdin`, validated against the endpoint's generated model,
which rejects unknown keys at any depth before the request is sent
([grimoire-api-coverage.md](grimoire-api-coverage.md) records the routes; the
design is in `docs/specs/2026-08-10-systems-write-commands-design.md`).

## Next

1. **Books have no commands at all.** The larger metadata surface — read
   side, then `PATCH /api/books/{id}`, `POST /api/books/bulk`, `/bulk/tags` —
   is its own design, following the same `--input`/`--stdin` and strict-DTO
   shape settled for systems.
2. **Remaining systems endpoints**: cover (get/upload/delete), book-folders
   (list/update), and the metadata-lookup trio (`metadata-sources`,
   `metadata-search`, `metadata-fetch`).

### Reopened by Grimoire v1.5.5

- **Bulk endpoints shipped.** `POST /api/{books,systems,maps,tokens,audio}/bulk`
  and `/bulk/tags` are released, not unreleased `main` work as
  `docs/plans/2026-08-06-login-and-smoke-test.md` recorded. `systems batch-update`
  and `systems batch-tag` are the first commands built against them.
- **29 new routes were uncovered** at the time: 13 bulk, 7 add-on management, 6
  metadata lookup (`metadata-sources` / `metadata-search` / `metadata-fetch` on
  books and systems), 3 system cover. Systems' bulk pair is now covered; the
  rest are tracked under "Next" above and in `docs/grimoire-api-coverage.md`.
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
