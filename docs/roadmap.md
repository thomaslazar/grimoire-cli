# Roadmap

What is intended, in the order it is intended. Not a status log, not a findings
list, and not a running tally — those belong where they already live:
[grimoire-api-coverage.md](grimoire-api-coverage.md) for what is implemented,
[grimoire-api-notes.md](grimoire-api-notes.md) for verified server behaviour,
git history for what changed. An item lands here when it is decided, and leaves
when it ships.

**One line and one link per item.** Endpoint lists, verified server behaviour and
the caveats help text will have to carry live in the issue, so this file stays
short enough to read in one go.

## The objective

One agent-drivable pipeline for **books**, from a file arriving to a finished
metadata sweep, matching what `abs-cli` gives for audiobooks. Its two workflows
are the target shape: *upload and catalogue*, and *fix a metadata problem across
the library on request*. **Met as of v0.2.0.**

What follows is tag and vocabulary hygiene across that library. The
per-collection commands and the cross-cutting ones — `duplicates`, `tags items`
and `search` take every resource type, `files` manages every tree, `library
rescan --scope` reaches every section — already put tags and lookup values on
everything Grimoire holds.

## Next

Nothing at present.

## Later

Decided, but not next. Nothing at present.

## Open questions

Not intended work — decisions to make before any of it could be.

- **[Per-user state](https://github.com/thomaslazar/grimoire-cli/issues/49)** — favorites, bookmarks, saved filters. A human's UI state; an agent writing to it either pollutes a real person's view or writes into a void.
- **[Administration](https://github.com/thomaslazar/grimoire-cli/issues/50)** — users, the rest of auth, themes, settings. A different product from library management.
- **[Campaign play side](https://github.com/thomaslazar/grimoire-cli/issues/51)** — wiki, sessions, members, guests, sheets, invites, calendar, banners, templates.
- **[VTT authoring geometry](https://github.com/thomaslazar/grimoire-cli/issues/70)** — the map routes that write walls, doors and lights. Reading a `.uvtt` already works; authoring one is map editing, not cataloguing.
