# Global pins and the GM screen

Wraps two route families that are new in Grimoire 1.11.0, in one increment:
the user's global pins (3 routes) and the campaign GM screen (13 routes).
Every command passes straight through to one endpoint. Source cites refer to
v1.11.0, under `backend/`.

## Global pins: `pins`

This is a new top-level group, `PinsCommands.cs` with `PinsService.cs`. The
router depends on `require_not_guest` (`routers/pins/__init__.py:8`), so no
command takes a role tag.

| Command | Endpoint |
|---|---|
| `pins list [--collection]` | `GET /api/pins` |
| `pins add --collection --target` | `POST /api/pins` |
| `pins remove --pin-id` | `DELETE /api/pins/{pin_id}` |

- `--collection` is a choice of `systems`, `maps`, `tokens`, `audio` or
  `models`. It is optional on `list` and required on `add`
  (`routers/pins/_schemas.py:6`).
- The verbs are `add` and `remove`. A pin attaches something that already
  exists, and adding it again is idempotent.

The help must say:
- `--target` is a game system's id for `systems`. For the other collections it
  is a folder path relative to that collection. Leading and trailing slashes
  are stripped (`_schemas.py:25-34`).
- Adding something already pinned returns the existing pin. Each collection
  holds at most 100 pins, and adding past that is a 400
  (`routers/pins/core.py:43-75`).
- The list comes back oldest first. Another user's pin is a 404, the same as a
  missing one (`core.py:28-90`).

## GM screen: `campaigns gm-screen`

This is new under `campaigns`, split into two groups:
- `CampaignGmScreenCommands.cs` with `CampaignGmScreenService.cs` for the board
  routes
- `CampaignGmPanelsCommands.cs` with `CampaignGmPanelsService.cs` for the panel
  routes, registered as `campaigns gm-screen panels`

Every route depends on `get_current_user`, so no command takes a role tag. The
owner check happens inside the handler (`routers/campaigns/gm_screen.py:44-56`).

| Command | Endpoint |
|---|---|
| `campaigns gm-screen get --id` | `GET /api/campaigns/{id}/gm-screen` |
| `campaigns gm-screen party --id` | `GET …/gm-screen/party` |
| `campaigns gm-screen export --id` | `GET …/gm-screen/export` |
| `campaigns gm-screen sources --id` | `GET …/gm-screen/sources` |
| `campaigns gm-screen presets --id` | `GET …/gm-screen/presets` |
| `campaigns gm-screen reset --id --scope` | `POST …/gm-screen/reset` |
| `campaigns gm-screen layout --id {--input\|--stdin}` | `PUT …/gm-screen/layout` |
| `campaigns gm-screen import --id {--input\|--stdin} [--append]` | `POST …/gm-screen/import` |
| `campaigns gm-screen copy --id --source-id [--append]` | `POST …/gm-screen/copy` |
| `campaigns gm-screen apply-preset --id --preset-id [--index-url] [--append]` | `POST …/gm-screen/presets/apply` |
| `campaigns gm-screen panels create --id {--input\|--stdin}` | `POST …/gm-screen/panels` |
| `campaigns gm-screen panels update --id --panel-id {--input\|--stdin}` | `PATCH …/gm-screen/panels/{panel_id}` |
| `campaigns gm-screen panels delete --id --panel-id` | `DELETE …/gm-screen/panels/{panel_id}` |

### Request bodies

- **`reset`**: `--scope` is a choice of `scene`, `session` or `rest`. The body
  is `{scope}`.
- **`layout`**: the body is `{panels:[{id,x,y,w,h}]}`, read from `--input` or
  `--stdin` and validated against `GmLayoutUpdate`.
- **`import`**: `--input` or `--stdin` holds the board document that `export`
  produces. The CLI wraps it as `{document, replace}`. The document must be a
  JSON object; anything else is a CLI input error before any request is sent.
- **`copy`**: the body is `{source_campaign_id, replace}`.
- **`apply-preset`**: the body is `{preset_id, index_url?, replace}`.
- **`--append`** sends `replace: false`. Without it the CLI sends
  `replace: true`, the server default.
- **`panels create` and `panels update`**: the body is read from `--input` or
  `--stdin` and validated against `GmPanelCreate` / `GmPanelUpdate`. Both
  models list `config` and `state`.

### Help must carry

- **Every command:** only the campaign owner can use it; anyone else gets a 403
  (`gm_screen.py:44-56`).
- **Every write:** a 409 while the campaign is archived, and a 403 while the
  GM's campaign access is disabled (the router's `require_writable_campaign`,
  `_helpers.py:289-301`). Reuse the wiki commands' wording.
- **`reset`:** each panel clears whatever it is marked to clear on that scope.
  `session` also clears what clears with `scene` (`panels.py` `reset_state`,
  `scopes_cleared_by`).
- **`layout`:** an id that is not on the board is skipped
  (`gm_screen.py` `update_gm_layout`).
- **`import`, `copy` and `apply-preset`:**
  - By default they replace the board; `--append` adds the panels below it.
  - A board holds at most 60 panels, and going over is a 400 (`_add_board`).
  - A board that names an unknown kind, or has an empty `panels` list, is a 400
    (`boards.py` `parse_board`).
- **`copy`:**
  - It copies the source's setup, not its values: counters go back to their
    start, clocks are unticked, and initiative is empty. Notes are matched by
    page slug.
  - `--source-id` comes from `sources`.
  - A campaign you don't own, or the same campaign, is a 404, and an empty
    source board is a 400 (`copy_gm_screen`).
- **`presets` and `apply-preset`:**
  - Both give a 403 when the server disables external installs. Reuse the
    wording of `campaigns wiki templates catalogue`.
  - `--preset-id` and `--index-url` come from `presets`.
  - An unknown preset is a 404, and a failed download is a 502
    (`apply_gm_screen_preset`).
- **`panels update`:**
  - Only the fields given change.
  - `state` replaces the panel's state whole.
  - A new `config` re-cleans the state against it.
  - An unknown panel id is a 404.
- **`panels create` and `panels update` share a "Panel kinds" section**, in a
  `GmPanelHelp` helper like `WikiPageHelp`. Every line must match
  `services/gm_screen/panels.py`:

```
The grid is 12 columns; without x and y a panel takes the first gap it fits.
Unknown keys and bad values in config and state silently become defaults;
only an unknown kind is a 400. resets is scene, session, rest or "" (never).
A row's id is kept when given, so a later update can address it.

note       config page_id or page_slug: a page in this campaign (an unknown
           page_id is a 404)
party      config stats [{value, label, max, style number|pips}],
           include_inactive; value names a field from party's fields, max
           one too or a number
counter    config min, max, start, style number|pips, resets; state value
clocks     config mode countdown|clock; state items [{id, name, size,
           ticks, note, resets, loops}]
pools      config resets; state items [{id, name, tokens, note}]
scratch    state text
initiative config mode ordered|sides, sides [{key, label}], resets;
           state round, turn, side, first, entries [{id, name, score, side,
           acted, note, character_id}]
reference  config sections [{id, title, text}], text in markdown
table      config mode list|join, columns [{id, label, entries}];
           state rolls, one 1-based entry number per column
```

## Cross-cutting

- In the same change:
  - the README Commands table
  - `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate
    `docs/grimoire-api-coverage.md` (expected: 271 covered)
  - in `docs/grimoire-api-notes.md`, a "Pins" section and a "GM screen" section
    covering the behaviour above
- The version floor stays at 1.10.1. On older servers these routes return 404.

## Testing

- **Unit tests**, in the per-command style:
  - each command's URL and method
  - the `import` wrapping, with `replace` both ways
  - the `copy` and `apply-preset` bodies
  - the `--scope` and `--collection` choices
  - no role tags
  - the Panel kinds section rendered on `panels create` and `panels update`
- **Smoke test:** it stays idempotent and writes only to the local stack.
  - **Pins:**
    1. `pins add --collection systems --target $SR4` twice, and assert both
       calls return the same id.
    2. `pins list --collection systems` contains it.
    3. `pins remove`, then `list` no longer contains it.
  - **GM screen**, in the Smoke Wiki GM campaign:
    1. Delete every panel left over from an interrupted run, and expect an
       empty board.
    2. `panels create` a counter
       (`{"kind":"counter","config":{"min":0,"max":10,"start":2,"resets":"scene"}}`).
    3. `panels update` its state to `{"value":7}`.
    4. `reset --scope scene`, and assert the value is back at 2.
    5. `export`, then `import --append` the export, and assert 2 panels.
    6. Delete both panels, and expect an empty board.
  - `party`, `sources`, `presets`, `copy` and `apply-preset` are covered by
    unit tests only, so the smoke test needs no network or second board.
