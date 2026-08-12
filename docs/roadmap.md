# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

## Next

1. **Books.** The larger metadata surface, and the only resource with no commands
   at all. Read side first, then the writes — `PATCH /api/books/{id}`,
   `POST /api/books/bulk`, `/bulk/tags` — following the `--input`/`--stdin` shape
   settled for systems.
2. **The remaining systems endpoints.** Cover (get/upload/delete), book-folders
   (list/update), and the metadata-lookup trio: `metadata-sources`,
   `metadata-search`, `metadata-fetch`. The trio wraps the server's add-on
   system, which fetches server-side with a per-field diff and a field whitelist.
