# Campaign member assets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #100 part 2, as specified in `docs/specs/2026-10-16-campaign-member-assets-design.md`. That means three subgroups under `campaigns members`:
- `art get|upload|delete`
- `token get|upload|delete`
- `sheet get|upload|delete|from-source|sources`

**Architecture:**
- Three subgroups, each with its own command file and service, registered in `CampaignMembersCommands.Create()`.
- Every service method has an `internal …Request` builder. Upload builders take `(bytes, fileName)`, as `CampaignBannerService` does.
- The patterns to copy are in `CampaignBannerCommands.cs` / `CampaignBannerService.cs`:
  - `get` with `--output`, `SavedFile`, and the `BodyInputException` catch
  - `upload` with multipart `file`
  - `delete` with 204

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, bash smoke test.

## Global Constraints

- Branch `feat/campaign-member-assets`. Use Conventional Commits, with **no `Co-Authored-By:` and no "Generated with" lines**.
- Run `dotnet format GrimoireCli.sln` after editing C#. No blank lines inside method bodies.
- **No role tag and no `permissionHint`.**
- **No not-found hint on per-member routes.** `sheet sources` uses `"No campaign with that ID. List them with: grimoire-cli campaigns list"`.
- Help is terse:
  - Don't restate flag descriptions, response fields or rendered shapes.
  - A `ChoiceOption` description doesn't repeat its values.
  - Check the rendered `--help`.
- `--member-id` description, verbatim: `Member ID: id from campaigns get members[] or guests list, not user_id`.
- Shared Notes lines, verbatim:
  - Writes: `The member themselves or the campaign owner; 409 while the campaign is archived. The owner has no slot of their own.`
  - Reads (`get`): `Any campaign member. 404 when the slot is empty.`
  - `--output` paragraph: `--output - writes the bytes to stdout; a path writes the file and prints {path, bytes}.`
- The local stack is at `http://host.docker.internal:9481`. Write only to the local stack.

---

### Task 1: `members art` and `members token`

**Files:**
- Create: `src/GrimoireCli/Services/CampaignMemberArtService.cs` and `src/GrimoireCli/Commands/CampaignMemberArtCommands.cs`
- Create: `src/GrimoireCli/Services/CampaignMemberTokenService.cs` and `src/GrimoireCli/Commands/CampaignMemberTokenCommands.cs`
- Create: one service test file and one command test file per subgroup
- Modify: `CampaignMembersCommands.cs`, to register both subgroups
- Modify: `README.md` (6 rows after `campaigns members remove`) and `IMPLEMENTED`, then regenerate coverage with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`. The total goes from 226 to 232.

**Interfaces (produces):** each service has the same three methods:
- `GetAsync(id, memberId) : Task<Stream>` via `SendStreamAsync`
- `UploadAsync(id, memberId, filePath) : Task<string>`. It reads the file and wraps a read failure in `BodyInputException`. The multipart `file` part takes its content type from `SystemsService.MimeForExtension`.
- `DeleteAsync(id, memberId) : Task<string>`

The generated builders are `Campaigns[id].Members[memberId].Art` and `.Token`. Confirm the member indexer name, since `Members[...]` may produce a different builder than the `{user_id}` route. Look in `Generated/Api/Campaigns/Item/Members/`.

- [ ] **Step 1: Write the failing service tests.**
  - Routes, for both art and token:
    - `GET …/c-1/members/m-1/art`
    - `POST …/art`
    - `DELETE …/art`
  - An upload with the name `a.png` has `name="file"` and `Content-Type: image/png`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement both services.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - `members` holds `art` and `token`, and each holds exactly `get upload delete`.
  - Every command fails without `--member-id`.
  - `get` fails without `--output`.
  - `upload` fails without `--file`.
- [ ] **Step 6: Implement the commands.**
  - Group descriptions:
    - `art`: "A member's character art (their avatar)"
    - `token`: "A member's character token"
  - Descriptions:
    - `get`: "Download the character art" / "Download the character token"
    - `upload`: "Upload the character art" / "Upload the character token"
    - `delete`: "Remove the character art" / "Remove the character token"
  - Notes:
    - `get`: Reads line, blank line, `--output` paragraph.
    - `upload`: Writes line, blank line, `PNG, JPEG, WebP or GIF, max 5 MB; replaces the current image.`
    - `delete`: Writes line. Prints nothing on 204.
  - `--file`: "Path to a PNG, JPEG, WebP or GIF".
  - `--output` (required): "Output file path, or '-' for the image to stdout".
  - Response examples:
    - `get`: `SavedFile`
    - `upload`: `MemberArtUploadOut` / `MemberTokenUploadOut`
  - One example per command.
- [ ] **Step 7: Format, run the full suite, and read the rendered `--help` of all six commands.**
- [ ] **Step 8: README and coverage.**
  - Rows:
    ```
    | `campaigns members art get --id <campaign-id> --member-id <member-id> --output <path\|->` | Download a member's character art |
    | `campaigns members art upload --id <campaign-id> --member-id <member-id> --file <path>` | Upload a member's character art |
    | `campaigns members art delete --id <campaign-id> --member-id <member-id>` | Remove a member's character art |
    | `campaigns members token get --id <campaign-id> --member-id <member-id> --output <path\|->` | Download a member's character token |
    | `campaigns members token upload --id <campaign-id> --member-id <member-id> --file <path>` | Upload a member's character token |
    | `campaigns members token delete --id <campaign-id> --member-id <member-id>` | Remove a member's character token |
    ```
  - `IMPLEMENTED`: the GET, POST and DELETE keys for `/api/campaigns/{campaign_id}/members/{member_id}/art` and `/token`, valued `` `campaigns members art get` ✅ `` and so on.
- [ ] **Step 9: Commit** `feat: add campaigns members art and token commands`.

### Task 2: `members sheet`

**Files:**
- Create: `src/GrimoireCli/Services/CampaignMemberSheetService.cs` and `src/GrimoireCli/Commands/CampaignMemberSheetCommands.cs`, plus tests for each
- Modify: `CampaignMembersCommands.cs`, to register the subgroup
- Modify: README (5 rows after the token rows) and `IMPLEMENTED`, then regenerate coverage. The total goes from 232 to 237.

**Interfaces (produces):** `CampaignMemberSheetService` has:
- `GetAsync(id, memberId) : Task<Stream>`
- `UploadAsync(id, memberId, filePath)`. The content type is `.pdf` → `application/pdf`; anything else goes through `SystemsService.MimeForExtension`. Keep that mapping in a small `internal static string SheetMimeForExtension(string path)` in this service, and test it.
- `DeleteAsync(id, memberId)`
- `FromSourceAsync(id, memberId, sourceType, sourceId)`. POST `Members[memberId].Sheet.Duplicate`, with the body built from the generated `Body_duplicate_member_sheet_…` model (`SourceType`, `SourceId`). Confirm the property names. No not-found hint.
- `SourcesAsync(id)`. GET `Campaigns[id].SheetSources`, with the campaign hint.

- [ ] **Step 1: Write the failing service tests.**
  - Routes:
    - `GET …/members/m-1/sheet`
    - `POST …/sheet`
    - `DELETE …/sheet`
    - `POST …/sheet/duplicate`
    - `GET …/c-1/sheet-sources`
  - The duplicate body contains `"source_type":"file"` and `"source_id":"f-1"`.
  - `SheetMimeForExtension("s.pdf") == "application/pdf"`, and `"s.png"` gives `"image/png"`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the service.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - `sheet` holds exactly `get upload delete from-source sources`.
  - `from-source --source-type pdf` fails.
  - `from-source --id c --member-id m --source-type book --source-id b` parses.
  - `sources --id c` parses.
  - `sources` has no `--member-id`.
- [ ] **Step 6: Implement the commands.**
  - Group description: "A member's character sheet file".
  - Commands, descriptions and Notes. Re-wrap any Notes line longer than about 79 characters.
    - `get`: "Download the character sheet".
      - Notes: Reads line, then `The file keeps the name it was uploaded with.`, blank line, `--output` paragraph.
      - Response example: `SavedFile`.
    - `upload`: "Upload the character sheet".
      - Notes: Writes line, blank line, then:
        ```
        A PDF, or a PNG, JPEG, WebP or GIF, max 15 MB; replaces the current
        sheet and clears any sheet link. A link set with members update clears
        the uploaded file in turn.
        ```
      - Option `--file`: "Path to a PDF or image".
      - Response example: `MemberSheetOut`.
    - `delete`: "Remove the character sheet".
      - Notes: Writes line.
      - Prints nothing on 204.
    - `from-source`: "Copy a blank sheet PDF into the member's slot".
      - Options:
        - `--source-type`: `OptionHelpers.Choice`, required, values `book` and `file`, description "Where the blank sheet comes from".
        - `--source-id` (required): "Its ID, from sheet sources".
      - Notes: Writes line, blank line, then:
        ```
        Only what sheet sources lists for you can be copied (404 otherwise); PDF
        only, max 15 MB. Clears any sheet link.
        ```
      - Response example: `MemberSheetOut`.
    - `sources`: "Blank sheets you can copy into a slot".
      - Notes:
        ```
        Any campaign member. Library books in the character-sheet category that
        you can read, limited to the campaign's system when it has one, and
        campaign PDF files you can read.
        ```
      - Response example: `SheetSourcesOut`.
  - One example per command.
- [ ] **Step 7: Format, run the full suite, and read the rendered `--help` of all five commands.**
- [ ] **Step 8: README and coverage.**
  - Rows:
    ```
    | `campaigns members sheet get --id <campaign-id> --member-id <member-id> --output <path\|->` | Download a member's character sheet |
    | `campaigns members sheet upload --id <campaign-id> --member-id <member-id> --file <path>` | Upload a member's character sheet |
    | `campaigns members sheet delete --id <campaign-id> --member-id <member-id>` | Remove a member's character sheet |
    | `campaigns members sheet from-source --id <campaign-id> --member-id <member-id> --source-type <book\|file> --source-id <id>` | Copy a blank sheet PDF into a member's slot |
    | `campaigns members sheet sources --id <campaign-id>` | Blank sheets you can copy into a slot |
    ```
  - `IMPLEMENTED`: the GET, POST and DELETE keys for `…/members/{member_id}/sheet`, plus `POST …/sheet/duplicate` → `` `campaigns members sheet from-source` ✅ `` and `GET /api/campaigns/{campaign_id}/sheet-sources` → `` `campaigns members sheet sources` ✅ ``.
- [ ] **Step 9: Commit** `feat: add campaigns members sheet commands`.

### Task 3: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`. Add a new section, `# ---- campaign member assets ----`, after the `campaign membership` section and before `# ---- api keys ----`.

**Interfaces:**
- Consumes the Task 1 and 2 commands.
- The smoke session is admin, so it owns `Smoke Assets`.
- Guest access is on in the stack (`GUEST_ACCESS_ENABLED=true`).

- [ ] **Step 1: Add the section.** Follow the existing style: a `# …` narration line before each step, stderr to `"$WORK/cli.err"`, `fail`/`ok`, and `set +e` / `set -e` around the steps that are expected to exit non-zero.
  1. Delete every campaign named `Smoke Assets` from `campaigns list --include-archived`.
  2. Create the campaign with `{"name":"Smoke Assets","is_gm_campaign":true}` and capture `AST`.
  3. Run `guests create --nickname "Smoke Holder"` and capture `MID=.id`.
  4. Art:
     - `art upload --file docker/fixture-cover.png`: expect `.character_art_path`.
     - `art get --output "$WORK/art.img"`: expect `-s`.
     - `art delete`.
     - `art get` again: expect a non-zero exit.
  5. Token: the same as art, expecting `.character_token_path`.
  6. Sheet:
     - `sheet upload --file docker/fixture-cover.png`: expect `.character_sheet_filename == "fixture-cover.png"`.
     - `sheet get --output "$WORK/sheet.img"`: expect `-s`.
  7. Sheet from a source:
     - Write a minimal PDF:
       ```bash
       printf '%%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%%%EOF\n' > "$WORK/smoke-sheet.pdf"
       ```
     - `campaigns files upload --id "$AST" --file "$WORK/smoke-sheet.pdf"` and capture `FID=.resource_id`.
     - `sheet sources --id "$AST"`: expect `.files[] | select(.id == FID)`.
     - `sheet from-source --source-type file --source-id "$FID"`: expect `.character_sheet_filename == "smoke-sheet.pdf"`.
  8. `sheet delete`, then `sheet get`: expect a non-zero exit.
  9. `campaigns delete --id "$AST"`.

  About 4 `ok` lines.
- [ ] **Step 2: Build, then run the smoke test twice.** Run `dotnet build GrimoireCli.sln`, then `bash docker/smoke-test.sh`. Capture the full output each time. Both runs must end with `smoke: all checks passed`.
- [ ] **Step 3: Commit** `test: cover campaign member assets in the smoke test`.
