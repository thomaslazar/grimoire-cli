# Campaign housekeeping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #101 as specified in `docs/specs/2026-10-15-campaign-housekeeping-design.md`. The commands are:
- `campaigns archive|unarchive|convert-to-group|delete`;
- a new `campaigns banner get|upload|delete|from-source|focus` group;
- `campaigns files upload-image`.

**Architecture:**
- The lifecycle commands extend `CampaignsCommand.cs` / `CampaignsService.cs`.
- The banner gets its own command file and service, mirroring `systems cover` (`CoverCommands.cs`, `SystemsService` cover methods).
- `upload-image` extends `CampaignFilesCommands.cs` / `CampaignFilesService.cs`.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, bash smoke test.

## Global Constraints

- Branch `feat/campaign-housekeeping`. Commits use Conventional Commits, with **no `Co-Authored-By:` and no "Generated with" lines**.
- Run `dotnet format GrimoireCli.sln` after editing C#. No blank lines inside method bodies.
- **No role tag and no `permissionHint` on any of these commands.** Every route gates on campaign ownership in the handler. `convert-to-group` also checks the gm/admin role in the handler, not as a route dependency, so it gets no tag either.
- Help text is terse. Don't restate flag descriptions, response fields or the rendered shapes. A `ChoiceOption`'s description must not repeat its values. Check the rendered `--help`.
- Every write uses the shared note `"Campaign owner only; refused while the campaign is archived."`. Exceptions: archive, unarchive and delete use `"Campaign owner only."`, and banner `get` uses `"Any campaign member."`.
- The local stack is at `http://host.docker.internal:9481`. Write only to the local stack.

---

### Task 1: Lifecycle commands

**Files:**
- Modify: `src/GrimoireCli/Services/CampaignsService.cs`
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs`
- Modify/Create tests: `tests/GrimoireCli.Tests/Commands/CampaignsCommandTests.cs`, plus a new `tests/GrimoireCli.Tests/Services/CampaignsServiceTests.cs`. The service test follows the `Routes()` theory pattern in `CampaignWikiServiceTests.cs`, so methods need `internal …Request` builders.
- Modify: `README.md` (4 rows after `campaigns update`) and `tools/generate-api-coverage.py` (`IMPLEMENTED`). Regenerate `docs/grimoire-api-coverage.md` with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`.

**Interfaces (produces):**
- `ArchiveAsync(string id, bool archived) : Task<string>` and `internal ArchiveRequest(string id, bool archived)`. Body: generated `CampaignArchive { Archived = archived }`.
- `ConvertToGroupAsync(string id, string? gmTitle) : Task<string>` and `internal ConvertToGroupRequest(string id, string? gmTitle)`. Body: `CampaignConvert`, with `GmTitle` set only when given, so an omitted title is not sent. Check how Kiota serialises a null property: it must not emit `"gm_title": null`, or if it does, the server must treat that the same as omitting the field. Verify against the live stack.
- `DeleteAsync(string id) : Task<string>` and `internal DeleteRequest(string id)`.
- All three pass the existing `NotFound` hint.

- [ ] **Step 1: Write the failing service tests.** Pin each route and method:
  - archive: `PUT .../campaigns/c-1/archive`
  - convert: `POST .../campaigns/c-1/convert-to-group`
  - delete: `DELETE .../campaigns/c-1`

  Also add a fact that reads `info.Content` and asserts `"archived":true` for `ArchiveRequest("c-1", true)` and `"archived":false` for `false`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the service methods.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - `archive`, `unarchive`, `convert-to-group` and `delete` are subcommands of `campaigns`.
  - Each fails to parse without `--id`.
  - `convert-to-group --id c --gm-title "Dungeon Master"` parses.
  - `delete` help contains `no undo`.
  - `convert-to-group` help contains `One-way`.
- [ ] **Step 6: Implement the commands.** Register them after `update`, before the subgroups.
  - `archive`: "Archive a campaign".
    - Notes:
      ```
      Campaign owner only.

      Hides it from every member's list unless --include-archived, and freezes
      it read-only for everyone, owner included.
      ```
  - `unarchive`: "Unarchive a campaign".
    - Notes: `Campaign owner only.`
  - `convert-to-group`: "Turn a personal campaign into a GM-run group campaign". Option `--gm-title`: "The GM's title shown to the table".
    - Notes:
      ```
      Campaign owner only; refused while the campaign is archived. Also needs
      the gm or admin role (403).

      One-way; 409 if it is already a group campaign. Members, guests and the
      schedule start empty; resources and the wiki carry over.
      ```
  - `delete`: "Delete a campaign and everything in it".
    - Notes:
      ```
      Campaign owner only; works on an archived campaign.

      Deletes its wiki, templates, resources, categories, files, members and
      schedule with it. There is no undo.
      ```
  - Response examples:
    - archive, unarchive and convert-to-group use `AddResponseExample<Generated.Models.CampaignOut>()`.
    - delete prints nothing on 204, like `wiki delete`.
  - Examples, one per command, e.g. `grimoire-cli campaigns archive --id <campaign-id>`.
- [ ] **Step 7: Format, run the full test suite, and read the rendered `--help` of all four commands.**
- [ ] **Step 8: README and coverage.**
  - README rows:
    ```
    | `campaigns archive --id <campaign-id>` | Archive a campaign |
    | `campaigns unarchive --id <campaign-id>` | Unarchive a campaign |
    | `campaigns convert-to-group --id <campaign-id> [--gm-title <title>]` | Turn a personal campaign into a GM-run group campaign |
    | `campaigns delete --id <campaign-id>` | Delete a campaign and everything in it |
    ```
  - `IMPLEMENTED`:
    ```python
    "PUT /api/campaigns/{campaign_id}/archive": "`campaigns archive` / `unarchive` ✅",
    "POST /api/campaigns/{campaign_id}/convert-to-group": "`campaigns convert-to-group` ✅",
    "DELETE /api/campaigns/{campaign_id}": "`campaigns delete` ✅",
    ```
  - Coverage goes from 197 to 200.
- [ ] **Step 9: Commit** with message `feat: add campaigns archive, unarchive, convert-to-group and delete`.

### Task 2: Banner group and image upload

**Files:**
- Create: `src/GrimoireCli/Services/CampaignBannerService.cs`
- Create: `src/GrimoireCli/Commands/CampaignBannerCommands.cs`. Register `CampaignBannerCommands.Create()` in `CampaignsCommand.Create()` next to the other subgroups.
- Create: `tests/GrimoireCli.Tests/Services/CampaignBannerServiceTests.cs` and `tests/GrimoireCli.Tests/Commands/CampaignBannerCommandTests.cs`.
- Modify: `src/GrimoireCli/Services/CampaignFilesService.cs`, `src/GrimoireCli/Commands/CampaignFilesCommands.cs`, and their tests.
- Modify: `README.md` (6 rows) and `IMPLEMENTED` (6 entries), then regenerate coverage. It goes from 200 to 206.

**Interfaces (produces):**
- `CampaignBannerService`. Every method has an `internal …Request` builder.
  - `GetAsync(string id, bool full) : Task<Stream>` via `SendStreamAsync`.
    - `size=full` is sent only when `full` is true.
    - No `notFoundHint`: the 404 is either the campaign or "No banner", and only the server's detail tells them apart.
  - `UploadAsync(string id, string filePath) : Task<string>`.
    - Copy `SystemsService.UploadCoverAsync`: read the file, turn a read failure into `BodyInputException`, and send a multipart `file` part.
    - The content type comes from `SystemsService.MimeForExtension`. It's `internal static`; reuse it.
    - Use the campaign `NotFound` hint.
  - `DeleteAsync(string id) : Task<string>`, with the campaign `NotFound` hint.
  - `FromSourceAsync(string id, string sourceType, string sourceId) : Task<string>`.
    - Body is the generated `BannerSourceIn`.
    - No `notFoundHint`: there are several 404 sources, exactly as the comment on `SystemsService.CoverFromSourceAsync` says.
  - `FocusAsync(string id, int focusY) : Task<string>`.
    - Body is the generated `BannerFocusIn { FocusY = focusY }`.
    - Use the campaign `NotFound` hint.
- `CampaignFilesService.UploadImageAsync(string campaignId, string filePath, string? categoryId, string? newCategoryName) : Task<string>`.
  - The same shape as `UploadAsync`, against `Campaigns[id].Images`.
  - The content type comes from the existing `MimeForExtension` in that file.
  - The category form fields are the same as `UploadAsync`.

- [ ] **Step 1: Write the failing service tests.**
  - Banner routes, with `Base = http://example.test/api/campaigns/c-1/banner`:
    - get: `GET Base`
    - get full: `GET Base?size=full`
    - upload: `POST Base`
    - delete: `DELETE Base`
    - from-source: `POST Base/from-source`
    - focus: `PUT Base/focus`
  - A fact that the focus body contains `"focus_y":30`.
  - A fact that the upload body has `name="file"` and `Content-Type: image/png` for a `.png` name.
  - Images: `POST .../campaigns/c-1/images`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the services.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - The `banner` subgroup holds exactly `get upload delete from-source focus`.
  - `get` fails without `--output`.
  - `get --size huge` fails, and `get --size full --output -` parses.
  - `from-source --source-type campaign_file --source-id x --id c` parses; `--source-type pdf` fails.
  - `focus` fails without `--focus-y`.
  - `files upload-image` fails without `--file`, and fails with both `--category-id` and `--new-category-name`.
- [ ] **Step 6: Implement the commands.**
  - Group: `new Command("banner", "The campaign's header image")`.
  - Options:
    - `--id` ("Campaign ID")
    - `--output` (required): "Output file path, or '-' for the image to stdout"
    - `--size`: `OptionHelpers.Choice("--size", "Image size; default small", ["small", "full"])`
    - `--file` (required): "Path to a PNG, JPEG, WebP or GIF"
    - `--source-type`: `OptionHelpers.Choice`, required, values `map, token, book, audio, campaign_file`, description "The kind of item to copy the image from". Make it required the same way other required choice options in the codebase are; check `CoverCommands.cs`.
    - `--source-id` (required): "That item's ID"
    - `--focus-y` (`Option<int>`, required): "Vertical focal point, 0 (top) to 100 (bottom)"
  - Descriptions, and Notes after the shared ownership line:
    - `get`: "Download the campaign banner". Shared line: `Any campaign member.`
      - Notes:
        ```
        small is a WebP at most 1000 px wide; full is the original. 404 when no
        banner is set.

        --output - writes the image to stdout; a path writes the file and prints
        {path, bytes}.
        ```
      - Response example: `SavedFile`. Copy the `BodyInputException` catch from `systems cover get`.
    - `upload`: "Upload the campaign banner".
      - Notes: `PNG, JPEG, WebP or GIF, max 5 MB; replaces any banner.`
      - Response example: `BannerUploadOut`.
    - `delete`: "Remove the campaign banner".
      - Notes: `Also resets focus to 50.`
      - Prints nothing on 204.
    - `from-source`: "Set the banner from an image Grimoire already holds".
      - Notes:
        ```
        campaign_file takes a file id from this campaign (resource_id of a file
        resource). You must be able to read the source yourself. The bytes are
        copied as WebP, so the banner outlives the source.
        ```
      - Response example: `BannerUploadOut`.
    - `focus`: "Set where the banner sits in its 2:1 frame".
      - Notes: `50 centres it; out of range is a 422.`
      - Response example: `BannerFocusOut`.
    - `files upload-image`: "Upload an image into a campaign and link it".
      - Shared line: `[.. OwnerOnly, …]`, using the existing `OwnerOnly` array in `CampaignFilesCommands.cs`.
      - Notes:
        ```
        As upload, but only a real PNG, JPEG, WebP or GIF, max 5 MB. The
        resource_id it returns is the file id [[image:<file-id>]] takes in a
        wiki page.
        ```
      - Response example: `LinkedResourceOut`. Copy the category validator from `upload`.
  - Examples, one per command.
- [ ] **Step 7: Format, run the full test suite, and read every new rendered `--help`.**
- [ ] **Step 8: README and coverage.**
  - README rows, after the `campaigns files upload` row:
    ```
    | `campaigns files upload-image --id <campaign-id> --file <path> [--category-id <id> \| --new-category-name <name>]` | Upload an image into a campaign and link it |
    | `campaigns banner get --id <campaign-id> --output <path\|-> [--size <small\|full>]` | Download the campaign banner |
    | `campaigns banner upload --id <campaign-id> --file <path>` | Upload the campaign banner |
    | `campaigns banner delete --id <campaign-id>` | Remove the campaign banner |
    | `campaigns banner from-source --id <campaign-id> --source-type <type> --source-id <id>` | Set the banner from an image Grimoire already holds |
    | `campaigns banner focus --id <campaign-id> --focus-y <0-100>` | Set where the banner sits in its 2:1 frame |
    ```
  - `IMPLEMENTED`:
    ```python
    "POST /api/campaigns/{campaign_id}/images": "`campaigns files upload-image` ✅",
    "GET /api/campaigns/{campaign_id}/banner": "`campaigns banner get` ✅",
    "POST /api/campaigns/{campaign_id}/banner": "`campaigns banner upload` ✅",
    "DELETE /api/campaigns/{campaign_id}/banner": "`campaigns banner delete` ✅",
    "POST /api/campaigns/{campaign_id}/banner/from-source": "`campaigns banner from-source` ✅",
    "PUT /api/campaigns/{campaign_id}/banner/focus": "`campaigns banner focus` ✅",
    ```
- [ ] **Step 9: Commit** with message `feat: add campaigns banner commands and files upload-image`.

### Task 3: Smoke test and API notes

**Files:**
- Modify: `docker/smoke-test.sh`. Add a new section `# ---- campaign housekeeping ----` after the `campaign wiki` section, before `# ---- api keys ----`.
- Modify: `docs/grimoire-api-notes.md`. Add one bullet to the `## Campaigns` list, after the `delete_items` bullet.

**Interfaces:** consumes the Task 1 and 2 commands. The smoke CLI session is admin, which passes the gm-or-admin check. Fixture image: `docker/fixture-cover.png`.

- [ ] **Step 1: Add the smoke section.** Follow the existing section style:
  - a `# …` narration line before each step;
  - stderr to `"$WORK/cli.err"`;
  - `fail`/`ok`.

  The steps:
  1. Delete every campaign named `Smoke Housekeeping` returned by `campaigns list --include-archived`.
  2. `campaigns create` with `{"name":"Smoke Housekeeping"}` and capture `HK`.
  3. `convert-to-group`, then expect `.is_gm_campaign == true`. Run `convert-to-group` again and expect a non-zero exit.
  4. `files upload-image --file docker/fixture-cover.png`, then expect `.is_image == true`. Capture `IMG=.resource_id`.
  5. `banner from-source --source-type campaign_file --source-id "$IMG"`, then expect `.banner_path` to end with `.webp`.
  6. `banner focus --focus-y 30`, then expect `.banner_focus_y == 30`.
  7. `banner get --output "$WORK/banner.webp"`, then expect a non-empty file (`[ -s … ]`).
  8. `banner upload --file docker/fixture-cover.png`, then `banner delete`. After that, `banner get --output "$WORK/none.webp"` must exit non-zero.
  9. `archive`. Expect `HK` to be absent from `campaigns list` and present in `campaigns list --include-archived`.
  10. `unarchive`, then expect `.is_archived == false`.
  11. `delete`, then `campaigns get --id "$HK"` must exit non-zero.

  Add `ok` lines grouped as in the wiki section, about 4–5 of them.
- [ ] **Step 2: Add the API note.**
  ```
  - **Delete leaves files on disk.** `delete_campaign` removes rows only
    (`core.py:364-376`): the banner (`<id>.webp`, `<id>_sm.webp`) and uploaded
    files stay under `campaign_uploads/`. Verified against the 1.8.0 stack.
  ```
- [ ] **Step 3: Build, then run the smoke test twice.** Run `dotnet build GrimoireCli.sln`, then `bash docker/smoke-test.sh`. Capture the full output each time. Both runs must end with `smoke: all checks passed`.
- [ ] **Step 4: Commit** with message `test: cover campaign housekeeping in the smoke test`.
