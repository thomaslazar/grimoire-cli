# Campaign linking

**Status:** draft
**Issue:** [#51](https://github.com/thomaslazar/grimoire-cli/issues/51)
**Verified against:** `hunterreadca/grimoire:1.7.2`, the pinned stack

## Problem

An agent should be able to research the library, then put books and other
items into a campaign and organise them there. The CLI implements none of the
91 campaign operations. This covers the linking slice — campaigns themselves,
their resource links, resource categories and file uploads — 17 endpoints.

The play side (wiki, sessions, guests, members, sheets, invites, calendar,
banners, templates, images) is **deferred, not declined**: it stays an open
question on #51 and may come later.

## Verified server behaviour

Read from `backend/routers/campaigns/` at tag `v1.7.2`. Cites are relative to
that directory and that tag.

### What a resource is

A campaign's resources are its link table: each `CampaignResource` row points
at a library item by `resource_type` + `resource_id` and carries its own `id`,
`visibility`, `category_id` and `sort_order` (`resources.py:80-101`). A link is
a pointer, not a copy — unlinking leaves the library item alone — except for
`file`, whose target is a `CampaignFile` uploaded into the campaign and deleted
with its link (`resources.py:300-302`).

`update`, `remove` and `reorder` take the **link id**, not the library item's id.

### Permissions are ownership, not roles

Every write calls `assert_can_manage` (`_helpers.py:304-317`), which refuses
with 403 when:

- the caller is not the campaign's owner — there is no admin override;
- the owner's campaign access is disabled;
- the campaign is archived (`assert_not_archived`), read-only for the owner too.

Reads go through `can_view`: the owner or an accepted member
(`core.py:221`, `resources.py:116`, `categories.py:48`).

`create` has its own checks (`core.py:160-166`): guests are refused,
`is_gm_campaign: true` needs the gm or admin role, and disabled campaign access
refuses.

None of these is a route-level role dependency, so **no command gets a role
tag**. The refusal reasons go in help text instead.

The upload route's description says "GM or admin role required"
(`__init__.py:528`), but the handler calls `assert_can_manage`
(`uploads.py:552`) — owner only. Help follows the code.

### Endpoints

Prefix `/api/campaigns`.

| Method | Path | Handler |
|---|---|---|
| GET | `` | `list_campaigns` (`core.py:40`) |
| POST | `` | `create_campaign` (`core.py:155`) |
| GET | `/{id}` | `get_campaign` (`core.py:215`) |
| PATCH | `/{id}` | `update_campaign` (`core.py:264`) |
| GET | `/{id}/resources` | `list_resources` (`resources.py:110`) |
| POST | `/{id}/resources` | `add_resource` (`resources.py:143`) |
| POST | `/{id}/resources/bulk` | `bulk_add_resources` (`resources.py:189`) |
| PATCH | `/{id}/resources/{rid}` | `update_resource` (`resources.py:237`) |
| DELETE | `/{id}/resources/{rid}` | `remove_resource` (`resources.py:292`) |
| PUT | `/{id}/resources/reorder` | `reorder_resources` (`resources.py:269`) |
| GET | `/{id}/categories` | `list_categories` (`categories.py:41`) |
| POST | `/{id}/categories` | `create_category` (`categories.py:59`) |
| PATCH | `/{id}/categories/{cid}` | `update_category` (`categories.py:96`) |
| DELETE | `/{id}/categories/{cid}` | `delete_category` (`categories.py:186`) |
| PUT | `/{id}/categories/reorder` | `reorder_categories` (`categories.py:125`) |
| PUT | `/{id}/resource-group-order` | `set_resource_group_order` (`categories.py:148`) |
| POST | `/{id}/files` | `upload_campaign_file` (`uploads.py:534`) |

Skipped from the slice: `GET /resources/suggested/{system_id}` is a system's
books with the core category flagged, which `books list --system-id` gives;
`GET /resources/search` is the UI picker's name search, which `search` covers.

### Campaigns

- `list` hides archived campaigns unless `include_archived=true`, which returns
  them alongside the active ones (`core.py:40-55`).
- `CampaignCreate` accepts `resources`, linking items at creation
  (`_schemas.py:42-44`).

### Adding resources

- Valid types: `book map token audio model file` (`resources.py:149,208`).
- Neither add checks that `resource_id` exists; an unknown id links a dangling
  row whose `name` is the raw id (`resources.py:38-62`).
- `model` has no name lookup either, so a model link's `name` is always its id
  (`resources.py:38-62`).
- An invalid `visibility` silently becomes `gm` on both adds
  (`resources.py:164,213`); `update` rejects it with 400 (`resources.py:252`).
- A restricted book is clamped to `gm` whatever was asked
  (`_helpers.py:351-373`), on add and update.
- An unknown `category_id` is a 400 (`resources.py:65-77`); `""` clears it on
  update (`_schemas.py:96`).
- `private` is visible to the owner and the listed users only
  (`_helpers.py:336-348`); setting any other visibility clears `shared_user_ids`
  (`resources.py:260-262`).

### Single add vs bulk

Single add returns 409 on a duplicate (`resources.py:161-162`). Bulk skips
duplicates and unknown types silently and returns only the rows it created
(`resources.py:195-234`) — there is no `errors` field, so the only signal of a
skip is fewer rows back than were sent.

### Ordering

- `GET /resources` sorts by visibility (public, private, gm), then
  `sort_order`, then name (`resources.py:139`). Manual order therefore only
  holds within a visibility bucket.
- `resources reorder` and `categories reorder` renumber the ids they are given
  from 0 and silently skip unknown ones; unlisted rows keep their old
  `sort_order` and can collide (`resources.py:278-288`,
  `categories.py:134-145`).
- `resource-group-order` orders the resource panel's groups: `type:<t>` keys and
  `cat:<category-id>` keys, mixed. It keeps only known type keys and this
  campaign's resource categories, drops duplicates and everything else, and
  returns what it kept (`categories.py:163-183`). The docstring says unknown keys
  are "stored verbatim"; the code drops them. `type:model` is not a known key
  (`categories.py:27`), so the models group cannot be placed.

### Categories

- Kinds are `note` and `resource`, but creating `note` is a 400
  (`categories.py:67-71`); note categories are legacy wiki data.
- `delete` with an unknown id returns success (`categories.py:206-207`).
- `delete --mode delete_items` unlinks every resource in the category
  (`categories.py:231-234`). It deletes the link rows directly, so a `file`
  link's upload is orphaned on disk rather than removed.

### Uploads

`POST /{id}/files` is multipart (`file`, optional `category_id` /
`new_category_name`) and both stores the file and links it as a `file`
resource at `gm` visibility, returning the new link (`uploads.py:534-617`).
Limits: 200 MB hard cap (`uploads.py:51,561`); an admin-set per-file limit,
per-campaign total, or a global disable (403) — admins bypass all three
(`uploads.py:555-571`).

## Design

### Commands

```
campaigns list [--include-archived]
campaigns get --id <campaign-id>
campaigns create --input <file> | --stdin
campaigns update --id <campaign-id> --input <file> | --stdin
campaigns resources list --id <campaign-id>
campaigns resources add --id <campaign-id> --resource-type <t> --resource-id <item-id>
                        [--visibility <v>] [--category-id <id>]
campaigns resources bulk --id <campaign-id> --input <file> | --stdin
campaigns resources update --id <campaign-id> --link-id <link-id>
                        [--visibility <v>] [--category-id <id>]
campaigns resources remove --id <campaign-id> --link-id <link-id>
campaigns resources reorder --id <campaign-id> --ordered-ids <link-id>...
campaigns categories list --id <campaign-id>
campaigns categories create --id <campaign-id> --name <n> [--icon <i>] [--icon-color <c>]
campaigns categories update --id <campaign-id> --category-id <id> [--name] [--icon] [--icon-color]
campaigns categories delete --id <campaign-id> --category-id <id> [--mode <m>]
campaigns categories reorder --id <campaign-id> --ordered-ids <category-id>...
campaigns categories group-order --id <campaign-id> --ordered-keys <key>...
campaigns files upload --id <campaign-id> --file <path>
                        [--category-id <id> | --new-category-name <n>]
```

- **Ids are flags**, as everywhere else in the CLI: `--id` is the campaign;
  a second id is named for what it is (`--link-id`, `--category-id`). Other
  flags mirror the body field names.
- **Bodies.** `create`/`update` carry a metadata body, so they take JSON via
  `--input`/`--stdin` validated against `CampaignCreate`/`CampaignUpdate`.
  `resources bulk` takes a `ResourceBulkAdd` body (`{"resources": [...]}`) the
  same way. Resource add/update and category create/update are a handful of
  scalars, so they take flags.
- **Choices.** `--resource-type` is a `ChoiceOption` of
  `book map token audio model` — `file` is left out because the upload creates
  its own link, and no command produces a file id to link. `--visibility` is a
  `ChoiceOption` of `gm public private`, so a typo fails before the request
  instead of silently hiding the item. `--mode` is a `ChoiceOption` of
  `uncategorize delete_items`. `bulk` validates field names only; its help warns
  that a bad `visibility` value becomes `gm`.
- **No `--shared-user-ids` flag.** Nothing in the CLI lists user ids, so a
  `private` share list is set through `bulk` JSON only; `private` with no list
  is visible to the owner alone.
- **Kind.** `categories create` sends `kind: resource`; `categories list` sends
  `kind=resource`. Note categories are wiki data and out of scope.
- **`group-order` lives under `categories`** because its keys are categories
  mixed with type groups.
- **Upload content type** comes from the extension: png, jpeg, webp, gif and pdf
  map to their types, anything else is `application/octet-stream`. The server
  sets `is_image` from it (`uploads.py:589`).

### Exit codes

`resources bulk` exits 3 via `BulkExit` when fewer rows come back than were
sent. stdout still carries the server's response unmodified, so the caller can
see which rows landed. Every other command follows the existing 0/1/2 rule.

### Help caveats

Each at its call site, terse:

- every write: owner only, no admin override; refused when the owner's campaign
  access is disabled or the campaign is archived;
- `create`: `is_gm_campaign` needs gm or admin;
- `resources add`/`bulk`: ids are not checked to exist; restricted books are
  forced to `gm`; `model` links show the id as `name`; `add` 409s on a
  duplicate where `bulk` skips it (exit 3);
- `resources update`/`remove`/`reorder`: take the link id from
  `resources list`, not the item id;
- `resources update`: `--category-id ""` clears; non-`private` visibility clears
  shares;
- `resources remove`: removing a `file` link deletes the upload;
- `resources reorder`, `categories reorder`: pass every id — unknown ones are
  skipped and unlisted ones keep their old position; `resources list` orders by
  visibility first;
- `categories delete`: unknown id succeeds; `delete_items` unlinks the
  category's resources and orphans any uploads among them;
- `categories group-order`: keys are `type:book|map|token|audio|file` and
  `cat:<category-id>`; others, including `type:model`, are dropped;
- `files upload`: the link starts at `gm` visibility; size limits.

### Files

One command file and one service per group:
`CampaignsCommand.cs`, `CampaignResourcesCommands.cs`,
`CampaignCategoriesCommands.cs`, `CampaignFilesCommands.cs`, with matching
services.

## Testing

- Unit: `bulk` exit 3 on a short response and 0 on a full one; `--type` and
  `--visibility` reject values outside their sets; `RoleSectionTests` confirm no
  campaign command carries a role tag.
- Smoke, as the admin session the script already holds (admin may create and
  therefore own a campaign), idempotent: find-or-create a campaign
  with a fixed name; ensure one fixed-name resource category; link a fixture
  book with `add` (accept 409 on re-runs), set its visibility and category to
  fixed values; `bulk` the same book and assert exit 3; `group-order` with a
  fixed key list and assert the kept keys; upload a small fixture file, then
  remove its link so re-runs converge.

## Docs

- README Commands table.
- `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate
  `docs/grimoire-api-coverage.md`.
- `docs/grimoire-api-notes.md`: the behaviour above that help text does not
  already carry.
- Roadmap: the Campaigns open question is reworded to the deferred play side;
  the linking slice ships in the same PR, so it never needs a Next line.
- #51: a comment recording the slice shipped and the play side deferred.
