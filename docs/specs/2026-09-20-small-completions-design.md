# small completions — design

Closes the three leftovers in [#44](https://github.com/thomaslazar/grimoire-cli/issues/44):
the library statistics call, the one missing `systems cover` verb, and the binary
getters the collection layers shipped without.

Every server-behaviour claim below cites `temp/grimoire` at `v1.7.1`, the tag the
local stack's image is pinned to, or an observation against that running stack.

## Commands

| Command | Endpoint | Perm | Output |
|---|---|---|---|
| `library stats` | `GET /api/stats` | — | JSON |
| `systems cover from-source --id --source-type --source-id` | `POST /api/systems/{id}/cover/from-source` | gm or admin | JSON |
| `maps file --id --output` | `GET /api/maps/{id}/file` | — | stream |
| `maps page --id --page <n> [--width <px>] --output` | `GET /api/maps/{id}/page/{n}` | — | stream |
| `maps vtt image --id --output` | `GET /api/maps/{id}/vtt/image` | — | stream |
| `maps vtt data --id` | `GET /api/maps/{id}/vtt/data` | — | JSON |
| `maps vtt export --id --output` | `GET /api/maps/{id}/export.uvtt` | — | stream |
| `audio file --id --output` | `GET /api/audio/{id}/file` | — | stream |
| `tokens file --id --output` | `GET /api/tokens/{id}/file` | — | stream |
| `models file --id --output` | `GET /api/models/{id}/file` | — | stream |

The four `file` downloads were listed in #44 and expected to land with the
per-item layers (#38–#41); they did not, leaving each collection one command
short of its own asset.

`maps vtt` is a subgroup, as `systems cover` is. It groups the two `/vtt/` routes
with the `.uvtt` export, and leaves the obvious home for `vtt/authoring`
(GET/PUT), which is a separate feature and not in this change.

## Verified server behaviour

- **`GET /api/stats` has two size fields that are not the same number.**
  `total_size_mb` is books only; `library_size_mb` adds maps, tokens, audio and
  models (`routers/library/_schemas.py:77-79`, `core.py:150`). Both keep the book
  portion access-scoped, so a restricted book's bytes stay hidden. Observed
  against the local stack: `{"game_systems":10,"books":18,"maps":3,"tokens":2,`
  `"audio":2,"models":3,"indexed_books":18,"total_pages":126,`
  `"total_size_mb":0.1,"library_size_mb":0.1}`.
- **`page/{n}` renders a PDF page to WebP**, `?width=` setting the target pixel
  width (default 1600, max 3000). An image map streams as-is and accepts page 1
  only (`routers/maps/__init__.py:85-88`).
- **`vtt/image` decodes the base64 battlemap** out of a `.uvtt`/`.dd2vtt`, 400 if
  the map is not one or carries no image (`__init__.py:89-99`).
- **`vtt/data` is JSON, not a download.** It is registered with
  `response_model=VttDataResponse` and returns the grid resolution and
  wall/portal/light counts with the image omitted (`__init__.py:115-126`). It is
  the one command here on the JSON path and takes no `--output`. Observed: 400
  `{"detail":"Not a Universal VTT map"}` against a PNG fixture map.
- **`export.uvtt` is a download whose payload happens to be JSON.** The handler
  returns a `Response` with attachment bytes dominated by a base64 WebP
  (`routers/maps/core.py:340-460`), so it belongs on the `--output` path. It 400s
  for PDF, video and archive maps, and for a raster already linked to a Universal
  VTT sibling. The result is cached on disk, keyed on mtime plus the authored
  geometry. Observed: **200 `application/octet-stream` on a plain PNG fixture
  map** — a raster export is the normal case, not an error case.
- **`systems cover from-source` copies bytes in exactly as an upload does**, so a
  folder `cover.*`/`folder.*` still wins over what it sets
  (`routers/systems/covers.py:161-181`). `source_type` is validated against
  `("map", "token", "book", "audio", "campaign_file")`
  (`services/image_source.py:32`), but `campaign_file` additionally requires a
  campaign id this endpoint never sends, so it always 400s here
  (`image_source.py:120-122`). The four usable types go in the help text; no
  client-side set mirrors the server's, which answers 400 with its own list.

## Implementation

Seven of the ten are the settled `--output` convention: a required flag, `-` for
stdout, a `SavedFile` receipt for a path, served through `SendStreamAsync` with
`BodyInputException` mapped to exit 1. They are applications of that convention,
not new design.

- `Commands/MapVttCommands.cs` is new, beside `CoverCommands.cs`, holding the
  `maps vtt` subgroup's three commands.
- `MapsCommand.cs` gains `file` and `page` and registers the subgroup;
  `AudioCommand.cs`, `TokensCommand.cs` and `ModelsCommand.cs` each gain `file`;
  `CoverCommands.cs` gains `from-source`; `LibraryCommand.cs` gains `stats`.
- `MapsService`, `AudioService`, `TokensService`, `ModelsService`,
  `SystemsService` and `LibraryService` gain one method each per command, matching
  their existing `ThumbnailAsync`/`CoverAsync` shape.

`systems cover from-source` carries `AddRoleRequired("gm or admin")` and a
`permissionHint` of `"the gm or admin role"`. Nothing else here carries a role:
every getter is guarded by `get_current_user`, and `GET /api/stats` likewise.

## Testing

- Service tests pin each new path, and the `page` route's `width` query
  parameter's wire name — the thing a client regeneration can silently move.
- Command tests cover the `--output` requirement on the seven streaming
  commands, the `maps vtt` subgroup's shape, and that `maps vtt data` registers
  **no** `--output` and no `SavedFile` response example.

Smoke, against the fixtures the seeded stack actually has (3 maps, 2 tokens, 3
models, 2 audio, confirmed live):

- `library stats` returns the ten documented keys, and `total_size_mb` is not
  greater than `library_size_mb`.
- `maps file`, `tokens file`, `models file` and `audio file` each download to a
  path and report a non-zero byte count.
- `maps page --page 1` renders; `maps vtt export` succeeds on a raster map.
- `maps vtt data` and `maps vtt image` 400 on a non-UVTT map — the fixture
  library has no `.uvtt`, so the refusal is what is assertable, and it is a real
  assertion rather than a placeholder.
- `systems cover from-source` sets a system's cover from a fixture book, then
  restores the prior state: `systems cover delete` if the system had no uploaded
  cover before, so the block converges.

**Ships without live success coverage:** `maps vtt image` and the success path of
`maps vtt data`, for want of a `.uvtt` fixture. Both are exercised on their
refusal path. Adding a Universal VTT fixture is out of scope here.

## To verify against the local stack, not assume

- Which fixture map, if any, `maps page --page 2` can be run against — image maps
  accept page 1 only, so a multi-page assertion needs a PDF map and the fixtures
  may have none.
- That `systems cover from-source` accepts a `book` source id from the fixtures,
  and what the response body looks like.

## Docs in the same PR

Ten README Commands rows; ten `IMPLEMENTED` entries in
`tools/generate-api-coverage.py`, then regenerate; `docs/grimoire-api-notes.md`
for the two size fields, the `vtt/data`-is-JSON finding and the `campaign_file`
source type being unreachable from this endpoint.

## Not doing

- **Audio covers** (`get`, `upload`, `delete`, `from-source`). They are a cover
  management layer rather than a getter, and #41 shipped without them; they want
  their own issue.
- **`vtt/authoring`** (GET/PUT). A feature, not a leftover.
- Any client-side validation of `--source-type`, or any pre-check that a map is a
  Universal VTT before calling. The server answers both.
