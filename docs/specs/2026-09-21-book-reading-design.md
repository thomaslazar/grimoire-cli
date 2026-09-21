# book reading — design

Closes [#46](https://github.com/thomaslazar/grimoire-cli/issues/46): the three
GETs that turn a book from a record to catalogue into a document to read.

Every server-behaviour claim below cites `temp/grimoire` at `v1.7.1`, the tag the
local stack's image is pinned to, or an observation against that running stack.

## Commands

| Command | Endpoint | Perm | Output |
|---|---|---|---|
| `books toc --id <id>` | `GET /api/books/{id}/toc` | — | JSON |
| `books page-text --id <id> --page <n>` | `GET /api/books/{id}/page/{n}/text` | — | JSON |
| `books page-words --id <id> --page <n>` | `GET /api/books/{id}/page/{n}/words` | — | JSON |

## Why these names

**The books group already names things this way.** `books metadata-fetch`,
`metadata-search` and `metadata-sources` are flat and hyphenated rather than a
`books metadata` subgroup, so flat hyphenated siblings are this group's own
existing habit, not an import from elsewhere.

It also keeps `books page` free for the render that
[#48](https://github.com/thomaslazar/grimoire-cli/issues/48) adds, which would
then match the shipped `maps page --id --page <n> --output`. That symmetry is a
reason to prefer this shape, not evidence for it: `books page` does not exist
today, so nothing an agent has already learned depends on it. If #48 later
chooses a `books page` subgroup with `render`/`text`/`words`, these two names are
what would have to change — the in-group convention above is why that is
unlikely to be worth it.

## What this completes

`search` already returns page-text hits **with page numbers**, but only a
snippet — so an agent can learn a term is on page 241 and has no way to read
page 241. `toc` supplies the structure and `page-text` the content.

## Verified server behaviour

- **`toc` covers EPUB as well as PDF.** PyMuPDF exposes an EPUB's nav document
  through the same API as a PDF outline, and the handler gates on
  `is_fitz_mime` rather than on PDF (`routers/books/pages.py:64-77`). It 404s
  for a book it cannot open, and for one that does not exist — the two are
  indistinguishable from the response. The tree is nested
  `{title, page, level, children}`.
- **`page-text` prefers the search index.** It reads the `book_search` FTS row
  for that page and only falls back to live extraction when there is none
  (`pages.py:221-245`) — a scan's result depends on the book being indexed and
  OCR having run, while a born-digital book extracts live with no index row
  at all. `books get` reports `indexed` and `index_failed`, which is the
  signal to check first for a scan.
- **`page-text` serves text documents too.** The format gate is `can_index`,
  not an `application/` MIME prefix, so `text/plain` and `text/markdown` books
  are readable (`pages.py:215-219`).
- **`page-words` answers 200 with an empty overlay for any book outside the
  `fitz` format family.** PDF, EPUB and DjVu are `fitz`; `.txt`/`.md`/`.rtf`
  and the comic archives all get the empty overlay instead of an error
  (`pages.py:256-259`, `indexer/formats.py:71-85`). Its `page-text` sibling
  only 404s for the comic family — the text family is `can_index`, so
  `page-text` succeeds there while `page-words` still returns the empty
  overlay. A caller therefore cannot tell "not a renderable document" from
  "no words on this page" without checking `width`, and that is the caveat
  this command's help exists to carry.
- **An out-of-range page is 400, not 404**, and the message carries the real
  page count (`pages.py:237-238, 243-244, 270`). `books get` reports
  `page_count`.
- Observed live against the seeded stack, on a 4-page fixture PDF:
  `{"toc":[]}`; `{"text":"grimoire-cli fixture · page 1"}`; and a words
  response of `width` 595.0, `height` 842.0 with per-word `x0/y0/x1/y1` boxes
  in PDF points.

## Implementation

`BooksService` gains three sends, each a plain `SendAsync` with no
`permissionHint` — all three routes are guarded by `get_current_user`.
`BooksCommand` gains the three subcommands. No new file, no new pattern, no new
output convention. `tests/GrimoireCli.Tests/Services/BooksServiceTests.cs` does
not exist yet and is created.

**Not-found hints differ per command, because the server's 404 bodies do.**
`GrimoireApiClient` replaces the response body whenever a hint is set, so a hint
is right only where the server says nothing useful:

- `toc` — its single 404 is bare (`pages.py:73`), so the caller otherwise sees
  `{"detail":"Not Found"}`. It gets a hint naming **both** causes: no such book,
  or a format PyMuPDF cannot open.
- `page-text` — four 404s in total: `pages.py:60` ("Book not found") and
  `:232` ("File not found on disk") carry detail; `:219` and `:241` are bare.
  It gets a hint naming the two bare causes; the hint's own first clause
  already covers the "Book not found" case, so nothing informative is lost
  there, and masking the rarer disk case is the accepted cost the help text
  mentions.
- `page-words` — both of its 404s carry a useful detail: "Book not found"
  (`pages.py:60`) and "File not found on disk" (`:263`). It gets **no hint**,
  so those messages survive.

Response examples: `TocResponse`, `PageTextResponse`, `PageWordsResponse`, all
already generated.

Help text carries, per command: `toc`'s EPUB support and its 404; `page-text`'s
index dependence with the `indexed` pointer, and the 400-with-page-count; and
`page-words`' empty-overlay behaviour, which is the one a caller cannot infer.

## Testing

- `BooksServiceTests` gains the three paths, including the `{page_num}` path
  segment, which is the part a client regeneration could move.
- Command tests: `--page` required on the two page commands and absent from
  `toc`, no role section on any of the three, and the empty-overlay caveat
  present in `page-words`' help.

Smoke, against the seeded fixtures (a 4-page indexed PDF, confirmed live):

- `books toc` returns an object carrying a `toc` key.
- `books page-text --page 1` returns the fixture's known page-1 string.
- `books page-words --page 1` returns a non-zero `width` and a non-empty
  `words` array.
- `books page-text --page 99` and `books page-words --page 99` both fail.

**Ships without live coverage of a populated `toc`:** the fixture PDFs carry no
outline, so the seeded stack answers `{"toc":[]}` and the smoke test can only
assert the shape. Adding an outlined fixture is out of scope here.

## To verify against the local stack, not assume

- Whether any seeded book is a non-PDF that `page-words` would answer with the
  empty overlay — if one exists, the smoke test should assert that branch,
  since it is the behaviour most likely to surprise.
- That `books page-text` on a page beyond `page_count` really answers 400 with
  the count, rather than some other status the CLI maps differently.

## Docs in the same PR

Three README Commands rows; three `IMPLEMENTED` entries in
`tools/generate-api-coverage.py`, then regenerate; `docs/grimoire-api-notes.md`
for the index-first text lookup, the empty-overlay asymmetry and the
400-with-page-count.

## Not doing

- `books page` (the render), `books file` and `downloads/archive` — those are
  [#48](https://github.com/thomaslazar/grimoire-cli/issues/48), the next block.
- Any client-side page-range check. The server answers 400 with the count,
  which is more useful than anything the CLI could pre-compute.
