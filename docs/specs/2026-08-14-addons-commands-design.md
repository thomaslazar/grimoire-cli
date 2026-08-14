# Add-on commands — design

Date: 2026-08-14
Status: draft, awaiting review

## Goal

Seven commands over Grimoire's add-on endpoints, so the CLI can install and
manage the sources that metadata lookup depends on.

```
addons list  refresh  install  update  upgrade-all  uninstall  settings
```

The metadata-lookup trio is the next roadmap item and the release gate. On a
stock instance that trio has nothing to talk to: no add-on is installed, and the
add-on index is not even fetched until something asks for it. Today the only way
from a fresh Grimoire to a working `metadata-search` is `curl`. These seven close
that gap, the same way `library` was pulled into the books spec because
`books rescan` could not discover a hand-copied file.

Out of scope: the metadata trio itself, and `POST /api/maintenance/cleanup-missing`.
Both are the next branch. The confirm-gate question that `cleanup-missing` forces
is deferred with it — `addons uninstall` removes a directory that can be
reinstalled in one command, which is not the class of loss that would justify
settling the question here.

## Grounding

Verified against Grimoire v1.5.6 by reading `temp/grimoire` **and by calling the
running stack**, which is how the shapes below were confirmed rather than
inferred. Behaviour that outlives this spec belongs in
[grimoire-api-notes.md](../grimoire-api-notes.md).

### An add-on is two static files

`refresh` fetches `index_url` as JSON. Installing resolves
`urljoin(index_url, entry.path)`, downloads a YAML manifest, and verifies it
against the index entry's `sha256`
(`backend/addons/install.py:119-160`). Script-backed add-ons additionally fetch a
script named by the staged manifest rather than by the index's claim about it.

There is no packaging, no archive, and `fetch_json` (`backend/addons/fetch.py:91`)
is plain httpx with no scheme allow-list. A local HTTP URL therefore works
everywhere the community index does, which is what makes the fixture below cheap.

### The two published add-ons

`POST /api/addons/refresh` against the default index yields two, both authored by
the Grimoire maintainer, both `requires_script: false`:

| id | target | what it serves |
|---|---|---|
| `drivethrurpg` | `book` | authors, artists, publisher, page count, genres, ISBN |
| `ttrpg-wiki` | `game-system` | publisher, year, family, dice, licence, genre |

Their targets are disjoint, which is why `metadata-sources` returns a different
list for a book than for a system. Nothing here is bundled with the server: an
instance that never refreshes its index has no add-ons available at all.

### Two add-on shapes, not one

| Shape | Source | Distinctive fields |
|---|---|---|
| `AddonInstalled` | `describe()` (`backend/addons/registry.py:257`) | `enabled`, `runnable`, `blocked_reason`, `script_approved`, `source`, `available_version` |
| `AddonAvailable` | the index row (`routers/addons/core.py:36-52`) | `script_sha256`, `installed` |

`GET /api/addons` returns both lists in one envelope. An installed add-on that
also appears in the index gets its `available_version` and `update_available`
annotated on the installed row too, so an available upgrade is visible on the row
a caller is actually reading.

**`runnable` is the field that explains an empty `metadata-sources`.** An add-on
can be installed and enabled and still not run — a script-backed one whose script
is unapproved, for instance — and `blocked_reason` carries why. Nothing else in
the API says this.

## Commands

`--server` and `--token` are declared per subcommand on all seven. Every command
calls `AddRoleRequired("admin")` and passes `permissionHint: "the admin role"`.

| Command | Endpoint | Flags |
|---|---|---|
| `addons list` | `GET /api/addons` | — |
| `addons refresh` | `POST /api/addons/refresh` | — |
| `addons install` | `POST /api/addons/{id}/install` | `--id`, `--approve-script` |
| `addons update` | `PATCH /api/addons/{id}` | `--id`, `--enabled`, `--script-approved` |
| `addons upgrade-all` | `POST /api/addons/update-all` | — |
| `addons uninstall` | `DELETE /api/addons/{id}` | `--id` |
| `addons settings` | `PATCH /api/addons/settings` | `--index-url`, `--allow-scripts` |

### Naming: `upgrade-all`, not `update-all`

The API puts two unrelated verbs behind one word. `PATCH /api/addons/{id}` sets
fields on one resource — `enabled`, `script_approved` — which is exactly what
`systems update` and `books update` do, so `addons update` is the convention
already established twice. `POST /api/addons/update-all` changes *versions*,
which no other command in this CLI does.

Naming it `upgrade-all` keeps `update` meaning "change fields" across all three
groups and gives version changes a word of their own. The cost is one command
whose name differs from its path segment; the coverage table pairs commands to
endpoints explicitly, so nothing is lost that the table does not already carry.
Recorded here so it is not "fixed" back to the path name.

Single-add-on upgrade has no separate command: `POST /{id}/install` is documented
upstream as "install or update", so `addons install` covers both and says so.

### Flag types follow the models

`--enabled`, `--script-approved` and `--allow-scripts` are `Option<bool?>`
rendered `true | false`. Their models declare them `Optional[bool] = None`, and
PATCH ignores what is absent, so an omitted flag must stay absent — a plain
switch could set but never clear. This is the `systems list --explicit`
tri-state, reused.

`--approve-script` is a plain switch. `AddonInstall.approve_script` is
`bool = False`, not optional, so an absent flag already means exactly the
server's default.

No command takes `--input`/`--stdin`. All three request bodies are one or two
scalars, composed from flags per the `library rescan` rule, and none registers a
request shape: a Request shape block would document a body the caller never
writes.

### `addons settings` requires at least one flag

The endpoint is PATCH-only and has no `GET` counterpart — current values are
visible inside `addons list`. A no-flag call would therefore be a legal write
that changes nothing and returns the current pair, making one command sometimes a
read and firing a PATCH to answer a question.

A command validator rejects it as a parse error before any client is built, the
way `--input`/`--stdin` exclusivity already does. Reading the values stays one
obvious place.

## Response DTOs

New, all registered on `AppJsonContext`:

- **`AddonInstalled`** — `id`, `name`, `version`, `kind`, `target`,
  `description`, `homepage`, `attribution`, `requires_script`, `script_approved`,
  `enabled`, `runnable`, `blocked_reason`, `source`, `available_version`,
  `update_available`. Returned by `install` and `update` as well as inside `list`.
- **`AddonAvailable`** — `id`, `name`, `kind`, `target`, `version`,
  `description`, `homepage`, `requires_script`, `script_sha256`, `installed`,
  `update_available`.
- **`AddonListResponse`** — `installed`, `available`, `index_url`,
  `default_index_url`, `allow_scripts`, `index_generated`.
- **`AddonSettings`** — `index_url`, `allow_scripts`.
- **`RefreshResult`** — `status`, `count`.
- **`UpgradeAllResult`** — `status`, `updated` (`AddonUpgrade`: `id`, `from`,
  `to`), `failed` (`AddonUpgradeFailure`: `id`, `error`).

`from` is a C# keyword; the property is named accordingly with
`[JsonPropertyName("from")]` carrying the wire name, as every DTO here already
does.

`AddonUpgradeFailure` cannot reuse `BulkError`: the bulk endpoints name the field
`detail` and this one names it `error`.

## Help text

`--help-full` is the primary interface for the agents driving this CLI, so this
section is a requirement, not a formatting note. Terseness is calibrated against
`SystemsCommand.cs`.

### Which shape blocks each command registers

| Command | Request shape | Response shape |
|---|---|---|
| `addons list` | — | `AddResponseExample<AddonListResponse>()` |
| `addons refresh` | — | `AddResponseExample<RefreshResult>()` |
| `addons install` | — | `AddResponseExample<AddonInstalled>()` |
| `addons update` | — | `AddResponseExample<AddonInstalled>()` |
| `addons upgrade-all` | — | `AddResponseExample<UpgradeAllResult>()` |
| `addons uninstall` | — | — |
| `addons settings` | — | `AddResponseExample<AddonSettings>()` |

No request shapes anywhere, for the reason given above. `uninstall` returns
`{"status":"ok"}` and only that, so it names the value in Notes rather than
rendering it as `"<string>"` — the rule `systems update` set.

`refresh` does register a shape despite being nearly a status response, because
`count` is a real datum: it is how a caller knows the index actually had entries.

### Notes, per command

Verbatim, so the implementer writes no prose of their own.

**`addons list`**

```
available comes from the cached index — empty until addons refresh runs,
and stale afterwards until it runs again. index_generated is when the
cache was built.

runnable false while enabled is true means the add-on is installed but
blocked; blocked_reason says why. Only runnable add-ons appear as
metadata sources.
```

**`addons refresh`**

```
Fetches index_url over the network; count is what the index offered.

Installing needs a cached index, so a fresh instance runs this first.
```

**`addons install`**

```
Takes an id from available in addons list; 400 if the index has no such
entry, 502 if the download fails. The manifest is verified against the
index's digest.

Also upgrades: re-running on an installed add-on replaces it.

--approve-script is consent to run third-party code, recorded against
the script's digest and ignored for add-ons that ship no script. An
upgrade that changes the script drops back to unapproved.
```

**`addons update`**

```
Changes state, never version — upgrade with install or upgrade-all.

404 if no such add-on is installed.
```

**`addons upgrade-all`**

```
Refreshes the index first, and carries on with the cached one if that
fails.

Skip-and-continue: an add-on that cannot be upgraded lands in failed and
the rest still upgrade. Exit 3 is HTTP 200 with a non-empty failed list.

Script approval is not carried over, so a script-backed add-on is
unapproved until re-approved with install --approve-script.
```

**`addons uninstall`**

```
Removes the add-on's directory and forgets its state; reinstall with
addons install --id. 404 if it is not installed.

Responds {"status": "ok"}.
```

**`addons settings`**

```
At least one flag is required.

Changing --index-url does not refetch; run addons refresh after.

--allow-scripts is the global switch. An add-on that ships a script also
needs its own approval, from install --approve-script.
```

### Flag descriptions worth stating here

`--id` is `"Add-on ID"`, matching the existing `--id` descriptions. `--approve-script`
is `"Consent to run this add-on's script; ignored when it ships none"`.
`--index-url` is `"Add-on index URL"`. The three tri-state booleans describe what
they set and nothing else, since `true | false` renders from the option type.

## Exit codes

`BulkExit.CodeFor` generalises to any failure list so `addons upgrade-all` can
return 3 on a non-empty `failed`. This is the third use of exit 3 and it means
what the other two mean: HTTP 200, and not what was asked for. An agent that
upgrades everything, sees exit 0 and moves on is otherwise running add-ons it
believes are current and are not — and the failures are per-add-on network
errors, so this is the ordinary case on a bad day.

Everything else exits 0, or 2 on an HTTP error.

`docs/input-output.md`'s exit-3 entry gains the third case.

## Testing

### A local fixture index

The install path is the only part of this group with real machinery in it —
staging directory, digest verification, approval keyed to the script's digest —
and it is unreachable without an index to install from. Pointing the smoke test
at the community index would make every PR build depend on
`raw.githubusercontent.com` and on a third party's host, and would install
third-party content on each run.

Instead, a static-file service in `docker-compose.yml` serves
`docker/addon-index/` on the compose network, reachable from the grimoire
container by service name. Fixture generation writes an `index.json` whose entry
carries the sha256 of a minimal manifest:

```yaml
id: fixture-source
name: Fixture Source
version: 1.0.0
kind: scraper
target: game-system
```

`AddonManifest` (`backend/addons/manifest.py:288`) is strict but requires only
`id`, `name`, `version` and `kind: scraper`; `id` must be lowercase alphanumeric
with hyphens. The fixture answers no searches, which install, update, upgrade and
uninstall never consult. When the metadata trio lands, this same fixture grows
`source`/`search`/`map` and becomes the fake source that makes `search` and
`fetch` testable without scraping anyone.

### Smoke test

Idempotent, per `CLAUDE.md`: point `--index-url` at the fixture service, refresh,
install, list, toggle with `update`, run `upgrade-all`, uninstall, then restore
the default index URL. A second run converges because every write is either
fixed-value or undone by the last step.

Two assertions worth naming because they are the ones a weaker test would skip:
`addons list` must show the fixture under `installed` with `enabled: true`
between install and uninstall, and `addons settings` with no flags must exit 1
without a request.

**The smoke test never points the stack at the community index**, so the fixture
service is the only index it ever sees.

### Unit tests

The existing areas: `Models/` for the six DTOs, `Commands/` for the role tags,
the shape blocks, the no-flag refusal on `settings`, and the tri-state flags
being absent from the request when omitted — the `LibraryServiceTests` pattern
that pins Kiota's constructor defaults.

## Docs

- README Commands table — seven rows.
- `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate.
- `docs/input-output.md` — the third exit-3 case.
- `docs/grimoire-api-notes.md` — whatever the live runs verify that the source
  alone did not settle.
- `docs/roadmap.md` — no change. The roadmap lists intended work and an item
  leaves when it ships; this one is decided and shipped in the same branch, so it
  never lands there. The metadata-lookup item stays as it is, now with its
  prerequisite met.

## Risks

**The fixture service is new infrastructure in the compose stack.** It is a
static file server, so the failure modes are name resolution and a wrong path
rather than anything subtle, but it is one more thing that must be up for the
smoke test to pass, and `CLAUDE.md`'s reset procedure grows a directory.

**Digest verification means the fixture cannot be hand-edited.** Editing the
manifest without regenerating `index.json` makes every install fail on a
mismatch. The generator must be the only way that file changes, and the smoke
test's failure message should point at it.

**`upgrade-all` is hard to exercise meaningfully.** With one fixture at one
version there is nothing to upgrade, so the command returns empty lists and exit
0. That asserts the plumbing and not the skip-and-continue path. Publishing a
second fixture version to exercise a real upgrade is possible but adds fixture
machinery for one assertion; the honest position is to assert the empty case and
say so, rather than dress it up as coverage of the failure path.
