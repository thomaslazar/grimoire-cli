# Migrating to Grimoire 1.6.0

The working reference for this migration. Everything needed to resume is here;
[the assessment](specs/research/2026-08-14-grimoire-1.6.0-assessment.md) is the
dated record of how the numbers were measured and is not kept current.

**Status: blocked, deliberately.** 1.6.0 is unreleased. Work happens on
`epic/grimoire-1.6.0` and merges only once upstream tags 1.6.0.

## Why this is a migration and not a version bump

Three upstream commits, all landed 2026-08-14 and all in `edge`/`nightly`:

| Commit | What it does | Cost to us |
|---|---|---|
| `a12b3c0` (#347) | OpenAPI response models on every endpoint | Opportunity: response DTOs become generated |
| `da55c9d` (#346) | Refresh tokens and revocable sessions | **Breaking**: the access token drops from 30 days to 30 minutes |
| `b16d112` (#345) | Fail closed on the default `SECRET_KEY` | None — our compose sets a non-default key |

## Version strategy

**One CLI version targets one server version.** CLI `0.1.x` supports Grimoire
1.5.6; whoever stays on 1.5.6 stays on `0.1.x`. There is no dual-version support
and none is wanted — the CLI never detects which server it is talking to, and
refresh is implemented unconditionally rather than behind a gate.

When 1.6.0 releases, `MinSupportedVersion` and `MaxTestedVersion` move to 1.6.0
**together**, and [grimoire-compatibility.md](grimoire-compatibility.md) gains a
matrix row.

## Workstream A — authentication (breaking, do first)

`POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/sessions`,
`DELETE /api/auth/sessions/others`, `DELETE /api/auth/sessions/{session_id}`.

The break: `backend/sessions.py` sets `ACCESS_TOKEN_EXPIRE_MINUTES` to **30
minutes** by default, with `REFRESH_TOKEN_EXPIRE_DAYS` at 30 days carried in a
`grimoire_refresh` **cookie**. 1.5.6 issued one 30-day bearer token
(`TOKEN_EXPIRE_DAYS`) and nothing else. Against a stock 1.6.0 server, a token
saved by `login` stops working half an hour later and every command fails until
the user logs in again.

What this requires:

- **Persist the refresh cookie** alongside the token in
  `~/.grimoire-cli/config.json`, with the same owner-only permissions.
- **Rotate on 401**: `POST /api/auth/refresh` takes the cookie, declares no
  bearer security, returns the same `{token, user}` shape as login, and re-sets
  both cookies. Rotation includes reuse detection, so a stale cookie is a signal
  to the server, not merely a failure.
- **Retire the "401 is terminal" rule** in [authentication.md](authentication.md),
  which records it as the sharpest divergence from abs-cli. abs-cli already does
  transparent refresh — harvest that design rather than inventing one.
- **Decide whether to expose sessions** as commands (`auth sessions`,
  revocation). New surface, not required by the migration.

Also: `GET /api/about` now declares `HTTPBearer`. The daily version check sends a
token so it should be unaffected, but the pre-login path needs checking.

## Workstream B — generated response models (blocked on upstream)

**Blocked on [hunter-read/grimoire#356](https://github.com/hunter-read/grimoire/issues/356)**
until the dev answers. It decides whether generated models are better or worse
than what they replace, which is the premise of the whole workstream.

### What the spec now gives us

| | 1.5.6 | edge |
|---|---|---|
| Component schemas | 86 | 281 |
| Operations | 207 | 233 |
| Success responses | 192 typed as `{}` | 195 of 233 typed |

The 38 untyped are 16 `204`s plus 22 binary/redirect endpoints, which cannot
carry a JSON schema. Every JSON success response is typed.

**All 31 hand-written DTOs match a new schema field-for-field** — `GameSystemSummary`
↔ `SystemSummary`, `Book` ↔ `BookOut`, `MetadataFieldDiff` ↔ `MetadataDiffField`,
and so on. No command's output shape changes.

### What the trial proved (measured, not predicted)

- **AOT is fine.** A scratch app parsing and re-serialising a real payload
  published with `PublishAot=true` — no trim or AOT warnings, output identical to
  the JIT run. This was the risk that could have sunk the plan.
- **`current` / `incoming` generate as `UntypedNode?`** and resolve on parse to
  `UntypedString` / `UntypedInteger` / `UntypedArray` / `UntypedObject`, with
  absent staying null. The polymorphism survives.
- **The existing `KiotaSampleWalker` handles response models unmodified** —
  flat, nested, `UntypedNode`-bearing and deep (`SystemDetail`, 2602 characters).
  It needs one `UntypedNode` case emitting `"<any>"`; today it renders `{}`.

### The blocker

18 array properties across 4 schemas declare `items: {}`, so Kiota generates
`UntypedNode?` where our DTOs declare `List<string>?` / `List<LinkEntry>?`:

| Schema | Untyped arrays |
|---|---|
| `SystemSummary`, `SystemDetail` (inherited) | `publishers`, `character_builder_urls`, `urls`, `genres`, `dice_materials` |
| `BookOut`, `BookDetail` | `authors`, `artists`, `genres`, `urls` |

Upstream declares these `list[Any]` while `tags: list[str]` beside them is
correct — reported as #356. If it is fixed, generated models are strictly better
than the hand-written ones. If it stands, the options are keeping hand-written
DTOs for those four schemas, or accepting the loss.

Count numbers carefully: **18 across 4** is the raw spec, which is what upstream
sees. The normalised copy our generator consumes reports 21 across 7, because
collapsing `anyOf: [array, null]` wrappers exposes three more.

### The plan, once unblocked

1. **Rename `tools/GenerateRequestExamples` to `GenerateJsonExamples`** and have
   it generate samples for every model. Delete `tools/GenerateResponseExamples`
   and its `SampleJsonWalker` outright — one tool, one walker, one registry.
   - Discovery loses its exclusions: "every model in
     `GrimoireCli.Generated.Models`" becomes the whole rule, since
     `HTTPValidationError` / `ValidationError` are no longer the only
     response-only schemas.
   - `AddRequestShape<T>()` and `AddResponseExample<T>()` both survive (different
     section titles) but read one dictionary.
   - `RequestExamplesTests`, `RequestExamplesDriftTest`,
     `ResponseExamplesDriftTest` and `ResponseExamplesJsonValidTest` consolidate
     into one drift test and one validity test.
   - Add the `UntypedNode` → `"<any>"` case; the `JsonElement` case goes with the
     deleted walker.
2. **Move ~34 call sites and 5 services** off `AppJsonContext.Default.<Dto>`.
3. **Delete `src/GrimoireCli/Models/`** except `AppConfig`, which is local config,
   and drop the response registrations from `AppJsonContext`.
4. **Decide the output contract** — see below.
5. **Recheck [kiota#2338](https://github.com/microsoft/kiota/issues/2338)** and
   `tools/normalize-spec.py`. It collapsed 40 `anyOf`-nullable arrays in the edge
   spec, so it matters more on the response side, not less.

### The output contract needs a decision

Printing a generated model is not the same as printing an STJ DTO:

| | today (STJ DTOs) | Kiota models |
|---|---|---|
| Undeclared server fields | dropped | kept (`AdditionalData`), at every level |
| Explicit `null` | written | omitted |
| Formatting / order | indented, declaration order | compact, alphabetical |

The first is a straight win for a JSON-in/JSON-out tool. The other two are
visible stdout changes — `"current": null` disappears from a metadata diff, and
every smoke-test `jq` assertion reads reordered, unindented output.

A third option avoids the question: **parse for the fields the CLI actually
reads** (`BulkExit.CodeFor`, `ScanExit.CodeFor`) **and print the server's bytes
verbatim.** Maximum fidelity, no formatting decision.

Note that standalone serialisation needs the writer factory registered
(`SerializationWriterFactoryRegistry.DefaultInstance.ContentTypeAssociatedFactories`);
the generated client does this in its constructor, so the CLI already has it, but
a test or generator that serialises outside the client does not.

## Workstream C — version gate and docs

Only once a 1.6.0 tag exists upstream. Never against `edge`: `CLAUDE.md` pins the
reference to a released version because `main` carries work no instance runs.

- `MinSupportedVersion` and `MaxTestedVersion` → 1.6.0.
- `docs/grimoire-compatibility.md` — new matrix row.
- `README.md` — the "Tested against Grimoire" line.
- `temp/grimoire/` — repin the clone to `v1.6.0`.
- `docker/docker-compose.yml` — repin the image.
- `docs/authentication.md` — rewrite for refresh (workstream A).
- `CLAUDE.md` — the "API client generation" section states that no success
  response carries a schema and that response DTOs are therefore hand-written.
  Both become false; the rule they justify goes with them.

## Living alongside `main`

`main` keeps shipping 1.5.6-era commands while this waits, so drift is the
standing cost.

- **Merge `main` into the epic regularly**, not once at the end. Every new
  command on `main` arrives with hand-written DTOs and `AppJsonContext`
  registrations — exactly what workstream B deletes — so a late merge means
  resolving the same conflict repeatedly.
- **A command added to `main` after workstream B lands needs doing twice**: the
  1.5.6 way on `main`, the generated way here. Cheaper than freezing `main`, but
  worth knowing before starting B rather than after.
- **Epic CI cannot pin a released server.** `docker/docker-compose.yml` pins
  1.5.6; this branch must point at `edge` or `nightly`, which move underneath it.
  A red smoke test here may mean upstream changed rather than the branch broke —
  treat epic CI as a signal, not a gate, until a 1.6.0 tag exists.

## Open questions

1. **#356** — does upstream type those 18 array fields? Gates workstream B.
2. **Can `authors` / `genres` / `urls` hold heterogeneous values in practice?**
   If yes, `list[Any]` is correct and today's `List<string>?` DTOs are the bug —
   a row with an object in `authors` would already fail to deserialise. Worth a
   probe against a real library.
3. **Output contract** — re-serialise through Kiota, or pass the server's bytes
   through verbatim?
4. **Session commands** — expose `auth sessions` and revocation, or stop at
   transparent refresh?

## Reproducing the environment

```bash
docker run -d --name grimoire-edge -p 9482:9481 \
  -e SECRET_KEY=edge-eval-only-not-a-real-secret \
  -v grimoire-edge-data:/data -v grimoire-edge-lib:/library \
  hunterreadca/grimoire:edge
# The published port is not reachable from the devcontainer under
# docker-outside-of-docker; use the container's bridge address:
EDGE=$(docker inspect grimoire-edge --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}')
curl -s "http://${EDGE}:9481/api/openapi.json" -o spec.json

# Generate a throwaway client (never into src/GrimoireCli/Generated on this branch
# until the switch is actually being done):
python3 tools/normalize-spec.py < spec.json > temp/kiota-trial/spec.json
cd temp/kiota-trial && kiota generate --openapi spec.json --language CSharp \
  --output Generated --class-name GrimoireApiClient --namespace-name Trial.Generated --clean-output
```

`temp/` is gitignored, so the trial tree and the 1.5.6 source pin in
`temp/grimoire/` are local-only. The 1.5.6 pin is deliberately untouched by this
branch — `main` still depends on it.
