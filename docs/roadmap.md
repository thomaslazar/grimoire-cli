# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

## The objective

One agent-drivable pipeline for **books**, from a file arriving to a finished
metadata sweep, matching what `abs-cli` already gives for audiobooks. Its two
workflows are the target shape: *upload and catalogue*, and *fix a metadata
problem across the library on request*.

Maps, tokens and audio are deliberately out of the MVP. They are structurally
parallel to books but carry almost no per-item metadata — `MapUpdate` has four
fields, `AudioUpdate` two, against `BookUpdate`'s nineteen — and they hang off
folders rather than systems, so for them folder tagging is the whole story
rather than a second layer. They come after duplicate handling.

## MVP

In this order. Cheapest and safest first; the destructive block only once a
backup command exists to precede it.

1. **Safety** — `backups`: create, list, settings read and write, delete,
   download. Both `abs-cli` workflows open with a backup. That was optional while
   the library was read-only and stops being optional the moment block 2 lands,
   because that is when the CLI can move and delete real files. Writes to the
   data directory rather than the library, so it needs no remount and can ship
   first.

2. **Ingest** — the `files` API: `upload`, `browse`, `move`, `rename`, `delete`,
   and folder create / delete / markers / scaffold / contents. The front of the
   pipeline and what 1.6.0 exists for. All admin.

   `upload` is deliberately one file per request, so a large import that fails
   partway can report and retry precisely; its `on_conflict` defaults to renaming
   and never overwrites. `browse` is the one to not overlook: it is DB-aware, so
   it distinguishes indexed records from loose files the scanner ignored, which
   is the "did my upload land *and* index?" check the pipeline has no answer for
   today.

   **Prerequisite:** `docker/docker-compose.yml` mounts the library `:ro`, and
   the server answers `409 The library is mounted read-only` to every write. The
   dev stack must be remounted `:rw` before any of this can be smoke-tested, and
   those smoke cases then write real files into the fixture tree — so they need
   the create-then-clean-up shape the book-folders block uses, or runs stop
   converging.

3. **Discovery** — `search`, plus `GET /api/tags` and
   `GET /api/tags/{internal}/items`.

   `search` is `GET /api/campaigns/resources/search`. It lives at a campaigns URL
   because it backs that feature's resource picker, but it takes no campaign id
   and matches names across the whole library — books by title, narrowable by
   system. It stays a top-level `search` rather than `books search` precisely
   because it also covers maps, tokens and audio, so it needs no new command when
   those arrive: `--type` already selects between them.

   The tag reads close the sweep loop: after applying metadata, they show what
   the catalogue now looks like, including folder-derived tags that never appear
   on a book's own record.

## Then

**Duplicate handling** — `duplicates`, thirteen endpoints. The one post-MVP block
worth describing now, because it is the answer to the problem a growing PDF
library actually develops: the same rulebook arriving as several scans of
differing quality, or a revised printing sitting beside the original.

- `scan`, `scan-status`, `cancel-scan` — a detection pass over the library,
  asynchronous like the library scan. It refuses with `409` while a *library*
  scan is running and answers `{"status": "already_running"}` when a duplicate
  scan is already in flight, so it needs both the 409 path and the exit-3
  mapping `library rescan` already uses for the latter.
- `groups` — the candidate groups the last scan produced.
- `compare` — two to four items side by side, for deciding what a group is.

Then one of five resolutions, which is where the design thinking will go, because
they are not interchangeable:

- `dismiss` — the group is not duplicates. Reversible: `dismissals` lists them
  and a `DELETE` undoes one, so a wrong dismissal is not permanent.
- `link` / `unlink` — file copies under a parent as its *variants*. Keeps every
  copy while nominating a relationship, which is the right answer for different
  printings.
- `promote` — make a different copy the main version of an existing family. The
  companion to `link`: it is how a better scan replaces the one that happened to
  be indexed first.
- `merge-metadata` — copy fields from one copy onto another, for when the good
  scan has the worse metadata.
- delete one record, and optionally its file. The only irreversible option, and
  the one that will need the most care in help text.

The verbs carry `resource_type` in their paths, so this generalises to maps,
tokens and audio for free once those exist.

**Vocabulary writes** — `create` and `delete` on each of the five vocabularies,
completing the set the shipped vocabulary reads open. Ten endpoints, all admin.

`create` and `delete` are the whole set: the API has no `PUT` or `PATCH` on any
vocabulary, so there is no rename, and `abs-cli`'s `genres rename` /
`tags rename` have no counterpart here to port. Verbs sit beside `list` on the
group the reads already establish — `genres create`, `genres delete`.

- `create` takes a name, 409s on a case-insensitive duplicate, and returns the
  new entry with `is_default: false`. A genre additionally takes a `parent_id`,
  and 404s if no such genre exists.
- `delete` takes the entry's `id` — the one field the reads expose that nothing
  else uses — and 409s while the value is in use, with a body carrying
  `usage_count` and `name`, unless `force=true`.
- **A forced delete strips nothing.** It removes the vocabulary row only; every
  system and book carrying that name keeps it, because the value is stored as a
  string rather than a foreign key. The response field is called
  `removed_usage`, which reads as though it did, so this is the caveat the help
  text has to carry. Deleting a genre does cascade to its children.

**The other resource types** — maps (11 endpoints), tokens (10), audio (14).
Three near-copies of the books shape: list, get, update, `bulk`, `bulk/tags`,
folder tags plus a `bulk` variant books does not have, and binary getters. Cheaper
than the count suggests, since the update models are nearly empty and the binary
convention is already settled. Audio additionally has cover management including
`cover/from-source`, mirroring `systems cover`.

## Later

Rough notes, to be looked at when they come up.

- **`books search-full-text`** — `GET /api/search`, books-only, returns one hit
  per matching *page* with a snippet, not one per book. The snippet contains
  literal `<mark>` HTML for the web UI, which the help text will need to warn
  about since responses pass through unmodified.
- **Book text extraction** — `toc`, `page/{n}/text`, `page/{n}/words`. All JSON,
  and what an agent needs to read a rulebook rather than catalogue it.
- **The remaining binary endpoints** — `books/{id}/file`, `/page/{n}`. The output
  convention is settled (`--output`, `-` for stdout, a `SavedFile` receipt
  otherwise); what remains is applying it.
- **`tags` writes** — create, rename display value, merge, delete. Catalogue
  hygiene after a sweep.
- **`saved-filters`** — four endpoints. A UI convenience; unclear that an agent
  wants stored filter state.
- **Campaign linking** — `{campaign_id}/resources` and friends: link, bulk-link,
  reorder, visibility, unlink. A real workflow, but downstream of library
  management rather than part of it.
- **Administration** — `users`, `settings`, `themes`, `logs`, `bookmarks`,
  `favorites`, `downloads`.
- **`campaigns` proper** — 91 endpoints of session notes, wikis, guests and
  handouts. Grimoire's play side, and no part of managing a library.
