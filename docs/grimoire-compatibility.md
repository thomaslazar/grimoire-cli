# Grimoire compatibility

## Matrix

| grimoire-cli | Grimoire | Status |
|---|---|---|
| 0.1.x | 1.5.5 | initial support |

## Runtime check

`src/GrimoireCli/Api/GrimoireApiClient.cs` defines `MinSupportedVersion` and
`MaxTestedVersion`, both currently `"1.5.5"`. `login` calls `GET /api/about`
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

2. Diff the two specs structurally. No spec snapshot is committed to this
   repo — start each version's container in turn and pull its spec fresh:

   ```bash
   for v in 1.5.4 X.Y.Z; do
     docker run -d --rm --name spec-$v -p 9500:9481 -e OPDS_ENABLED=false hunterreadca/grimoire:$v
     until curl -sf localhost:9500/api/openapi.json -o /tmp/spec-$v.json; do sleep 2; done
     docker stop spec-$v
   done
   python3 -c "
   import json
   a = json.load(open('/tmp/spec-1.5.4.json')); b = json.load(open('/tmp/spec-X.Y.Z.json'))
   print('added paths:', sorted(set(b['paths']) - set(a['paths'])))
   print('removed paths:', sorted(set(a['paths']) - set(b['paths'])))
   "
   ```

3. Diff the serializers backing the untyped response shapes — the spec types
   almost every response as `{}`, so this is where shape changes actually
   show up:

   ```bash
   git -C temp/grimoire diff vOLD..vNEW -- backend/routers/*/_serializers.py backend/routers/*/core.py backend/models/
   ```

4. Update DTOs, flags and help text to match. Bump the Grimoire image tag in
   `docker/docker-compose.yml`. Re-run `bash docker/seed.sh` and
   `bash docker/smoke-test.sh`. Update `MinSupportedVersion` /
   `MaxTestedVersion` in `GrimoireApiClient.cs`, the matrix above, and the
   compatibility line in `README.md` — all in the same PR as the code change.
