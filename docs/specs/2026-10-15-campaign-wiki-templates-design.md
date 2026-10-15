# Campaign wiki note templates (A3 of #102)

A note template is a reusable starting point for a wiki page. It belongs to one
campaign and is used to create pages. This spec covers building and using
templates: 8 routes. Browsing and installing from the community catalogue
(`browse`, `download/{id}`) is split out as A3.1.

Server facts are read at `v1.8.0`, `backend/routers/campaigns/wiki_templates.py`
(cited as `:line`), and probed on the local stack.

## Group

- Commands: `campaigns wiki templates`, in a new
  `src/GrimoireCli/Commands/CampaignWikiTemplateCommands.cs`. The group is
  registered from `CampaignWikiCommands.Create()`.
- Service: a new `src/GrimoireCli/Services/CampaignWikiTemplateService.cs`
  (one file per command group).
- Flags: `--id <campaign-id>` and `--template-id <template-id>`.
- Access: every route calls `assert_can_manage`, so all of them are campaign
  owner only, and an archived campaign answers 409. The gate is ownership, not
  a role: no role tag and no permission hint.
- An unknown template is a 404 on every per-template route, including
  `DELETE` (`_get_template_or_404`, :146-155). Probed: a second delete answers
  404.

## Commands

| Command | Route | Notes |
|---|---|---|
| `list --id` | `GET templates` (:174) | |
| `get --id --template-id` | `GET templates/{t}` (:211) | |
| `create --id {--input\|--stdin}` | `POST templates` (:223) | validated against `WikiTemplateInput` |
| `update --id --template-id {--input\|--stdin}` | `PATCH templates/{t}` (:256) | validated against `WikiTemplateUpdate` |
| `delete --id --template-id` | `DELETE templates/{t}` (:294) | |
| `import --id --file <path>` | `POST templates/upload` (:307) | one multipart `file` part, filename = local basename |
| `export --id --template-id --output <path\|->` | `GET templates/{t}/export` (:425) | bytes, like `wiki export` |
| `use --id --template-id` | `POST templates/{t}/use` (:483) | |

`import` is named for `wiki import`, the same action one level up. It pairs
with `export`, whose zip it reads back.

## Help caveats

All of these are verified in source and probed on the local stack.

- **`create` / `update`: defaults.**
  - `defaults` holds the starting fields of a page made by `use`: title, icon,
    icon_color, visibility and page_type. They are stored as frontmatter on
    `body`.
  - On create, omitting `defaults` keeps any frontmatter already in `body`
    (:98).
  - On update, a sent `defaults` replaces all five fields, and an omitted field
    resets to its default (`WikiTemplateDefaults`, `_schemas.py:225`).
    Probed: `{"defaults":{"title":"X"}}` cleared `icon` and reset `page_type`
    to `note`.
  - A body-only update keeps the current defaults (:284).
- **`create` / `update`: cleaning.** An unrecognised `visibility` or
  `page_type` is stored as `gm` / `note` (`_frontmatter.py:134,140`). Probed:
  `bogus` was stored as `gm`.
- **`create` / `update`: body syntax.** The `body` is a wiki page body. Its
  link, embed and secret syntax is taught by `campaigns wiki create --help`,
  and the help says so in one line.
- **`get`.** `body` comes back without its frontmatter; those fields are in
  `defaults` (`_detail`, :124).
- **`use`.**
  - Every use creates a new page and never overwrites one.
  - The title is `defaults.title`, else the body's first `# heading`, else the
    template name (:497 via `parse_markdown_file`). Probed all three.
  - A repeated title gets a suffixed slug: probed `x`, then `x-2`.
  - `session_date` is never set, even for a session page (it is not passed at
    :499-509).
  - Unresolved `[[links]]` get stubs, as on `wiki create` (:515).
  - There is no title override, so rename the page with `campaigns wiki update`.
- **`import`.**
  - Accepts `.md` / `.markdown` / `.txt`, or a template `.zip` (:342). The zip
    holds its first `.md` and an optional `.yml` manifest (:366), so an export
    re-imports. Probed the round trip.
  - The name comes from the manifest, else the frontmatter title, else the
    filename (:412, :416).
  - Maximum size is 512 KB (`_MAX_UPLOAD_BYTES`, :61).
- **`export`.** A zip of `<slug>/<slug>.yml` and `<slug>/<slug>.md` (:425).
  Probed `npc/npc.yml` and `npc/npc.md`.

## Docs and tests

- README Commands table: 8 rows.
- `tools/generate-api-coverage.py` `IMPLEMENTED`: 8 entries, then regenerate
  `docs/grimoire-api-coverage.md` (187 → 195).
- Unit tests:
  - Service: route and method for each of the 8; the `file` part and its
    filename.
  - Commands: required options, `--output` required on export, and
    `import`/`use` present.
- Smoke test, in the `Smoke Wiki` section before its page cleanup:
  - clear templates left by an interrupted run;
  - create a template with defaults, then `get` it;
  - `update` its body;
  - `use` it and check the new page's title and visibility;
  - `export` it, `import` the zip and check the name;
  - delete both templates.

  The section's existing delete-all page cleanup removes the page `use` made,
  so a re-run converges.
- #102: A3 becomes 8 routes, and a new A3.1 row holds `browse` + `download`,
  the offline catalogue fixture (`docker/addon-index/templates/index.json`)
  and the `catalogue` / `install --source-id` naming.
