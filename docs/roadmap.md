# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

## Next

1. **The remaining systems endpoints.** Cover (get/upload/delete) and
   book-folders (list/update).
2. **Book text extraction.** `toc`, `page/{n}/text`, `page/{n}/words` — all JSON,
   and what an agent needs to read a rulebook rather than catalogue it.
3. **Binary endpoints.** `books/{id}/file`, `/thumbnail`, `/page/{n}`, and systems
   cover images. These return bytes, not JSON, so the first of them settles an
   output convention the CLI has not needed until now.
