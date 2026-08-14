# Metadata lookup — design

Date: 2026-08-14
Status: draft, awaiting review

## Goal

Six commands over Grimoire's add-on metadata endpoints — the same three on
systems and on books, since the server exposes one flow against two targets:

```
systems metadata-sources  metadata-search  metadata-fetch
books   metadata-sources  metadata-search  metadata-fetch
```

This is roadmap item 1 and the release gate: the point at which the CLI can
*find* metadata as well as edit it. `addons` shipped the machinery that installs
the sources these read from; without this trio, an installed add-on has no
caller.

Out of scope: applying a fetched value. There is no endpoint for it — see
[No apply path exists](#no-apply-path-exists).

## Grounding

Verified against Grimoire v1.5.6 by reading `temp/grimoire`. The shapes below
come from `routers/_metadata_lookup.py`, `addons/diff.py` and
`addons/interpreter.py` rather than from the spec, which types every success
response as `{}`. Anything the live runs confirm that the source did not settle
goes to [grimoire-api-notes.md](../grimoire-api-notes.md) when it ships.

### One implementation, two targets

`routers/_metadata_lookup.py` is shared by both routers: `list_sources`,
`search` and `fetch` take the resource as an argument and are otherwise
identical, and the systems and books `MetadataSearch` / `MetadataFetch` request
models are field-for-field the same. The two routers differ only in which
object they load and which target string they filter add-ons by. The CLI mirrors
that: one command factory and one service, parameterised by path segment.

### No apply path exists

All three endpoints are reads. `fetch` writes nothing — its docstring says so,
and both routers repeat it — and there is no apply endpoint anywhere in the API.
The only write is `PATCH /api/systems/{id}` / `PATCH /api/books/{id}`, already
shipped as `systems update` and `books update`, where the caller sends the exact
fields it chose.

This is worth recording because it is the property the workflow depends on: a
fetch can be run, read, edited and discarded without touching the resource. The
caller decides what to apply, and may enrich or override a value on the way.
Nothing here needs to opt out of a server-side apply, because there is none to
opt out of.

Three further guarantees, all from `addons/diff.py:build`:

- **`current` ships alongside `incoming`**, so a caller sees what it would
  overwrite before it writes anything.
- **`incoming` for `urls` and `character_builder_urls` is already the union**
  with the resource's existing list (`_merge_links`), keyed on URL with existing
  entries winning on label. Applying an incoming link list therefore cannot drop
  a link the user added. Every other field's `incoming` is a replacement value.
- **A field the source has nothing for is omitted**, not emitted as empty, so a
  fetch never proposes blanking a populated field.

A source's reach is bounded by `MAPPABLE_SYSTEM_FIELDS` (12) and
`MAPPABLE_BOOK_FIELDS` (16) in `addons/manifest.py`, enforced when the manifest
loads and again on script output. Ids, file paths, cover images and indexing
flags are not reachable from an add-on at all.

### The three steps

| Step | Answers | Feeds |
|---|---|---|
| `metadata-sources` | which add-ons can answer for this resource | a `source_id` |
| `metadata-search` | which record at that source | an `identity` |
| `metadata-fetch` | what that record says vs. what we have | a hand-picked `update` |

`fetch` needs an `identity`, and `search` is how one is obtained. `--paste`
short-circuits it: a source URL or bare id resolves to an identity directly
(`service.resolve_identity`), but only where the manifest declares an
`identity_pattern` — which is exactly what `supports_paste` on each source row
reports, and why that flag is in the sources response at all.

`query` is echoed back into `fetch` because a search-backed source answers per
query rather than serving a cacheable catalogue; the record lives in that
query's response (`service.fetch_fields`).

### Empty results have three distinct causes

An empty `sources` list means no *runnable* add-on targets this resource type —
not installed, not enabled, blocked (`runnable: false`, with `blocked_reason`),
or targeting the other resource. `addons list` is where that is diagnosed.

An empty `results` list means the add-on ran and matched nothing at or above
`min_score`. `service.search` also returns `[]` for a blank effective query,
which cannot happen through the CLI: the server substitutes the system's name or
the book's title before the add-on sees it.

A failing source is neither — it is a 502.

### Status codes

`_translate` maps add-on failures onto two codes, and the distinction is
useful enough to surface in help: **502** when the source is at fault
(unreachable, script error, unexpected data) and **400** when the request or
configuration is (disabled add-on, unknown source, no identity chosen, a paste
the source cannot parse). 404 is the resource itself.

## Commands

`--server` and `--token` are declared per subcommand on all six. Every command
calls `AddRoleRequired("gm or admin")` and passes
`permissionHint: "the gm or admin role"` — `require_gm_or_admin` guards all six
routes.

| Command | Endpoint | Flags |
|---|---|---|
| `<res> metadata-sources` | `GET /api/<res>/{id}/metadata-sources` | `--id` |
| `<res> metadata-search` | `POST /api/<res>/{id}/metadata-search` | `--id`, `--source-id`, `--query` |
| `<res> metadata-fetch` | `POST /api/<res>/{id}/metadata-fetch` | `--id`, `--source-id`, `--identity`, `--query`, `--paste` |

`<res>` is `systems` and `books`; the two sets are identical in flags, help text
and behaviour.

### Flat names, not a nested group

`systems metadata-sources`, not `systems metadata sources`. Every command in
this CLI is two levels deep and a third level would be new machinery for a
naming preference. The leaf names then mirror their path segments one for one.

abs-cli puts its metadata commands in a top-level `metadata` group, which is
**not** the precedent to port here: ABS's endpoints take `--provider --title
--author` and no item id, so a resource group was never an option there. Where
an ABS action *is* keyed to a resource id it lives under that resource —
`authors match --id`. Grimoire's six are all id-keyed. Recorded so the top-level
group is not "restored" to match abs-cli.

### One factory, called twice

`MetadataCommands.Create(string resource)` returns the three subcommands, and
`SystemsCommand` / `BooksCommand` each add them. `MetadataService` takes the
same path segment. The server shares one implementation across both targets; a
second copy here would be a copy of something upstream deliberately does not
duplicate.

The resource noun appears in help text only where it changes meaning — the
default that `--query` falls back to is the system's name or the book's title.
That is one string, parameterised.

### Validation

Parse-level only, and no mirroring of server policy:

- `--source-id` is `Required = true` on `search` and `fetch`. The models declare
  it non-optional.
- `fetch` takes **exactly one** of `--identity` / `--paste`, enforced by a
  command validator, the way `addons settings` and `--input`/`--stdin` already
  do. Neither is a request the server answers with anything but a 400
  ("no result was chosen"); both is ambiguous, and the server silently prefers
  `paste`. Rejecting before the client is built keeps that from being a quiet
  surprise.
- `--query` stays optional everywhere. Omitted, the server substitutes the
  resource's own name or title, which is the common case. The CLI must not
  pre-fill it — that would be client-side mirroring of a server default, and the
  server's fallback reads the live record.

Whether `--paste` is supported by the chosen source is **not** validated
client-side. It is server policy, it is already reported by `supports_paste`,
and checking it would need a second request.

## Response DTOs

New, all registered on `AppJsonContext`:

- **`MetadataSource`** — `id`, `name`, `description`, `homepage`,
  `attribution`, `supports_paste`.
- **`MetadataSourceList`** — `sources`.
- **`MetadataCandidate`** — `identity`, `label`, `score` (double), `url`.
- **`MetadataSearchResult`** — `query` (the effective query, after the server's
  fallback), `results`.
- **`MetadataFieldDiff`** — `field`, `current` (`JsonElement?`), `incoming`
  (`JsonElement?`), `status`.
- **`MetadataFetchResult`** — `source_id`, `identity`, `url`, `attribution`,
  `fields`.

### Why `current` and `incoming` are `JsonElement?`

They are polymorphic by field: a string (`description`), an int (`year`), a list
of strings (`genres`, `tags`), a list of `{name, url}` (`publishers`), or a list
of `{label, url}` (`urls`). One row's type is decided by its own `field` value.

This is abs-cli's settled convention — type the envelope, drop to `JsonElement`
at the polymorphic leaf, as `SearchResult.Book` and `TaskItem.Data` do. abs-cli's
one raw pass-through, `metadata search`, is where ABS returns no fixed envelope
at all; all three responses here have server-fixed envelopes, so all three are
typed.

`ConsoleOutput.WriteJson` re-emits a `JsonElement` verbatim, so stdout is the
server's own JSON either way. The typing buys the response shape in `--help` and
an AOT-registered type to test against.

### The response-example walker needs a `JsonElement` case

`SampleJsonWalker.WriteValue` (`tools/GenerateResponseExamples/`) has no case for
`JsonElement`: it would fall through to the object branch and render
`JsonElement`'s own properties — `ValueKind` and friends — as if they were the
response. One case beside the `string` case, emitting `"<any>"`, next to the
existing date/time guard that fails loudly for the same class of mistake.

`<any>` is the honest placeholder. The alternative abs-cli uses — a
help-shape wrapper type standing in for the real shape — cannot work here,
because there is no single real shape to stand in for.

## Help text

Terseness calibrated against `SystemsCommand.cs`. The systems and books wording
is identical apart from the fallback noun, since the endpoints are.

### Which shape blocks each command registers

| Command | Request shape | Response shape |
|---|---|---|
| `metadata-sources` | — | `AddResponseExample<MetadataSourceList>()` |
| `metadata-search` | — | `AddResponseExample<MetadataSearchResult>()` |
| `metadata-fetch` | — | `AddResponseExample<MetadataFetchResult>()` |

No request shapes: both bodies are two to four scalars composed from flags, per
the `library rescan` rule.

### Notes, per command

Verbatim, so the implementer writes no prose of their own. Two placeholders,
which are the only difference between the two sets: `{name}` is `name` for
systems and `title` for books, and `{res}` is `systems` or `books`.

**`metadata-sources`**

```
Add-ons that can answer for this resource. Empty until one is installed,
enabled and runnable (addons list) and targets this resource type — a
book source never appears here for a system.

supports_paste false means metadata-fetch --paste is a 400 for that
source; search for an identity instead.
```

**`metadata-search`**

```
Candidates only — identity, label, score, url. No field data; that is
metadata-fetch.

An omitted --query defaults to the {name}; query echoes back what was
actually searched. Pass the same value to metadata-fetch: search-backed
sources answer per query, not from a catalogue.

[] means the source matched nothing. 502 means it could not be reached
or returned junk.
```

**`metadata-fetch`**

```
Writes nothing. Reports, per field, what this resource has now and what
the source offers; apply what you want with {res} update.

Exactly one of --identity (from metadata-search) or --paste (a source
URL or bare ID, only where supports_paste is true).

status is only_incoming (empty here), differs, or same, sorted in that
order. A field the source has nothing for is omitted, so nothing is ever
proposed to be blanked. incoming for urls and character_builder_urls is
the union with the existing list, not a replacement.

502 is a source failure, 400 a configuration one.
```

### Flag descriptions

`--id` matches the existing `"System ID"` / `"Book ID"`. `--source-id` is
`"Source add-on ID, from metadata-sources"`. `--query` is
`"Search text; defaults to the {name}"`. `--identity` is
`"Candidate identity, from metadata-search"`. `--paste` is
`"Source URL or bare ID, instead of --identity"`.

## Exit codes

Nothing new: 0, 1 on the fetch validator's parse error, 2 on any HTTP error
including the 502s. No bulk shape, so no exit 3.

A 502 is an ordinary outcome here — a third-party source is down — but it is
still an HTTP error, and exit 2 with the server's message on stderr is what
every other failing request does.

## Testing

### The fixture add-on grows a real source

`docker/addon-index/fixture-source.yml` today points `source.url` at
`https://example.test/…`, unreachable by design: install, update, upgrade and
uninstall never consult a source. Its own comment says the metadata work is what
exercises `source`/`search`/`map`, and this is that work.

The `addon-index` nginx service already serves `docker/addon-index/` on the
compose network. A second static file beside `index.json` is therefore the whole
of the new infrastructure:

- **`docker/addon-index/catalogue.json`** — a top-level array of two records,
  checked in and hand-written. Not generated: unlike `index.json` it carries no
  digest, so `make-addon-index.py` has no reason to touch it.
- **`fixture-source.yml`** gains `source.url:
  http://addon-index/catalogue.json`, a `search.identity` / `search.label` /
  `search.url`, an `identity_pattern` (so `supports_paste` is true and `--paste`
  is testable), and a `map` block.

Editing the manifest means re-running `docker/seed.sh`, which regenerates the
index digest. Editing `catalogue.json` alone does not — it is not digested.

The `map` block targets three fields chosen so each status is deterministic
against `Shadowrun 4 DE`:

| Field | Fixture value | Resource value | Status |
|---|---|---|---|
| `system_family` | `Shadowrun` | empty | `only_incoming` |
| `description` | `smoke fixture description` | written by the systems section | `same` |
| `parent_system` | `Shadowrun (fixture)` | folder-derived `Shadowrun` | `differs` |

`system_family` is guaranteed empty by an existing assertion — the systems
section checks that `--family Shadowrun` matches 2 and *not* this system, and its
comment forbids writing that field. Fetch writes nothing, so mapping it here
cannot disturb that. `description` is `same` only because the systems section
ran first, which fixes the ordering below.

### Smoke test

A metadata section placed after the `addons install` step and **before**
`addons update --enabled false` — a disabled add-on is not runnable and drops
out of `metadata-sources`. It also depends on the systems section having run,
for the `description` row.

Assertions:

- `systems metadata-sources --id $SR4` lists `fixture-source` with
  `supports_paste: true`.
- `books metadata-sources` on the same run returns `sources: []` — the fixture
  targets `game-system`, which is what proves target filtering rather than
  emptiness.
- `systems metadata-search` with an explicit `--query` returns the fixture
  record first; omitting `--query` echoes the system's name back as `query`.
- `systems metadata-fetch --identity` returns one row of each status, in the
  documented order.
- `systems metadata-fetch --paste <fixture url>` returns the same `identity` as
  the search hit did.
- `systems metadata-fetch` with neither `--identity` nor `--paste`, and with
  both, exits 1 without a request.
- `systems get --id $SR4` after the fetch still reports `system_family` empty —
  the assertion that fetch wrote nothing.

Every call is a read, so the section is idempotent by construction.

### Unit tests

- `Models/` — the six DTOs, including a `MetadataFieldDiff` round-trip over each
  of the value shapes (string, int, list of strings, list of objects) proving
  `JsonElement` re-emits verbatim.
- `Commands/` — role tags on all six, the shape blocks, and the fetch validator
  rejecting neither-flag and both-flags.
- The response-example generator's output is checked in, so the walker's
  `JsonElement` case is covered by `--verify-no-changes` on regeneration.

## Docs

- README Commands table — six rows.
- `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate.
- `docs/grimoire-api-notes.md` — a metadata-lookup section for what the live
  runs verify: the union semantics on link lists, the omission of empty incoming
  values, and the 502/400 split.
- `docs/roadmap.md` — item 1 leaves when this ships. Items 2–4 renumber.
- `CHANGELOG.md` — untouched; it belongs to the release process.

## Risks

**`description` as the `same` row couples two smoke-test sections.** If the
systems section stops writing that field, the metadata assertion turns into a
`differs` and fails for an unrelated reason. The failure message must name the
dependency rather than just reporting the wrong status.

**`parent_system` is folder-derived.** The `differs` row assumes it stays
non-empty. That is a property of the fixture library tree, not of a PATCH, so a
library reshuffle could silently turn the row into `only_incoming` — the same
class of coupling, and it fails loudly for the same reason.

**Two of the six commands cannot be exercised against a real source in CI.**
The fixture answers from a static file on the compose network, so the search
ranking and the fetch mapping are exercised, but no real add-on is. That is
deliberate — the alternative is scraping a third party's site on every PR — and
the honest position is that the community add-ons are covered by reading their
manifests, not by running them.
