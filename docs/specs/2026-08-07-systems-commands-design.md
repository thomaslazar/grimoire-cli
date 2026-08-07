# Systems commands, seeded fixtures and response examples — design

Date: 2026-08-07
Status: draft, awaiting review

## Goal

Implement `systems list` and `systems get` to the standard `abs-cli` established:
every query parameter the endpoint accepts is a flag, responses are typed DTOs,
commands are declarations over a service, `--help-full` carries generated response
shapes, and the whole surface is exercised by a smoke test against a seeded local
stack.

Testing filter permutations requires fixture data, so `docker/seed.sh` and its
library fixtures are part of this work rather than a follow-up: seed and smoke are
the staple testing tools for these CLIs and both must exist.

Out of scope: books commands, `PATCH` (metadata editing), `me` / `about` commands,
and any endpoint beyond the two named above.

## Grounding

Verified against Grimoire v1.5.4 (`temp/grimoire` at tag `v1.5.4`) and the live
OpenAPI spec. See [grimoire-api-notes.md](../grimoire-api-notes.md) for behaviour
that outlives this spec.

### What the endpoints accept

`GET /api/systems` — eight usable query parameters, none currently exposed:

| param | type | default | notes |
|---|---|---|---|
| `sort` | string | `name` | accepted: `name`, `book_count`, `page_count`, `year` |
| `order` | string | `asc` | `desc` only on exact match, else ascending |
| `genre` | string? | — | case-insensitive membership test |
| `family` | string? | — | matches `system_family` |
| `parent_system` | string? | — | |
| `edition` | string? | — | |
| `license` | string? | — | |
| `explicit` | bool? | — | tri-state: unset means no filter |

`GET /api/systems/{system_id}` — five, of which only the path id is exposed:

| param | type | default | notes |
|---|---|---|---|
| `book_sort` | string | `category` | accepted: `title`, `page_count`, `year` |
| `book_order` | string | `asc` | as above |
| `explicit` | bool? | — | filters the **books**, not the system |
| `genre` | string? | — | filters the books |
| `category` | string? | — | filters the books |

`token` and `grimoire_session` also appear on every path; both are browser
affordances (query-string auth for embedded images). The CLI uses the bearer
header and ignores them.

### Two behaviours that must reach help text

- **Unknown sort keys fall back silently.** `_sort_systems` does
  `key = sort if sort in _SYSTEM_SORT_KEYS else "name"`, and `_sort_books` does
  the same with `category`. A typo returns wrongly-ordered data with exit 0.
- **On `get`, filters change the summary counts.** `book_count` and
  `total_page_count` are computed from the *filtered* book list
  (`backend/routers/systems/core.py`), so `--category maps` reports counts
  describing only maps, not the system.

Also note `book_sort`'s default is `category`, which is **not** in `_BOOK_SORT_KEYS` —
the server's own default falls outside its whitelist and survives only because the
fallback lands on the same value.

### Response shapes

`GET /api/systems` returns a bare JSON **array** of `serialize_system_summary`
objects — 23 fields:

```
id, name, slug, description, publishers[], character_builder_url,
character_builder_urls[], urls[], tags[], genre, genres[], dice_materials[],
system_family, parent_system, edition, license, year, book_count,
total_page_count, cover_image, cover_book_id, is_explicit, is_system_agnostic,
is_one_page
```

`publishers[]` are `{name, url}`; `urls[]` and `character_builder_urls[]` are
`{label, url}`. `GET /api/systems/{id}` returns that same object plus
`books: [serialize_book(...)]`.

### Systems have no language field

`GameSystemUpdate` exposes 17 fields and `serialize_system_summary` returns 23;
**neither includes `language`**. It exists only on books (`BookUpdate.language`),
and `GET /api/systems` has no `language` query parameter. A system's language can
therefore only be expressed through its name (`Shadowrun 6 DE`, the existing
convention), a tag, or per-book metadata.

This does not change anything in this spec — there is no `--language` flag to add,
because there is no parameter to map it to — but it constrains the metadata work
this CLI exists for, so it is recorded here and in the API notes rather than
discovered later.

### Access

Both endpoints require an authenticated non-guest user. `admin`, `gm` and `player`
can all read them, so neither command gets an `AddRoleRequired` tag — matching
`abs-cli`, which tags only non-default permissions.

## Decisions

### Typed DTOs, no extension-data escape hatch

Responses deserialize into DTOs registered in `AppJsonContext`, and the command
re-serializes them — full `abs-cli` parity.

Explicitly rejected: a `[JsonExtensionData]` catch-all for unmodelled fields. The
CLI is a deterministic interface for agents, so its output must be a contract, not
a passthrough of whatever the server happens to send. If Grimoire 1.6 changes
shapes, the answer is to update the DTOs deliberately under the version-bump
procedure below — not to let unknown fields leak through unmodelled.

### Parse-time validation of sort keys

`--sort` and `--book-sort` use `AcceptOnlyFromAmong`, so an unknown value fails at
parse time instead of silently returning differently-ordered data. This is a
deliberate, narrow exception to "no client-side mirroring of server policy": the
value sets are tiny, fixed, already enumerated in the flag description, and the
server's failure mode is silent. `--book-sort` accepts `category` as well as the
three whitelisted keys, since that is the server's default.

Filter values (`--genre`, `--edition`, …) are **not** validated — they are open
strings whose valid values depend on library content.

### `--desc`, not `--order`

`--desc` as a boolean, mapped to `order=desc`, matching `abs-cli`'s
`authors list --desc`. `systems get` gets `--book-desc` for `book_order`.

### `--explicit` as a tri-state

`Option<bool?>`, so `--explicit true` / `--explicit false` / omitted map 1:1 onto
the API's nullable boolean. One flag per API parameter beats inventing
`--explicit` / `--not-explicit` pairs.

### Response examples are generated, not hand-written

Port `abs-cli`'s `tools/GenerateResponseExamples` (a reflection walker over the
DTOs emitting `ResponseExamples.g.cs`), with its drift test and JSON-validity
test. Hand-written shape blocks were considered and rejected: this CLI will
eventually cover most of the Grimoire API, and hand-maintained samples drift.
`HelpExtensions.AddShapeSection` stays as the rendering mechanism; the generator
supplies its content.

## Components

### 1. `docker/make-fixtures.py`

Generates the fixture files with **PyMuPDF** — the same library Grimoire reads them
with (`pymupdf==1.25.1` in the image), so parseability is guaranteed by
construction rather than hoped for:

```python
import fitz
doc = fitz.open()
for _ in range(pages):
    doc.new_page()
doc.save(path)
```

Takes a path and a page count, so fixtures can vary `page_count` for sort testing.
PDFs are the only fixture file type needed — see §2 for why there is no map
fixture. Should one ever be wanted, the same library renders PNGs
(`page.get_pixmap().save(...)`), so the dependency already covers it.

**Dependency, in two places:**

- `.devcontainer/Dockerfile` installs `python3-fitz`. **Changing the Dockerfile
  requires a container rebuild** — "Dev Containers: Rebuild Container" in VS Code —
  which the README's dev-container section must state, since `seed.sh` fails without
  it.
- The CI `smoke-test` job installs it too (`sudo apt-get install -y python3-fitz`),
  because GitHub runners have Python but not MuPDF. Without this the seeded assertions
  fail only in CI, which is the worst place to discover it.

### 2. `docker/library/` fixtures

Written by `seed.sh`, never committed. `docker/library/` is **not** currently
gitignored — only `.gitkeep` is tracked — so this work adds
`docker/library/*` + `!docker/library/.gitkeep` to `.gitignore`, or generated
fixtures show up as untracked noise on every run.

Real systems with accurate metadata, chosen so the filter matrix is meaningful and
so DE/EN pairs exist:

**One immediate subdirectory of `books/` is one system.** `scan.py:181-217` sets
`name` to the folder name and `slug` to its slugify, and parses **nothing** else
from the path — edition, language, family and parent system exist only as metadata
applied afterwards. So a German and an English edition of the same game need two
top-level folders; there is no `books/Shadowrun/6/de/` nesting to express it.

Folder names follow the real library, inspected on the live instance 2026-08-07:
the one system there is named **`Shadowrun 6 DE`** — edition and language in the
folder name, since neither can be expressed structurally — with category folders
`core`, `starter-set`, `supplements`, `adventures`, `homebrew`, `handouts`.

| system folder | category folders | books | metadata applied by PATCH |
|---|---|---|---|
| `Shadowrun 6 DE` | `core` 2, `supplements` 1 | 3 | family `Shadowrun`, parent `Shadowrun`, edition `6`, genre `Cyberpunk`, publisher Pegasus Spiele, year 2019 |
| `Shadowrun 5 DE` | `core` 2 | 2 | family `Shadowrun`, parent `Shadowrun`, edition `5`, genre `Cyberpunk`, publisher Pegasus Spiele, year 2013 |
| `!!Dungeons & Dragons 5e EN` | `core` 1, `adventures` 1 | 2 | family `D&D`, parent `Dungeons & Dragons`, edition `5e`, genre `Fantasy`, license `OGL`, publisher Wizards of the Coast, year 2014 |
| `Das Schwarze Auge 5 DE` | `core` 2 | 2 | family `The Dark Eye`, parent `Das Schwarze Auge`, edition `5`, genre `Fantasy`, publisher Ulisses Spiele, year 2015 |
| `The Dark Eye 5 EN` | `core` 1 | 1 | family `The Dark Eye`, parent `The Dark Eye`, edition `5`, genre `Fantasy`, publisher Ulisses North America, year 2016 |
| `Vampire The Masquerade 5 EN (nsfw)` | `core` 1 | 1 | family `World of Darkness`, parent `Vampire: The Masquerade`, edition `5`, genre `Horror`, publisher Renegade Game Studios, year 2018 |
| `Shadowrun 4 DE` | `core` 1 | 1 | **none — left raw on purpose** |

`Shadowrun 4 DE` is seeded but never PATCHed, mirroring the live instance's actual
state: a pure import with books indexed and every metadata field empty. That is the
starting condition this CLI exists to fix, so the fixture set should contain one.
It also sharpens the filters — `--family Shadowrun` and `--parent-system Shadowrun`
must return **2**, not 3, proving that a system with an empty field is excluded
rather than matched loosely. When the metadata commands land, it is the obvious
target for their smoke tests.

**Category folder names are not the category values.** `CATEGORY_MAP`
(`backend/indexer/constants.py:48`) normalises folder-name aliases onto canonical
singular categories, so the `supplements/` folder yields category `supplement` and
`adventures/` yields `adventure`. Confirmed on the live instance, whose 227 books
sit in `supplement` (175), `adventure` (30), `core` (11), `starter-set` (8),
`homebrew` (2), `handout` (1). Canonical values are `core`, `supplement`,
`adventure`, `character-sheet`, `map`, `handout`, `homebrew`, `starter-set`.

`systems get --category` therefore takes the **canonical** value — `--category
supplement`, never `--category supplements`, which silently matches nothing. That
belongs in the flag description and in `grimoire-api-notes.md`.

Seeded books carry only what the scanner derives — title from filename, category,
page count. `language`, `year`, `authors` and `publisher` come back empty, exactly
as they do on the live instance, and no book-level PATCH is needed since no
`systems` filter reads those fields.

Two scanner behaviours the fixture set exercises deliberately, both verified in
`scan.py:181-187`:

- **`(nsfw)` in a folder name sets `is_explicit` and is stripped from the name.**
  The Vampire folder therefore yields a system named `Vampire The Masquerade 5`
  with `is_explicit: true` **without** a PATCH — so `--explicit true` tests the
  scanner's own behaviour rather than something the seed script wrote.
- **Leading `!`, `$`, `%` sort-prefixes are stripped** (`strip_sort_prefix`). The
  D&D folder is created as `!!Dungeons & Dragons 5e EN` and must appear as
  `Dungeons & Dragons 5e EN`, which the seed asserts when it looks the system up by
  name to PATCH it.

The `&` in that name is also deliberate: it is what the real folder looks like, and
`--parent-system "Dungeons & Dragons"` returning exactly one system proves
`QueryBuilder` encodes filter values correctly.

No `maps/` fixture. Maps are a top-level library section beside `books/`, not a
system category, and nothing in `systems list` / `systems get` surfaces them.

Plus the two scanner edge cases from the API notes: a loose `stray.pdf` directly
under `books/`, and `one-page-rpgs/` with two files. Both become their own systems
and give the list rows with **no** metadata at all, so "filter matches nothing"
and "sort with null year" are covered.

That is **10 systems** total (7 named + 1 stray + 2 one-page), and every filter has
both matching and non-matching rows:

- `--genre`: Cyberpunk 2, Fantasy 3, Horror 1
- `--family`: Shadowrun 2, The Dark Eye 2, D&D 1, World of Darkness 1
- `--parent-system`: Shadowrun 2, the rest 1 each
- `--edition`: `5` matches 3 (SR5, DSA5, TDE5 — deliberately ambiguous across
  families), `5e` matches 1, `6` matches 1
- `--license`: OGL 1
- `--explicit true`: 1; `--explicit false`: 9
- four systems (`Shadowrun 4 DE`, the stray, the two one-page) match **no**
  metadata filter at all
- sorts: `book_count` 1–3, `page_count` varied per book, `year` 2013–2019 with the
  three metadata-less systems null (which sort last regardless of direction —
  `_sort_systems` special-cases that, and the smoke test asserts it)

### 3. `docker/seed.sh`

Modelled on `abs-cli`'s: bash + `curl` + `python3` for JSON, idempotent enough to
re-run, driven by `GRIMOIRE_SERVER`. Steps:

1. Wait for `/api/health`.
2. Log in as `admin` (from `users.json.example`) and keep the bearer token.
3. Write the fixture tree into the library directory (path from `GRIMOIRE_LIBRARY`,
   defaulting to `docker/library`), generating the PDFs via `make-fixtures.py` with
   per-book page counts that make `page_count` sorting observable.
4. `POST /api/rescan` with `metadata_mode: new` (returns `{"status":"scan_started"}`),
   then poll `GET /api/scan-status`. Verified shape:

   ```json
   { "running": false, "phase": null, "total_books": 1, "scanned_books": 1,
     "new_books": 1, "indexed": 1, "to_index": 1, "total_maps": 0, ... }
   ```

   The flag is `running`, and it reads `false` **before** the scan starts as well as
   after it finishes, so waiting for `running == false` alone can return instantly
   and wrongly. The poll must test positively: wait until `running == false` **and**
   `scanned_books` equals the number of books the fixture wrote, with a bounded
   timeout and a message naming both numbers on failure.
5. `PATCH /api/systems/{id}` per system to apply the metadata that folders cannot
   express — `parent_system`, `edition`, `system_family`, `genres`, `license`,
   `is_explicit`. IDs come from `GET /api/systems` matched on `name`.
6. Print a one-line summary of what was created.

The library is mounted `:ro` into the container, so seeding writes from the host
side and Grimoire only reads — no upload API is involved.

### 4. `Models/`

`GameSystemSummary` (23 fields), `GameSystemDetail` (summary + `books[]`), `Book`,
`PublisherEntry` (`{name,url}`), `LinkEntry` (`{label,url}`), each with
`JsonPropertyName` matching the API's snake_case exactly, all registered in
`AppJsonContext` along with `List<GameSystemSummary>`.

`GameSystemDetail` inherits from `GameSystemSummary` rather than redeclaring 23
fields.

### 5. `Api/QueryBuilder.cs`

One static helper: takes name/value pairs, skips nulls and empty strings,
URL-encodes both sides, returns `""` or `?a=b&c=d`. Filter values contain spaces
and ampersands (`Dungeons & Dragons`), so encoding is load-bearing, and the
behaviour is worth unit-testing in isolation.

### 6. `Services/SystemsService.cs`

```csharp
Task<List<GameSystemSummary>> ListAsync(string? sort, bool desc, string? genre,
    string? family, string? parentSystem, string? edition, string? license,
    bool? explicit)
Task<GameSystemDetail> GetAsync(string id, string? bookSort, bool bookDesc,
    string? genre, string? category, bool? explicit)
```

Builds the query string, calls `GrimoireApiClient`, deserializes through
`AppJsonContext`. `GetAsync` passes the existing `notFoundHint` so a bad id keeps
its current message.

### 7. `Commands/SystemsCommand.cs`

Rewritten as declarations over the service, following `AuthorsCommand`'s layout.

```
systems list [--sort name|book_count|page_count|year] [--desc]
             [--genre <g>] [--family <f>] [--parent-system <p>]
             [--edition <e>] [--license <l>] [--explicit true|false]

systems get  --id <id> [--book-sort category|title|page_count|year] [--book-desc]
             [--genre <g>] [--category <c>] [--explicit true|false]
```

`systems get` carries a `Notes` section recording that the filters apply to the
books list and that `book_count` / `total_page_count` are computed after filtering.
Both carry examples and a generated `Response shape` block.

### 8. `tools/GenerateResponseExamples`

Ported from `abs-cli`: a console project that reflects over the DTO types and
writes `src/GrimoireCli/Commands/ResponseExamples.g.cs`, plus
`ResponseExamplesDriftTest` (regenerate and diff) and a JSON-validity test.
`AddShapeSection` renders the generated sample under `--help-full`.

### 9. Smoke-test additions

`docker/smoke-test.sh` gains a seeded-data section. It currently asserts against an
empty instance; it will now run after `seed.sh` and assert:

- `systems list` returns an array of exactly **10** systems — the seven named
  folders, plus `stray.pdf` becoming its own single-book system, plus one per file
  in `one-page-rpgs/` (two). The number is defined once as a constant in
  `smoke-test.sh` beside the fixture list it mirrors, so adding a fixture forces the
  assertion to be updated rather than silently loosened.
- `--family Shadowrun` returns exactly 2, not 3: the raw `Shadowrun 4 DE` must be
  excluded, proving an empty field is not matched loosely
- each sort key returns valid JSON, and `--sort page_count --desc` is ordered
  descending (checked with `jq`, not assumed)
- each filter narrows the result: `--genre Cyberpunk` returns the two Shadowrun
  systems, `--edition 6` returns one, `--edition 5` returns three across different
  families, `--explicit true` returns Vampire only, `--parent-system Shadowrun`
  returns two, `--family "The Dark Eye"` returns the DE and EN pair, and
  `--license OGL` returns D&D only
- a filter value containing `&` round-trips: `--parent-system "Dungeons & Dragons"`
  returns exactly one system, proving `QueryBuilder` encoding rather than assuming it
- an unmatched filter returns `[]` rather than erroring
- `--sort bogus` exits non-zero **without** an HTTP call
- `systems get --id <shadowrun-6-de>` returns 3 books, and `--category core`
  narrows it to 2 **and** drops `book_count` to 2 — the filtered-counts caveat,
  asserted rather than merely documented
- `--category supplements` (the folder name) returns zero books while
  `--category supplement` (the canonical value) returns one, pinning the
  normalisation behaviour
- `systems get --id nope` exits 2 with the not-found hint

CI runs `seed.sh` before `smoke-test.sh`, mirroring `abs-cli`'s job, with
`python3-fitz` installed first (see §1).

**`smoke-test.sh` stops being idempotent** in one respect: it now depends on seeded
content. It still only reads, so re-running is safe, but it fails against an
unseeded stack. That is documented in its header and in CLAUDE.md's Pre-PR section.

### 10. Docs

- **`docs/grimoire-api-coverage.md`** — the `abs-api-coverage.md` counterpart: every
  endpoint in the spec, and which command implements it. Answers "what's
  implemented" from the repo.
- **`docs/grimoire-api-notes.md`** — four verified additions: the
  folder-alias-to-canonical category normalisation and its list of canonical values;
  `(nsfw)` in a system folder name setting `is_explicit`; the `!$%` sort-prefix
  stripping; and the absence of any system-level `language` field. The first three
  are scanner behaviour a command author would otherwise rediscover; the fourth
  constrains the metadata work this CLI is being built for.
- **`docs/grimoire-compatibility.md`** — version matrix, the runtime version check,
  and the bump procedure below.
- **`CLAUDE.md`** — three maintenance rules it currently lacks: update the README
  Commands table in the same PR as any command change; update
  `grimoire-api-coverage.md` in the same PR as any endpoint change; and tag
  non-default roles with `AddRoleRequired`, mirroring the string into the service's
  `permissionHint`.
- **`README.md`** — Commands table updated with the new flags, and the
  `config set` key list corrected to `server` only (it currently claims
  `accessToken`, which `ApplyConfigSet` rejects).

## Version-bump procedure

Recorded in `docs/grimoire-compatibility.md`, mirroring `abs-cli`'s "Handling ABS
Updates" but mechanised, because Grimoire publishes an OpenAPI spec where ABS does
not:

1. Re-point the reference clone: `git -C temp/grimoire fetch --depth 1 origin tag vX.Y.Z && git -C temp/grimoire checkout vX.Y.Z`
2. **Diff the two specs structurally** — added/removed paths, changed parameters,
   changed request schemas. No committed baseline is needed: both image tags are on
   Docker Hub, so a script starts each version in turn with `OPDS_ENABLED=false`,
   pulls `/api/openapi.json` from each, and diffs them. This keeps the rule that the
   spec is always lifted from a running instance rather than checked in, and it is a
   script rather than an eyeball pass.
3. Diff the serializers for the response shapes the spec leaves untyped:
   `git -C temp/grimoire diff vOLD..vNEW -- backend/routers/*/\_serializers.py backend/routers/*/core.py backend/models/`
4. Update DTOs, flags and help text for anything that moved.
5. Bump the compose image tag, re-run `seed.sh` + `smoke-test.sh` against it.
6. Update `MinSupportedVersion` / `MaxTestedVersion`, the compatibility matrix, and
   the README compatibility line.

No spec snapshot is committed at any point — `temp/grimoire-openapi.json` stays a
working copy pulled from a running instance, and the bump script pulls both sides of
the comparison the same way.

## Risks

- ~~**Hand-built PDFs may not satisfy PyMuPDF.**~~ Resolved by generating fixtures
  with PyMuPDF itself. What remains is an environment risk rather than a format one:
  `seed.sh` must fail with a clear message if `import fitz` is unavailable, naming the
  container rebuild as the fix, rather than producing an empty library and letting the
  smoke test fail obscurely downstream. It still verifies a non-null
  `total_page_count` after the rescan, which catches a fixture that scanned but
  yielded nothing.

  Verified end to end against the running stack on 2026-08-07: a 3-page PDF written
  by `fitz` was picked up by a rescan and reported as
  `{"name":"Test System","book_count":1,"total_page_count":3}`. Generation, the bind
  mount, the rescan and page counting all work.
- ~~**Scan timing.**~~ Characterised rather than guessed — see §3 step 4. `running`
  reads `false` both before and after a scan, so the poll tests `scanned_books`
  against the known fixture count instead of trusting the flag alone.
- **DTO drift against 1.5.4 itself.** The DTOs are written from the serializers, not
  from observed traffic. The smoke test must assert on real fields (`book_count`,
  `is_explicit`) so a mis-transcribed name fails rather than silently emitting null.
- **Generated file churn.** `ResponseExamples.g.cs` is committed and drift-tested;
  forgetting to regenerate fails CI with instructions, as in `abs-cli`.

## Acceptance

- `docker compose up -d --wait && bash docker/seed.sh` produces the fixture systems
- `bash docker/smoke-test.sh` passes every assertion in §9
- `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test` all clean
- `systems list --help` shows every flag; `--help-full` shows the response shape
- `docs/grimoire-api-coverage.md` lists all four implemented endpoints and marks the
  rest unimplemented
- CI green, including the new `seed.sh` step
