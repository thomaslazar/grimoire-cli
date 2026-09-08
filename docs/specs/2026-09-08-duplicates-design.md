# Duplicate handling — design

Wraps the whole `duplicates` router: thirteen endpoints, every one `require_admin`.
Closes [#30](https://github.com/thomaslazar/grimoire-cli/issues/30) — the router
is unreachable from the CLI today, which makes variant linking the last part of a
library import that still forces the web UI.

Read off `temp/grimoire` at tag `v1.6.1` and verified against the running stack.
Issue #30 was written against upstream `main` and a 1.5.6 instance; every claim in
it that this design depends on was re-checked here, and all of them hold.

## Why the whole router

A *variant* is a second file of the same thing — a printer-friendly cut, a
form-fillable sheet, a superseded printing. The parent is the only row that
appears in listings, counts and search; the children keep their ids and stay
readable. `books get` already returns `variant_kind`, `variant_label`,
`variant_main_id`, `variant_parent_id` and `variants[]`, and `books list` returns
`variant_count`, so the read half is shipped and every one of those fields is
permanently empty for a CLI-driven library.

Linking is the verb an import needs in bulk. It is not shippable alone: an agent
filing 11 links for one folder of one edition will mislink, and without `unlink`
the only correction is the web UI, which defeats the point.

## Command surface

One top-level `duplicates` group, thirteen flat leaves. `resource_type` is a
**body or query field** on twelve of the thirteen — only
`DELETE /items/{resource_type}/{item_id}` carries it in the path — so one group
covers books, maps, tokens and audio without waiting for those command groups to
exist. (`docs/roadmap.md` says "the verbs carry `resource_type` in their paths";
that is wrong, and this design does not depend on it.)

Issue #30 proposes `books variants link` instead. Rejected: it splits one router
across two command groups (the issue still puts `compare` and `scan` under
`duplicates`), and the one-group-per-file convention would make it four
near-identical command files and services once maps, tokens and audio arrive.

| Method | Path | Command |
| --- | --- | --- |
| POST | `/link` | `duplicates link` |
| POST | `/promote` | `duplicates promote` |
| POST | `/unlink` | `duplicates unlink` |
| POST | `/merge-metadata` | `duplicates merge-metadata` |
| DELETE | `/items/{resource_type}/{item_id}` | `duplicates delete` |
| GET | `/compare` | `duplicates compare` |
| POST | `/scan` | `duplicates scan` |
| GET | `/scan-status` | `duplicates scan-status` |
| POST | `/cancel-scan` | `duplicates cancel-scan` |
| GET | `/groups` | `duplicates groups` |
| POST | `/dismiss` | `duplicates dismiss` |
| GET | `/dismissals` | `duplicates dismissals` |
| DELETE | `/dismissals/{dismissal_id}` | `duplicates undismiss` |

Eleven names mirror their path segment. Two do not, deliberately:

- **`delete`** for `/items/{resource_type}/{item_id}`. The segment is the noun
  `items`, and `duplicates items` reads as a listing — the wrong name for the
  block's one irreversible verb.
- **`undismiss`** for `DELETE /dismissals/{id}`, because `dismissals` is taken by
  the GET on the parent path. Matches the roadmap's own naming.

Neither is a nesting-rule case: no path here carries several methods, so nothing
becomes a subgroup.

## Files

| Path | Contents |
| --- | --- |
| `src/GrimoireCli/Commands/DuplicatesCommand.cs` | The group, plus `link`, `promote`, `unlink`, `merge-metadata`, `delete`, `compare` |
| `src/GrimoireCli/Commands/DuplicatesScanCommands.cs` | `scan`, `scan-status`, `cancel-scan`, `groups`, `dismiss`, `dismissals`, `undismiss` |
| `src/GrimoireCli/Services/DuplicatesService.cs` | All thirteen calls |

Two command files over one service, following the shipped
`FilesCommand` / `FilesFolderCommands` split over `FilesService`. Thirteen leaves
in one file would be the largest command file in the repo by half.

Every command declares `AddRoleRequired("admin")` and every service call passes
`permissionHint: "the admin role"`. Verified per handler: the router carries no
`dependencies=`, and all thirteen handlers take
`_: CurrentUser = Depends(require_admin)`.

## Input shapes

Flags where the body is flat scalars and simple arrays, JSON where it nests —
the line `files move --sources a b --destination x` and `books update --stdin`
already draw. Only `LinkRequest` nests, so only `link` takes `--input`/`--stdin`.

| Command | Input |
| --- | --- |
| `link` | `{--input <file> \| --stdin}` — `LinkRequest` |
| `promote` | `--resource-type --new-parent-id --old-parent-id [--kind] [--label]` |
| `unlink` | `--resource-type (--ids <id>… \| --parent-id <id>)` |
| `merge-metadata` | `--resource-type --source-id --target-id --fields <f>… [--overwrite]` |
| `delete` | `--resource-type --id --delete-file true\|false [--reparent-to <id\|"">]` |
| `compare` | `--resource-type --ids <id> --ids <id>` (2–4) |
| `scan` | `[--resource-types <t>…] [--accuracy exact\|high\|medium\|low]` |
| `groups` | `[--resource-type] [--min-confidence] [--limit] [--offset]` |
| `dismiss` | `--resource-type --member-ids <id> <id> [--note]` (≥2) |
| `dismissals` | `[--resource-type]` |
| `undismiss` | `--id <dismissal-id>` |
| `scan-status`, `cancel-scan` | none |

Array fields take a repeatable flag named after the field, matching
`files move --sources`: `--ids`, `--member-ids`, `--fields`, `--resource-types`,
and `--ids` on `compare`. No comma-splitting — a delimiter inside a flag is a
mini-language, and the repeatable form is already the house shape.

`link` caps at 20 children per request (`Field(min_length=1, max_length=20)`), so
a bulk linker chunks. The cap is the server's, stated in help, not enforced
client-side.

## Exit codes

Three different rules apply, and the third is the one to get wrong.

- **`link` → `BulkExit.CodeFor(errors)`.** `validate_kind` and every structural
  guard run *inside* the per-child loop (`core.py:56-70`), so a bad id, a
  duplicate id, or a bad kind lands in `errors` while the remaining children
  commit at HTTP 200. That is the established exit-3 contract.
- **`scan` → `ScanExit.CodeFor(status)`.** `{"status": "already_running"}` is a
  200 and maps to 3. A *library* scan in flight is a hard **409** instead, with
  `"A library scan is already running; retry after it completes."`
- **`cancel-scan` → plain 0.** It answers `not_running` or `stop_requested`, and
  `library cancel-scan` already returns 0 for the same shape.
- **`merge-metadata` → plain 0, not `BulkExit`.** Its `skipped` list is the
  documented no-op path — the field was empty on the source, or non-empty on the
  target without `--overwrite`. Mapping it to 3 would make the ordinary case exit
  non-zero.

## Client-side validation

Two guards, both for cases the server answers 200 to.

**`groups --limit` takes `Range(1, 200)`.** Declared `Query(50, le=200)`: the
ceiling is guarded, the floor is not. A negative limit slices to an empty page
(`out[offset : offset + limit]`) and returns 200 — less exposure than
`/api/search`'s unbounded `LIMIT -1`, but still a value silently honoured as a
different one.

**`unlink` takes an "at least one of" command validator** over `--ids` and
`--parent-id`. With neither, `variants.unlink(db, model, [])` returns `[]` and the
call answers `200 {"unlinked": []}` — a silent no-op. `backups settings set`
already carries this validator shape. When both are given, `parent_id` wins
(`core.py:124`), which the help states rather than the CLI refusing.

**`--resource-type` and `--accuracy` take `Choice`, forced by the generated
models.** Both are `Literal`s upstream, so Kiota types them as enums —
`LinkRequest_resource_type`, `ScanRequest_accuracy`, and one per model besides —
and the model cannot hold any other value. The allowed set is therefore
generator-derived, exactly as `JsonBodyInput.Validate` takes its field list from
`GetFieldDeserializers().Keys`; it is not a hand-maintained mirror of server
policy, and it moves when the client is regenerated. `Choice` is used so the
refusal is a parse error rather than an exception out of `Enum.TryParse`.

This is the first place in the repo that assigns a generated enum to a body
field. `link` is unaffected: its JSON body is validated and sent unchanged, so
Kiota's own deserializer handles the enum there.

**Nothing else is validated client-side.** `merge-metadata --fields` 400s naming
the allowed set; `compare --ids` 400s outside 2–4; `dismiss` requires ≥2
`member_ids` and 422s.

**Two fields are composed-type wrappers**, both `Optional[str]` upstream:
`UnlinkRequest.ParentId` and `DeleteItemRequest.ReparentTo`. Assigning through
the wrapper only when the flag was given is what keeps `--reparent-to ""`
distinguishable from the flag being absent — the same mechanism
`FilesService.BuildMarkersBody` already uses for `container_kind`.

**The variant `kind` vocabulary is not mirrored.** It is closed and scoped by
collection (`models/variants.py`: book 7, map 9, token 4, audio 5 values), and
`validate_kind` rejects an unknown one with a message listing the accepted set.
Mirroring it would be the client-side policy duplication thin pass-through
forbids — the same call already made for `in_use_by` and `resource_type`. `link`
takes JSON, so validating would additionally mean inspecting the parsed body.
The kinds are listed in `link`'s help instead, since nothing else exposes them.

## `compare` needed a generator fix

`GET /api/duplicates/compare` could not be called at all as generated. Kiota
rendered its required `ids` array query parameter with simple expansion —
`?ids={ids}` — which comma-joins the values, and FastAPI reads `ids` as repeated
keys. Verified against the running stack:

| Query | Result |
| --- | --- |
| `?resource_type=book&ids=<a>&ids=<b>` | 200 |
| `?resource_type=book&ids=<a>,<b>` | **400 "Compare needs between two and four items."** |

The spec is not at fault: OpenAPI 3.x defaults a query parameter to
`style: form, explode: true`, so FastAPI states neither, and the default is
exactly what it reads. Kiota ignores that for a required parameter — setting
`explode: true`, and then `style: form` alongside it, both left the template
byte-identical on 1.34.1.

`tools/normalize-spec.py` gains a second pass that drops `required` on
array-typed query parameters, which moves them into the exploded group
(`{&ids*,token*}`). Dropping `required` is a lie about the spec, and a narrow
one: it is confined to array query parameters, of which the whole spec has
exactly one, and nothing is lost because the CLI declares its own `--ids` flag
Required so the parameter is sent on every call. The alternative — assembling
the query string by hand at the call site — is what the generated-client rule
exists to prevent.

The regeneration diff is three lines across two files: the `compare` template,
twice, and the lock hash. The pass logs its count to stderr like the existing
one, so a Kiota release that fixes this makes the workaround visibly redundant.

## What the help must carry

**`mergeable_fields` never reaches the client.** `/compare` declares
`response_model=CompareResult`, which omits the field; `CompareResponse`, which
has it, is unused by this route. Verified live — a compare response holds only
`differences`, `items`, `page_count_min`, `resource_type` and
`suggested_parent_id`. So no endpoint exposes the copyable set, and
`merge-metadata --fields` cannot cross-reference `compare` for it. The book set
(18 values, `routers/duplicates/_helpers.py:35`) goes in `merge-metadata`'s help;
the other three collections are narrower and the 400 names them.

**`delete` is the block's only irreversible verb, and what it removes does not
depend on the flag.** The row goes either way, and `purge_references` takes
everything no foreign key would: `book_search` page rows, bookmarks, favorites,
tags, and campaign resource links with their shares. `_purge_derived` drops the
thumbnail and page cache. `--delete-file` decides one thing only — whether the
file leaves the disk, with its sidecars.

**`--delete-file` is `true|false` and Required.** The server defaults it to
`true`, and an omitted body becomes `DeleteItemRequest()`, so a bodyless call
destroys the file. That default is correct for its workflow: `delete_file: false`
drops the row and keeps the file, which the next `library rescan` re-indexes,
putting the duplicate straight back. But `files delete` spells the same intent
`--delete-files` and defaults to *soft*, so the CLI would carry two `delete`
verbs with the same flag name and opposite defaults. Requiring the value costs a
caller who already types `--resource-type` and `--id` nothing, mirrors no server
policy and changes none.

**`delete` on a parent with variants 409s** unless `--reparent-to` is given:
`""` promotes them all to standalone, an id names the inheritor, and the id must
already be one of that parent's variants. Empty-string-means-clear is the shape
the repo already documents for `system_family`, so `--reparent-to ""` must stay
reachable and distinguishable from the flag being absent.

**A record can outlive its file.** `delete_record` tolerates `ENOENT`
deliberately, so a record whose file a rescan already lost still deletes, and
`file_deleted` comes back `false`. `EROFS` is a 409 with nothing committed.

**The three structural rules** are per-child errors on `link`, not request
failures: two levels only (*"That item is already a variant of something else.
Link to its main version instead."*), same collection only, and an item cannot be
a variant of itself.

**`variant_label` is trimmed to 120 characters server-side**
(`services/variants.py:215`), silently. `promote`'s `--kind`/`--label` describe
the *old* parent, the copy being demoted — not the new one.

## Tests

Parse-level, matching the shipped command tests; no HTTP.

- All thirteen leaves exist under `duplicates`, and each declares the admin role
  and carries a response shape.
- `groups --limit` rejects `0`, `-1`, `201`; accepts `1` and `200`; reports a
  non-numeric value as a parse error rather than throwing.
- `unlink` errors with neither `--ids` nor `--parent-id`; parses with either.
- `delete` errors without `--delete-file`; parses with `true` and with `false`;
  `--reparent-to ""` parses and is distinguishable from absent.
- `link` requires exactly one of `--input`/`--stdin`, per the shipped
  `JsonBodyInput` convention.
- `compare` takes `--ids` repeatably.
- `dismiss` takes `--member-ids` repeatably.
- `merge-metadata` help lists the book field set; `link` help lists the kinds.
- A service test pins all thirteen URIs and every wire query-parameter name,
  matching `FilesServiceTests`.

## Documentation

- `tools/generate-api-coverage.py`: thirteen `IMPLEMENTED` entries, then
  regenerate `docs/grimoire-api-coverage.md`. Never hand-edit the markdown.
- `README.md`: thirteen rows in the Commands table.
- `docs/grimoire-api-notes.md`: a `## Duplicates` section, house style, recording
  the `delete_file` default and what `delete` purges, the per-child `link`
  contract, `mergeable_fields` being stripped by the response model, the silent
  `unlink` no-op, the 120-char label trim, and `groups --limit`'s missing floor.
- `docs/roadmap.md`: remove the **Duplicate handling** item from `## Next`, and
  correct nothing else — the "verbs carry `resource_type` in their paths" claim
  leaves with the item.
- `CHANGELOG.md` is owned by the release process and is not touched.

## Smoke test

The fixture has no duplicate books and creating one would break the idempotence
rule, so the block is read-only plus one reversible round trip:

- `scan-status` reports `running: false` on an idle stack.
- `cancel-scan` exits 0 and reports `not_running`.
- `groups` returns a listing; `--limit 0` and `--limit -1` are refused at parse
  time.
- `compare --ids <a> --ids <b>` over two fixture books returns two `items` and a
  `differences` array; one id alone is refused server-side.
- `link` two fixture books, assert `linked` names the child and `errors` is
  empty, then `unlink --ids <child>` and assert it comes back — a
  link/unlink round trip returns the fixture to its prior state, so a re-run
  converges.
- `link` with a bogus child id exits 3 and names it in `errors`.
- `delete` is **not** exercised: it is irreversible, and there is no fixture item
  whose loss a re-run could restore.
