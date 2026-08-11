#!/usr/bin/env bash
# Regenerate the Kiota API client from the running stack's OpenAPI spec.
#
#   bash tools/generate-api-client.sh
#   GRIMOIRE_SERVER=http://localhost:9481 bash tools/generate-api-client.sh
#
# The spec is read from the running container, which pins the exact release this
# CLI targets — a snapshot on disk can be stale, a container cannot disagree
# with itself. Start it first:
#   docker compose -f docker/docker-compose.yml up -d --wait
#
# Every operation is generated, not a filtered subset: the diff across a version
# bump is the point, and the trimmer removes whatever the CLI never calls.
set -euo pipefail

SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
OUT="src/GrimoireCli/Generated"

command -v kiota >/dev/null 2>&1 \
  || { echo "kiota not on PATH — dotnet tool install --global Microsoft.OpenApi.Kiota" >&2; exit 1; }
curl -sf "$SERVER/api/openapi.json" -o /dev/null \
  || { echo "no spec at $SERVER/api/openapi.json — is the stack up?" >&2; exit 1; }

kiota generate \
  --openapi "$SERVER/api/openapi.json" \
  --language CSharp \
  --output "$OUT" \
  --class-name GrimoireApiClient \
  --namespace-name GrimoireCli.Generated \
  --clean-output

echo "generated $(find "$OUT" -name '*.cs' | wc -l) files into $OUT" >&2
