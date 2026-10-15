# Campaign housekeeping (#101)

This covers the 9 routes that tidy up a campaign: archive, convert, delete,
banner, and note-embedding images. They become 10 commands.

Server facts were read at `v1.8.0` in `backend/routers/campaigns/`: `core.py`,
`uploads.py` and `_schemas.py`. `services/image_source.py` is cited as `src:`.
Everything marked *probed* was checked against the local stack.

## Lifecycle: `CampaignsCommand.cs` / `CampaignsService.cs`

| Command | Route |
|---|---|
| `campaigns archive --id` | `PUT /campaigns/{id}/archive` `{"archived": true}` |
| `campaigns unarchive --id` | `PUT /campaigns/{id}/archive` `{"archived": false}` |
| `campaigns convert-to-group --id [--gm-title <title>]` | `POST /campaigns/{id}/convert-to-group` |
| `campaigns delete --id` | `DELETE /campaigns/{id}` |

- **Archive and unarchive** (`set_campaign_archived`, `core.py:335`).
  - The route is owner only, with no admin override. It deliberately skips the
    archived check in `assert_can_manage`, so an archived campaign can still be
    unarchived.
  - Archiving hides the campaign from every member's `list` unless
    `--include-archived` is passed. It also freezes the campaign read-only for
    everyone, the owner included.
  - *Probed:* an archived campaign is gone from `GET /campaigns`, and a banner
    write to it answers 409.
  - The single bool route is split into two commands, following the
    `wiki hide` / `unhide` pattern. The service sends the body itself.
- **convert-to-group** (`convert_campaign_to_group`, `core.py:303`).
  - Needs campaign ownership (`assert_can_manage`, so an archived campaign is
    refused).
  - Also needs the gm or admin role. The handler checks this itself
    (`is_gm_or_admin`, 403); it is not a route dependency. CLAUDE.md requires a
    role tag to match the router's dependency, so there is **no tag**, and the
    help states the requirement in words.
  - It is one-way, and answers 409 if the campaign is already a group
    campaign (*probed*).
  - Nothing is migrated. Members, guests and the schedule start empty;
    resources and the wiki carry over.
  - `--gm-title` maps to the body field `gm_title` (`CampaignConvert`,
    `_schemas.py:56`). When it is omitted, the existing title is kept.
    *Probed:* the default title is "Game Master".
- **delete** (`delete_campaign`, `core.py:364`).
  - Owner only, and it works on an archived campaign.
  - Deletes the campaign and, by cascade, its wiki, templates, resources,
    categories, files, members and schedule (`models/campaigns.py:67-102`).
    There is no undo.
  - No prompt and no `--yes`; see CLAUDE.md, "No confirm-gated command". The
    warning lives in the help text.

## Banner: new `CampaignBannerCommands.cs` / `CampaignBannerService.cs`

The group is `campaigns banner`, and it mirrors `systems cover`.

| Command | Route |
|---|---|
| `get --id --output <path\|-> [--size small\|full]` | `GET /campaigns/{id}/banner[?size=full]` |
| `upload --id --file <path>` | `POST /campaigns/{id}/banner` (multipart `file`) |
| `delete --id` | `DELETE /campaigns/{id}/banner` |
| `from-source --id --source-type <type> --source-id <id>` | `POST /campaigns/{id}/banner/from-source` |
| `focus --id --focus-y <0-100>` | `PUT /campaigns/{id}/banner/focus` |

- **Writes are owner only**, through `assert_can_manage`, so they are refused
  while the campaign is archived. `get` needs only membership (`can_view`,
  `uploads.py:236`).
- **`get`**:
  - `small`, the server's default, is a WebP at most 1000 px wide. `full` is
    the original.
  - The server checks `size` only for `full`, so the CLI sends it only when
    `full` is chosen. `--size` is a choice option.
  - Answers 404 "No banner" when none is set.
  - *Probed:* both sizes answer 200 with `Content-Type: text/plain`. The CLI
    writes the bytes regardless.
- **`upload`**:
  - Accepts PNG, JPEG, WebP or GIF, up to 5 MB (`_IMAGE_TYPES`,
    `_MAX_IMAGE_BYTES`, `uploads.py:29-38`).
  - Checks the content type, then the bytes (`_validate_image`).
  - The content type comes from the file extension, the same way
    `SystemsService.MimeForExtension` does it.
- **`delete`** also resets the focus to 50 (`uploads.py:270`).
- **`from-source`** (`uploads.py:184`):
  - `--source-type` is a choice option: `map`, `token`, `book`, `audio` or
    `campaign_file` (`src:32`). `campaign_file` resolves against this
    campaign's own files.
  - The caller must be able to read the source in their own right.
  - The bytes are copied and converted to WebP, so the banner survives the
    source being deleted.
  - *Probed:* a `campaign_file` source answers 200 with `banner_path`
    `<id>.webp`.
- **`focus`** maps to body field `focus_y`, between 0 and 100. A value outside
  that range is a server 422 (*probed*), and the CLI adds no range check.

## Images: `CampaignFilesCommands.cs` / `CampaignFilesService.cs`

`campaigns files upload-image --id --file <path> [--category-id <id> | --new-category-name <name>]`
calls `POST /campaigns/{id}/images` (`upload_campaign_image`, `uploads.py:620`).

- It is `files upload` (`uploads.py:534`) narrowed to images: PNG, JPEG, WebP
  or GIF, with the bytes checked, up to 5 MB instead of 200 MB. It always sets
  `is_image`.
- It is owner only. Uploads that an admin has disabled answer 403, and the
  admin's per-file limit answers 413. Admins are exempt from both.
- It returns the linked resource. Its `resource_id` is the file id that
  `[[image:<file-id>]]` takes (*probed*).
- The category flags, and their mutual exclusion, are copied from
  `files upload`.

## Not in help: server behaviour for `grimoire-api-notes.md`

- **Delete leaves files on disk.** `delete_campaign` removes only rows. The
  banner (`<id>.webp`, `<id>_sm.webp`) and uploaded files stay under
  `campaign_uploads/`. *Probed:* both were still present after a 204. This is
  the server's defect and the CLI lives with it. It is not a caveat a caller
  can act on.

## Docs and tests

- README: 10 rows.
- `IMPLEMENTED`: 9 entries; archive and unarchive share one key. Coverage goes
  from 197 to 206.
- Unit tests:
  - Service tests pin each route and method, the archive body for both
    commands, `size` sent only for `full`, and the multipart `file` part with
    its content type.
  - Command tests check required options, the `--size` and `--source-type`
    choices, and that `--category-id` and `--new-category-name` are mutually
    exclusive.
- Smoke test, as a new `campaign housekeeping` section using a throwaway gm
  campaign named `Smoke Housekeeping`:
  1. Delete any leftover campaign with that name, archived ones included.
  2. Create the campaign (personal) and `convert-to-group`. Check
     `is_gm_campaign`. Convert again and check that it exits non-zero.
  3. `files upload-image` the fixture `docker/fixture-cover.png`.
  4. `banner from-source campaign_file` with the image's `resource_id`.
  5. `banner focus 30`. `banner get --output` a file and check it is non-empty.
  6. `banner upload` the fixture, then `banner delete`. Check that `banner get`
     then fails.
  7. `archive`, and check the campaign is missing from `list` but present with
     `--include-archived`.
  8. `unarchive`.
  9. `delete`, and check that `get` fails.

  The section runs as the user the smoke test is logged in as (admin), who
  passes the gm-or-admin check.
- #101 is updated to point at the spec, and closed by the PR.
