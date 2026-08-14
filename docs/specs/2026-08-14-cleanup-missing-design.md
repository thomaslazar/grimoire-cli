# `library cleanup-missing` — design

Date: 2026-08-14
Status: draft, awaiting review

## Goal

One command over `POST /api/maintenance/cleanup-missing`, the last endpoint the
add-on branch deferred:

```
library cleanup-missing
```

The [add-on spec](2026-08-14-addons-commands-design.md) put it out of scope with
"the next branch", the metadata branch was that branch and did not carry it, and
it never reached the roadmap — so it fell through. Coverage records the gap as
`maintenance 0 / 2`.

Out of scope: `GET /api/health`, the other operation the coverage table groups
under `maintenance` — see [Why health stays uncovered](#why-health-stays-uncovered).

## Grounding

Verified against Grimoire v1.5.6 by reading `temp/grimoire`:
`backend/routers/maintenance/core.py` and `backend/routers/maintenance/_helpers.py`.

### What the endpoint does

No parameters, no body, `require_admin`. It returns

```json
{"removed": {"books": 0, "maps": 0, "tokens": 0, "audio": 0, "systems": 0}}
```

with the five keys fixed by `_do_cleanup` (`_helpers.py:113`).

Per book whose file is gone it deletes that book's FTS rows
(`DELETE FROM book_search`), every `Bookmark` pointing at it, and then the book,
committing after each one so the write lock is released between rows. After the
book sweep it prunes game systems whose books are all gone
(`_prune_orphaned_systems`) — keeping any that a campaign references or that
still has a surviving child — then sweeps maps, tokens and audio the same way.

Two consequences worth carrying into help text, because neither is guessable:

- **It deletes more than book rows.** Bookmarks are user data, and a rescan
  restores neither them nor any hand-entered metadata — only the file's own
  indexable content.
- **Committing per row means partial application.** A failure part-way through
  leaves earlier removals applied; the handler's `except` rolls back only the
  uncommitted remainder.

### Absent is not the same as hung

`_path_exists` (`_helpers.py:13-30`) runs `os.path.exists` on a daemon thread
with a 5-second join. If the thread is still alive it **returns True** — a hung
or stalled mount is treated as present and skipped, deliberately, "to avoid
false deletion".

A directory that is simply *absent* is not that case: the check returns False
promptly and every row beneath it is removed. So the hazard is narrow and
specific — an unmounted-and-gone library, or a bind target pointing at an empty
path — rather than "any storage problem". Help text must say which is which, or
it teaches the wrong caution.

### A scan blocks it

`cleanup_missing` raises `409` with
`"A library scan is already running; retry after it completes."` when
`_lib._get_status()["running"]` is true. The message is already actionable, and
it names the state the existing `library scan-status` command reports.

## The command

| Command | Endpoint | Flags |
|---|---|---|
| `library cleanup-missing` | `POST /api/maintenance/cleanup-missing` | `--server`, `--token` |

`AddRoleRequired("admin")` and `permissionHint: "the admin role"`, matching
`require_admin`.

### Why it joins `library`

The group's three existing commands are `/api/rescan`, `/api/scan-status` and
`/api/cancel-scan` — top-level paths, so `library` is already a concern-based
grouping rather than a path segment mirrored into a noun. Nothing is bent by
adding a fourth.

The positive reason is the interlock: cleanup is refused while a scan runs, and
the command that answers "is one running?" is `library scan-status`. A
one-command `maintenance` group would mirror the path and separate the pair.

### No confirm gate

`CLAUDE.md` has carried "No confirm-gated command" as a deliberate deviation
from abs-cli, whose `libraries delete` takes a type-the-name prompt, with the
note that the first delete command decides whether to adopt it. **This is that
decision, and the answer is no.**

- **The consumers are agents.** A prompt on stdin is either bypassed with a flag
  that becomes boilerplate, or it hangs a non-interactive caller. It buys
  attention from a reader who is not there.
- **The operation is a near-no-op in normal use.** It is run deliberately after
  restructuring the library on disk, and on a healthy library every count is
  zero. A gate on the ordinary path trains callers to skip gates.
- **The warning is what carries the weight**, in the help text where an agent
  reads it, naming the bookmark loss and the absent-vs-hung distinction.

`CLAUDE.md`'s deviation note is updated in this branch to record the decision
rather than leaving the question open.

### Exit codes

Exit 0 on HTTP 200, whatever the counts. Exit 2 on the 409 and on any other HTTP
error, with the server's message on stderr. No new codes.

Exit 3 was considered and rejected: in this CLI it means "HTTP 200, and not what
you asked for" — a bulk operation with a non-empty failure list. A cleanup that
removed rows did exactly what was asked, and the run that removes the most is
the legitimate post-restructure one. Signalling it as an anomaly would cry wolf
on the command's main use.

A derived stderr warning on non-zero counts was also rejected: reading a
response to emit a warning is precisely what thin pass-through excludes, and the
counts are already on stdout.

### No 409 hint

`GrimoireApiClient.EnsureSuccessAsync` has hint parameters for 403 and 404 only
(`GrimoireApiClient.cs:381,384`); every other status falls through to the
server's own body. The 409 body is already actionable and names the blocking
state, so a fourth hint parameter would be machinery serving one call site with
a worse message than it replaces.

### Why `health` stays uncovered

`GET /api/health` is declared in `main.py:195` with `tags=["maintenance"]`, which
is the only reason the generated coverage table groups it here; it is not in the
maintenance router. It is unauthenticated, and it answers `200` with a per-check
status or `503` when the database or the configured page cache is unreachable.

Covering it would force a new exit-code decision — on a 503 the body *is* the
answer, so the command would either exit non-zero while printing the useful
JSON, or exit 0 on a failed probe — and `self-test` already occupies "can I
reach this server", while `docker/smoke-test.sh` probes health with `curl`
before the CLI is involved. If it is ever wanted, folding it into `self-test` is
the cheaper move than a command whose whole design question is what to do with a
503. Coverage therefore reads `maintenance 1 / 2`, which is the honest number.

## Response DTOs

New, both registered on `AppJsonContext`:

- **`CleanupCounts`** — `books`, `maps`, `tokens`, `audio`, `systems`, all `int`.
- **`CleanupResult`** — `removed`.

Typed rather than `Dictionary<string, int>`: `_do_cleanup` initialises the dict
with exactly these five keys and adds none, so the shape is fixed, and a typed
DTO puts it in `--help` through the response-example generator.

## Help text

Registers `AddResponseExample<CleanupResult>()`. No request shape — there is no
body.

Notes, verbatim:

```
Deletes DB rows for files no longer on disk, each book's search index and
bookmarks with it, then prunes systems whose books are all gone — unless a
campaign or a surviving child keeps one. Never touches files.

Normally a no-op. Run it after restructuring the library on disk.

A library directory that is absent rather than hung reads as wholly
deleted, and a rescan does not restore hand-entered metadata or bookmarks.
A hung mount is safe — the server treats a timed-out path as present.

409 while a scan is running; commits per row, so a failure part-way leaves
earlier removals applied.
```

Four blocks, in the order an agent needs them: what it does, when to run it, how
it can hurt, and how it fails. "Never touches files" is stated because the
command's name does not distinguish deleting rows from deleting content, and
that is the first thing a cautious caller wants to know.

## Testing

### Unit

- `Models/CleanupDtoTests` — deserialising the documented response, including
  that a non-zero `systems` count survives (the field a caller is least likely
  to expect, since no system is named in the request).
- `Commands/LibraryCommandTests` — the new command's presence, its `admin` role
  tag, its response-shape block, and that the Notes name both the bookmark
  deletion and the absent-vs-hung distinction. Those two are the whole point of
  the branch; a help block that loses them has lost the feature.

### Smoke test

Two consecutive calls, placed after the library-scan section (so no scan is
running and the 409 path is not tripped) and after the `EXPECTED_BOOKS`
assertions (so a cleanup of stale rows cannot invalidate a count asserted
earlier in the same run).

Assert the five keys are present and integral on the first call, and that the
**second** call reports all five as zero. That is the endpoint's contract —
after a cleanup, nothing is missing — expressed without depending on the
fixture's prior state, which is what keeps the suite idempotent. A first-call
assertion of zero would fail on any stack carrying stale `is_missing` rows, and
`CLAUDE.md` names exactly that state as a thing a database-only reset leaves
behind.

The fixture library is fully present, so the expected first call is all zeros
too — but asserting that would be asserting the stack's history, not the
command.

## Docs

- README Commands table — one row.
- `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate;
  `maintenance` moves to 1 / 2.
- `docs/grimoire-api-notes.md` — a maintenance section for the verified
  behaviour: the absent-vs-hung asymmetry in `_path_exists`, the per-row commit
  and what it means for a partial failure, the bookmark and FTS deletion, and
  the 409.
- `CLAUDE.md` — the "No confirm-gated command" deviation note closes: the
  question is decided, with the reasoning above.
- `docs/roadmap.md` — nothing. The roadmap lists intended work and an item leaves
  when it ships; this one is decided and shipped in the same branch, so it never
  lands there.
- `CHANGELOG.md` — untouched; it belongs to the release process.

The add-on spec's stale "out of scope … the next branch" line is left as it is.
Specs are dated records of what was decided when, not documents kept current.

## Risks

**The smoke test cannot exercise the destructive path.** The fixture library is
fully present, so every count is zero and what is proved is the plumbing, the
role, and the idempotency contract — not that a missing file is actually
removed. Deliberately: making it non-zero means deleting a seeded fixture
file mid-run and rebuilding it, which is a write whose expected value depends on
the fixture's prior state, and `CLAUDE.md` forbids exactly that. The honest
position is to assert the empty case and say so.

**The 409 path is untested for the same reason.** Tripping it needs a scan
running at a known moment, and the scan section deliberately waits for
quiescence before it ends.
