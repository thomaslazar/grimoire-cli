# Campaign scheduling (#99)

Recurring schedule, availability, cancelling a date, and calendar output. This
adds 10 commands, one per route.

Two of the 12 routes are not wrapped: the token-authenticated feeds
`GET /api/campaigns/calendar/{token}/all.ics` and
`GET /api/campaigns/calendar/{token}/{campaign_id}.ics`. They sit outside the
JWT router (`__init__.py:173-194`) and exist for calendar apps to poll, which
need the URL that `subscription` returns. The authenticated
`GET /{id}/calendar.ics` serves the same per-user events as a download. A CLI
call to a feed would only put the secret token in a flag.

Server facts were read at `v1.8.0`. Line references are to
`backend/routers/campaigns/schedule.py` (`:line`) and `calendar.py`
(`cal:line`). Everything marked *probed* was checked against the local stack.

## `campaigns schedule`: `CampaignScheduleCommands.cs` / `CampaignScheduleService.cs`

| Command | Route |
|---|---|
| `get --id` | `GET /campaigns/{id}/schedule` (:54) |
| `set --id {--input\|--stdin}` | `PUT /campaigns/{id}/schedule` (:76), body validated against `ScheduleUpsert` |
| `delete --id` | `DELETE /campaigns/{id}/schedule` (:151) |

- **`get`**
  - Needs campaign membership (`can_view`).
  - With no schedule set, it returns `definition: null`.
- **`set`**
  - Needs ownership (`assert_can_manage`, so 409 while archived).
  - Only group campaigns can have a schedule: a personal campaign answers 400.
  - It replaces the whole schedule. An omitted field takes its default
    (`frequency` weekly, `enabled` true; `_schemas.py:154-171`).
- **`set` body fields**
  - `days` are 0 = Monday through 6 = Sunday, in `timezone`, an IANA name
    (400 if invalid).
  - `time_local` is `HH:MM` in that zone. `time_utc` is a legacy alias that the
    server converts.
  - `biweekly` uses `biweekly_reference` (YYYY-MM-DD, default today).
  - `monthly` uses `monthly_week` (1-4, or -1 for the last week; default 1).
  - `custom` uses only `custom_dates`.
  - `enabled: false` keeps the definition but produces no sessions.
  - `next_sessions` (10 dates) counts from yesterday on (`_helpers.py:559`).
  - *Probed:* weekly, `days [2]`, `19:30` Europe/Berlin gave Wednesdays.
- **`delete`**
  - Needs ownership.
  - Deleting a campaign that has no schedule is a silent 204.

## `campaigns availability`: `CampaignAvailabilityCommands.cs` / `CampaignAvailabilityService.cs`

| Command | Route |
|---|---|
| `get --id` | `GET /campaigns/{id}/availability` (:164) |
| `set --id --date <YYYY-MM-DD> --status <s> [--is-cancelled <bool>] [--user-id <id>]` | `PUT /campaigns/{id}/availability/{date}` (:230) |
| `toggle-cancel --id --date <YYYY-MM-DD>` | `PUT /campaigns/{id}/availability/{date}/cancel` (:294) |

- **`get`**
  - Needs membership.
  - Covers the owner and accepted members, for the next 10 scheduled dates only.
  - A date counts as cancelled when any row for it is cancelled.
- **`set`**
  - Needs membership, and is refused with 409 while the campaign is archived.
  - `--status` is a choice option: `available`, `tentative` or `unavailable`.
  - Any valid date is accepted, not only scheduled ones.
  - The body mirrors `AvailabilityUpdate`; each optional flag is sent only when
    given.
  - `--is-cancelled` is for the GM only (owner or admin; 403 otherwise). It sets
    only the target's own row, so it can't reliably uncancel a date that
    `toggle-cancel` cancelled.
  - `--user-id` currently always answers 422. The server types `user_id` as an
    integer (`_schemas.py:177`), but user ids are UUID strings. *Probed:* 422
    `int_parsing`.
- **`toggle-cancel`**
  - Needs the owner or an admin. This is an admin override, unlike the
    campaign's other writes. Refused with 409 while archived.
  - It **flips** the date's cancelled state for every row, so a second call
    undoes it. The response's `is_cancelled` is the new state.
  - *Probed:* the first call answered `true`, the second `false`.

## `campaigns calendar`: `CampaignCalendarCommands.cs` / `CampaignCalendarService.cs`

| Command | Route |
|---|---|
| `download --id --output <path\|->` | `GET /campaigns/{id}/calendar.ics` (cal:228) |
| `subscription get [--id]` | `GET /campaigns/calendar/subscription` (cal:281) |
| `subscription create [--id]` | `POST /campaigns/calendar/subscription` (cal:290) |
| `subscription revoke` | `DELETE /campaigns/calendar/subscription` (cal:308) |

- **`download`**
  - Needs membership.
  - Writes the next 26 sessions as `text/calendar`. Event titles carry your own
    availability, and cancelled dates are marked.
  - Needs no token and no `BASE_URL`.
  - *Probed:* 200, `BEGIN:VCALENDAR`.
- **`subscription`**
  - Covers the caller's own calendar-feed token, across all their campaigns.
  - `--id` maps to the `campaign_id` query parameter and adds that campaign's
    `campaign_feed_url` to the response.
  - All three routes carry `x-api-key-access: none`, so a CLI logged in with
    `--api-key-stdin` is refused.
- **`subscription create`**
  - Mints a token, or rotates it, which breaks every URL issued before.
  - Answers 400 unless the server's `BASE_URL` is set. *Probed:* the local
    stack answers 400.
- **`subscription revoke`** makes every subscribed URL stop working.

## Shared

- No command has a role tag or a permission hint. Every check is in the
  handlers.
- A campaign-level 404 uses the existing campaign not-found hint. `download`
  and `subscription` follow the same rule, wherever only the campaign can be
  missing.

## Server behaviour for `grimoire-api-notes.md`

- `toggle-cancel` is a toggle, and it lets admins through.
- `availability set --user-id` is unusable because of the int type.
- The subscription routes are session only.
- The feed routes are left unwrapped, for the reason given above.

## Docs and tests

- README: 10 rows.
- `IMPLEMENTED`: 10 entries. Coverage goes from 206 to 216.
- Unit tests:
  - Service: each route and method; the schedule raw body; the availability
    body sending only the given fields; `campaign_id` sent only when `--id` is
    given.
  - Commands: required options; the `--status` choice; `toggle-cancel` help
    says it flips.
- Smoke test, as a new `campaign scheduling` section, on the `Smoke Wiki` GM
  campaign:
  1. `schedule set` weekly, then `get` and check that `next_sessions` is
     non-empty.
  2. `availability set` the first date to `tentative`, then `get` and check the
     owner's row.
  3. `toggle-cancel` twice and check `true`, then `false`.
  4. `calendar download --output` and check for `BEGIN:VCALENDAR`.
  5. `calendar subscription get` and check `has_token` is a boolean.
  6. `schedule delete` and check that `get` returns `definition == null`.

  Every write uses fixed values and the schedule is removed at the end, so the
  section converges on re-run.
- #99 is closed by the PR.
