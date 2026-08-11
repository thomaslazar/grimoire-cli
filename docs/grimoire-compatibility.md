# Grimoire compatibility

## Matrix

| grimoire-cli | Grimoire | Status |
|---|---|---|
| 0.1.x | 1.5.6 | initial support |

## Runtime check

`src/GrimoireCli/Api/GrimoireApiClient.cs` defines `MinSupportedVersion` and
`MaxTestedVersion`, both currently `"1.5.6"`. `login` calls `GET /api/about`
after saving the token and compares the reported server version against that
range:

- Below `MinSupportedVersion` → a warning on stderr that some features may
  not work.
- Above `MaxTestedVersion` → a warning on stderr that the server is newer
  than anything this CLI has been tested against.
- Inside the range → nothing (a debug line only, under `--debug`).

Either way the CLI **never refuses to run** — the warning is advisory, not a
hard gate.

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
