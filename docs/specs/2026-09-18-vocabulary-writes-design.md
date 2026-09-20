# vocabulary writes — design

Completes the five lookup vocabularies with `create` and `delete`
([#43](https://github.com/thomaslazar/grimoire-cli/issues/43)). The reads ship —
`genres list`, `licenses list`, `parent-systems list`, `system-families list`,
`dice-materials list` — and nothing can add to or remove from any of them.

Every server-behaviour claim below cites `temp/grimoire` at `v1.7.1`, the tag the
local stack's image is pinned to.

## Commands

Ten commands, two on each existing group. All are `require_admin`
(`routers/lookups/core.py`), so each carries `AddRoleRequired("admin")` and a
`permissionHint` of `"the admin role"`.

| Group | create | delete |
|---|---|---|
| `genres` | `--name <n>` `[--parent-id <id>]` | `--id <id>` `[--force]` |
| `system-families` | `--name <n>` | `--id <id>` `[--force]` |
| `parent-systems` | `--name <n>` | `--id <id>` `[--force]` |
| `licenses` | `--name <n>` | `--id <id>` `[--force]` |
| `dice-materials` | `--name <n>` `[--group <g>]` | `--id <id>` `[--force]` |

Flags mirror the API's own body and query field names. `--id` is the entry's
`id`, the one field the reads expose that nothing else uses.

## Verified server behaviour

- **`create` 409s on a case-insensitive duplicate** (`core.py:55-57` and the four
  siblings, each matching with `ilike`). A blank name is rejected by the schema
  (`_schemas.py`, `name_not_blank` on all five). The new entry is always
  `is_default: false`, and its `sort_order` is the row count at insert time.
- **`genres create` 404s on an unknown `--parent-id`** (`core.py:58-61`). It is
  the only 404 that command has, so its hint can name the parent specifically.
- **`dice-materials create --group` defaults to `"Custom"`**, and a
  whitespace-only group falls back to it too (`core.py:278`).
- **`delete` 409s while the value is in use**, unless `--force`. The 409 body
  carries `usage_count` and `name` alongside the message (`core.py:81-89`).
  "In use" is counted by name, case-insensitively, over the systems and books
  that carry it (`_helpers.py:71-127`).
- **A forced delete strips nothing.** It removes the vocabulary row only; every
  system and book keeps the value, because it is stored as a plain string rather
  than a foreign key. The success body's `removed_usage` is that count — what was
  left behind, not what was cleaned up. Genre children *are* cascaded away
  (`core.py:90-93`).
- **Built-in entries are deletable and not restorable.** No delete handler checks
  `is_default` (`core.py:71-93` and the four siblings), and the defaults are
  seeded by one-time migrations (`migrations/versions/0004_expand_metadata.py`,
  `0006_parent_system_licenses.py`) rather than re-seeded on boot. `create` gives
  the name back only as a new id with `is_default: false`.

## Implementation

Each group keeps its own command file and its own service — the settled
convention here, even though the four flat vocabularies are near-identical. No
shared builder, no table-driven command factory.

Each service gains `CreateAsync`/`DeleteAsync` plus `internal RequestInformation
CreateRequest(...)`/`DeleteRequest(...)`, matching the existing `ListRequest()`
pattern: internal so a test can pin the path, the body and the `force` query
parameter's wire name, which is what a client regeneration could silently move.

`GenreCreate.ParentId` and `DiceMaterialCreate.Group` are composed-type wrappers
whose constructors set nothing — the same quirk as `TagCreate.Display`. Assign
through the wrapper only when the flag was given, so an omitted one stays absent
from the body rather than sending null and overriding the server's default.

Response examples: `GenreOut` on `genres create`, `DiceMaterialOut` on
`dice-materials create`, `LookupOut` on the other three, `LookupDeleteResponse`
on all five deletes.

Not-found hints: each delete names its group's `list`. `genres create` gets one
naming `--parent-id`.

Help text carries, on `delete` only: the 409-while-in-use and what its body
holds, the `removed_usage` caveat with the genre-children exception, and the
deletable-defaults warning. `create` carries the 409-on-duplicate, and on
`genres` the parent 404. No client-side guard on either — server policy is passed
through.

Shipped deviates from this in two places, both kept as-is: `genres create`'s
Notes don't carry the parent 404 — `--parent-id`'s own description already
points at `genres list`, and the service names a not-found hint at runtime, so
the Notes line would only repeat it. And every `create`'s Notes carry an
enforcement caveat ("Creating a value does not make it enforced...") this
paragraph never mentioned.

## Testing

The two existing table-driven files cover all five groups and are extended
rather than replaced:

- `VocabularyServiceTests` gains create/delete path, body and `force`
  wire-name assertions per vocabulary.
- `VocabularyCommandTests` gains the role tag, required flags and the delete
  caveats. Two of its existing assertions must change: the one pinning each
  group to exactly one `list` subcommand, and `AnUnknownSubcommandErrors`, which
  currently uses `create --name x` as its example of an unknown subcommand.

Smoke, per vocabulary and converging on a re-run: create → a second create with
different casing 409s → delete returns `removed_usage: 0`. Plus one
genre-specific case: create a parent, create a child under it, delete the
parent, assert the child is gone. Nothing touches a built-in entry or any
fixture's metadata.

## To verify against the local stack, not assume

- A forced delete really leaves the string on a system that carries it — the
  `removed_usage` claim, tested on a value this smoke run applied itself, never
  on fixture metadata it did not write. Outcome: `removed_usage` came back `1`
  against a `usage_count` of `1`, and the system kept the license string.
- Deleting an entry with `is_default: true` is permitted — cannot be checked
  live: `create` always returns `is_default: false`, and the constraints rule
  out touching a real built-in, so no throwaway entry with `is_default: true`
  can exist to delete. What was confirmed live is the other half: a
  non-default entry deletes cleanly. The built-in case rests on reading the
  server source (no delete handler checks `is_default`; defaults are seeded by
  one-time migrations, not re-seeded on boot).

## Docs in the same PR

Ten README Commands rows; ten `IMPLEMENTED` entries in
`tools/generate-api-coverage.py`, then regenerate; `docs/grimoire-api-notes.md`
for the deletable-defaults finding and the `removed_usage` semantics.

## Not doing

- Any rename. The API has no `PUT` or `PATCH` on any vocabulary, so `abs-cli`'s
  `genres rename` has no counterpart to port.
- Any client-side validation of a value against a vocabulary. No server write
  path does it either: an unmatched string is stored as written and merely stops
  matching `?genre=` and the server's own usage counts.
