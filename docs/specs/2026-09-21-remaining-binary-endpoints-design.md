# remaining binary endpoints — design

Closes [#48](https://github.com/thomaslazar/grimoire-cli/issues/48) and finishes
the `books` group, the last incomplete one in library management.

Every server-behaviour claim below cites `temp/grimoire` at `v1.7.1`, the tag the
local stack's image is pinned to.

## Commands

| Command | Endpoint | Perm | Output |
|---|---|---|---|
| `books file --id <id> --output <path>` | `GET /api/books/{id}/file` | — | stream |
| `books page --id <id> --page <n> [--width <px>] --output <path>` | `GET /api/books/{id}/page/{n}` | — | stream |
| `downloads archive --type <t> [scope flags] [--fmt <f>] --output <path>` | `GET /api/downloads/archive` | — | stream |

All three follow the settled `--output` convention: required flag, `-` for
stdout, a `SavedFile` receipt for a path, `SendStreamAsync`, and
`BodyInputException` mapped to exit 1.

**None of the three passes a `notFoundHint`.** Every 404 they can raise carries
a real message — "Book not found", "File not found on disk", "Page not found in
archive", "No files found for the requested scope" — so a hint would replace
information rather than supply it.

## Verified server behaviour

- **`books page` is not a clone of `maps page`.** Its `width` defaults to
  **1200**, where maps defaults to 1600; both cap at 3000
  (`routers/books/pages.py:106`).
- **`books page` handles three document kinds.** It renders PDF, EPUB and DjVu
  to WebP; serves a **comic archive's** page as the image member it already is,
  without rendering (`pages.py:148-156`); and serves a single-page image book
  as stored, page 1 only (`pages.py:126-134`). Anything else 404s
  (`pages.py:158-159`). This is what makes it the complement to the reading
  commands: for a comic, `books page-text` 404s and `books page-words` returns
  an empty overlay, but `books page` returns the page.
- **Both book commands mutate on a missing file.** When the file is gone from
  disk they set the book's `is_missing` to true and commit before raising 404
  (`routers/books/core.py:388-392`, `pages.py:130,141`). A GET with a write
  side effect is worth stating in help.
- **`books page` is ETag-cached**, keyed on a token over the file's contents
  plus the page and width, so a replaced file renders under a new key
  (`pages.py:116-122`).
- **`downloads archive` takes eleven scope types**, each requiring a different
  combination of `id`, `category`, `tag`, `resource_type` and `folder`
  (`routers/downloads/core.py:27-144`). A missing required flag is 400 naming
  the flag and the type. `library_folder` additionally requires the admin role
  and is refused 403 otherwise — it reads any folder as it sits on disk,
  including files the scanner never indexed, so it is not filtered by book
  visibility the way the other scopes are (`core.py:131-141`).
- **An empty scope is 404** "No files found for the requested scope", and an
  unknown `fmt` is 400 listing the valid ones
  (`routers/downloads/_helpers.py:98-101`).
- **Archives stream as they are built** (`StreamingResponse` over a generator),
  so the first byte arrives without waiting for the whole archive.

## The archive scope table

This is a mini-language in a flag, so the help text has to teach it — it is the
only way to use the command. The `--type` value determines which other flags are
required:

| `--type` | also needs |
|---|---|
| `system` | `--id` |
| `system_category` | `--id --category` |
| `book_folder` | `--id --folder` |
| `map_folder`, `token_folder`, `audio_folder`, `model_folder` | `--folder` |
| `library_folder` | `--folder` (admin only) |
| `tag` | `--tag` |
| `tag_type` | `--tag --resource-type` |
| `tag_folder` | `--tag --resource-type --folder` |

`--fmt` is `zip` (default), `tar`, `tar.gz` or `tar.bz2`.

No client-side validation of `--type` or `--fmt`. The server validates both and
answers 400 with its own list, which is this repo's settled convention for a
value set the server owns.

## Implementation

`BooksService` gains two sends; a new `DownloadsService` gains one, with the
archive's six optional query parameters passed through as given. `BooksCommand`
gains `file` and `page`; a new `DownloadsCommand` hosts `archive` and is
registered in `Program.cs`.

`downloads` is a one-command group. That matches the API's own tag, which is
what `docs/grimoire-api-coverage.md` groups by, so the table and the CLI stay
aligned.

No timeout override. The server streams the archive as it builds it, so the
client's default 100-second budget covers the response headers rather than the
body, and no service in this repo passes a custom timeout.

## Testing

- Service tests pin the two book paths, `books page`'s `width` wire name, and
  **all seven** archive query-parameter wire names (`type`, `fmt`, `id`,
  `category`, `tag`, `resource_type`, `folder`) — a regeneration that renamed
  `resource_type` to `resourceType` would send a parameter the server ignores
  and silently widen the scope of an export.
- Command tests cover `--output` required on all three, `--page` required on
  `books page`, `--type` required on `downloads archive`, no role section on
  any of them, and the scope table being present in the archive's help.

Smoke, against the seeded fixtures:

- `books file` downloads a non-empty file and reports a byte count.
- `books page --page 1` renders; `--page 99` is refused.
- `downloads archive --type system --id <fixture system>` writes a zip whose
  first bytes are the zip magic `PK`, and which `unzip -l` can list.
- An unknown `--fmt` is refused, and a scope that resolves to nothing 404s.

## To verify against the local stack, not assume

- Whether a fixture tag resolves to a non-empty archive, so `--type tag` can be
  exercised rather than only the system scope.
- That a container system holding no books directly really 404s, as #48 claims —
  the fixtures include container systems, so this is checkable.
- Whether `books page` on the fixture PDFs honours `--width` with a visibly
  different byte count, or whether the ETag cache makes a second call a 304 the
  CLI surfaces oddly.

## Docs in the same PR

Three README Commands rows; three `IMPLEMENTED` entries in
`tools/generate-api-coverage.py`, then regenerate; `docs/grimoire-api-notes.md`
for the `is_missing` side effect, the 1200-versus-1600 width default, the comic
and image branches of `books page`, and the archive scope matrix.

## Not doing

- Any subcommand-per-scope surface for the archive (`downloads archive tag …`).
  Eleven scopes would become eleven subcommands mirroring server policy the
  server already enforces, against the thin pass-through rule.
- Any client-side pre-check that a scope is non-empty.
