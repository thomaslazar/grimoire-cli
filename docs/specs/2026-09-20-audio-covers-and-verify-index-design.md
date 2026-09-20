# audio covers and verify-index — design

Closes [#64](https://github.com/thomaslazar/grimoire-cli/issues/64) and the last
addons endpoint. Five commands.

Every server-behaviour claim below cites `temp/grimoire` at `v1.7.1`, the tag the
local stack's image is pinned to.

## Scope

| Command | Endpoint | Perm | Output |
|---|---|---|---|
| `audio cover get --id --output` | `GET /api/audio/{id}/cover` | gm or admin | stream |
| `audio cover upload --id --file` | `POST /api/audio/{id}/cover` | gm or admin | JSON |
| `audio cover delete --id` | `DELETE /api/audio/{id}/cover` | gm or admin | JSON |
| `audio cover from-source --id --source-type --source-id` | `POST /api/audio/{id}/cover/from-source` | gm or admin | JSON |
| `addons verify-index --url` | `GET /api/addons/verify-index` | — | JSON |

#64 also listed `systems cover from-source`. That shipped separately, and with
it the `source_type` vocabulary the issue wanted settled once for both call
sites — so only the audio half remains.

## Verified server behaviour

- **`cover get` is not `audio artwork`.** `serve_audio_cover`
  (`routers/audio/covers.py:149-165`) serves only the *deliberately set* cover
  and 404s with "No cover image" when there is none. The shipped `audio artwork`
  resolves the full precedence chain — set cover, else folder art, else embedded
  album art. The split is deliberate upstream, so an editor can tell "a cover was
  set here" apart from "the folder happens to have one".
- **`cover delete` can leave `has_artwork` true.** It clears the set cover, then
  recomputes the flag from folder art and embedded art
  (`covers.py:128-146`), so the track may still serve `audio artwork`
  afterwards — it just no longer has a chosen cover. Returns `{"status": "ok"}`.
- **`cover upload` checks content type, then size, then decodes.** Unsupported
  `content_type` 400s, over the ceiling 413s, an empty file 400s, and the bytes
  are validated as an image (`covers.py:91-109`). Returns `{"cover_image": …}`.
- **`cover from-source` mirrors the systems verb exactly.**
  `AudioCoverSourceIn.known_source` (`routers/audio/_schemas.py:100-118`) builds
  its allowed set as `tuple(t for t in SOURCE_TYPES if t != "campaign_file")` and
  raises — identical to `SystemCoverSourceIn` — so `map`, `token`, `book` and
  `audio` are accepted and `campaign_file` answers **422**.
- **`verify-index` normalizes both sides before comparing.**
  `is_trusted_index_url` (`addons/constants.py:42-49`) normalizes the given URL
  and every trusted URL, so a difference that is only normalization still
  verifies. It is guarded by `get_current_user` and carries no role. Returns
  `{url, verified, trusted_index_urls}`; an omitted `url` yields
  `verified: false` rather than an error.

## Implementation

`Commands/AudioCoverCommands.cs` is new, mirroring `Commands/CoverCommands.cs`,
and registers a `cover` subgroup under `audio`. `AudioService` gains four
methods, including **its own copy** of the `MimeForExtension` map that
`SystemsService` carries — per this repo's convention, near-identical per-group
code rather than a shared helper.

`AddonsCommand` gains `verify-index`; `AddonsService` gains one send.

Notable, and both learned from the immediately preceding work:

- **`cover from-source` passes no `notFoundHint`.** The route 404s from two
  independent places — the track lookup, and `load_source_image`'s per-source
  messages — and `GrimoireApiClient` replaces the server body whenever a hint is
  set, which would report a bad `--source-id` as a bad `--id`. `systems cover
  from-source` carries the same reasoning after the same mistake.
- **`verify-index` gets no client-side comparison.** The normalization above is
  server policy; mirroring it here is exactly what the thin pass-through rule
  forbids.

Response examples: `SavedFile` on `cover get`, `AudioCoverResponse` on `upload`
and `from-source`, the audio group's existing status shape on `delete`, and
`VerifyIndexResponse` on `verify-index`.

## Testing

- `AudioServiceTests` gains the four cover paths and the multipart body's part
  name; `AddonsServiceTests` gains the `url` query parameter's wire name.
- Command tests: the `cover` subgroup's shape, `--output` required on `get` and
  absent from the other three, the four role tags, `verify-index` carrying none,
  and the help stating the artwork-versus-cover distinction.

Smoke, against the two audio fixtures the seeded stack carries:

- `addons verify-index` on a URL from `addons list`'s `trusted_index_urls`
  reports `verified: true`; on a junk URL, `verified: false`.
- `audio cover get` 404s on a fixture track that has no set cover — checked
  first, so the write below is known to be writing into a clean state.
- `audio cover from-source` from a fixture book, then `cover get` succeeds, then
  `cover delete` restores the track to having no set cover.

The block pre-checks its fixture before writing and restores it afterwards, as
the previous block's cover smoke does, so a re-run converges.

## To verify against the local stack, not assume

- That a fixture audio track has no set cover to begin with, and what
  `has_artwork` reads before and after `cover delete` — the recomputation is the
  claim most likely to surprise.
- Whether `cover upload` is worth a smoke check at all: it needs an image file
  the script owns, and `from-source` already exercises the same storage path.

## Docs in the same PR

Five README Commands rows; five `IMPLEMENTED` entries in
`tools/generate-api-coverage.py`, then regenerate; `docs/grimoire-api-notes.md`
for the cover-versus-artwork split, the `has_artwork` recomputation, and the
normalization in `verify-index`.

## Not doing

- **`files folder delete`** (`DELETE /api/files/folder`). Not a gap: it calls the
  same `fs.delete_path` as the shipped `files delete`, always hard-deleting with
  the confirm-name guard, which is `files delete --delete-files --confirm-name`
  restricted to folders. A second command for it would add a way to get it wrong,
  not a capability.
- **`GET /api/health`, `GET /api/changelog`, `GET /api/latest-release`.**
  Informational reads about the server's own build rather than the library.
- **`maps vtt/authoring`** (GET/PUT). Undecided work, not a leftover.
