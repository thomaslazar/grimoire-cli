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

What follows extends it to the rest of the library. Grimoire holds five
collections, and the cross-cutting commands already reach all of them —
`duplicates`, `tags items` and `search` all take every resource type, `files`
manages every tree, `library rescan --scope` reaches every section. Only
`list`/`get`/`update` stop at books. Closing that asymmetry is the direction.

The sharpest symptom, and where the collection work starts: the only way to set
a tag on a map today is `duplicates merge-metadata --resource-type map --fields
tags`, copying it off another map that already carries it.

## Next

1. **[logs](https://github.com/thomaslazar/grimoire-cli/issues/45)** — one endpoint, and everything after it benefits: `library rescan`, `duplicates scan`, `books reindex` and `books rescan` all start background work and report only `scan_started`, so a failure currently explains itself only in the UI.
2. **[maps per-item layer](https://github.com/thomaslazar/grimoire-cli/issues/38)** — first of the four, because it has the richest update model and the only real list filters, so it settles the shape the other three port.
3. **[models per-item layer](https://github.com/thomaslazar/grimoire-cli/issues/39)** — next, because 1.6.2's model variant kinds are already accepted by `duplicates link` and unusable without a way to list candidates.
4. **[tokens per-item layer](https://github.com/thomaslazar/grimoire-cli/issues/40)** — mechanical once maps lands.
5. **[audio per-item layer](https://github.com/thomaslazar/grimoire-cli/issues/41)** — last of the four; thinnest update model, plus an optional cover block.
6. **[tags writes](https://github.com/thomaslazar/grimoire-cli/issues/42)** — create, rename, delete, merge. Deliberately after the four above: each collection that gains `batch-tag` makes the hygiene problem bigger, so this lands when it is most needed.
7. **[vocabulary writes](https://github.com/thomaslazar/grimoire-cli/issues/43)** — completes the five lookups the shipped reads open.

**[Small completions](https://github.com/thomaslazar/grimoire-cli/issues/44)** —
`library stats`, `systems cover from-source`, and the binary getters for each new
collection. Too small to schedule; fold each into whichever block is in flight.

## Later

Decided, but not next.

- **[book reading](https://github.com/thomaslazar/grimoire-cli/issues/46)** — `toc`, page text, page words. Serves "look up the relevant section and explain it to me" rather than library management: a different axis, and cheap whenever it is wanted.
- **[sidecar export](https://github.com/thomaslazar/grimoire-cli/issues/47)** — makes a metadata sweep survive the instance, and closes a loop `library rescan --metadata-mode` already half-owns. One endpoint in practice; the settings behind it are a one-time UI action.
- **[remaining binary endpoints](https://github.com/thomaslazar/grimoire-cli/issues/48)** — book file, page render, and the archive download that can export a tag-scoped slice of the library in one call.

## Open questions

Not intended work — decisions to make before any of it could be.

- **[Per-user state](https://github.com/thomaslazar/grimoire-cli/issues/49)** — favorites, bookmarks, saved filters. A human's UI state; an agent writing to it either pollutes a real person's view or writes into a void.
- **[Administration](https://github.com/thomaslazar/grimoire-cli/issues/50)** — users, the rest of auth, themes, settings. A different product from library management.
- **[Campaigns](https://github.com/thomaslazar/grimoire-cli/issues/51)** — 91 operations, 30% of the API. The linking half touches the library; the play side is a separate tool.
