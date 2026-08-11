# Systems write commands and `me` — design

**Date:** 2026-08-10
**Status:** Approved. Deferred on 2026-08-11 behind the generated-API-client
migration; §3.2 revised against the generated client on 2026-08-11 and now in
implementation.
**Targets:** Grimoire **v1.5.6**, the version the CLI targets. Written against
v1.5.5; every mechanic in §2 was re-verified at the `v1.5.6` tag on 2026-08-11
and none changed, so the citations below are v1.5.6 line numbers.

---

## 1. Why this exists

This is the job the CLI was built for. Everything shipped so far reads; nothing
writes. The intended workflow is an agent looking metadata up on the web and
applying it to a Grimoire instance unattended, and that needs a write surface.

Three commands write, and one makes writing safe to attempt:

- `systems update` applies one system's metadata.
- `systems batch-update` applies many in a single transaction — the point of the
  server's bulk route is that tag creation is serialised, which per-item
  concurrent PATCHes could not do (upstream #270).
- `systems batch-tag` adds tags across a selection.
- `me` reports the caller's role, so an agent can discover whether it is allowed
  to write instead of learning it from a 403.

Scope is **systems only**. Books are the larger metadata surface but have no
commands at all — their read side is its own design, and a write command
landing before any way to find what to write to would be the wrong order.

---

## 2. Verified mechanics

Read from `temp/grimoire` at the `v1.5.6` tag. Every row cites its source,
because an uncited claim in this repo has historically been a wrong one.

| mechanic | verified behaviour | source |
|---|---|---|
| editable fields | 17 on `GameSystemUpdate`: `name`, `description`, `publishers`, `character_builder_url`, `character_builder_urls`, `urls`, `tags`, `genre`, `genres`, `dice_materials`, `system_family`, `parent_system`, `edition`, `license`, `year`, `cover_book_id`, `is_explicit` | `routers/systems/_schemas.py` |
| legacy singles | `genre` and `character_builder_url` are kept for backward compatibility; new clients send `genres` / `character_builder_urls` | same file, inline comments |
| single PATCH response | `{"status": "ok"}` — it does not echo the system, so it confirms nothing about what changed | `routers/systems/core.py:311` |
| unknown keys | dropped by pydantic before `model_dump`, so they never reach the database and never surface as an error | `routers/systems/core.py:302` |
| nulls | `model_dump(exclude_none=True)` drops explicit nulls, so `null` is a silent no-op. Clearing a field needs `""` | same line |
| bulk failure mode | skip-and-continue: an unresolved id or a rejected item goes to `errors`, the rest still apply. Returns `{updated: [ids], errors: [{id, detail}]}`, committing once and only if at least one item applied | `services/bulk_service.py::run_bulk_update` |
| bulk cap | `MAX_BULK_ITEMS = 1000` | `services/bulk_service.py` |
| bulk shares single's path | `apply_updates` is the same code both use, "so bulk and single edits can never drift apart" | `services/bulk_service.py` module docstring |
| tags | live in shared tables, not a column; `apply_updates` syncs them via the tag service and pops them from the `setattr` loop | `services/bulk_service.py::apply_updates` |
| `bulk/tags` is additive | merges with existing tags and never removes one. Returns `{updated, errors, tags: {id: [display tags]}}` | `services/bulk_service.py::run_bulk_add_tags` |
| `bulk/tags` validation | `ids` and `tags` both required and non-empty; `ids` capped at 1000 | `routers/_bulk_schemas.py::BulkAddTags` |
| **rename is sticky** | changing `name` sets `name_is_custom = True`, after which the scanner's `if not system.name_is_custom` gate stops re-deriving the name from the folder — permanently | `routers/systems/core.py:314-334`, `indexer/scan.py:358` |
| rename to same value | a no-op that returns early and does **not** set the flag | `routers/systems/core.py:326-327` |
| name clash | 409 on the single-item handler, a per-item `errors` entry in bulk | `routers/systems/core.py:303-308`, `:327-333` |
| blank name | rejected by a validator (422) — `name` is the system's identity and is NOT NULL | `routers/systems/_schemas.py:82-91` |
| role | all three write routes are `require_gm_or_admin` | `routers/systems/core.py:294`, `:339`, `:359` |
| `me` role | `GET /api/auth/me` is `Depends(get_current_user)` — any authenticated user, no role | `routers/auth/core.py:157-161` |
| `me` response | `{id, username, display_name, email, role, allow_explicit, campaign_access, oidc_linked}` | `routers/auth/core.py:177-186` |
| `me` side effect | sets a session cookie when the caller authenticated by Bearer without one, reusing the existing token rather than minting a new one | `routers/auth/core.py:167-170` |

### 2.1 Why `logout` is not implemented

`POST /api/auth/logout` calls `clear_auth_cookie` and returns `{"ok": true}`
(`routers/auth/core.py:132-136`). It clears a **session cookie**. This CLI
authenticates with a bearer JWT that is valid for 30 days with no refresh
endpoint, so the call would revoke nothing while appearing to. A verb that
looks like revocation and isn't is worse than no verb. Clearing the local token
belongs to `config`, and is not in this change.

---

## 3. Commands

```
grimoire-cli me
grimoire-cli systems update --id <id> {--input <file> | --stdin}
grimoire-cli systems batch-update       {--input <file> | --stdin}
grimoire-cli systems batch-tag          {--input <file> | --stdin}
```

`me` is top-level, matching `abs-cli`'s `MeCommand`.

`batch-` rather than `bulk-` is deliberate: `abs-cli` already settled this
vocabulary (`items batch-update`, `items batch-get`, `collections batch-add`),
and an agent driving both tools should learn one word. The cost — Grimoire's own
docs say "bulk" — is accepted and recorded here.

### 3.1 Input contract

`--input <file>` and `--stdin` are mutually exclusive, and exactly one is
required. There are no typed metadata flags and no inline JSON. This is
`abs-cli`'s settled shape for metadata bodies (`items update --id {--input |
--stdin}`, which carries no typed field flags at all), and it is the right side
of that project's own split: typed flags for small flat config sets like
`libraries update --name --icon`, a JSON body for a 17-field metadata object
with three nested arrays.

Bodies are Grimoire's own shapes, passed through unchanged:

| command | body |
|---|---|
| `update` | the field object, without `id` — that comes from `--id` |
| `batch-update` | `{"items": [{"id": "…", …fields}]}`, at most 1000 items |
| `batch-tag` | `{"ids": ["…"], "tags": ["…"]}`, both non-empty, at most 1000 ids |

### 3.2 Request DTOs validate the body

The generated client supplies the **URL, method and path parameters**; the body
is validated by hand-written request types. The spec does define all four body
schemas (`GameSystemUpdate`, `GameSystemBulkItem`, `GameSystemBulkUpdate`,
`BulkAddTags`) and Kiota generates them, but neither can do this job — verified
2026-08-11 against the committed `Generated/Models/GameSystemUpdate.cs`:

- It is an **`IAdditionalDataHolder`** (`:12`), its constructor seeds
  `AdditionalData` (`:156`), and `Serialize` ends in
  `writer.WriteAdditionalData(AdditionalData)` (`:221`). An unknown key is
  therefore **transmitted** to the server, which drops it silently — strictly
  worse than the hand-written approach, because it makes the silent no-op §3.2
  exists to prevent harder to catch, not easier.
- FastAPI emits `anyOf: [string, null]` for every nullable field, so each becomes
  a **composed-type wrapper**: `Name` is a `GameSystemUpdate.GameSystemUpdate_name?`
  (`:100`), not a `string`. Fourteen of the seventeen fields are wrapped this way;
  only `publishers`, `urls` and `character_builder_urls` come through as plain lists.

So the CLI gains **dedicated request types**, separate from the response types it
already has:

| type | fields | mirrors | used by |
|---|---|---|---|
| `GameSystemUpdateRequest` | the 17 editable, none required | `GameSystemUpdate` | `update` |
| `GameSystemBulkItem` | those 17 **plus a required `id`** | `GameSystemBulkItem` | items of `batch-update` |
| `GameSystemBulkUpdateRequest` | `items`, required | `GameSystemBulkUpdate` | `batch-update` |
| `BulkAddTagsRequest` | `ids`, `tags`, both required | `BulkAddTags` | `batch-tag` |

The item type is **not** the same as `update`'s body: the batch item carries a
required `id` where the single-item body must not carry one at all. Sharing one
type between them would either allow `id` where it is rejected, or reject it
where it is mandatory.

Each carries `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`,
so **System.Text.Json rejects an unknown key itself** — verified working under
the source generator, which is what Native AOT requires. A misspelled field
throws a `JsonException` naming the offending property, before any HTTP request
is made.

This is not a deviation from thin pass-through, and needs no entry in
`CLAUDE.md`. The CLI already maintains response DTOs bound to a Grimoire
version; request DTOs are the same commitment facing the other way. There is no
hand-written field list to drift, because the type *is* the list.

Why it matters: Grimoire drops unknown keys at pydantic validation, so a
misspelled field name produces `{"status": "ok"}` from `update` and — worse — an
entry in `updated` from `batch-update`, having changed nothing. For a tool
designed to run unattended, a silent no-op that reports success is the failure
class least likely to be caught and most likely to be trusted.

Separating request from response types is what makes this correct. Deriving the
allowed set from a response type would wave through `child_count`, `has_cover`
and every other read-only field, since `GameSystemSummary` carries 31 fields
where only 17 are editable.

Two consequences fall out for free:

- **`id` is rejected**, because it is not an editable field. A body pasted from
  a `systems get` dump or a `batch-update` file fails rather than silently
  updating whatever `--id` names.
- **Type errors are caught too** — `{"year": "soon"}` fails on deserialization.
  Validation therefore covers what the type expresses, names and types; value
  ranges and business rules remain the server's.

The raw body is validated by deserializing it, then **sent unchanged**. The CLI
never re-serializes the user's JSON, so it cannot alter what was meant — an
explicit `""` stays `""`, and an omitted field stays omitted.

Sending it unchanged is what decides how the two halves meet. The generated
builder's `ToPatchRequestInformation` takes a generated body and serializes it,
so the request is built with a throwaway empty instance and its content then
replaced with the user's bytes:

```csharp
var info = client.Api.Api.Systems[id].ToPatchRequestInformation(new GameSystemUpdate());
info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawJson)), "application/json");
```

The builder therefore contributes only what it is trustworthy for — the URL
template, the path parameter and the method — and the generated model never
reaches the wire.

`JsonException` is translated into a readable message rather than surfaced raw:
the offending key, its nearest match from `JsonTypeInfo.Properties`, and for
`id` specifically a note that it belongs in `--id` for `update` and in each item
for `batch-update`.

### 3.3 Exit codes

| code | meaning |
|---|---|
| 0 | everything applied |
| 1 | client-side refusal — unknown field, both or neither input source, unreadable file |
| 2 | API error — 401, 403, 404, 409 |
| 3 | **partial** — HTTP 200, but `errors` is non-empty |

Code 3 is new. Reusing 2 would conflate "the request failed" with "the request
succeeded and three items did not apply", which is precisely the distinction an
unattended caller needs. stdout still carries the full `{updated, errors}` JSON
in the partial case, so an agent can act on exactly what landed.

`update` is single-item and therefore all-or-nothing; it never exits 3.

### 3.4 Role tagging

All three write commands call `AddRoleRequired("gm or admin")` and pass
`permissionHint: "the gm or admin role"`, rendering as
`Permission denied. This operation requires the gm or admin role.`

This is the mechanism's **first real call site**. Until now `AddRoleRequired` was
exercised only by `RoleSectionTests` against a throwaway command, and the
tag/hint mapping in `CLAUDE.md` had never been used. `me` gets no tag.

---

## 4. Help-text caveats

Each is verified in §2 and non-obvious, so each belongs at the call site rather
than in a doc:

- **Renaming is permanent.** Changing `name` makes the name sticky; the scanner
  stops re-deriving it from the folder and never resumes. Folder reorganisation
  will no longer rename the system.
- **`batch-tag` only adds.** It cannot remove a tag. Use `batch-update` with
  `tags` to replace a set.
- **Clear a field with `""`, not `null`.** An explicit null is dropped and does
  nothing.
- **`batch-update` reports ids, not fields.** An id in `updated` means the row
  resolved, not that any value changed.
- **`genre` and `character_builder_url` are legacy singles**; prefer `genres`
  and `character_builder_urls`.
- **`me` sets a session cookie** when called with a Bearer token and no cookie.
  Harmless here — the CLI stores no cookies — but it is a write-ish side effect
  on a read.

---

## 5. Sequencing

Three increments, per the standing preference for one runnable step at a time:

1. **`me`** — a new resource, no write risk, and it lands the role-awareness the
   write commands assume.
2. **`systems update`** — `GameSystemUpdateRequest`, the `JsonException`
   translation, and the first
   `AddRoleRequired` call site.
3. **`systems batch-update` and `batch-tag`** — their two request DTOs wrap the
   item shape from step 2, and exit 3 arrives with them.

Each is independently useful and independently reviewable.

---

## 6. Out of scope

- **Books**, entirely.
- The remaining systems endpoints: cover (3), book-folders (2), and
  metadata-sources / metadata-search / metadata-fetch (3). The metadata-lookup
  trio is the more interesting future work — it wraps the server's add-on system,
  which fetches server-side with a per-field diff and a field whitelist.
- `logout`, per §2.1.
- Any orchestration that composes lookup and update. The CLI stays thin; the
  agent composes.

---

## 7. Verification

The four pre-PR gates, plus the published binary. `docker/seed.sh` leaves
`Shadowrun 4 DE` unpatched precisely so a write command has a target that starts
empty — the fixture was created for this change.

Smoke-test additions:

- `systems update` sets a field on `Shadowrun 4 DE`, and `systems get` reads it
  back — the first write→read round trip in the suite.
- An unknown field name exits 1 without a request being made.
- `batch-update` with one good and one bogus id exits **3**, with the good id in
  `updated` and the bogus one in `errors`.
- `batch-tag` adds a tag and leaves an existing one in place, proving additivity.
- `me` returns `role` of `admin` for the seeded admin account.
- Both input sources are exercised: `--input` for one command, `--stdin` for
  another.

All writes go to the local stack. The live instance is never a write target.

---

## 8. Open questions

| # | question | status |
|---|---|---|
| 1 | ~~Should `update` reject a body containing `id`?~~ | **RESOLVED.** Yes, and it needs no special case: `id` is not an editable field, so §3.2's check already rejects it. Only the message is special-cased |
| 2 | ~~Does the field validator belong in `SystemsService` or a shared helper?~~ | **RESOLVED, and dissolved.** There is no validator to place. Each resource declares a request DTO and `System.Text.Json` does the checking, so `books update` next run adds `BookUpdateRequest` and inherits the behaviour. The only shared code is the `JsonException` → readable-message translation |
| 3 | ~~Should an `--allow-unknown-fields` override exist?~~ | **RESOLVED. No.** The response DTOs already drop fields a newer Grimoire adds, with no escape hatch, and that is the accepted design. An override on writes alone would be inconsistent with reads and would undercut `MinSupportedVersion`/`MaxTestedVersion`, which is the compatibility mechanism |
