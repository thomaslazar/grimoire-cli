# tags writes — design

Completes the `tags` group with the four write endpoints
([#42](https://github.com/thomaslazar/grimoire-cli/issues/42)). `tags list` and
`tags items` ship; nothing can yet create, rename, delete or merge a tag.

Every server-behaviour claim below cites `temp/grimoire` at `v1.7.1`, the tag the
local stack's image is pinned to.

## Commands

| Command | Endpoint | Perm | Response |
|---|---|---|---|
| `tags create --value <v> [--display <d>]` | POST `/api/tags` | gm or admin | 201 `{internal, display, category}` |
| `tags rename --tag <k> --display <d>` | PATCH `/api/tags/{k}` | gm or admin | `{internal, display}` |
| `tags merge --tag <k> --into <k2>` | POST `/api/tags/{k}/merge` | gm or admin | `{internal, display}` — the survivor |
| `tags delete --tag <k>` | DELETE `/api/tags/{k}` | gm or admin | 204, no body |

`--tag` is the path key, matching the shipped `tags items --tag`. `create` has no
key to address, so its flags mirror the API body fields (`value`, `display`);
the other three take `--tag` plus the body's own field name.

All four are `Depends(require_gm_or_admin)`
(`routers/tags/core.py:177,190,208,239`) → `AddRoleRequired("gm or admin")` and
`permissionHint: "the gm or admin role"`.

## Verified server behaviour

The issue states that `PATCH` renames only the display value and that the
internal key does not change. **That is wrong as of 1.7.1** and the help text has
to say the opposite.

- **`rename` re-keys.** `rename_tag` (`services/tag_service/_admin.py:68`)
  normalizes the new display; when the key changes it rewrites folder
  `tags.json` entries onto the new key (`:107`) and, if another tag already owns
  that key, **merges this tag into it** and returns the survivor (`:73-75`,
  `:109`). A folder-only tag (no catalog row) is materialised into one first so
  the rename survives a rescan (`:77-93`).
- **`create` is idempotent** by internal key — it is `get_or_create_tag`
  (`core.py:183`). `display` defaults to `value`'s own trimmed casing
  (`_catalog.py:22`), so it only matters when the two must differ. Blank value
  400s (`core.py:185`).
- **`merge` creates its target** if absent (`core.py:219`), 400s on a blank
  target (`:220`) and on a self-merge (`:222`), and 404s when the *source* has no
  catalog row (`:213`) — so a folder-only tag cannot be merged, only renamed. It
  re-points `ResourceTag` rows and deletes duplicates (`:225-233`); folder tags
  are untouched, so a source tag carried by a folder reappears in `tags list`
  after the merge.
- **`delete` is 204** (`routers/tags/__init__.py:67`). It unlinks every resource
  and strips the tag from folder tags (`core.py:239-257`); a rescan may reapply
  it from `tags.json`. 404 only when neither a catalog row nor a folder
  association exists.
- **`/` and `\` are rejected** in `create`'s value, `rename`'s display and
  `merge`'s *target* (`_schemas.py`, `_catalog.py:40`). A merge *source* is
  deliberately unchecked: merging is the way out of a tag that already contains
  one.

## Implementation

`Commands/TagsCommand.cs` gains four subcommands; `Services/TagsService.cs` gains
four sends. No new types — the generated `TagCreate`, `TagDisplayUpdate` and
`TagMerge` models carry the bodies, and the existing `NotFoundHint` covers the
three keyed paths.

Help text carries the caveats above at the call site: `rename`'s re-keying and
silent merge, `merge`'s created target and left-behind folder carriers,
`delete`'s lack of undo and its rescan interaction, `create`'s idempotency. No
confirmation gate on `delete` — settled by `library cleanup-missing`; the warning
lives in the help text, where an agent reads it.

## Testing

- `TagsServiceTests`: the three request bodies and the four request paths.
- `TagsCommandTests`: the four role tags, the required flags, and the three
  caveats that must stay in the help text.
- Smoke test: create → rename → merge → delete a tag the script owns, so a
  re-run converges rather than drifting.

## To verify against the local stack, not assume

- `merge` on a folder-only tag returns 404, and a merged tag carried by a folder
  survives in `tags list`.
- A slashed internal key round-trips through Kiota's path encoding on all four
  commands — the route is `{internal:path}` precisely so such tags stay
  reachable (`routers/tags/__init__.py:22-31`).

## Docs in the same PR

README Commands table; `IMPLEMENTED` in `tools/generate-api-coverage.py`, then
regenerate; `docs/grimoire-api-notes.md` for the rename-re-keys and
merge-leaves-folder-tags findings.

## Not doing

- A `--yes` flag or confirmation prompt on `delete`.
- Any client-side existence check or post-write read — thin pass-through.
