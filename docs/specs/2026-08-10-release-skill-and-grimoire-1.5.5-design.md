# Release skill and Grimoire 1.5.5 support — design

**Date:** 2026-08-10
**Status:** Design approved, not yet implemented.
**Targets:** Grimoire **v1.5.5** (released 2026-08-07), replacing v1.5.4.
**Related:** `grimoire-management` `docs/specs/2026-08-10-library-structure-design.md`,
which sets the folder grammar this repo's fixtures now mirror.

---

## 1. Why this exists

Two pieces of work land together because the second is what makes the first worth
having, and because they touch the same three files.

1. **The release skill has no counterpart here.** `abs-cli` carries
   `.claude/skills/release/SKILL.md`, an eight-step gated process.
   `docs/releasing.md` already describes that process in prose; the skill is its
   executable form. `.claude/` was excluded from the earlier parity tree-diff,
   which is why it was missed.
2. **Grimoire v1.5.5 changes the shape of the one resource this CLI reads.**
   `GET /api/systems` gains two query parameters and seven response fields, and
   the folder grammar gains system containers. The DTOs carry no
   `[JsonExtensionData]` by design, so every new field is silently absent from
   output until they are updated.

No release is cut in this change. The CLI covers `systems list` / `systems get`
and nothing else, which is not yet worth shipping; the skill is ported so that a
first release is a checklist rather than an improvisation when the surface
justifies one.

---

## 2. Verified v1.5.5 mechanics

Read from the `v1.5.5` tag, not from `main` and not from the upstream docs. Every
row cites the source that establishes it, because a claim in this repo without a
citation has historically been a claim that was wrong.

| mechanic | verified behaviour | source |
|---|---|---|
| container markers | `.parent-system-container` / `.one-page-container` files, or a `(parent-system)` / `(one-page)` folder-name suffix | `indexer/constants.py:52,65` |
| child display name | `f"{container.name} {folder.name}"` — so `Shadowrun` + `6 DE` = `Shadowrun 6 DE` | `indexer/scan.py:443` (`_child_display_name`) |
| `edition` | set from the child folder name **verbatim**: folder `6 DE` gives edition `6 DE`, not `6` | `indexer/scan.py:490` |
| `parent_system` | still a free-text column, but auto-set to the container's name on child creation | `indexer/scan.py:331` |
| sort prefixes | stripped from the container folder name before it is used, so `!!Dungeons & Dragons` still yields `Dungeons & Dragons` | `indexer/scan.py:188` |
| category depth | `system_depth=3` is hardcoded, so the category folder must sit exactly one level below the edition folder | `indexer/scan.py:504` |
| `GET /api/systems` params | gains exactly `parent_id` (string) and `include_children` (bool, default `false`); children are hidden from the default listing | `routers/systems/core.py:42,83` |
| `GET /api/systems/{id}` | returns the summary shape plus `books` **and a new `children` array** of summary rows | `routers/systems/core.py:186-196` |
| one-page collections | a **reserved slug alone declares a one-page container** — no marker file needed. `detect_container_kind` returns `CONTAINER_ONE_PAGE` for any of `one-page-rpgs`, `single-page-rpgs`, `one-shot-rpgs`, `micro-rpgs`. Marker files are tested first, so `.parent-system-container` can override a reserved slug | `indexer/categories.py::detect_container_kind`, `indexer/scan.py:189` |
| reserved slug list | **`micro-rpgs` is new in v1.5.5** (issue #262); v1.5.4 had only the other three | `indexer/constants.py:95-105` vs the same file at `v1.5.4` |
| one-page children | each loose file under a one-page **container** becomes its own system; `is_one_page` marks the collection, not its children | `indexer/scan.py:526`, `scan.py:317-321` |
| one-page child naming | `prettify_collection_name` capitalises any word containing no uppercase letter, so `Lasers and Feelings.pdf` becomes **`Lasers And Feelings`** | `indexer/categories.py:72-80` |
| `category` durability | re-derived **only when a book is re-homed to a different system**, not on every scan. A `PATCH category` still survives an ordinary rescan | `indexer/scan.py:725-731` — see §2.1 |

### 2.1 Corrections to the library spec

Two claims in `grimoire-management`'s library structure spec do not survive a
read of the tag. Both are reported back there; they are recorded here because
this spec would otherwise inherit them.

**`category` is not re-derived on every scan.** The library spec's §2.0 and §2.2
state that v1.5.5 flipped this, that the folder is therefore the sole durable
owner of category, and that any design leaning on `PATCH category` would be
silently re-shelved after the upgrade. In the whole backend, `Book.category` is
assigned in exactly two places: `scan.py:782`, the new-book insert, and
`scan.py:730`, which is guarded by `if existing.game_system_id != system.id` —
a **re-home**, not a rescan. An ordinary rescan leaves an existing book's
category untouched, exactly as on 1.5.4, so a `PATCH category` does hold.

The practical difference matters for their pass rather than ours. Their
conclusion — put category in the folder — remains the right call, and is safer
than the mechanism they attribute it to. But the failure they are guarding
against is not gradual drift on every scan; it is a **single wipe at the moment
of the container migration**, because turning a folder into a container re-homes
every book in it and that is precisely the branch that re-derives. A PATCHed
category set before their steps 1–2 is lost; one set afterwards persists.

**`0004_expand_metadata` does not exist.** The migrations added between `v1.5.4`
and `v1.5.5` are `0012_campaign_icon_color` and `0013_system_containers`. The
container work is real and the advice to back up before migrating is sound; the
migration name in their step 0 is not.

### 2.2 New response fields

`serialize_system_summary` gains seven fields
(`routers/systems/_serializers.py`):

| field | type | meaning |
|---|---|---|
| `has_cover` | bool | whether `GET /systems/{id}/cover` will serve something, so a client need not probe with a speculative 404 |
| `container_kind` | string | `""`, `"parent"`, or `"one-page"` |
| `parent_id` | string \| null | the container row this system belongs to |
| `parent_name` | string | the container's name, so a child needs no second request to resolve it |
| `parent_is_one_page` | bool | whether the container is a one-page collection |
| `name_is_custom` | bool | true once a user renames the system in the UI; the scanner then stops overwriting `name` |
| `child_count` | int | systems nested inside this one; zero for ordinary systems |

`GET /api/systems/{id}` additionally gains `children`, an array of the same
summary rows.

---

## 3. Part A — the release skill

`.claude/skills/release/SKILL.md`, ported from `abs-cli` with
`disable-model-invocation: true` so it only ever runs when asked for by name.

The eight-step shape is kept verbatim: preflight, branch and version bump,
release notes, PR for CI, tag and GitHub release, watch the release run,
verify an artefact, report. Every human gate is kept. Per `CLAUDE.md`, the skill
may commit without asking, because its commit steps *are* the approved workflow.

### 3.1 Deliberate deviations from abs-cli's skill

Recorded here and in `CLAUDE.md`'s "Relationship to abs-cli" section, per the
standing rule that drift between the two tools is only acceptable when the
reason is written down.

- **An added step: reconcile the supported-server range.** `MinSupportedVersion`
  and `MaxTestedVersion` in `GrimoireApiClient.cs`, the matrix in
  `docs/grimoire-compatibility.md`, and the compatibility line in `README.md`
  must agree before a tag is cut. `abs-cli` has no counterpart because it has no
  login-time version gate. This is step 3 of `docs/releasing.md` and has no home
  in the ported eight steps otherwise.
- **A different preflight.** The stack needs `docker/users.json.example` copied
  to `docker/data/users.json` before its first boot — skipping it produces a
  stack with no users whose only symptom is a 401. Under
  docker-outside-of-docker the daemon runs on the host, so the server is reached
  at `host.docker.internal:9481` and `GRIMOIRE_LIBRARY_LOCAL` governs where
  fixtures are written. Reset is `down && rm -rf docker/data docker/library/books`,
  not `down -v` — a database-only reset leaves the boot scan to re-index the old
  library tree as stale rows.
- **The `--version` check asserts bare output.** Release builds pass no
  `BuildId`, so `--version` must print `0.1.0` and not `0.1.0+pr-12.abc1234`.
  A suffix here means the release build picked up PR build metadata. See
  `docs/build.md`.
- **Names and paths** — `GrimoireCli.sln`, `src/GrimoireCli/GrimoireCli.csproj`,
  the `grimoire-cli` binary, and the `thomaslazar/homebrew-grimoire-cli` tap.

### 3.2 `docs/releasing.md`

The `HOMEBREW_TAP_TOKEN` repository secret was created on 2026-08-09, so its row
moves from outstanding to done and the prerequisites table no longer blocks a
first release. The file gains a pointer to the skill as its executable form; the
prose stays, because the skill is invoked by name and a reader looking for the
process should not have to know that.

---

## 4. Part B — Grimoire 1.5.5 support

### 4.1 Supported range

`MinSupportedVersion` and `MaxTestedVersion` both move to `1.5.5`. The range is
not widened to span both releases: the local stack pins one image tag, so a
claimed 1.5.4 range would be a range nothing exercises.

The consequence is accepted deliberately — the live instance still runs 1.5.4
and will emit a login-time warning until it is upgraded. That warning is
advisory and never refuses to run, and the library structure spec makes the
upgrade a prerequisite of its own first pass, so the instance is moving to 1.5.5
regardless.

### 4.2 Model changes

- `GameSystemSummary` gains the seven fields of §2.2, taking it from 24 to 31.
- `GameSystemDetail` gains `children` as a `List<GameSystemSummary>`. It already
  derives from `GameSystemSummary`, so the seven new fields reach the detail
  shape without further change.
- `JsonContext` registers whatever the new nesting requires for AOT
  serialization. The published binary is the only place a missing registration
  surfaces, which is why the smoke test runs against it.

### 4.3 Command surface

`systems list` gains `--parent-id <id>` and `--include-children`, matching the
two new query parameters. This follows the standing convention that every query
parameter the endpoint accepts is exposed as a flag, and the thin-pass-through
principle: the CLI forwards them and does not interpret them.

Help text records the one non-obvious behaviour — that container children are
**hidden from the default listing**, so a caller who wants every system must
pass `--include-children`. That is exactly the kind of outcome-affecting default
the help-text rules require surfacing at the call site.

`--help-full` response shapes are regenerated from the new DTOs.

### 4.4 Pins

`docker/docker-compose.yml` moves to `hunterreadca/grimoire:1.5.5`, and the
`temp/grimoire` reference clone is re-pinned to `v1.5.5`. The clone command in
`CLAUDE.md` moves with it.

---

## 5. Part C — the fixture library

The fixture is restructured to mirror the grammar the real library is adopting,
on the principle that a fixture which does not resemble the library it stands in
for cannot catch the failures that library will produce. Every fixture system
carrying an edition token becomes a container child.

```
docker/library/books/
├── Shadowrun/                     .parent-system-container
│   ├── 4 DE/core/
│   ├── 5 DE/core/
│   └── 6 DE/{core,supplements}/
├── Das Schwarze Auge/             .parent-system-container
│   └── 5 DE/core/
├── The Dark Eye/                  .parent-system-container
│   └── 5 EN/core/
├── !!Dungeons & Dragons/          .parent-system-container
│   └── 5e EN/{core,adventures}/
├── Vampire The Masquerade/        .parent-system-container
│   └── 5 EN/core/
├── Fixture Explicit RPG (nsfw)/   flat — the ordinary-system and nsfw case
│   └── core/
└── one-page-rpgs/                 .one-page-container
    ├── Honey Heist.pdf
    └── Lasers and Feelings.pdf
```

Three properties make this cheap:

- **Every system name is unchanged.** The container generates
  `"<container> <folder>"`, so `Das Schwarze Auge/5 DE` is still
  `Das Schwarze Auge 5 DE`. The seed's name-keyed PATCH lookups and the
  smoke test's name assertions keep working.
- **`Fixture Explicit RPG (nsfw)` stays flat**, so the ordinary-system path and
  the `(nsfw)` marker remain covered rather than being traded away for container
  coverage.
- **Single-child containers are realistic**, not a fixture artefact. The library
  spec's own target has three of four Shadowrun editions holding one or two
  books; a shelf holding one book today is the right home for the next one.

### 5.1 The one-page collection changes underneath us

**This one is not a design choice.** The folder is already called
`one-page-rpgs`, which is a reserved slug, so on v1.5.5 it becomes a one-page
container with no marker file and no edit from us (§2). Its two loose PDFs stop
being books of one system and each become a system in their own right.

The consequence is that **the existing fixture breaks on upgrade with zero
changes to it**: `seed.sh`'s closing `COUNT -eq 9` assertion fails, and the
comment above the one-page block — that the scanner "makes it ONE system whose
subfolder names become category labels, not one system per file" — becomes false
for v1.5.5. That comment records a correction made against v1.5.4 and is right
for that version; it needs the version attached rather than deleting, since the
claim it corrects was wrong then and is right now.

No marker file is added. The fixture is left to be what the slug already
declares, and the assertions move to meet it.

Note the naming quirk from §2: the resulting systems are `Honey Heist` and
**`Lasers And Feelings`**, with a capital `A`, because `prettify_collection_name`
capitalises any word containing no uppercase letter. The fixture pins this
rather than working around it — a silent rename is exactly what a smoke test
should catch. The container row keeps the raw slug `one-page-rpgs` as its name,
so the existing name assertion on it still holds.

### 5.2 `docker/seed.sh`

`edition` and `parent_system` are **removed from every PATCH body**. Both are
folder-derived under a container (§2), so leaving them in would overwrite
derived values with hand-set ones and mask whether derivation works at all.

`system_family`, `genres`, `year`, `license` and `publishers` stay: none has a
folder route on v1.5.5 — `.system-family-container` is `main`-only — so these
keep the PATCH path exercised.

PATCHes continue to target **child** rows by name, as they do today. Container
rows are left unpatched, which is also what the real library will look like:
a container is a shelf, and its metadata is the editions it holds.

`Shadowrun 4 DE` stays unpatched. It mirrors a fresh import and remains the
fixture the first metadata command will target.

The stale-flag warning in the script's header comment is re-verified against
`v1.5.5` rather than carried forward on trust.

### 5.3 Expected counts

| quantity | before | after |
|---|---|---|
| systems, default listing | 9 | 7 |
| child systems | 0 | 9 |
| systems with `--include-children` | 9 | 16 |
| books | 15 | 15 |

The nine children are three Shadowrun editions, one each for Das Schwarze Auge,
The Dark Eye, Dungeons & Dragons and Vampire The Masquerade, and the two
one-page games.

### 5.4 `docker/smoke-test.sh`

Gains assertions for the mechanics the fixture now exercises: `container_kind`
on a container and its absence on a flat system, `parent_id` and `parent_name`
on a child, `child_count` of 3 on Shadowrun, folder-derived `edition` of `6 DE`,
the default listing excluding children, and `--include-children` returning all
16. The count assertions in the existing 19 move to the §5.3 numbers.

---

## 6. Documentation

- `docs/grimoire-api-coverage.md` regenerated by `tools/generate-api-coverage.py`
  from a fresh v1.5.5 spec snapshot, with `IMPLEMENTED` updated in the script
  rather than the markdown. v1.5.5 adds **29 routes**, counted from the
  `add_api_route` registrations between the two tags: 13 bulk (`/bulk` and
  `/bulk/tags` on books, systems, maps, tokens and audio, plus three
  `*-folders/bulk`), 7 add-on management, 6 metadata lookup (`metadata-sources`,
  `metadata-search`, `metadata-fetch` on books and systems) and 3 system cover.
  None is implemented by this change; all 29 land in the table as uncovered.
- `docs/grimoire-api-notes.md` records the container mechanics of §2 with their
  source citations, and **restores `micro-rpgs` as a real reserved slug**. That
  file currently lists it among four false claims it corrected — right for
  v1.5.4, wrong from v1.5.5, where issue #262 added it. Every one-page claim in
  the file gets its version attached, because this is the second time an
  unqualified statement there has aged into a defect.
- `docs/grimoire-compatibility.md` matrix row for 1.5.5; `README.md`
  compatibility line and Commands table (two new flags).
- `docs/roadmap.md` records that **bulk endpoints shipped in v1.5.5**. The
  existing plan deferred them as "unreleased upstream `main`", and that premise
  no longer holds — which reopens the parked metadata-command design question,
  since a bulk route changes what a thin pass-through should offer.
- `CLAUDE.md`: the v1.5.5 clone pin, and the skill's deviations from §3.1.

`CHANGELOG.md` is untouched — it is owned by the release process and this is a
feature branch.

---

## 7. Out of scope

The new v1.5.5 surfaces get no commands in this change. They are recorded on the
roadmap and designed separately:

- `GET|POST|DELETE /api/systems/{id}/cover`
- `/api/{systems,books}/{id}/metadata-sources` and the metadata search/fetch pair
- `/api/addons/*`
- the bulk routes on systems, books, maps, tokens and audio

Also out of scope: the metadata command surface itself
(`PATCH /api/systems/{id}`, `PATCH /api/books/{id}`, `POST /api/rescan`), and
cutting a release.

---

## 8. Verification

The four pre-PR checks, plus the published binary, per `CLAUDE.md`:

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

The stack must be recreated from scratch, not re-seeded. Three reasons make
this mandatory rather than tidy: the image tag changes, `is_explicit` is never
cleared on an existing system row so a renamed or re-marked fixture folder
leaves a stale flag behind that only a database reset removes, and the boot
scan indexes whatever library tree is on disk — a database-only reset leaves
the old tree to be re-indexed as stale rows that survive as `is_missing` and
still count toward `book_count`.

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

Confirm `/api/openapi.json` reports `info.version: "1.5.5"` before trusting any
container assertion — the container fixtures produce a *wrong shelf* on 1.5.4
rather than an error, which is the failure mode worth gating on.

The AOT binary is the only check that catches a missing `[JsonSerializable]`
registration, so the smoke test is run against it as well:

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
```

The release skill is not fired as part of this change. Its preflight block is
verified by running the commands it contains by hand.

---

## 9. Open questions

| # | question | status |
|---|---|---|
| 1 | Does the bulk route change what the metadata command should look like — one PATCH per id, or a bulk body? | open, belongs to the metadata command design (§7), not to this change |

`parent_system` and `parent_id` are both returned by v1.5.5 and the CLI passes
both through. That is not an open question — thin pass-through settles it — and
is recorded here only because the library spec asks which one the CLI surfaces.
