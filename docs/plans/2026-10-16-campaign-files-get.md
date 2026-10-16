# Campaign file download Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Add `campaigns files get` as specified in `docs/specs/2026-10-16-campaign-files-get-design.md`.
- Record three groups of campaign routes as "not wrapped" in the coverage generator.

**Architecture:**
- The command extends the existing `CampaignFilesCommands.cs` / `CampaignFilesService.cs`.
- The bytes-out pattern is copied from `CampaignBannerCommands` `get`: `--output`, `SavedFile`, and the `BodyInputException` catch.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (never hand-edit), xUnit, bash smoke test.

## Global Constraints

- Branch `feat/campaign-files-get`.
- Conventional Commits, with **no `Co-Authored-By:` and no "Generated with" lines**.
- Run `dotnet format GrimoireCli.sln` after editing C#. No blank lines inside method bodies.
- No role tag, no `permissionHint`, and no not-found hint on `files get`.
- Help is terse:
  - Don't restate flag descriptions, response fields or rendered shapes.
  - Check the rendered `--help`.
- The local stack is at `http://host.docker.internal:9481`. Write only to the local stack.

---

### Task 1: `campaigns files get`, README, coverage notes, smoke

**Files:**
- Modify: `src/GrimoireCli/Services/CampaignFilesService.cs`, `src/GrimoireCli/Commands/CampaignFilesCommands.cs`, and their tests.
- Modify: `README.md`. Add a row after `campaigns files upload-image`.
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`. The total goes from 237 to 238.
- Modify: `docker/smoke-test.sh`, in the `campaign member assets` section.

**Interfaces (produces):**
- `CampaignFilesService.GetAsync(string campaignId, string fileId) : Task<Stream>`
- `internal GetRequest(string campaignId, string fileId)`, which calls `_client.Api.Api.Campaigns[id].Files[fileId].ToGetRequestInformation()`. Confirm the indexer name.

- [ ] **Step 1: Write the failing tests.**
  - Service: the route is `GET http://example.test/api/campaigns/c-1/files/f-1`.
  - Commands: `files` holds `get`. `get` fails without `--file-id` and fails without `--output`. `get --id c --file-id f --output -` parses.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
  - `get`: "Download a campaign file".
  - Options:
    - `--file-id` (required): "File ID: resource_id from files upload or a file row in resources list"
    - `--output` (required): "Output file path, or '-' for the bytes to stdout"
  - Notes, verbatim:
    ```
    Any campaign member. Anyone but the owner reads only files whose link is
    public, or private and shared with them; gm or unlinked files are 403.

    --output - writes the bytes to stdout; a path writes the file and prints
    {path, bytes}.
    ```
  - Example: `grimoire-cli campaigns files get --id <campaign-id> --file-id <file-id> --output handout.pdf`
  - Response example: `SavedFile`.
- [ ] **Step 4: Run the tests and confirm they pass. Format, run the full suite, and read the rendered `--help`.**
- [ ] **Step 5: README and coverage.**
  - README row:
    ```
    | `campaigns files get --id <campaign-id> --file-id <file-id> --output <path\|->` | Download a campaign file |
    ```
  - `IMPLEMENTED` additions. The values are verbatim and include the backticks:
    ```python
    "GET /api/campaigns/{campaign_id}/files/{file_id}": "`campaigns files get` ✅",
    "GET /api/campaigns/resources/search": "not wrapped: `search` finds the same items; their ids are what `resources add` takes",
    "GET /api/campaigns/resources/suggested/{system_id}": "not wrapped: `systems books list --category core` gives the same set",
    ```
  - Also add these 8 keys, each with the value `"not wrapped: legacy; rolled into session wiki pages on startup"`:
    - `GET /api/campaigns/{campaign_id}/sessions`
    - `POST /api/campaigns/{campaign_id}/sessions`
    - `GET /api/campaigns/{campaign_id}/sessions/search`
    - `GET /api/campaigns/{campaign_id}/sessions/{session_id}`
    - `PATCH /api/campaigns/{campaign_id}/sessions/{session_id}`
    - `DELETE /api/campaigns/{campaign_id}/sessions/{session_id}`
    - `PUT /api/campaigns/{campaign_id}/sessions/{session_id}/notes/gm`
    - `PUT /api/campaigns/{campaign_id}/sessions/{session_id}/notes/player`
  - Check the generated doc renders all 11 rows. The counted total must be 238.
- [ ] **Step 6: Smoke.**
  - In the `campaign member assets` section of `docker/smoke-test.sh`, add a step right after the `files upload` of `smoke-sheet.pdf` captures `FID`.
  - The step is a `# downloading the uploaded PDF expecting identical bytes` narration line, then:
    ```bash
    "$CLI" campaigns files get --id "$AST" --file-id "$FID" --output "$WORK/got-sheet.pdf" >/dev/null 2>"$WORK/cli.err" \
      || { cat "$WORK/cli.err" >&2; fail "campaigns files get exited non-zero"; }
    cmp -s "$WORK/smoke-sheet.pdf" "$WORK/got-sheet.pdf" || fail "campaigns files get should return the uploaded bytes"
    ok "campaigns files get returns the uploaded file"
    ```
  - Build, then run `bash docker/smoke-test.sh` once. Capture the full output, and expect it to end with `smoke: all checks passed`.
- [ ] **Step 7: Commit** `feat: add campaigns files get and note unwrapped campaign routes`.
