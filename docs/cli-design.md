# CLI Design

## Status

Early: the command surface is not finished. This document describes the design
principles applied so far and the pattern to extend. What exists is listed in the
README's Commands table and mapped to endpoints in
[grimoire-api-coverage.md](grimoire-api-coverage.md); [roadmap.md](roadmap.md)
has what's planned next.

## Command Pattern

```
grimoire-cli <resource> <action> [options]
```

Same shape as abs-cli: a resource noun, an action verb, then options. Unlike
abs-cli's `items`/`libraries`/`authors`/… sprawl, Grimoire's resource surface
is much smaller — one system, one set of books per system — so the resource
list below is short by design, not by omission.

## Thin pass-through

Each command maps to exactly one Grimoire API endpoint (see
[grimoire-api-coverage.md](grimoire-api-coverage.md) for the full generated
endpoint-to-role table). No command pre-fetches extra data, reads the
response to synthesize a derived warning, or mirrors server-side policy
client-side. A workflow spanning multiple endpoints — e.g. "rescan, then
patch every book missing a genre" — is the caller's job to compose from
single-endpoint commands, or belongs in a higher-level orchestration layer
outside this CLI.

## Systems

The first resource implemented. Maps to `GET /api/systems` and
`GET /api/systems/{id}`.

| Command | Grimoire Endpoint | Description |
|---------|-------------------|--------------|
| `grimoire-cli systems list [--sort name\|book_count\|page_count\|year] [--desc] [--genre <g>] [--family <f>] [--parent-system <p>] [--edition <e>] [--license <l>] [--explicit true\|false]` | `GET /api/systems` | List all game systems |
| `grimoire-cli systems get --id <id> [--book-sort category\|title\|page_count\|year] [--book-desc] [--genre <g>] [--category <c>] [--explicit true\|false]` | `GET /api/systems/{id}` | Get one system, with its books |

Every query parameter the two endpoints accept is exposed as a flag — no
parameter is left unmapped, and no flag exists that isn't backed by a
parameter.

## Lookup vocabularies

Five read-only groups behind the API's `lookups` tag, each a parameterless
GET naming one controlled vocabulary. `systems list` filters, and the write
fields on `systems update` and `books update`, draw their values from
these — which vocabulary applies to which command differs (`books update`
takes no `dice_materials` field, for one); see `systems update --help` for
the exact mapping.

| Command | Grimoire Endpoint | Description |
|---------|-------------------|--------------|
| `grimoire-cli genres list` | `GET /api/genres` | List the genre vocabulary (tiered via `parent_id`) |
| `grimoire-cli licenses list` | `GET /api/licenses` | List the license vocabulary |
| `grimoire-cli parent-systems list` | `GET /api/parent-systems` | List the parent-system vocabulary (ships empty) |
| `grimoire-cli system-families list` | `GET /api/system-families` | List the system-family vocabulary |
| `grimoire-cli dice-materials list` | `GET /api/dice-materials` | List the dice/material vocabulary |

## Backups

Six commands behind the API's `backups` tag: list, create, delete, download
and a settings read/write pair.

| Command | Grimoire Endpoint | Description |
|---------|-------------------|--------------|
| `grimoire-cli backups list` | `GET /api/backups` | List backups, newest first, with the directory and total size |
| `grimoire-cli backups create` | `POST /api/backups` | Take a backup now |
| `grimoire-cli backups delete --id <backup-id>` | `DELETE /api/backups/{backup_id}` | Delete one archive |
| `grimoire-cli backups download --id <backup-id> --output <path\|->` | `GET /api/backups/{backup_id}/download` | Download one archive as zip |
| `grimoire-cli backups settings get` | `GET /api/backups/settings` | Read the backup schedule and retention settings |
| `grimoire-cli backups settings set [--schedule off\|hourly\|daily\|weekly] [--hour <0-23>] [--minute <0-59>] [--weekday <0-6>] [--retention-count <n>] [--retention-gb <n>] [--dir <path>]` | `PUT /api/backups/settings` | Configure the schedule and retention |

## Files

Ten commands behind the API's `files` tag: browse, upload, move, rename, a
soft/hard delete pair, and a `folder` subgroup for create, delete, markers,
scaffold and contents.

| Command | Grimoire Endpoint | Description |
|---------|-------------------|--------------|
| `grimoire-cli files browse [--path <path>] [--limit <1-2000>]` | `GET /api/files/browse` | List a library folder, merged with indexing state |
| `grimoire-cli files upload --destination <path> --file <path> [--relative-dir <path>] [--on-conflict skip\|rename]` | `POST /api/files/upload` | Upload one file; loop for many |
| `grimoire-cli files move --sources <path>... --destination <path> [--on-conflict skip\|rename]` | `POST /api/files/move` | Move files or folders, keeping their metadata |
| `grimoire-cli files rename --path <path> --new-name <name>` | `POST /api/files/rename` | Rename a file or folder on disk |
| `grimoire-cli files delete --path <path> [--confirm-name <name>] [--delete-files]` | `POST /api/files/delete` | Drop index entries; `--delete-files` also deletes the files, irreversibly |
| `grimoire-cli files folder create --parent <path> --name <name> [--container-kind <kind>] [--nsfw]` | `POST /api/files/folder` | Create a folder, optionally a container or NSFW |
| `grimoire-cli files folder delete --path <path> [--confirm-name <name>]` | `DELETE /api/files/folder` | Delete a folder and its files; always irreversible |
| `grimoire-cli files folder markers --path <path> [--container-kind <kind>] [--nsfw true\|false]` | `PUT /api/files/folder/markers` | Set a folder's container/NSFW markers |
| `grimoire-cli files folder scaffold --path <path>` | `POST /api/files/folder/scaffold` | Create the standard category folders |
| `grimoire-cli files folder contents --path <path>` | `GET /api/files/folder/contents` | Report whether a folder holds content |

## Login / Config / Self-test

Not resource commands in the same sense — see
[authentication.md](authentication.md) for `login`,
[configuration.md](configuration.md) for `config get`/`config set`, and
below for `self-test`.

## Flag conventions

- **`--desc` booleans, not `--order asc|desc`.** `systems list --sort
  book_count --desc` rather than `--sort book_count --order desc`. Matches
  abs-cli's `items list --sort ... --desc`.
- **Tri-state `Option<bool?>` for nullable server booleans.** `--explicit
  true|false` on `systems list`/`systems get` filters on Grimoire's
  `is_explicit` field, which can be true, false, or unset — a plain
  `Option<bool>` can't express "omit the filter" versus "filter for false",
  so these are `bool?` and unset means no filter at all.
- **Positional arguments for value-only subcommands.** `config set <key>
  <value>` uses two `Argument<string>`s, not `--key`/`--value` options —
  there's nothing to disambiguate by name once the subcommand name has fixed
  the shape.

## Sort key validation

Sort keys are validated at parse time via `Option.Validators`
(`SystemsCommand.ChoiceOption`), rejecting an unrecognized `--sort`/
`--book-sort` value with a parse error rather than sending it to the server.
This matters because Grimoire silently falls back to its default sort order
on an unknown key — the request still succeeds with exit 0, just ordered
differently than asked, which is a much worse failure mode than a rejected
flag.

`Argument<T>` has `AcceptOnlyFromAmong` for exactly this in System.CommandLine
2.0.7; `Option<T>` does not, which is why `ChoiceOption` hand-rolls the same
check via a validator delegate plus `CompletionSources` for shell completion.

**Filter values are not validated** the same way — `--genre`, `--family`,
`--parent-system`, `--edition`, `--license`, `--category` all accept any
string. Unlike sort keys (a fixed, server-defined enum), valid filter values
depend on what's actually in the library — genres, families and editions are
free-text metadata fields, so there's no closed set to validate against
client-side. An unmatched filter returns an empty or unfiltered result, not
a wrong-but-successful one.

## Help Text

`--help` is written for the agents that consume this CLI, not for humans
skimming a terminal — see the "Help text" section in `CLAUDE.md` for the
terseness rules. Two mechanics worth calling out here:

- **`AddHelpSection` / `AddExamples`** (`src/GrimoireCli/Commands/HelpExtensions.cs`)
  attach a "Notes" section (non-obvious caveats, positioned above the
  auto-generated Options block) and an "Examples" section (positioned below)
  to any command.
- **`--help-full`** is a recursive root option, as `--debug` and `--log-json`
  also are, that additionally prints two generated blocks per command: a
  "Response shape" block — a JSON sample via `AddResponseExample<T>()` /
  `AddResponseExampleArray<T>()` — beside a "Request shape" block for write
  commands, via `AddRequestShape<T>()`. Both sample types come from the same
  source now: `Commands/JsonExamples.g.cs`, generated by one tool,
  `tools/GenerateJsonExamples`, walking every model in
  `GrimoireCli.Generated.Models` (request and response alike, since the edge
  spec's response models live there too) and checked for drift by
  `JsonExamplesDriftTest`. The request sample's field names come from the
  same `GetFieldDeserializers()` set `JsonBodyInput.Validate` enforces;
  required-ness isn't in the models, so it stays hand-written in Notes. Plain
  `--help` prints a one-line hint ("Run --help-full to see the request and
  response shapes.") instead of the blocks, to keep the default help short.
  Walking the model's own fields and property types means a free-string field
  renders as a bare `"<string>"` with no vocabulary hint (the old hand-written
  response generator could say `"category": "core"` or `"phase": "scanning"`);
  a real enum still renders its own value set via `ChoiceOption`, so that loss
  is deliberate, not a regression to chase.

```
$ grimoire-cli systems get --help

Notes:
  --genre, --category and --explicit filter the books, not the system, and
  book_count / total_page_count are recomputed from the filtered list — so
  --category core reports counts for the core books alone.
  ...

Description:
  Get one game system, with its books

Usage:
  grimoire-cli systems get [options]

Options:
  --id <id> (REQUIRED)      System ID
  --book-sort <book-sort>   Sort the books (category | title | page_count | year); default category
  ...

Examples:
  grimoire-cli systems get --id <system-id>
  grimoire-cli systems get --id <system-id> --category core
  grimoire-cli systems get --id <system-id> --book-sort page_count --book-desc

Run --help-full to see the request and response shapes.
```

## Nesting: when a resource gets a subgroup

**Several HTTP methods on one path become a nested subgroup; distinct sibling
paths stay flat, with leaf names mirroring the path segment.** `/systems/{id}/cover`
is GET/POST/DELETE → `systems cover get|upload|delete`. `/books/{id}/thumbnail` is a
single GET with no verb set to host → the flat `books thumbnail`, never a
one-verb group.

(`/systems/{id}/book-folders` is GET/PATCH and would nest the same way as
`cover`, but no command ships for it today — see
[docs/roadmap.md](roadmap.md).)

This corrects an earlier rule: `systems metadata-sources` /
`metadata-search` / `metadata-fetch` were justified by "every command here is
two levels deep," which was the wrong generalization — those three are
distinct sibling paths under `systems`, flat for the same reason
`books thumbnail` is, not because of a depth cap. They stay as shipped; the
rule above is the one to apply to the next resource.

## Binary output

`--output` is required on a command whose response is bytes, not JSON:
`"Output file path, or '-' for binary to stdout"`. `-` copies the bytes to
stdout and prints nothing else; any other value writes the file, then prints
a `SavedFile` receipt (`{path, bytes}`) so stdout stays valid JSON in the
default case. All three commands — `books thumbnail`, `systems cover get` and
`backups download` — share one helper, `ConsoleOutput.WriteStreamAsync`, rather
than repeating the branch per command.

`GrimoireApiClient.SendStreamAsync` is `SendAsync` with `ReadAsStreamAsync` in
place of `ReadAsStringAsync` — same preflight version check, permission hints
and error handling. **It still buffers the whole response body in memory**:
the two-arg `_http.SendAsync(request, cts.Token)` defaults to
`HttpCompletionOption.ResponseContentRead`, so nothing streams until the
server has finished sending. That's fine for thumbnails and the 10 MB cover
cap this convention was built for. Book files and page images are expected to
reuse this same path (see `docs/roadmap.md`) — for those, buffering a large
PDF in memory is not fine, and the fix is three changes together, not one:

- `HttpCompletionOption.ResponseHeadersRead` on the `_http.SendAsync` call, so
  the body streams instead of buffering.
- The `CancellationTokenSource` moved out of its `using` and disposed only
  when the caller is done with the stream. `SendStreamAsync` returns the
  stream to the caller while `using var cts` is still in scope today; under
  `ResponseHeadersRead` that would dispose the CTS while the caller is still
  reading a live body.
- A timeout that covers the whole download, not the fixed 100 s
  `DefaultRequestTimeout` — which, left as is, would stay armed across an
  entire book-file download and cancel a slow-but-healthy transfer partway
  through.

## Role tagging

Commands whose endpoint requires a non-default role call
`command.AddRoleRequired("<role>")`, rendering a "Role required" section
above the Notes section. `systems list`/`systems get` carry no tag — reads
need only a non-guest account. `systems cover upload` and `systems cover
delete` carry `gm or admin`; the tag string must match the `permissionHint`
passed into the corresponding `GrimoireApiClient` call so the 403 message and
the help text agree.

## Self-Test

A built-in AOT integrity check that exercises source-generated JSON
round-trips, JWT `exp` parsing, version comparison, and the informational
version's assembly-attribute lookup — all without network access. Native
AOT trims reflection paths that compile fine and only fail at runtime on a
published binary, so this is what CI runs against every platform RID before
release.

| Command | Description |
|---------|-------------|
| `grimoire-cli self-test` | Runs the offline checks in `SelfTestCommand.cs` |

Exit code 0 on success, 1 on the first recorded failure (all failures are
collected and printed before exiting, not just the first). Output goes to
stderr, consistent with every other human-facing message.

## Deviations from abs-cli

Recorded here rather than only in `CLAUDE.md`'s "Relationship to abs-cli",
per that section's own instruction to record deviations where they're found:

- **No filter-encoding layer.** abs-cli's filters go through a
  `group.base64(value)` scheme dictated by the ABS API. Grimoire's list
  filters are plain query parameters (`?genre=Sci-Fi`), built by the
  generated request builders — no encoding step to hide from the user.
- **No pagination flags yet.** abs-cli's `items list` takes `--limit`/
  `--page`; `GET /api/systems` returns a bare array with no pagination
  envelope, so `systems list` has none either. This will need revisiting if
  a paginated list endpoint is implemented.
- **One file per upload invocation.** `abs-cli`'s `upload` posts every file in a
  single multipart request because the ABS API accepts that; Grimoire's endpoint
  is deliberately one file per request, so a large import that fails partway can
  report and retry precisely. The CLI mirrors the endpoint rather than looping,
  which keeps stdout the server's own bytes; importing many is a shell loop.
- **Five top-level vocabulary groups, not one umbrella noun.** `genres`,
  `licenses`, `parent-systems`, `system-families` and `dice-materials` each get
  their own group, which is the shape abs-cli settled for `genres` / `tags` /
  `narrators`. It sits against this file's "resource surface is short by design"
  line above, and the parity wins: the API's own tag is `lookups`, but grouping
  five distinct endpoints behind one noun would have made a flag select the
  endpoint, which no other command here does.
