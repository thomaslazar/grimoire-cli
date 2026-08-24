# Edge client and byte-passthrough output

Workstream B of [the 1.6.0 migration](../grimoire-1.6.0-migration.md), designed as
two PRs back to `epic/grimoire-1.6.0`.

The migration document parked workstream B behind a released 1.6.0 tag on the
grounds that generated response models did not exist yet. That was wrong: they
exist in the `edge` spec today, and this branch targets 1.6.0. Generating the
client from `edge` is what the epic is for, so it comes first and everything else
follows from it.

## Why the order changed

The hand-written DTOs in `src/GrimoireCli/Models/` have three consumers, not one:

1. **Printing.** A service deserializes the response, the command re-serializes it
   to stdout. This is the round trip the change exists to remove.
2. **Six exit-code readers.** Five `BulkExit.CodeFor(result.Errors/Failed)` and one
   `ScanExit.CodeFor(result.Status)`.
3. **`--help` response shapes.** `HelpExtensions.AddResponseShape<T>()` renders a
   sample from `ResponseExamples.g.cs`, which `tools/GenerateResponseExamples`
   generates *from the DTOs on `AppJsonContext`*, kept honest by a drift test.

The third is the binding constraint. `--help` is the primary interface for the
agents that consume this CLI, so the samples are a feature, and their only
replacement is a generated model carrying a response schema. Delete the DTOs
before that source exists and either help loses its samples or the samples become
frozen literals that get re-keyed twice.

Regenerating from `edge` removes the constraint instead of working around it, so
`Models/` can be deleted in one step with the samples re-sourced, not staged.

## PR 1 — edge stack and regenerated client

No behaviour change. The review is a read of the generated diff, which
[CLAUDE.md](../../CLAUDE.md) already calls the authoritative list of what changed
in the API surface.

### `docker/docker-compose.edge.yml`

An override that runs the `edge` image alongside the pinned 1.5.6 stack rather
than instead of it:

- **own project name and port** — 9482, so both stacks run at once
- **own data dir** — `docker/data-edge`, because a shared database would be
  indexed by two server versions
- **shared fixture library** — the same read-only mount; the fixtures are not
  version-specific
- **same `SECRET_KEY` and env shape** as the pinned stack

Compose rather than a hand-rolled `docker run` is also what fixes addressing.
The migration document records that a published port is unreachable from the
devcontainer under docker-outside-of-docker, but that was measured against a bare
`docker run`; the pinned stack is reachable at `host.docker.internal:9481`
precisely because it is a compose stack with a published port. **The plan verifies
this before anything depends on it.**

No script changes are needed. `docker/seed.sh` and `tools/generate-api-client.sh`
both already read `GRIMOIRE_SERVER`:

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9482 bash docker/seed.sh
GRIMOIRE_SERVER=http://host.docker.internal:9482 bash tools/generate-api-client.sh
```

Seeding works unchanged because `backend/seed_users.py` is byte-identical between
1.5.6 and the 2026-08-17 edge build and still reads `{DATA_PATH}/users.json` in the
shape `docker/users.json.example` already has.

### Regenerating `src/GrimoireCli/Generated/`

`tools/generate-api-client.sh` is the only supported path and stays so. Two
details:

- **`kiota-lock.json`'s `descriptionLocation`** is written from `$SERVER`. Point it
  at `host.docker.internal:9482`, never a container IP, or the committed lock
  carries an ephemeral address.
- **`tools/normalize-spec.py` is still required.** Re-measured against the
  2026-08-17 edge spec: it still collapses 40 `anyOf`-nullable arrays, so
  [kiota#2338](https://github.com/microsoft/kiota/issues/2338) has not been fixed
  out from under us.

### One consequence: the examples file grows

`tools/GenerateRequestExamples` discovers every public `IParsable` class in
`GrimoireCli.Generated.Models`, and `RequestExamplesDriftTest` shells out to it and
compares against the checked-in `RequestExamples.g.cs`. Regenerating the client
therefore forces regenerating that file, and it picks up the new response models
along with the request ones.

This is harmless and is not suppressed with an exclusion list. `RequestExamples.For`
is only ever queried for types passed to `AddRequestShape<T>()`, so the extra
entries are unread — and PR 2 renames the tool to `GenerateJsonExamples` with
exactly this whole-namespace rule, so the grown file is that end state minus the
rename.

### Compatibility with the pinned stack

The CLI must keep working against 1.5.6 for the smoke test, so a client built from
the edge spec must not change any request it sends. Measured across all **207
operations shared** between the 1.5.6-era spec and the 2026-08-17 edge spec:

| Check | Result |
|---|---|
| Parameters removed | 0 |
| Parameters newly required | 0 |
| Request body fields removed | 0 |
| Request body fields newly required | 0 |

Nothing was removed at the schema level either: every 1.5.6 operation and
component schema is still present in edge.

### Verification

1. `dotnet format GrimoireCli.sln --verify-no-changes`
2. `dotnet build GrimoireCli.sln`
3. `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
4. `bash docker/smoke-test.sh` against the **pinned 1.5.6 stack** — the real gate
5. the same smoke test against the edge stack — signal, not gate, per the
   migration document

### Docs in PR 1

- **The migration document's `## Sequence` section** is invalidated by this design
  and is rewritten with it. It currently puts byte-passthrough first on the
  grounds that it needs no edge stack.
- **`CLAUDE.md` is not touched.** Its claim that no success response carries a
  schema, and that response DTOs are therefore hand-written, only becomes false
  when the DTOs go. It moves to PR 2 rather than to workstream C.

### Risk

Regeneration may rename request builders and break call sites. This is mechanical
and surfaces at build time. A large churn is a reason to read the diff carefully,
not to back out — the diff is the deliverable.

## PR 2 — byte-passthrough

Designed here because the decisions are interlocked with PR 1; built separately.

### Size

22 typed `WriteJson(..., AppJsonContext.Default.X)` call sites across 7 command
files, and 51 `AppJsonContext.Default` references once `AppConfig` and
`DictionaryStringString` are excluded, spread over 6 services and their commands.

### Data flow

Before:

```
service --SendAsync<T>(typeInfo)--> DTO --> command --WriteJson(dto, typeInfo)--> stdout
```

After:

```
service --SendAsync()--> string --> command --WriteRawJson(json)--> stdout
```

`GrimoireApiClient.SendAsync` already returns `string`; the typed overload only
adds a deserialize on top. `ConsoleOutput.WriteRawJson` already exists and seven
write commands already use it. The change removes a layer rather than adding one.

### Output contract

**Compact by default, `--pretty` to re-indent.** The server's bytes reach stdout
unmodified, so undeclared fields, explicit nulls and the server's key order all
survive — a straight fidelity gain for a JSON-in/JSON-out tool, and cheaper for
the agents that read it.

`--pretty` is a root option declared `Recursive = true`. `--debug` and
`--log-json` are root-position-only today — measured: `grimoire-cli config get
--debug` prints usage and ignores the flag, while `grimoire-cli --debug config get`
works. An agent would write `systems get --id x --pretty` and hit exactly that, so
the flag has to accept both positions.

**This leaves an inconsistency that needs a decision, because `README.md:234`
documents the current rule explicitly:** "`--debug` and `--log-json` are root
options, so they go **before** the subcommand". Either that sentence grows an
exception for `--pretty`, or all three become recursive and the sentence is
deleted. The second is the smaller total diff and removes a failure mode rather
than documenting one; it costs a change to `--debug` behaviour, which
`RootHelpTests` covers. **Open for the maintainer** — the rest of the design does
not depend on it.

`AppJsonContext` keeps `WriteIndented = true`. Once only `AppConfig`,
`SavedFile` and `DictionaryStringString` remain, that setting again means what its
comment says: readable config files.

### Keeping the HTML guard

`GrimoireApiClient.Deserialize` is load-bearing beyond typing. Its comment records
why:

> Grimoire's SPA catch-all answers an unroutable request (an empty, `.`, or
> otherwise mis-encoded id) with an HTML 200, not an API error — so
> deserialization is where that case must be caught.

The DTOs are therefore the accidental guard against printing an HTML page to
stdout as if it were JSON. Passthrough must keep it, so an `EnsureJson(json,
endpoint)` takes over: `JsonDocument.Parse`, discard, and on failure the same
truncated-debug-body plus `exit(2)` path `Deserialize` uses today.

It belongs in `SendAsync(string)`, which is the one place every raw response is
read. Two consequences:

- **The seven existing passthrough commands gain the guard**, which they lack
  today.
- **An empty or whitespace body passes through untouched.** `JsonDocument.Parse("")`
  throws, and 204s are legitimate.

### Exit-code readers

`ReadStringProperty(json, property)` already exists for exactly this — its summary
says "Used for untyped responses" — and covers `ScanExit`. The five `BulkExit`
sites need one sibling, `HasItems(json, property)`, returning whether a top-level
array property is non-empty.

### Deletions

- `src/GrimoireCli/Models/` in full. `AppConfig` is local config and `SavedFile` is
  the CLI's own `--output` receipt; both move out of the response-DTO area rather
  than being deleted.
- The response registrations on `AppJsonContext`.
- `tests/GrimoireCli.Tests/Models/` except the `SavedFile` coverage.

### Help samples and the generators

`AddResponseShape<T>()` keeps its signature; each call site swaps its type
argument from a hand-written DTO to the corresponding generated model. The samples regenerate from the
Kiota response models, which the trial already proved works: the existing
`KiotaSampleWalker` handled flat, nested, `UntypedNode`-bearing and deep models
unmodified, needing one `UntypedNode` case that emits `"<any>"`.

That folds the generator consolidation into this PR instead of leaving it as a
separate step, because this is where the sample source changes:

- `tools/GenerateRequestExamples` becomes `GenerateJsonExamples` and generates for
  every model in `GrimoireCli.Generated.Models`, losing its exclusion list.
- `tools/GenerateResponseExamples` and its `SampleJsonWalker` are deleted. One
  tool, one walker, one registry.
- `RequestExamplesTests`, `RequestExamplesDriftTest`, `ResponseExamplesDriftTest`
  and `ResponseExamplesJsonValidTest` consolidate into one drift test and one
  validity test.

`MetadataDiffField.current` / `incoming` are still untyped in the edge spec, so
they still generate as `UntypedNode?` and the polymorphism the metadata diff needs
still survives.

### `self-test`

Its DTO round-trips exist for AOT coverage of what crosses the JSON boundary, so
they follow the DTOs out and are replaced by coverage of what crosses it now:
`JsonDocument.Parse` under AOT, `ReadStringProperty` and `HasItems`, the
pretty-printer, and an HTML body reaching `exit(2)` rather than stdout. `AppConfig`
and `SavedFile` coverage stays.

### Behaviour changes, all deliberate

| | Before | After |
|---|---|---|
| Default formatting | indented | compact (`--pretty` to indent) |
| Undeclared server fields | dropped | preserved |
| Explicit `null` | written | preserved |
| Key order | DTO declaration order | server's order |
| HTML catch-all on a passthrough command | printed to stdout | `exit(2)` |

Byte-passthrough changes stdout, so it wants a version boundary. With `main`
feature-frozen at 1.5.6, shipping `0.1.0` from `main` as it stands puts this change
in the 1.6.0 release rather than retroactively into a released format.

### Docs in PR 2

- **`CLAUDE.md`** — the API-client-generation section states that no success
  response carries a schema and that response DTOs are hand-written from
  `temp/grimoire/`. Both become false; the rule they justify goes with them.
- **`README.md`** — `--pretty` is a new user-visible flag, so the Commands table
  section covers it, and the root-option sentence at `README.md:234` is either
  amended or deleted per the decision above.
- **`docs/grimoire-api-coverage.md`** — unchanged. Which endpoints are called does
  not change.

## Out of scope

- Refresh plumbing (workstream A) — independent, and the next thing after this.
- The version gate, the book-folders restore and the compatibility matrix
  (workstreams C and D) — those wait for a released 1.6.0 tag.
- Making `--debug` and `--log-json` recursive.
- Session commands (`auth sessions` and revocation), still an open question in the
  migration document.
