# Media folder counts — design

**Targets:** Grimoire **1.8.0**, the current floor. Read from `temp/grimoire` at
the `v1.8.0` tag and probed against the local 1.8.0 stack on 2026-10-09.

## Purpose

Expose `GET /api/{maps,tokens,audio,models}/groups` as
`<collection> folder-counts`: every folder holding items that match the list
filters, with how many.

The use case is folder-level tag hygiene at scale. `maps folder-counts --tags
'["__grim:none__"]'` names each folder holding untagged maps in one call, and
the agent tags the folder with `maps folders set`, which reaches everything
beneath it. Without the endpoint the agent pages the whole filtered list and
aggregates folder paths itself, which on a token library of hundreds of
thousands of files is thousands of calls. Libraries are expected to grow in
that direction.

## Server behaviour

Verified in source and live:

- **Routes.** `list_map_groups` (`routers/maps/core.py:101-111`),
  `list_token_groups` (`routers/tokens/core.py:72-82`), `list_audio_groups`
  (`routers/audio/core.py:82-90`), `list_model_groups`
  (`routers/models/core.py:77-87`). All four are `require_not_guest`.
- **Parameters** are the list's own, from `media_browse_params`
  (`routers/_browse.py`): `q`, `tags`, `favorites`, `added_since`, `folder`,
  `sort`, `order`; maps also takes `map_type`. No `limit`/`offset`: the
  endpoint does not page.
- **Response** `{total, groups: [{path, count}]}` (`FolderGroupsResponse`,
  `routers/_browse.py`), built by `MediaBrowser.groups`
  (`services/browse/media.py:215-234`):
  - `count` covers items **directly** in that folder, not its subfolders.
    Live fixture: `battlemaps` = 2, `battlemaps/caves` = 1, although
    `battlemaps` holds three maps beneath it.
  - `path` is collection-relative (`battlemaps/caves`, not
    `maps/battlemaps/caves`); `""` holds the collection root's items.
  - Only folders with at least one matching item appear.
  - Groups are sorted by path in Python. `sort` and `order` are validated (an
    unknown value is a 422, `routers/_browse.py:63`) but change nothing.
  - `folder` filters like any other filter, so it narrows the result to that
    one folder's row.

Not to be confused with `tags items`' `folders[].count`, which counts the whole
subtree under a tagged folder.

## Naming

`folder-counts`, a sibling of `list`. The server's word, `groups`, names the web
gallery's "grouped" view mode (`frontend/src/hooks/useMediaGallery.js:43-58`),
which a CLI caller has never seen, and the CLI already uses `groups` for
`duplicates groups`. Nesting under `<collection> folders` would mix it into a
group backed by the folder-tag endpoints (`/map-folders` etc.). The hyphenated
sibling follows `tags items` / `tags folder-items`.

## Commands

`maps folder-counts`, `tokens folder-counts`, `audio folder-counts`,
`models folder-counts`, each registered directly after `list` in its own
`*Command.cs`.

**Flags mirror the server exactly.** Every flag `MediaBrowseOptions.AddTo`
adds — `--query`, `--tags`, `--favorites`, `--added-since`, `--folder`,
`--sort`, `--order` — plus `--map-type` on maps. `--sort`/`--order` are kept
although they currently change nothing: the CLI passes the server's parameters
through rather than deciding which of them matter, and the server may honour
them later. No role tag.

**Help.** The shared Filters, Query syntax and Tag filter sections come from
`AddTo`. Each command's own Notes:

- each count covers items directly in that folder, not its subfolders
- `path` is relative to the collection root; `""` is the root
- groups come back in path order; `--sort` and `--order` change nothing today

The shared Filters section's `--folder` line gains the second producer of a
folder value: "folder_path from `<collection> get`, or path from
`<collection> folder-counts`". That cross-reference is consumer → producer.

Examples:

```
grimoire-cli maps folder-counts
grimoire-cli maps folder-counts --tags '["__grim:none__"]'
```

Response sample: `AddResponseExample<Generated.Models.FolderGroupsResponse>()`.

## Services

Each of `MapsService`, `TokensService`, `AudioService`, `ModelsService` gains:

- `FolderCountsRequest(...)` returning the `RequestInformation` for
  `_client.Api.Api.{Collection}.Groups`, setting `Q`, `Tags`, `Favorites`,
  `AddedSince`, `Folder`, `Sort` and `OrderAsGetOrderQueryParameterType` from
  the `MediaBrowseFilter` record (and `MapType` on maps), the same way
  `ListAsync` does;
- `FolderCountsAsync(...)`, which sends it and returns the raw body.

`MediaBrowseOptions` and `MediaBrowseFilter` are reused unchanged apart from
the `--folder` help line.

## Testing

**Unit tests**

- Each collection's subcommand-order test lists `folder-counts` after `list`.
- `maps folder-counts` parses with `--map-type` and the shared flags; each
  `folder-counts` rejects an unknown `--sort` and `--order`.
- Each `folder-counts` help contains the direct-contents caveat ("not its
  subfolders").
- A service test pins each `FolderCountsRequest` to its own
  `/api/{collection}/groups` path, following `VocabularyServiceTests`, so a
  copied service cannot keep the maps builder.

**Smoke test** (read-only, so a re-run converges)

- `maps folder-counts` returns `battlemaps` with count 2 and
  `battlemaps/caves` with count 1.
- `tokens`, `audio` and `models folder-counts` each exit 0 with a non-empty
  `groups`.

## Docs

- README Commands table: four rows.
- `tools/generate-api-coverage.py` `IMPLEMENTED`: the four `/groups` routes;
  regenerate `docs/grimoire-api-coverage.md`.
- `docs/grimoire-api-notes.md`: a bullet under Maps (covering all four
  collections) on direct counts, collection-relative paths, matching folders
  only, and `sort`/`order` accepted but inert.

## Out of scope

- The system shelf routes (`/api/systems/{id}/books`, `/book-groups`,
  `/book-facets`) — the next item, brainstormed separately.
- No floor change: the routes are new in 1.8.0, already the floor.
