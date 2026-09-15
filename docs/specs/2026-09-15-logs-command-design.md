# `logs`

**Status:** approved
**Issue:** [#45](https://github.com/thomaslazar/grimoire-cli/issues/45)
**Verified against:** `hunterreadca/grimoire:1.6.2`, the pinned stack

## Problem

Four shipped commands start work that finishes somewhere the CLI cannot see.
`library rescan`, `duplicates scan`, `books reindex` and `books rescan` all
return a status and run in the background. When one goes wrong, the signals a
caller has today are `library scan-status`, which reports progress rather than
failure detail, and a book row's `index_failed` / `index_error`.

The reason lives in the server log. Without a way to read it, diagnosing a
failed scan means asking a human to open the Grimoire UI, which breaks the
agentic loop exactly where it matters.

`GET /api/logs` is one admin-only endpoint that closes this. `abs-cli` has no
counterpart, so nothing here is a port.

## Verified server behaviour

Read from `backend/routers/logs/core.py`, `_schemas.py` and `__init__.py` at
tag `v1.6.2`, and measured against the running 1.6.2 stack. Every claim below
was observed, not inferred from the route signature.

### Parameters

| Name | Type | Default | Server validation |
|---|---|---|---|
| `level` | `debug`/`info`/`warning`/`error`/`critical` | `info` | `Literal`, 422 otherwise |
| `limit` | int | 200 | `ge=1, le=20000`, 422 otherwise |
| `offset` | int | 0 | `ge=0` |
| `after_seq` | int | — | `ge=0` |

### Response

`{entries: [{seq, timestamp, level, logger, message}], total, max_seq, level,
limit, offset}`.

### Measured facts

- **The source is an in-memory ring buffer of 20 000 entries.** Anything older
  is gone; there is no disk history behind this route.
- **DEBUG is always available regardless of `LOG_LEVEL`.** The env var governs
  console output only — the handler feeding this buffer is installed at DEBUG.
  So `--level debug` returns detail an operator cannot see in `docker logs`.
  (Issue #45 recorded the opposite; this corrects it.)
- **`level` is a minimum and hierarchical.** Against a freshly booted stack:
  `debug` → `total: 107`, `info` → `total: 8`, `error` → `total: 0`.
- **A page is taken from the newest end and returned oldest-first.** With the
  eight `info` entries at seq `[1, 2, 7, 9, 10, 100, 102, 103]`:

  | Query | Entries returned |
  |---|---|
  | `limit=2` | `[102, 103]` |
  | `limit=2&offset=2` | `[10, 100]` |
  | `limit=3&offset=5` | `[1, 2, 7]` |

  `offset` skips from the newest end, then the page is emitted in ascending
  `seq`. Both halves of that are non-obvious and both affect what a caller
  reads.
- **`offset` is ignored when `after_seq` is set.** `after_seq=100&level=info`
  and `after_seq=100&level=info&offset=2` returned the identical
  `entries: [102, 103]`. The route says so and the stack confirms it.
- **`max_seq` tracks the whole buffer, not the filtered set.** `level=error`
  returned `entries: []` with `max_seq: 107`. This is what makes the cursor
  safe under a narrow filter: a poll that matches nothing still advances, so
  the caller cannot be pinned to a stale position.
- **`total` counts what matches `level`**, not what the page holds and not the
  buffer size.
- **An exhausted cursor is an empty page.** `after_seq=107` returned
  `entries: []` with `max_seq: 107` unchanged — the idle poll.

## Design

### 1. Command surface

A top-level `logs`, a bare action with no subcommands, beside `me` and
`self-test`. It maps 1:1 to a top-level route, the way `genres` and `search`
do. New `Commands/LogsCommand.cs` and `Services/LogsService.cs`, per the
one-file-per-command-group rule.

```
grimoire-cli logs [--level <l>] [--limit <n>] [--offset <n>] [--after-seq <n>] [--server <url>]
```

| Flag | Construction |
|---|---|
| `--level` | `OptionHelpers.Choice` over the five levels |
| `--limit` | `OptionHelpers.Range(1, 20000)` |
| `--offset` | `OptionHelpers.Range(0)` — floor, no ceiling |
| `--after-seq` | `OptionHelpers.Range(0)` |
| `--server` | as on every command that consumes a saved token |

`command.AddRoleRequired("admin")`, and the service call passes
`permissionHint: "the admin role"` so the help tag and the 403 message agree.

Exit 0 always. Nothing here maps to the batch (`BulkExit`) or scan (`ScanExit`)
codes: there is no partial-failure shape and no `already_running` status.

An omitted flag sends nothing and lets the server apply its own default, so the
CLI declares no defaults of its own and cannot drift from the server's.

Flag names mirror the API's query names. `--after-seq` rather than a friendlier
`--since-seq`: the field is `after_seq`, and "since" reads as inclusive when the
cursor is strictly exclusive.

### 2. The CLI does not poll

`--follow` is deliberately absent. The endpoint is built for polling, but the
loop belongs in the calling layer:

- stdout is the server's bytes unmodified ([input-output.md](../input-output.md)).
  A follow loop emits N documents; NDJSON is not one JSON document, and merging
  responses into an array means rewriting them.
- Choosing a poll interval is client-side policy, which thin pass-through
  excludes.
- The useful stop condition is "the scan finished", which lives in
  `library scan-status` — a different endpoint, so a bounded follow would be
  composing two endpoints inside one command.

What ships instead is the cursor: the caller reads `max_seq` and passes it back
as `--after-seq`. The help text teaches that loop, because a cursor nobody knows
how to advance is a flag nobody can use.

### 3. Help text

```
Role required:
  admin

Notes:
  Ring buffer of the last 20000 entries; anything older is gone. DEBUG is
  available here whatever the server's LOG_LEVEL is set to.

  --level is a minimum: error returns error and critical.

  A page is taken from the newest end and returned oldest-first. --offset is
  ignored when --after-seq is given.

  To poll, pass the previous response's max_seq back as --after-seq. max_seq
  tracks the whole buffer rather than the filtered set, so a --level that
  matches nothing still advances the cursor.

  total counts what matches --level, not what this page holds.

Examples:
  grimoire-cli logs --level error
  grimoire-cli logs --after-seq 1423 --level warning
```

Every line is a measured fact from the section above, and each one changes what
a caller reads or does. `--level`'s own description omits the value set, which
`ChoiceOption` renders itself.

### 4. Testing

Unit tests, in `tests/GrimoireCli.Tests/Commands/LogsCommandTests.cs`:

- the `admin` role section renders
- `--level` rejects an unknown value and names the five
- `--limit` rejects 0 and 20001; `--offset` and `--after-seq` reject negatives
- the response shape section renders `entries`, `max_seq` and `total`
- one assertion per Notes claim that would rot silently — the newest-end/
  oldest-first ordering, the `--offset` override, and the `max_seq` cursor rule

A smoke-test block, since the help claims are about live behaviour:

- a default call returns a page and a `max_seq`
- `--level error` narrows `total` relative to `--level debug`
- `--after-seq <max_seq>` returns `entries: []` with `max_seq` unchanged — the
  idle poll, which is the behaviour the command exists for

The block is read-only and idempotent, so it re-runs cleanly.

## Documentation

- README Commands table gains one row.
- `tools/generate-api-coverage.py` gains an `IMPLEMENTED` entry, and the table
  regenerates:

  ```python
  "GET /api/logs": "`logs` ✅",
  ```
- `docs/grimoire-api-notes.md` gains a `## Logs` section carrying the measured
  facts above — the response shape types cleanly in the spec, but the
  newest-end/oldest-first ordering, the `offset` override and the buffer-wide
  `max_seq` do not appear in it at all.
- `docs/roadmap.md` loses the `logs` line once this ships.

## Out of scope

- **`--follow` in any form.** Reasoned above; recorded here so a later coverage
  sweep does not add it as an oversight.
- **The rest of the administration surface** — users, settings, themes, the auth
  remainder. Tracked as an open question in
  [#50](https://github.com/thomaslazar/grimoire-cli/issues/50); `logs` is
  separated from it deliberately, because it serves the library workflow rather
  than instance administration.
- **Log output formatting** — no level colouring, no human-readable rendering.
  stdout stays the server's JSON.

## Decided during design

- **`--limit` is range-checked client-side** even though the server 422s
  cleanly, matching `search --limit`'s `Range(1, 200)`. It saves an agent a paid
  round-trip to learn the ceiling. This is the one place the command mirrors
  server policy, and it is deliberate.
- **Placement is top-level**, not `library logs` and not a new `server` group.
  It maps 1:1 to a top-level route; a `server` group would be structure built
  for endpoints that are an open question.
