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

# --- seeded data -------------------------------------------------------------
# Requires docker/seed.sh to have run. Counts mirror the fixture set defined
# there; changing a fixture must change these numbers.
EXPECTED_SYSTEMS=8

count() { "$CLI" systems list "$@" 2>/dev/null | jq 'length'; }

[ "$(count)" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "expected $EXPECTED_SYSTEMS systems, got $(count)"
ok "systems list returns $EXPECTED_SYSTEMS systems"

[ "$(count --genre Cyberpunk)" -eq 2 ] || fail "--genre Cyberpunk should match 2"
[ "$(count --edition 6)" -eq 1 ] || fail "--edition 6 should match 1"
[ "$(count --edition 5)" -eq 4 ] || fail "--edition 5 should match 4 across families"
[ "$(count --license OGL)" -eq 1 ] || fail "--license OGL should match 1"
[ "$(count --genre nope)" -eq 0 ] || fail "an unmatched filter should return []"
ok "filters narrow the result set"

# Shadowrun 4 DE is seeded raw, so a family filter must exclude it.
[ "$(count --family Shadowrun)" -eq 2 ] \
  || fail "--family Shadowrun should match 2, not the raw Shadowrun 4 DE"
ok "systems with empty metadata are excluded by filters"

# The (nsfw) folder marker, not a PATCH, is what sets this.
EXPLICIT=$("$CLI" systems list --explicit true | jq -r '.[].name')
[ "$EXPLICIT" = "Vampire The Masquerade 5 EN" ] \
  || fail "--explicit true returned '$EXPLICIT'"
ok "--explicit true matches the nsfw-marked system"

# Filter values with an ampersand must survive URL encoding.
[ "$(count --parent-system "Dungeons & Dragons")" -eq 1 ] \
  || fail "a filter value containing '&' did not round-trip"
ok "ampersand in a filter value round-trips"

# Descending sort must actually be descending.
COUNTS=$("$CLI" systems list --sort book_count --desc | jq '[.[].book_count]')
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
SR6=$("$CLI" systems list --edition 6 | jq -r '.[0].id')
[ "$("$CLI" systems get --id "$SR6" | jq '.books | length')" -eq 3 ] \
  || fail "Shadowrun 6 DE should have 3 books"
CORE=$("$CLI" systems get --id "$SR6" --category core)
[ "$(echo "$CORE" | jq '.books | length')" -eq 2 ] || fail "--category core should keep 2 books"
[ "$(echo "$CORE" | jq '.book_count')" -eq 2 ] \
  || fail "book_count should be recomputed from the filtered books"
ok "systems get filters books and recomputes counts"

# The canonical category, not the folder name.
[ "$("$CLI" systems get --id "$SR6" --category supplements | jq '.books | length')" -eq 0 ] \
  || fail "'supplements' is a folder name and should match nothing"
[ "$("$CLI" systems get --id "$SR6" --category supplement | jq '.books | length')" -eq 1 ] \
  || fail "'supplement' is the canonical category and should match 1"
ok "category filtering uses canonical values"

set +e
"$CLI" systems get --id no-such-id >/dev/null 2>"$WORK/nf.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a missing id should exit 2, got $rc"
grep -qi "not found" "$WORK/nf.err" || fail "no not-found hint: $(cat "$WORK/nf.err")"
ok "systems get on a missing id exits 2 with a hint"

echo "smoke: all checks passed" >&2
