#!/usr/bin/env bash
# Smoke test: exercises a built grimoire-cli binary against a running Grimoire.
#
#   bash docker/smoke-test.sh
#   GRIMOIRE_SERVER=http://localhost:9481 CLI=./publish/grimoire-cli bash docker/smoke-test.sh
#
# It does NOT start, seed or reset the stack — bring it up first (see
# docker/docker-compose.yml). That keeps the script identical in CI and locally.
#
# WARNING: it writes $HOME/.grimoire-cli/config.json. Harmless in the
# devcontainer (container HOME isn't the host's), but running this on a host
# machine overwrites that host's saved grimoire-cli credentials.
set -euo pipefail

SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
CLI="${CLI:-src/GrimoireCli/bin/Debug/net10.0/grimoire-cli}"
CONFIG="$HOME/.grimoire-cli/config.json"

# Deliberately NOT exporting GRIMOIRE_SERVER: `systems list` must resolve the
# server from the config file that `login` wrote. With it in the environment,
# a login that failed to persist anything would still pass this test.

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

echo "smoke: all checks passed" >&2
