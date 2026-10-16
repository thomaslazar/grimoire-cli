# Campaign scheduling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #99 as specified in `docs/specs/2026-10-15-campaign-scheduling-design.md`. That covers:
- `campaigns schedule get|set|delete`
- `campaigns availability get|set|toggle-cancel`
- `campaigns calendar download`
- `campaigns calendar subscription get|create|revoke`

**Architecture:**
- Three new groups, each with its own command file and its own service. Register all three in `CampaignsCommand.Create()` beside the other subgroups.
- Every service method has an `internal …Request` builder so a test can pin its route, as `CampaignWikiTemplateService` does.

**Patterns to copy:**
- JSON body with `--input`/`--stdin`, validated against the generated model: `CampaignWikiTemplateCommands` `create`.
- Bytes out with `--output`: `CampaignBannerCommands` `get`.
- Scalar flags mapped to a body: `CampaignBannerService.FocusAsync`.

**Tech Stack:** .NET, System.CommandLine, the Kiota-generated client (`src/GrimoireCli/Generated/`, never hand-edit), xUnit, bash smoke test.

## Global Constraints

- Branch `feat/campaign-scheduling`. Use Conventional Commits, with **no `Co-Authored-By:` and no "Generated with" lines**.
- Run `dotnet format GrimoireCli.sln` after editing C#. No blank lines inside method bodies.
- **No role tag and no `permissionHint`** on any command.
- Help is terse:
  - Don't restate flag descriptions, response fields or rendered shapes.
  - A `ChoiceOption` description must not repeat its values.
  - Check the rendered `--help`.
- Campaign-level 404s use the hint `"No campaign with that ID. List them with: grimoire-cli campaigns list"`.
- The local stack is at `http://host.docker.internal:9481`. Write only to the local stack.

---

### Task 1: `campaigns schedule` and `campaigns availability`

**Files:**
- Create: `src/GrimoireCli/Services/CampaignScheduleService.cs` and `src/GrimoireCli/Commands/CampaignScheduleCommands.cs`
- Create: `src/GrimoireCli/Services/CampaignAvailabilityService.cs` and `src/GrimoireCli/Commands/CampaignAvailabilityCommands.cs`
- Create: tests for each, under `tests/GrimoireCli.Tests/Services/` and `tests/GrimoireCli.Tests/Commands/`
- Modify: `src/GrimoireCli/Commands/CampaignsCommand.cs` to register both groups
- Modify: `README.md` (6 rows after the `campaigns banner` rows) and `tools/generate-api-coverage.py` (`IMPLEMENTED`), then regenerate `docs/grimoire-api-coverage.md` with `GRIMOIRE_SERVER=http://host.docker.internal:9481 python3 tools/generate-api-coverage.py`. Coverage goes from 206 to 212.

**Interfaces (produces):**
- `CampaignScheduleService`:
  - `GetAsync(id)`
  - `SetAsync(id, rawBody)`: raw JSON via `SetStreamContent`, built from `new Generated.Models.ScheduleUpsert()`
  - `DeleteAsync(id)`
  - All return `Task<string>`.
- `CampaignAvailabilityService`:
  - `GetAsync(id)`
  - `SetAsync(id, date, status, bool? isCancelled, string? userId)`: body is the generated `AvailabilityUpdate`. `Status` is always set. `IsCancelled` and `UserId` are set only when non-null; they are composed wrapper types, so check the generated `AvailabilityUpdate_*Member1` classes.
    - `user_id` is typed int in the spec. Check how the generated model types it. If it is numeric, send the CLI's string by putting the raw JSON in the body instead: build `{"status":…,"is_cancelled":…,"user_id":"<string>"}` with `System.Text.Json` and `SetStreamContent`. Then the server, not the CLI, decides what happens. Record which way you went.
  - `ToggleCancelAsync(id, date)`
  - All return `Task<string>`.
- Generated builders, to confirm under `src/GrimoireCli/Generated/Api/Campaigns/Item/`:
  - `Campaigns[id].Schedule`
  - `Campaigns[id].Availability`
  - `Campaigns[id].Availability[date]`
  - `Campaigns[id].Availability[date].Cancel`

- [ ] **Step 1: Write the failing service tests.**
  - `Routes()` theory:
    - schedule get: `GET .../c-1/schedule`
    - schedule set: `PUT .../c-1/schedule`
    - schedule delete: `DELETE .../c-1/schedule`
    - availability get: `GET .../c-1/availability`
    - availability set: `PUT .../c-1/availability/2026-10-21`
    - toggle: `PUT .../c-1/availability/2026-10-21/cancel`
  - Schedule set sends the raw body unchanged.
  - Availability set with only a status sends exactly `{"status":"tentative"}`. With all three values it contains `"is_cancelled":true` and `"user_id":"u-1"`.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement both services.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - The groups hold `get set delete` and `get set toggle-cancel`.
  - Every command fails without `--id`.
  - `schedule set` fails with neither `--input` nor `--stdin`.
  - `availability set` fails without `--date` or `--status`.
  - `--status maybe` fails.
  - `availability set --id c --date 2026-10-21 --status available --is-cancelled true --user-id u` parses.
  - `toggle-cancel` help contains `flips`.
- [ ] **Step 6: Implement the commands.** Group descriptions:
  - `schedule`: "A group campaign's recurring session schedule".
  - `availability`: "Who can make the upcoming sessions".

  Commands, descriptions and verbatim Notes:
  - `schedule get`: "Get the schedule and the next 10 session dates".
    - Notes: `Any campaign member. definition is null when none is set.`
  - `schedule set`: "Create or replace the schedule".
    - Notes:
      ```
      Campaign owner only; refused while the campaign is archived. Group
      campaigns only (400 on a personal one).

      Replaces the whole schedule; omitted fields take their defaults. days are
      0 (Monday) to 6 (Sunday) and time_local is HH:MM, both in timezone (an IANA
      name). biweekly uses biweekly_reference (YYYY-MM-DD, default today);
      monthly uses monthly_week (1-4, -1 = last, default 1); custom uses only
      custom_dates. time_utc is a legacy alias. enabled false keeps the
      definition but yields no sessions. next_sessions counts from yesterday.
      ```
    - Example: `echo '{"frequency":"weekly","days":[2],"time_local":"19:30","timezone":"Europe/Berlin"}' | grimoire-cli campaigns schedule set --id <campaign-id> --stdin`
    - Request shape: `ScheduleUpsert`. Response example: `ScheduleOut`.
  - `schedule delete`: "Remove the schedule".
    - Notes: `Campaign owner only; refused while the campaign is archived.`
    - Prints nothing on 204.
  - `availability get`: "Each member's availability for the next 10 sessions".
    - Notes:
      ```
      Any campaign member. A date is cancelled when any row for it is.
      ```
    - Response example: `AvailabilityOut`.
  - `availability set`: "Set availability for one session date".
    - Options:
      - `--date` (required): "Session date, YYYY-MM-DD"
      - `--status`: `OptionHelpers.Choice`, required, values `available`, `tentative`, `unavailable`, description "Your answer for that date"
      - `--is-cancelled` (`Option<bool?>`): "Mark the date cancelled on the target's row"
      - `--user-id`: "Set another member's row instead of your own"
    - Notes:
      ```
      Any campaign member, for their own row; refused while the campaign is
      archived. Any valid date is accepted, scheduled or not.

      --is-cancelled needs the owner or an admin and sets only the target's row,
      so it cannot reliably uncancel a date toggle-cancel cancelled.
      --user-id currently always answers 422: the server types user_id as an
      integer, and user ids are strings.
      ```
    - Response example: `AvailabilitySetOut`.
  - `availability toggle-cancel`: "Flip whether a session date is cancelled".
    - Notes:
      ```
      Campaign owner or an admin; refused while the campaign is archived.

      Flips the date for every member, so calling it twice undoes it;
      is_cancelled in the response is the new state.
      ```
    - Response example: use the generated model for that route, found from the spec or `Generated/Models`. If none exists, add no example.
  - One example per command.
- [ ] **Step 7: Format, run the full suite, and read the rendered `--help` of all six commands.**
- [ ] **Step 8: README and coverage.**
  - README rows:
    ```
    | `campaigns schedule get --id <campaign-id>` | Get the schedule and the next 10 session dates |
    | `campaigns schedule set --id <campaign-id> {--input <file> \| --stdin}` | Create or replace the schedule |
    | `campaigns schedule delete --id <campaign-id>` | Remove the schedule |
    | `campaigns availability get --id <campaign-id>` | Each member's availability for the next 10 sessions |
    | `campaigns availability set --id <campaign-id> --date <YYYY-MM-DD> --status <status> [--is-cancelled <bool>] [--user-id <id>]` | Set availability for one session date |
    | `campaigns availability toggle-cancel --id <campaign-id> --date <YYYY-MM-DD>` | Flip whether a session date is cancelled |
    ```
  - `IMPLEMENTED`:
    ```python
    "GET /api/campaigns/{campaign_id}/schedule": "`campaigns schedule get` ✅",
    "PUT /api/campaigns/{campaign_id}/schedule": "`campaigns schedule set` ✅",
    "DELETE /api/campaigns/{campaign_id}/schedule": "`campaigns schedule delete` ✅",
    "GET /api/campaigns/{campaign_id}/availability": "`campaigns availability get` ✅",
    "PUT /api/campaigns/{campaign_id}/availability/{session_date}": "`campaigns availability set` ✅",
    "PUT /api/campaigns/{campaign_id}/availability/{session_date}/cancel": "`campaigns availability toggle-cancel` ✅",
    ```
- [ ] **Step 9: Commit** with `feat: add campaigns schedule and availability commands`.

### Task 2: `campaigns calendar`

**Files:**
- Create: `src/GrimoireCli/Services/CampaignCalendarService.cs` and `src/GrimoireCli/Commands/CampaignCalendarCommands.cs`, plus tests for both
- Modify: `CampaignsCommand.cs` to register the group
- Modify: README (4 rows) and `IMPLEMENTED` (4 entries). Regenerate coverage; it goes from 212 to 216.
- Modify: `docs/grimoire-api-notes.md`. Add a `### Scheduling and calendar` subsection after `### Session notes are legacy` (the text is below).

**Interfaces (produces):**
- `CampaignCalendarService`:
  - `DownloadAsync(id) : Task<Stream>` through `SendStreamAsync`, with the campaign hint. The route is `Campaigns[id].CalendarIcs`.
  - `SubscriptionAsync(string? campaignId) : Task<string>`. GET on `Campaigns.Calendar.Subscription`. The `campaign_id` query parameter is sent only when given.
  - `CreateSubscriptionAsync(string? campaignId)`. POST, same query rule.
  - `RevokeSubscriptionAsync()`. DELETE.

- [ ] **Step 1: Write the failing service tests.**
  - Routes:
    - `GET .../campaigns/c-1/calendar.ics`
    - `GET .../campaigns/calendar/subscription`
    - `POST .../campaigns/calendar/subscription`
    - `DELETE .../campaigns/calendar/subscription`
  - `campaign_id=c-1` is present only when given.
- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the service.**
- [ ] **Step 4: Run the tests and confirm they pass.**
- [ ] **Step 5: Write the failing command tests.**
  - The group holds `download` and `subscription`.
  - `subscription` holds `get create revoke`.
  - `download` fails without `--output`.
  - `subscription get` parses with no options, and parses with `--id c`.
  - `subscription revoke --id c` fails, because `revoke` has no `--id`.
- [ ] **Step 6: Implement the commands.**
  - Group descriptions:
    - `calendar`: "Campaign sessions as calendar (.ics) data".
    - `subscription`: "Your calendar-feed token and its URLs".
  - `--id` on `subscription get` and `subscription create` is optional, with the description "Also return this campaign's feed URL".
  - `download`: "Download the next 26 sessions as an .ics file".
    - Notes:
      ```
      Any campaign member. Event titles carry your own availability; cancelled
      dates are marked. Needs no feed token.

      --output - writes the calendar to stdout; a path writes the file and
      prints {path, bytes}.
      ```
    - Response example: `SavedFile`.
  - `subscription get`: "Show your calendar-feed URLs".
  - `subscription create`: "Mint or rotate your calendar-feed token".
    - Notes:
      ```
      Rotating breaks every URL issued before. 400 unless the server's BASE_URL
      is set.
      ```
  - `subscription revoke`: "Revoke your calendar-feed token".
    - Notes: `Every subscribed URL stops working.`
  - Every `subscription` command starts its Notes with `Refused with an API-key login: these routes need a password login.`
  - Response example for all three: `CalendarSubscriptionOut`.
  - One example per command.
- [ ] **Step 7: Format, run the full suite, and read the rendered `--help` of all four commands.**
- [ ] **Step 8: README, coverage and API notes.**
  - README rows:
    ```
    | `campaigns calendar download --id <campaign-id> --output <path\|->` | Download the next 26 sessions as an .ics file |
    | `campaigns calendar subscription get [--id <campaign-id>]` | Show your calendar-feed URLs |
    | `campaigns calendar subscription create [--id <campaign-id>]` | Mint or rotate your calendar-feed token |
    | `campaigns calendar subscription revoke` | Revoke your calendar-feed token |
    ```
  - `IMPLEMENTED`:
    ```python
    "GET /api/campaigns/{campaign_id}/calendar.ics": "`campaigns calendar download` ✅",
    "GET /api/campaigns/calendar/subscription": "`campaigns calendar subscription get` ✅",
    "POST /api/campaigns/calendar/subscription": "`campaigns calendar subscription create` ✅",
    "DELETE /api/campaigns/calendar/subscription": "`campaigns calendar subscription revoke` ✅",
    ```
    Check how the generator renders the two unwrapped feed routes. If `IMPLEMENTED` supports a non-✅ note for deliberately skipped routes, add `"not wrapped: feeds for calendar apps; subscription returns the URLs"` for both. If it doesn't, leave them out.
  - API notes subsection:
    ```
    ### Scheduling and calendar

    Read from `backend/routers/campaigns/schedule.py`, `calendar.py` and
    `_schemas.py` at tag `v1.8.0`; verified against the 1.8.0 stack.

    - **Cancelling a date is a toggle.** `PUT availability/{date}/cancel` flips
      `is_cancelled` on every row for that date (`schedule.py:294-336`); a second
      call undoes the first. It admits the owner or any admin, unlike the
      campaign's other writes.
    - **`availability` `user_id` cannot work.** `AvailabilityUpdate.user_id` is
      `Optional[int]` (`_schemas.py:177`) while user ids are UUID strings, so any
      value is a 422 `int_parsing` and a GM cannot set another member's row.
    - **Calendar subscription is session only.** The three
      `/calendar/subscription` routes carry `x-api-key-access: none`; minting
      also needs `BASE_URL` (400 otherwise).
    - **The token feeds are not wrapped.** `/calendar/{token}/all.ics` and
      `/calendar/{token}/{campaign_id}.ics` authenticate by the token in the path
      for calendar apps; `subscription` returns their URLs, and
      `{id}/calendar.ics` serves the same events to an authenticated caller.
    ```
- [ ] **Step 9: Commit** with `feat: add campaigns calendar commands`.

### Task 3: Smoke test

**Files:**
- Modify: `docker/smoke-test.sh`. Add a new section `# ---- campaign scheduling ----` after the `campaign housekeeping` section, before `# ---- api keys ----`.

**Interfaces:**
- Consumes the Task 1 and Task 2 commands.
- `$WIKI` is the Smoke Wiki GM campaign id, set earlier at top level. The smoke session is admin, and admin owns that campaign.

- [ ] **Step 1: Add the section.** Follow the existing style: a `# …` narration line before each step, stderr to `"$WORK/cli.err"`, and `fail`/`ok`.
  1. Set a weekly schedule:
     `echo '{"frequency":"weekly","days":[2],"time_local":"19:30","timezone":"Europe/Berlin"}' | "$CLI" campaigns schedule set --id "$WIKI" --stdin`
     Expect `.next_sessions | length > 0`. Capture `D=.next_sessions[0]`.
  2. `schedule get`. Expect `.definition.frequency == "weekly"`.
  3. `availability set --id "$WIKI" --date "$D" --status tentative`. Expect `.status == "tentative"`.
  4. `availability get`. Expect the `is_owner` row's `.dates[$D].status == "tentative"`.
  5. `toggle-cancel` twice. Expect `.is_cancelled == true`, then `false`.
  6. `calendar download --id "$WIKI" --output "$WORK/smoke.ics"`. Expect the first line of the file to be `BEGIN:VCALENDAR`. Strip any `\r` before comparing.
  7. `calendar subscription get`. Expect `.has_token | type == "boolean"`.
  8. `schedule delete`, then `schedule get`. Expect `.definition == null`.

  Write about 4 `ok` lines.
- [ ] **Step 2: Build, then run the smoke test twice.** Run `dotnet build GrimoireCli.sln`, then `bash docker/smoke-test.sh`. Capture the full output each time. Both runs must end with `smoke: all checks passed`.
- [ ] **Step 3: Commit** with `test: cover campaign scheduling in the smoke test`.
