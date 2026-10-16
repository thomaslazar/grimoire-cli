# Campaign file download and the last campaign gaps

After #103–#111 the campaign area has one real gap: a file uploaded into a campaign
cannot be downloaded again. This spec adds that command. It also records why
three other campaign routes stay unwrapped, so the coverage table shows a
decision rather than a hole.

Server facts are read at `v1.8.0`, `backend/routers/campaigns/uploads.py`
(`:line`) and `resource_search.py` (`rs:line`), and probed on the local stack.

## `campaigns files get --id <campaign-id> --file-id <file-id> --output <path|->`

`GET /api/campaigns/{id}/files/{file_id}` (`get_campaign_file`, :724).

- **Where `--file-id` comes from.** It is the `resource_id` that `files upload`
  and `files upload-image` return, and the `resource_id` of a `file` row in
  `campaigns resources list`.
- **Who can read a file** (`can_read_campaign_file`, :695):
  - Any campaign member, subject to the visibility of the resource that links
    the file.
  - The owner reads every file.
  - Anyone else reads a `public` file, or a `private` one shared with them.
  - A `gm` or unlinked file is 403 for them.
  - *Probed:* the player got 403 while the file was `gm` and 200 once it was
    `public`.
- **Responses.**
  - An unknown file is 404 "File not found" (*probed*).
  - The server sends the original filename and MIME type (*probed*:
    `image/png`, `attachment; filename="fixture-cover.png"`). The CLI writes
    the bytes as-is.
  - `--output -` writes the bytes to stdout. A path writes the file and prints
    `{path, bytes}`, as `banner get` does.
- **Code.**
  - The command goes in `CampaignFilesCommands.cs` and the method in
    `CampaignFilesService.cs`, which already hold `upload` and `upload-image`.
  - The method returns a `Stream` via `SendStreamAsync`.
  - It passes no not-found hint: a 404 is either the campaign or the file, and
    only the server's detail tells the two apart.
  - No role tag and no permission hint.

## Not wrapped, noted in `IMPLEMENTED`

The coverage generator renders a non-✅ value verbatim and counts it as neither
covered nor internal, the same as the calendar feeds.

- **`GET /api/campaigns/resources/search`** (rs:79): "not wrapped: `search`
  finds the same items; their ids are what `resources add` takes".
  - It differs only in matching folder paths first, and in returning rows in the
    `resources add` shape.
- **`GET /api/campaigns/resources/suggested/{system_id}`** (rs:241): "not
  wrapped: `systems books list --category core` gives the same set".
  - Its `suggested` flag is `category == "core"` (rs:265). *Probed:* the D&D
    system's suggested set equals `systems books list --category core`.
  - It applies no library-access filtering, which `systems books list` does.
- **The 8 `/api/campaigns/{campaign_id}/sessions…` routes**: "not wrapped:
  legacy; rolled into session wiki pages on startup".
  - See `docs/grimoire-api-notes.md`, "Session notes are legacy".

## Docs and tests

- README: 1 row.
- Coverage goes from 237 to 238.
- Unit tests:
  - The service route is `GET …/c-1/files/f-1`.
  - `get` fails without `--file-id` or `--output`.
- Smoke test: extend the `campaign member assets` section, which already
  uploads a PDF into `Smoke Assets` as `FID`.
  - After that upload, `files get --file-id "$FID" --output "$WORK/got.pdf"`.
  - Check that the bytes are identical to the uploaded file (`cmp`).
