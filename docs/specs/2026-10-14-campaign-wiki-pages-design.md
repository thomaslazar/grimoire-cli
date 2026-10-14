# Campaign wiki pages — design

**Targets:** Grimoire **1.8.0**, the current floor. Read from `temp/grimoire` at
the `v1.8.0` tag (cites relative to `backend/` unless they name `frontend/`) and
probed against the local 1.8.0 stack on 2026-10-14.

**Tracking:** #102 (campaign prep and lore), piece **A1**. Scope decision: #51.

## Purpose

An agent acting as a campaign's prep and lore keeper writes and updates wiki
pages — NPCs, locations, lore — that link to each other and embed library items
the CLI already manages. A1 covers the pages themselves; import/export (A2),
note templates (A3) and session notes (A4) follow as their own specs.

## Commands

A new `campaigns wiki` subgroup in `src/GrimoireCli/Commands/CampaignWikiCommands.cs`
with its own `src/GrimoireCli/Services/CampaignWikiService.cs` (one file per
command group). Every command takes the campaign as `--id <campaign-id>` and a
page as `--page-id`, matching `campaigns resources` / `categories`.

| Command | Route | Input |
|---|---|---|
| `campaigns wiki list --id` | `GET /api/campaigns/{id}/wiki` | `--mine`, `--include-hidden` |
| `campaigns wiki get --id --page-id` | `GET …/wiki/{page_id}` | — |
| `campaigns wiki create --id` | `POST …/wiki` | JSON via `--input` / `--stdin`, validated against `WikiPageCreate` |
| `campaigns wiki update --id --page-id` | `PATCH …/wiki/{page_id}` | JSON via `--input` / `--stdin`, validated against `WikiPageUpdate` |
| `campaigns wiki delete --id --page-id` | `DELETE …/wiki/{page_id}` | — |
| `campaigns wiki search --id --query` | `GET …/wiki/search` | `q`, 1–200 chars |
| `campaigns wiki titles --id` | `GET …/wiki/titles` | — |
| `campaigns wiki reorder --id --ordered-ids <page-id>...` | `PUT …/wiki/reorder` | `ordered_ids` |
| `campaigns wiki hide --id --page-id` | `POST …/wiki/{page_id}/hide` | — |
| `campaigns wiki unhide --id --page-id` | `DELETE …/wiki/{page_id}/hide` | — |

Ten routes. The page body is markdown in the request's `body` string; the CLI
takes no `--body-file` and assembles nothing — an agent builds the JSON
(`jq -n --rawfile body page.md '{title:"…", body:$body}'`). No role tags: none
of the routes carries a role dependency; access is decided inside the handlers
(below) and stated in help.

## Server behaviour

Verified in source; the starred items also live.

- **Page model** (`models/campaigns.py:260-334`): `title`, `slug` (normalized
  title, unique per campaign), markdown `body`, `visibility`
  (`gm` | `group` | `members`), `page_type` (`note` | `session`, with
  `session_date` `YYYY-MM-DD`), `parent_id` (nesting to any depth), `icon`
  (Lucide key or emoji), `icon_color` (palette token or `#rrggbb`),
  `sort_order`. Sharing rows (`WikiPageShare`, `:376`) grant read, or read+write
  with `can_write`.
- **Create** (`routers/campaigns/wiki.py:261-327`): needs campaign membership;
  `visibility` outside the three values is a 400; a personal campaign stores
  every page as `gm`; an empty title becomes `Untitled`; a bad `session_date`
  is a 400; `parent_id` must be a page the caller can write
  (`_resolve_parent`).
- **Update** (`wiki.py:330-464`): needs edit access. All fields optional;
  `parent_id: ""` moves the page to the top level; `icon: ""` /
  `icon_color: ""` clear them. Only the author changes `visibility` or sharing
  (403 otherwise). A rename rewrites `[[Old Title]]` links in other pages
  (`rewrite_inbound_titles`).
- **Edit access** (`_wiki_helpers.py:67-87`): the author; any member for a
  `group` page; users with a write share for a `members` page. Campaign
  ownership alone grants nothing.
- **Delete** (`wiki.py:466-515`, `_wiki_helpers.py:89-97`): author only (403
  otherwise); children move up to the deleted page's parent; an unknown page id
  is a silent no-op.
- **Get** (`wiki.py:169-238`): the campaign owner receives the raw body; anyone
  else receives it with every `||…||` span removed, no marker left. `backlinks`
  lists visible pages linking here; share lists are shown to the author only.
- **GM secrets** (`_helpers.py:157-190`, `:225`): `||text||` spans belong to the
  campaign owner. A non-owner's `||…||` is stored as literal text, and on a
  non-owner's save the stored secrets are re-woven by position
  (`merge_gm_secrets`).
- **Links** (`routers/campaigns/wikilinks.py`): `[[Title]]`, `[[Title|label]]`,
  `[[Title:#Heading]]`, `[[Title:id-<page_id>]]` and combinations; parsed
  right-to-left, so titles may contain colons.
- **Stubs\*** (`wiki.py:1-10`, `_wiki_helpers.py:208`): every save re-parses the
  body and creates an empty page for each unknown *unpinned* `[[Title]]`
  (`[[Title|label]]` included), authored by the saver and inheriting the saving
  page's visibility. A pinned link to a missing page creates none. Live: `[[npc:goblin]]`
  created a stub titled `npc:goblin` — a prefix that is not reserved is a title.
- **Embeds\*** (`wikilinks.py` `EMBED_PREFIXES`; `frontend/src/components/campaigns/EmbedCard.jsx`,
  `GrimoireEmbedPicker.jsx:63-68`): `[[book:<id>]]`, `[[book:<id>:<page>]]`,
  `[[map:<id>]]`, `[[token:<id>]]`, `[[audio:<id>]]`, `[[model:<id>]]` take
  library ids; `[[image:<file-id>]]` (inline) and `[[file:<file-id>]]` (card)
  take campaign file ids. Embeds are never tracked as links and never create
  stubs. The web picker offers only items already linked to the campaign.
- **List** (`wiki.py:68-96`): visible pages, hidden ones excluded unless
  `include_hidden`; `mine` keeps the caller's own; ordered by `sort_order`, then
  title.
- **Search** (`wiki.py:578-617`): case-insensitive substring over title and
  body — no query syntax; non-owners do not match on secret text; hidden pages
  excluded.
- **Titles** (`wiki.py:620-`): every visible page's id, title, headings and
  `ambiguous` (another visible page shares the slug) — the source for `:id-` and
  `:#Heading` links.
- **Reorder** (`wiki.py:99-166`): `ordered_ids` re-deals the slots of the pages
  the caller can edit; unknown, invisible and read-only ids are dropped and keep
  their slots.
- **Hide / unhide** (`wiki.py:518-575`): per-user and private to the caller;
  hiding a parent hides its subtree; `hide` is a 409 in a personal campaign,
  `unhide` is always allowed.
- **Archived campaigns** refuse create, update, delete and reorder
  (`assert_not_archived`); hide/unhide are allowed.

## Help

A small `WikiPageHelp` beside the subgroup adds two sections to `create` and
`update`, the pattern `MediaBrowseOptions` uses for shared teaching:

```
Page body:
  body is markdown. [[Title]] links a page; [[Title|label]] shows label;
  [[Title:#Heading]] jumps to a heading; [[Title:id-<page-id>]] pins the
  page so the link survives renames (titles from campaigns wiki titles).
  Saving creates an empty stub for each unknown unpinned [[Title]], with
  this page's visibility; a pinned link to a missing page creates none.
  Renaming a page rewrites links to its old title.

  Embeds are not links: [[book:<id>]], [[book:<id>:<page>]], [[map:<id>]],
  [[token:<id>]], [[audio:<id>]], [[model:<id>]] take library ids;
  [[image:<file-id>]] and [[file:<file-id>]] take campaign file ids. Any
  other prefix is a page title. The web editor offers only items linked
  to the campaign.

  ||text|| is a GM secret: only the campaign owner sees it. Anyone else's
  ||…|| is stored as literal text.

Visibility:
  gm — the author alone (the GM's private pages, or a player's self-only
  note); group — every member, and every member can edit; members — the
  author plus shared_user_ids (read) and shared_write_user_ids (read and
  write). Only the author changes visibility or sharing. A personal
  campaign stores every page as gm.
```

Per-command Notes:

- `update` — send only the fields to change; `parent_id` `""` moves the page to
  the top level; `""` clears `icon` / `icon_color`. Needs edit access.
- `get` — non-owners receive the body with GM secrets removed, no marker.
- `delete` — author only; children move up to the deleted page's parent; an
  unknown page is a silent no-op.
- `search` — plain substring over title and body; hidden pages excluded.
- `hide` — affects only the caller's view; hiding a parent hides its subtree;
  refused (409) in a personal campaign.
- `reorder` — pages the caller cannot edit keep their slots.
- create/update/delete/reorder — refused while the campaign is archived.

## Testing

**Unit**

- `campaigns wiki` hosts the ten commands; required flags enforced.
- `create` / `update` reject an unknown body field (`JsonBodyInput.Validate`
  against `WikiPageCreate` / `WikiPageUpdate`).
- `search --query` bounded to 1–200 characters.
- Help carries the caveats: stubs, embed forms, GM secret syntax, delete
  re-parenting, hide's personal-campaign refusal.
- A service test pins each of the ten requests to its route and method.

**Smoke** (re-runs converge). Find-or-create a GM campaign named `Smoke Wiki`
(`is_gm_campaign: true` — the existing `Smoke Campaign` is personal, where
every page is `gm` and `hide` is refused). Each run creates its pages and deletes
every page it created, stubs included, before it ends:

- create a parent page, then a child under it whose body links `[[Smoke Stub]]`
  and embeds a seeded map; `list` shows the stub, `get` round-trips the body;
- `search` finds the child; `titles` lists all three;
- `update` renames the parent and the child's link to it is rewritten;
- `reorder`, `hide` / `unhide` succeed;
- `delete` the parent; the child is now top-level;
- delete the child and the stub; `list` is empty again.

## Docs

- README Commands table: ten `campaigns wiki` rows.
- `tools/generate-api-coverage.py` `IMPLEMENTED`: the ten routes; regenerate
  `docs/grimoire-api-coverage.md`.
- `docs/grimoire-api-notes.md`: a **Campaign wiki** section with the server
  behaviour above.

## Out of scope

- Import/export (A2), note templates (A3), session notes (A4) — #102.
- No floor change: everything here exists in 1.8.0.
- No `--body-file` or front-matter input: page bodies travel as JSON only.
