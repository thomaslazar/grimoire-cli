# Wiki map pins

Wraps the three map-pin routes added in Grimoire 1.11.0, and documents the
map-page fields that `campaigns wiki create|update` already pass through. Each
command is a thin pass-through to one endpoint. Source cites are v1.11.0, under
`backend/routers/campaigns/`.

## Map-page fields

The fields need no new code. #138 regenerated `WikiPageCreate`/`WikiPageUpdate`
with `map_id` and `map_file_id`, and `wiki create|update` validate `--input`
against those models. `WikiPageHelp` already states that `page_type` may be
`map` and that the two fields apply only to a map page. It still lacks the
following rules (`_wiki_maps.py:63-119`):

- `map_id` must be an image map (png, jpg, jpeg, gif, webp, bmp) that is
  already linked to the campaign. Anything else is a 400: `Unknown map`,
  `A map page can only show an image map`, `Link the map to the campaign first`.
- `map_file_id` must be one of the campaign's own image files, or it is a 400.
- Setting one field clears the other. `""` clears a field. Pins are kept when
  the picture is replaced, because their coordinates are fractions of it.

`wiki get` returns `map` (`source` is `library`, `file`, `missing` or `none`)
and `pins` (`wiki.py:257-258`, `_wiki_maps.py:122-153`).

## Commands

These go in a new `CampaignWikiPinsCommands.cs` and `CampaignWikiPinsService.cs`,
registered under `campaigns wiki` as `pins`. None of them takes a role tag or a
`permissionHint`: every route depends on `get_current_user` (`wiki_pins.py:18`).

| Command | Endpoint |
|---|---|
| `campaigns wiki pins create --id --page-id --x --y` | `POST /api/campaigns/{id}/wiki/{page_id}/pins` |
| `campaigns wiki pins update --id --page-id --pin-id` | `PATCH /api/campaigns/{id}/wiki/{page_id}/pins/{pin_id}` |
| `campaigns wiki pins delete --id --page-id --pin-id` | `DELETE /api/campaigns/{id}/wiki/{page_id}/pins/{pin_id}` |

The body is a handful of scalars, so each field is a flag named after it, as
`campaigns categories` does:

- `--x` and `--y`: a double from 0 to 1, a fraction of the picture
  (`_schemas.py:232-259`). Both are required on create.
- `--label`
- `--target-page-id`
- `--target-title`
- `--icon`
- `--icon-color`
- `--visibility`: a choice option, `page` or `gm`

On update, an unset flag is omitted from the body.

There is no `list`, because the server has none. The pins come back in
`campaigns wiki get` under `pins`.

The verbs follow the house pattern: `create`/`update`/`delete` for a record that
gets its own id, as a pin does. `add`/`remove` is for attaching something that
already exists.

## Behaviour the help must carry

Shared by all three commands (`wiki_pins.py:33-50,89-93`):

- Anyone who can edit the map page can manage its pins. Anyone else gets a 403.
- Only the campaign GM makes a pin `gm` or turns a `gm` pin back. Anyone else
  gets a 403.
- A pin the caller can't see is a 404. That is a `gm` pin when the caller is
  not the GM, or a pin linking a page the caller can't read.
- A page that is not a map page is a 400.

On `create` and `update` (`_wiki_maps.py:288-334`):

- `--target-title` links the visible page with that title. If there is none it
  creates a stub with the map page's visibility, the same as a `[[Title]]` link
  does.
- `--target-page-id` must be a page the caller can see, and not the map page
  itself, or it is a 400.

On `update` only (`wiki_pins.py:110-125`):

- `--target-title` wins over `--target-page-id`.
- `--target-page-id ""` unlinks the pin.
- `""` clears `--icon` and `--icon-color`.

## Cross-cutting

- In the same change:
  - the README Commands table
  - `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate
    `docs/grimoire-api-coverage.md`
  - a `docs/grimoire-api-notes.md` section on map pages and pins, covering the
    behaviour above
- The version floor stays at 1.10.1. On an older server these commands get a
  404. They do not drop a field and still answer 200, which is the case that
  forces a floor.

## Testing

- Unit tests in the per-command style:
  - the URLs and methods
  - the create body with only `x` and `y`
  - an update body holding only the flags that were set, with `""` passed
    through
  - the `--visibility` choices
- Smoke test, inside the existing `campaign wiki` block on the Smoke Wiki GM
  campaign. That block clears the wiki first and deletes everything it creates,
  so a re-run converges:
  1. create a map page with no picture (`{"title":"Smoke Map","page_type":"map"}`)
  2. `pins create --x 0.25 --y 0.5 --label Gate`
  3. `pins update --x 0.75 --visibility gm`
  4. `wiki get`: assert the page has one pin, at `x` 0.75 and `y` 0.5, with
     visibility `gm`
  5. `pins delete`, then `wiki get` shows no pins
  6. delete the page
