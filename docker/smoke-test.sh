#!/usr/bin/env bash
# Smoke test: exercises a built grimoire-cli binary against a running Grimoire.
#
#   bash docker/smoke-test.sh
#   GRIMOIRE_SERVER=http://localhost:9481 CLI=./publish/grimoire-cli bash docker/smoke-test.sh
#
# Expects a stack that is already up (see docker/docker-compose.yml), which keeps
# the script identical in CI and locally. Also requires the stack to be seeded
# (run `bash docker/seed.sh` first) — the seeded-data section below asserts on
# the fixture set it creates.
#
# WARNING: it writes $HOME/.grimoire-cli/config.json. Harmless in the
# devcontainer (container HOME isn't the host's), but running this on a host
# machine overwrites that host's saved grimoire-cli credentials.
set -euo pipefail

# GRIMOIRE_SERVER stays unexported: `systems list` must resolve the server from
# the config file `login` wrote, so a login that persisted nothing still fails.
SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
CLI="${CLI:-src/GrimoireCli/bin/Debug/net10.0/grimoire-cli}"
CONFIG="$HOME/.grimoire-cli/config.json"

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }
ok() { echo "  ok: $*" >&2; }

echo "smoke: $CLI against $SERVER" >&2
[ -x "$CLI" ] || fail "no executable CLI at $CLI"

# 1. The instance answers at all.
for i in $(seq 1 60); do
  curl -sf "$SERVER/api/health" >/dev/null 2>&1 && break
  [ "$i" -eq 60 ] && fail "no response from $SERVER/api/health after 60s"
  sleep 1
done
ok "health"

# Clear any stale config first: without this, a regressed ConfigManager.Save
# that silently writes nothing would still leave a *previous* run's config
# behind, and checks 3/4 below would pass against stale data instead of
# catching the regression.
rm -f "$CONFIG"

# 2. Login. Retried: the healthcheck can go green before user seeding commits,
# so a first-attempt 401 is a race, not a failure.
for i in $(seq 1 30); do
  if printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
      >"$WORK/login.out" 2>"$WORK/login.err"; then
    break
  fi
  if [ "$i" -eq 30 ]; then
    cat "$WORK/login.err" >&2
    fail "login never succeeded"
  fi
  sleep 1
done
ok "login exited 0"

# 3. The token and server were persisted.
[ -f "$CONFIG" ] || fail "no config written at $CONFIG"
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "config server is not $SERVER: $(cat "$CONFIG")"
jq -e '.accessToken | type == "string" and length > 0' "$CONFIG" >/dev/null \
  || fail "config holds no access token: $(cat "$CONFIG")"
ok "config has server and token"

# 4. The token authenticates, and stdout is JSON with logs kept on stderr.
# list.err is captured for diagnostics only (dumped on failure below) — nothing
# is asserted about its contents. At the default log level (LogSetup.cs sets
# minimum Warn) a clean run emits nothing to stderr anyway.
"$CLI" systems list >"$WORK/list.out" 2>"$WORK/list.err" \
  || { cat "$WORK/list.err" >&2; fail "systems list exited non-zero"; }
jq -e . "$WORK/list.out" >/dev/null \
  || fail "systems list stdout was not valid JSON: $(cat "$WORK/list.out")"
ok "systems list returned JSON on stdout"

# 5. A bad password fails cleanly and leaves the config alone.
cp "$CONFIG" "$WORK/config.before"
set +e
printf 'definitely-wrong' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
  >"$WORK/bad.out" 2>"$WORK/bad.err"
rc=$?
set -e
[ "$rc" -eq 2 ] || fail "bad password exited $rc, expected 2"
# LoginCommand.cs prints "Login failed: <exception message>" for any
# HttpRequestException — DNS failure, connection refused or a 500 would also
# match "login failed", so also require "401" (verified empirically: a bad
# password's HttpRequestException message includes "401 (Unauthorized)").
grep -qi "login failed" "$WORK/bad.err" \
  || fail "bad password produced no 'Login failed' message: $(cat "$WORK/bad.err")"
grep -q "401" "$WORK/bad.err" \
  || fail "bad password stderr did not mention 401: $(cat "$WORK/bad.err")"
cmp -s "$CONFIG" "$WORK/config.before" \
  || fail "a failed login modified $CONFIG"
ok "bad password exits 2 and leaves the config untouched"

# 6. The binary's own offline integrity check.
"$CLI" self-test >"$WORK/self.out" 2>"$WORK/self.err" \
  || { cat "$WORK/self.err" >&2; fail "self-test exited non-zero"; }
ok "self-test"

# 6b. me: the caller's own account, and the role a write command will need.
ME_JSON=$("$CLI" me 2>"$WORK/me.err") \
  || { cat "$WORK/me.err" >&2; fail "me exited non-zero"; }
[ "$(echo "$ME_JSON" | jq -r .username)" = "admin" ] \
  || fail "me should report username admin: $ME_JSON"
[ "$(echo "$ME_JSON" | jq -r .role)" = "admin" ] \
  || fail "me should report role admin: $ME_JSON"
[ "$(echo "$ME_JSON" | jq -r .id)" != "null" ] || fail "me returned no id: $ME_JSON"
ok "me reports the seeded admin account"

# --- seeded data -------------------------------------------------------------
# Requires docker/seed.sh to have run. Counts mirror the fixture set defined
# there; changing a fixture must change these numbers. EXPECTED_SYSTEMS is the
# top-level listing: container children are hidden unless asked for.
EXPECTED_SYSTEMS=7
EXPECTED_ALL_SYSTEMS=16

# syslist/sysget capture the CLI's own exit status via a plain assignment,
# then abort with fail(). They must be called directly (e.g. `syslist ...`
# on its own line), never nested inside `$(...)` — a nested substitution
# would run fail()'s `exit 1` in the subshell the substitution creates,
# which leaves only that subshell, not the script. Previously the CLI call
# lived inside `count() { ... | jq 'length'; }`, itself invoked as
# `$(count ...)`: a CLI that printed `[]` and then exited non-zero produced
# a pipeline whose last command (jq) still succeeded, so the failure was
# silently captured as the string "0" instead of aborting anything.
syslist() {
  LIST_JSON=$("$CLI" systems list "$@" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "systems list $* exited non-zero"; }
  COUNT=$(echo "$LIST_JSON" | jq 'length')
}

sysget() {
  GET_JSON=$("$CLI" systems get "$@" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "systems get $* exited non-zero"; }
}

syslist
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "expected $EXPECTED_SYSTEMS systems, got $COUNT"
ok "systems list returns $EXPECTED_SYSTEMS systems"

syslist --include-children
[ "$COUNT" -eq "$EXPECTED_ALL_SYSTEMS" ] \
  || fail "--include-children should return $EXPECTED_ALL_SYSTEMS, got $COUNT"
ok "--include-children returns $EXPECTED_ALL_SYSTEMS systems"

# The container is a shelf of systems: kind "parent", three editions, no books.
syslist
CONTAINER=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun")')
[ "$(echo "$CONTAINER" | jq -r .container_kind)" = "parent" ] \
  || fail "Shadowrun should be a parent container"
[ "$(echo "$CONTAINER" | jq -r .child_count)" -eq 3 ] \
  || fail "Shadowrun should hold 3 editions"
ok "the Shadowrun container reports kind=parent and child_count=3"

# A child carries the folder-derived edition and a link back to its container.
syslist --include-children
CHILD=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 6 DE")')
[ -n "$CHILD" ] || fail "Shadowrun 6 DE missing — the container did not adopt it"
[ "$(echo "$CHILD" | jq -r .edition)" = "6 DE" ] \
  || fail "edition should be folder-derived as '6 DE', got '$(echo "$CHILD" | jq -r .edition)'"
[ "$(echo "$CHILD" | jq -r .parent_name)" = "Shadowrun" ] \
  || fail "parent_name should be Shadowrun"
ok "a container child carries a derived edition and parent_name"

# --parent-id selects exactly one container's children.
CONTAINER_ID=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun") | .id')
syslist --parent-id "$CONTAINER_ID"
[ "$COUNT" -eq 3 ] || fail "--parent-id should return the 3 Shadowrun editions, got $COUNT"
ok "--parent-id lists one container's children"

# systems get on a container populates its `children` array — the one place a
# missing [JsonSerializable] registration on the nested summary shape would
# surface in the AOT binary.
sysget --id "$CONTAINER_ID"
[ "$(echo "$GET_JSON" | jq '.children | length')" -eq 3 ] \
  || fail "Shadowrun container should have 3 children"
ok "systems get on a container returns its children"

# The reserved slug one-page-rpgs becomes a one-page container with no marker
# file, and each loose PDF becomes its own system.
syslist
ONEPAGE=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "one-page-rpgs")')
[ "$(echo "$ONEPAGE" | jq -r .container_kind)" = "one-page" ] \
  || fail "one-page-rpgs should be a one-page container"
[ "$(echo "$ONEPAGE" | jq -r .child_count)" -eq 2 ] \
  || fail "one-page-rpgs should hold 2 games"
syslist --include-children
echo "$LIST_JSON" | jq -e '.[] | select(.name == "Lasers And Feelings")' >/dev/null \
  || fail "expected 'Lasers And Feelings' — prettify_collection_name capitalises 'and'"
ok "one-page-rpgs is a container holding 2 single-book systems"

# --- override flags -----------------------------------------------------------
# --server/--token are the flag tier of ConfigManager.Resolve — the tested
# precedence logic (ConfigManagerTests.cs) is otherwise unreachable through the
# CLI, since no command declared the flags until now. Config is deliberately
# emptied first so a config-file fallback can't mask a broken flag.
TOKEN=$(jq -r .accessToken "$CONFIG")
cp "$CONFIG" "$WORK/config.saved"
echo '{}' >"$CONFIG"

syslist --server "$SERVER" --token "$TOKEN"
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "systems list --server/--token returned $COUNT with an emptied config, expected $EXPECTED_SYSTEMS"
ok "systems list --server/--token succeeds against an emptied config"

set +e
"$CLI" systems list --server "$SERVER" --token "bogus-token" >/dev/null 2>"$WORK/badtoken.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a bogus --token should exit 2, got $rc"
grep -qi "not authenticated" "$WORK/badtoken.err" \
  || fail "a bogus --token gave no 'Not authenticated' message: $(cat "$WORK/badtoken.err")"
ok "a bogus --token against a correct --server exits 2"

cp "$WORK/config.saved" "$CONFIG"

# The child-hiding check runs BEFORE the filters, so a filter on metadata that
# only children carry returns [] with exit 0 — indistinguishable from a genuine
# miss. This asserts the trap exists rather than working around it.
syslist --genre Cyberpunk
[ "$COUNT" -eq 0 ] \
  || fail "--genre without --include-children should return 0, got $COUNT"
ok "a filter without --include-children returns [] on a containerised library"

syslist --include-children --genre Cyberpunk
[ "$COUNT" -eq 2 ] || fail "--genre Cyberpunk should match 2"
syslist --include-children --edition "6 DE"
[ "$COUNT" -eq 1 ] || fail "--edition '6 DE' should match 1"
syslist --include-children --edition "5 DE"
[ "$COUNT" -eq 2 ] || fail "--edition '5 DE' should match 2 across families"
syslist --include-children --edition "5 EN"
[ "$COUNT" -eq 2 ] || fail "--edition '5 EN' should match 2 across families"
syslist --include-children --license OGL
[ "$COUNT" -eq 1 ] || fail "--license OGL should match 1"
syslist --include-children --genre nope
[ "$COUNT" -eq 0 ] || fail "an unmatched filter should return []"
ok "filters narrow the result set"

# Shadowrun 4 DE is seeded raw, so a family filter must exclude it.
syslist --include-children --family Shadowrun
[ "$COUNT" -eq 2 ] \
  || fail "--family Shadowrun should match 2, not the raw Shadowrun 4 DE"
ok "systems with empty metadata are excluded by filters"

# The (nsfw) folder marker, not a PATCH, is what sets this. The system is flat,
# so it needs no --include-children.
syslist --explicit true
EXPLICIT=$(echo "$LIST_JSON" | jq -r '.[].name')
[ "$EXPLICIT" = "Fixture Explicit RPG" ] \
  || fail "--explicit true returned '$EXPLICIT'"
ok "--explicit true matches the nsfw-marked system"

# Filter values with an ampersand must survive URL encoding. parent_system is
# now folder-derived from the container name, with its !! sort prefix stripped.
syslist --include-children --parent-system "Dungeons & Dragons"
[ "$COUNT" -eq 1 ] || fail "a filter value containing '&' did not round-trip"
ok "ampersand in a filter value round-trips"

# Descending sort must actually be descending. Containers hold no books
# directly, so this runs over children to avoid a near-all-zero comparison.
syslist --include-children --sort book_count --desc
COUNTS=$(echo "$LIST_JSON" | jq '[.[].book_count]')
echo "$COUNTS" | jq -e '. == (. | sort | reverse)' >/dev/null \
  || fail "--sort book_count --desc was not descending: $COUNTS"
ok "--sort book_count --desc is ordered"

# A rejected sort key must fail before any request is made.
set +e
"$CLI" systems list --sort bogus >/dev/null 2>"$WORK/sort.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "--sort bogus should have failed"
grep -q "Must be one of" "$WORK/sort.err" || fail "no value-set message: $(cat "$WORK/sort.err")"
ok "--sort bogus is rejected at parse time"

# systems get: filters apply to the books and change the reported counts.
syslist --include-children --edition "6 DE"
SR6=$(echo "$LIST_JSON" | jq -r '.[0].id')
sysget --id "$SR6"
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 3 ] \
  || fail "Shadowrun 6 DE should have 3 books"
sysget --id "$SR6" --category core
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 2 ] || fail "--category core should keep 2 books"
[ "$(echo "$GET_JSON" | jq '.book_count')" -eq 2 ] \
  || fail "book_count should be recomputed from the filtered books"
ok "systems get filters books and recomputes counts"

# The canonical category, not the folder name.
sysget --id "$SR6" --category supplements
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 0 ] \
  || fail "'supplements' is a folder name and should match nothing"
sysget --id "$SR6" --category supplement
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 1 ] \
  || fail "'supplement' is the canonical category and should match 1"
ok "category filtering uses canonical values"

set +e
"$CLI" systems get --id no-such-id >/dev/null 2>"$WORK/nf.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a missing id should exit 2, got $rc"
grep -qi "not found" "$WORK/nf.err" || fail "no not-found hint: $(cat "$WORK/nf.err")"
ok "systems get on a missing id exits 2 with a hint"

# An empty id, ".", and "../about" all miss the /api/systems/{id} route and land
# on Grimoire's SPA catch-all, which answers with an HTML 200 instead of a JSON
# 404. Each must be caught as a JSON-parse failure and exit 2 with a readable
# message on stderr — not an unhandled JsonException and a raw stack trace.
for bad_id in "" "." "../about"; do
  set +e
  "$CLI" systems get --id "$bad_id" >/dev/null 2>"$WORK/badid.err"; rc=$?
  set -e
  [ "$rc" -eq 2 ] || fail "id '$bad_id' should exit 2, got $rc: $(cat "$WORK/badid.err")"
  grep -qi "could not be parsed as JSON" "$WORK/badid.err" \
    || fail "id '$bad_id' gave no not-JSON message: $(cat "$WORK/badid.err")"
  grep -qi "at System\.\|StackTrace\|Unhandled exception" "$WORK/badid.err" \
    && fail "id '$bad_id' leaked a stack trace: $(cat "$WORK/badid.err")"
done
ok "systems get on an empty, '.', or '../about' id exits 2 with no stack trace"

# The first write in this suite. Shadowrun 4 DE is seeded raw for exactly this.
# description is the field used deliberately: no assertion above filters on it,
# so re-running the suite converges instead of drifting. Do NOT write
# system_family here — the "--family Shadowrun should match 2" check depends on
# this system having none.
syslist --include-children
SR4=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 4 DE") | .id')
[ -n "$SR4" ] || fail "no Shadowrun 4 DE fixture to write to"

echo '{"description":"smoke fixture description"}' \
  | "$CLI" systems update --id "$SR4" --stdin >"$WORK/upd.out" 2>"$WORK/upd.err" \
  || { cat "$WORK/upd.err" >&2; fail "systems update exited non-zero"; }
jq -e '.status == "ok"' "$WORK/upd.out" >/dev/null \
  || fail "update should answer {\"status\":\"ok\"}: $(cat "$WORK/upd.out")"
sysget --id "$SR4"
[ "$(echo "$GET_JSON" | jq -r .description)" = "smoke fixture description" ] \
  || fail "the written description did not read back: $(echo "$GET_JSON" | jq -r .description)"
ok "systems update writes a field and systems get reads it back"

# An unknown field is refused client-side: exit 1, and no request is made.
printf '{"descriptoin":"typo"}' >"$WORK/typo.json"
set +e
"$CLI" systems update --id "$SR4" --input "$WORK/typo.json" >/dev/null 2>"$WORK/typo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown field should exit 1, got $rc: $(cat "$WORK/typo.err")"
grep -q "descriptoin" "$WORK/typo.err" || fail "no offending field named: $(cat "$WORK/typo.err")"
grep -q "description" "$WORK/typo.err" || fail "no suggestion offered: $(cat "$WORK/typo.err")"
ok "an unknown field exits 1 before any request"

# Nested objects, both ways. The generated entry models only describe their own
# fields because the spec is normalized before generation (kiota#2338), so this
# is what proves that workaround still holds in the shipped binary: a valid
# nested body applies, and a typo one level down is refused with its path.
cat >"$WORK/nested.json" <<'JSON'
{"publishers":[{"name":"Smoke Fixture Press","url":""}],
 "urls":[{"label":"Fixture","url":"https://example.test"}]}
JSON
"$CLI" systems update --id "$SR4" --input "$WORK/nested.json" >/dev/null 2>"$WORK/nested.err" \
  || { cat "$WORK/nested.err" >&2; fail "a valid nested body should apply"; }
sysget --id "$SR4"
[ "$(echo "$GET_JSON" | jq -r '.publishers[0].name')" = "Smoke Fixture Press" ] \
  || fail "the nested publisher did not read back: $(echo "$GET_JSON" | jq -c .publishers)"
ok "a valid nested body applies"

set +e
echo '{"publishers":[{"nmae":"typo"}]}' \
  | "$CLI" systems update --id "$SR4" --stdin >/dev/null 2>"$WORK/nestedtypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "a nested typo should exit 1, got $rc: $(cat "$WORK/nestedtypo.err")"
grep -q 'publishers\[0\].nmae' "$WORK/nestedtypo.err" \
  || fail "no path to the nested typo: $(cat "$WORK/nestedtypo.err")"
grep -q "'name'" "$WORK/nestedtypo.err" \
  || fail "no suggestion from the nested model: $(cat "$WORK/nestedtypo.err")"
ok "a typo inside a nested entry exits 1 with its path"

# Both sources, and neither, are parse-time refusals.
set +e
"$CLI" systems update --id "$SR4" --stdin --input "$WORK/typo.json" >/dev/null 2>"$WORK/both.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "--stdin with --input should exit 1, got $rc"
grep -q "not both" "$WORK/both.err" || fail "no mutual-exclusion message: $(cat "$WORK/both.err")"
set +e
"$CLI" systems update --id "$SR4" >/dev/null 2>"$WORK/none.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "no body source should exit 1, got $rc"
ok "--input and --stdin are mutually exclusive and one is required"

# batch-update: one good id and one bogus id must exit 3, applying the good one.
# license, not description or system_family: no assertion above filters on a
# license other than OGL, so this stays idempotent across re-runs.
cat >"$WORK/batch.json" <<JSON
{"items":[{"id":"$SR4","license":"Smoke Fixture License"},
          {"id":"no-such-id","license":"x"}]}
JSON
set +e
"$CLI" systems batch-update --input "$WORK/batch.json" >"$WORK/batch.out" 2>"$WORK/batch.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "a partial batch should exit 3, got $rc: $(cat "$WORK/batch.err")"
jq -e --arg id "$SR4" '.updated | index($id) != null' "$WORK/batch.out" >/dev/null \
  || fail "the good id should be in updated: $(cat "$WORK/batch.out")"
jq -e '.errors | length == 1 and .[0].id == "no-such-id"' "$WORK/batch.out" >/dev/null \
  || fail "the bogus id should be the only error: $(cat "$WORK/batch.out")"
ok "batch-update applies the good id and exits 3 on a partial"

# A fully-applying batch exits 0.
echo "{\"items\":[{\"id\":\"$SR4\",\"license\":\"Smoke Fixture License\"}]}" \
  | "$CLI" systems batch-update --stdin >"$WORK/batch2.out" 2>"$WORK/batch2.err" \
  || { cat "$WORK/batch2.err" >&2; fail "a fully-applying batch should exit 0"; }
jq -e '.errors | length == 0' "$WORK/batch2.out" >/dev/null \
  || fail "no errors expected: $(cat "$WORK/batch2.out")"
ok "batch-update exits 0 when every item applies"

# batch-tag is additive: the second call must not displace the first tag.
echo "{\"ids\":[\"$SR4\"],\"tags\":[\"smoke-alpha\"]}" \
  | "$CLI" systems batch-tag --stdin >"$WORK/tag1.out" 2>"$WORK/tag1.err" \
  || { cat "$WORK/tag1.err" >&2; fail "batch-tag exited non-zero"; }
echo "{\"ids\":[\"$SR4\"],\"tags\":[\"smoke-beta\"]}" \
  | "$CLI" systems batch-tag --stdin >"$WORK/tag2.out" 2>"$WORK/tag2.err" \
  || { cat "$WORK/tag2.err" >&2; fail "the second batch-tag exited non-zero"; }
jq -e --arg id "$SR4" '.tags[$id] | index("smoke-alpha") != null and index("smoke-beta") != null' \
  "$WORK/tag2.out" >/dev/null \
  || fail "batch-tag should have merged both tags: $(cat "$WORK/tag2.out")"
sysget --id "$SR4"
echo "$GET_JSON" | jq -e '.tags | index("smoke-alpha") != null' >/dev/null \
  || fail "the first tag did not survive the second call: $(echo "$GET_JSON" | jq -c .tags)"
ok "batch-tag adds a tag and leaves the existing one in place"

# A bogus id alone is still exit 3, and no ids resolve.
echo '{"ids":["no-such-id"],"tags":["smoke-alpha"]}' \
  >"$WORK/tagbad.json"
set +e
"$CLI" systems batch-tag --input "$WORK/tagbad.json" >"$WORK/tagbad.out" 2>"$WORK/tagbad.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "an all-bogus batch-tag should exit 3, got $rc"
ok "batch-tag exits 3 when an id does not resolve"

# An unknown key in a batch item is refused client-side.
printf '{"items":[{"id":"%s","licence":"typo"}]}' "$SR4" >"$WORK/batchtypo.json"
set +e
"$CLI" systems batch-update --input "$WORK/batchtypo.json" >/dev/null 2>"$WORK/batchtypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown field in an item should exit 1, got $rc"
grep -q "licence" "$WORK/batchtypo.err" || fail "no offending field named: $(cat "$WORK/batchtypo.err")"
ok "an unknown field inside a batch item exits 1"

echo "smoke: all checks passed" >&2
