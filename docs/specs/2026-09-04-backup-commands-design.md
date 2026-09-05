# Backup commands

**Status:** approved
**Roadmap item:** MVP 1, *Safety*, in [roadmap.md](../roadmap.md)
**Verified against:** `hunterreadca/grimoire:1.6.1`, source at tag `v1.6.1`
(`temp/grimoire/backend/routers/backups/`, `backend/services/backup/`). The
backups router and service are byte-identical between `v1.6.0` and `v1.6.1`.

## Problem

Both `abs-cli` workflows open with a backup. That was optional while Grimoire's
library was mounted read-only, and stops being optional the moment the `files`
block lands, because that is when the CLI can move and delete real files. A
backup writes to the data directory rather than the library, so it needs no
remount and can ship first. The archive itself protects the database and
user-authored assets, not the library tree — the files the `files` block will
touch still need a separate backup.

## What the API does and does not offer

Six endpoints, all `require_admin`: list, create, settings read, settings write,
delete, download.

**There is no restore endpoint, and no upload.** `abs-cli`'s `backup` group has
`apply` (restore) and `upload`; neither exists here — `grep -i restore` over the
whole spec returns nothing. So a Grimoire backup is an artifact the CLI can take
and fetch, but putting one back is out of band. This does not undermine the
block: the point of a backup before a destructive operation is that the archive
exists. It does mean `download` must say so, rather than implying a round trip.

## Verified server behaviour

Read from `backend/routers/backups/core.py` and
`backend/services/backup/_config.py`.

- **`create` snapshots the database under a read lock**, so writes are held off
  for the duration — the handler's own docstring calls it "brief for a typical
  library, but not instant".
- **`create` 409s when a backup is already in flight** (`RuntimeError` →
  `HTTPException(409)`), and 500s on `OSError`.
- **`delete` returns 204 with no body** and is irreversible.
- **`download` returns a `FileResponse`**, `application/zip`, with
  `Content-Disposition` naming the archive.
- **`PUT /settings` is a partial patch despite the method.** Every field on
  `BackupSettingsPatch` is optional and omitted ones are left alone. It returns
  the full effective settings, unlike `systems update`'s `{"status": "ok"}`.
- **Four fields are env-lockable.** Setting `backup_schedule`,
  `backup_retention_count`, `backup_retention_gb` or `backup_dir` while the
  corresponding env var is set is a **400**. `GET /settings` reports
  `schedule_env_locked`, `retention_count_env_locked`, `retention_gb_env_locked`
  and `dir_env_locked`, so a caller can tell before trying.
- **`backup_schedule_hour` / `_minute` / `_weekday` and both retentions are NOT
  env-lockable** and are **silently clamped**: `max(0, min(23, hour))`,
  `min(59, minute)`, `min(6, weekday)`, `max(0, …)` for both retentions. No
  error, exit 0, and a value the caller did not ask for.
- **`backup_schedule` is a closed set** — `("off", "hourly", "daily", "weekly")`
  from `VALID_SCHEDULES`; anything else is a 400.
- **`weekday` is 0=Mon … 6=Sun**, per `DEFAULTS`' own comment.
- **`backup_dir: ""` resets to the default** (`DATA_PATH/backups`), and a
  non-empty value is validated for writability at save time, not at the next
  scheduled run.
- **Fixture defaults** are `schedule: "off"`, `hour: 3`, `minute: 0`,
  `weekday: 0`, which is what makes an idempotent smoke write possible.
- **`list` returns `directory` and `total_bytes`** alongside the rows, and each
  row carries `version` — `"unknown"` when the archive's manifest is unreadable,
  which is what makes a cross-version restore detectable.

## Command shape

From the nesting rule in [cli-design.md](../cli-design.md) — several methods on
one path nest, distinct sibling paths stay flat:

| Command | Endpoint |
|---|---|
| `backups list` | `GET /api/backups` |
| `backups create` | `POST /api/backups` |
| `backups delete --id` | `DELETE /api/backups/{id}` |
| `backups download --id --output` | `GET /api/backups/{id}/download` |
| `backups settings get` | `GET /api/backups/settings` |
| `backups settings set --…` | `PUT /api/backups/settings` |

`settings` nests because that path is GET+PUT, exactly as `systems cover` does.
The group is `backups`, not `abs-cli`'s `backup`: leaf names mirror the path
segment, and the path is `/api/backups`.

Every command calls `AddRoleRequired("admin")` and passes
`permissionHint: "the admin role"`, because all six routes are `require_admin`.

`settings set` flags, mirroring the API's body field names:

| Flag | Field | Validation |
|---|---|---|
| `--schedule` | `backup_schedule` | `Choice`: off, hourly, daily, weekly |
| `--hour` | `backup_schedule_hour` | `Range` 0–23 |
| `--minute` | `backup_schedule_minute` | `Range` 0–59 |
| `--weekday` | `backup_schedule_weekday` | `Range` 0–6 |
| `--retention-count` | `backup_retention_count` | `Range` min 0 |
| `--retention-gb` | `backup_retention_gb` | `Range` min 0 |
| `--dir` | `backup_dir` | none; `""` resets |

At least one is required, matching the `addons settings` validator.

## Client-side range rejection

**Out-of-range numeric values are rejected at parse time rather than passed
through.** This is a deliberate exception to thin pass-through, taken on the
same grounds `OptionHelpers.Choice` already exists: its doc says a server that
silently falls back when given an unknown value "would otherwise return
differently-shaped data with exit 0, so an unrecognised value is rejected here
instead". Clamping is that failure mode exactly — `--hour 99` stores 23, exits
0, and says nothing, so a caller who fat-fingers a schedule gets a working
backup at the wrong time and no signal.

The cost is stated plainly: this mirrors a server constraint, so if upstream
widens a range the CLI is wrong until someone notices. The ranges are asserted
in one place and cited to `core.py`.

Add `OptionHelpers.Range(string name, string description, int min, int? max = null)`
alongside `Choice`, returning `Option<int?>`. `max` is nullable for the two
retention fields, which have a floor and no ceiling.

## Components

- **`src/GrimoireCli/Commands/BackupsCommand.cs`** — the `backups` group:
  `list`, `create`, `delete`, `download`, and the `settings` subgroup.
- **`src/GrimoireCli/Commands/BackupSettingsCommands.cs`** — the nested
  `settings get|set` pair, split out the way `CoverCommands.cs` is split from
  `SystemsCommand.cs`.
- **`src/GrimoireCli/Services/BackupsService.cs`** — one service for the
  resource: `ListAsync`, `CreateAsync`, `DeleteAsync`, `DownloadAsync`,
  `SettingsAsync`, `UpdateSettingsAsync`, plus
  `internal static BuildSettingsBody(…)`.
- **`src/GrimoireCli/Commands/OptionHelpers.cs`** — gains `Range`.

Generated builders, all confirmed present: `Api.Api.Backups`
(`ToGetRequestInformation`, `ToPostRequestInformation` with no body),
`Api.Api.Backups.Settings` (`ToGetRequestInformation`,
`ToPutRequestInformation(body)`), `Api.Api.Backups[id]`
(`ToDeleteRequestInformation`), `Api.Api.Backups[id].Download`
(`ToGetRequestInformation`). `download` uses `SendStreamAsync`, as
`SystemsService.CoverAsync` does.

**Every `BackupSettingsPatch` field is a composed-type wrapper** — int fields
carry `.Integer`, string fields `.String` — because each is `X | None` upstream.
`BuildSettingsBody` assigns through the wrapper only when the flag was given, so
an omitted flag stays absent from the body and the server leaves that field
alone. It is `internal` so a test can pin that a client regeneration cannot
silently change it, exactly as `AddonsService.BuildUpdateBody` and
`LibraryService.BuildBody` are.

## The 204 on delete

`delete`'s empty body passes through unmodified: `SendAsync` returns `""`,
`IsJsonOrEmpty` already accepts it, and `WriteRawJson` prints an empty line.
No special-casing. `jq` exits 0 with no output on both empty input and a bare
newline, so nothing downstream breaks, and the help text states that `delete`
answers with no body. This is the first shipped command whose endpoint returns
204 and therefore sets the de-facto convention for the `tags`, `users` and
`campaigns` deletes that come later.

## The 409 on create

A plain error: the generic non-success path reports the server's message on
stderr and exits non-zero. **Not** mapped to exit 3. `library rescan` uses exit 3
because its already-running case is an HTTP **200** that would otherwise be
indistinguishable from a started scan; a 409 is already unambiguous, and
introducing exit 3 on an error path would blur what that code means.

## Help text

- **`list`** — `version` is `unknown` when an archive's manifest is unreadable.
- **`create`** — snapshots under a read lock, so writes are blocked for the
  duration; 409 if a backup is already running.
- **`delete`** — irreversible, no confirmation prompt, and answers with no body.
  Takes no `--yes`: `library cleanup-missing` settled that here, because the
  callers are agents, so a prompt is either bypassed by a flag that becomes
  boilerplate or hangs a non-interactive caller.
- **`download`** — `-` writes the zip to stdout, a path writes the file and
  prints a `SavedFile` receipt. **No restore endpoint exists**, so the archive is
  the whole recovery path and restoring it is out of band.
- **`settings get`** — a field whose `*_env_locked` is true cannot be written.
- **`settings set`** — omitted fields are left alone despite the PUT; echoes the
  full settings; `--weekday` is 0=Mon…6=Sun; `--dir ""` resets to the default;
  writing an env-locked field is a 400, so read `settings get` first.

## Explicitly out of scope

- **Restore and upload.** No endpoint exists for either.
- **Reading `settings get` before a write to pre-empt a 400 on a locked field.**
  That is a second endpoint per invocation and client-side mirroring of server
  policy; the help text names the flags to check instead.
- **A confirmation prompt on `delete`.** Settled by `library cleanup-missing`.

## Testing

**Unit** — `OptionHelpersTests`: `Range` accepts its bounds (0 and 23), rejects
`-1` and `24`, and min-only accepts any non-negative value.
`BackupsServiceTests`: `BuildSettingsBody` leaves an omitted flag null and sets
the correct wrapper branch for each given one (`.Integer` for the five numeric
fields, `.String` for `--schedule` and `--dir`), including that `--dir ""`
survives as an empty string rather than being dropped.
`BackupsCommandTests`: every command renders a `Role required: admin` section
and a response shape, `delete`/`download` require `--id`, `download` requires
`--output`, and `settings set` errors with no flags.

**Smoke** — `create` writes a real archive, so the block must clean up after
itself or runs stop converging. Create-then-clean-up, the shape the book-folders
block uses:

1. `settings get` → assert the seven fields and four `*_env_locked` flags.
2. `settings set --schedule off --hour 3` → the fixture defaults, so this is a
   no-op on a seeded stack and converges on a re-run. Only fixed values are
   written, per the rule in CLAUDE.md.
3. `create` → capture `id`.
4. `list` → assert that id is present, and that `directory` and `total_bytes`
   are reported.
5. `download --id <id> --output "$WORK/backup.zip"` → assert the receipt's
   `bytes` matches the row's `size_bytes`.
6. `delete --id <id>` → assert exit 0 and an empty body.
7. `list` → assert the id is gone.

Step 7 is what keeps the run idempotent, and step 5 is the only place the
download path is exercised end to end.

## Documentation

- **README Commands table** — six rows, each suffixed `(admin)`.
- **`tools/generate-api-coverage.py`** — six `IMPLEMENTED` entries, then
  regenerate `docs/grimoire-api-coverage.md` from the running stack.
- **`docs/grimoire-api-notes.md`** — a `## Backups` entry for the findings the
  source is slow to reveal: the read lock, the silent clamping and which fields
  it applies to, the env locks, the PUT-that-patches, `""` resetting `dir`, and
  the absence of any restore endpoint.
- **`docs/cli-design.md`** — add `backups` to the resource list.
- **`docs/roadmap.md`** — remove MVP item 1 at ship time and renumber, as the
  vocabularies block did.
