# Global Pins and GM Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `pins list|add|remove` commands and the `campaigns gm-screen` commands, including `panels create|update|delete`, for Grimoire 1.11.0.

**Architecture:** Three command groups, each with its own command file and its own service:

- pins
- the GM screen board
- the GM screen panels

Every command is a thin pass-through to one endpoint. A shared `GmPanelHelp` helper teaches the panel config and state keys. Docs and the smoke test go in a final task.

**Tech Stack:** C#/.NET 10, System.CommandLine, the Kiota client in `src/GrimoireCli/Generated/` (never edit it), xUnit, and bash/jq for the smoke test.

Spec: `docs/specs/2026-11-15-pins-gm-screen-design.md`. Read the sections for your task. That spec is the source for every behaviour claim in the help text.

## Global Constraints

- No command takes `AddRoleRequired` or a `permissionHint`:
  - pins: `require_not_guest`
  - GM screen: `get_current_user`, with the owner check made inside the handler
- Pin verbs: `list`, `add`, `remove`. Panel verbs: `create`, `update`, `delete`.
- Choices:
  - `--collection`: `systems|maps|tokens|audio|models`
  - `--scope`: `scene|session|rest`
- `--append` sends `replace: false`. Without it, `replace: true` is sent.
- Bodies with nested JSON (`layout`, `import`, `panels create|update`) come from `--input <file>` or `--stdin`, with exactly one of the two required (`JsonBodyInput.RequireExactlyOneSource`).
- Help is terse. A description does not repeat a choice option's values or what the response sample shows. Every behaviour claim must match the spec.
- One file per command group, each with its own service. There is no shared table.
- The version floor stays at 1.10.1.
- Commit messages: Conventional Commits, with no `Co-Authored-By` and no "Generated with" lines.
- Run `dotnet format GrimoireCli.sln` after editing C#.
- The smoke test stays idempotent and writes only to the local stack.

## Patterns to copy (read before writing)

- **Flag-bodied commands and their service with the `Build*Body` helpers:** `src/GrimoireCli/Commands/CampaignCategoriesCommands.cs` and `src/GrimoireCli/Services/CampaignCategoriesService.cs`.
- **`--input`/`--stdin` bodies:** `CampaignWikiCommands.CreateCreateCommand` / `CreateUpdateCommand` and `CampaignWikiService.CreateRequest` / `UpdateRequest`. Each reads the body, validates it against the generated model, and sends it as raw JSON through `info.SetStreamContent`.
- **Internal `XxxRequest(...)` builders, and service tests that pin the method and URL:** `CampaignWikiService` and `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs`.
- **Parse and help tests:** `tests/GrimoireCli.Tests/Commands/CampaignCategoriesCommandTests.cs`.
- **The shared help helper:** `src/GrimoireCli/Commands/WikiPageHelp.cs`.
- **The 403 wording when external installs are disabled:** `CampaignWikiTemplateCommands.cs`, `catalogue` and `install`.
- **The locked-campaign wording:** the `Locked` array in `CampaignWikiCommands.cs`.

---

### Task 1: Global pins

**Files:**
- Create: `src/GrimoireCli/Services/PinsService.cs`
- Create: `src/GrimoireCli/Commands/PinsCommands.cs`
- Modify: `src/GrimoireCli/Program.cs`. Register `PinsCommand` after `CampaignsCommand.Create()`, named so that it matches the file's other entries.
- Test: `tests/GrimoireCli.Tests/Services/PinsServiceTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/PinsCommandTests.cs`

**Interfaces:**
- Produces:
  - `PinsService(GrimoireApiClient)`
  - `ListAsync(string? collection)`
  - `AddAsync(string collection, string target)`
  - `RemoveAsync(string pinId)`
  - the internal `ListRequest`, `AddRequest` and `RemoveRequest`, which return `RequestInformation`
- Generated builders:
  - `_client.Api.Api.Pins` (GET with `QueryParameters.Collection`; POST with `Generated.Models.PinIn { Collection, Target }`)
  - `_client.Api.Api.Pins[pinId]` (DELETE)

- [ ] **Step 1: Write the failing tests**

Service tests, in the style of `CampaignWikiServiceTests`:
- `list` with no collection is `GET http://example.test/api/pins`.
- `list` with `maps` is `GET …/api/pins?collection=maps`.
- `add` is `POST …/api/pins`.
- `remove` is `DELETE …/api/pins/k-1`.

Command tests:
- `pins add --collection books --target x` has parse errors.
- `pins add --collection systems --target x` parses clean.
- `pins add` without `--target` has errors.
- `pins remove` without `--pin-id` has errors.
- No `pins` verb's help contains `Role required:`.
- The help for `add` contains `100`.

- [ ] **Step 2: Run them and confirm they fail.** `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~Pins"`. The build fails.

- [ ] **Step 3: Implement.**
  - Group description: `"Your pinned folders and game systems"`.
  - `--collection` uses `OptionHelpers.Choice`.
  - Descriptions:
    - `list`: `"List your pins"`
    - `add`: `"Pin a folder or game system"`
    - `remove`: `"Unpin"`
  - `--target` description: `"A system id for systems; else a folder path in that collection"`.
  - Notes on `add`:
    ```
    Leading and trailing slashes are stripped. Pinning something already
    pinned returns the existing pin. A collection holds at most 100 pins;
    past that is a 400.
    ```
  - Notes on `list`: `"Oldest first."`
  - Notes on `remove`: `"Another user's pin is a 404, as a missing one is."`
  - Response samples:
    - `list`: `Generated.Models.PinsResponse`
    - `add`: `Generated.Models.PinOut`
    - `remove`: none, because it returns 204
  - Not-found hint on `remove`: `"No such pin. List yours with: grimoire-cli pins list"`.

- [ ] **Step 4: Format, build and test.**
  - Run `dotnet format GrimoireCli.sln`, then the full test suite.
  - If a root command-tree or help-snapshot test lists the top-level groups, add `pins` to it.
  - Render `dotnet run --project src/GrimoireCli -- pins add --help` and read the output.

- [ ] **Step 5: Commit.** `git commit -m "feat: add pins commands"`

---

### Task 2: GM screen board commands

**Files:**
- Create: `src/GrimoireCli/Services/CampaignGmScreenService.cs`
- Create: `src/GrimoireCli/Commands/CampaignGmScreenCommands.cs`
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs`. Register `CampaignGmScreenCommands.Create()` after `CampaignWikiCommands.Create()`.
- Test: `tests/GrimoireCli.Tests/Services/CampaignGmScreenServiceTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/CampaignGmScreenCommandTests.cs`

**Interfaces:**
- Produces:
  - `CampaignGmScreenCommands.Create()`, which returns the `gm-screen` `Command`. Task 3 adds `panels` to it inside this file.
  - `CampaignGmScreenService` with `GetAsync`, `PartyAsync`, `ExportAsync`, `SourcesAsync`, `PresetsAsync`, `ResetAsync(id, scope)`, `LayoutAsync(id, rawBody)`, `ImportAsync(id, documentJson, bool append)`, `CopyAsync(id, sourceId, bool append)` and `ApplyPresetAsync(id, presetId, string? indexUrl, bool append)`.
  - A matching internal `XxxRequest` for each, and `internal static string BuildImportBody(string documentJson, bool append)`.
- Generated builders sit under `_client.Api.Api.Campaigns[id].GmScreen`:
  - `.Party`, `.Export`, `.Sources`, `.Presets`, `.Presets.Apply`
  - `.Reset` (`GmResetRequest { Scope = Enum.Parse<GmResetRequest_scope>(…) }`; check the enum member names)
  - `.Layout` (raw body, PUT)
  - `.Import` (raw body)
  - `.Copy` (`GmCopyRequest { SourceCampaignId, Replace }`)
  - `.Presets.Apply` (`GmApplyPresetRequest`; `IndexUrl` is a composed wrapper, as in the categories service)

The import body wraps the user's document. A document that is not a JSON object is a `BodyInputException`, which the command logs before returning 1:

```csharp
internal static string BuildImportBody(string documentJson, bool append)
{
    var node = System.Text.Json.Nodes.JsonNode.Parse(documentJson) as System.Text.Json.Nodes.JsonObject
        ?? throw new BodyInputException("The board document must be a JSON object, as gm-screen export writes it.");
    return new System.Text.Json.Nodes.JsonObject { ["document"] = node, ["replace"] = !append }.ToJsonString();
}
```

Check `BodyInputException`'s namespace and constructor, and that `JsonNode.Parse` throws on malformed JSON. If it does, catch `System.Text.Json.JsonException` and rethrow it as `BodyInputException`.

- [ ] **Step 1: Write the failing tests.**
  - Service route theory. The base is `http://example.test/api/campaigns/c-1/gm-screen`.
    - `get`: `GET` on the base
    - `party`: `GET /party`
    - `export`: `GET /export`
    - `sources`: `GET /sources`
    - `presets`: `GET /presets`
    - `reset`: `POST /reset`
    - `layout`: `PUT /layout`
    - `import`: `POST /import`
    - `copy`: `POST /copy`
    - `apply-preset`: `POST /presets/apply`
  - `BuildImportBody("{\"panels\":[]}", false)` parses to `replace == true` and keeps `document.panels`. With `append: true` it gives `replace == false`.
  - `BuildImportBody("[1]", false)` throws `BodyInputException`.
  - Command tests:
    - `reset --scope battle` has errors, and `reset --scope rest` parses.
    - `copy` without `--source-id` has errors.
    - `apply-preset` without `--preset-id` has errors.
    - `import` with neither `--input` nor `--stdin` has errors.
    - No verb's help has `Role required:`.
    - `get`'s help contains `Only the campaign owner`.
    - `presets`' help contains `external installs`.

- [ ] **Step 2: Confirm the tests fail.**

- [ ] **Step 3: Implement.**
  - Option and group naming: `--id` is the campaign ID.
    - Group: `"gm-screen"`, `"The campaign GM's board of tools"`.
  - Descriptions:
    - `get`: `"Get the GM screen's panels"`
    - `party`: `"The party's characters and values for the GM screen"`
    - `export`: `"Export the board as a document import reads"`
    - `sources`: `"Other campaigns you run with a board to copy"`
    - `presets`: `"Browse community GM screen presets"`
    - `reset`: `"End a scene or session, or rest: clear marked panels"`
    - `layout`: `"Move and resize several panels at once"`
    - `import`: `"Put a board document on the GM screen"`
    - `copy`: `"Copy another campaign's board"`
    - `apply-preset`: `"Apply a community GM screen preset"`
  - Notes. Every command gets this line first:
    ```
    Only the campaign owner; anyone else gets 403.
    ```
    Every write (`reset`, `layout`, `import`, `copy`, `apply-preset`) adds the `Locked` wording, defined as a private array in this file:
    ```
    409 while the campaign is archived; 403 while the GM's campaign access
    is disabled.
    ```
    Then, per command:
    - `reset`:
      ```
      Clears what each panel is marked to clear on that scope (its resets);
      session also clears what clears with scene.
      ```
    - `layout`:
      ```
      Body {"panels":[{"id","x","y","w","h"}]}; an id not on the board is
      skipped.
      ```
      The request shape sample also renders. Keep the line only if it adds something the sample doesn't show; otherwise drop it.
    - `import`, `copy` and `apply-preset` share:
      ```
      Replaces the board unless --append, which adds the panels below it. A
      board holds at most 60 panels; over that is a 400.
      ```
    - `import` adds:
      ```
      Takes the document export writes; an empty panels list or an unknown
      kind is a 400.
      ```
    - `copy` adds:
      ```
      --source-id from sources. Copies the setup, not the values: counters
      back to their start, clocks unticked, initiative empty; notes match by
      page slug. A campaign you don't own, or this one, is a 404; an empty
      board a 400.
      ```
    - `presets` adds:
      ```
      403 when the server disables external installs.
      ```
    - `apply-preset` adds:
      ```
      --preset-id and --index-url from presets. 403 when the server disables
      external installs; 404 for an unknown preset; 502 when the download
      fails.
      ```
  - Response samples:
    - `get`, `reset`, `layout`, `import`, `copy`, `apply-preset`: `GmScreenOut`
    - `party`: `GmPartyOut`
    - `export`: `GmBoardExport`
    - `sources`: `GmSourcesOut`
    - `presets`: `GmPresetListOut`
  - Request shape: `layout` gets `AddRequestShape<GmLayoutUpdate>()`.
  - Not-found hint: `"No campaign with that ID. List them with: grimoire-cli campaigns list"`.
  - `layout` validates its body with `JsonBodyInput.Validate(body, Generated.Models.GmLayoutUpdate.CreateFromDiscriminatorValue, "pass it with --id")`.

- [ ] **Step 4: Format, build, test, and render `--help` for `get`, `reset`, `copy` and `apply-preset`.** If a command-tree test lists the `campaigns` subcommands, add `gm-screen` to it.

- [ ] **Step 5: Commit.** `git commit -m "feat: add campaigns gm-screen board commands"`

---

### Task 3: GM screen panels and the Panel kinds help

**Files:**
- Create: `src/GrimoireCli/Services/CampaignGmPanelsService.cs`
- Create: `src/GrimoireCli/Commands/CampaignGmPanelsCommands.cs`
- Create: `src/GrimoireCli/Commands/GmPanelHelp.cs`
- Modify: `src/GrimoireCli/Commands/CampaignGmScreenCommands.cs`. Add `command.Subcommands.Add(CampaignGmPanelsCommands.Create());` as the last subcommand.
- Test: `tests/GrimoireCli.Tests/Services/CampaignGmPanelsServiceTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/CampaignGmPanelsCommandTests.cs`

**Interfaces:**
- Consumes: `CampaignGmScreenCommands.Create()` from Task 2.
- Produces:
  - `CampaignGmPanelsService` with `CreateAsync(id, rawBody)`, `UpdateAsync(id, panelId, rawBody)` and `DeleteAsync(id, panelId)`
  - the internal `CreateRequest`, `UpdateRequest` and `DeleteRequest`
- Builders: `_client.Api.Api.Campaigns[id].GmScreen.Panels` (POST) and `.Panels[panelId]` (PATCH, DELETE). Send the raw body the way `CampaignWikiService.CreateRequest` does.

- [ ] **Step 1: Write the failing tests.**
  - Routes:
    - `create`: `POST …/gm-screen/panels`
    - `update`: `PATCH …/gm-screen/panels/k-1`
    - `delete`: `DELETE …/gm-screen/panels/k-1`
  - Command tests:
    - `panels update` without `--panel-id` has errors.
    - `panels create` with neither `--input` nor `--stdin` has errors.
    - No role tag.
    - The help for both `create` and `update` contains `initiative` and `silently become defaults`.
    - `update`'s help contains `replaces the panel's state whole`.

- [ ] **Step 2: Confirm the tests fail.**

- [ ] **Step 3: Implement.**

`GmPanelHelp.cs` follows the `WikiPageHelp` pattern. Keep its doc comment one line long and naming `services/gm_screen/panels.py` v1.11.0. Its section is titled `"Panel kinds"` and its lines are exactly these:

```csharp
command.AddHelpSection("Panel kinds", HelpSectionPosition.Top,
    "The grid is 12 columns; without x and y a panel takes the first gap it fits.",
    "Unknown keys and bad values in config and state silently become defaults;",
    "only an unknown kind is a 400. resets is scene, session, rest or \"\" (never).",
    "A row's id is kept when given, so a later update can address it.",
    "",
    "note       config page_id or page_slug: a page in this campaign (an unknown",
    "           page_id is a 404)",
    "party      config stats [{value, label, max, style number|pips}],",
    "           include_inactive; value names a field from party's fields, max",
    "           one too or a number",
    "counter    config min, max, start, style number|pips, resets; state value",
    "clocks     config mode countdown|clock; state items [{id, name, size,",
    "           ticks, note, resets, loops}]",
    "pools      config resets; state items [{id, name, tokens, note}]",
    "scratch    state text",
    "initiative config mode ordered|sides, sides [{key, label}], resets;",
    "           state round, turn, side, first, entries [{id, name, score, side,",
    "           acted, note, character_id}]",
    "reference  config sections [{id, title, text}], text in markdown",
    "table      config mode list|join, columns [{id, label, entries}];",
    "           state rolls, one 1-based entry number per column");
```

Commands:
- Group: `"panels"`, `"Panels on the GM screen"`.
- Options: `--id` is the campaign ID, and `--panel-id` is the panel ID, from `gm-screen get`.
- `create`: `"Add a panel to the GM screen"`. Its body is validated against `Generated.Models.GmPanelCreate`. It gets `AddRequestShape<GmPanelCreate>()` and `AddResponseExample<GmPanelOut>()`.
- `update`: `"Update a GM screen panel"`. Its body is validated against `GmPanelUpdate`. It gets a request shape and `GmPanelOut`.
- `delete`: `"Remove a GM screen panel"`. Its response is `GmScreenOut`.
- `create` and `update` call `GmPanelHelp.AddTo(command)`.
- Notes:
  - All three:
    ```
    Only the campaign owner; anyone else gets 403.
    409 while the campaign is archived; 403 while the GM's campaign access
    is disabled.
    ```
  - `create` adds: `A board holds at most 60 panels.`
  - `update` adds:
    ```
    Only the fields given change. state replaces the panel's state whole; a
    new config re-cleans the state against it. An unknown panel id is a 404.
    ```
  - `delete` adds: `An unknown panel id is a 404.`
- Example on `create`:
  ```
  echo '{"kind":"counter","title":"Fear","config":{"min":0,"max":12,"resets":"session"}}' | grimoire-cli campaigns gm-screen panels create --id <campaign-id> --stdin
  ```
- Not-found hints:
  - `create`: the campaign hint, as in Task 2
  - `update` and `delete`: `"No such campaign or panel. List panels with: grimoire-cli campaigns gm-screen get --id <campaign-id>"`

- [ ] **Step 4: Format, build and test, then render the `--help` for `panels create` and `panels update`.** Check that the Panel kinds table lines up in monospace.

- [ ] **Step 5: Commit.** `git commit -m "feat: add campaigns gm-screen panels commands"`

---

### Task 4: Docs and smoke test

**Files:**
- Modify: `README.md`, the Commands table:
  - `pins` rows after the last `campaigns …` row group, or wherever top-level groups sit in the order of `Program.cs`. Match the table's existing order.
  - `campaigns gm-screen` rows after the `campaigns wiki pins` rows.
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docker/smoke-test.sh`

- [ ] **Step 1: README.** Add one row per command, with flags in the existing style. Take the exact flag spelling from `--help` output for:
  - `pins list|add|remove`
  - `campaigns gm-screen get|party|export|sources|presets|reset|layout|import|copy|apply-preset`
  - `campaigns gm-screen panels create|update|delete`

  Each row's description is the command's own description.

- [ ] **Step 2: Coverage.** Add 15 `IMPLEMENTED` entries, in the style of their neighbours:
  - `GET /api/pins`, `POST /api/pins`, `DELETE /api/pins/{pin_id}`
  - 12 routes under `/api/campaigns/{campaign_id}/gm-screen`. Read the exact paths from `docs/grimoire-api-coverage.md`.

  Run `python3 tools/generate-api-coverage.py`, which needs the running stack. Expected: `270 covered`.

- [ ] **Step 3: API notes.** Add a `## Pins` section and a `## GM screen` section to `docs/grimoire-api-notes.md`. Place each next to the related sections, and write them in the style of the file's existing bullets: bold claim first, then the cite with the version, e.g. `(core.py:43-75, v1.11.0)`. Cover the behaviour in the spec's "Help must carry" lists and the pins bullets, using the spec's cites. Also add:
  - Panel `config` and `state` are cleaned forgivingly. Only an unknown kind is refused (`services/gm_screen/panels.py:1-21`).
  - `export` drops note `page_id` and keeps the slug (`services/gm_screen/boards.py` `export_board`).
  - A board document is `{format: "grimoire://gm-screen/v1", name, panels}`, and only `panels` is read (`boards.py`).

  Check each cite against `temp/grimoire/backend/` before writing it.

- [ ] **Step 4: Smoke test.** Add two blocks to `docker/smoke-test.sh`.
  - **Pins:** put this right after the block that sets `SR4` (around line 642, "listing systems to find Shadowrun 4 DE's id"), or in a later systems-related spot where `$SR4` is still set. Wherever it goes, it must not break any later assertion.
    1. `pins add --collection systems --target "$SR4"` twice. Assert both `.id` values are equal and non-null.
    2. `pins list --collection systems`. Assert it holds a pin with that id and `target == $SR4`.
    3. `pins remove --pin-id`. Then `list` must not hold it.
  - **GM screen:** put this inside the campaign wiki block, which has `$WIKI` set to the Smoke Wiki GM campaign, after the `campaigns wiki pins` block and before `# exporting the wiki as a JSON bundle…`.
    1. Delete every panel in `gm-screen get --id "$WIKI"`, then assert `.panels == []`.
    2. `panels create` with the body `{"kind":"counter","title":"Smoke Fear","config":{"min":0,"max":10,"start":2,"resets":"scene"}}`, and capture `.id`.
    3. `panels update` with `{"state":{"value":7}}`. Assert `.state.value == 7`.
    4. `reset --scope scene`. Assert that the panel's `.state.value == 2`.
    5. `export` to `$WORK/gm-board.json`, then `import --input "$WORK/gm-board.json" --append`. Assert `.panels | length == 2`.
    6. Delete both panels, then assert `.panels == []`.

  Use the script's existing `ok` / `fail` / `$WORK/cli.err` idioms. Check that new variable names don't clash with existing ones.

- [ ] **Step 5: Verify.**
  ```bash
  dotnet format GrimoireCli.sln --verify-no-changes
  dotnet build GrimoireCli.sln
  dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
  bash docker/smoke-test.sh
  bash docker/smoke-test.sh
  ```
  All must pass, and the second smoke-test run shows that a re-run converges.

- [ ] **Step 6: Commit.** `git commit -m "docs: cover pins and the gm screen"`
