# Campaign wiki note-template catalogue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `campaigns wiki templates catalogue` and `campaigns wiki templates install`, as specified in `docs/specs/2026-10-15-campaign-wiki-template-catalogue-design.md`.

**Architecture:**
- Two more commands and two more service methods go into the existing `CampaignWikiTemplateCommands.cs` and `CampaignWikiTemplateService.cs`, following the patterns already there.
- A static catalogue fixture under `docker/addon-index/templates/` lets the smoke test browse and install with no network access.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, bash smoke test.

## Global Constraints

- Branch `feat/campaign-wiki-template-catalogue`. Use Conventional Commits, with **no `Co-Authored-By:` and no "Generated with" lines** in any commit.
- Run `dotnet format GrimoireCli.sln` after editing C#. Leave no blank lines inside method bodies.
- No role tag and no `permissionHint`: the gate is campaign ownership.
- Help text is terse. Don't restate flag descriptions, response fields, or the rendered shapes. Check the rendered `--help`.
- The local stack is at `http://host.docker.internal:9481`. Only write to the local stack.

---

### Task 1: Service, commands, unit tests, README, coverage, fixture

**Files:**
- Modify: `src/GrimoireCli/Services/CampaignWikiTemplateService.cs`
- Modify: `src/GrimoireCli/Commands/CampaignWikiTemplateCommands.cs`
- Modify: `tests/GrimoireCli.Tests/Services/CampaignWikiTemplateServiceTests.cs`
- Modify: `tests/GrimoireCli.Tests/Commands/CampaignWikiTemplateCommandTests.cs`
- Modify: `README.md`: add 2 rows after `campaigns wiki templates use` (~line 406)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`, after the `.../use` entry ~line 291), then regenerate `docs/grimoire-api-coverage.md`
- Already on the branch, uncommitted: `docker/addon-index/templates/index.json` and `docker/addon-index/templates/generic/smoke-note/smoke-note.md`. Commit them in this task. Don't change them.

**Interfaces:**
- Produces:
  - `CatalogueAsync(string campaignId, bool refresh) : Task<string>` and `internal CatalogueRequest(string campaignId, bool refresh)`.
  - `InstallAsync(string campaignId, string sourceId, string? indexUrl) : Task<string>` and `internal InstallRequest(string campaignId, string sourceId, string? indexUrl)`.
- Generated builders:
  - `_client.Api.Api.Campaigns[c].Wiki.Templates.Browse.ToGetRequestInformation(...)`, query parameter `Refresh`.
  - `_client.Api.Api.Campaigns[c].Wiki.Templates.Download[sourceId].ToPostRequestInformation(...)`, query parameter `IndexUrl`.
  - Confirm the names under `src/GrimoireCli/Generated/Api/Campaigns/Item/Wiki/Templates/Browse/` and `.../Download/Item/`.
- Each query parameter is sent only when set: `refresh` only when true, `index_url` only when non-null.
- Not-found hints:
  - catalogue: the existing campaign hint constant.
  - install: `"No such campaign or catalogue entry. List entries with: grimoire-cli campaigns wiki templates catalogue --id <campaign-id>"`.

- [ ] **Step 1: Write the failing service tests.**
  - Extend the `Routes()` theory with:
    - `catalogue`: `GET Base + "/browse"`, from `CatalogueRequest("c-1", false)`.
    - `install`: `POST Base + "/download/smoke-note"`, from `InstallRequest("c-1", "smoke-note", null)`.
  - Add these facts:
    ```csharp
    [Fact]
    public void CatalogueSendsRefreshOnlyWhenSet()
    {
        Assert.DoesNotContain("?", Of(Service().CatalogueRequest("c-1", false)).Uri);
        Assert.EndsWith("/browse?refresh=true", Of(Service().CatalogueRequest("c-1", true)).Uri);
    }

    [Fact]
    public void InstallSendsIndexUrlOnlyWhenSet()
    {
        Assert.DoesNotContain("?", Of(Service().InstallRequest("c-1", "smoke-note", null)).Uri);
        Assert.Contains("index_url=http", Of(Service().InstallRequest("c-1", "smoke-note", "http://x.test/templates/index.json")).Uri);
    }
    ```
- [ ] **Step 2: Run the tests and confirm they fail.** `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter CampaignWikiTemplateServiceTests`
- [ ] **Step 3: Implement both service methods** next to the existing ones. Change the class summary from "The eight routes" to "The ten routes".
- [ ] **Step 4: Run the service tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - Extend the subgroup-contents assertion to include `catalogue` and `install`.
  - `install` without `--source-id` fails to parse.
  - `install --id c --source-id smoke-note --index-url http://x/templates/index.json` parses cleanly.
  - `catalogue --id c --refresh` parses cleanly.
  - `catalogue` help contains `Entries carry no template body`.
  - `install` help contains `another copy`.
- [ ] **Step 6: Implement the commands.** Register them after `use` in `Create()`. Both get `[.. OwnerOnly, "", …]` Notes, as the other commands in the file do.
  - `catalogue`:
    - Description: `Browse the community note-template catalogue`.
    - Option: `--refresh` (`Option<bool>`), description `Re-fetch the catalogue instead of using the server's cache`.
    - Notes after `OwnerOnly`, verbatim:
      ```
      Sources are the add-on index URLs (addons settings --index-url, admin);
      each is read as a template index, else as <its dir>/templates/index.json.
      Entries carry no template body; it is only readable once installed.

      403 when the server disables external installs; 502 when the only source
      fails (with several, failing ones are skipped).
      ```
    - Example: `grimoire-cli campaigns wiki templates catalogue --id <campaign-id>`.
    - `AddResponseExample<Generated.Models.WikiTemplateBrowseOut>()`.
  - `install`:
    - Description: `Copy a catalogue template into the campaign`.
    - Options:
      - `--source-id` (required): `Catalogue template id`.
      - `--index-url`: `Source to take it from, when several list the id`.
    - Notes after `OwnerOnly`, verbatim:
      ```
      --index-url takes an available_in[].index_url from catalogue; any other
      value answers 404, as an unknown id does. Installing again makes another
      copy.

      403 when the server disables external installs; 502 when the catalogue or
      the body can't be fetched, or the body fails its sha256 check.
      ```
    - Example: `grimoire-cli campaigns wiki templates install --id <campaign-id> --source-id session-recap`.
    - `AddResponseExample<Generated.Models.WikiTemplateDetailOut>()`.
- [ ] **Step 7: Format, run the full test suite, and read the rendered `--help`** for both commands.
- [ ] **Step 8: README and coverage.**
  - README rows:
    ```
    | `campaigns wiki templates catalogue --id <campaign-id> [--refresh]` | Browse the community note-template catalogue |
    | `campaigns wiki templates install --id <campaign-id> --source-id <catalogue-id> [--index-url <url>]` | Copy a catalogue template into the campaign |
    ```
  - `IMPLEMENTED` entries:
    ```python
    "GET /api/campaigns/{campaign_id}/wiki/templates/browse": "`campaigns wiki templates catalogue` ✅",
    "POST /api/campaigns/{campaign_id}/wiki/templates/download/{template_id}": "`campaigns wiki templates install` ✅",
    ```
  - Regenerate with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`. The total goes from 195 to 197.
- [ ] **Step 9: Commit.**
  ```bash
  git add src tests README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docker/addon-index/templates
  git commit -m "feat: add campaigns wiki templates catalogue and install"
  ```

### Task 2: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`. Insert in the `campaign wiki` section, right after `ok "campaigns wiki templates export, import and delete"` (~line 2894).

**Interfaces:**
- Consumes `catalogue` and `install` from Task 1.
- Uses the existing variables:
  - `$WIKI`: the Smoke Wiki campaign.
  - `$FIXTURE_INDEX`: `http://addon-index/index.json`, set in the add-ons section.
  - `$DEFAULT_INDEX_URL`: read in the add-ons section from `addons list`'s `default_index_url`.
- The smoke CLI session is admin, so `addons settings` works.

- [ ] **Step 1: Add the checks.** Follow the section's style.
  ```bash
  # pointing the add-on index at the fixture so the template catalogue is served offline
  "$CLI" addons settings --index-url "$FIXTURE_INDEX" >/dev/null 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "addons settings (catalogue fixture) exited non-zero"; }
  # browsing the catalogue expecting the fixture template and no body
  "$CLI" campaigns wiki templates catalogue --id "$WIKI" --refresh 2>"$WORK/cli.err" \
    | jq -e '[.folders[].templates[] | select(.id == "smoke-note")] | length == 1 and (.[0] | has("body") | not)' >/dev/null \
    || { cat "$WORK/cli.err" >&2; fail "wiki templates catalogue should list smoke-note without a body"; }
  # installing the fixture template expecting its catalogue id recorded and its body verified
  CAT=$("$CLI" campaigns wiki templates install --id "$WIKI" --source-id smoke-note 2>"$WORK/cli.err" \
    | jq -r 'select(.source_id == "smoke-note" and .name == "Smoke Note") | .id') \
    || { cat "$WORK/cli.err" >&2; fail "wiki templates install exited non-zero"; }
  [ -n "$CAT" ] || fail "the installed template should record source_id smoke-note"
  "$CLI" campaigns wiki templates catalogue --id "$WIKI" | jq -e '.downloaded_ids == ["smoke-note"]' >/dev/null \
    || fail "catalogue should report smoke-note as downloaded"
  # deleting the installed template and restoring the published add-on index
  "$CLI" campaigns wiki templates delete --id "$WIKI" --template-id "$CAT" >/dev/null 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "wiki templates delete (installed) exited non-zero"; }
  "$CLI" addons settings --index-url "$DEFAULT_INDEX_URL" >/dev/null 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "addons settings restore (catalogue) exited non-zero"; }
  ok "campaigns wiki templates catalogue and install from the fixture catalogue"
  ```
  The template pre-clean loop added earlier already removes an installed copy left by an interrupted run. If an interrupted run leaves the index on the fixture, the add-ons section sets it again anyway, and this block restores it.
- [ ] **Step 2: Build, then run the smoke test twice against the local stack.** Run `dotnet build GrimoireCli.sln`, then `bash docker/smoke-test.sh`. Both runs must end with `smoke: all checks passed`. Don't filter that line out of the output.
- [ ] **Step 3: Commit.**
  ```bash
  git add docker/smoke-test.sh
  git commit -m "test: cover the note-template catalogue in the smoke test"
  ```
