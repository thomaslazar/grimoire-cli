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

# Kiota drops every property of a schema reached only through an `anyOf: [array
# of $ref, null]` — microsoft/kiota#2338, still open and present in 1.34.1, the
# latest release. FastAPI emits that wrapper for any Optional[list[Model]], so
# PublisherEntry, both LinkEntry variants and CampaignResourceInput generate as
# empty shells that know none of their own fields. Collapsing the wrapper to the
# array branch is enough to restore them, and the branch it drops carries no
# information the CLI uses: a property absent from a request body is already how
# "not set" is expressed, and the CLI reads no response through these types.
# Verified by generating the same schema both ways.
SPEC_FILE=$(mktemp --suffix=.json)
trap 'rm -f "$SPEC_FILE"' EXIT
echo "$SPEC_JSON" | python3 "$(dirname "$0")/normalize-spec.py" > "$SPEC_FILE"

kiota generate \
  --openapi "$SPEC_FILE" \
  --language CSharp \
  --output "$OUT" \
  --class-name GrimoireApiClient \
  --namespace-name GrimoireCli.Generated \
  --clean-output

# Kiota records the file it read, which is the normalized copy under /tmp — a
# path that differs every run and names nothing a reader can fetch. Restore the
# server the spec was pulled from; the hash beside it still covers what was
# generated, so the pair reads as "this server's spec, through this pipeline".
# printf, not jq's own newline: Kiota writes the file without a trailing one, and
# adding one would put a spurious hunk in every regeneration diff.
TMP_LOCK=$(mktemp)
jq --arg loc "$SERVER/api/openapi.json" '.descriptionLocation = $loc' "$LOCK_FILE" \
  | printf '%s' "$(cat)" > "$TMP_LOCK"
mv "$TMP_LOCK" "$LOCK_FILE"

echo "generated $(find "$OUT" -name '*.cs' | wc -l) files into $OUT" >&2
