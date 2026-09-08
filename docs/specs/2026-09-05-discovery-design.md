# Discovery — design

The final MVP roadmap item. Four read-only endpoints that let an agent find
things in the library and see what the catalogue looks like after a metadata
sweep.

| Endpoint | Command | Role |
| --- | --- | --- |
| `GET /api/search` | `search` | none (`require_not_guest`) |
| `GET /api/search/fields` | `search fields` | none (`require_not_guest`) |
| `GET /api/tags` | `tags list` | none (`get_current_user`) |
| `GET /api/tags/{internal}/items` | `tags items` | none (`get_current_user`) |

No command carries `AddRoleRequired`, and no service passes a `permissionHint`.
`require_not_guest` is the CLI's documented no-tag default; `get_current_user`
likewise carries no role.

## What changed since the roadmap was written

The roadmap scoped Discovery to `GET /api/campaigns/resources/search` and filed
`GET /api/search` under "Later" as `search-full-text`. That framing is stale:
1.6.1 turned `/api/search` into a general search. It now returns five buckets
rather than page hits alone, and accepts a `field:value` filter syntax over
fourteen fields.

`/api/search` is therefore what the MVP's stated workflow — *fix a metadata
problem across the library on request* — actually needs. `author:gygax`,
`year:<1990`, `system:pbta category:core` are that workflow;
`campaigns/resources/search` matches names only and cannot express any of it.

`campaigns/resources/search` ships no command. Its reasoning is recorded in
[grimoire-api-notes.md](../grimoire-api-notes.md) rather than here, following the
`DELETE /api/files/folder` precedent, so a later coverage-gap sweep does not
re-add it.

## `search` — `GET /api/search`

**Flags:** `--query` (required) · `--limit` · `--book-id` · `--system-id` ·
`--server`

### Response buckets

The response is one envelope holding five independently-populated arrays:

| Bucket | Contents | Bounded by |
| --- | --- | --- |
| `results` | Page-text hits, with `<mark>` snippets | `--limit` |
| `book_matches` | Books matched on their own metadata | hard 50 |
| `maps` / `tokens` / `audio` | Matched on filename, path, tags, artist/album | hard 50 each |

`total` counts every row across all five. A book matching by title *and* by page
text appears in `book_matches` and again in `results`, and is counted twice —
the server does this deliberately so the web UI can pin the book above its pages.

### `--limit` takes a `Range(1, 200)`

The server declares `limit: int = Query(50, le=200)`. That guards the top —
`--limit 201` is a clean 422 — but there is no lower bound, and the value reaches
SQLite as a bare `LIMIT :limit`, where `-1` means *unlimited*.

Verified against the local stack, `q=text:fixture` over a 126-page index:

| `--limit` | Result |
| --- | --- |
| omitted | 200, 50 rows |
| `3` | 200, 3 rows |
| `0` | 200, 0 rows |
| `-1` | **200, 126 rows** |
| `200` | 200, 126 rows |
| `201` | 422 |

`--limit -1` silently returns the whole index. That is the silent-fallback
pattern `OptionHelpers.Range` exists for, so `--limit` is declared
`Range("--limit", …, 1, 200)`.

`--query`'s own `min_length=2` needs no client mirror: the server answers a clean
422 naming the constraint.

### The `field:value` syntax

Filters are written inside `--query`, not as flags — `--query "author:'Ben
Robbins' year:>2010"`. The parser is in `routers/search/_query.py`. Three rules
drive behaviour, and all three are outcome-affecting:

- **A metadata filter suppresses page-text search.** `title:dsa` returns
  `book_matches` and leaves `results` empty. `text:` (alias `content:`, `page:`)
  forces content search back on, and combines with free text.
- **Book-only fields suppress the media buckets** entirely — `author`,
  `publisher`, `category`, `year`, `isbn`, `language`, `description`, `text`.
- **An unrecognised prefix is not an error.** It falls through to free text and
  is searched literally, because a colon is ordinary punctuation in a title
  ("Vaesen: Nordic Horror"). A typo therefore returns a clean 200 with every
  bucket empty. The response's `fields` array echoes the canonical names the
  server *did* recognise, which is the only way to detect it.

Verified: `q=title:dsa` → `book_matches: 3, results: 0, fields: ["title"]`;
`q=titel:dsa` → every bucket empty, `fields: []`, HTTP 200.

### Scoping

`--book-id` and `--system-id` both suppress `maps`, `tokens` and `audio`.
`--book-id` additionally empties `book_matches` — inside one book there is
nothing to pin above the page hits.

### Notes block

Five caveats, each invisible from the flag list and each able to change what a
caller concludes from the response:

1. `--limit` bounds `results` only; the other four buckets are capped at 50 and
   cannot be raised.
2. `field:value` filters go inside `--query`; a metadata filter suppresses
   page-text search and `text:` forces it back. `search fields` lists them.
3. An unknown prefix searches literally rather than erroring — check the
   response's `fields` for what was understood.
4. `snippet` carries literal `<mark>` HTML.
5. `--book-id` / `--system-id` suppress the media buckets; `--book-id` also
   empties `book_matches`.

Response shape is `AddResponseExample<SearchResponse>`, which the generator
already renders with every bucket expanded.

### Query syntax section

The filter language lives entirely inside `--query`, and `search fields` returns
field names and aliases only — never the operators — so without a section of its
own the syntax is learnable nowhere. `abs-cli`'s `search` settled this shape with
its `Search behavior` and `Fields searched` sections: Notes keeps the caveats,
and the language sits where a caller looks for it.

The fourteen canonical fields and their aliases are listed inline rather than
delegated to `search fields`, so writing a query costs no second call. Each
field's *scope* is listed with it, because the scope rules are what silently
change the answer: `MEDIA_FIELDS` is `{title, tag, filename, artist, album}` and
`BOOK_FIELDS` omits `album`, so a filter naming nothing in one set drops that
half of the response — `_media_terms` returns `None` and `search_book_metadata`
returns `[]` rather than an unfiltered list. `artist` and `album` reach audio
only: `_search_media` is given `extra_fields` for audio alone, and its
`if not clauses: return []` guard is what stops a map query with no applicable
clause returning every map. Verified: `album:jazz` and `artist:someone` each
returned nothing on either side, `system:auge` returned 3 books and no media, and
`text:fixture` returned 50 page hits with no `book_matches`.

`search fields` remains the drift-proof source and keeps its own command; the
inline list is the convenience. A test pins all fourteen names, since a field
missing from the list reads as unsupported.

Four rules, each verified against the running stack:

- **Different fields AND; a repeated field ORs.** `_apply_field_filters` calls
  `query.filter` once per field and `_any_of` within one. Verified:
  `title:dsa title:honey` returned 4 where the two separately returned 3 and 1,
  and `title:dsa category:nonsuch` returned 0 where `title:dsa` returned 3.
- **A multi-word value must be quoted.** `_TOKEN_RE` binds one
  whitespace-delimited token to the prefix and the rest falls through to free
  text. Verified: `category:"core errata"` returned 0 against
  `category:core errata`'s 1.
- **`year:` takes `1999`, `>1999`, `<=2005` and `1999-2005`** (`year_bounds`).
- **Page text takes an FTS5 prefix match, trailing `*` only.** `to_fts_query`
  passes a token through bare only when it is a plain bareword or a bareword
  followed by `*`; anything else is quoted into a literal phrase. So an infix
  `*` searches for itself and matches nothing. Verified: `text:fixt*` and
  `text:fixture*` returned 50, while `text:*ture`, `text:fi*ure` and
  `text:f*xture` each returned 0.

### Query syntax section

The filter language lives entirely inside `--query`, and `search fields` returns
field names and aliases only — never the operators — so a `Query syntax` section
carries what is otherwise learnable nowhere. `abs-cli`'s `search` settled this
shape with its own `Search behavior` and `Fields searched` sections; the split
keeps Notes for caveats and puts the language where a caller looks for it.

Four rules, each verified against the running stack:

- **Different fields AND, a repeated field ORs.** `_apply_field_filters` calls
  `query.filter` per field and `_any_of` within one. Verified:
  `title:dsa title:honey` returned 4 where the two alone returned 3 and 1, and
  `title:dsa category:nonsuch` returned 0 where `title:dsa` alone returned 3.
- **A multi-word value must be quoted.** `_TOKEN_RE` binds one whitespace-
  delimited token to the prefix; the rest becomes free text. Verified:
  `category:"core errata"` returned 0, `category:core errata` returned 1.
- **`year:` takes `1999`, `>1999`, `<=2005` and `1999-2005`** (`year_bounds`).
- **Page text takes an FTS5 prefix match.** Verified: `text:fixt*` returned 50
  where `text:fixt` returned 0.

## `search fields` — `GET /api/search/fields`

No parameters. Returns the fourteen canonical field names with their aliases,
served from the same `FIELD_ALIASES` table the parser uses, so the documented
list cannot drift from the implemented one.

**Nesting.** `/api/search` → `search`, `/api/search/fields` → `search fields`,
per "distinct sibling paths stay flat, with leaf names mirroring the path
segment". The rule's "never a one-verb group" does not apply: `search` is a leaf
that hosts a child, not a group wrapping a single verb.

The command needs no Notes. Its own response is the documentation, and the
cross-reference lives on `search`, the consumer.

## `tags list` — `GET /api/tags`

**Flags:** `--in-use-by` · `--server`

`--in-use-by` restricts to tags used on one resource type, and scopes each row's
`count` to that type as well. Five values: `system`, `book`, `map`, `token`,
`audio` — note this set includes `system`, which the search world has no
counterpart for.

**No `Choice`.** The server validates and answers
`400 in_use_by must be one of: audio, book, map, system, token`. Declaring the
set client-side would mirror policy the server already enforces, which thin
pass-through forbids; the `Choice`/`Range` carve-out is for silent fallbacks
only, and there is none here. The values go in the flag's own description
instead.

**One caveat.** Folder-derived tags (from folder tagging) are merged into the
listing and their items added to `count`, so a row can carry a count with no
shared-tag row behind it, and a tag's `category` is its *effective* category
across every type it appears on.

## `tags items` — `GET /api/tags/{internal}/items`

**Flags:** `--tag` (required) · `--resource-type` · `--server`

`--tag` is the path parameter, named for the reader rather than after the
server's `internal`. The CLI already renames path parameters this way —
`/books/{book_id}` is `books get --id`. The key is matched case-insensitively
(`normalize_internal` is strip-and-lowercase), so the `internal` value from
`tags list` works, and so does its display casing.

404s on a tag that matches nothing, so the service passes a `notFoundHint`
pointing at `tags list`. `--resource-type` takes the same five values as
`--in-use-by` and validates the same way, so it too gets no `Choice`.

Items carrying the tag directly appear in `items`; folders carrying it appear in
`folders`, each rendered with everything inside.

### Union response shapes

`items` and `folders[].items` are heterogeneous, discriminated by `item_type`
across five shapes. The generator renders them as the placeholder
`"<TaggedAudioItem|TaggedBookItem|TaggedMapItem|TaggedSystemItem|TaggedTokenItem>"`,
which names the branches but shows none of their fields — and they differ
materially (a book has `title` and `page_count`, maps and tokens have
`filename`, audio has `duration`, a system has `publishers`).

`abs-cli` settled this with `AddMediaUnionShapes`: one extra `AddShapeSection`
per branch, titled with its discriminator. Ported here as a private helper in
`TagsCommand.cs` rather than `HelpExtensions.cs`, because one command calls it.
The five samples already exist in `JsonExamples.g.cs`, so the helper only titles
and emits them, behind `--help-full` like every other shape section.

## Files

New source files. Files that are *modified* rather than created — `Program.cs`,
`docker/smoke-test.sh`, the README, and the three docs — are named in the
sections that describe those changes.

| Path | Contents |
| --- | --- |
| `src/GrimoireCli/Commands/SearchCommand.cs` | `search`, `search fields` |
| `src/GrimoireCli/Services/SearchService.cs` | Both GETs |
| `src/GrimoireCli/Commands/TagsCommand.cs` | `tags list`, `tags items`, the union-shape helper |
| `src/GrimoireCli/Services/TagsService.cs` | Both GETs |
| `tests/GrimoireCli.Tests/Commands/SearchCommandTests.cs` | Parse, range, help |
| `tests/GrimoireCli.Tests/Commands/TagsCommandTests.cs` | Parse, help, union shapes |

Each group gets its own command file *and* its own service, per the established
convention, even where the two services are near-identical.

`Program.cs` registers `SearchCommand.Create()` and `TagsCommand.Create()`.

## Tests

Parse-level, in the shape the existing command tests use — no HTTP.

- `search` requires `--query`; parses with only `--query`.
- `--limit` rejects `0`, `-1`, `201`; accepts `1` and `200`. `-1` gets its own
  named test recording that the server treats it as unlimited.
- `--limit abc` produces a parse error rather than an unhandled exception —
  the regression `OptionHelpers.Range` was fixed for.
- Both `search` leaves and both `tags` leaves carry a response shape.
- No command in either group declares a role.
- `search` help states the 50-cap, `text:`, and the unknown-prefix behaviour.
- `tags items` requires `--tag`; help carries all five `item_type` shapes.
- `tags list --in-use-by` accepts an arbitrary string (no client-side set).

## Documentation

- `tools/generate-api-coverage.py`: four routes added to `IMPLEMENTED`, then
  `docs/grimoire-api-coverage.md` regenerated. The markdown is never hand-edited.
- `README.md`: four rows in the Commands table.
- `docs/grimoire-api-notes.md`: a `## Search` section recording the verified
  `LIMIT -1` behaviour, the hard 50-caps, the unknown-prefix silence, and why
  `campaigns/resources/search` has no command.
- `docs/roadmap.md`: Discovery removed from the MVP; the stale
  `search-full-text` entry removed from Later, since it is what shipped.
  `CHANGELOG.md` is untouched — it belongs to the release process.

## Smoke test

`docker/smoke-test.sh` already reaches `GET /api/tags/{internal}/items` by **raw
curl** at the book-folders block, because no command existed for it — the only
place in the script where a missing command forced a workaround. `tags items`
replaces it:

```sh
TAG_ITEMS=$("$CLI" tags items --tag errata-smoke 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags items exited non-zero"; }
```

The assertion on the result is unchanged, so the block keeps testing what it
tested — that a folder tag reaches the book below its path — while dropping the
hand-rolled `Authorization` header.

The new block is read-only throughout and therefore idempotent by construction.
It is placed after the existing tagging steps, which is what seeds the tags it
reads:

- `search --query "text:fixture" --limit 3` returns exactly three `results`.
- `search --query "title:dsa"` returns `book_matches` with an empty `results`,
  proving the suppression rule.
- `search --query "titel:dsa"` exits zero with an empty `fields`, proving the
  unknown-prefix path.
- `search --limit 0` and `--limit -1` exit non-zero at parse time, proving the
  `Range` guard against the unbounded `LIMIT -1`.
- `search fields` lists `title` among its fields.
- `tags list` includes `smoke-book-alpha`; `--in-use-by book` still includes it
  and drops the system-only tags.
- `tags items --tag no-such-tag` exits non-zero on the 404.
