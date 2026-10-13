# System shelf commands — design

**Targets:** Grimoire **1.8.0**, the current floor. Read from `temp/grimoire` at
the `v1.8.0` tag (cites relative to `backend/`) and probed against the local
1.8.0 stack on 2026-10-12.

## Purpose

Expose the three routes 1.8.0 added for browsing one system's books a page at a
time, and gather everything about a system's books under one `systems books`
subgroup:

| Command | Route |
|---|---|
| `systems books list --id <system>` | `GET /api/systems/{id}/books` |
| `systems books folder-counts --id <system>` | `GET /api/systems/{id}/book-groups` |
| `systems books facets --id <system>` | `GET /api/systems/{id}/book-facets` |
| `systems books folders list\|set\|delete` | `/api/systems/{id}/book-folders` — **moved** from `systems book-folders` |

`systems get` returns every book in one response, which does not scale to a
system of tens of thousands of books (the reason upstream #221 added these
routes, `routers/systems/books.py:1-20`); `books list` pages but returns a
reduced row with no metadata. `systems books list` pages the **full** book
record with the shelf's filters, which is what a metadata sweep over a large
system needs. `facets` names the genres, tags, product-code prefixes and
categories a system actually uses, in one call, ahead of a vocabulary
clean-up. `folder-counts` is the book counterpart of the media `folder-counts`.

Moving `book-folders` under the subgroup is a **breaking rename**, taken
deliberately so the shelf mirrors the media collections:

| maps | systems |
|---|---|
| `maps list` | `systems books list --id` |
| `maps folder-counts` | `systems books folder-counts --id` |
| `maps folders …` | `systems books folders …` |

## Server behaviour

Verified in source and live (fixture: the `Das Schwarze Auge 5 DE` system,
three books — two in `core/`, one in `core/errata/`):

- **Routes** `routers/systems/__init__.py:72-104`, handlers
  `routers/systems/books.py:399-440`. All three are `require_not_guest`.
- **Visibility.** Every route starts from `ShelfBrowser.base`
  (`books.py:149-155`): the system's books, variants hidden, explicit books
  dropped for an account without explicit permission, and access control
  applied. The system itself 404s when the caller cannot see it.
- **Shelf filters** (`book_browse_params`, `books.py:73-127`), shared by
  `/books` and `/book-groups`:
  - `q` — bare text matches title, filename or product code
    (`routers/search/_books.py:137-149`); field prefixes are the book search
    fields (`services/search_query.py:86-100`: `title`, `author`, `artist`,
    `publisher`, `system`, `category`, `tag`, `year`, `isbn`, `code`,
    `language`, `description`, `filename`), with the aliases the `search`
    command documents. `text:` is ignored (`search_query.py:211-213`).
  - `tags` — the media lists' JSON tag filter, matched against the book's own
    tags only (`TagMatcher(..., folder_tags=False)`, `books.py:218`).
  - `favorites`, `added_since` (undated books never match), `explicit`
    (`true`/`false`).
  - `genre` — case-insensitive exact genre; `product_code` — a prefix,
    case-insensitive. Both also take `__grim:none__` / `__grim:any__`
    (`books.py:161-181`).
  - `category` — exact, case-sensitive. `folder` — the subfolder below the
    category directory, `""` for the category's own; **`folder` without
    `category` is a 400** (`books.py:112`).
  - `sort` — `title`, `year`, `page_count`, `size`, `product_code`,
    `added_at` (`books.py:43`; unknown → 422, `:110`); `order` — `asc`/`desc`.
    Books with no year or date sort last either way.
- **`/books`** — `limit` 1–500, default 100; `offset` (`books.py:401-402`).
  Returns `{total, books}`; each book is the full `BookOut` record, the shape
  `systems get` embeds (`_schemas.py:279-283`). Live: total 3;
  `category=core&folder=errata` → `DSA5 Errata`.
- **`/book-groups`** — returns `{total, groups: [{category, path, dir, count,
  first}]}` (`books.py:281-323`, `_schemas.py:286-303`):
  - one row per stored directory; `path` is the subfolder below the category
    directory (`""` for books directly in it); `dir` the library-relative
    directory; `count` the matching books directly in it;
  - `first` is the active sort's value for the folder's first book, null under
    `title` — so `sort`/`order` do change this response;
  - two directories can share a `(category, path)` and come back as separate
    rows.
  - Live: `core`/`""` = 2, `core`/`errata` = 1.
- **`/book-facets`** — `{categories, genres, product_code_prefixes, tags}`
  over every visible book, before any filter; takes no filters
  (`books.py:338-380`). Live: `categories: ["core"]`, the rest empty.

## Commands

A new `src/GrimoireCli/Commands/SystemBookCommands.cs` builds the `systems
books` subgroup, registered from `SystemsCommand` the way `CoverCommands` is,
with a new `src/GrimoireCli/Services/SystemBooksService.cs` for the three
routes (one file per command group). The moved `folders` subgroup keeps its
code in `BookFolderCommands.cs` and its service methods in `SystemsService`.

**Shelf filter flags** mirror `book_browse_params` exactly, declared once in a
`ShelfBrowseOptions` beside the subgroup (the server declares them once):
`--query`, `--tags`, `--favorites`, `--added-since`, `--explicit
true|false`, `--genre`, `--product-code`, `--category`, `--folder`, `--sort`
(the six values above), `--order`. `list` adds `--limit` (1–500, default 100)
and `--offset`; `facets` takes `--id` only. No role tag.

**Help.** The shared options add:

- a **Query syntax** section: bare text matches title, filename or product
  code; the book fields with their aliases; different fields narrow, repeating
  one widens; quote multi-word values.
- a **Filters** section: `--tags` takes the media lists' JSON (taught inline)
  but matches only the book's own tags, not folder tags; `--genre` and
  `--product-code` (a prefix) also take `__grim:none__` / `__grim:any__`;
  `--category` is the normalised value and case-sensitive; `--folder` is the
  subfolder below the category, `""` its own directory, and needs
  `--category`; undated books never match `--added-since` and sort last.

Per-command Notes:

- `list` — page with `--offset` against `total`; variants are hidden, and the
  account's explicit permission and access settings filter the shelf
  server-side.
- `folder-counts` — each row is one directory: `count` covers books directly
  in it, not its subfolders; `dir` is library-relative; `first` is the active
  sort's value for the folder's first book (null under `title`); two
  directories can share a category and path.
- `facets` — values come from every book on the system the account can see,
  before any filter.

## The move (breaking)

`BookFolderCommands.Create()` names its group `folders` and is registered under
`systems books` instead of `systems`. Flags and behaviour are unchanged. Every
literal `systems book-folders` in help examples, Notes (`book-folders set` →
`folders set`), the not-found hint (`SystemsService.cs:204`), doc comments
(`MapsService.cs:10`, `FilesFolderCommands.cs:11`, `MapFolderCommands.cs:9`),
tests (`BookFolderCommandTests`, `RoleSectionTests`), the smoke test, the
README, the coverage labels and the live docs (`grimoire-api-notes.md`,
`cli-design.md`, `grimoire-compatibility.md`) changes in the same commit, so
the tree is never half-moved. Dated specs, plans and
`grimoire-1.6.0-migration.md` stay as written.

The commit carries both breaking markers:

```
refactor!: move systems book-folders under systems books

BREAKING CHANGE: systems book-folders list|set|delete is now
systems books folders list|set|delete; flags and behaviour are
unchanged.
```

## Release tooling

The release skill drops a breaking commit today: its changelog filter is
`grep -E "^- (feat|fix|refactor|docs|test|ci|chore):"`
(`.claude/skills/release/SKILL.md:179`), which `refactor!:` does not match, and
nothing looks for a footer. abs-cli marks breaking changes with a
`BREAKING CHANGE:` footer and calls them out in the release notes by hand; this
makes the call-out mechanical. As the PR's **first** commit:

- **Release skill, step 1** — scan the commit bodies since the last tag for
  `BREAKING CHANGE:` footers and `type!:` subjects. If any, the proposed
  version is at least a minor bump (0.x allows breaking changes only between
  minors), and the version gate lists each breaking change.
- **Release skill, step 4** — the changelog filter accepts `type!:`
  (`^- (feat|fix|refactor|docs|test|ci|chore)!?:`); release notes gain a
  **Breaking changes** section, before Highlights, quoting each footer; the
  opening paragraph names the count ("Minor release with one breaking
  change").
- **`docs/releasing.md`** — the same rule beside the versioning rules.
- **`CLAUDE.md` Git Conventions** — mark a breaking change with `!` after the
  type and a `BREAKING CHANGE:` footer naming the old and new form.

## Commits

1. `docs:` release tooling recognises breaking changes.
2. `feat:` `systems books list | folder-counts | facets`.
3. `refactor!:` the move, with the footer above.
4. `docs:` README rows, coverage, API notes.

## Testing

**Unit**

- `systems books` hosts `list`, `folder-counts`, `facets`, `folders`;
  `systems` no longer hosts `book-folders`.
- The shelf flags parse on `list` and `folder-counts`; an unknown `--sort` or
  `--order` is rejected; `--limit` outside 1–500 is rejected.
- Help carries the caveats: `--folder` needs `--category`; `--tags` matches
  the book's own tags only; folder counts are direct-only; facets ignore
  filters.
- A service test pins each request to `/api/systems/{id}/books`,
  `/book-groups` and `/book-facets`, and checks the filters reach the query
  string.

**Smoke** (read-only; a re-run converges), on `Das Schwarze Auge 5 DE`:

- `systems books list` → `total` 3.
- `systems books list --category core --folder errata` → only `DSA5 Errata`.
- `systems books folder-counts` → `core`/`""` = 2, `core`/`errata` = 1.
- `systems books facets` → `categories == ["core"]`.
- `systems books list --folder errata` without `--category` exits non-zero.
- The existing book-folder steps run as `systems books folders …`.

## Docs

- README Commands table: rows for the three new commands; the three
  `book-folders` rows renamed.
- `tools/generate-api-coverage.py` `IMPLEMENTED`: the three new routes, the
  `book-folders` labels renamed; regenerate `docs/grimoire-api-coverage.md`.
- `docs/grimoire-api-notes.md`: a **System shelf** section with the server
  behaviour above.

## Out of scope

- No floor change: the routes are new in 1.8.0, already the floor.
- `systems get` and `books list` are unchanged.
- No alias for the old `systems book-folders` name: the break is announced
  through the release notes instead.
