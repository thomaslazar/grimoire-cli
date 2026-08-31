# Controlled-vocabulary read commands

**Status:** approved
**Roadmap item:** MVP 1, *Vocabularies*, in [roadmap.md](../roadmap.md)
**Verified against:** `hunterreadca/grimoire:1.6.0`, source at tag `v1.6.0`
(`temp/grimoire/backend/routers/lookups/`)

## Problem

`GameSystemUpdate` accepts five fields whose values are drawn from controlled
vocabularies — `genres`, `license`, `parent_system`, `system_family` and
`dice_materials` — and `BookUpdate` accepts two of them. `systems update` and
`books update` have shipped, so those fields can be written today with nothing
in the CLI exposing what the existing values are. They are set by guesswork, and
a guess that misses does not fail.

Five read-only endpoints answer it, one per vocabulary. No infrastructure risk,
and they make commands that already shipped correct.

## Verified server behaviour

Read from `backend/routers/lookups/` at tag `v1.6.0`, cross-checked against the
generated client.

- **The five GETs take no parameters.** The generated request builders carry a
  single `token` query parameter, which is the spec's alternative auth scheme,
  not a filter. There is nothing to expose as a flag.
- **The reads carry no role dependency.** All five are
  `Depends(get_current_user)` — not `require_not_guest`. Per CLAUDE.md's role
  tagging rule that means **no** `AddRoleRequired` call. Only the POST and
  DELETE on each path are `require_admin`, and neither is in scope.
- **Systems and books store the vocabulary `name`, not the `id`.** Every usage
  count in `_helpers.py` matches on `name`, case-insensitively and with
  whitespace stripped (`_matches`). The `id` in a response addresses the
  vocabulary entry itself, which only the unimplemented `DELETE` needs.
- **`PATCH` validates nothing against the lookup tables.**
  `services/bulk_service.py:apply_updates` is a blind `setattr` loop over the
  payload; no lookup table is consulted on any write path. An unmatched string
  is stored as written and merely stops matching `systems list --genre` and the
  server's own usage counts. The five lists are therefore conventions to agree
  with, not enforced value sets.
- **`parent-systems` ships empty.** `models/lookup_defaults.py` seeds defaults
  for genres, system families, licenses and dice materials, but
  `DEFAULT_PARENT_SYSTEMS` is `()`. Since `parent_system` is also folder-derived
  for a container child, values in use and values in this vocabulary diverge
  freely.
- **Ordering is `sort_order` then `name`** on all five. `is_default false` marks
  an entry created through the API rather than seeded. Genres are a flat list
  with `parent_id` linking a child to its parent; dice materials carry a `group`
  that defaults to `"Custom"`.

## Command shape

Harvested from abs-cli, which settled it: `GenresCommand.cs`, `TagsCommand.cs`
and `NarratorsCommand.cs` are each a top-level group named after the vocabulary,
with `list` as a verb alongside `rename` and `delete`, registered flat in
`Program.cs`. ABS genres are free-text strings rather than a curated set, so the
*data* has no counterpart — but the *command shape* does, and it grows the way
this one will when the roadmap's later `lookups` writes land.

| Command | Path | Response model |
|---|---|---|
| `genres list` | `GET /api/genres` | `GenresResponse` |
| `licenses list` | `GET /api/licenses` | `LicensesResponse` |
| `parent-systems list` | `GET /api/parent-systems` | `ParentSystemsResponse` |
| `system-families list` | `GET /api/system-families` | `SystemFamiliesResponse` |
| `dice-materials list` | `GET /api/dice-materials` | `DiceMaterialsResponse` |

Each `list` declares `--server` and nothing else. It consumes a saved token, so
per CLAUDE.md the flag is declared per-subcommand and threaded into
`CommandHelper.BuildClient`, making the flag tier of `flags > env > file`
reachable. No `AddRoleRequired`.

## Components

**`src/GrimoireCli/Commands/LookupCommands.cs`** — one file. `Create()` returns
five `Command`s built from a table of (vocabulary name, summary, per-vocabulary
Notes line, response-example registrar). The five bodies differ only in path and
response model, so this follows `MetadataCommands.cs` — one builder,
the varying part as a parameter, named commands out — rather than abs-cli's
per-file split, which exists there because each group has three distinct verbs.

`Program.cs` adds the five in a loop.

**`src/GrimoireCli/Services/LookupsService.cs`** — `ListAsync(vocabulary)`
switches to the generated builder's `ToGetRequestInformation()` and calls
`_client.SendAsync(info)`. No `permissionHint`, because there is no role
dependency to name, and no `notFoundHint`, because no id appears in the path.

**No generator work.** All five response samples already exist in
`JsonExamples.g.cs` — `tools/GenerateJsonExamples` discovers every `IParsable`
in the model assembly, not only the ones a command references — and they render
cleanly, with the genre `parent_id` composed type resolving to `"<string>"`.
`JsonExamplesDriftTest` continues to hold them in sync.

## Help text

Two lines shared by all five, then one per vocabulary. The two shared lines are
the caveats an agent cannot recover from the response sample: it shows `id` and
`name` side by side without saying which one is written, and shows nothing at all
about validation.

```
Notes:
  Submit name, not id — systems and books store the name. id addresses the
  vocabulary entry itself.

  Nothing validates a written value against this list: an unmatched string is
  stored as written and stops matching systems list --genre.

  <per-vocabulary line>
```

Per-vocabulary lines:

- **genres** — `parent_id` links a child to its parent; ordered by `sort_order`,
  then `name`.
- **dice-materials** — `group` buckets the entry; defaults to `Custom`.
- **parent-systems** — ships empty: Grimoire seeds no defaults, and
  `parent_system` is folder-derived for a container child, so values in use need
  not appear here.
- **licenses**, **system-families** — `is_default false` is a custom entry.

No cross-reference from these commands to `systems update`. Cross-references are
one-way, consumer → producer, so the pointer lives on the consumer.

## Edits to shipped commands

- **`systems update`** Notes gains a line: values for `genres`, `license`,
  `parent_system`, `system_family` and `dice_materials` come from the five
  `… list` commands, unvalidated, so an unmatched string is stored as written.
- **`books update`** Notes gains the same for `genres` and `license` only.
  `BookUpdate` carries no `parent_system`, `system_family` or `dice_materials`.

Without these the new commands ship with nothing pointing at them, since the
producer → consumer direction is closed.

## Explicitly out of scope

- **`create` and `delete` per vocabulary.** Decided, and specified as
  **Vocabulary writes** under "Then" in [roadmap.md](../roadmap.md) — ten
  endpoints, all admin, and the whole set, since no vocabulary has a `PUT` or
  `PATCH`. They sit beside `list` on the groups this change establishes, so this
  spec's shape is what they land on; they need their own design for the
  `force=true` semantics, notably that a forced delete removes the vocabulary
  row without stripping the value from any system or book.
- **Client-side validation of `systems update` values against these lists.**
  That is client-side mirroring of server policy, which thin pass-through
  forbids. The server's own lack of validation makes it tempting and does not
  change the answer; the help text states the behaviour instead.
- **Client-side assembly of the genre tree.** The response is flat with
  `parent_id`; nesting it would be reading the response to synthesize derived
  data.

## Testing

**`tests/GrimoireCli.Tests/Commands/LookupCommandTests.cs`** — one `[Theory]`
over the five vocabularies:

- help renders Notes → Options → Examples in order and carries a Response shape
  section;
- help carries **no** role section. This is the negative assertion that matters:
  the route is an untagged one that is a verified decision rather than an
  accident, so a later reflexive `AddRoleRequired` fails here.
- `list` parses with no errors; `--server <url>` is accepted; an unknown
  subcommand errors.

**`tests/GrimoireCli.Tests/Services/LookupsServiceTests.cs`** — pins each
vocabulary to the request path its builder produces, so a client regeneration
that moves a builder fails loudly rather than silently reading the wrong
vocabulary.

**`docker/smoke-test.sh`** — one read-only block, idempotent by construction. All
five must exit 0 and emit valid JSON carrying the expected envelope key.
Non-empty is asserted for `genres`, `system-families`, `licenses` and
`dice-materials`; `parent-systems` is asserted present but permitted to be empty,
because `DEFAULT_PARENT_SYSTEMS` is `()` and a non-empty assertion would fail on
a fresh stack.

## Documentation

- **README Commands table** — five rows, no role suffix.
- **`tools/generate-api-coverage.py`** — five `IMPLEMENTED` entries, then
  regenerate `docs/grimoire-api-coverage.md` from the running stack. The
  markdown is generated; the script is what is edited.
- **`docs/grimoire-api-notes.md`** — one entry for the three findings above that
  the source alone is slow to reveal: storage and counting by name, the blind
  `setattr` on every write path, and the empty parent-systems default.
- **`docs/cli-design.md`** — add the five to the resource list, and record the
  deviation: five new top-level groups against that file's own "resource surface
  is short by design" line, taken from abs-cli's `genres`/`tags`/`narrators`
  rather than grouped under one umbrella noun.
- **`docs/roadmap.md`** — remove MVP item 1 at ship time. An item leaves the
  roadmap when it ships. Already done on this branch: item 1 is marked reads-only
  and its "abs-cli has no counterpart to harvest" claim corrected — true of the
  data, wrong about the command shape — and **Vocabulary writes** is promoted
  from a rough note under "Later" to a decided block under "Then".

## Delivery

Branch `feat/lookup-commands`. Spec, plan and code commit together on that
branch per CLAUDE.md, so design and delivery review as one unit. The four
pre-PR checks run against a booted, seeded local stack, then CI is watched to a
terminal state.
