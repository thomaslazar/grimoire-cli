# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

## Next

1. **Book text extraction.** `toc`, `page/{n}/text`, `page/{n}/words` — all JSON,
   and what an agent needs to read a rulebook rather than catalogue it.
2. **The remaining binary endpoints.** `books/{id}/file`, `/page/{n}`, and
   map/token thumbnails. The output convention is settled (`--output`, `-` for
   stdout, a `SavedFile` receipt otherwise); what remains is applying it here.
