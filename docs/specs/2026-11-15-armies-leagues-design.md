# Armies, leagues and system factions

This increment wraps the wargame routes added in Grimoire 1.11.0: armies (14
routes), leagues (23 routes) and `GET /api/systems/{id}/factions`. Every
command is a thin pass-through to one endpoint. Source cites are v1.11.0 and
relative to `backend/`. `A/` is `routers/armies/` and `L/` is
`routers/leagues/`.

## Access, in general

- Every `/armies` and `/leagues` route depends on `require_wargame_access`
  (`A/__init__.py:36`, `L/__init__.py:42`). It admits full accounts and league
  guests, and returns 403 to a campaign's guests. That is not a role, so these
  routes carry **no role tag**.
- Four routes depend on `require_gm_or_admin`, so they are tagged `gm or admin`
  with the hint "the gm or admin role":
  - `POST /leagues` (`L/core.py:48`)
  - the three `/armies/publication-links` routes (`A/core.py:254,263,280`)
- Organiser checks are made inside the handlers (`L/_helpers.py:39-46`).
  - A caller who is not in the league gets 404 "League not found".
  - A member who is not the organiser gets 403.
  - An admin counts as the organiser of every league.
  - These rules go in each command's Notes.
- Army access (`A/_helpers.py:24-60`):
  - An army is visible to its owner, an admin, and the members of any league
    where it is a member's registered army. Anyone else gets 404.
  - Only the owner or an admin may change it. Anyone else gets 403.
- `GET /systems/{id}/factions` depends on `require_not_guest`
  (`routers/systems/__init__.py:121`). It gets no tag.

## Commands

| Command | Endpoint | Tag |
|---|---|---|
| `armies list` | `GET /api/armies` | — |
| `armies get --id` | `GET /api/armies/{id}` | — |
| `armies create {--input\|--stdin}` | `POST /api/armies` | — |
| `armies update --id {--input\|--stdin}` | `PATCH /api/armies/{id}` | — |
| `armies delete --id` | `DELETE /api/armies/{id}` | — |
| `armies import --file [--name]` | `POST /api/armies/import` | — |
| `armies export --id [--format] --output <path\|->` | `GET /api/armies/{id}/export` | — |
| `armies publications --id` | `GET /api/armies/{id}/publications` | — |
| `armies progress --id --unit-id [--experience] [--honours …] [--notes]` | `PUT /api/armies/{id}/units/{unit_id}/progress` | — |
| `armies games` | `GET /api/armies/games` | — |
| `armies factions --system-id` | `GET /api/armies/games/{system_id}/factions` | — |
| `armies publication-links set --publication --book-id [--system-name] [--page-offset]` | `PUT /api/armies/publication-links` | gm or admin |
| `armies publication-links remove --publication [--system-name]` | `DELETE /api/armies/publication-links` | gm or admin |
| `armies publication-links books --query` | `GET /api/armies/publication-links/books` | gm or admin |
| `systems factions --id` | `GET /api/systems/{id}/factions` | — |
| `leagues list` | `GET /api/leagues` | — |
| `leagues get --id` | `GET /api/leagues/{id}` | — |
| `leagues create {--input\|--stdin}` | `POST /api/leagues` | gm or admin |
| `leagues update --id {--input\|--stdin}` | `PATCH /api/leagues/{id}` | — |
| `leagues delete --id` | `DELETE /api/leagues/{id}` | — |
| `leagues eligible --id` | `GET /api/leagues/{id}/eligible-members` | — |
| `leagues members add --id --user-id` | `POST /api/leagues/{id}/members` | — |
| `leagues members remove --id --user-id` | `DELETE /api/leagues/{id}/members/{user_id}` | — |
| `leagues members army --id --user-id [--army-id]` | `PUT /api/leagues/{id}/members/{user_id}/army` | — |
| `leagues games create --id {--input\|--stdin}` | `POST /api/leagues/{id}/games` | — |
| `leagues games update --id --game-id {--input\|--stdin}` | `PUT /api/leagues/{id}/games/{game_id}` | — |
| `leagues games delete --id --game-id` | `DELETE /api/leagues/{id}/games/{game_id}` | — |
| `leagues guests list --id` | `GET /api/leagues/{id}/guests` | — |
| `leagues guests create --id [--nickname]` | `POST /api/leagues/{id}/guests` | — |
| `leagues guests regenerate --id --user-id` | `POST …/guests/{user_id}/regenerate` | — |
| `leagues guests share --id --user-id` | `GET …/guests/{user_id}/share` | — |
| `leagues guests remove --id --user-id` | `DELETE …/guests/{user_id}` | — |
| `leagues books list --id` | `GET /api/leagues/{id}/books` | — |
| `leagues books search --id --query` | `GET /api/leagues/{id}/books/search` | — |
| `leagues books add --id --book-id [--visibility] [--shared-user-ids …]` | `POST /api/leagues/{id}/books` | — |
| `leagues books update --id --resource-id [--visibility] [--shared-user-ids …]` | `PATCH /api/leagues/{id}/books/{resource_id}` | — |
| `leagues books remove --id --resource-id` | `DELETE /api/leagues/{id}/books/{resource_id}` | — |
| `leagues books reorder --id --ordered-ids …` | `PUT /api/leagues/{id}/books/order` | — |

### Input conventions

- Body formats:
  - Army, league and game bodies come from `--input` or `--stdin` as JSON.
    They are validated against the generated `ArmyCreate`, `ArmyUpdate`,
    `LeagueCreate`, `LeagueUpdate` and `GameIn` models, the same way as
    `campaigns create`.
  - Every other body is a handful of scalars and is given as flags.
- Choice options:
  - `--format`: `text` or `roster`
  - `--visibility`: `public`, `private` or `organizer`
- `armies import` uploads `--file` as the multipart field `file`, with an
  optional form field `name`.
- `armies export` writes bytes to `--output`, or to stdout when it is `-`, as
  `campaigns wiki export` does.
- The flag and verb names follow the existing campaign groups:
  - `eligible` mirrors `campaigns members eligible`.
  - `guests list|create|regenerate|share|remove` mirrors `campaigns guests`.
  - `books add|update|remove|reorder` mirrors `campaigns resources`.
- Each group gets its own command file and its own service:
  - `ArmiesCommand`
  - `ArmyPublicationLinksCommands`
  - `LeaguesCommand`
  - `LeagueMembersCommands`
  - `LeagueGamesCommands`
  - `LeagueGuestsCommands`
  - `LeagueBooksCommands`

  `systems factions` goes into `SystemsCommand`, with its method on the
  existing systems service.

## Behaviour the help must carry

Each item names the commands whose Notes carry it.

**Armies**
- `list`: only your own armies, admins included (`A/core.py:29-40`).
- `create` and `update` (`A/_helpers.py:63-90`):
  - `game_system_id` must be a visible wargame system, else 400.
  - `faction_id` must belong to it, else 400. A faction given without a
    system sets the system.
  - `""` clears either field.
  - At most 500 units, each with a name. A unit `id` is kept when given.
- `update` (`A/core.py:133-162`):
  - It is partial.
  - `units` replaces a hand-built army's whole list. Keep unit ids to keep
    their progress.
  - On an imported army, `units` is a 400.
  - Changing the system without a faction clears the faction.
- `import` (`A/core.py:70-107`, `services/armies/battlescribe.py`):
  - Takes a BattleScribe or New Recruit `.ros` or `.rosz` file, detected by
    content.
  - Over 20 MiB is a 413. A file that isn't a roster is a 400.
  - The system and faction are matched by name where they can be, and
    otherwise left null.
- `export`: `roster` returns the imported file unchanged, and is a 404 for a
  hand-built army (`A/core.py:200-233`).
- `progress` (`A/core.py:170-183`):
  - Replaces that unit's whole entry, so omitted flags reset to 0, empty or
    "".
  - Unit ids are not checked. They are the `data.forces[].units[].id` values
    from `get`, recursively through `children` and `forces`.
- `delete`: un-registers the army from leagues and blanks it in past games.
  The games stay (`models/leagues.py:83-85,147-154`).
- `factions`: 404 unless the system is a wargame you can choose
  (`A/core.py:310-311`).
- `publications`: a league guest always gets empty `suggestions`
  (`services/armies/links.py:109-136`).

**Publication links** (`A/core.py:250-286`, `services/armies/links.py`)
- `set` is an upsert keyed on `system_name` (case-insensitive) plus
  `publication`. Every army that cites that publication under that system
  shares the one link. `page_offset` is added to the cited page.
- `set` returns 404 for a book you can't see.
- `remove` returns 204 even when no link existed.
- `books` returns wargame books only, at most 10. `--query` takes 2 to 200
  characters.

**System factions**
- A role-playing system gives an empty list.
- League guests get 403 and use `armies factions` instead
  (`routers/systems/__init__.py:112-121`).

**Leagues**
- `list`: leagues you organise or belong to. An admin sees every league
  (`L/core.py:33-43`).
- `create`: the organiser is not a member until added, so is not in the
  standings and cannot be a game player. `points_limit` is stored but never
  enforced (`L/core.py:46-60`).
- `update` (`L/core.py:76-88`):
  - Organiser only, and partial.
  - `status: "finished"` is only a label.
  - Changing the win, draw or loss points re-scores the standings.
- `delete`: organiser only. It removes the shares, games and memberships.
  Armies and guest accounts stay (`L/core.py:91-105`).
- `eligible`: organiser only. It lists full accounts that are not yet
  members.

**Members** (`L/core.py:128-180`)
- `add`:
  - Organiser only.
  - An unknown user, or a guest, is a 404. Guests join through
    `leagues guests create`.
  - Adding an existing member again is a no-op.
- `remove`:
  - With your own id, you leave the league. Removing anyone else is
    organiser only.
  - The member's games stay, and they drop out of the standings.
- `army`:
  - Yours, or anyone's if you are the organiser.
  - The army must belong to that member, else 400. Omitting `--army-id`
    clears it.
  - A registered army becomes readable to the whole league.

**Games** (`L/core.py:186-268`, `services/leagues.py:19-40`)
- Body fields:
  - Both players must be different, current members.
  - `played_on` is `YYYY-MM-DD`.
  - `result` (`a`, `b` or `draw`, from A's side) is inferred from the scores
    when both are given, and must match them. With neither, the request is a
    400.
- Who may log or edit:
  - A non-organiser can only log games they played in.
  - Only the organiser, the recorder or the players may change or delete a
    game.
- `update` replaces the whole game.

**Guests** (`L/guests.py`)
- `create`:
  - Organiser only.
  - 403 when guest access is disabled on the server.
  - The response carries the `guest_code`. The nickname becomes the display
    name.
- `regenerate`: the old code stops working and the guest's sessions end. A
  guest who also plays in another organiser's league is a 403.
- `remove`: the guest only leaves the league. The account, the armies and the
  sign-in remain. This differs from `campaigns guests remove`.
- `list`: `guest_code` is null unless you organise every league the guest
  is in.
- API keys are refused on `list`, `create`, `regenerate` and `share`
  (`openapi_extra=api_keys.EXCLUDED`). The CLI signs in with a login token,
  so this affects nothing here and the help does not mention it.

**Books** (`L/books.py`)
- Organiser only, except `list`.
- `--resource-id` and `--ordered-ids` take the league-resource `id` from
  `list`, not the `book_id`.
- `add`:
  - Visibility defaults to `organizer`.
  - A restricted book is clamped to `organizer` whatever is asked.
  - Adding a book already shared is a 409.
  - `--shared-user-ids` applies only to `private` and must be members, else
    400.
- `update`: any visibility other than `private` drops all shares.
  `--shared-user-ids` replaces the share set.
- `search`: `--query` takes 2 to 200 characters. It searches books of any
  game type, the league's own system first, at most 20, and does not exclude
  books already shared.
- `list`: a member sees `public` books, plus the `private` books shared with
  them.

## Cross-cutting

- In the same change:
  - the README Commands table
  - `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate
    `docs/grimoire-api-coverage.md` (38 more routes covered)
  - `docs/grimoire-api-notes.md`, with sections on armies, leagues and system
    factions taken from the behaviour above
- The version floor stays at 1.10.1. These routes return 404 on older
  servers.

## Testing

- **Unit tests**, in the per-command style:
  - URLs and methods
  - the flag-built bodies
  - the choice options
  - role tags on the four gated commands, and on nothing else
  - multipart for `import`
  - the query for `publication-links remove`
- **Smoke test**, idempotent and writing only to the local stack:
  1. Clear up first: delete leftover "Smoke …" leagues and armies by name.
  2. Create an army by hand with one unit (`id: "u1"`). Set `progress` on
     `u1` and assert it.
  3. `export --format text --output -` is non-empty. `list` holds the army.
  4. `systems factions` on SR4 gives `[]`.
  5. Create a league as admin. Add admin and gm as members.
  6. Register the army for admin.
  7. Log a game, admin against gm, with scores 3 and 1. Assert the result is
     `a` and admin tops the standings. Delete the game.
  8. Share an SR4 book with the league (`books add`), `update` it to
     `public`, then `remove` it.
  9. Delete the league, then the army.

  `import` and `publication-links` are covered by unit tests only, because no
  roster fixture is kept. `leagues guests` is unit-tested only. Removing a
  league guest keeps its account, and the CLI has no command that deletes a
  user, so every run would leave a guest account behind.
