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

# The version the stack is actually running, read from the stack rather than
# hardcoded, so a Grimoire bump doesn't also require editing this number.
EXPECTED_VERSION=$(curl -sf "$SERVER/api/openapi.json" | jq -r .info.version)

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

# The five controlled-vocabulary reads. Read-only, so this block is idempotent.
# parent-systems is asserted present but allowed to be empty: Grimoire's
# DEFAULT_PARENT_SYSTEMS is (), so a non-empty assertion would fail on a fresh
# stack, while the other four are seeded.
for pair in "genres:genres" "licenses:licenses" "parent-systems:parent_systems" \
            "system-families:families" "dice-materials:dice_materials"; do
  cmd="${pair%%:*}"
  key="${pair##*:}"
  "$CLI" "$cmd" list >"$WORK/$cmd.out" 2>"$WORK/$cmd.err" \
    || { cat "$WORK/$cmd.err" >&2; fail "$cmd list exited non-zero"; }
  jq -e "has(\"$key\")" "$WORK/$cmd.out" >/dev/null \
    || fail "$cmd list did not return a .$key envelope: $(cat "$WORK/$cmd.out")"
  if [ "$cmd" != "parent-systems" ]; then
    jq -e ".$key | length > 0" "$WORK/$cmd.out" >/dev/null \
      || fail "$cmd list should return the seeded defaults: $(cat "$WORK/$cmd.out")"
    jq -e ".$key[0] | has(\"id\") and has(\"name\")" "$WORK/$cmd.out" >/dev/null \
      || fail "$cmd list entries should carry id and name: $(cat "$WORK/$cmd.out")"
  fi
  ok "$cmd list returned a .$key envelope"
done

# Backups. create writes a real archive, so this creates one, exercises every
# read against it, and deletes it again — the create-then-clean-up shape, so a
# re-run converges instead of accumulating archives.
"$CLI" backups settings get >"$WORK/bset.out" 2>"$WORK/bset.err" \
  || { cat "$WORK/bset.err" >&2; fail "backups settings get exited non-zero"; }
jq -e 'has("backup_schedule") and has("backup_dir") and has("schedule_env_locked")' "$WORK/bset.out" >/dev/null \
  || fail "backups settings get should report settings and env locks: $(cat "$WORK/bset.out")"
ok "backups settings get reports settings and env locks"

# The fixture defaults, so this is a no-op on a seeded stack and converges.
"$CLI" backups settings set --schedule off --hour 3 >"$WORK/bsset.out" 2>"$WORK/bsset.err" \
  || { cat "$WORK/bsset.err" >&2; fail "backups settings set exited non-zero"; }
jq -e '.backup_schedule == "off" and .backup_schedule_hour == 3' "$WORK/bsset.out" >/dev/null \
  || fail "backups settings set should echo the full settings: $(cat "$WORK/bsset.out")"
ok "backups settings set echoes the effective settings"

"$CLI" backups create >"$WORK/bcreate.out" 2>"$WORK/bcreate.err" \
  || { cat "$WORK/bcreate.err" >&2; fail "backups create exited non-zero"; }
BACKUP_ID=$(jq -r .id "$WORK/bcreate.out")
[ -n "$BACKUP_ID" ] && [ "$BACKUP_ID" != "null" ] \
  || fail "backups create should return an id: $(cat "$WORK/bcreate.out")"
ok "backups create returned a new archive"

"$CLI" backups list >"$WORK/blist.out" 2>"$WORK/blist.err" \
  || { cat "$WORK/blist.err" >&2; fail "backups list exited non-zero"; }
jq -e --arg id "$BACKUP_ID" 'any(.backups[]; .id == $id)' "$WORK/blist.out" >/dev/null \
  || fail "backups list should include the new archive: $(cat "$WORK/blist.out")"
jq -e 'has("directory") and has("total_bytes")' "$WORK/blist.out" >/dev/null \
  || fail "backups list should report directory and total_bytes"
ok "backups list includes the new archive"

"$CLI" backups download --id "$BACKUP_ID" --output "$WORK/backup.zip" >"$WORK/bdl.out" 2>"$WORK/bdl.err" \
  || { cat "$WORK/bdl.err" >&2; fail "backups download exited non-zero"; }
EXPECTED_BYTES=$(jq -r --arg id "$BACKUP_ID" '.backups[] | select(.id == $id) | .size_bytes' "$WORK/blist.out")
jq -e --argjson n "$EXPECTED_BYTES" '.bytes == $n' "$WORK/bdl.out" >/dev/null \
  || fail "download receipt should match the listed size_bytes: $(cat "$WORK/bdl.out")"
ok "backups download wrote the archive and reported its size"

"$CLI" backups delete --id "$BACKUP_ID" >"$WORK/bdel.out" 2>"$WORK/bdel.err" \
  || { cat "$WORK/bdel.err" >&2; fail "backups delete exited non-zero"; }
[ ! -s "$WORK/bdel.out" ] || [ "$(tr -d '[:space:]' <"$WORK/bdel.out")" = "" ] \
  || fail "backups delete answers 204 and should print no body: $(cat "$WORK/bdel.out")"
ok "backups delete answered 204 with no body"

"$CLI" backups list >"$WORK/blist2.out" 2>/dev/null \
  || fail "backups list exited non-zero after delete"
jq -e --arg id "$BACKUP_ID" 'any(.backups[]; .id == $id) | not' "$WORK/blist2.out" >/dev/null \
  || fail "the deleted archive should be gone: $(cat "$WORK/blist2.out")"
ok "the deleted archive is gone, so the run converges"

# Files. Every command here writes into the fixture tree, so the whole lifecycle
# happens under one temp folder that is deleted at the end — the same
# create-then-clean-up shape the backups block uses, so a re-run converges.
SMOKE_DIR="__smoke_files"
# Cleanup on failure, not just on success: the folder name is fixed, so a
# leftover would make the next run's folder create collide (409) instead of
# merely accumulating. Best-effort and silent — it must not mask the real
# failure or its exit code. Replaces the $WORK-only trap set above.
trap '"$CLI" files delete --path "books/$SMOKE_DIR" --confirm-name "$SMOKE_DIR" --delete-files >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT
"$CLI" files folder create --parent books --name "$SMOKE_DIR" >"$WORK/fcreate.out" 2>"$WORK/fcreate.err" \
  || { cat "$WORK/fcreate.err" >&2; fail "files folder create exited non-zero"; }
jq -e --arg p "books/$SMOKE_DIR" '.path == $p' "$WORK/fcreate.out" >/dev/null \
  || fail "files folder create should echo the new path: $(cat "$WORK/fcreate.out")"
ok "files folder create made the temp folder"

"$CLI" files folder contents --path "books/$SMOKE_DIR" >"$WORK/fcontents.out" 2>"$WORK/fcontents.err" \
  || { cat "$WORK/fcontents.err" >&2; fail "files folder contents exited non-zero"; }
jq -e '.has_content == false' "$WORK/fcontents.out" >/dev/null \
  || fail "a new folder should report has_content false: $(cat "$WORK/fcontents.out")"
ok "files folder contents reports an empty folder"

"$CLI" files folder scaffold --path "books/$SMOKE_DIR" >"$WORK/fscaffold.out" 2>"$WORK/fscaffold.err" \
  || { cat "$WORK/fscaffold.err" >&2; fail "files folder scaffold exited non-zero"; }
jq -e '.created | length == 8' "$WORK/fscaffold.out" >/dev/null \
  || fail "scaffold should create the eight category folders: $(cat "$WORK/fscaffold.out")"
ok "files folder scaffold created the category folders"

"$CLI" files folder markers --path "books/$SMOKE_DIR" --nsfw true >"$WORK/fmarkers.out" 2>"$WORK/fmarkers.err" \
  || { cat "$WORK/fmarkers.err" >&2; fail "files folder markers exited non-zero"; }
jq -e '.nsfw == true' "$WORK/fmarkers.out" >/dev/null \
  || fail "markers should report the NSFW flag: $(cat "$WORK/fmarkers.out")"
ok "files folder markers set the NSFW flag"

# A tiny file of our own, so the upload never depends on a fixture book.
printf 'smoke' >"$WORK/smoke-upload.txt"
"$CLI" files upload --destination "books/$SMOKE_DIR" --file "$WORK/smoke-upload.txt" >"$WORK/fupload.out" 2>"$WORK/fupload.err" \
  || { cat "$WORK/fupload.err" >&2; fail "files upload exited non-zero"; }
jq -e '.name == "smoke-upload.txt" and .size == 5' "$WORK/fupload.out" >/dev/null \
  || fail "upload should report the name and size: $(cat "$WORK/fupload.out")"
ok "files upload landed one file"

"$CLI" files browse --path "books/$SMOKE_DIR" >"$WORK/fbrowse.out" 2>"$WORK/fbrowse.err" \
  || { cat "$WORK/fbrowse.err" >&2; fail "files browse exited non-zero"; }
jq -e 'any(.entries[]; .name == "smoke-upload.txt")' "$WORK/fbrowse.out" >/dev/null \
  || fail "browse should list the uploaded file: $(cat "$WORK/fbrowse.out")"
jq -e 'has("total") and has("truncated") and has("writable")' "$WORK/fbrowse.out" >/dev/null \
  || fail "browse should report total, truncated and writable"
# The point of the DB-aware listing: an uploaded, unscanned file carries no
# record_id, which is how "landed but not indexed" is visible at all.
jq -e '.entries[] | select(.name == "smoke-upload.txt") | .record_id == null' "$WORK/fbrowse.out" >/dev/null \
  || fail "an unindexed upload should carry no record_id: $(cat "$WORK/fbrowse.out")"
ok "files browse distinguishes the unindexed upload"

"$CLI" files rename --path "books/$SMOKE_DIR/smoke-upload.txt" --new-name "renamed.txt" >"$WORK/frename.out" 2>"$WORK/frename.err" \
  || { cat "$WORK/frename.err" >&2; fail "files rename exited non-zero"; }
jq -e --arg t "books/$SMOKE_DIR/renamed.txt" '.to == $t' "$WORK/frename.out" >/dev/null \
  || fail "rename should report where it landed: $(cat "$WORK/frename.out")"
ok "files rename moved the file to its new name"

"$CLI" files move --sources "books/$SMOKE_DIR/renamed.txt" --destination "books/$SMOKE_DIR/Core" >"$WORK/fmove.out" 2>"$WORK/fmove.err" \
  || { cat "$WORK/fmove.err" >&2; fail "files move exited non-zero"; }
jq -e '.count == 1' "$WORK/fmove.out" >/dev/null \
  || fail "move should report one moved entry: $(cat "$WORK/fmove.out")"
ok "files move relocated the file"

# Soft delete: the row goes, the file stays — files_deleted false is the proof.
"$CLI" files delete --path "books/$SMOKE_DIR/Core/renamed.txt" >"$WORK/fdelete.out" 2>"$WORK/fdelete.err" \
  || { cat "$WORK/fdelete.err" >&2; fail "files delete exited non-zero"; }
jq -e '.files_deleted == false' "$WORK/fdelete.out" >/dev/null \
  || fail "a delete without --delete-files should report files_deleted false: $(cat "$WORK/fdelete.out")"
ok "files delete defaulted to the soft form"

"$CLI" files delete --path "books/$SMOKE_DIR" --confirm-name "$SMOKE_DIR" --delete-files >"$WORK/ffdelete.out" 2>"$WORK/ffdelete.err" \
  || { cat "$WORK/ffdelete.err" >&2; fail "files delete --delete-files exited non-zero"; }
jq -e '.files_deleted == true' "$WORK/ffdelete.out" >/dev/null \
  || fail "a hard delete should report files_deleted true: $(cat "$WORK/ffdelete.out")"
ok "files delete --delete-files removed the folder and its files"

"$CLI" files browse --path books >"$WORK/fbrowse2.out" 2>/dev/null \
  || fail "files browse exited non-zero after cleanup"
jq -e --arg n "$SMOKE_DIR" 'any(.entries[]; .name == $n) | not' "$WORK/fbrowse2.out" >/dev/null \
  || fail "the temp folder should be gone: $(cat "$WORK/fbrowse2.out")"
ok "the temp folder is gone, so the run converges"

# 4b. The version check runs on a cadence, not only at login.
jq -e '.lastServerVersion == "'"$EXPECTED_VERSION"'"' "$CONFIG" >/dev/null \
  || fail "login should have recorded the server version: $(cat "$CONFIG")"
jq -e '.lastVersionCheck != null' "$CONFIG" >/dev/null \
  || fail "login should have recorded a check timestamp"
ok "login records the server version"

# Inside the window: no probe, and the timestamp is untouched. DebugHttpHandler
# logs every request it sends, so its absence for /api/about is what proves no
# probe happened — an unmoved timestamp alone would also be consistent with a
# probe that ran and failed to persist.
BEFORE=$(jq -r .lastVersionCheck "$CONFIG")
"$CLI" --debug systems list >/dev/null 2>"$WORK/inwindow.err" \
  || { cat "$WORK/inwindow.err" >&2; fail "systems list exited non-zero"; }
grep -qi "next due in" "$WORK/inwindow.err" \
  || fail "a check inside the window should say it is not due: $(cat "$WORK/inwindow.err")"
grep -q "GET .*api/about" "$WORK/inwindow.err" \
  && fail "a check inside the window should not have probed /api/about: $(cat "$WORK/inwindow.err")"
[ "$(jq -r .lastVersionCheck "$CONFIG")" = "$BEFORE" ] \
  || fail "a check inside the window must not move the timestamp"
ok "no probe inside the 24-hour window"

# Backdated: probes and advances.
jq '.lastVersionCheck = "2020-01-01T00:00:00+00:00"' "$CONFIG" > "$WORK/cfg" && mv "$WORK/cfg" "$CONFIG"
"$CLI" --debug systems list >/dev/null 2>"$WORK/stale.err" \
  || { cat "$WORK/stale.err" >&2; fail "systems list exited non-zero"; }
grep -q "GET .*api/about 200" "$WORK/stale.err" \
  || fail "a stale timestamp should have probed /api/about: $(cat "$WORK/stale.err")"
[ "$(jq -r .lastVersionCheck "$CONFIG")" != "2020-01-01T00:00:00+00:00" ] \
  || fail "a stale timestamp should have triggered a probe: $(cat "$WORK/stale.err")"
jq -e '.lastServerVersion == "'"$EXPECTED_VERSION"'"' "$CONFIG" >/dev/null \
  || fail "the probe should have recorded the version"
ok "a stale timestamp triggers a probe and advances"

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
# --server is the flag tier of ConfigManager.Resolve — the tested precedence
# logic (ConfigManagerTests.cs) is otherwise unreachable through the CLI. The
# stored server is deliberately made unreachable first so a config-file fallback
# can't mask a broken flag. The token has no flag tier and stays in the file.
cp "$CONFIG" "$WORK/config.saved"
jq '.server = "http://127.0.0.1:1"' "$WORK/config.saved" >"$CONFIG"

syslist --server "$SERVER"
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "systems list --server returned $COUNT over an unreachable stored server, expected $EXPECTED_SYSTEMS"
ok "systems list --server overrides the stored server"

# A rejected token surfaces as exit 2. The refresh token goes with it, so the
# renewal path cannot rescue the request and hide the 401.
jq '.accessToken = "bogus-token" | del(.refreshToken)' "$WORK/config.saved" >"$CONFIG"
set +e
"$CLI" systems list --server "$SERVER" >/dev/null 2>"$WORK/badtoken.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a bogus stored token should exit 2, got $rc"
grep -qi "not authenticated" "$WORK/badtoken.err" \
  || fail "a bogus stored token gave no 'Not authenticated' message: $(cat "$WORK/badtoken.err")"
ok "a bogus stored token against a correct --server exits 2"

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
# `systems get` prints its response with no DTO layer in front of it now, so
# this also exercises GrimoireApiClient.EnsureJson, the guard that keeps an
# HTML body like this one off stdout — assert that explicitly, not just the
# exit code, since a regression that printed the page would still exit 2.
# The catch-all only answers GET: confirmed by curl that every write verb
# (PATCH/POST/DELETE) against these same id shapes gets an ordinary JSON 405
# instead, so no write command can be substituted here to exercise it.
for bad_id in "" "." "../about"; do
  set +e
  "$CLI" systems get --id "$bad_id" >"$WORK/badid.out" 2>"$WORK/badid.err"; rc=$?
  set -e
  [ "$rc" -eq 2 ] || fail "id '$bad_id' should exit 2, got $rc: $(cat "$WORK/badid.err")"
  [ ! -s "$WORK/badid.out" ] \
    || fail "id '$bad_id' printed to stdout instead of failing: $(cat "$WORK/badid.out")"
  grep -qi "could not be parsed as JSON" "$WORK/badid.err" \
    || fail "id '$bad_id' gave no not-JSON message: $(cat "$WORK/badid.err")"
  grep -qi "at System\.\|StackTrace\|Unhandled exception" "$WORK/badid.err" \
    && fail "id '$bad_id' leaked a stack trace: $(cat "$WORK/badid.err")"
done
ok "systems get on an empty, '.', or '../about' id exits 2 with no stack trace and empty stdout"

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

# --- book folders ------------------------------------------------------------
# Fixed path and fixed tags, so a second run converges. The fixture's only book
# below a category directory lives here, which is what makes the inheritance
# assertion below possible at all.
syslist --include-children
DSA=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Das Schwarze Auge 5 DE") | .id')
[ -n "$DSA" ] || fail "no Das Schwarze Auge 5 DE fixture for book folders"
FOLDER_PATH="$DSA/core/errata"

SET_JSON=$(printf '{"path":"%s","tags":["errata-smoke"]}' "$FOLDER_PATH" \
  | "$CLI" systems book-folders set --id "$DSA" --stdin 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders set exited non-zero"; }
[ "$(echo "$SET_JSON" | jq -r .path)" = "$FOLDER_PATH" ] \
  || fail "set should echo the path it wrote: $SET_JSON"
ok "systems book-folders set writes a folder's tags"

FOLDERS_JSON=$("$CLI" systems book-folders list --id "$DSA" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero"; }
echo "$FOLDERS_JSON" | jq -e --arg p "$FOLDER_PATH" '.folders[] | select(.path == $p)' >/dev/null \
  || fail "the folder just written should be listed: $FOLDERS_JSON"
ok "systems book-folders list shows the written folder"

# The point of the feature: a book below the path inherits the tag. This is the
# round trip that upstream #357 broke — the server derived the folder's depth
# differently for a container child, and Das Schwarze Auge 5 DE is one.
TAG_ITEMS=$(curl -sf "$SERVER/api/tags/errata-smoke/items" \
  -H "Authorization: Bearer $(jq -r .accessToken "$CONFIG")") \
  || fail "could not read the tag's items"
echo "$TAG_ITEMS" | jq -e '.folders[] | select(.path == "errata") | .items[] | select(.title == "DSA5 Errata")' >/dev/null \
  || fail "the folder tag should reach the book below it: $TAG_ITEMS"
ok "a folder tag reaches the book below its path"

DEL_JSON=$("$CLI" systems book-folders delete --id "$DSA" --path "$FOLDER_PATH" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders delete exited non-zero"; }
[ "$(echo "$DEL_JSON" | jq -r .status)" = "deleted" ] \
  || fail "delete should report the deletion: $DEL_JSON"
ok "systems book-folders delete removes the folder"

FOLDERS_JSON=$("$CLI" systems book-folders list --id "$DSA" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero after delete"; }
[ "$(echo "$FOLDERS_JSON" | jq '.folders | length')" -eq 0 ] \
  || fail "the folder should be gone after delete: $FOLDERS_JSON"
ok "the deleted folder is no longer listed"

# --- system covers --------------------------------------------------------
# A different system from Shadowrun 4 DE on purpose: that one already carries
# the description write above and the metadata diff assertions below, and a
# cover write would couple a third assertion to the same fixture.
syslist --include-children
COVER_SYS=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Fixture Explicit RPG") | .id')
[ -n "$COVER_SYS" ] || fail "no Fixture Explicit RPG to attach a cover to"

# 404 first: this system has neither folder art nor an upload.
set +e
"$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/none.png" >/dev/null 2>"$WORK/cover404.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "cover get on a system with no cover should exit 2, got $rc: $(cat "$WORK/cover404.err")"
grep -qi "not found" "$WORK/cover404.err" \
  || fail "no not-found hint: $(cat "$WORK/cover404.err")"
ok "systems cover get 404s when the system has no cover"

UPLOAD_JSON=$("$CLI" systems cover upload --id "$COVER_SYS" --file docker/fixture-cover.png 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover upload exited non-zero"; }
echo "$UPLOAD_JSON" | jq -e '.cover_image | endswith(".png")' >/dev/null \
  || fail "upload should report a .png cover_image: $UPLOAD_JSON"
ok "systems cover upload stores a png"

GET_JSON=$("$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/cover.png" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover get exited non-zero"; }
[ "$(echo "$GET_JSON" | jq -r .bytes)" -eq "$(wc -c < "$WORK/cover.png")" ] \
  || fail "the receipt's byte count should match the file: $GET_JSON"
ok "systems cover get writes the file and reports its size"

"$CLI" systems cover get --id "$COVER_SYS" --output - > "$WORK/cover-dash.png" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems cover get --output - exited non-zero"; }
cmp -s "$WORK/cover.png" "$WORK/cover-dash.png" \
  || fail "--output - and --output <file> should produce identical bytes"
ok "systems cover get --output - streams the same bytes to stdout"

DEL_JSON=$("$CLI" systems cover delete --id "$COVER_SYS" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover delete exited non-zero"; }
[ "$(echo "$DEL_JSON" | jq -r .status)" = "ok" ] || fail "delete should answer ok: $DEL_JSON"
set +e
"$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/gone.png" >/dev/null 2>"$WORK/cover-gone.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "cover get after delete should exit 2 again, got $rc: $(cat "$WORK/cover-gone.err")"
grep -qi "not found" "$WORK/cover-gone.err" \
  || fail "no not-found hint: $(cat "$WORK/cover-gone.err")"
ok "systems cover delete removes the upload"

# --- books --------------------------------------------------------------------
# Requires docker/seed.sh to have run. EXPECTED_BOOKS mirrors the fixture count
# there; changing a fixture must change this number. Shadowrun 4 DE additionally
# carries 3 books across 2 categories, specifically so a --limit below that
# system's own total proves paging rather than a coincidence of the global
# count already exceeding it.
EXPECTED_BOOKS=18

booklist() {
  LIST_JSON=$("$CLI" books list "$@" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "books list $* exited non-zero"; }
}

bookget() {
  GET_JSON=$("$CLI" books get "$@" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "books get $* exited non-zero"; }
}

booklist
[ "$(echo "$LIST_JSON" | jq -r .total)" -eq "$EXPECTED_BOOKS" ] \
  || fail "expected $EXPECTED_BOOKS books, got $(echo "$LIST_JSON" | jq -r .total)"
[ "$(echo "$LIST_JSON" | jq '.books | length')" -eq "$EXPECTED_BOOKS" ] \
  || fail "the default limit should return every book, got $(echo "$LIST_JSON" | jq '.books | length')"
ok "books list returns a total and a books array"

booklist --system-id "$SR4" --limit 2
[ "$(echo "$LIST_JSON" | jq -r .total)" -eq 3 ] \
  || fail "Shadowrun 4 DE should report 3 books regardless of --limit, got $(echo "$LIST_JSON" | jq -r .total)"
[ "$(echo "$LIST_JSON" | jq '.books | length')" -eq 2 ] \
  || fail "--limit 2 should return 2 books, got $(echo "$LIST_JSON" | jq '.books | length')"
FIRST_PAGE_ID=$(echo "$LIST_JSON" | jq -r '.books[0].id')
ok "--limit narrows the page while total stays the system's full count"

booklist --system-id "$SR4" --offset 2 --limit 2
[ "$(echo "$LIST_JSON" | jq '.books | length')" -eq 1 ] \
  || fail "offset 2 of 3 should leave exactly 1 book, got $(echo "$LIST_JSON" | jq '.books | length')"
[ "$(echo "$LIST_JSON" | jq -r '.books[0].id')" != "$FIRST_PAGE_ID" ] \
  || fail "--offset 2 should have skipped past the first page's lead book"
ok "--offset advances to a different first id"

booklist --category core
[ "$(echo "$LIST_JSON" | jq '.books | length')" -gt 0 ] || fail "--category core matched nothing"
echo "$LIST_JSON" | jq -e '.books | all(.category == "core")' >/dev/null \
  || fail "--category core returned a non-core book: $(echo "$LIST_JSON" | jq -c '.books | map(.category)')"
booklist --category Core
[ "$(echo "$LIST_JSON" | jq -r .total)" -eq 0 ] \
  || fail "'Core' should match nothing, got $(echo "$LIST_JSON" | jq -r .total)"
ok "--category is case-sensitive"

booklist --system-id "$SR4" --category core --limit 1
SR4_BOOK=$(echo "$LIST_JSON" | jq -r '.books[0].id')
[ -n "$SR4_BOOK" ] && [ "$SR4_BOOK" != null ] || fail "no core book under Shadowrun 4 DE"

bookget --id "$SR4_BOOK"
[ "$(echo "$GET_JSON" | jq -r '.game_system.id')" = "$SR4" ] \
  || fail "books get should populate game_system: $(echo "$GET_JSON" | jq -c .game_system)"
ok "books get returns the detail shape with game_system populated"

# Whether a fixture PDF gets a scan-generated thumbnail is the server's call,
# not the CLI's — assert on has_thumbnail first and only download when true.
if [ "$(echo "$GET_JSON" | jq -r .has_thumbnail)" = "true" ]; then
  THUMB_JSON=$("$CLI" books thumbnail --id "$SR4_BOOK" --output "$WORK/thumb.webp" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "books thumbnail exited non-zero"; }
  [ "$(echo "$THUMB_JSON" | jq -r .bytes)" -gt 0 ] || fail "thumbnail should have bytes: $THUMB_JSON"
  ok "books thumbnail downloads the scan-generated image"
else
  ok "books thumbnail skipped — the server generated no thumbnail for this fixture"
fi

# The first book write. Shadowrun 4 DE is seeded raw for exactly this, same as
# the systems section above. description is used for the same reason: nothing
# above filters on it, so re-running the suite converges instead of drifting.
echo '{"description":"smoke fixture book description"}' \
  | "$CLI" books update --id "$SR4_BOOK" --stdin >"$WORK/bupd.out" 2>"$WORK/bupd.err" \
  || { cat "$WORK/bupd.err" >&2; fail "books update exited non-zero"; }
jq -e '.status == "ok"' "$WORK/bupd.out" >/dev/null \
  || fail "update should answer {\"status\":\"ok\"}: $(cat "$WORK/bupd.out")"
bookget --id "$SR4_BOOK"
[ "$(echo "$GET_JSON" | jq -r .description)" = "smoke fixture book description" ] \
  || fail "the written description did not read back: $(echo "$GET_JSON" | jq -r .description)"
ok "books update writes a field and books get reads it back"

# batch-tag is additive: the second call must not displace the first tag.
echo "{\"ids\":[\"$SR4_BOOK\"],\"tags\":[\"smoke-book-alpha\"]}" \
  | "$CLI" books batch-tag --stdin >"$WORK/btag1.out" 2>"$WORK/btag1.err" \
  || { cat "$WORK/btag1.err" >&2; fail "batch-tag exited non-zero"; }
echo "{\"ids\":[\"$SR4_BOOK\"],\"tags\":[\"smoke-book-beta\"]}" \
  | "$CLI" books batch-tag --stdin >"$WORK/btag2.out" 2>"$WORK/btag2.err" \
  || { cat "$WORK/btag2.err" >&2; fail "the second batch-tag exited non-zero"; }
jq -e --arg id "$SR4_BOOK" '.tags[$id] | index("smoke-book-alpha") != null and index("smoke-book-beta") != null' \
  "$WORK/btag2.out" >/dev/null \
  || fail "batch-tag should have merged both tags: $(cat "$WORK/btag2.out")"
ok "batch-tag adds a tag and leaves the existing one in place"

# batch-update: one good id and one bogus id must exit 3, applying the good
# one. license, not description: nothing above filters on a book's license,
# so this stays idempotent across re-runs.
cat >"$WORK/bbatch.json" <<JSON
{"items":[{"id":"$SR4_BOOK","license":"Smoke Fixture Book License"},
          {"id":"no-such-id","license":"x"}]}
JSON
set +e
"$CLI" books batch-update --input "$WORK/bbatch.json" >"$WORK/bbatch.out" 2>"$WORK/bbatch.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "a partial batch should exit 3, got $rc: $(cat "$WORK/bbatch.err")"
jq -e --arg id "$SR4_BOOK" '.updated | index($id) != null' "$WORK/bbatch.out" >/dev/null \
  || fail "the good id should be in updated: $(cat "$WORK/bbatch.out")"
jq -e '.errors | length == 1 and .[0].id == "no-such-id"' "$WORK/bbatch.out" >/dev/null \
  || fail "the bogus id should be the only error: $(cat "$WORK/bbatch.out")"
ok "batch-update applies the good id and exits 3 on a partial, naming the bad id"

# reindex is OCR-only, and the fixtures are real PDFs with a real text layer
# (make-fixtures.py inserts real text), so the server always rejects with a
# 400 — that rejection, not a successful re-index, is the assertable behaviour.
set +e
"$CLI" books reindex --id "$SR4_BOOK" >/dev/null 2>"$WORK/reindex.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "reindex on a text-layer fixture should have failed"
grep -qi "bad request" "$WORK/reindex.err" \
  || fail "reindex should have reported a 400: $(cat "$WORK/reindex.err")"
ok "reindex rejects a fixture book with a 400"

"$CLI" books rescan --id "$SR4_BOOK" >"$WORK/brescan.out" 2>"$WORK/brescan.err" \
  || { cat "$WORK/brescan.err" >&2; fail "books rescan exited non-zero"; }
jq -e '.status == "rescan_queued"' "$WORK/brescan.out" >/dev/null \
  || fail "books rescan should answer rescan_queued: $(cat "$WORK/brescan.out")"
ok "books rescan queues a re-read"

# A single-book rescan sets the same running flag a full library scan uses
# (rescan_single_book in temp/grimoire's backend/routers/library/_helpers.py),
# so the library rescan below must wait for it to clear — otherwise it would
# see running=true and answer already_running instead of scan_started.
for i in $(seq 1 30); do
  RUNNING=$("$CLI" library scan-status 2>"$WORK/cli.err" | jq -r .running) \
    || { cat "$WORK/cli.err" >&2; fail "scan-status exited non-zero"; }
  [ "$RUNNING" = "false" ] && break
  [ "$i" -eq 30 ] && fail "the book rescan never finished"
  sleep 1
done

# --- library scan --------------------------------------------------------------
# The real path: the container nests Shadowrun 4 DE at books/Shadowrun/4 DE, not
# books/Shadowrun 4 DE. That distinction matters here — resolve_scope validates
# only that a scope begins with a known collection and does not escape the
# library root, never that the target exists (docs/grimoire-api-notes.md), so
# a scope matching nothing would still answer scan_started and this assertion
# would pass against any syntactically valid books/-prefixed garbage. Only the
# real path drives an actual walk, which is what the settled counters below
# prove. metadata_mode defaults to "new", which only fills in brand-new book
# records and leaves already-indexed metadata alone, so this does not disturb
# the description/license/tags this suite wrote to Shadowrun 4 DE above.
"$CLI" library rescan --scope "books/Shadowrun/4 DE" \
  >"$WORK/librescan.out" 2>"$WORK/librescan.err" \
  || { cat "$WORK/librescan.err" >&2; fail "library rescan exited non-zero"; }
jq -e '.status == "scan_started"' "$WORK/librescan.out" >/dev/null \
  || fail "library rescan should answer scan_started: $(cat "$WORK/librescan.out")"
ok "library rescan starts a scoped scan"

# Wait for the scoped scan to settle before reading its counters, or a
# still-in-flight snapshot would read as a smaller, unstable number.
for i in $(seq 1 30); do
  STATUS_JSON=$("$CLI" library scan-status 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "library scan-status exited non-zero"; }
  [ "$(echo "$STATUS_JSON" | jq -r .running)" = "false" ] && break
  [ "$i" -eq 30 ] && fail "the scoped rescan never finished"
  sleep 1
done
echo "$STATUS_JSON" | jq -e '.running | type == "boolean"' >/dev/null \
  || fail "scan-status should carry a boolean running field: $STATUS_JSON"

# total_books/scanned_books settling at 3 — Shadowrun 4 DE's own book count,
# asserted earlier via books list --system-id — is something only a walk of
# the real subtree produces; a scope matching nothing would leave both at 0.
[ "$(echo "$STATUS_JSON" | jq -r .total_books)" -eq 3 ] \
  || fail "a scoped rescan of Shadowrun 4 DE should settle on 3 books, got $(echo "$STATUS_JSON" | jq -r .total_books)"
[ "$(echo "$STATUS_JSON" | jq -r .scanned_books)" -eq 3 ] \
  || fail "a scoped rescan of Shadowrun 4 DE should scan all 3 books, got $(echo "$STATUS_JSON" | jq -r .scanned_books)"
ok "library scan-status shows the scoped rescan actually walked its subtree"

CANCEL_JSON=$("$CLI" library cancel-scan 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cancel-scan exited non-zero"; }
echo "$CANCEL_JSON" | jq -e '.status == "not_running"' >/dev/null \
  || fail "cancel-scan after the wait loop above should report not_running: $CANCEL_JSON"
ok "library cancel-scan exits 0 and reports not_running"

# --- cleanup-missing ------------------------------------------------------
# Placed after the scan section so nothing is running — the endpoint answers 409
# while a scan is. It is safe beside the resource counts either side of it
# because EXPECTED_BOOKS above already proves nothing is missing: a stack
# carrying stale is_missing rows fails there and never reaches this block.
#
# The assertion is the contract, not the first call's numbers: whatever the
# first call removes, the second must find nothing left. That survives a change
# to the fixture counts, where asserting zero on the first call would encode
# this stack's history instead of the endpoint's behaviour.
CLEANUP_JSON=$("$CLI" library cleanup-missing 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cleanup-missing exited non-zero"; }
for key in books maps tokens audio systems; do
  echo "$CLEANUP_JSON" | jq -e --arg k "$key" '.removed[$k] | type == "number"' >/dev/null \
    || fail "removed.$key should be a number: $CLEANUP_JSON"
done
ok "library cleanup-missing reports a count for every resource"

CLEANUP_JSON=$("$CLI" library cleanup-missing 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cleanup-missing exited non-zero on the second call"; }
echo "$CLEANUP_JSON" | jq -e '[.removed[]] | add == 0' >/dev/null \
  || fail "a second cleanup should find nothing left to remove: $CLEANUP_JSON"
ok "a second library cleanup-missing removes nothing"

# --- addons ---------------------------------------------------------------
# Installs from a local fixture index rather than the published community one:
# pointing the smoke test at the real index would make every PR build depend
# on raw.githubusercontent.com and a third-party host, and would install
# third-party content on every run. The fixture (docker/addon-index/) and its
# index.json (generated by docker/seed.sh -> make-addon-index.py) are served
# by the addon-index nginx service in docker-compose.yml. That URL is fetched
# by the grimoire container, not this script, so it is the compose service
# name http://addon-index/index.json — unreachable from the devcontainer, and
# not meant to be; addons refresh returning a count is what proves the
# grimoire container reached it. Never point the stack at the community index
# and never call addons refresh while it is pointed there — this section
# switches to the fixture before its first refresh and restores the default
# before it ends.
FIXTURE_INDEX="http://addon-index/index.json"

SETTINGS_JSON=$("$CLI" addons settings --index-url "$FIXTURE_INDEX" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons settings --index-url exited non-zero"; }
[ "$(echo "$SETTINGS_JSON" | jq -r .index_url)" = "$FIXTURE_INDEX" ] \
  || fail "addons settings did not echo back the fixture index URL: $SETTINGS_JSON"
ok "addons settings points the stack at the fixture index"

REFRESH_JSON=$("$CLI" addons refresh 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons refresh exited non-zero"; }
[ "$(echo "$REFRESH_JSON" | jq -r .count)" -eq 1 ] \
  || fail "addons refresh should report count 1, got: $REFRESH_JSON"
ok "addons refresh reaches the fixture index and reports 1 add-on"

INSTALL_JSON=$("$CLI" addons install --id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons install exited non-zero (digest mismatch? re-run docker/seed.sh after editing docker/addon-index/fixture-source.yml — it regenerates index.json via make-addon-index.py)"; }
[ "$(echo "$INSTALL_JSON" | jq -r .id)" = "fixture-source" ] \
  || fail "addons install returned the wrong id: $INSTALL_JSON"
[ "$(echo "$INSTALL_JSON" | jq -r .enabled)" = "true" ] \
  || fail "a freshly installed add-on should be enabled: $INSTALL_JSON"
[ "$(echo "$INSTALL_JSON" | jq -r .runnable)" = "true" ] \
  || fail "a freshly installed, script-free add-on should be runnable: $INSTALL_JSON"
ok "addons install installs the fixture add-on, enabled and runnable"

ADDONLIST_JSON=$("$CLI" addons list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons list exited non-zero"; }
echo "$ADDONLIST_JSON" | jq -e '.installed[] | select(.id == "fixture-source" and .enabled == true)' >/dev/null \
  || fail "fixture-source should be installed and enabled: $(echo "$ADDONLIST_JSON" | jq -c .installed)"
echo "$ADDONLIST_JSON" | jq -e '.available[] | select(.id == "fixture-source" and .installed == true)' >/dev/null \
  || fail "fixture-source should show installed under available: $(echo "$ADDONLIST_JSON" | jq -c .available)"
DEFAULT_INDEX_URL=$(echo "$ADDONLIST_JSON" | jq -r .default_index_url)
ok "addons list shows the fixture under both installed and available"

# --- metadata lookup ------------------------------------------------------
# Runs here, between install and the disable below: a disabled add-on is not
# runnable and drops out of metadata-sources. It also depends on the systems
# section above having written description — that write is what makes the
# description row "same" rather than "differs".
SOURCES_JSON=$("$CLI" systems metadata-sources --id "$SR4" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-sources exited non-zero"; }
echo "$SOURCES_JSON" | jq -e '.sources[] | select(.id == "fixture-source" and .supports_paste == true)' >/dev/null \
  || fail "fixture-source should offer itself with supports_paste true: $SOURCES_JSON"
ok "systems metadata-sources lists the installed fixture add-on"

# The fixture targets game-system, so an empty list here is target filtering
# working rather than the endpoint returning nothing.
BOOKSOURCES_JSON=$("$CLI" books metadata-sources --id "$SR4_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books metadata-sources exited non-zero"; }
[ "$(echo "$BOOKSOURCES_JSON" | jq '.sources | length')" -eq 0 ] \
  || fail "a game-system add-on must not appear as a book source: $BOOKSOURCES_JSON"
ok "books metadata-sources excludes a game-system add-on"

SEARCH_JSON=$("$CLI" systems metadata-search --id "$SR4" --source-id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-search exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq -r .query)" = "Shadowrun 4 DE" ] \
  || fail "an omitted --query should echo back the system's name: $SEARCH_JSON"
[ "$(echo "$SEARCH_JSON" | jq -r '.results[0].identity')" = "shadowrun-4-de" ] \
  || fail "the fixture record should rank first: $SEARCH_JSON"
ok "systems metadata-search defaults its query to the system name"

FETCH_JSON=$("$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --identity shadowrun-4-de 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-fetch exited non-zero"; }
[ "$(echo "$FETCH_JSON" | jq -r '.fields[] | select(.field == "system_family") | .status')" = "only_incoming" ] \
  || fail "system_family is empty on this fixture, so it must read only_incoming: $FETCH_JSON"
# same, not differs, only because the systems section wrote this description
# earlier in this run. A differs here means that write moved or stopped.
[ "$(echo "$FETCH_JSON" | jq -r '.fields[] | select(.field == "description") | .status')" = "same" ] \
  || fail "description should match what the systems section wrote; did that write move? $FETCH_JSON"
# parent_system is folder-derived, so it is populated and disagrees with the
# catalogue's value. only_incoming here means the fixture tree changed shape.
[ "$(echo "$FETCH_JSON" | jq -r '.fields[] | select(.field == "parent_system") | .status')" = "differs" ] \
  || fail "parent_system is folder-derived and should disagree with the fixture: $FETCH_JSON"
ok "systems metadata-fetch reports one row of each status"

PASTE_JSON=$("$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --paste "https://fixture.test/systems/shadowrun-4-de" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-fetch --paste exited non-zero"; }
[ "$(echo "$PASTE_JSON" | jq -r .identity)" = "shadowrun-4-de" ] \
  || fail "--paste should resolve to the same identity the search returned: $PASTE_JSON"
ok "systems metadata-fetch --paste resolves a source URL to an identity"

# Fetching is a read. The field it offered must still be empty afterwards —
# and the family filter assertion earlier depends on it.
sysget --id "$SR4"
[ -z "$(echo "$GET_JSON" | jq -r '.system_family // ""')" ] \
  || fail "metadata-fetch must not have written system_family: $(echo "$GET_JSON" | jq -r .system_family)"
ok "metadata-fetch left the system unchanged"

set +e
"$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source >/dev/null 2>"$WORK/fetchargs.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "metadata-fetch with neither --identity nor --paste should exit 1, got $rc"
grep -q -- "--identity" "$WORK/fetchargs.err" \
  || fail "no mention of --identity: $(cat "$WORK/fetchargs.err")"
set +e
"$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --identity shadowrun-4-de --paste "https://fixture.test/systems/shadowrun-4-de" \
  >/dev/null 2>"$WORK/fetchboth.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "metadata-fetch with both --identity and --paste should exit 1, got $rc"
grep -q -- "--identity" "$WORK/fetchboth.err" \
  || fail "no mention of --identity: $(cat "$WORK/fetchboth.err")"
ok "metadata-fetch requires exactly one of --identity and --paste"

UPDATE_JSON=$("$CLI" addons update --id fixture-source --enabled false 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons update exited non-zero"; }
[ "$(echo "$UPDATE_JSON" | jq -r .enabled)" = "false" ] \
  || fail "addons update --enabled false did not disable the add-on: $UPDATE_JSON"
ok "addons update disables the fixture add-on"

# With a single fixture at a single version there is nothing to upgrade, so
# this only exercises the plumbing (a refresh, then an empty pass) — it is NOT
# coverage of upgrade-all's skip-and-continue behaviour, which needs an add-on
# that actually fails to upgrade. Asserted honestly as the empty case.
UPGRADE_JSON=$("$CLI" addons upgrade-all 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons upgrade-all exited non-zero"; }
[ "$(echo "$UPGRADE_JSON" | jq '.updated | length')" -eq 0 ] \
  || fail "nothing should have needed upgrading: $UPGRADE_JSON"
[ "$(echo "$UPGRADE_JSON" | jq '.failed | length')" -eq 0 ] \
  || fail "nothing should have failed to upgrade: $UPGRADE_JSON"
ok "addons upgrade-all exits 0 with nothing to upgrade"

UNINSTALL_JSON=$("$CLI" addons uninstall --id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons uninstall exited non-zero"; }
[ "$(echo "$UNINSTALL_JSON" | jq -r .status)" = "ok" ] \
  || fail "addons uninstall should answer {\"status\":\"ok\"}: $UNINSTALL_JSON"
ADDONLIST_JSON=$("$CLI" addons list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons list exited non-zero"; }
echo "$ADDONLIST_JSON" | jq -e '.installed[] | select(.id == "fixture-source")' >/dev/null \
  && fail "fixture-source should no longer be installed: $(echo "$ADDONLIST_JSON" | jq -c .installed)"
ok "addons uninstall removes the fixture add-on"

# Restore the published index so the run leaves no state behind, taken from
# default_index_url above rather than hard-coded.
RESTORE_JSON=$("$CLI" addons settings --index-url "$DEFAULT_INDEX_URL" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons settings restore exited non-zero"; }
[ "$(echo "$RESTORE_JSON" | jq -r .index_url)" = "$DEFAULT_INDEX_URL" ] \
  || fail "addons settings did not restore the default index URL: $RESTORE_JSON"
ok "addons settings restores the default index URL"

set +e
"$CLI" addons settings >/dev/null 2>"$WORK/addonsettings.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "addons settings with no flags should exit 1, got $rc"
grep -q -- "--index-url" "$WORK/addonsettings.err" \
  || fail "no mention of --index-url: $(cat "$WORK/addonsettings.err")"
grep -q -- "--allow-scripts" "$WORK/addonsettings.err" \
  || fail "no mention of --allow-scripts: $(cat "$WORK/addonsettings.err")"
ok "addons settings with no flags exits 1 and names the required flags"

# The token comes from the config file alone, so there is no --token to override it.
set +e
"$CLI" systems list --token whatever >"$WORK/notoken.out" 2>"$WORK/notoken.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "--token should be rejected as an unknown option, got $rc"
ok "--token is not an option"

# A config file that is not valid JSON must not take the CLI down with it, and
# logging in again must be enough to recover — no hand-editing, no rm. This runs
# last because it ends by restoring the config the earlier checks depend on.
printf '{not json' > "$CONFIG"
set +e
"$CLI" systems list >"$WORK/corrupt.out" 2>"$WORK/corrupt.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "a corrupt config should not report success"
grep -q "not valid JSON" "$WORK/corrupt.err" \
  || fail "no readable message for a corrupt config: $(cat "$WORK/corrupt.err")"
grep -qi "at System\.\|Unhandled exception" "$WORK/corrupt.err" \
  && fail "a corrupt config leaked a stack trace: $(cat "$WORK/corrupt.err")"
[ ! -s "$WORK/corrupt.out" ] || fail "stdout should stay empty on a config failure"
# The unparseable file is moved aside rather than left to be overwritten: it may
# hold the refresh token that a one-character fix would recover.
[ -f "$CONFIG.corrupt" ] || fail "the corrupt config should have been kept aside"
[ "$(cat "$CONFIG.corrupt")" = '{not json' ] \
  || fail "the kept-aside file should hold the original bytes"
grep -q "$CONFIG.corrupt" "$WORK/corrupt.err" \
  || fail "the warning should name where the file went: $(cat "$WORK/corrupt.err")"
rm -f "$CONFIG.corrupt"
ok "a corrupt config is kept aside and fails readably with no stack trace"

printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
  >/dev/null 2>"$WORK/relogin.err" \
  || { cat "$WORK/relogin.err" >&2; fail "login should recover from a corrupt config"; }
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "login did not repair the config: $(cat "$CONFIG")"
syslist
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "the CLI should work again after re-login"
ok "login repairs a corrupt config"

# The config is replaced, not rewritten in place: no temporary survives, and the
# file holding the session's tokens is readable only by its owner.
[ -z "$(find "$(dirname "$CONFIG")" -name '*.tmp' -print -quit)" ] \
  || fail "a temporary config file was left behind: $(ls "$(dirname "$CONFIG")")"
[ "$(stat -c '%a' "$CONFIG")" = "600" ] \
  || fail "the config should be owner-only, got $(stat -c '%a' "$CONFIG")"
ok "config writes leave no temporary file and stay owner-only"


# Grimoire does not merely refuse a refresh token it has already rotated away:
# it reads the replay as theft and revokes the session. Reaching that state on
# purpose is the only way to check the failure path without waiting out a
# 30-minute access token.
DEV_SECRET=$(docker inspect docker-grimoire-1 \
  --format '{{range .Config.Env}}{{println .}}{{end}}' 2>/dev/null \
  | sed -n 's/^SECRET_KEY=//p')
if [ "$DEV_SECRET" != "dev-only-not-a-real-secret" ]; then
  echo "  skip: the retired-session case needs the dev SECRET_KEY" >&2
else
  STORED=$(jq -r '.refreshToken // empty' "$CONFIG")
  [ -n "$STORED" ] || fail "login stored no refresh token: $(cat "$CONFIG")"
  curl -sf -X POST "$SERVER/api/auth/refresh" \
    -H "Cookie: grimoire_refresh=$STORED" -o /dev/null \
    || fail "could not rotate the refresh token out from under the CLI"
  # An expired but correctly signed token: TokenHelper reads its exp, finds it
  # spent, and the CLI refreshes before sending — with the cookie just retired.
  STALE_JWT=$(python3 -c "
import base64,hmac,hashlib,json,time
def b64(b): return base64.urlsafe_b64encode(b).rstrip(b'=')
h=b64(json.dumps({'alg':'HS256','typ':'JWT'},separators=(',',':')).encode())
n=int(time.time())
p=b64(json.dumps({'sub':'x','username':'admin','role':'admin','iat':n-3600,'jti':'p','exp':n-60,'sid':'p'},separators=(',',':')).encode())
print((h+b'.'+p+b'.'+b64(hmac.new(b'$DEV_SECRET',h+b'.'+p,hashlib.sha256).digest())).decode())")
  jq --arg t "$STALE_JWT" '.accessToken = $t' "$CONFIG" >"$WORK/retired.json"
  mv "$WORK/retired.json" "$CONFIG"
  rc=0
  "$CLI" systems list >"$WORK/retired.out" 2>"$WORK/retired.err" || rc=$?
  [ "$rc" -eq 2 ] || fail "a retired refresh token should exit 2, got $rc"
  grep -qi "session expired" "$WORK/retired.err" \
    || fail "no readable message for a retired session: $(cat "$WORK/retired.err")"
  grep -q "at GrimoireCli" "$WORK/retired.err" \
    && fail "a retired session leaked a stack trace: $(cat "$WORK/retired.err")"
  [ ! -s "$WORK/retired.out" ] || fail "stdout should stay empty when the session is gone"
  ok "a retired refresh token fails readably with no stack trace"

  # Restore a working session: this script must converge on a re-run, not drift.
  printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
    >/dev/null 2>"$WORK/relogin2.err" \
    || { cat "$WORK/relogin2.err" >&2; fail "login should recover a revoked session"; }
  syslist
  [ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "the CLI should work again after re-login"
  ok "login recovers a revoked session"
fi

echo "smoke: all checks passed" >&2
