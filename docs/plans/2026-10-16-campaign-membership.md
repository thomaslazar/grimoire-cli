# Campaign membership and guests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #100 part 1, as specified in `docs/specs/2026-10-16-campaign-membership-design.md`. The commands are:
- `campaigns members eligible|invite|update|remove`
- `campaigns invites`
- `campaigns guests list|create|regenerate|remove|share`

**Architecture:**
- Two new groups, each with its own command file and service. Register both in `CampaignsCommand.Create()`.
- `invites` is a top-level command in `CampaignsCommand.cs` / `CampaignsService.cs`.
- Every service method has an `internal …Request` builder so a test can pin its route, as in `CampaignScheduleService`.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, bash smoke test.

## Global Constraints

- Branch `feat/campaign-membership`. It already holds an uncommitted `docker/docker-compose.yml` change that adds `GUEST_ACCESS_ENABLED=true`; commit it in Task 2.
- Conventional Commits, with **no `Co-Authored-By:` and no "Generated with" lines**.
- Run `dotnet format GrimoireCli.sln` after editing C#. Keep blank lines out of method bodies.
- **No role tag and no `permissionHint`** on any command.
- Help is terse:
  - Don't restate flag descriptions, response fields or rendered shapes.
  - A `ChoiceOption` description doesn't repeat its values.
  - Check the rendered `--help`.
- Not-found hints:
  - Campaign-level 404s use `"No campaign with that ID. List them with: grimoire-cli campaigns list"`, but only on routes where the campaign is the only thing that can be missing (`eligible`, `invite`, guests `list` and `create`).
  - Per-member and per-guest routes pass no hint, so the server's own detail shows.
- The local stack is at `http://host.docker.internal:9481`. Write only to the local stack.

---

### Task 1: `campaigns members` and `campaigns invites`

**Files:**
- Create: `src/GrimoireCli/Services/CampaignMembersService.cs`, `src/GrimoireCli/Commands/CampaignMembersCommands.cs`, and one service test file and one command test file for them.
- Modify: `CampaignsCommand.cs` and `CampaignsService.cs` to add `invites` and register the group, plus their existing tests.
- Modify: `README.md` (5 rows after the `campaigns calendar` rows) and `IMPLEMENTED`. Regenerate coverage with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`. The total goes from 216 to 221.

**Interfaces (produces):**
- `CampaignMembersService`, all methods returning `Task<string>`:
  - `EligibleAsync(id)` → `Campaigns[id].EligibleMembers` GET
  - `InviteAsync(id, userId)` → `Campaigns[id].Invite` POST, body `InvitePayload { UserId }`
  - `UpdateAsync(id, userId, string? status, string? characterName, string? characterSheetUrl)` → `Campaigns[id].Members[userId]` PATCH. Body is `MemberStatusUpdate`; set each field only when non-null. The fields are composed wrappers (`MemberStatusUpdate_*Member1`). `""` must still be sent, because it clears the value. Add a test asserting `"character_name":""` is present when `""` is given.
  - `RemoveAsync(id, userId)` → `Campaigns[id].Members[userId]` DELETE
- `CampaignsService.InvitesAsync()` → `Campaigns.Invites` GET.
- Check the builder names under `Generated/Api/Campaigns/`.

- [ ] **Step 1: Write the failing service tests.**
  - Routes:
    - eligible: `GET .../campaigns/c-1/eligible-members`
    - invite: `POST .../c-1/invite`
    - update: `PATCH .../c-1/members/u-1`
    - remove: `DELETE .../c-1/members/u-1`
    - invites: `GET .../campaigns/invites`
  - Bodies:
    - invite contains `"user_id":"u-1"`
    - update with only a status is exactly `{"status":"accepted"}`
    - update with `characterName: ""` contains `"character_name":""`
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - The `members` group holds exactly `eligible invite update remove`.
  - `invites` is a subcommand of `campaigns`.
  - `invite`, `update` and `remove` fail without `--user-id`.
  - `update --status maybe` fails.
  - `update --id c --user-id u --status accepted --character-name "" --character-sheet-url x` parses.
- [ ] **Step 6: Implement the commands.**
  - Option `--user-id`: "User ID (from members eligible or the campaign's members)".
  - Group description `members`: "Who plays in a campaign".
  - Commands, descriptions and Notes (verbatim):
    - `eligible`: "Users who can be invited".
      - Notes: `Campaign owner only. Registered users other than you; guests are never listed.`
    - `invite`: "Invite a user to the campaign".
      - Notes:
        ```
        Campaign owner only; refused while the campaign is archived. Group
        campaigns only (400). 409 if already invited or a member; 403 if their
        campaign access is disabled.
        ```
    - `update`: "Answer an invitation or set a member's character".
      - Options:
        - `--status`: `OptionHelpers.Choice`, values `accepted` and `declined`, description "Your answer to the invitation".
        - `--character-name`: "Character name".
        - `--character-sheet-url`: "Link to the character sheet".
      - Notes:
        ```
        Yourself, or the campaign owner for anyone; refused while the campaign
        is archived. Send only what changes; "" clears a name or link. A sheet
        link replaces any uploaded sheet file. Accepting needs your campaign
        access (403).
        ```
    - `remove`: "Remove a member, or leave".
      - Notes:
        ```
        Yourself, which works even while the campaign is archived, or the
        campaign owner for anyone. Removing a guest also deletes their account.
        An unknown user is a silent 204.
        ```
      - Prints nothing on 204.
    - `campaigns invites`: "Your pending campaign invitations".
      - Notes: `Archived campaigns are left out until they are unarchived.`
  - Response examples:
    - `eligible`: `EligibleMemberOut`
    - `update`: `MemberStatusOut`
    - `invite` and `invites`: the generated model for each route, if one exists.
  - One example per command.
- [ ] **Step 7: Format, run the full suite, and read the rendered `--help` of all five commands.**
- [ ] **Step 8: README and coverage.**
  - README rows:
    ```
    | `campaigns invites` | Your pending campaign invitations |
    | `campaigns members eligible --id <campaign-id>` | Users who can be invited |
    | `campaigns members invite --id <campaign-id> --user-id <user-id>` | Invite a user to the campaign |
    | `campaigns members update --id <campaign-id> --user-id <user-id> [--status <accepted\|declined>] [--character-name <name>] [--character-sheet-url <url>]` | Answer an invitation or set a member's character |
    | `campaigns members remove --id <campaign-id> --user-id <user-id>` | Remove a member, or leave |
    ```
  - `IMPLEMENTED`:
    ```python
    "GET /api/campaigns/invites": "`campaigns invites` ✅",
    "GET /api/campaigns/{campaign_id}/eligible-members": "`campaigns members eligible` ✅",
    "POST /api/campaigns/{campaign_id}/invite": "`campaigns members invite` ✅",
    "PATCH /api/campaigns/{campaign_id}/members/{user_id}": "`campaigns members update` ✅",
    "DELETE /api/campaigns/{campaign_id}/members/{user_id}": "`campaigns members remove` ✅",
    ```
- [ ] **Step 9: Commit** `feat: add campaigns members and invites commands`.

### Task 2: `campaigns guests` and the stack setting

**Files:**
- Create: `src/GrimoireCli/Services/CampaignGuestsService.cs`, `src/GrimoireCli/Commands/CampaignGuestsCommands.cs`, plus one service test file and one command test file.
- Modify: `CampaignsCommand.cs` to register the group.
- Modify: `README.md` (5 rows) and `IMPLEMENTED`, then regenerate coverage. It goes from 221 to 226.
- Commit: the already-modified `docker/docker-compose.yml`.

**Interfaces (produces):** `CampaignGuestsService`, all methods returning `Task<string>`:
- `ListAsync(id)`: GET `Campaigns[id].Guests`
- `CreateAsync(id, string? nickname)`: POST, body `GuestCreate`, with `Nickname` set only when given
- `RegenerateAsync(id, memberId)`: POST `Guests[memberId].Regenerate`
- `RemoveAsync(id, memberId)`: DELETE `Guests[memberId]`
- `ShareAsync(id, memberId)`: GET `Guests[memberId].ShareTemplate`

- [ ] **Step 1: Write the failing service tests.**
  - Routes:
    - list: `GET .../c-1/guests`
    - create: `POST .../c-1/guests`
    - regenerate: `POST .../c-1/guests/m-1/regenerate`
    - remove: `DELETE .../c-1/guests/m-1`
    - share: `GET .../c-1/guests/m-1/share-template`
  - The create body without a nickname has no `nickname` key; with one, it contains `"nickname":"Sam"`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - The group holds exactly `list create regenerate remove share`.
  - `regenerate`, `remove` and `share` fail without `--member-id`.
  - `create --id c` and `create --id c --nickname Sam` parse.
  - The help of `create`, `list`, `regenerate` and `share` contains `anyone holding it is that guest`.
- [ ] **Step 6: Implement the commands.**
  - Option `--member-id`: "Guest's member ID (id from guests list, not user_id)".
  - Group description: "Guest invite codes for players without an account".
  - Shared lines:
    - `Owned`: `"Campaign owner only; group campaigns only (400)."`
    - `Code`: `"The code is the guest's login; anyone holding it is that guest."`
  - Commands and Notes (verbatim):
    - `list`: "List the campaign's guests and their codes".
      - Notes: `Owned`, `Code`.
    - `create`: "Create a guest and their invite code". Option `--nickname`: "Name shown for the guest (default Guest)".
      - Notes: `Owned`, then `403 when the server has guest access off. The guest is a member at once.`, then `Code`.
    - `regenerate`: "Replace a guest's invite code".
      - Notes: `Owned`, then `403 when the server has guest access off. The old code stops working and the guest's sessions end.`, then `Code`.
    - `remove`: "Remove a guest and delete their account".
      - Notes: `Owned`, then:
        ```
        Deletes their availability, player notes and shares with them; pages
        they wrote stay, without an author.
        ```
      - Prints nothing on 204.
    - `share`: "Invite text and link carrying a guest's code".
      - Notes: `Owned`, `Code`, then `The link is built from the server's BASE_URL.`
  - Re-wrap any of the lines above that run past about 79 characters.
  - Response examples: `GuestShareTemplateOut` for `share`. For the others, use the generated model if one exists.
  - One example per command.
- [ ] **Step 7: Format, run the full suite, and read the rendered `--help` of all five commands.**
- [ ] **Step 8: README and coverage.**
  - README rows:
    ```
    | `campaigns guests list --id <campaign-id>` | List the campaign's guests and their codes |
    | `campaigns guests create --id <campaign-id> [--nickname <name>]` | Create a guest and their invite code |
    | `campaigns guests regenerate --id <campaign-id> --member-id <member-id>` | Replace a guest's invite code |
    | `campaigns guests remove --id <campaign-id> --member-id <member-id>` | Remove a guest and delete their account |
    | `campaigns guests share --id <campaign-id> --member-id <member-id>` | Invite text and link carrying a guest's code |
    ```
  - `IMPLEMENTED`:
    ```python
    "GET /api/campaigns/{campaign_id}/guests": "`campaigns guests list` ✅",
    "POST /api/campaigns/{campaign_id}/guests": "`campaigns guests create` ✅",
    "POST /api/campaigns/{campaign_id}/guests/{member_id}/regenerate": "`campaigns guests regenerate` ✅",
    "DELETE /api/campaigns/{campaign_id}/guests/{member_id}": "`campaigns guests remove` ✅",
    "GET /api/campaigns/{campaign_id}/guests/{member_id}/share-template": "`campaigns guests share` ✅",
    ```
- [ ] **Step 9: Commit** `feat: add campaigns guests commands`. Include `docker/docker-compose.yml`.

### Task 3: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`. Add a new section `# ---- campaign membership ----` after the `campaign scheduling` section, before `# ---- api keys ----`.

**Interfaces:**
- Consumes the Task 1 and 2 commands.
- The main smoke session is admin, so admin owns `Smoke Table`.
- Log in as `player`/`player` into a scratch config with `GRIMOIRE_CONFIG="$WORK/player-config.json" "$CLI" login --server "$SERVER" --username player --password player`. Check how the api-keys section logs in, and match it.

- [ ] **Step 1: Add the section.** Use the existing style: a `# …` narration line before each step, stderr to `"$WORK/cli.err"`, `fail`/`ok`, and non-zero expectations wrapped in `set +e`/`set -e` as in the housekeeping section.
  1. Delete every campaign named `Smoke Table` from `campaigns list --include-archived`.
  2. Create the campaign with `{"name":"Smoke Table","is_gm_campaign":true}` and capture `TBL`.
  3. Run `members eligible`. Expect `player` in the list, and capture `PID`.
  4. Run `members invite --user-id "$PID"` and expect `.status == "invited"`.
  5. As player, run `campaigns invites`. Expect `TBL` among the `campaign_id`s.
  6. As player, run `members update --user-id "$PID" --status accepted` and expect `.status == "accepted"`.
  7. As owner, run `members update --user-id "$PID" --character-name "Smoke Vex"` and expect `.character_name == "Smoke Vex"`.
  8. Run `members remove --user-id "$PID"` and expect exit 0.
  9. Run `guests create --nickname "Smoke Guest"`. Capture `GM_ID=.id` and `CODE=.guest_code`, and expect `CODE` to be 10 characters.
  10. Run `guests list`. Expect the entry with `id == GM_ID` to have `guest_code == CODE`.
  11. Run `guests share` and expect `.code == CODE`.
  12. Run `guests regenerate` and expect `.guest_code != CODE`.
  13. Run `guests remove` and expect `guests list` to be empty.
  14. Run `campaigns delete --id "$TBL"`.

  Add about 4 `ok` lines.
- [ ] **Step 2: Recreate the stack so the compose change takes effect.**
  - Run `docker compose -f docker/docker-compose.yml up -d --wait`. If `docker/.env` exists, export its variables first, because this is docker-outside-of-docker and the host paths live there.
  - Confirm it is still seeded: `systems list` works.
- [ ] **Step 3: Build, then run the smoke test twice.** Run `dotnet build GrimoireCli.sln`, then `bash docker/smoke-test.sh`. Capture the full output each time. Both runs must end with `smoke: all checks passed`.
- [ ] **Step 4: Commit** `test: cover campaign membership and guests in the smoke test`.
