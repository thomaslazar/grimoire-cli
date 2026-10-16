# Campaign member assets (#100, part 2)

This covers each member's character art, token and sheet slot: 11 commands, one per route, in three subgroups under `campaigns members`. Part 1, which covers membership and guests, is `docs/specs/2026-10-16-campaign-membership-design.md`.

Server facts are read at `v1.8.0` from `backend/routers/campaigns/uploads.py` (`:line`) and `sheets.py` (`s:line`).

These slots hold files only. The schema-driven character builder is a separate feature (#98) and is not touched here.

## Member id

- Every per-member route is keyed by **member id**, which is the `CampaignMember.id`.
- That id appears as `id` in `campaigns get`'s `members[]` and in `guests list`.
- The owner's row has no `id` (`_helpers.py:450-460`), so the GM has no slot of their own.
- The option is `--member-id`, with the description: "Member ID: `id` from `campaigns get` members[] or `guests list`, not user_id".
- Every command also takes `--id <campaign-id>`.

## Commands

| Command | Route |
|---|---|
| `members art get --id --member-id --output <path\|->` | `GET /campaigns/{id}/members/{m}/art` (:314) |
| `members art upload --id --member-id --file` | `POST …/art` (:290) |
| `members art delete --id --member-id` | `DELETE …/art` (:333) |
| `members token get --id --member-id --output <path\|->` | `GET …/token` (:377) |
| `members token upload --id --member-id --file` | `POST …/token` (:352) |
| `members token delete --id --member-id` | `DELETE …/token` (:396) |
| `members sheet get --id --member-id --output <path\|->` | `GET …/sheet` (:443) |
| `members sheet upload --id --member-id --file` | `POST …/sheet` (:413) |
| `members sheet delete --id --member-id` | `DELETE …/sheet` (:466) |
| `members sheet from-source --id --member-id --source-type book\|file --source-id` | `POST …/sheet/duplicate` (s:68) |
| `members sheet sources --id` | `GET /campaigns/{id}/sheet-sources` (s:120) |

- **Naming:** `from-source` is the CLI's existing word for "copy an existing item into a slot", as in `systems cover from-source` and `banner from-source`. The server calls this route `duplicate`.
- **Files:** each subgroup has its own command file and service:
  - `CampaignMemberArtCommands` / `CampaignMemberArtService`
  - `CampaignMemberTokenCommands` / `CampaignMemberTokenService`
  - `CampaignMemberSheetCommands` / `CampaignMemberSheetService`

  The three subgroups are registered in `CampaignMembersCommands.Create()`.

## Behaviour (help caveats)

- **Writes** are allowed for the member themselves or the campaign owner. They are refused while the campaign is archived (409; `_assert_can_edit_member`, :125).
- **`get`** (all three slots):
  - Allowed for any campaign member (`can_view`).
  - 404 ("No art", "No token", "No sheet") when the slot is empty.
  - `--output -` writes the bytes to stdout. A path writes the file and prints `{path, bytes}`, as `banner get` does.
- **Art and token `upload`:**
  - Accepts PNG, JPEG, WebP or GIF, max 5 MB (`_IMAGE_TYPES`, `_MAX_IMAGE_BYTES`).
  - Checks the bytes, and replaces the current image.
  - The content type comes from the file extension (`SystemsService.MimeForExtension`).
- **Sheet `upload`:**
  - Accepts a PDF or one of those image types, max 15 MB (`_SHEET_TYPES`, `_MAX_SHEET_BYTES`).
  - Image bytes are checked.
  - Clears any sheet link. A link set with `members update --character-sheet-url` clears the uploaded file in turn.
  - The download keeps the uploaded filename.
  - A `.pdf` extension sends `application/pdf`, so the sheet service maps `.pdf` itself on top of the image map.
- **`from-source`:**
  - The body is `{"source_type": "book"|"file", "source_id": …}` (embedded `Body` parameters).
  - It copies only a source that `sheet sources` lists for the caller. Anything else is 404 "Book not found" / "File not found", and a bad type is 400.
  - PDF only (400). Max 15 MB (413).
  - Clears any sheet link.
  - `--source-type` is a choice option with the server's values `book` and `file`.
- **`sources`:**
  - Allowed for any campaign member.
  - Lists library books in the `character-sheet` category that the caller can read, limited to the campaign's system when it has one. It also lists campaign PDF files the caller can read (s:37-65).
- **Delete:** a slot delete is 204 even when the slot is already empty.

## Shared

- No role tag and no permission hint.
- No not-found hint on per-member routes, so the server's own detail shows. `sheet sources` uses the campaign hint.

## Docs and tests

- README: 11 rows.
- `IMPLEMENTED`: 11 entries. Coverage goes from 226 to 237.
- The PR closes #100.
- Unit tests:
  - Services: route and method for each command; the multipart `file` part and its content type (`.png` → `image/png`, `.pdf` → `application/pdf` for the sheet); the `from-source` body.
  - Commands: required options; the `--source-type` choice; `get` requires `--output`.
- Smoke test: a new `campaign member assets` section using a throwaway group campaign `Smoke Assets` and one guest. The guest's `id` is the member id, so the section needs no second login.
  1. Sweep and recreate the campaign, then `guests create`.
  2. Art: `art upload` with `docker/fixture-cover.png`, `art get` (non-empty file), `art delete`, then `art get` fails.
  3. Token: the same sequence.
  4. Sheet:
     - `sheet upload` with the PNG fixture, then `sheet get` (non-empty file).
     - Write a minimal PDF into `$WORK` and `files upload` it into the campaign.
     - `sheet sources`: the file is listed.
     - `sheet from-source --source-type file` with its id: `character_sheet_filename` matches.
     - `sheet delete`, then `sheet get` fails.
  5. Delete the campaign. This also removes the guest.
