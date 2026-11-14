# Linked books, folder access rules and media index

Wraps three groups of routes added in Grimoire 1.11.0, as one increment. Every
command is a thin pass-through to one endpoint. Source cites are v1.11.0.

Out of scope: `GET /api/systems/{id}/factions`. It is wargame-only (empty for
a role-playing system, `routers/systems/__init__.py:112-120`), so it moves to
the armory round.

## Commands

| Command | Endpoint | Role tag |
|---|---|---|
| `maps index` | `GET /api/maps/index` | — |
| `tokens index` | `GET /api/tokens/index` | — |
| `models index` | `GET /api/models/index` | — |
| `audio index` | `GET /api/audio/index` | — |
| `files campaign-map-copies --path` | `GET /api/files/campaign-map-copies` | admin |
| `books links list --id` | `GET /api/books/{id}/systems` | — |
| `books links add --id --system-id` | `POST /api/books/{id}/systems` | gm or admin |
| `books links remove --id --system-id` | `DELETE /api/books/{id}/systems/{system_id}` | gm or admin |
| `files folder access list` | `GET /api/settings/folder-access` | admin |
| `files folder access set --path --level` | `PUT /api/settings/folder-access` | admin |
| `files folder access remove --path` | `DELETE /api/settings/folder-access` | admin |

Roles come from the handler dependencies:
- `routers/books/links.py:22,36,56`
- `routers/settings/folder_access.py:108,118,154`
- `routers/files/core.py:431`
- The index routes carry `require_not_guest`: `routers/maps/__init__.py:78-91` and the matching routes in tokens, models and audio.

## Media index

Each `index` command copies its group's `folder-counts`. It uses the same
`MediaBrowseOptions`, and `maps index` also takes `--map-type`. The handler
takes the same `media_browse_params` (`routers/maps/core.py:120-130`).

The response is `{entries: [{key, label, count, offset}]}`. Entries come back
only for `--sort name`, one per first letter with `#` for digits and symbols,
and for `--sort added_at`, one per month. Any other sort returns no entries
(`services/browse/media.py:276-333`). That is the one note the help carries.

## Map copies preview

`files campaign-map-copies --path <p>` lists the campaigns using a map at or
under `path`, and whether each would keep a copy if the map were deleted
(`routers/files/core.py:429-455`). `outcome` is `copy`, `too_large`,
`uploads_disabled`, `not_image` or `file_missing`
(`routers/files/_schemas.py:185-201`). The `files delete` help already
describes the copy itself, so this command adds no Notes beyond what its
response sample shows.

## Linked books: `books links`

A link shows one book on another system's shelf too: one file and one row,
listed in both places (`routers/books/__init__.py:89-101`). Every subcommand
returns the book's link list. The book's own system is never in it.

- **`list`** shows only the systems the caller can see
  (`routers/books/links.py:20-30`).
- **`add`** has the body `{system_id}`, taken from `--system-id`.
  - Linking twice is a no-op.
  - A 400 for:
    - a version of a book (link the main version instead, `links.py:41-43`)
    - the book's own system
    - a system across the `books/` / `armory/` divide (`services/book_links.py:136-150`)
- **`remove`** returns 404 when the book is not linked there. The book stays
  on its own shelf (`links.py:53-67`).

## Folder access rules: `files folder access`

A rule restricts every book under one folder to `gm` (GMs and admins) or
`admin`. A book resolves to the stricter of the rule and what its system or
category gives it, and the book's own `access_level` overrides both
(`routers/settings/__init__.py:53-63`).

- **`list`** returns `{rules: [{path, level, exists, books}]}`. `exists` is
  false once the folder has gone from disk outside the file manager.
- **`set --path --level gm|admin`** (`folder_access.py:116-149`):
  - `--level` is a choice option.
  - `--path` is relative to the library root and must name a folder *inside*
    `books/` or `armory/`, not the collection itself. Otherwise it is a 400
    (`services/folder_access.py:47-60`).
  - A missing folder is a 404.
  - The rule replaces any level the folder had.
  - The books it covers have their campaign shares demoted to GM-only.
- **`remove --path`** returns 404 when the folder has no rule. Campaign shares
  that the rule demoted stay GM-only (`folder_access.py:152-166`).

Help must carry these:
- the demotion, and that `remove` does not undo it
- the inside-`books/`/`armory/` rule
- the rule following its folder on `files move`

## Cross-cutting

- One file per command group and one service per group, as the house rule
  requires:
  - the index methods go in `MapsService`, `TokensService`, `ModelsService`
    and `AudioService`
  - map copies go in `FilesService`
  - links go in a new `BookLinksCommands.cs` and `BookLinksService.cs`
  - folder access goes in a new `FolderAccessCommands.cs` and
    `FolderAccessService.cs`, wired under `files folder`
- `permissionHint` mirrors each role tag.
- In the same change:
  - the README Commands table
  - `IMPLEMENTED` in `tools/generate-api-coverage.py`, and the regenerated
    `docs/grimoire-api-coverage.md`
  - `docs/grimoire-api-notes.md` entries for the behaviour above
- **The version floor stays at 1.10.1.** On an older server these commands
  get a 404. They are not a dropped field that answers 200, which is the case
  that forces a floor.

## Testing

- **Unit tests**, in the existing per-command style: the role tag, the URL
  and query string, the request body, and the `--level` choices.
- **Smoke test**, kept idempotent and limited to `Shadowrun 4 DE`:
  - link one of its books onto another fixture system, assert the link list
    holds it, then remove the link and assert it is gone
  - set a `gm` rule on its folder, assert `list` shows it, then remove it
  - one read of `maps index --sort name`

Each write is undone in the same run, so a re-run converges. Removing the
rule does not undo the share demotion, though, so the smoke test must first
confirm that no seeded campaign shares a `Shadowrun 4 DE` book. If one does,
restrict a subfolder that holds no shared book instead.
