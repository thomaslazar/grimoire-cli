# Grimoire compatibility

## Matrix

| grimoire-cli | Grimoire | Status |
|---|---|---|
| 0.1.x | 1.5.6 | initial support |

## Runtime check

`src/GrimoireCli/Api/GrimoireApiClient.cs` defines `MinSupportedVersion` and
`MaxTestedVersion`, both currently `"1.5.6"`. A check runs before the first
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

4. Diff the serializers backing the untyped response shapes — the spec types
   almost every response as `{}`, so the generator cannot see them and this is
   where response changes actually show up:

   ```bash
   git -C temp/grimoire diff vOLD..vNEW -- backend/routers/*/_serializers.py backend/routers/*/core.py backend/models/
   ```

5. Update DTOs, flags and help text to match. Re-run `bash docker/seed.sh` and
   `bash docker/smoke-test.sh`. Update `MinSupportedVersion` /
   `MaxTestedVersion` in `GrimoireApiClient.cs`, the matrix above, and the
   compatibility line in `README.md` — all in the same PR as the code change,
   alongside the regenerated `src/GrimoireCli/Generated/`.
