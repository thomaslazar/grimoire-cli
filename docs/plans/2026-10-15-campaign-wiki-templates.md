# Campaign wiki note templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `campaigns wiki templates list|get|create|update|delete|import|export|use`. The spec is `docs/specs/2026-10-15-campaign-wiki-templates-design.md`.

**Architecture:**
- A new command file and a new service, one per command group.
- Each command maps to one route.
- The patterns to copy all live in `CampaignWikiCommands.cs` / `CampaignWikiService.cs`:
  - JSON bodies: `create`/`update`.
  - Bytes out: `export`.
  - Multipart in: `import`.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, and a bash smoke test.

## Global Constraints

- Branch `feat/campaign-wiki-templates`. Conventional Commits, and **no `Co-Authored-By:` and no "Generated with" lines** in any commit.
- Run `dotnet format GrimoireCli.sln` after editing C#. Keep blank lines out of method bodies.
- No role tag and no `permissionHint`: every route is gated on campaign ownership (`assert_can_manage`), not on a role.
- Help text is terse. Don't restate flag descriptions, response fields or the request/response shapes the help already renders. Check the rendered `--help`.
- The local stack is at `http://host.docker.internal:9481`, with logins `admin/admin`, `gm/gm`, `player/player`. Write only to the local stack.

---

### Task 1: Service, commands, unit tests, README, coverage

**Files:**
- Create: `src/GrimoireCli/Services/CampaignWikiTemplateService.cs`
- Create: `src/GrimoireCli/Commands/CampaignWikiTemplateCommands.cs`
- Create: `tests/GrimoireCli.Tests/Services/CampaignWikiTemplateServiceTests.cs`
- Create: `tests/GrimoireCli.Tests/Commands/CampaignWikiTemplateCommandTests.cs`
- Modify: `src/GrimoireCli/Commands/CampaignWikiCommands.cs`, to register the group: `command.Subcommands.Add(CampaignWikiTemplateCommands.Create());` as the last line of `Create()` before `return`.
- Modify: `README.md`, adding 8 rows after the `campaigns wiki import` row.
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`.

**Interfaces:**
- Produces `CampaignWikiTemplateService(GrimoireApiClient client)`. Every public method has an `internal` `…Request` builder beside it, the same as `CampaignWikiService`.
- Public methods:
  - `ListAsync(string campaignId) : Task<string>`
  - `GetAsync(string campaignId, string templateId) : Task<string>`
  - `CreateAsync(string campaignId, string rawBody) : Task<string>`
  - `UpdateAsync(string campaignId, string templateId, string rawBody) : Task<string>`
  - `DeleteAsync(string campaignId, string templateId) : Task<string>`
  - `ImportAsync(string campaignId, string filePath) : Task<string>`. It reads the file, turning a read error into `BodyInputException("Could not read {path}: {msg}")` exactly as `CampaignWikiService.ImportAsync` does.
  - `ExportAsync(string campaignId, string templateId) : Task<Stream>`, through `SendStreamAsync`.
  - `UseAsync(string campaignId, string templateId) : Task<string>`
- Internal builders, with these signatures:
  - `ListRequest(c)`, `GetRequest(c, t)`, `CreateRequest(c, rawBody)`, `UpdateRequest(c, t, rawBody)`, `DeleteRequest(c, t)`, `UseRequest(c, t)`, `ExportRequest(c, t)`
  - `ImportRequest(string campaignId, byte[] bytes, string fileName)`
- Generated builders: `_client.Api.Api.Campaigns[c].Wiki.Templates`, `.Templates[t]`, `.Templates.Upload`, `.Templates[t].Export`, `.Templates[t].Use`. Check the exact indexer names in `src/GrimoireCli/Generated/Api/Campaigns/Item/Wiki/Templates/`.
- Not-found hints:
  - Campaign-level routes (`list`, `create`, `import`): `"No campaign with that ID. List them with: grimoire-cli campaigns list"`.
  - Per-template routes: `"No such campaign or template. List templates with: grimoire-cli campaigns wiki templates list --id <campaign-id>"`.
- Raw JSON bodies are sent the way `CampaignWikiService.CreateRequest` does it: build with `new Generated.Models.WikiTemplateInput()` / `WikiTemplateUpdate()`, then `SetStreamContent` the raw bytes as `application/json`.
- The multipart part is `file`, `application/octet-stream`, with the local basename as its filename.

- [ ] **Step 1: Write the failing service tests.** Copy the structure of `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs`: a `Routes()` theory pinning method and URI, plus the multipart fact. With `Base = "http://example.test/api/campaigns/c-1/wiki/templates"`:
  - list: `GET Base`
  - get: `GET Base/t-1`
  - create: `POST Base`
  - update: `PATCH Base/t-1`
  - delete: `DELETE Base/t-1`
  - import: `POST Base/upload`
  - export: `GET Base/t-1/export`
  - use: `POST Base/t-1/use`

  Also add `CreateSendsTheRawBodyUnchanged`, mirroring the wiki test, and `ImportSendsOneFilePartNamedAfterTheLocalFile`, which uses `"npc.zip"` and asserts `name="file"` and `filename="npc.zip"` in the content.
- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter CampaignWikiTemplateServiceTests`.
- [ ] **Step 3: Implement the service.** The class summary reads: "The eight routes behind `campaigns wiki templates`: a campaign's note templates (routers/campaigns/wiki_templates.py). Every route is owner-only, checked by the server, so no permission hint is passed."
- [ ] **Step 4: Run the service tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.** Copy the structure of `CampaignWikiCommandTests.cs`, with `Parse` going through `CampaignsCommand.Create().Parse(["wiki", "templates", .. args])` and `Help` rendering `["campaigns", "wiki", "templates", verb]`.
  - The `templates` subgroup holds exactly `list get create update delete import export use`.
  - `get`, `update`, `delete`, `export` and `use` each fail to parse without `--template-id`.
  - `export` fails without `--output`.
  - `create` fails with neither `--input` nor `--stdin`.
  - `import` fails without `--file`.
  - Help assertions: `update` contains `replaces all five`; `use` contains `session_date is never set`; `import` contains `Max 512 KB`.
- [ ] **Step 6: Implement the commands** in `CampaignWikiTemplateCommands.cs`:
  - Group: `new Command("templates", "A campaign's note templates for new wiki pages")`.
  - Options: `CampaignIdOption()` (`--id`, "Campaign ID") and `TemplateIdOption()` (`--template-id`, "Note template ID"), as private helpers in this file.
  - Every command gets this Notes section first (`[.. OwnerOnly, "", …]`), with `private static readonly string[] OwnerOnly = ["Campaign owner only; refused while the campaign is archived."];`.
  - Every command adds `AddResponseExample<…>` for the generated model (`WikiTemplateListOut`, `WikiTemplateDetailOut`, `WikiTemplateUseOut`, or `SavedFile` for export). `create`/`update` also add `AddRequestShape<WikiTemplateInput|WikiTemplateUpdate>()`. `delete` prints nothing on 204, the same as `wiki delete`.
  - Descriptions, and the Notes text after `OwnerOnly`, are below. Lines are verbatim; leave the Notes empty apart from `OwnerOnly` where none is given.
    - `list`: "List the campaign's note templates".
    - `get`: "Get one note template with its body".
      - Notes: `body comes back without its frontmatter; those fields are in defaults.`
    - `create`: "Write a new note template".
      - Notes:
        ```
        defaults are the starting fields of a page made by use. Omitted, any
        frontmatter already in body is kept. An unknown visibility or page_type
        is stored as gm or note.

        body is a wiki page body; its syntax is in campaigns wiki create --help.
        ```
    - `update`: "Edit a note template".
      - Notes:
        ```
        Send only the fields to change. A sent defaults replaces all five
        fields: any left out reset (visibility gm, page_type note, the rest
        empty). Sending only body keeps the current defaults. An unknown
        visibility or page_type is stored as gm or note.
        ```
    - `delete`: "Delete a note template". No extra notes.
    - `import`: "Add a note template from a .md file or template .zip".
      - Notes:
        ```
        Reads .md/.markdown/.txt, or a .zip holding one .md and an optional .yml
        manifest — what export writes. The name comes from the manifest, else
        the frontmatter title, else the first # heading, else the file name.
        Max 512 KB.
        ```
    - `export`: "Export a note template as a .zip".
      - Notes:
        ```
        The zip holds <slug>/<slug>.yml and <slug>/<slug>.md.

        --output - writes the zip to stdout; a path writes the file and prints
        {path, bytes}.
        ```
    - `use`: "Create a wiki page from a note template".
      - Notes:
        ```
        Every use creates a new page; nothing is overwritten. Title: defaults
        title, else the body's first # heading, else the template name; a
        repeated title gets a suffixed slug. session_date is never set, even
        for a session page. Unresolved [[links]] get stubs, as on create.
        Rename the page with campaigns wiki update.
        ```
  - Examples:
    - `create`: `echo '{"name":"NPC","category":"Characters","body":"## Appearance\n\n## Motives","defaults":{"title":"New NPC","visibility":"gm"}}' | grimoire-cli campaigns wiki templates create --id <campaign-id> --stdin`
    - `update`: `echo '{"description":"Stat-free NPC sheet"}' | grimoire-cli campaigns wiki templates update --id <campaign-id> --template-id <template-id> --stdin`
    - `import`: `grimoire-cli campaigns wiki templates import --id <campaign-id> --file npc.zip`
    - `export`: `grimoire-cli campaigns wiki templates export --id <campaign-id> --template-id <template-id> --output npc.zip`
    - `use`: `grimoire-cli campaigns wiki templates use --id <campaign-id> --template-id <template-id>`
    - Every other command gets one plain invocation example.
  - Validator hint strings for `JsonBodyInput.Validate`: `"pass it with --id"` for create, and `"pass it with --template-id"` for update.
- [ ] **Step 7: Format, run the full test suite, and read the rendered help.**
  - `dotnet format GrimoireCli.sln`
  - `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`: everything passes, including the help-completeness tests.
  - `dotnet run --project src/GrimoireCli -- campaigns wiki templates <verb> --help` for every verb. Read each one.
- [ ] **Step 8: README and coverage.**
  - Add these README rows after `campaigns wiki import`:
    ```
    | `campaigns wiki templates list --id <campaign-id>` | List the campaign's note templates |
    | `campaigns wiki templates get --id <campaign-id> --template-id <template-id>` | Get one note template with its body |
    | `campaigns wiki templates create --id <campaign-id> {--input <file> \| --stdin}` | Write a new note template |
    | `campaigns wiki templates update --id <campaign-id> --template-id <template-id> {--input <file> \| --stdin}` | Edit a note template |
    | `campaigns wiki templates delete --id <campaign-id> --template-id <template-id>` | Delete a note template |
    | `campaigns wiki templates import --id <campaign-id> --file <path>` | Add a note template from a .md file or template .zip |
    | `campaigns wiki templates export --id <campaign-id> --template-id <template-id> --output <path\|->` | Export a note template as a .zip |
    | `campaigns wiki templates use --id <campaign-id> --template-id <template-id>` | Create a wiki page from a note template |
    ```
  - Add these `IMPLEMENTED` entries; the keys must match the spec paths exactly:
    ```python
    "GET /api/campaigns/{campaign_id}/wiki/templates": "`campaigns wiki templates list` ✅",
    "POST /api/campaigns/{campaign_id}/wiki/templates": "`campaigns wiki templates create` ✅",
    "POST /api/campaigns/{campaign_id}/wiki/templates/upload": "`campaigns wiki templates import` ✅",
    "GET /api/campaigns/{campaign_id}/wiki/templates/{template_id}": "`campaigns wiki templates get` ✅",
    "PATCH /api/campaigns/{campaign_id}/wiki/templates/{template_id}": "`campaigns wiki templates update` ✅",
    "DELETE /api/campaigns/{campaign_id}/wiki/templates/{template_id}": "`campaigns wiki templates delete` ✅",
    "GET /api/campaigns/{campaign_id}/wiki/templates/{template_id}/export": "`campaigns wiki templates export` ✅",
    "POST /api/campaigns/{campaign_id}/wiki/templates/{template_id}/use": "`campaigns wiki templates use` ✅",
    ```
  - Regenerate with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py` (check the script header for the exact invocation). The total goes from 187 to 195.
- [ ] **Step 9: Commit**
  ```bash
  git add src tests README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md
  git commit -m "feat: add campaigns wiki templates"
  ```

### Task 2: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`, in the `campaign wiki` section, after the `campaigns wiki import adds pages from a bundle` check and before `# deleting the remaining pages expecting an empty wiki again`.

**Interfaces:** Consumes the Task 1 commands. `$WIKI` is the Smoke Wiki campaign id, already set in the section, and the smoke user owns it.

- [ ] **Step 1: Add the checks.** Follow the section's style: a `# …` narration line before each step, stderr to `"$WORK/cli.err"`, and `fail`/`ok`.

```bash
# clearing templates an interrupted run left behind, expecting none
for t in $("$CLI" campaigns wiki templates list --id "$WIKI" | jq -r '.templates[].id'); do
  "$CLI" campaigns wiki templates delete --id "$WIKI" --template-id "$t" >/dev/null 2>&1 || true
done
"$CLI" campaigns wiki templates list --id "$WIKI" | jq -e '.templates | length == 0' >/dev/null \
  || fail "the Smoke Wiki should have no templates before the template checks"
# creating a template with page defaults expecting them split out of the body
TPL=$(echo '{"name":"Smoke NPC","body":"## Motives","defaults":{"title":"Smoke Villain","visibility":"group"}}' \
  | "$CLI" campaigns wiki templates create --id "$WIKI" --stdin 2>"$WORK/cli.err" | jq -r .id) \
  || { cat "$WORK/cli.err" >&2; fail "wiki templates create exited non-zero"; }
"$CLI" campaigns wiki templates get --id "$WIKI" --template-id "$TPL" \
  | jq -e '.body == "## Motives" and .defaults.title == "Smoke Villain" and .defaults.visibility == "group"' >/dev/null \
  || fail "wiki templates get should return the body and its defaults apart"
# updating only the body expecting the defaults kept
echo '{"body":"## Motives\n\n## Secrets"}' | "$CLI" campaigns wiki templates update --id "$WIKI" --template-id "$TPL" --stdin \
  | jq -e '.defaults.title == "Smoke Villain"' >/dev/null \
  || fail "a body-only template update should keep the defaults"
ok "campaigns wiki templates create, get and update"

# using the template expecting a page carrying its defaults
"$CLI" campaigns wiki templates use --id "$WIKI" --template-id "$TPL" 2>"$WORK/cli.err" \
  | jq -e '.pages[0].title == "Smoke Villain" and .pages[0].visibility == "group"' >/dev/null \
  || { cat "$WORK/cli.err" >&2; fail "wiki templates use should create a page from the defaults"; }
ok "campaigns wiki templates use creates a page"

# exporting the template and importing the zip back expecting its name kept
"$CLI" campaigns wiki templates export --id "$WIKI" --template-id "$TPL" --output "$WORK/tpl.zip" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki templates export exited non-zero"; }
TPL2=$("$CLI" campaigns wiki templates import --id "$WIKI" --file "$WORK/tpl.zip" 2>"$WORK/cli.err" \
  | jq -r 'select(.name == "Smoke NPC") | .id') \
  || { cat "$WORK/cli.err" >&2; fail "wiki templates import exited non-zero"; }
[ -n "$TPL2" ] || fail "the imported template should keep the name Smoke NPC"
# deleting both templates expecting none left
"$CLI" campaigns wiki templates delete --id "$WIKI" --template-id "$TPL" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki templates delete exited non-zero"; }
"$CLI" campaigns wiki templates delete --id "$WIKI" --template-id "$TPL2" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "wiki templates delete (import) exited non-zero"; }
"$CLI" campaigns wiki templates list --id "$WIKI" | jq -e '.templates | length == 0' >/dev/null \
  || fail "no templates should remain after the template checks"
ok "campaigns wiki templates export, import and delete"
```

  The page `use` created is removed by the section's existing delete-all cleanup that follows, so a re-run converges.

- [ ] **Step 2: Run the smoke test against the local stack, twice.** Run `bash docker/smoke-test.sh`; its defaults point at `http://host.docker.internal:9481` and the Debug build. Build first with `dotnet build GrimoireCli.sln`. Both runs must pass.
- [ ] **Step 3: Commit**
  ```bash
  git add docker/smoke-test.sh
  git commit -m "test: cover campaigns wiki templates in the smoke test"
  ```
