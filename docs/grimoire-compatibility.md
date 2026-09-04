# Grimoire compatibility

## Matrix

| grimoire-cli | Grimoire | Status |
|---|---|---|
| 0.1.x | 1.5.6 | initial support, maintained on `support/grimoire-1.5.6` |
| 0.2.x | 1.6.0–1.6.1 | current, on `main` |

**One CLI version targets one server version.** Whoever stays on Grimoire 1.5.6
stays on grimoire-cli `0.1.x`, which is maintained on `support/grimoire-1.5.6` —
fixes are made and released there, then cherry-picked forward.

`main` targets Grimoire 1.6.1. Reaching 1.6.0 was more than a version bump: it
shortened the access token from 30 days to 30 minutes and made the library
writable, which is why the CLI renews its own session
([authentication.md](authentication.md)) and why the `files` endpoints exist at
all. 1.6.1 on top of it is additive only — `BookOut` gained `variant_count`,
`MapDetailResponse` gained `media_kind`, `GET /api/search/fields` and
`GET /api/maps/{id}/vtt` are new, and `GET /api/search` grew metadata matching
with `field:value` filters. Nothing was removed or renamed, so a 1.6.0 server
still works and `MinSupportedVersion` stays there.
`docker/docker-compose.yml` pins the `1.6.1` release tag, so the spec cannot
drift under the committed client between regenerations.

## Runtime check

`src/GrimoireCli/Api/GrimoireApiClient.cs` defines `MinSupportedVersion` and
`MaxTestedVersion`, currently `"1.6.0"` and `"1.6.1"`. A check runs before the first
request of any command, calling `GET /api/about` and comparing the reported
version against that range. It is throttled to once every 24 hours — a
config with a recent `lastVersionCheck` skips the probe entirely — and
`login` always forces one regardless of how recently the last check ran.
Both fields live in the config file (`lastVersionCheck`, `lastServerVersion`;
see [configuration.md](configuration.md)) so the cadence persists across
invocations.

- Below `MinSupportedVersion` → a warning on stderr that some features may
  not work.
- Above `MaxTestedVersion` → a warning on stderr that the server is newer
  than anything this CLI has been tested against.
- Inside the range → nothing (a debug line only, under `--debug`).
- No numeric component to compare (e.g. the literal `nightly`) → skipped
  silently, with a debug line only.

A probe that fails — unreachable server, non-2xx, unparseable body — is
silent except under `--debug`, and leaves `lastVersionCheck` untouched so the
next invocation retries rather than waiting out the full window.

Either way the CLI **never refuses to run** — the warning is advisory, not a
hard gate.

### Known limitation: one server, one slot

`lastVersionCheck` / `lastServerVersion` are a single slot in the config
file, not keyed by server. Pointing `--server` or `GRIMOIRE_SERVER` at a
second instance records that instance's version into the same slot, which
can suppress the check for the original server for up to 24 hours and, if
the two are alternated, can print a warning claiming the server "moved"
between versions that is not true of either one. This is warn-only, so
nothing breaks — but treat the record as belonging to whichever server was
checked most recently, not to any one server in particular.

## Handling a Grimoire release

1. Pin the reference clone to the new tag:

   ```bash
   git -C temp/grimoire fetch --depth 1 origin tag vX.Y.Z
   git -C temp/grimoire checkout vX.Y.Z
   ```

2. **Bump the Grimoire image tag in `docker/docker-compose.yml` and restart the
   stack on the new version first** — the client is regenerated from a running
   server, never a file on disk:

   ```bash
   docker compose -f docker/docker-compose.yml up -d --wait
   ```

3. **Regenerate the committed client and review the diff.** This is the
   authoritative list of what changed in the request surface — paths, methods,
   query parameters and every request body — and it beats reading release
   notes. Regenerating in place is what makes the diff exist at all: the
   previous output has to already be in git for `git diff` to show anything.

   ```bash
   bash tools/generate-api-client.sh
   git diff src/GrimoireCli/Generated
   ```

4. Diff the serializers backing the response shapes the spec still types as
   `{}` — the generator cannot see those, and stdout is a byte passthrough
   with no DTO to update, but a field the exit-code readers
   (`ReadStringProperty`, `HasItems`) key on, or documented behaviour in
   [grimoire-api-notes.md](grimoire-api-notes.md), can still change shape
   underneath them:

   ```bash
   git -C temp/grimoire diff vOLD..vNEW -- backend/routers/*/_serializers.py backend/routers/*/core.py backend/models/
   ```

5. Update flags and help text to match. Regenerate the `--help-full` sample
   file, now downstream of the client regeneration in step 3:

   ```bash
   dotnet run --project tools/GenerateJsonExamples -- src/GrimoireCli/Commands/JsonExamples.g.cs
   ```

   Re-run `bash docker/seed.sh` and `bash docker/smoke-test.sh`. Update
   `MinSupportedVersion` / `MaxTestedVersion` in `GrimoireApiClient.cs`, the
   matrix above, and the compatibility line in `README.md` — all in the same
   PR as the code change, alongside the regenerated
   `src/GrimoireCli/Generated/`.
