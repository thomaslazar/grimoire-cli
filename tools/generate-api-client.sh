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
LOCK_FILE="$OUT/kiota-lock.json"

command -v kiota >/dev/null 2>&1 \
  || { echo "kiota not on PATH — dotnet tool install --global Microsoft.OpenApi.Kiota" >&2; exit 1; }

# The committed tree records the generator version that produced it. A newer
# Kiota picked up by a rebuilt devcontainer would mix generator churn into a
# regeneration diff whose entire value is showing API changes only.
if [ -f "$LOCK_FILE" ]; then
  EXPECTED_VERSION=$(jq -r '.kiotaVersion' "$LOCK_FILE")
  INSTALLED_VERSION=$(kiota --version 2>&1 | head -1 | cut -d+ -f1)
  if [ "$INSTALLED_VERSION" != "$EXPECTED_VERSION" ]; then
    echo "kiota $INSTALLED_VERSION is installed, but $LOCK_FILE was generated with $EXPECTED_VERSION." >&2
    echo "Install the matching version: dotnet tool update --global Microsoft.OpenApi.Kiota --version $EXPECTED_VERSION" >&2
    exit 1
  fi
fi

SPEC_JSON=$(curl -sf "$SERVER/api/openapi.json") \
  || { echo "no spec at $SERVER/api/openapi.json — is the stack up?" >&2; exit 1; }
# GRIMOIRE_SERVER is honoured, so a maintainer may have it pointed at the live
# instance by mistake — make the source of the spec visible before generating.
echo "generating from $SERVER (Grimoire $(echo "$SPEC_JSON" | jq -r '.info.version'))" >&2

kiota generate \
  --openapi "$SERVER/api/openapi.json" \
  --language CSharp \
  --output "$OUT" \
  --class-name GrimoireApiClient \
  --namespace-name GrimoireCli.Generated \
  --clean-output

echo "generated $(find "$OUT" -name '*.cs' | wc -l) files into $OUT" >&2
