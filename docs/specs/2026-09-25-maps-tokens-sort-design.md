# `--sort` on `maps list` and `tokens list`

**Status:** approved
**Issue:** [#75](https://github.com/thomaslazar/grimoire-cli/issues/75)
**Verified against:** `hunterreadca/grimoire:1.7.2`, the pinned stack

## Problem

Grimoire 1.7.2 added `sort=path|name` to `GET /api/maps` and `GET /api/tokens`.
Both commands are thin pass-throughs and carry every other query parameter their
endpoint takes; `sort` was left out of the version bump ([#74](https://github.com/thomaslazar/grimoire-cli/pull/74))
because adding it moves `MinSupportedVersion`, which is a decision rather than a
detail of a bump.

Without it there is no way to page either collection in filename order. The
default groups rows by folder, so a caller wanting an alphabetical listing has
to fetch the whole collection and sort it client-side.

## Verified server behaviour

Read from `backend/routers/maps/core.py` and `backend/routers/tokens/core.py` at
tag `v1.7.2`. Line cites are that tag.

- **The parameter is `sort: str = Query("path", pattern="^(path|name)$")`**
  (`maps/core.py:88`, `tokens/core.py:33`). Unlike `/api/systems`, which
  resolves an unrecognised sort key back to its default
  (`systems/core.py:124`), an unknown value here fails the pattern and is a 422.
- **`path` orders by `relative_path`, `name` by `filename`**
  (`maps/core.py:104`, `tokens/core.py:48-49`). Upstream's reason for the
  default: a page under `path` is a contiguous run of folders, so later pages
  append below what the reader is already looking at, where `name` scatters each
  page across the whole tree.
- **There is no companion order parameter.** Both are ascending only; the CLI
  gets no `--desc`.
- **The parameter is unknown to 1.7.0 and 1.7.1.** FastAPI ignores an
  unrecognised query parameter rather than refusing the request, so `--sort name`
  there answers 200 with path-ordered rows.

## Design

### The flag

`maps list --sort <path|name>` and `tokens list --sort <path|name>`, built with
`OptionHelpers.Choice("--sort", "Row order; default path", ["path", "name"])`.
Omitted, the option sends no query parameter and leaves the server on its own
default.

`Choice`'s own doc comment justifies itself by the server silently falling back
to a default, which is not what this endpoint does — the pattern makes an
unknown value a 422. It is used anyway for three reasons that do apply: shell
completions, a parse-time error instead of a round-trip, and consistency with
`systems list --sort`, the same flag name on the same kind of endpoint.

Rejected: a plain unvalidated `Option<string?>`, which is thinner but loses the
completions and the consistency; and sending `path` explicitly as a client-side
default, which puts a parameter on the wire that the caller did not ask for and
is dropped below 1.7.2 regardless.

### The floor

`MinSupportedVersion` moves to `1.7.2`. This is the same shape as the 1.7.0 move
for `--frames-container`: a flag an in-range server silently drops is the one
failure the version warning exists to catch.

Follow-on edits, all in this change:

- `VersionCheckCadenceTests` — the two facts using `1.7.0` as an in-range
  version move to `1.7.2`.
- `README.md` — the compatibility line reads `Requires Grimoire v1.7.2`.
- `docs/grimoire-compatibility.md` — the matrix gains `0.3.x | 1.7.0 – 1.7.1 |
  last release for that line` and `unreleased | 1.7.2 | current, on main`. No CLI
  version number is claimed for `main`: whether this ships as a minor or a patch
  is decided when the release is cut. No support branch is cut for 1.7.0/1.7.1.
- `docs/grimoire-compatibility.md` — the 1.7.2 section's `sort` bullet stops
  describing the parameter as unexposed, and a `MinSupportedVersion moved to
  1.7.2 for one reason: --sort` paragraph joins the 1.7.0 one.
- `SearchCommand.cs` — the `1.7.2+` annotation on `code (sku, product_code)`
  comes off. Every supported server now has the field, so the annotation is
  noise.
- `docs/grimoire-api-coverage.md` regenerates; its tested-range line reads the
  constants.

### Help text

One Notes line on each `list`, worded identically:

> `--sort path orders by relative_path, so a page is a contiguous run of`
> `folders; name orders by filename across the whole tree.`

That is the outcome-affecting fact a caller cannot infer from the flag: which
column decides whether a page is one folder or a slice of the whole library. The
value set renders itself, so the description does not repeat it.

The README Commands table rows for both commands gain `[--sort <path|name>]`.

## Testing

### Unit

Per command, in `MapsCommandTests` and `TokensCommandTests`:

- an unknown `--sort` value is a parse error
- both valid values parse
- the help states what each order sorts by

### Smoke

The live HTTP path is what unit tests miss, and the current token fixtures
cannot show the difference: `tokens/Monsters/Goblin.png` and
`tokens/Monsters/Undead/Skeleton.png` sort identically by path and by name.

`docker/seed.sh` gains a third token, `tokens/Monsters/Undead/Ghoul.png`. Path
order becomes Goblin, Ghoul, Skeleton; name order becomes Ghoul, Goblin,
Skeleton. Both commands then assert the real ordering under `--sort name`
against the running stack.

Map fixtures already differ — path order Crossroads, Tavern, Deep Cave against
name order Crossroads, Deep Cave, Tavern — so no map fixture changes.

`(.tokens | length) == 1` under `--limit 1` is the only exact token count the
smoke test asserts, and it is unaffected by a third fixture. The seed's
`wrote 2 fixture tokens` line becomes 3.

## Not in scope

- **No entry in `docs/grimoire-api-notes.md`.** The ordering rule lives in the
  flag's own help and in the compatibility doc; the notes file is for behaviour
  the source is slow to reveal.
- **No `--desc`.** The endpoints take no order parameter.
- **The 1.7.2 phrase-search change** (`"lucky feat"` parses as one FTS token
  rather than an implicit AND) needs no CLI work and is already recorded in
  `docs/grimoire-compatibility.md`.
