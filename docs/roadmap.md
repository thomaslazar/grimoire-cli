# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

## Next

1. **Metadata lookup, systems and books in one pass.** `metadata-sources`,
   `metadata-search`, `metadata-fetch` on both resources. The trio wraps the
   server's add-on system, which fetches server-side with a per-field diff and a
   field whitelist — one design serving both resources rather than two. **The
   first release is cut after this**, as the point where the CLI can find
   metadata as well as edit it.
2. **The remaining systems endpoints.** Cover (get/upload/delete) and
   book-folders (list/update).
3. **Book text extraction.** `toc`, `page/{n}/text`, `page/{n}/words` — all JSON,
   and what an agent needs to read a rulebook rather than catalogue it.
4. **Binary endpoints.** `books/{id}/file`, `/thumbnail`, `/page/{n}`, and systems
   cover images. These return bytes, not JSON, so the first of them settles an
   output convention the CLI has not needed until now.
