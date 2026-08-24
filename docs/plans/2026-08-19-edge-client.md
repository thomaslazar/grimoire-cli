# Edge Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the `edge` Grimoire image as a second local stack and regenerate the Kiota client from its spec, so the generated response models workstream B needs exist on this branch.

**Architecture:** PR 1 of [the design](../specs/2026-08-19-edge-client-and-byte-passthrough-design.md). A compose override runs `hunterreadca/grimoire:edge` alongside the pinned 1.5.6 stack on its own port, project name, network and data directory, sharing the read-only fixture library. `tools/generate-api-client.sh` and `docker/seed.sh` already read `GRIMOIRE_SERVER`, so neither changes. No C# is hand-written: the only source change is the regenerated `src/GrimoireCli/Generated/` tree plus the regenerated examples file that its drift test demands.

**Tech Stack:** Docker Compose, Kiota 1.34.1, .NET 10, bash.

## Global Constraints

- **Branch:** `feat/edge-client`, off `epic/grimoire-1.6.0`. PRs target the epic, never `main`.
- **No behaviour change in this PR.** The CLI's stdout, flags and commands are identical before and after. Anything that changes them belongs to PR 2.
- **Never hand-edit `src/GrimoireCli/Generated/`.** `bash tools/generate-api-client.sh` is the only supported path, and `kiota update` is forbidden — it refetches the raw spec and would skip `tools/normalize-spec.py`.
- **Kiota must be exactly 1.34.1**, matching `.kiotaVersion` in `src/GrimoireCli/Generated/kiota-lock.json`. The generator script enforces this and exits rather than mixing generator churn into an API diff.
- **The pinned 1.5.6 stack stays pinned.** `docker/docker-compose.yml` is not edited by this plan; the edge stack is a separate override.
- **`bash docker/smoke-test.sh` against the edge stack is the gate**, because edge is what this branch targets. One CLI version targets one server version, so a gate on 1.5.6 would test dual-version compatibility the migration document rules out. The 1.5.6 run is a bonus signal that the regeneration caused no accidental request churn.
- **Compose v2.24+ is required** for the `!override` tag the edge file depends on. Verified present: this devcontainer has v5.4.0.
- **Conventional Commits**, imperative, lowercase, no period, no `Co-Authored-By` and no tool attribution.
- **`CHANGELOG.md` and `docs/roadmap.md` are not touched.** The changelog belongs to the release process; the roadmap records only maintainer-decided intent.
- **`CLAUDE.md` is not touched.** Its response-DTO claims only become false in PR 2.

---

### Task 1: Commit the design and correct the migration sequence

The spec and plan land with the code on this branch, per CLAUDE.md's rule that design and delivery are reviewed as one unit. The migration document's `## Sequence` section was written before this design and contradicts it: it puts byte-passthrough first on the grounds that it needs no edge stack.

**Files:**
- Create: `docs/specs/2026-08-19-edge-client-and-byte-passthrough-design.md` (already written, uncommitted)
- Create: `docs/plans/2026-08-19-edge-client.md` (this file, uncommitted)
- Modify: `docs/grimoire-1.6.0-migration.md` — the `## Sequence` section

- [ ] **Step 1: Read the current Sequence section**

```bash
sed -n '/^## Sequence/,/^## Workstream A/p' docs/grimoire-1.6.0-migration.md
```

It currently describes two tracks split on whether they need an edge stack, with byte-passthrough as track 1 step 1.

- [ ] **Step 2: Replace the Sequence section**

Replace everything from `## Sequence` up to (not including) `## Workstream A` with:

```markdown
## Sequence

`main` is feature-frozen at 1.5.6, so nothing here is ordered around avoiding
conflicts with it. The edge stack comes first because everything else is built
from the client generated against it — see
[the design](specs/2026-08-19-edge-client-and-byte-passthrough-design.md).

1. **An edge stack and a client generated from it.** A
   `docker/docker-compose.edge.yml` override runs `edge` beside the pinned 1.5.6
   stack; `src/GrimoireCli/Generated/` is regenerated from its spec. No behaviour
   a read of the generated diff. The gate is the smoke test against edge, which is
   what this branch targets; the pinned 1.5.6 stack is a bonus signal only, since
   one CLI version targets one server version.
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
```

- [ ] **Step 3: Verify no dangling reference to the old ordering**

```bash
grep -n "track 1\|Track 1\|track 2\|Track 2\|no edge stack needed" docs/grimoire-1.6.0-migration.md
```

Expected: no output. If any line matches, it is left over from the replaced section — remove it.

- [ ] **Step 4: Verify the internal anchors resolve**

```bash
grep -oE '\(#[a-z0-9-]+\)' docs/grimoire-1.6.0-migration.md | tr -d '(#)' | sort -u > /tmp/anchors-used
grep -E '^#{2,3} ' docs/grimoire-1.6.0-migration.md \
  | sed 's/^#* //' | tr '[:upper:]' '[:lower:]' \
  | sed 's/[^a-z0-9 -]//g; s/ /-/g' | sort -u > /tmp/anchors-have
comm -23 /tmp/anchors-used /tmp/anchors-have
```

Expected: no output. Any line printed is a link to a heading that does not exist.

- [ ] **Step 5: Commit**

```bash
git add docs/specs/2026-08-19-edge-client-and-byte-passthrough-design.md \
        docs/plans/2026-08-19-edge-client.md \
        docs/grimoire-1.6.0-migration.md
git commit -m "docs: design the edge client and byte-passthrough output

The migration document parked workstream B behind a released 1.6.0 tag
because generated response models did not exist yet. They exist in the
edge spec today, and this branch targets 1.6.0 — so generating from edge
comes first and the DTO deletion follows from it in one step instead of
being staged around a help-sample source that had no replacement."
```

---

### Task 2: Edge stack override

**Files:**
- Create: `docker/docker-compose.edge.yml`
- Modify: `docker/env.example` — add `GRIMOIRE_DATA_EDGE`
- Modify: `.gitignore` — add `docker/data-edge/`

**Interfaces:**
- Produces: an edge stack reachable at `http://host.docker.internal:9482`, seeded with the same fixture logins as the pinned stack (`admin/admin`, `gm/gm`, `player/player`). Task 3 reads its `/api/openapi.json`.

- [ ] **Step 1: Write the override**

This is a compose *override*, merged over `docker/docker-compose.yml` rather than replacing it, so the env block, healthcheck and add-on index service are inherited rather than duplicated. Three things must differ, and each has a reason:

Create `docker/docker-compose.edge.yml`:

```yaml
# The unreleased `edge` image, run BESIDE the pinned 1.5.6 stack rather than
# instead of it. Merged over docker-compose.yml, so everything not named here —
# env, healthcheck, the add-on index service, the read-only library mount — is
# inherited:
#
#   docker compose -f docker/docker-compose.yml -f docker/docker-compose.edge.yml \
#     -p grimoire-cli-edge up -d --wait
#   curl -sf http://host.docker.internal:9482/api/health
#
# The project name is required, not cosmetic: without -p both files' services
# share one project and the edge image would replace the pinned container.
#
# `edge` moves under you. It is not a release and nothing pins it — re-pull
# deliberately and record what you measured against:
#
#   docker pull hunterreadca/grimoire:edge
#
# Reset:  docker compose -f docker/docker-compose.yml -f docker/docker-compose.edge.yml \
#           -p grimoire-cli-edge down && rm -rf docker/data-edge
services:
  grimoire:
    image: hunterreadca/grimoire:edge
    # `!override` is required, not stylistic: Compose CONCATENATES ports across
    # merged files, so without it this stack publishes 9481 as well and fights
    # the pinned one for it. Measured with `docker compose config`.
    ports: !override
      # 9481 belongs to the pinned stack; both run at once.
      - "9482:9481"
    volumes:
      # A separate database. Two server versions indexing one data directory is
      # not a scenario either of them is built for, and the 1.5.6 stack's DB is
      # what the gating smoke test runs against.
      - ${GRIMOIRE_LIBRARY:-./library}:/library:ro
      - ${GRIMOIRE_DATA_EDGE:-./data-edge}:/data

networks:
  # Overriding the name, not the key. Both files declare the `grimoire-cli-dev`
  # network; leaving the name shared would put two `addon-index` containers on
  # one bridge, and `http://addon-index/index.json` — which the grimoire
  # container resolves by service name — would become ambiguous.
  grimoire-cli-dev:
    name: grimoire-cli-edge
```

- [ ] **Step 2: Ignore the new data directory**

`docker/data-edge/` holds a database and a seeded `users.json`, exactly like `docker/data/`. In `.gitignore`, immediately after the existing `docker/data/` line:

```
docker/data-edge/
```

- [ ] **Step 3: Document the new variable**

Append to `docker/env.example`:

```
# Host path for the edge stack's data directory (docker/docker-compose.edge.yml).
# Separate from GRIMOIRE_DATA on purpose: the two stacks run different server
# versions and must not share one database.
#
# Under docker-outside-of-docker this is NOT optional despite the default. The
# fallback ./data-edge resolves to the devcontainer's own path, which the host
# daemon cannot see, so the mount lands somewhere the server never reads. Set it
# to the host path, exactly as GRIMOIRE_DATA above.
GRIMOIRE_DATA_EDGE=/Users/you/Development/grimoire-cli/docker/data-edge
```

- [ ] **Step 4: Point `GRIMOIRE_DATA_EDGE` at the host path**

`docker/.env` is gitignored and already carries the host paths for the pinned
stack. Add the edge one beside them, using the same host prefix the existing
`GRIMOIRE_DATA` line uses:

```bash
grep -n 'GRIMOIRE_DATA' docker/.env
```

Take the directory from that line's value and append `-edge`, then add:

```
GRIMOIRE_DATA_EDGE=<same host prefix>/docker/data-edge
```

The workspace is bind-mounted, so `docker/data-edge` in the devcontainer and that
host path are one directory seen from two vantage points — the same split
`GRIMOIRE_LIBRARY` and `GRIMOIRE_LIBRARY_LOCAL` already document.

- [ ] **Step 5: Verify the merged config before starting anything**

`docker compose config` resolves the merge without touching a container, which
catches both mistakes this file exists to avoid.

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.edge.yml \
  -p grimoire-cli-edge config
```

Check three things in the output:

1. **Exactly one published port, `9482`.** Two entries means `!override` is not
   taking effect and the stack will collide with the pinned one on 9481.
2. **The `/data` bind source is a host path**, matching `GRIMOIRE_DATA_EDGE` — not
   a `/workspaces/...` path, which the host daemon cannot resolve.
3. **`networks.grimoire-cli-dev.name` is `grimoire-cli-edge`.** Sharing the pinned
   stack's network would put two `addon-index` containers on one bridge and make
   `http://addon-index/index.json` ambiguous.

- [ ] **Step 6: Seed the fixture users before the first boot**

Grimoire seeds users from `{DATA_PATH}/users.json` on first startup only, then renames the file. Skip this and the only symptom is a 401.

```bash
cd /workspaces/grimoire-cli
mkdir -p docker/data-edge
cp docker/users.json.example docker/data-edge/users.json
```

- [ ] **Step 7: Remove the hand-rolled container from the earlier investigation**

A `grimoire-edge` container created by a bare `docker run` may still be present, holding port 9482.

```bash
docker rm -f grimoire-edge 2>/dev/null; docker volume rm -f grimoire-edge-data grimoire-edge-lib 2>/dev/null; true
```

- [ ] **Step 8: Bring the stack up**

```bash
docker pull hunterreadca/grimoire:edge
docker compose -f docker/docker-compose.yml -f docker/docker-compose.edge.yml \
  -p grimoire-cli-edge up -d --wait
```

Expected: both `grimoire` and `addon-index` reach healthy/running.

- [ ] **Step 9: Verify the addressing claim**

This is the assumption the design flagged for verification: a published port was previously found unreachable from the devcontainer, but that was measured against a bare `docker run`, not a compose stack.

```bash
curl -sf http://host.docker.internal:9482/api/health && echo REACHABLE
```

Expected: `REACHABLE`.

If it fails, the compose stack is no better than `docker run` for addressing, and the fallback is the container's bridge address — every later command then takes that instead of `host.docker.internal:9482`:

```bash
docker inspect grimoire-cli-edge-grimoire-1 \
  --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}'
```

Record which of the two worked in the commit message — Task 3 bakes the URL into `kiota-lock.json`, and a container IP there would be an ephemeral address in a committed file.

- [ ] **Step 10: Verify the pinned stack is untouched**

```bash
curl -sf http://host.docker.internal:9481/api/health && echo PINNED-STILL-UP
docker ps --format '{{.Names}}\t{{.Image}}' | grep grimoire
```

Expected: `PINNED-STILL-UP`, and two grimoire containers listed — one on `1.5.6`, one on `edge`.

- [ ] **Step 11: Seed the edge stack**

`docker/seed.sh` writes fixtures into the shared library and then drives the server over HTTP, so it needs only the server URL.

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9482 bash docker/seed.sh
```

Expected: exits 0. It logs in as `admin`, writes fixture PDFs, rescans, and PATCHes fixture metadata.

- [ ] **Step 12: Verify the seed took**

```bash
TOKEN=$(curl -sf -X POST http://host.docker.internal:9482/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq -r .token)
curl -sf http://host.docker.internal:9482/api/systems -H "Authorization: Bearer $TOKEN" | jq 'length'
```

Expected: a non-zero count.

- [ ] **Step 13: Re-seed to confirm idempotence**

The pinned stack's smoke test is idempotent by design and the same must hold here, or a second run drifts.

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9482 bash docker/seed.sh
```

Expected: exits 0 again.

- [ ] **Step 14: Commit**

```bash
git add docker/docker-compose.edge.yml docker/env.example .gitignore
git commit -m "ci: run the edge image as a second local stack

Workstream B is built against a client generated from the edge spec, so
the branch needs an edge server without giving up the pinned 1.5.6 one
the gating smoke test runs against. An override rather than a second
compose file keeps the env block, healthcheck and add-on index service
in one place; the project name, port, data directory and network name
are the only things that can safely differ."
```

---

### Task 3: Regenerate the client from the edge spec

**Files:**
- Modify: `src/GrimoireCli/Generated/` (entire tree, by generator only)
- Modify: `src/GrimoireCli/Commands/RequestExamples.g.cs` (by generator only)
- Possibly modify: `src/GrimoireCli/Services/*.cs`, `src/GrimoireCli/Commands/*.cs` — only if regeneration renames a request builder a call site uses

**Interfaces:**
- Consumes: the edge stack from Task 2, at the URL Step 9 of that task established.
- Produces: `GrimoireCli.Generated.Models` containing response models — the source PR 2 draws `--help` samples from.

- [ ] **Step 1: Confirm the generator's preconditions**

```bash
kiota --version | head -1
jq -r '.kiotaVersion' src/GrimoireCli/Generated/kiota-lock.json
```

Expected: both report `1.34.1`. The script refuses to run otherwise, deliberately — a newer Kiota would mix generator churn into a diff whose whole value is showing API changes.

- [ ] **Step 2: Record the before state**

The diff is the deliverable, so capture what it is a diff from.

```bash
find src/GrimoireCli/Generated -name '*.cs' | wc -l
ls src/GrimoireCli/Generated/Models | wc -l
grep -c 'typeof(GrimoireCli.Generated.Models' src/GrimoireCli/Commands/RequestExamples.g.cs
```

- [ ] **Step 3: Verify the normaliser is still needed**

The design's claim is that kiota#2338 has not been fixed out from under us.

```bash
curl -s http://host.docker.internal:9482/api/openapi.json \
  | python3 tools/normalize-spec.py > /dev/null
```

Expected on stderr: `normalized 40 anyOf-nullable arrays (kiota#2338)`. If it reports 0, #2338 may be fixed — stop and check the issue, because CLAUDE.md says to delete the normaliser rather than carry a workaround for a fixed bug.

- [ ] **Step 4: Regenerate**

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9482 bash tools/generate-api-client.sh
```

Expected on stderr: `generating from http://host.docker.internal:9482 (Grimoire 1.5.6-…)` — edge builds report a 1.5.6 suffixed version, not 1.6.0, so this string is not a mistake — followed by a file count.

- [ ] **Step 5: Verify the lock records a stable address**

```bash
jq -r '.descriptionLocation' src/GrimoireCli/Generated/kiota-lock.json
```

Expected: `http://host.docker.internal:9482/api/openapi.json`. If it holds a container IP, the URL from Task 2 Step 9's fallback was used; rewrite it to the stable hostname so a committed file does not carry an ephemeral address:

```bash
jq --arg loc 'http://host.docker.internal:9482/api/openapi.json' \
  '.descriptionLocation = $loc' src/GrimoireCli/Generated/kiota-lock.json > /tmp/lock.json
printf '%s' "$(cat /tmp/lock.json)" > src/GrimoireCli/Generated/kiota-lock.json
```

- [ ] **Step 6: Confirm response models arrived**

This is the whole point of the task.

```bash
ls src/GrimoireCli/Generated/Models | wc -l
ls src/GrimoireCli/Generated/Models | grep -E '^(SystemDetail|BookOut|PublisherRef|MetadataDiffField)\.cs$'
```

Expected: a substantially higher count than Step 2, and all four files present. `PublisherRef` is the schema that proves upstream's `list[Any]` fix landed.

- [ ] **Step 7: Build**

```bash
dotnet build GrimoireCli.sln
```

Expected: 0 errors. If there are errors, they are call sites naming a request builder Kiota renamed. Each error names the missing symbol; find its replacement under `src/GrimoireCli/Generated/Api/` and update the call site. Change only the symbol — a renamed builder is not permission to change what a command sends, which would break this PR's no-behaviour-change constraint.

- [ ] **Step 8: Regenerate the examples file**

`RequestExamplesDriftTest` shells out to the generator and compares against the checked-in file, so a changed model set fails the test until this runs.

```bash
dotnet run --project tools/GenerateRequestExamples -- src/GrimoireCli/Commands/RequestExamples.g.cs
```

- [ ] **Step 9: Expect the examples file to grow, and check why**

The generator's rule is every public `IParsable` class in `GrimoireCli.Generated.Models`, so it now picks up response models too.

```bash
grep -c 'typeof(GrimoireCli.Generated.Models' src/GrimoireCli/Commands/RequestExamples.g.cs
```

Expected: far more entries than Step 2 recorded. This is correct and harmless — `RequestExamples.For(type)` is only ever queried for types passed to `AddRequestShape<T>()`, so the extra entries are unused, and PR 2 renames the tool to `GenerateJsonExamples` with exactly this whole-namespace rule. Do not add an exclusion list to suppress the growth.

- [ ] **Step 10: Format**

```bash
dotnet format GrimoireCli.sln
dotnet format GrimoireCli.sln --verify-no-changes
```

Expected: the second command exits 0.

- [ ] **Step 11: Run the unit tests**

```bash
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all pass, including `RequestExamplesDriftTest` and `RequestExamplesJsonValidTest`.

- [ ] **Step 12: Commit**

```bash
git add src/GrimoireCli/Generated src/GrimoireCli/Commands/RequestExamples.g.cs
git add -u src/GrimoireCli
git commit -m "feat: generate the api client from the edge spec

The generated tree is committed so a version bump produces a reviewable
diff, and this diff is that: 19 new operations and the response models
every success response now carries. Requests are unaffected — across all
207 operations shared with 1.5.6, no parameter or body field was removed
or became newly required, which is what keeps the pinned stack's smoke
test valid.

The examples file grows because its rule is every model in the generated
namespace, which now includes response models. They are unused until the
generators consolidate."
```

---

### Task 4: Verify against both stacks

The unit tests never touch HTTP, so nothing so far proves the regenerated client still talks to a 1.5.6 server.

**Files:**
- Modify: `docs/grimoire-api-notes.md` — only if the edge run surfaces a behaviour difference worth recording

- [ ] **Step 1: Publish the binary the smoke test drives**

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -o /tmp/grimoire-publish
```

- [ ] **Step 2: Smoke test against the pinned 1.5.6 stack — the gate**

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9481 CLI=/tmp/grimoire-publish/grimoire-cli \
  bash docker/smoke-test.sh
```

Expected: exits 0. **This is the gate.** A failure here means the regenerated client changed a request, which contradicts the measured compatibility — stop and diff the offending builder against the previous revision rather than adjusting the smoke test.

- [ ] **Step 3: Smoke test against the edge stack — signal only**

```bash
GRIMOIRE_SERVER=http://host.docker.internal:9482 CLI=/tmp/grimoire-publish/grimoire-cli \
  bash docker/smoke-test.sh
```

Expected: exits 0, but a failure here is not a gate. The migration document already records why: edge moves underneath the branch, so a red run may mean upstream changed rather than the branch broke.

- [ ] **Step 4: Record any edge-only difference**

If Step 3 failed, capture what differs — the endpoint, the request, and both servers' responses — and add it to `docs/grimoire-api-notes.md` under the relevant existing section, following that file's convention of stating what was verified and against which version.

Do not fix the CLI to accommodate edge in this PR. This PR has no behaviour change; a real edge incompatibility is either a PR 2 concern or an upstream report.

If Step 3 passed, skip this step and change nothing.

- [ ] **Step 5: Confirm no behaviour change reached stdout**

The constraint for this whole PR is that output is identical. Check one read command against the pinned stack:

```bash
/tmp/grimoire-publish/grimoire-cli --server http://host.docker.internal:9481 systems list | head -5
```

Expected: indented JSON, exactly as before this branch. Compact output here would mean PR 2's change leaked in early.

- [ ] **Step 6: Commit, only if Step 4 wrote something**

```bash
git add docs/grimoire-api-notes.md
git commit -m "docs: record the edge behaviour difference found while regenerating"
```

If Step 4 changed nothing, there is nothing to commit — proceed.

- [ ] **Step 7: Open the PR against the epic**

```bash
git push -u origin feat/edge-client
gh pr create --base epic/grimoire-1.6.0 \
  --title "feat: generate the api client from the edge spec" \
  --body "$(cat <<'BODY'
PR 1 of docs/specs/2026-08-19-edge-client-and-byte-passthrough-design.md.

Runs `hunterreadca/grimoire:edge` as a second local stack and regenerates
`src/GrimoireCli/Generated/` from its spec, so workstream B has generated
response models to draw `--help` samples from. No behaviour change: stdout,
flags and commands are identical, which is what makes the generated diff the
whole of the review.

**Why edge first.** The migration document parked workstream B behind a
released 1.6.0 tag because generated response models did not exist yet. They
exist in the edge spec today, and this branch targets 1.6.0 — so the DTO
deletion in PR 2 becomes one step instead of being staged around a help-sample
source with no replacement.

**The gate is edge**, which is what this branch targets, and it passes. The pinned
1.5.6 stack is a bonus signal — it also passes, and across all 206 shared
operations no parameter or request body field was removed or became newly
required, so the regeneration caused no accidental request churn.

**Not in this PR:** byte-passthrough output, the `Models/` deletion, the
generator consolidation, `--pretty`, and the `CLAUDE.md` response-DTO claims
that only become false once the DTOs go.
BODY
)"
```

- [ ] **Step 8: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked. A PR is done at "all checks green", not at "PR open". Epic CI may run the smoke test against edge, which is signal rather than gate — if that is the only red check, say so explicitly rather than treating it as a pass or a failure.

---

## Notes for the implementer

- **`docker/data-edge/users.json` is a one-shot.** Grimoire renames it to `users.json.imported` after seeding, so re-copying it into a live stack does nothing. Recreating users means `rm -rf docker/data-edge` and starting over from Task 2 Step 6.
- **A database-only reset leaves stale rows.** The boot scan indexes whatever library tree is on disk, so wiping `docker/data-edge` without also considering `docker/library` can leave systems that survive as `is_missing` and still count toward `book_count`.
- **The fixture library is shared and mounted read-only.** `docker/seed.sh` writes it from the devcontainer side, not through either server, so seeding one stack rewrites the fixtures both read. That is intended — the fixtures are not version-specific — but it means a re-seed aimed at edge also changes what the pinned stack will see on its next rescan.
- **`Shadowrun 4 DE` is deliberately left unpatched** by `seed.sh` as a fixture for the first metadata command. Do not spend it.
- **Writes go to these local stacks, never the live instance.**
