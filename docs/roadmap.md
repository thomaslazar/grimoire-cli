# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

## Next

1. **Typed request shapes in `--help-full`.** Write commands list their field
   *names*, from the generated model, while responses get a shape with types
   (`{"updated": ["<string>"], …}`). An agent writing a body cannot see that
   `publishers` is a list of `{name, url}` or that `year` is a number, and learns
   it by being refused once. The types are in the OpenAPI spec but not reachable
   from the generated properties at runtime — they are composed-type wrappers, and
   digging them out needs reflection Native AOT trims — so this is a build-time
   generator emitting `RequestExamples.g.cs` from the spec, the way
   `ResponseExamples.g.cs` is generated from the DTOs. **Before books**, so every
   write command added after it gets request shapes for free instead of being
   retrofitted.
2. **Books.** The larger metadata surface, and the only resource with no commands
   at all. Read side first, then the writes — `PATCH /api/books/{id}`,
   `POST /api/books/bulk`, `/bulk/tags` — following the `--input`/`--stdin` shape
   settled for systems.
3. **The remaining systems endpoints.** Cover (get/upload/delete), book-folders
   (list/update), and the metadata-lookup trio: `metadata-sources`,
   `metadata-search`, `metadata-fetch`. The trio wraps the server's add-on
   system, which fetches server-side with a per-field diff and a field whitelist.
