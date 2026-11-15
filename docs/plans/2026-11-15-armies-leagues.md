# Armies and Leagues Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wrap Grimoire 1.11.0's wargame routes: armies (14), leagues (23) and `GET /api/systems/{id}/factions`.

**Architecture:** Each command group has one command file and one service. Every command is a thin pass-through to a single endpoint. Bodies are JSON (`--input`/`--stdin`) where they nest, and flags where they are a handful of scalars. Docs and the smoke test come last.

**Tech Stack:** C#/.NET 10, System.CommandLine, the Kiota client in `src/GrimoireCli/Generated/` (never edit it), xUnit, and bash/jq for the smoke test.

**Spec (binding):** `docs/specs/2026-11-15-armies-leagues-design.md`.
- The command table gives the exact names, flags and tags.
- "Behaviour the help must carry" lists the Notes each command needs. Each item names its command(s) and cites the server source.
- Write the Notes tersely in the house style (see `CLAUDE.md` "Help text"). Every claim must match the spec. Verify any wording you are unsure of against `temp/grimoire/backend/` (v1.11.0).

## Global Constraints

- **Role tags.** `AddRoleRequired("gm or admin")` with permissionHint `"the gm or admin role"` goes only on:
  - `leagues create`
  - `armies publication-links set`, `remove` and `books`

  No other command takes a tag or a permissionHint.
- **Organiser/owner rules** are enforced in the handler. They belong in Notes, never in a tag.
- **Verbs and flags** are exactly as in the spec's command table.
- **Choices:**
  - `--format`: `text|roster`, default `text`
  - `--visibility`: `public|private|organizer`
- **JSON bodies** (`--input`/`--stdin`, exactly one, via `JsonBodyInput.RequireExactlyOneSource`) are validated against the generated model:
  - `armies create` → `ArmyCreate`
  - `armies update` → `ArmyUpdate`
  - `leagues create` → `LeagueCreate`
  - `leagues update` → `LeagueUpdate`
  - `leagues games create` and `update` → `GameIn`

  Check the generated model names in `src/GrimoireCli/Generated/Models/`. Name collisions may give names like `Backend__routers__…`.
- **Help** is terse. It must not repeat a choice's values, or what the request/response sample shows.
- **One file and one service per command group.** No shared tables between groups.
- The version floor stays at 1.10.1.
- **Commits** use Conventional Commits, with no `Co-Authored-By` and no "Generated with" lines.
- Run `dotnet format GrimoireCli.sln` after editing C#.
- The smoke test stays idempotent and writes only to the local stack.

## Patterns to copy (read before writing)

- **Flag-bodied commands and a service with `Build*Body` helpers:** `CampaignCategoriesCommands.cs` and `CampaignCategoriesService.cs`.
- **JSON bodies, internal `XxxRequest` builders and raw-body sending:** `CampaignWikiCommands.cs` and `CampaignWikiService.cs` (`CreateRequest` / `UpdateRequest`). Also `CampaignsCommand.cs` `create`.
- **Route tests:** `tests/GrimoireCli.Tests/Services/CampaignWikiServiceTests.cs`, a method + URL theory.
- **Parse and help tests:** `tests/GrimoireCli.Tests/Commands/CampaignCategoriesCommandTests.cs`.
- **Role tag plus hint:** `BookLinksCommands.cs` / `BookLinksService.cs`.
- **File upload:** `CampaignWikiService.ImportAsync` (`SendUploadAsync`, `FileUpload.Open`) and `CampaignWikiCommands` `import`. Armies import also sends an optional `name` form field. Find how an existing upload adds an extra form field (e.g. grep `SendUploadAsync` callers) and follow it.
- **Binary export to `--output <path|->`:** `CampaignWikiCommands` `export` and `CampaignWikiService.ExportAsync` (`SendStreamAsync`).
- **Group shapes to mirror:**
  - `CampaignGuestsCommands.cs` (guests)
  - `CampaignResourcesCommands.cs` (books)
  - `CampaignMembersCommands.cs` (members, eligible)
- **`systems` subcommand registration:** `SystemsCommand.cs` and `SystemsService.cs`.

---

### Task 1: Armies and system factions

**Files:**
- Create: `src/GrimoireCli/Commands/ArmiesCommand.cs`
- Create: `src/GrimoireCli/Services/ArmiesService.cs`
- Modify: `src/GrimoireCli/Program.cs`. Register `ArmiesCommand.Create()` after `CampaignsCommand.Create()` (before `PinsCommand` if it sits there; keep related groups together).
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs` (`factions`) and `src/GrimoireCli/Services/SystemsService.cs` (`FactionsAsync`)
- Test: `tests/GrimoireCli.Tests/Services/ArmiesServiceTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/ArmiesCommandTests.cs`
- Test: add the `factions` route and help tests to the existing systems tests

**Commands:** `armies list|get|create|update|delete|import|export|publications|progress|games|factions`, and `systems factions --id`, as in the spec table. Task 2 adds `publication-links` to `armies`. Leave `ArmiesCommand.Create()` so that a single `command.Subcommands.Add(ArmyPublicationLinksCommands.Create());` line can be added.

**Bodies and requests:**
- `progress` is flags:
  - `--experience` (int, default 0)
  - `--honours` (string[], zero or more)
  - `--notes`

  It sends the generated `UnitProgress` model, with every field always sent: the PUT replaces the whole entry. Add `internal static … BuildProgressBody(int experience, string[] honours, string? notes)`.
- `export`: `--format` is a choice (`text|roster`). `--output` is required, with `-` meaning stdout, as wiki export does.
- `import`: `--file` (required, must exist) and `--name` (optional form field).
- Builders, under `_client.Api.Api`:
  - `Armies`
  - `Armies[id]`
  - `Armies[id].Export`
  - `Armies[id].Publications`
  - `Armies[id].Units[unitId].Progress`
  - `Armies.Games`
  - `Armies.Games[systemId].Factions`
  - `Armies.Import`
  - `Systems[id].Factions`

  Check the real names under `src/GrimoireCli/Generated/Api/Armies/` and `Generated/Api/Systems/Item/`.

**Help:** use the spec's "Armies" and "System factions" bullets for each command. Response samples:
- `list`: `ArmySummary` array
- `get`, `create`, `update`, `import`: `ArmyDetail`
- `publications`: `ArmyPublication` array
- `progress`: `UnitProgress`
- `games`: `ArmyGame` array
- `factions`: `ArmyFactionsResponse`
- `systems factions`: its generated response model
- `delete`, `export`: none

`create` and `update` also get `AddRequestShape<…>()`. Examples:
- `create`: `echo '{"name":"Iron Warriors","points_limit":1000,"units":[{"id":"u1","name":"Warsmith","points":120}]}' | grimoire-cli armies create --stdin`
- `progress`: `grimoire-cli armies progress --id <army-id> --unit-id <unit-id> --experience 6 --honours "Slayer of Kings"`

- [ ] **Step 1: Write the failing tests.**
  - Route theory, with base `http://example.test/api/armies`:
    - `list`: `GET` on the base
    - `get`: `GET /a-1`
    - `create`: `POST` on the base
    - `update`: `PATCH /a-1`
    - `delete`: `DELETE /a-1`
    - `import`: `POST /import`
    - `export`: `GET /a-1/export?format=roster`
    - `publications`: `GET /a-1/publications`
    - `progress`: `PUT /a-1/units/u-1/progress`
    - `games`: `GET /games`
    - `factions`: `GET /games/s-1/factions`
    - plus systems factions: `GET http://example.test/api/systems/s-1/factions`
  - `BuildProgressBody(0, [], null)` carries experience 0, an empty honours list and notes `""` (or null, if the model serialises a default; assert what is actually sent).
  - Command tests:
    - `export --format pdf` has errors.
    - `export` without `--output` has errors.
    - `progress` without `--unit-id` has errors.
    - `create` with neither input source has errors.
    - No armies verb has `Role required:`.
    - `update` help contains `imported`.
    - `progress` help contains `reset`.
- [ ] **Step 2: Confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Format, build and test.** Render `--help` for `armies update`, `progress`, `import`, `export` and `systems factions`. If a root command-tree or help-snapshot test lists the groups, add `armies`.
- [ ] **Step 5: Commit.** `git commit -m "feat: add armies commands and systems factions"`

---

### Task 2: Army publication links

**Files:**
- Create: `src/GrimoireCli/Commands/ArmyPublicationLinksCommands.cs`
- Create: `src/GrimoireCli/Services/ArmyPublicationLinksService.cs`
- Modify: `ArmiesCommand.cs`. Add the `publication-links` group as the last subcommand.
- Test: `tests/GrimoireCli.Tests/Services/ArmyPublicationLinksServiceTests.cs`
- Test: `tests/GrimoireCli.Tests/Commands/ArmyPublicationLinksCommandTests.cs`

**Commands:**
- `set --publication --book-id [--system-name] [--page-offset]`. The body is the generated `PublicationLinkIn`, built by `BuildSetBody`.
- `remove --publication [--system-name]`. These are **query parameters**, not a body.
- `books --query`, sent as query `q`.

All three carry `AddRoleRequired("gm or admin")`, and the service passes `permissionHint: "the gm or admin role"`.

**Help:** use the spec's "Publication links" bullets. `--publication` is the name as the roster cites it, from `armies publications`. `--system-name` is the roster's game-system name, `system_name` in `armies get`. Response samples: `set` → `PublicationLinkOut`, `books` → `BookMatch` array.

- [ ] **Step 1: Write the failing tests.**
  - `set`: `PUT http://example.test/api/armies/publication-links`
  - `remove` with publication `Codex: Space Marines` and system name `Warhammer 40,000`: `DELETE` with both query params, URL-encoded. Assert the parsed query values rather than the raw string if encoding varies.
  - `books`: `GET …/publication-links/books?q=codex`
  - `BuildSetBody` carries `page_offset` 0 by default.
  - Each verb's help contains `Role required: gm or admin`.
  - `books` without `--query` has errors.
- [ ] **Step 2: Confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Format, build, test, and render help.**
- [ ] **Step 5: Commit.** `git commit -m "feat: add armies publication-links commands"`

---

### Task 3: Leagues and members

**Files:**
- Create: `src/GrimoireCli/Commands/LeaguesCommand.cs`
- Create: `src/GrimoireCli/Services/LeaguesService.cs`
- Create: `src/GrimoireCli/Commands/LeagueMembersCommands.cs`
- Create: `src/GrimoireCli/Services/LeagueMembersService.cs`
- Modify: `Program.cs`. Register `LeaguesCommand.Create()` right after `ArmiesCommand.Create()`.
- Test: `LeaguesServiceTests.cs`, `LeaguesCommandTests.cs`, `LeagueMembersServiceTests.cs`, `LeagueMembersCommandTests.cs`

**Commands:**
- `leagues list|get|create|update|delete|eligible`
  - `create` and `update` take JSON bodies.
  - `create` carries the gm-or-admin tag and hint.
- `leagues members add --user-id`, `remove --user-id` and `army --user-id [--army-id]`.
  - `army` sends `{army_id}` with the generated `MemberArmy` model. Omitting `--army-id` sends null, which clears it.

`LeaguesCommand.Create()` registers `members`. Tasks 4 and 5 add `games`, `guests` and `books` as the last subcommands.

**Help:** use the spec's "Leagues" and "Members" bullets. Response samples:
- `list`: `LeagueSummary` array
- `get`, `create`, `update`: the league detail model (check its name)
- `members add`: the detail model
- `members army`: what the route returns

Read the route's `response_model` in `temp/grimoire/backend/routers/leagues/__init__.py`. `eligible` returns the eligible-member model array.

Example for `create`: `echo '{"name":"Autumn Escalation","points_limit":1500}' | grimoire-cli leagues create --stdin`.

- [ ] **Step 1: Write the failing tests.**
  - Routes, with base `http://example.test/api/leagues`:
    - `list`: `GET` on the base
    - `get`: `GET /l-1`
    - `create`: `POST` on the base
    - `update`: `PATCH /l-1`
    - `delete`: `DELETE /l-1`
    - `eligible`: `GET /l-1/eligible-members`
    - `members add`: `POST /l-1/members`
    - `members remove`: `DELETE /l-1/members/u-1`
    - `members army`: `PUT /l-1/members/u-1/army`
  - The `army` body with no `--army-id` has a null `army_id`.
  - The role tag appears on `create` only.
  - `members add` without `--user-id` has errors.
  - `update` help contains `label` (the finished status).
  - `members army` help contains `not theirs` or equivalent wording about ownership. Pick wording, then assert it.
- [ ] **Step 2: Confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Format, build, test, and render help for `create`, `update`, `members remove` and `members army`.** Add `leagues` to any root command-tree test.
- [ ] **Step 5: Commit.** `git commit -m "feat: add leagues and leagues members commands"`

---

### Task 4: League games and guests

**Files:**
- Create: `LeagueGamesCommands.cs` and `LeagueGamesService.cs`
- Create: `LeagueGuestsCommands.cs` and `LeagueGuestsService.cs`
- Modify: `LeaguesCommand.cs`. Add `games`, then `guests`.
- Tests: service and command tests for both groups.

**Commands:**
- `leagues games create --id {--input|--stdin}`, `update --id --game-id {--input|--stdin}` and `delete --id --game-id`.
  - `create` and `update` bodies are validated against `GameIn`.
- `leagues guests list --id`, `create --id [--nickname]`, `regenerate --id --user-id`, `share --id --user-id` and `remove --id --user-id`.
  - `create` sends the generated league `GuestCreate` model. Name collisions may give it a `Backend__routers__leagues___schemas__GuestCreate`-style name.

**Help:** use the spec's "Games" and "Guests" bullets.
- The `guests remove` Notes must say the account stays, which differs from campaigns.
- The `games update` Notes must say it replaces the whole game.

Example for `games create`:

```
echo '{"player_a_id":"<user-id>","player_b_id":"<user-id>","score_a":12,"score_b":8,"played_on":"2026-11-14"}' | grimoire-cli leagues games create --id <league-id> --stdin
```

Player ids come from `leagues get` (`members[].user_id`). Say that once, in the `games create` Notes.

- [ ] **Step 1: Write the failing tests.**
  - Routes:
    - `games create`: `POST /l-1/games`
    - `games update`: `PUT /l-1/games/g-1`
    - `games delete`: `DELETE /l-1/games/g-1`
    - `guests list`: `GET /l-1/guests`
    - `guests create`: `POST /l-1/guests`
    - `guests regenerate`: `POST /l-1/guests/u-1/regenerate`
    - `guests share`: `GET /l-1/guests/u-1/share`
    - `guests remove`: `DELETE /l-1/guests/u-1`
  - No role tags.
  - `games update` help contains `replaces`.
  - `guests remove` help contains `account`.
  - `games update` without `--game-id` has errors.
- [ ] **Step 2: Confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Format, build, test, and render help.**
- [ ] **Step 5: Commit.** `git commit -m "feat: add leagues games and guests commands"`

---

### Task 5: League books

**Files:**
- Create: `LeagueBooksCommands.cs` and `LeagueBooksService.cs`
- Modify: `LeaguesCommand.cs`. Add `books` as the last subcommand.
- Tests: service and command tests.

**Commands:**
- `leagues books list --id`
- `search --id --query`, sent as query `q`
- `add --id --book-id [--visibility] [--shared-user-ids …]`
- `update --id --resource-id [--visibility] [--shared-user-ids …]`
  - Only the flags given are sent.
  - Require at least one of `--visibility` / `--shared-user-ids`.
  - An empty `--shared-user-ids` list must still be sendable if `CampaignResourcesCommands` has a pattern for it. Otherwise omit it.
- `remove --id --resource-id`
- `reorder --id --ordered-ids …`

Bodies use the generated league `BookAdd`, `BookUpdate` and `BookReorder` models; names may be prefixed. Use `Build*Body` helpers.

**Help:** use the spec's "Books" bullets. `--resource-id` and `--ordered-ids` take the `id` from `books list`, not the `book_id`. Say that on `update`, `remove` and `reorder`.

- [ ] **Step 1: Write the failing tests.**
  - Routes:
    - `list`: `GET /l-1/books`
    - `search`: `GET /l-1/books/search?q=codex`
    - `add`: `POST /l-1/books`
    - `update`: `PATCH /l-1/books/r-1`
    - `remove`: `DELETE /l-1/books/r-1`
    - `reorder`: `PUT /l-1/books/order`
  - The add body without `--visibility` leaves visibility unset, so the server default `organizer` applies.
  - The update body carries only the flags given.
  - `--visibility everyone` has errors.
  - `update` with neither flag has errors.
  - No role tags.
  - `remove` help contains `book_id`.
- [ ] **Step 2: Confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Format, build, test, and render help.**
- [ ] **Step 5: Commit.** `git commit -m "feat: add leagues books commands"`

---

### Task 6: Docs and smoke test

**Files:**
- Modify: `README.md` (the Commands table)
- Modify: `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docker/smoke-test.sh`

- [ ] **Step 1: README.**
  - Add a row for every new command, with flags and descriptions taken from the real `--help`.
  - Place `armies` and `leagues` in `Program.cs` order.
  - Place `systems factions` with the other `systems` rows.
- [ ] **Step 2: Coverage.**
  - Add the 38 entries: 14 armies, 23 leagues, and `GET /api/systems/{system_id}/factions`. Take the exact paths from `docs/grimoire-api-coverage.md`.
  - Run `python3 tools/generate-api-coverage.py`, which needs the running stack. Expected: 309 covered, up from 271. Report the real number if it differs.
- [ ] **Step 3: API notes.**
  - Add `## Armies` and `## Leagues` sections, and a system-factions bullet in the systems section.
  - Use the house bullet style: a bold claim, then the cite with its version.
  - Cover the spec's "Access, in general" and "Behaviour the help must carry", keeping the cites.
  - Verify each cite against `temp/grimoire/backend/` before writing it.
- [ ] **Step 4: Smoke test.** Add one block, where `$SR4` is set and the admin login is active. Follow the spec's Testing steps 1–9:
  1. **Clean up first:** delete every league named `Smoke League` and every army named `Smoke Army` that the caller can list.
  2. Create the army with unit `u1`. Run `progress --unit-id u1 --experience 3 --honours Bloodied` and assert the response.
  3. `export --format text --output -`: assert the output is non-empty and contains `Smoke Army`.
  4. `systems factions --id "$SR4"`: assert it equals `[]`.
  5. Create the league. Add admin and gm as members:
     - Get admin's id from `me`, or however existing smoke code does.
     - Get gm's id from `leagues eligible`, by `name`.
  6. Register the army for admin.
  7. Log a game with admin as A and gm as B, scores 3 and 1:
     - Assert the game's `result == "a"`.
     - Assert the top standings row is admin.
     - Delete the game.
  8. Share a Shadowrun 4 DE book, any book id under `$SR4` from existing smoke variables or a `books`/`systems` listing:
     - Run `books add`, then `books update --visibility public` and assert it.
     - Run `books remove`.
  9. Delete the league, then the army. Assert neither is listed.

  Use the script's existing `ok` / `fail` / `$WORK/cli.err` idioms and non-clashing variable names. Guests, import and publication-links are not smoke-tested; the spec explains why.
- [ ] **Step 5: Verify.**
  ```bash
  dotnet format GrimoireCli.sln --verify-no-changes
  dotnet build GrimoireCli.sln
  dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
  bash docker/smoke-test.sh
  bash docker/smoke-test.sh
  ```
  All must pass.
- [ ] **Step 6: Commit.** `git commit -m "docs: cover armies and leagues"`
