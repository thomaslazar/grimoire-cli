# Campaign wiki import and export (A2 of #102)

Two commands under `campaigns wiki`, one per route. Server facts are read at
`v1.8.0`, `backend/routers/campaigns/wiki_io.py`, and probed on the local stack.

## `campaigns wiki export --id <campaign-id> [--format md|mdfile|json] --output <path|->`

`GET /api/campaigns/{id}/wiki/export?format=…` (`export_wiki`, :197).

- `--format` is a choice option. Its default is `md`, the server's own default
  (`Query("md", pattern="^(md|mdfile|json)$")`, :199).
  - `md`: a zip with one `<slug>.md` file per page, plus frontmatter (title,
    visibility, parent slug, icon, page_type, session_date).
  - `mdfile`: one combined markdown file, nested by heading level. It can't be
    re-imported: it comes back as a single page.
  - `json`: a Grimoire bundle with `grimoire_wiki_version`.
- `--output` is required for every format, including `json`, the same as
  `backups download`. `-` copies the bytes to stdout, and a path writes the file
  and prints `{path, bytes}` (`ConsoleOutput.WriteStreamAsync`).
- No role tag. The route takes `get_current_user` plus a membership check
  (`can_view`, :219).
- Notes for the help text:
  - The export holds only the pages the caller can view. GM secrets are
    stripped unless the caller owns the campaign (:221-237).
  - It works on an archived campaign (docstring, :208).

## `campaigns wiki import --id <campaign-id> --file <path>`

`POST /api/campaigns/{id}/wiki/import` (`import_wiki`, :948), status 201,
response `WikiImportOut` (`{imported, format, pages}`).

- One multipart part, `file`. Its filename is the local basename, because
  `_parse_upload` (:824) picks the parser by extension.
- The server's folder shape (`files[]` + `paths[]`) is not offered. A zip of the
  same folder goes through the same `_Foldered` nesting and link remapping
  (`_records_from_zip`, :783).
- No role tag. The route needs the campaign owner (`assert_can_manage`,
  `_helpers.py:324`), not a role, and an archived campaign answers 409. The
  service passes no permission hint, as `CampaignWikiService` does already.
- Notes for the help text:
  - Never overwrites: every record becomes a new page, so importing twice
    duplicates pages.
  - Accepted input: `.md`/`.markdown`/`.txt`, a `.zip` (markdown vault,
    LegendKeeper page JSON or a Grimoire bundle), Grimoire JSON, or LegendKeeper
    JSON/`.lk`.
  - A zip's folders become nested pages, and a `Places/Places.md` or
    `index.md` file becomes its folder's page.
  - Unresolved `[[links]]` get stubs, the same as on create (probed).
  - Limit: 25 MB (`_MAX_IMPORT_BYTES`).

## Placement

- Both methods go on `CampaignWikiService`, and both commands go in
  `CampaignWikiCommands.cs`.
- The upload body follows `CampaignFilesService.UploadAsync`: a read failure
  becomes a `BodyInputException`, and the content type is
  `application/octet-stream`, which the server ignores.

## Docs and tests

- README Commands table: two rows.
- `tools/generate-api-coverage.py` `IMPLEMENTED`: two entries, then regenerate
  `docs/grimoire-api-coverage.md`.
- Unit tests:
  - The service pins the route, method and `format` query, and the `file` part
    and its filename.
  - The command tests check option shape and that `--output` is required.
- Smoke test, inside the existing `Smoke Wiki` section, before its cleanup:
  - Export `json`: the bundle lists the section's pages.
  - Export `md`: the output is a valid zip.
  - Import the `json` bundle back and check `imported`.
  - The section's existing delete-all cleanup removes the imported copies, so a
    re-run converges.
