# File management commands

**Status:** approved
**Roadmap item:** MVP 1, *Ingest*, in [roadmap.md](../roadmap.md)
**Verified against:** `hunterreadca/grimoire:1.6.1`, source at tag `v1.6.1`
(`temp/grimoire/backend/routers/files/`, `backend/services/library_fs/`)

## Problem

The pipeline has no front. A file arriving on disk is only visible to the CLI
once someone puts it there by other means and triggers a rescan, and there is no
way to ask whether an upload landed *and* indexed. The `files` API is what
Grimoire 1.6.0 made the library writable for, and it is the block the rest of the
ingest workflow hangs off.

Ten endpoints, all `require_admin`.

## Prerequisite, now met

`docker/docker-compose.yml` mounted the library `:ro` until this branch's parent
commit, and Grimoire refuses a read-only library with **409** — `assert_writable`
in `services/library_fs/paths.py` probes with `os.access` before writing, and the
write modules catch `EROFS` as a backstop — so the mount was the only thing
gating every write here. It is now `:rw`, verified by a folder
create answering 409 before and 200 after.

## Verified server behaviour

Read from `backend/routers/files/core.py` and `backend/services/library_fs/`.

- **`upload` is one file per request, deliberately.** The handler's own docstring:
  a single multi-file request would make the batch succeed or fail together, and
  "a 200-file import that dies on file 40 would leave the user with no idea which
  39 landed". Multipart, `MAX_UPLOAD_BYTES` = 8 GiB → **413**. The file lands
  under a temporary name and is renamed into place only once fully written, so an
  interrupted upload never leaves a truncated file for the scanner.
- **The two `on_conflict` defaults differ on purpose.** `upload` defaults to
  **`rename`** — "an upload is an explicit *add this*, so landing it under a
  suffixed name is friendlier than discarding it". `move` defaults to **`skip`** —
  a bulk reorganisation should step over a collision and report it. **Neither ever
  overwrites**; `_dest_for` has no overwrite branch at all.
- **`upload` does not validate `on_conflict`; `move` does.** `move`'s schema
  carries `pattern="^(skip|rename)$"` and 422s. `upload`'s is a bare `Form(...)`
  field, and `_dest_for` treats anything that is not `"skip"` as rename — so
  `--on-conflict overwrite` would silently rename and answer 200.
- **`delete` defaults to a *soft* delete.** `delete_files: false` removes the
  indexed rows and everything keyed to them; the files stay and the next rescan
  re-adds whatever is still on disk and not excluded. It works on a read-only
  library, where nothing could be unlinked anyway. `delete_files: true` is the
  irreversible one: unlinked, **not** moved to a trash folder, and the row goes
  with its tags, favorites, bookmarks, progress and campaign links.
- **`files folder delete` is always hard.** `delete_folder` calls the same
  `fs.delete_path` as `delete_entry` does under `delete_files: true`. There is no
  soft form of it. Nothing in the name says so.
- **The typed-name guard is server-side.** A folder still holding content is
  refused with **428 `confirm_required`** until `confirm_name` matches the
  folder's own name — "the only guard standing between a mis-click and a
  collection". An empty folder, or one holding only markers and empty
  descendants, goes without it.
- **`browse` is DB-aware and bounded.** It merges the directory read with what
  Grimoire knows, so `record_id`/`title` distinguish indexed records from loose
  files the scanner ignored — the "did my upload land *and* index?" check.
  `limit` is **silently clamped** to `max(1, min(limit, 2000))`; `total` reports
  the true count and `truncated` says whether `entries` is a prefix. Also returns
  `writable` and `singletons_taken`. `child_count` per folder row stops at
  `CHILD_COUNT_CAP` = 1000.
- **Container kinds are a closed set**: `parent`, `one-page`, `agnostic`,
  `family`, `publisher`, `generic` (`indexer/constants.py`). An unknown one is a
  400. **`one-page` and `agnostic` are singletons** — only one of each may exist,
  recognised only at the top level of `books/`, and `browse` reports
  `singletons_taken` as `{kind: path}`.
- **`scaffold` creates a fixed category set**: Core, Supplements, Adventures,
  Character Sheets, Maps, Handouts, Homebrew, Starter Sets. It reports `created`
  and `existing`, so it is idempotent.
- **`markers` is a partial patch.** `container_kind` and `nsfw` are both
  optional; omitted means left alone.
- **Error codes** (`_STATUS`): 403 forbidden, 404 not_found, 409 conflict,
  409 read_only, 413 too_large, 500 io_error, 400 noop/invalid, **428
  confirm_required**. Anything unmapped is a 400.

## Command shape

From the nesting rule in [cli-design.md](../cli-design.md) — several methods on
one path nest, distinct sibling paths stay flat with leaf names mirroring the
path segment. `/api/files/folder` is POST+DELETE, so `folder` nests; its three
sibling paths become flat leaves under it.

| Command | Endpoint |
|---|---|
| `files browse [--path] [--limit]` | `GET /api/files/browse` |
| `files upload --destination --file [--relative-dir] [--on-conflict]` | `POST /api/files/upload` |
| `files move --sources… --destination [--on-conflict]` | `POST /api/files/move` |
| `files rename --path --new-name` | `POST /api/files/rename` |
| `files delete --path [--confirm-name] [--delete-files]` | `POST /api/files/delete` |
| `files folder create --parent --name [--container-kind] [--nsfw]` | `POST /api/files/folder` |
| `files folder delete --path [--confirm-name]` | `DELETE /api/files/folder` |
| `files folder markers --path [--container-kind] [--nsfw]` | `PUT /api/files/folder/markers` |
| `files folder scaffold --path` | `POST /api/files/folder/scaffold` |
| `files folder contents --path` | `GET /api/files/folder/contents` |

Every command calls `AddRoleRequired("admin")` and passes
`permissionHint: "the admin role"`.

`--path` on `browse` is optional: omitted lists the library root, which is what
the server does with `path=""`.

## Validators

Three of the four guard a silent server-side fallback, which is the same ground
`OptionHelpers.Choice` and `Range` were already built on:

| Flag | Validator | Why |
|---|---|---|
| `--limit` | `Range(1, 2000)` | server clamps silently and answers 200 |
| `--on-conflict` | `Choice(skip, rename)` | `upload` does not validate; anything ≠ `skip` renames |
| `--container-kind` | `Choice(parent, one-page, agnostic, family, publisher, generic)` | 400 on unknown, but the set is otherwise invisible |
| `--nsfw` | `bool?` | tri-state on `markers`: omitted means leave alone |

`--limit` gives `Range` its second consumer, which settles the question the
backups final review raised about whether it earned being a shared helper.

`move --sources` is repeatable (`AllowMultipleArgumentsPerToken`), mapping to the
endpoint's own `sources` array — one request, not a client-side loop.

## Upload takes one file per invocation

The command mirrors the endpoint: one `--file`, one request, the server's bytes
on stdout. Importing many is a shell loop, which gives per-file exit codes for
free.

The alternative — a repeatable `--file` that the CLI loops over — was rejected
because the CLI would have to synthesize an aggregate response the server never
sent, breaking the byte-passthrough rule in
[input-output.md](../input-output.md), and invent partial-failure semantics the
server has no opinion on. No existing command loops client-side.

`abs-cli` cannot settle this: its `upload` posts every file in one
`MultipartFormDataContent` because the ABS API accepts a multi-file request.
Grimoire's refuses to, by design, so the situation does not arise there.

## Components

- **`src/GrimoireCli/Commands/FilesCommand.cs`** — `browse`, `upload`, `move`,
  `rename`, `delete`, and hosting the `folder` subgroup.
- **`src/GrimoireCli/Commands/FilesFolderCommands.cs`** — the five `folder`
  subcommands. Named for its group, and distinct from the existing
  `BookFolderCommands.cs`, which serves `systems book-folders` — a different
  feature.
- **`src/GrimoireCli/Services/FilesService.cs`** — the ten calls plus
  `internal static` body builders for the two partial-patch bodies.

`upload` builds a `MultipartBody` with one part named `file`, following
`SystemsService.UploadCoverAsync` — the CLI's only existing multipart call, whose
comment records that an empty `MultipartBody` throws, so the part must be added
before `ToPostRequestInformation`. The other Form fields (`destination`,
`relative_dir`, `on_conflict`) are parts of the same body.

**`DELETE /api/files/folder` carries a request body** (`DeleteFolderRequest`),
which is unusual for a DELETE and is what the generated builder expects —
`ToDeleteRequestInformation(body)`.

**Composed-type wrappers**, as with `BackupSettingsPatch`: `MarkersRequest`'s
`ContainerKind` and `Nsfw`, and `DeleteRequest`'s `ConfirmName`. Assigning
through the wrapper only when the flag was given is what keeps `markers` a
partial patch. `CreateFolderRequest`, `MoveRequest`, `RenameRequest` and
`ScaffoldRequest` are plain.

## Help text

The caveats that cannot be read off a response sample:

- **`delete`** — soft by default: rows go, files stay, a rescan restores them;
  works on a read-only library. `--delete-files` is irreversible and takes tags,
  favorites, bookmarks, progress and campaign links with the row. 428 when the
  target is a non-empty folder and `--confirm-name` is absent or wrong.
- **`folder delete`** — **always deletes the files**, unlike `files delete`,
  which is soft unless asked. Same 428 guard.
- **`upload`** — one file per request; loop for many. Defaults to renaming on a
  collision and never overwrites. 413 above 8 GiB. `--relative-dir` recreates a
  sub-path under the destination.
- **`move`** — defaults to **skipping** a collision, unlike `upload`; reports
  `moved` and `skipped` per source.
- **`browse`** — capped at 2000: read `total` and `truncated` before treating the
  listing as complete. An entry carrying `record_id` is in the catalogue; one
  without is present on disk but not indexed.
- **`folder create`** — `one-page` and `agnostic` may exist only once;
  `browse`'s `singletons_taken` reports which are gone.
- **`markers`** — omitted fields are left alone.
- **`scaffold`** — reports `created` and `existing`, so re-running is safe.

Neither delete command takes a prompt or a `--yes`: `library cleanup-missing`
settled that, and here the server's own 428 already provides the guard.

## Explicitly out of scope

- **Client-side looping over many uploads.** See above.
- **Reading `browse` before a write to pre-empt a conflict or a 428.** That is a
  second endpoint per invocation and client-side mirroring of server policy.
- **Any client-side prompt on the destructive commands.**

## Testing

**Unit** — `FilesServiceTests`: each of the ten calls resolves to its own path
(the ten near-identical sends are exactly where a copy-paste reaches the wrong
endpoint, and every response has a different shape only sometimes); the two
partial-patch builders leave an omitted flag null and set the right wrapper
branch; `--confirm-name ""` survives as an empty string rather than being
dropped. `FilesCommandTests`: every command renders `Role required: admin` and a
response shape; the required flags are required; `--on-conflict overwrite`,
`--container-kind shelf` and `--limit 0`/`--limit 2001` are all rejected at parse
time; the two delete commands' Notes state their opposite defaults.

**Smoke** — one lifecycle under a single temp folder, create-then-clean-up as the
`backups` block does, so a re-run converges:

1. `folder create --parent books --name __smoke_files` → capture the path.
2. `folder markers --path … --nsfw true` → assert the marker.
3. `folder scaffold --path …` → assert `created` holds the eight categories.
4. `folder contents --path …` → assert `has_content`.
5. `upload --destination … --file <fixture>` → assert `path`/`name`/`size`.
6. `browse --path …` → assert the file appears, and that `record_id` is absent
   (it is on disk but not yet indexed — the distinction the endpoint exists for).
7. `rename` then `move` within the temp tree → assert `from`/`to` and `count`.
8. `delete --path <file>` without `--delete-files` → assert `files_deleted: false`.
9. `folder delete --path … --confirm-name __smoke_files` → assert it is gone, and
   that `browse` no longer lists it.

Step 9 is what keeps the run idempotent. Step 6 is the only place the DB-aware
listing is exercised end to end.

## Documentation

- **README Commands table** — ten rows, each suffixed `(admin)`.
- **`tools/generate-api-coverage.py`** — ten `IMPLEMENTED` entries, then
  regenerate; expect `files` `0 / 10` → `10 / 10` and the Total up by exactly 10.
- **`docs/grimoire-api-notes.md`** — a `## Files` entry for the findings above,
  led by the two delete semantics and the differing `on_conflict` defaults.
- **`docs/cli-design.md`** — add `files` to the resource list.
- **`docs/roadmap.md`** — remove MVP item 1 at ship time and renumber.
