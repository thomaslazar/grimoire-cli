# Campaign wiki note-template catalogue (A3.1 of #102)

Adds two commands to `campaigns wiki templates`, one per route, so a caller can
browse the community catalogue and copy a template into the campaign. A3
(`docs/specs/2026-10-15-campaign-wiki-templates-design.md`) covers the rest of
the group.

Server facts are read at `v1.8.0`. Line references are to
`backend/routers/campaigns/wiki_templates.py` (`:line`) and
`backend/services/wiki_template_catalogue.py` (`svc:line`). Everything was also
probed on the local stack against the fixture below and the live community
catalogue.

## Where the catalogue comes from

- The catalogue has no setting of its own. Its sources are the **add-on index
  URLs**, a comma-separated list set with `addons settings --index-url`, which
  needs the admin role (`svc:68`).
- For each source, if the URL serves a template index, the server uses it as
  is. Otherwise it tries `<same dir>/templates/index.json` (`svc:100`, `svc:112`).
- With the default add-on index, this resolves to
  `grimoire-codex/community-add-ons/main/templates/index.json`.
- With `DISABLE_EXTERNAL_ADD_ON_INSTALL` set, both routes answer 403. With one
  source, a failure answers 502 with the reason; with several, failing sources
  are skipped silently, and the catalogue can come back empty (svc:112,
  `fetch_catalogue`).

## Commands

### `campaigns wiki templates catalogue --id <campaign-id> [--refresh]`

Calls `GET /api/campaigns/{id}/wiki/templates/browse` (:527).

- `--refresh` sends `refresh=true` when set. The catalogue is cached on the
  server, and `refresh` forces a re-fetch (`svc:112`, `force`).
- Each entry carries id, name, system, category, description, author and
  `available_in`, but no body. `build_tree` (`svc:215`) drops `body_path`.
  Probed. The web UI shows the same fields and also has no preview
  (`frontend/src/components/campaigns/WikiTemplateBrowser.jsx:160-210`).
- `downloaded_ids` lists the catalogue ids this campaign already holds.
  Probed: it updates after an install.

### `campaigns wiki templates install --id <campaign-id> --source-id <catalogue-id> [--index-url <url>]`

Calls `POST /api/campaigns/{id}/wiki/templates/download/{template_id}[?index_url=…]` (:568).

- The command is called `install` because `download` already means saving bytes
  locally in this CLI. It is the same verb as `addons install`.
- `--source-id` takes the catalogue id. The installed template reports it back
  as `source_id`.
- `--index-url` chooses among sources when the id appears in more than one. It
  must equal the entry's `index_url` / `available_in[].index_url` exactly: that
  is the templates index, not the add-on index URL (`find_entry`, `svc:281`).
  - Probed: a non-matching URL answers 404 "That template is not in the
    catalogue", the same as an unknown id.
- The result is an ordinary template that records `source_id`, `source_url` (the
  templates index) and `source_version`. The body is checked against the
  catalogue's `body_sha256` (`svc:326`).
- A body that can't be fetched or fails `body_sha256` answers 502 (svc:326).
- Installing the same id again makes a second copy (docstring, :577).

### Shared

- Both go in the existing `CampaignWikiTemplateCommands.cs` and
  `CampaignWikiTemplateService.cs`.
- No role tag and no permission hint: both use `assert_can_manage`, so they are
  owner-only and refused while the campaign is archived. They use the group's
  `OwnerOnly` note.

## Help caveats

- `catalogue`:
  - Sources are the add-on index URLs (`addons settings --index-url`, admin).
    This cross-reference is needed to change what the command lists.
  - It answers 403 when the server disables external installs; 502 when the only source
    fails (with several, failing ones are skipped).
  - "Entries carry no template body; it is only readable once installed." Only
    this caveat is given, with no recipe naming other commands.
- `install`:
  - `--index-url` takes an `available_in[].index_url` value.
  - A non-matching URL answers 404, the same as an unknown id.
  - Installing again makes another copy.
  - It answers 403 when the server disables external installs; 502 when the
    catalogue or the body can't be fetched, or the body fails its sha256 check.

## Smoke fixture

- `docker/addon-index/templates/index.json` and
  `docker/addon-index/templates/generic/smoke-note/smoke-note.md` are checked
  in. The `addon-index` nginx serves them as
  `http://addon-index/templates/index.json`, which is the path derived from the
  fixture add-on index `http://addon-index/index.json`.
- The entry's `body_sha256` is the body file's sha256. The body is static, so a
  hand-edited body that doesn't match fails `install` in the smoke test.
- The smoke test's `campaign wiki` section runs after the add-ons section has
  restored the default index. In the template checks it:
  1. points `addons settings --index-url` at the fixture;
  2. runs `catalogue --refresh` and expects `smoke-note`;
  3. runs `install`, then expects `source_id == "smoke-note"`, and expects it in
     `downloaded_ids`;
  4. deletes the installed template;
  5. restores `$DEFAULT_INDEX_URL`.

## Docs and tests

- README: 2 rows.
- `IMPLEMENTED`: 2 entries. The coverage total goes from 195 to 197.
- Unit tests:
  - service: route and method; `refresh` sent only when set; `index_url` sent
    only when set;
  - commands: `--source-id` is required, and the `catalogue` help contains the
    no-body caveat.
- #102: mark A3 done (#105) and A3.1 in progress.
