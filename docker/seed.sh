#!/usr/bin/env bash
# Seed the local Grimoire stack with a fixture library.
#
#   bash docker/seed.sh
#   GRIMOIRE_SERVER=http://localhost:9481 bash docker/seed.sh
#
# Writes fixture PDFs into the library directory, rescans, then PATCHes the
# metadata that folder structure cannot express (family, genres, license, year),
# and generates the fixture add-on index the addons smoke-test section installs
# from.
# edition and parent_system are left out of the PATCH bodies — a container child
# already has both folder-derived from the scan. Grimoire mounts the library
# read-only, so seeding writes from this side and the server only reads.
#
# Re-runnable: the library is rebuilt from scratch each time. To reset the
# database as well: docker compose -f docker/docker-compose.yml down, then
# rm -rf docker/data docker/library/books — the boot scan indexes whatever
# library tree is on disk, so a database-only reset leaves stale rows that
# survive as is_missing and still count toward book_count.
#
# Renaming/re-marking a fixture folder (e.g. dropping "(nsfw)") needs that
# database reset, not just a re-seed: rescan only ever sets is_explicit=true,
# never clears it on an existing system row (backend/indexer/scan.py:347-348
# in temp/grimoire @ v1.5.6). A re-seed alone leaves the stale flag in place.
set -euo pipefail

SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
LIBRARY="${GRIMOIRE_LIBRARY_LOCAL:-docker/library}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fail() { echo "SEED FAIL: $*" >&2; exit 1; }
say() { echo "  $*" >&2; }

python3 -c "import fitz" 2>/dev/null \
  || fail "python3-fitz (PyMuPDF) missing — rebuild the devcontainer, or: sudo apt-get install -y python3-fitz"

# 1. Wait for the instance.
for i in $(seq 1 60); do
  curl -sf "$SERVER/api/health" >/dev/null 2>&1 && break
  [ "$i" -eq 60 ] && fail "no response from $SERVER/api/health after 60s"
  sleep 1
done
say "health ok"

# 2. Authenticate.
TOKEN=$(curl -sf -X POST "$SERVER/api/auth/login" \
  -H 'content-type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq -r .token)
[ -n "$TOKEN" ] && [ "$TOKEN" != null ] || fail "login failed — is the stack seeded with docker/users.json.example?"
AUTH="Authorization: Bearer $TOKEN"
say "authenticated"

# 3. Build the fixture tree. Folder names carry edition and language because the
#    scanner parses neither from the path — one folder is exactly one system.
rm -rf "${LIBRARY:?}/books"
mkdir -p "$LIBRARY/books"

book() {  # book <system-folder> <category-folder> <filename> <pages>
  local dir="$LIBRARY/books/$1/$2"
  mkdir -p "$dir"
  python3 "$HERE/make-fixtures.py" "$dir/$3.pdf" "$4"
}

container() {  # container <folder> — mark a folder as a parent-system container
  mkdir -p "$LIBRARY/books/$1"
  touch "$LIBRARY/books/$1/.parent-system-container"
}

container "Shadowrun"
container "Das Schwarze Auge"
container "The Dark Eye"
container "!!Dungeons & Dragons"
container "Vampire The Masquerade"

book "Shadowrun/6 DE"                  core         "SR6 Grundregelwerk"      12
book "Shadowrun/6 DE"                  core         "SR6 Kreuzfeuer"           8
book "Shadowrun/6 DE"                  supplements  "SR6 Strassengrimoire"     6
book "Shadowrun/5 DE"                  core         "SR5 Grundregelwerk"      10
book "Shadowrun/5 DE"                  core         "SR5 Datenpfade"           5
book "Shadowrun/4 DE"                  core         "SR4 Grundregelwerk"       7
book "Shadowrun/4 DE"                  adventures   "SR4 Kampfhandbuch"        5
book "Shadowrun/4 DE"                  supplements  "SR4 Strassengrimoire"     4
book "!!Dungeons & Dragons/5e EN"      core         "Players Handbook"        14
book "!!Dungeons & Dragons/5e EN"      adventures   "Lost Mine of Phandelver"  9
book "Das Schwarze Auge/5 DE"          core         "DSA5 Regelwerk"          11
book "Das Schwarze Auge/5 DE"          core         "DSA5 Aventurien"          4
book "The Dark Eye/5 EN"               core         "TDE5 Core Rules"         11
book "Vampire The Masquerade/5 EN"     core         "V5 Corebook"             13
book "Fixture Explicit RPG (nsfw)"     core         "Fixture RPG Core Rules"   3

# one-page-rpgs is a reserved slug, so v1.5.6 treats it as a one-page CONTAINER
# with no marker file: each loose PDF becomes its own single-book system, named
# by prettify_collection_name — which capitalises any word with no uppercase in
# it, so "Lasers and Feelings" indexes as "Lasers And Feelings". On v1.5.4 the
# same folder produced ONE system with its subfolders as categories. A loose PDF
# at the books root is still skipped entirely (scan.py requires a directory).
mkdir -p "$LIBRARY/books/one-page-rpgs"
python3 "$HERE/make-fixtures.py" "$LIBRARY/books/one-page-rpgs/Lasers and Feelings.pdf" 1
python3 "$HERE/make-fixtures.py" "$LIBRARY/books/one-page-rpgs/Honey Heist.pdf" 1

EXPECTED_BOOKS=17
say "wrote $EXPECTED_BOOKS fixture books"

# 4. Rescan, then wait for completion. `running` reads false before the scan
#    starts too, so completion is tested with scanned_books.
curl -sf -X POST "$SERVER/api/rescan" -H "$AUTH" \
  -H 'content-type: application/json' -d '{"metadata_mode":"new"}' >/dev/null \
  || fail "rescan request failed"
for i in $(seq 1 90); do
  ST=$(curl -sf "$SERVER/api/scan-status" -H "$AUTH")
  RUNNING=$(echo "$ST" | jq -r .running)
  SCANNED=$(echo "$ST" | jq -r .scanned_books)
  if [ "$RUNNING" = false ] && [ "$SCANNED" -ge "$EXPECTED_BOOKS" ]; then break; fi
  [ "$i" -eq 90 ] && fail "scan did not finish: running=$RUNNING scanned=$SCANNED expected>=$EXPECTED_BOOKS"
  sleep 1
done
say "scan complete ($SCANNED books)"

# 5. Apply the metadata folders cannot express. edition and parent_system are
#    folder-derived under a container, so they are deliberately absent here —
#    patching them would mask whether derivation works. system_family is
#    PATCHed because these fixtures use no .system-family-container — v1.5.6
#    added one (upstream #301), so a family shelf would derive it from the
#    folder instead. Shadowrun 4 DE is left raw: it mirrors a fresh import and
#    is the fixture the future metadata commands will target.
patch_system() {  # patch_system <system name> <json body>
  local name="$1" body="$2" id
  id=$(curl -sf "$SERVER/api/systems?include_children=true" -H "$AUTH" \
       | jq -r --arg n "$name" '.[] | select(.name == $n) | .id')
  [ -n "$id" ] || fail "no system named '$name' after the scan"
  curl -sf -X PATCH "$SERVER/api/systems/$id" -H "$AUTH" \
    -H 'content-type: application/json' -d "$body" >/dev/null \
    || fail "PATCH failed for '$name'"
  say "patched $name"
}

patch_system "Shadowrun 6 DE" '{"system_family":"Shadowrun","genres":["Cyberpunk"],"year":2019,"publishers":[{"name":"Pegasus Spiele","url":""}]}'
patch_system "Shadowrun 5 DE" '{"system_family":"Shadowrun","genres":["Cyberpunk"],"year":2013,"publishers":[{"name":"Pegasus Spiele","url":""}]}'
patch_system "Dungeons & Dragons 5e EN" '{"system_family":"D&D","genres":["Fantasy"],"license":"OGL","year":2014,"publishers":[{"name":"Wizards of the Coast","url":""}]}'
patch_system "Das Schwarze Auge 5 DE" '{"system_family":"The Dark Eye","genres":["Fantasy"],"year":2015,"publishers":[{"name":"Ulisses Spiele","url":""}]}'
patch_system "The Dark Eye 5 EN" '{"system_family":"The Dark Eye","genres":["Fantasy"],"year":2016,"publishers":[{"name":"Ulisses North America","url":""}]}'
patch_system "Vampire The Masquerade 5 EN" '{"system_family":"World of Darkness","genres":["Horror"],"year":2018,"publishers":[{"name":"Renegade Game Studios","url":""}]}'

TOP=$(curl -sf "$SERVER/api/systems" -H "$AUTH" | jq 'length')
ALL=$(curl -sf "$SERVER/api/systems?include_children=true" -H "$AUTH" | jq 'length')
say "seed complete — $TOP top-level systems, $ALL including children"
[ "$TOP" -eq 7 ] || fail "expected 7 top-level systems, got $TOP"
[ "$ALL" -eq 16 ] || fail "expected 16 systems including children, got $ALL"

# 6. Generate the add-on index from the checked-in fixture manifest, so its
# digest can never drift from what's on disk. Served to the grimoire container
# by the addon-index nginx service (docker-compose.yml).
python3 "$HERE/make-addon-index.py"
