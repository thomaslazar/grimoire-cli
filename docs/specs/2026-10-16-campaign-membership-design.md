# Campaign membership and guests (#100, part 1)

Covers inviting, joining, leaving and removing members, plus guest invite
codes. That is 10 commands, one per route. The 11 member-asset routes (art,
token and sheet) are part 2, tracked on #100.

Server facts were read at `v1.8.0` in `backend/routers/campaigns/`:
`members.py` (`:line`), `guests.py` (`g:line`) and `core.py`. Everything
marked *probed* was checked against the local stack.

## Two kinds of id

- `campaigns members` takes **user ids**. They come from `members eligible`
  or from the campaign's `members[]`.
- `campaigns guests` takes **member ids**: the `id` of an entry in
  `guests list`, which is a `CampaignMember` row. It does not take the guest's
  `user_id` (`_get_guest_member`, g:92).
- Each command's option description says which id it takes. Both share
  `--id <campaign-id>`.

## `campaigns members`: `CampaignMembersCommands.cs` / `CampaignMembersService.cs`

| Command | Route |
|---|---|
| `eligible --id` | `GET /campaigns/{id}/eligible-members` (:145) |
| `invite --id --user-id` | `POST /campaigns/{id}/invite` (:25) |
| `update --id --user-id [--status accepted\|declined] [--character-name <n>] [--character-sheet-url <url>]` | `PATCH /campaigns/{id}/members/{user_id}` (:59) |
| `remove --id --user-id` | `DELETE /campaigns/{id}/members/{user_id}` (:115) |

- **`eligible`**
  - Owner only (`assert_can_manage`).
  - Lists every registered user except the owner and guests, with
    `already_invited` and `campaign_access`.
- **`invite`**
  - Owner only, and group campaigns only (400 otherwise).
  - 404 for an unknown user. 403 if that user's campaign access is disabled.
  - 409 if the user is already invited or a member. *Probed:* 201, then 409.
- **`update`**
  - Usable on yourself, or by the owner on anyone. Refused with 409 while the
    campaign is archived.
  - `--status` is a choice option (`accepted` or `declined`). Accepting needs
    your campaign access (403).
  - `--character-name` and `--character-sheet-url`: `""` clears the value. A
    sheet link replaces any uploaded sheet file.
  - Each flag is sent only when given. The body is the generated
    `MemberStatusUpdate`.
  - *Probed:* the player accepted, and the owner set `character_name`.
- **`remove`**
  - Usable on yourself (leaving, which works even while the campaign is
    archived) or by the owner.
  - Removing a guest this way also deletes their account (`delete_guest_user`).
  - An unknown user is a silent 204 (*probed*).

## `campaigns invites`: `CampaignsCommand.cs` / `CampaignsService.cs`

`GET /campaigns/invites` (`core.py:105`) returns the caller's own pending
invitations. Archived campaigns drop out of the list and reappear when they
are unarchived. *Probed:* it lists the invited campaign for the player.

## `campaigns guests`: `CampaignGuestsCommands.cs` / `CampaignGuestsService.cs`

| Command | Route |
|---|---|
| `list --id` | `GET /campaigns/{id}/guests` (g:144) |
| `create --id [--nickname <name>]` | `POST /campaigns/{id}/guests` (g:103) |
| `regenerate --id --member-id` | `POST /campaigns/{id}/guests/{member_id}/regenerate` (g:157) |
| `remove --id --member-id` | `DELETE /campaigns/{id}/guests/{member_id}` (g:176) |
| `share --id --member-id` | `GET /campaigns/{id}/guests/{member_id}/share-template` (g:192) |

- **Access.** Every command is owner only, on group campaigns only (400
  otherwise; `_get_managed_campaign`, g:84).
- **`create` and `regenerate`** answer 403 when guest access is off on the
  server, which is the default (`_assert_guest_access`, g:38).
- **The code is a credential.** `create`, `list`, `regenerate` and `share`
  print it, and each says so in its help: "The code is the guest's login;
  anyone holding it is that guest."
- **The CLI prints the server's response unchanged.** It does not redact the
  code, because the code is exactly what the caller creates a guest to hand
  out.
- **`create`**
  - Makes a guest account that is already a member.
  - `--nickname` defaults to "Guest".
  - The first guest turns on guest invites for the campaign.
- **`regenerate`** issues a new code and ends the guest's sessions. This is how
  a shared code is revoked. *Probed:* the code changed.
- **`remove`**
  - Deletes the guest's account along with their per-user rows: availability,
    player notes and shares (`purge_user_data`, `_helpers.py:108`).
  - Pages they wrote stay, with no author (`_USER_ATTRIBUTION_COLUMNS`,
    `_helpers.py:42`).
- **`share`** returns invite text and a `mailto:` link that carry the code.
  The link is built from the server's `BASE_URL`.

## Shared

- No command gets a role tag or a permission hint. Every check is in the
  handlers.
- Campaign-level 404s use the campaign hint. A per-member or per-guest 404
  passes no hint, so the server's message ("Member not found", "Guest not
  found", "User not found") is shown instead.

## Stack

- `docker/docker-compose.yml` sets `GUEST_ACCESS_ENABLED=true`, so the guest
  commands can be smoke-tested.
- On an existing local stack, `docker compose up -d` recreates the container
  with its data kept.

## Docs and tests

- README: 10 rows.
- `IMPLEMENTED`: 10 entries. Coverage goes from 216 to 226.
- #100 gets a part-2 row for the asset routes.
- Unit tests:
  - Services: route and method for each command; the `update` body sends only
    the given fields; the `invite` body carries `user_id`; `create` sends
    `nickname` only when it is given.
  - Commands: required options; the `--status` choice; the credential
    sentence appears in the help of the four code-printing commands.
- Smoke test: a new `campaign membership` section using a throwaway group
  campaign named `Smoke Table`.
  1. Delete any leftover campaign with that name, then create it.
  2. `eligible` to find `player`'s user id, then `invite`.
  3. Log in as `player` into a scratch `GRIMOIRE_CONFIG`. Check that `invites`
     lists the campaign, then `update --status accepted`.
  4. As the owner, `update --character-name`, then `remove`.
  5. `guests create`, `list` and `share`, checking that the code appears in
     each. Then `regenerate` and check that the code changed.
  6. `guests remove`, then delete the campaign.
