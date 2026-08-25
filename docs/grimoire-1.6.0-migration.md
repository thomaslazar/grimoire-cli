# Migrating to Grimoire 1.6.0

The working reference for this migration. Everything needed to resume is here;
[the assessment](specs/research/2026-08-14-grimoire-1.6.0-assessment.md) is the
dated record of how the numbers were measured and is not kept current.

**Status: waiting on a tag, not on upstream answers.** 1.6.0 is still unreleased —
no `v1.6.0` tag existed upstream as of 2026-08-24. Work happens on `main`, which is
the 1.6.0 line; released 1.5.6 support is maintained on `support/grimoire-1.5.6`.
See [where this work happens](#where-this-work-happens).

Numbers below are re-measured against `hunterreadca/grimoire:edge` built
2026-08-23 (spec `info.version` `1.5.6-tk8i6j`), which reports 342 schemas and
282 operations. Both #356 and #357 — the two blockers this document was written
around — are fixed. **`edge` moves fast:** the three builds measured while writing
this document reported 281/233, 302/252 and 342/282 schemas/operations over nine
days, so re-measure rather than trusting any number here.

## Why this is a migration and not a version bump

Three upstream commits, all landed 2026-08-14 and all in `edge`/`nightly`, are
what make this a migration:

| Commit | What it does | Cost to us |
|---|---|---|
| `a12b3c0` (#347) | OpenAPI response models on every endpoint | Opportunity: response DTOs become generated |
| `da55c9d` (#346) | Refresh tokens and revocable sessions | **Breaking**: the access token drops from 30 days to 30 minutes |
| `b16d112` (#345) | Fail closed on the default `SECRET_KEY` | None — our compose sets a non-default key |

`edge` kept moving afterwards: by 2026-08-17 it had also fixed #356 and #357 and
added 19 operations. Those change the plan's cost, not its shape — see
[workstream B](#workstream-b--generated-response-models-unblocked),
[workstream D](#workstream-d--systems-book-folders-returns) and
[new surface](#new-surface-since-the-2026-08-14-assessment).

## Version strategy

**One CLI version targets one server version.** CLI `0.1.x` supports Grimoire
1.5.6; whoever stays on 1.5.6 stays on `0.1.x`. There is no dual-version support
and none is wanted — the CLI never detects which server it is talking to, and
refresh is implemented unconditionally rather than behind a gate.

When 1.6.0 releases, `MinSupportedVersion` and `MaxTestedVersion` move to 1.6.0
**together**, and [grimoire-compatibility.md](grimoire-compatibility.md) gains a
matrix row.

## Sequence

`main` is feature-frozen at 1.5.6, so nothing here is ordered around avoiding
conflicts with it. The edge stack comes first because everything else is built
from the client generated against it — see
[the design](specs/2026-08-19-edge-client-and-byte-passthrough-design.md).

1. **A client generated from the prerelease channel.** `docker/docker-compose.yml`
   tracked an `edge` digest at first, then a `nightly` one once the 1.6.0 RC landed
   there, and now rides `nightly` unpinned — and `src/GrimoireCli/Generated/` is
   regenerated from its spec. The review is a read of the generated diff, and the
   gate is the smoke test against that
   stack — one CLI version targets one server version, so there is one stack and
   it is the one being targeted.
2. **Byte-passthrough output.** Services return the response bytes, commands print
   them; compact by default with `--pretty` to re-indent. `src/GrimoireCli/Models/`
   is deleted outright, because step 1 gives `--help` a generated model to draw its
   response samples from — which is what the ordering is for. The example
   generators consolidate here, since this is where the sample source changes.
3. **Refresh plumbing** ([workstream A](#workstream-a--authentication-breaking-do-first)).

**On the tag** — [workstream C](#workstream-c--version-gate-and-docs) and
[workstream D](#workstream-d--systems-book-folders-returns): move the version
gate, revert the book-folders cut, update the docs. Mechanical once the rest is
done.

Byte-passthrough changes stdout, so it wants a version boundary. If `0.1.0` ships
from `main` as it stands, the change arrives with the 1.6.0 release rather than
retroactively.

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

Also: `GET /api/about` now declares `HTTPBearer` and **enforces it** — an
unauthenticated `GET /api/about` against edge returns
`{"detail":"Not authenticated"}`, verified live. The daily version check sends a
token so it is unaffected, but `login` forces a check and builds its own client;
that path runs before a token exists and needs re-checking.

The expiry constants and the cookie name are unchanged in the 2026-08-17 build:
`ACCESS_TOKEN_EXPIRE_MINUTES` 30, `REFRESH_TOKEN_EXPIRE_DAYS` 30,
`REFRESH_COOKIE_NAME` `grimoire_refresh` (`backend/sessions.py`).

## Workstream B — generated response models (unblocked)

[hunter-read/grimoire#356](https://github.com/hunter-read/grimoire/issues/356) is
fixed in the 2026-08-17 edge build, which was the premise the workstream was
waiting on. See [the blocker, resolved](#the-blocker-resolved).

### What the spec now gives us

| | 1.5.6 | edge (2026-08-23) |
|---|---|---|
| Component schemas | 86 | 342 |
| Operations | 207 | 282 |
| Success responses | 192 typed as `{}` | 238 of 282 typed |

The 44 untyped are `204`s plus binary, redirect and `.ics` endpoints, which cannot
carry a JSON schema. Every JSON success response is typed.

**32 of the 33 hand-written DTOs match a schema field-for-field** —
`GameSystemSummary` ↔ `SystemSummary`, `Book` ↔ `BookOut`, `MetadataFieldDiff` ↔
`MetadataDiffField`, `CoverUploadResult` ↔ `SystemCoverResponse`, and so on. Three
(`BookDetail`, `GameSystemDetail`, `GameSystemSummary`) declare a subset of their
schema's fields, so the generated model is a superset. No command's output shape
changes.

`SavedFile` is the exception and has no counterpart: it is the CLI's own
`--output` receipt, not a server shape. It joins `AppConfig` as a model that stays
hand-written.

### What the trial proved (measured, not predicted)

- **AOT is fine.** A scratch app parsing and re-serialising a real payload
  published with `PublishAot=true` — no trim or AOT warnings, output identical to
  the JIT run. This was the risk that could have sunk the plan.
- **`current` / `incoming` generate as `UntypedNode?`** and resolve on parse to
  `UntypedString` / `UntypedInteger` / `UntypedArray` / `UntypedObject`, with
  absent staying null. The polymorphism survives.
- **The existing `KiotaSampleWalker` handled the sampled response models
  unmodified** — flat, nested, `UntypedNode`-bearing and deep (`SystemDetail`,
  2602 characters). This did **not** generalise: walking all 883 models needed
  rules for `DateTimeOffset`, genuine multi-branch unions and recursive models.
  See [the design](specs/2026-08-19-edge-client-and-byte-passthrough-design.md).

### The blocker, resolved

**#356 is fixed.** The 2026-08-23 edge spec has **zero** array properties
declaring `items: {}` — down from 18 across 4 schemas. `genres`, `dice_materials`,
`authors`, `artists` and `tags` are `list[str]`; `urls` and
`character_builder_urls` reference a `LinkEntry` schema; and `publishers`
references a new `PublisherRef` (`name`, `url`), which matches our
`PublisherEntry` exactly.

The normalised copy our generator consumes reports zero as well, so the two counts
no longer disagree the way they did on 2026-08-14.

Generated models are therefore strictly better than the hand-written ones, which
was the premise workstream B was waiting on. Nothing gates it now but a 1.6.0 tag.

### One naming wrinkle to expect

29 schema names arrive fully qualified because FastAPI disambiguates collisions by
module path — `backend__routers__systems___schemas__StatusResponse`,
`backend__routers__books___schemas__LinkEntry`,
`backend__routers__auth___schemas__OkResponse` and so on, plus nine
`Body_<operation>_<path>_<method>` multipart bodies. Kiota generates those names
verbatim. Our single `LinkEntry` covers both the books and systems variants and
our single `ScanTriggerResult` covers nine identical `StatusResponse` schemas, so
the switch trades 2 readable types for 11 unreadable ones on those shapes. Cosmetic
in code the CLI does not print, but budget for it when reviewing the diff.

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
2. **Move the call sites off `AppJsonContext.Default.<Dto>`** — 57 references
   today, 51 excluding `AppConfig` and `DictionaryStringString`, spread across
   6 services and their commands.
3. **Delete `src/GrimoireCli/Models/`** except `AppConfig` (local config) and
   `SavedFile` (the CLI's own `--output` receipt), and drop the response
   registrations from `AppJsonContext`.
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

## Workstream D — `systems book-folders` returns

**[hunter-read/grimoire#357](https://github.com/hunter-read/grimoire/issues/357) is
fixed** in the 2026-08-17 edge build, and fixed more broadly than reported:
`tag_service.system_category_depth` walks the whole container chain
(`2 + <ancestor count>`) rather than special-casing one level of `parent_id`, so
arbitrarily nested containers resolve too. `system_category_depths` gives every
system's depth in one query for the bulk resolvers.

`systems book-folders list|set` were built and cut on `main`
(`5c566b4`) with the commands returning once #357 was fixed, so this is a revert
plus the new endpoint:

- **Restore `BookFolderCommands.cs`** and its smoke-test coverage from `5c566b4^`.
  `PATCH` still takes `BookFolderUpdate` (`path`, `tags`) and `GET` still returns
  `BookFolderOut` (`path`, `tags`), so the command surface is unchanged.
- **Add `DELETE /api/systems/{system_id}/book-folders`** — new in edge, takes the
  folder path as a **query** parameter rather than a body, and returns
  `StatusResponse`. Needs a `book-folders delete`.
- **Rewrite the depth-mismatch caveat** in
  [grimoire-api-notes.md](grimoire-api-notes.md), which currently records the bug
  and the cut as verified 1.5.6 behaviour. Re-verify the round trip live before
  rewriting it — the fix is read from source, not yet measured.
- `docs/roadmap.md` item 3 is what this closes.

## New surface since the 2026-08-14 assessment

19 operations arrived between the two edge builds. None are required by the
migration; they are listed so the epic's coverage regeneration is not a surprise.

- **A file-management API** — `GET /api/files/browse`, `POST /api/files/upload`,
  `POST|DELETE /api/files/folder`, `POST /api/files/folder/scaffold`,
  `PUT /api/files/folder/markers`, `POST /api/files/move`,
  `POST /api/files/rename`.
- **Campaign calendars** — `GET|POST|DELETE /api/campaigns/calendar/subscription`
  plus three `.ics` feeds, which are the new untyped-but-not-JSON responses.
- **Sidecars** — `GET|PUT /api/maintenance/sidecars/settings`,
  `POST /api/maintenance/sidecars/export`.
- **`POST /api/users/{user_id}/merge`** (guest merge) and
  **`DELETE /api/systems/{system_id}/book-folders`** (workstream D).

Nothing was removed: every 1.5.6 operation and schema is still present.

## Workstream C — version gate and docs

Only once a 1.6.0 tag exists upstream. Never against `edge`: `CLAUDE.md` pins the
reference to a released version because `main` carries work no instance runs.

- **`docker/docker-compose.yml` — repin the image to the release tag,
  `hunterreadca/grimoire:1.6.0`, and regenerate the client from it in the same
  commit.** Riding an unpinned prerelease channel is a deliberate, temporary
  exception taken only because the CLI is being built alongside active server
  development; a released target gets a pinned release tag like every other version
  has, and version bumps are deliberate from then on. Retiring it is what closes
  this workstream, so do it first — the rest of this list is downstream of the spec
  that regeneration produces, and this is the regeneration the whole drift-tolerant
  arrangement was deferring.
- `MinSupportedVersion` and `MaxTestedVersion` → 1.6.0.
- `docs/grimoire-compatibility.md` — new matrix row, and drop the note about the
  pin being an unpinned channel that can drift.
- `README.md` — the "Tested against Grimoire" line.
- `temp/grimoire/` — repin the clone to `v1.6.0`. Until then there is no tag to
  pin, which is why behaviour is read out of the running container instead.
- `CLAUDE.md` — the unpinned-channel exception under "API client generation" goes
  away with it.
- **`docs/grimoire-api-coverage.md` — regenerate, and only here.** The table is
  generated from the live spec plus the role dependencies in `temp/grimoire`, so
  it cannot be regenerated while those two disagree on version: against a 1.6.0
  spec with a 1.5.6 source pin, every route added since 1.5.6 resolves to a blank
  Perm column, which the table's own legend reads as "any authenticated user".
  Measured: 62 new operations, `POST /api/backups` and
  `DELETE /api/backups/{backup_id}` among them, would each be published as
  needing no role. Repin `temp/grimoire` to `v1.6.0` *first*, then regenerate.
  Until then `IMPLEMENTED` is kept current by hand and the table lags — which is
  why `POST /api/auth/refresh` is in the script but not yet in the markdown.
- `CLAUDE.md` — the "API client generation" section states that no success
  response carries a schema and that response DTOs are therefore hand-written.
  Both become false; the rule they justify goes with them.

## Where this work happens

**`main` is the 1.6.0 line.** There is no epic branch: keeping one alive for months
meant carrying drift and getting no CI, since the workflow only runs on pull
requests into `main`. The work happens on ordinary feature branches off `main`,
reviewed and gated like anything else.

**Released support lives on `support/grimoire-1.5.6`**, cut from `main` at
`618c4bf`. It carries v0.1.0 plus the finished 1.5.6-era work that was never
released — covers, `books thumbnail`, binary output — so a 1.5.6 user can still
get a release. Fixes are made and released there, then **cherry-picked** forward,
not merged: once workstream B lands, `main` has no `Models/` for a DTO-era fix to
apply against.

**CI can target a prerelease after all.** The earlier claim that it could not
assumed the only choices were a release tag or a floating one, and for a while the
answer was a digest — as reproducible as a release tag. That has since been
relaxed: `docker/docker-compose.yml` on `main` rides `nightly` **unpinned**, where
the 1.6.0 RC lands. The RC is not expected to move much, the window closes at
release, and 1.6.0 needs a regeneration regardless — so a digest bought
reproducibility at the price of repin ceremony and the risk of silently testing a
stale RC.

**The cost is accepted, not eliminated:** the spec can drift under the committed
client between regenerations. An upstream request-shape change surfaces as a
smoke-test failure, and the answer is to regenerate and read the diff. Which build
a run actually used is recoverable from the image's
`org.opencontainers.image.revision` label, a nightly-only label that matches the
`commit_hash` in `GET /api/about`.

**That arrangement is an exception with an expiry.** It exists only because the CLI is being
built alongside active server development, and [workstream
C](#workstream-c--version-gate-and-docs) retires it: when 1.6.0 releases the local
stack goes back to a release tag. Whether the pin has gone stale is two commands,
which is why there is no second compose file for it:

```bash
docker pull hunterreadca/grimoire:nightly
docker inspect hunterreadca/grimoire:nightly --format '{{index .RepoDigests 0}}'
```

## Open questions

1. ~~**#356** — does upstream type those 18 array fields?~~ Fixed in the
   2026-08-17 edge build; workstream B is unblocked.
2. ~~**Can `authors` / `genres` / `urls` hold heterogeneous values in practice?**~~
   Answered by the fix: they are `list[str]`, `urls` is a `LinkEntry` array and
   `publishers` a `PublisherRef` array. `list[Any]` was the bug, not the contract,
   and today's DTOs were right.
3. **Output contract** — re-serialise through Kiota, or pass the server's bytes
   through verbatim?
4. ~~**Session commands** — expose `auth sessions` and revocation, or stop at
   transparent refresh?~~ Stopped at transparent refresh. `logout` was weighed on
   the grounds that a refresh token sits on disk for 30 days, and dropped: the
   remedy already exists off-CLI, since revoking the session in the web UI or
   changing the password both kill it.

## Reproducing the environment

`edge` moves. Pull before measuring anything, and record the build date the
numbers came from — the two measurements in this document are five days apart and
disagree on every count.

**A seeded edge stack is an image-tag override away, not a new seeding story.**
`backend/seed_users.py` is byte-identical between 1.5.6 and the 2026-08-17 edge
build and still reads `{DATA_PATH}/users.json` in the shape
`docker/users.json.example` already has, so `docker/seed.sh` works against edge
unchanged, so `docker/docker-compose.yml` on its own is the whole of the setup — no
second compose file, and not the hand-rolled `docker run` plus
`POST /api/auth/setup` below either. Keep the raw recipe for one-off spec
measurements against a build the compose stack does not run.

Pair the override with a spec-diff tool that pulls edge, records the build date
and the counts, and diffs against the last recorded snapshot. Every number in this
document was derived by hand-writing that script; the third time is what makes it
worth committing.

```bash
docker pull hunterreadca/grimoire:edge
docker rm -f grimoire-edge 2>/dev/null
docker run -d --name grimoire-edge -p 9482:9481 \
  -e SECRET_KEY=edge-eval-only-not-a-real-secret \
  -v grimoire-edge-data:/data -v grimoire-edge-lib:/library \
  hunterreadca/grimoire:edge
# The published port is not reachable from the devcontainer under
# docker-outside-of-docker; use the container's bridge address:
EDGE=$(docker inspect grimoire-edge --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}')
curl -s "http://${EDGE}:9481/api/openapi.json" -o spec.json

# The image ships its own backend source, so behaviour questions need no clone:
docker exec grimoire-edge grep -n EXPIRE backend/sessions.py

# Generate a throwaway client (never into src/GrimoireCli/Generated on this branch
# until the switch is actually being done):
python3 tools/normalize-spec.py < spec.json > temp/kiota-trial/spec.json
cd temp/kiota-trial && kiota generate --openapi spec.json --language CSharp \
  --output Generated --class-name GrimoireApiClient --namespace-name Trial.Generated --clean-output
```

`temp/` is gitignored, so the trial tree and the 1.5.6 source pin in
`temp/grimoire/` are local-only. The 1.5.6 pin is deliberately untouched by this
branch — `main` still depends on it, and edge behaviour is read out of the running
container instead.
