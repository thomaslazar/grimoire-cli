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
# WARNING: it writes $HOME/.grimoire-cli/config.json, and refuses to run
# unless that home-tier file is in fact what resolves. Harmless in the
# devcontainer (container HOME isn't the host's), but running this on a host
# machine overwrites that host's saved grimoire-cli credentials.
set -euo pipefail

# GRIMOIRE_SERVER stays unexported: `systems list` must resolve the server from
# the config file `login` wrote, so a login that persisted nothing still fails.
# GRIMOIRE_CONFIG is unset outright: left exported, login would write there
# instead of the home file this script assumes and asserts against below.
unset GRIMOIRE_CONFIG
SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
CLI="${CLI:-src/GrimoireCli/bin/Debug/net10.0/grimoire-cli}"
CONFIG="$HOME/.grimoire-cli/config.json"

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }
ok() { echo "  ok: $*" >&2; }

echo "smoke: $CLI against $SERVER" >&2
# checking the built cli binary exists and is executable
[ -x "$CLI" ] || fail "no executable CLI at $CLI"

# 1. The instance answers at all.
# waiting for the server to answer /api/health, retrying for up to 60s
for i in $(seq 1 60); do
  curl -sf "$SERVER/api/health" >/dev/null 2>&1 && break
  [ "$i" -eq 60 ] && fail "no response from $SERVER/api/health after 60s"
  sleep 1
done
ok "health"

# The version the stack is actually running, read from the stack rather than
# hardcoded, so a Grimoire bump doesn't also require editing this number.
# reading the server's actual version from openapi.json for later checks
EXPECTED_VERSION=$(curl -sf "$SERVER/api/openapi.json" | jq -r .info.version)

# Refuse to run unless the home tier is what actually resolves: a developer's
# own GRIMOIRE_CONFIG (unset above, but a sibling grimoire-cli.json cannot be
# unset away) would otherwise have the login below overwrite that other file,
# plausibly a real account, before anything here notices.
# running config get expecting configSource and configPath to be the home tier
CONFIG_LOCATION=$("$CLI" config get)
echo "$CONFIG_LOCATION" | jq -e '.configSource == "home"' >/dev/null \
  || fail "refusing to run: configSource is $(echo "$CONFIG_LOCATION" | jq -r .configSource), not home"
echo "$CONFIG_LOCATION" | jq -e --arg config "$CONFIG" '.configPath == $config' >/dev/null \
  || fail "refusing to run: configPath is $(echo "$CONFIG_LOCATION" | jq -r .configPath), not $CONFIG"
ok "config resolves to the home tier"

# Clear any stale config first: without this, a regressed ConfigManager.Save
# that silently writes nothing would still leave a *previous* run's config
# behind, and checks 3/4 below would pass against stale data instead of
# catching the regression.
# deleting any stale config file before logging in
rm -f "$CONFIG"

# 2. Login. Retried: the healthcheck can go green before user seeding commits,
# so a first-attempt 401 is a race, not a failure.
# logging in as admin via stdin password, retried until the seeded user is ready
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
# checking login wrote a config file with the right server and a token
[ -f "$CONFIG" ] || fail "no config written at $CONFIG"
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "config server is not $SERVER: $(cat "$CONFIG")"
jq -e '.accessToken | type == "string" and length > 0' "$CONFIG" >/dev/null \
  || fail "config holds no access token: $(cat "$CONFIG")"
ok "config has server and token"

# GRIMOIRE_CONFIG chooses the file outright, so config get must report that
# path and not the home default — the answer to "why is this the wrong account".
# running config get under GRIMOIRE_CONFIG expecting that path and source env
GRIMOIRE_CONFIG="$WORK/alt-config.json" "$CLI" config get >"$WORK/config-alt.out" 2>&1 \
  || fail "config get under GRIMOIRE_CONFIG exited non-zero"
jq -e --arg p "$WORK/alt-config.json" '.configPath == $p and .configSource == "env"' \
  "$WORK/config-alt.out" >/dev/null \
  || fail "config get should report the GRIMOIRE_CONFIG file: $(cat "$WORK/config-alt.out")"
ok "GRIMOIRE_CONFIG chooses the config file"

# 4. The token authenticates, and stdout is JSON with logs kept on stderr.
# list.err is captured for diagnostics only (dumped on failure below) — nothing
# is asserted about its contents. At the default log level (LogSetup.cs sets
# minimum Warn) a clean run emits nothing to stderr anyway.
# running systems list expecting valid json on stdout
"$CLI" systems list >"$WORK/list.out" 2>"$WORK/list.err" \
  || { cat "$WORK/list.err" >&2; fail "systems list exited non-zero"; }
jq -e . "$WORK/list.out" >/dev/null \
  || fail "systems list stdout was not valid JSON: $(cat "$WORK/list.out")"
ok "systems list returned JSON on stdout"

# The five controlled-vocabulary reads. Read-only, so this block is idempotent.
# parent-systems is asserted present but allowed to be empty: Grimoire's
# DEFAULT_PARENT_SYSTEMS is (), so a non-empty assertion would fail on a fresh
# stack, while the other four are seeded.
# listing each controlled-vocabulary endpoint expecting seeded entries with id/name
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
# getting backup settings expecting schedule, dir and env-lock fields
"$CLI" backups settings get >"$WORK/bset.out" 2>"$WORK/bset.err" \
  || { cat "$WORK/bset.err" >&2; fail "backups settings get exited non-zero"; }
jq -e 'has("backup_schedule") and has("backup_dir") and has("schedule_env_locked")' "$WORK/bset.out" >/dev/null \
  || fail "backups settings get should report settings and env locks: $(cat "$WORK/bset.out")"
ok "backups settings get reports settings and env locks"

# The fixture defaults, so this is a no-op on a seeded stack and converges.
# setting backup schedule off/hour 3 expecting the effective settings echoed back
"$CLI" backups settings set --schedule off --hour 3 >"$WORK/bsset.out" 2>"$WORK/bsset.err" \
  || { cat "$WORK/bsset.err" >&2; fail "backups settings set exited non-zero"; }
jq -e '.backup_schedule == "off" and .backup_schedule_hour == 3' "$WORK/bsset.out" >/dev/null \
  || fail "backups settings set should echo the full settings: $(cat "$WORK/bsset.out")"
ok "backups settings set echoes the effective settings"

# creating a backup archive expecting a new id
"$CLI" backups create >"$WORK/bcreate.out" 2>"$WORK/bcreate.err" \
  || { cat "$WORK/bcreate.err" >&2; fail "backups create exited non-zero"; }
BACKUP_ID=$(jq -r .id "$WORK/bcreate.out")
[ -n "$BACKUP_ID" ] && [ "$BACKUP_ID" != "null" ] \
  || fail "backups create should return an id: $(cat "$WORK/bcreate.out")"
ok "backups create returned a new archive"

# listing backups expecting the new archive plus directory/total_bytes
"$CLI" backups list >"$WORK/blist.out" 2>"$WORK/blist.err" \
  || { cat "$WORK/blist.err" >&2; fail "backups list exited non-zero"; }
jq -e --arg id "$BACKUP_ID" 'any(.backups[]; .id == $id)' "$WORK/blist.out" >/dev/null \
  || fail "backups list should include the new archive: $(cat "$WORK/blist.out")"
jq -e 'has("directory") and has("total_bytes")' "$WORK/blist.out" >/dev/null \
  || fail "backups list should report directory and total_bytes"
ok "backups list includes the new archive"

# downloading the backup expecting its byte count to match the listed size
"$CLI" backups download --id "$BACKUP_ID" --output "$WORK/backup.zip" >"$WORK/bdl.out" 2>"$WORK/bdl.err" \
  || { cat "$WORK/bdl.err" >&2; fail "backups download exited non-zero"; }
EXPECTED_BYTES=$(jq -r --arg id "$BACKUP_ID" '.backups[] | select(.id == $id) | .size_bytes' "$WORK/blist.out")
jq -e --argjson n "$EXPECTED_BYTES" '.bytes == $n' "$WORK/bdl.out" >/dev/null \
  || fail "download receipt should match the listed size_bytes: $(cat "$WORK/bdl.out")"
ok "backups download wrote the archive and reported its size"

# deleting the backup expecting a 204 with no body
"$CLI" backups delete --id "$BACKUP_ID" >"$WORK/bdel.out" 2>"$WORK/bdel.err" \
  || { cat "$WORK/bdel.err" >&2; fail "backups delete exited non-zero"; }
[ ! -s "$WORK/bdel.out" ] || [ "$(tr -d '[:space:]' <"$WORK/bdel.out")" = "" ] \
  || fail "backups delete answers 204 and should print no body: $(cat "$WORK/bdel.out")"
ok "backups delete answered 204 with no body"

# listing backups again expecting the deleted archive is gone
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
# creating a temp folder under books expecting the new path echoed back
"$CLI" files folder create --parent books --name "$SMOKE_DIR" >"$WORK/fcreate.out" 2>"$WORK/fcreate.err" \
  || { cat "$WORK/fcreate.err" >&2; fail "files folder create exited non-zero"; }
jq -e --arg p "books/$SMOKE_DIR" '.path == $p' "$WORK/fcreate.out" >/dev/null \
  || fail "files folder create should echo the new path: $(cat "$WORK/fcreate.out")"
ok "files folder create made the temp folder"

# checking folder contents expecting has_content false on the new empty folder
"$CLI" files folder contents --path "books/$SMOKE_DIR" >"$WORK/fcontents.out" 2>"$WORK/fcontents.err" \
  || { cat "$WORK/fcontents.err" >&2; fail "files folder contents exited non-zero"; }
jq -e '.has_content == false' "$WORK/fcontents.out" >/dev/null \
  || fail "a new folder should report has_content false: $(cat "$WORK/fcontents.out")"
ok "files folder contents reports an empty folder"

# scaffolding the folder expecting the eight category folders created
"$CLI" files folder scaffold --path "books/$SMOKE_DIR" >"$WORK/fscaffold.out" 2>"$WORK/fscaffold.err" \
  || { cat "$WORK/fscaffold.err" >&2; fail "files folder scaffold exited non-zero"; }
jq -e '.created | length == 8' "$WORK/fscaffold.out" >/dev/null \
  || fail "scaffold should create the eight category folders: $(cat "$WORK/fscaffold.out")"
ok "files folder scaffold created the category folders"

# setting the nsfw marker true expecting it echoed back
"$CLI" files folder markers --path "books/$SMOKE_DIR" --nsfw true >"$WORK/fmarkers.out" 2>"$WORK/fmarkers.err" \
  || { cat "$WORK/fmarkers.err" >&2; fail "files folder markers exited non-zero"; }
jq -e '.nsfw == true' "$WORK/fmarkers.out" >/dev/null \
  || fail "markers should report the NSFW flag: $(cat "$WORK/fmarkers.out")"
ok "files folder markers set the NSFW flag"

# A tiny file of our own, so the upload never depends on a fixture book.
# creating a small local file to upload
printf 'smoke' >"$WORK/smoke-upload.txt"
# uploading the file expecting the correct name and size reported
"$CLI" files upload --destination "books/$SMOKE_DIR" --file "$WORK/smoke-upload.txt" >"$WORK/fupload.out" 2>"$WORK/fupload.err" \
  || { cat "$WORK/fupload.err" >&2; fail "files upload exited non-zero"; }
jq -e '.name == "smoke-upload.txt" and .size == 5' "$WORK/fupload.out" >/dev/null \
  || fail "upload should report the name and size: $(cat "$WORK/fupload.out")"
ok "files upload landed one file"

# browsing the folder expecting the uploaded file listed with no record_id
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

# renaming the uploaded file expecting the new path reported
"$CLI" files rename --path "books/$SMOKE_DIR/smoke-upload.txt" --new-name "renamed.txt" >"$WORK/frename.out" 2>"$WORK/frename.err" \
  || { cat "$WORK/frename.err" >&2; fail "files rename exited non-zero"; }
jq -e --arg t "books/$SMOKE_DIR/renamed.txt" '.to == $t' "$WORK/frename.out" >/dev/null \
  || fail "rename should report where it landed: $(cat "$WORK/frename.out")"
ok "files rename moved the file to its new name"

# moving the renamed file into Core expecting one moved entry
"$CLI" files move --sources "books/$SMOKE_DIR/renamed.txt" --destination "books/$SMOKE_DIR/Core" >"$WORK/fmove.out" 2>"$WORK/fmove.err" \
  || { cat "$WORK/fmove.err" >&2; fail "files move exited non-zero"; }
jq -e '.count == 1' "$WORK/fmove.out" >/dev/null \
  || fail "move should report one moved entry: $(cat "$WORK/fmove.out")"
ok "files move relocated the file"

# Soft delete: the row goes, the file stays — files_deleted false is the proof.
# deleting the file without --delete-files expecting a soft delete
"$CLI" files delete --path "books/$SMOKE_DIR/Core/renamed.txt" >"$WORK/fdelete.out" 2>"$WORK/fdelete.err" \
  || { cat "$WORK/fdelete.err" >&2; fail "files delete exited non-zero"; }
jq -e '.files_deleted == false' "$WORK/fdelete.out" >/dev/null \
  || fail "a delete without --delete-files should report files_deleted false: $(cat "$WORK/fdelete.out")"
ok "files delete defaulted to the soft form"

# deleting the temp folder with --delete-files expecting a hard delete
"$CLI" files delete --path "books/$SMOKE_DIR" --confirm-name "$SMOKE_DIR" --delete-files >"$WORK/ffdelete.out" 2>"$WORK/ffdelete.err" \
  || { cat "$WORK/ffdelete.err" >&2; fail "files delete --delete-files exited non-zero"; }
jq -e '.files_deleted == true' "$WORK/ffdelete.out" >/dev/null \
  || fail "a hard delete should report files_deleted true: $(cat "$WORK/ffdelete.out")"
ok "files delete --delete-files removed the folder and its files"

# browsing books again expecting the temp folder is gone
"$CLI" files browse --path books >"$WORK/fbrowse2.out" 2>/dev/null \
  || fail "files browse exited non-zero after cleanup"
jq -e --arg n "$SMOKE_DIR" 'any(.entries[]; .name == $n) | not' "$WORK/fbrowse2.out" >/dev/null \
  || fail "the temp folder should be gone: $(cat "$WORK/fbrowse2.out")"
ok "the temp folder is gone, so the run converges"

# 4b. The version check runs on a cadence, not only at login.
# checking login recorded the server version and a check timestamp
jq -e '.lastServerVersion == "'"$EXPECTED_VERSION"'"' "$CONFIG" >/dev/null \
  || fail "login should have recorded the server version: $(cat "$CONFIG")"
jq -e '.lastVersionCheck != null' "$CONFIG" >/dev/null \
  || fail "login should have recorded a check timestamp"
ok "login records the server version"

# Inside the window: no probe, and the timestamp is untouched. DebugHttpHandler
# logs every request it sends, so its absence for /api/about is what proves no
# probe happened — an unmoved timestamp alone would also be consistent with a
# probe that ran and failed to persist.
# running systems list inside the 24h window expecting no probe, timestamp unmoved
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
# backdating the last check then listing systems expecting a probe that advances it
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
# saving the config before attempting a bad login
cp "$CONFIG" "$WORK/config.before"
# logging in with a wrong password expecting exit 2 and an unmodified config
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
# running self-test expecting the offline integrity check to pass
"$CLI" self-test >"$WORK/self.out" 2>"$WORK/self.err" \
  || { cat "$WORK/self.err" >&2; fail "self-test exited non-zero"; }
ok "self-test"

# 6b. me: the caller's own account, and the role a write command will need.
# running me expecting the seeded admin account's username, role and id
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

# listing systems expecting the seeded top-level count with children hidden
syslist
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "expected $EXPECTED_SYSTEMS systems, got $COUNT"
ok "systems list returns $EXPECTED_SYSTEMS systems"

# listing systems with --include-children expecting the full count
syslist --include-children
[ "$COUNT" -eq "$EXPECTED_ALL_SYSTEMS" ] \
  || fail "--include-children should return $EXPECTED_ALL_SYSTEMS, got $COUNT"
ok "--include-children returns $EXPECTED_ALL_SYSTEMS systems"

# The container is a shelf of systems: kind "parent", three editions, no books.
# listing systems expecting the shadowrun container as kind parent, child_count 3
syslist
CONTAINER=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun")')
[ "$(echo "$CONTAINER" | jq -r .container_kind)" = "parent" ] \
  || fail "Shadowrun should be a parent container"
[ "$(echo "$CONTAINER" | jq -r .child_count)" -eq 3 ] \
  || fail "Shadowrun should hold 3 editions"
ok "the Shadowrun container reports kind=parent and child_count=3"

# A child carries the folder-derived edition and a link back to its container.
# listing systems with --include-children expecting a derived edition and parent_name
syslist --include-children
CHILD=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 6 DE")')
[ -n "$CHILD" ] || fail "Shadowrun 6 DE missing — the container did not adopt it"
[ "$(echo "$CHILD" | jq -r .edition)" = "6 DE" ] \
  || fail "edition should be folder-derived as '6 DE', got '$(echo "$CHILD" | jq -r .edition)'"
[ "$(echo "$CHILD" | jq -r .parent_name)" = "Shadowrun" ] \
  || fail "parent_name should be Shadowrun"
ok "a container child carries a derived edition and parent_name"

# --parent-id selects exactly one container's children.
# extracting the shadowrun container id, then listing its children via --parent-id
CONTAINER_ID=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun") | .id')
syslist --parent-id "$CONTAINER_ID"
[ "$COUNT" -eq 3 ] || fail "--parent-id should return the 3 Shadowrun editions, got $COUNT"
ok "--parent-id lists one container's children"

# systems get on a container populates its `children` array — the one place a
# missing [JsonSerializable] registration on the nested summary shape would
# surface in the AOT binary.
# getting the container expecting its children array to hold 3 entries
sysget --id "$CONTAINER_ID"
[ "$(echo "$GET_JSON" | jq '.children | length')" -eq 3 ] \
  || fail "Shadowrun container should have 3 children"
ok "systems get on a container returns its children"

# The reserved slug one-page-rpgs becomes a one-page container with no marker
# file, and each loose PDF becomes its own system.
# listing systems expecting one-page-rpgs as a one-page container with 2 children
syslist
ONEPAGE=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "one-page-rpgs")')
[ "$(echo "$ONEPAGE" | jq -r .container_kind)" = "one-page" ] \
  || fail "one-page-rpgs should be a one-page container"
[ "$(echo "$ONEPAGE" | jq -r .child_count)" -eq 2 ] \
  || fail "one-page-rpgs should hold 2 games"
# listing systems with --include-children expecting 'Lasers And Feelings' capitalised
syslist --include-children
echo "$LIST_JSON" | jq -e '.[] | select(.name == "Lasers And Feelings")' >/dev/null \
  || fail "expected 'Lasers And Feelings' — prettify_collection_name capitalises 'and'"
ok "one-page-rpgs is a container holding 2 single-book systems"

# --- server resolution --------------------------------------------------------
# GRIMOIRE_SERVER is the only tier above the config file now that --server is
# gone from everything but login. The stored server is deliberately made
# unreachable first so a config-file fallback can't mask a broken env tier. The
# token has no env tier and stays in the file.
# pointing the stored server at an unreachable address to test env override
cp "$CONFIG" "$WORK/config.saved"
jq '.server = "http://127.0.0.1:1"' "$WORK/config.saved" >"$CONFIG"

# listing systems with GRIMOIRE_SERVER set expecting it to override the stored server
LIST_JSON=$(GRIMOIRE_SERVER="$SERVER" "$CLI" systems list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems list with GRIMOIRE_SERVER exited non-zero"; }
COUNT=$(echo "$LIST_JSON" | jq 'length')
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "GRIMOIRE_SERVER returned $COUNT over an unreachable stored server, expected $EXPECTED_SYSTEMS"
ok "GRIMOIRE_SERVER overrides the stored server"

# The flag is gone: a caller still passing it gets a parse error, not a silent
# ignore that would send the request somewhere it did not intend.
# running systems list with the removed --server flag expecting a parse error
"$CLI" systems list --server "$SERVER" >/dev/null 2>&1 \
  && fail "systems list should no longer accept --server"
ok "systems list refuses the removed --server flag"

# A rejected token surfaces as exit 2. The refresh token goes with it, so the
# renewal path cannot rescue the request and hide the 401.
# swapping in a bogus access token with no refresh token
jq '.accessToken = "bogus-token" | del(.refreshToken)' "$WORK/config.saved" >"$CONFIG"
# listing systems with a bogus token expecting exit 2, not-authenticated message
set +e
GRIMOIRE_SERVER="$SERVER" "$CLI" systems list >/dev/null 2>"$WORK/badtoken.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a bogus stored token should exit 2, got $rc"
grep -qi "not authenticated" "$WORK/badtoken.err" \
  || fail "a bogus stored token gave no 'Not authenticated' message: $(cat "$WORK/badtoken.err")"
ok "a bogus stored token against a reachable server exits 2"

# restoring the saved good config
cp "$WORK/config.saved" "$CONFIG"

# The child-hiding check runs BEFORE the filters, so a filter on metadata that
# only children carry returns [] with exit 0 — indistinguishable from a genuine
# miss. This asserts the trap exists rather than working around it.
# listing systems with --genre and no --include-children expecting zero results
syslist --genre Cyberpunk
[ "$COUNT" -eq 0 ] \
  || fail "--genre without --include-children should return 0, got $COUNT"
ok "a filter without --include-children returns [] on a containerised library"

# listing systems with --include-children --genre Cyberpunk expecting 2 matches
syslist --include-children --genre Cyberpunk
[ "$COUNT" -eq 2 ] || fail "--genre Cyberpunk should match 2"
# listing systems with --edition '6 DE' expecting 1 match
syslist --include-children --edition "6 DE"
[ "$COUNT" -eq 1 ] || fail "--edition '6 DE' should match 1"
# listing systems with --edition '5 DE' expecting 2 matches across families
syslist --include-children --edition "5 DE"
[ "$COUNT" -eq 2 ] || fail "--edition '5 DE' should match 2 across families"
# listing systems with --edition '5 EN' expecting 2 matches across families
syslist --include-children --edition "5 EN"
[ "$COUNT" -eq 2 ] || fail "--edition '5 EN' should match 2 across families"
# listing systems with --license OGL expecting 1 match
syslist --include-children --license OGL
[ "$COUNT" -eq 1 ] || fail "--license OGL should match 1"
# listing systems with --genre nope expecting zero matches
syslist --include-children --genre nope
[ "$COUNT" -eq 0 ] || fail "an unmatched filter should return []"
ok "filters narrow the result set"

# Shadowrun 4 DE is seeded raw, so a family filter must exclude it.
# listing systems with --family Shadowrun expecting 2, excluding the raw fixture
syslist --include-children --family Shadowrun
[ "$COUNT" -eq 2 ] \
  || fail "--family Shadowrun should match 2, not the raw Shadowrun 4 DE"
ok "systems with empty metadata are excluded by filters"

# The (nsfw) folder marker, not a PATCH, is what sets this. The system is flat,
# so it needs no --include-children.
# listing systems with --explicit true expecting only the nsfw-marked fixture
syslist --explicit true
EXPLICIT=$(echo "$LIST_JSON" | jq -r '.[].name')
[ "$EXPLICIT" = "Fixture Explicit RPG" ] \
  || fail "--explicit true returned '$EXPLICIT'"
ok "--explicit true matches the nsfw-marked system"

# Filter values with an ampersand must survive URL encoding. parent_system is
# now folder-derived from the container name, with its !! sort prefix stripped.
# listing systems with a '&' in --parent-system expecting it to round-trip
syslist --include-children --parent-system "Dungeons & Dragons"
[ "$COUNT" -eq 1 ] || fail "a filter value containing '&' did not round-trip"
ok "ampersand in a filter value round-trips"

# Descending sort must actually be descending. Containers hold no books
# directly, so this runs over children to avoid a near-all-zero comparison.
# listing systems sorted by book_count desc expecting genuinely descending order
syslist --include-children --sort book_count --desc
COUNTS=$(echo "$LIST_JSON" | jq '[.[].book_count]')
echo "$COUNTS" | jq -e '. == (. | sort | reverse)' >/dev/null \
  || fail "--sort book_count --desc was not descending: $COUNTS"
ok "--sort book_count --desc is ordered"

# A rejected sort key must fail before any request is made.
# running systems list with --sort bogus expecting a parse-time rejection
set +e
"$CLI" systems list --sort bogus >/dev/null 2>"$WORK/sort.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "--sort bogus should have failed"
grep -q "Must be one of" "$WORK/sort.err" || fail "no value-set message: $(cat "$WORK/sort.err")"
ok "--sort bogus is rejected at parse time"

# systems get: filters apply to the books and change the reported counts.
# listing shadowrun 6 de then getting it expecting all 3 books
syslist --include-children --edition "6 DE"
SR6=$(echo "$LIST_JSON" | jq -r '.[0].id')
sysget --id "$SR6"
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 3 ] \
  || fail "Shadowrun 6 DE should have 3 books"
# getting shadowrun 6 de with --category core expecting 2 books, recomputed count
sysget --id "$SR6" --category core
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 2 ] || fail "--category core should keep 2 books"
[ "$(echo "$GET_JSON" | jq '.book_count')" -eq 2 ] \
  || fail "book_count should be recomputed from the filtered books"
ok "systems get filters books and recomputes counts"

# The canonical category, not the folder name.
# getting with --category supplements (a folder name) expecting zero matches
sysget --id "$SR6" --category supplements
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 0 ] \
  || fail "'supplements' is a folder name and should match nothing"
# getting with --category supplement (the canonical value) expecting 1 match
sysget --id "$SR6" --category supplement
[ "$(echo "$GET_JSON" | jq '.books | length')" -eq 1 ] \
  || fail "'supplement' is the canonical category and should match 1"
ok "category filtering uses canonical values"

# getting a system with an unknown id expecting exit 2 with a not-found hint
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
# getting systems with empty, '.', and '../about' ids expecting exit 2, no stack trace
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
# listing systems to find Shadowrun 4 DE's id for the first write test
syslist --include-children
SR4=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Shadowrun 4 DE") | .id')
[ -n "$SR4" ] || fail "no Shadowrun 4 DE fixture to write to"

# updating Shadowrun 4 DE's description then reading it back expecting the new value
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
# updating with an unknown field 'descriptoin' expecting exit 1 naming the typo
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
# writing valid nested publishers/urls then reading back expecting it applied
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

# updating with a typo inside a nested publisher entry expecting exit 1 with its path
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
# passing both --stdin and --input expecting exit 1 with a mutual-exclusion message
set +e
"$CLI" systems update --id "$SR4" --stdin --input "$WORK/typo.json" >/dev/null 2>"$WORK/both.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "--stdin with --input should exit 1, got $rc"
grep -q "not both" "$WORK/both.err" || fail "no mutual-exclusion message: $(cat "$WORK/both.err")"
# passing neither --stdin nor --input expecting exit 1
set +e
"$CLI" systems update --id "$SR4" >/dev/null 2>"$WORK/none.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "no body source should exit 1, got $rc"
ok "--input and --stdin are mutually exclusive and one is required"

# batch-update: one good id and one bogus id must exit 3, applying the good one.
# license, not description or system_family: no assertion above filters on a
# license other than OGL, so this stays idempotent across re-runs.
# batch-updating one valid id and one bogus id expecting exit 3, good id applied
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
# batch-updating with only the valid id expecting exit 0 and no errors
echo "{\"items\":[{\"id\":\"$SR4\",\"license\":\"Smoke Fixture License\"}]}" \
  | "$CLI" systems batch-update --stdin >"$WORK/batch2.out" 2>"$WORK/batch2.err" \
  || { cat "$WORK/batch2.err" >&2; fail "a fully-applying batch should exit 0"; }
jq -e '.errors | length == 0' "$WORK/batch2.out" >/dev/null \
  || fail "no errors expected: $(cat "$WORK/batch2.out")"
ok "batch-update exits 0 when every item applies"

# batch-tag is additive: the second call must not displace the first tag.
# batch-tagging Shadowrun 4 DE with smoke-alpha
echo "{\"ids\":[\"$SR4\"],\"tags\":[\"smoke-alpha\"]}" \
  | "$CLI" systems batch-tag --stdin >"$WORK/tag1.out" 2>"$WORK/tag1.err" \
  || { cat "$WORK/tag1.err" >&2; fail "batch-tag exited non-zero"; }
# batch-tagging the same system with smoke-beta expecting both tags merged
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
# batch-tagging an id that does not resolve expecting exit 3
echo '{"ids":["no-such-id"],"tags":["smoke-alpha"]}' \
  >"$WORK/tagbad.json"
set +e
"$CLI" systems batch-tag --input "$WORK/tagbad.json" >"$WORK/tagbad.out" 2>"$WORK/tagbad.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "an all-bogus batch-tag should exit 3, got $rc"
ok "batch-tag exits 3 when an id does not resolve"

# An unknown key in a batch item is refused client-side.
# batch-updating with an unknown key 'licence' in an item expecting exit 1
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
# listing systems to find Das Schwarze Auge 5 DE's id for book-folders tests
syslist --include-children
DSA=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Das Schwarze Auge 5 DE") | .id')
[ -n "$DSA" ] || fail "no Das Schwarze Auge 5 DE fixture for book folders"
FOLDER_PATH="$DSA/core/errata"

# setting tags on a book folder via stdin expecting the written path echoed back
SET_JSON=$(printf '{"path":"%s","tags":["errata-smoke"]}' "$FOLDER_PATH" \
  | "$CLI" systems book-folders set --id "$DSA" --stdin 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders set exited non-zero"; }
[ "$(echo "$SET_JSON" | jq -r .path)" = "$FOLDER_PATH" ] \
  || fail "set should echo the path it wrote: $SET_JSON"
ok "systems book-folders set writes a folder's tags"

# listing book folders expecting the just-written folder to appear
FOLDERS_JSON=$("$CLI" systems book-folders list --id "$DSA" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero"; }
echo "$FOLDERS_JSON" | jq -e --arg p "$FOLDER_PATH" '.folders[] | select(.path == $p)' >/dev/null \
  || fail "the folder just written should be listed: $FOLDERS_JSON"
ok "systems book-folders list shows the written folder"

# The point of the feature: a book below the path inherits the tag. This is the
# round trip that upstream #357 broke — the server derived the folder's depth
# differently for a container child, and Das Schwarze Auge 5 DE is one.
# listing items for the errata-smoke tag expecting the folder, then its contents to hold the book
TAG_ITEMS=$("$CLI" tags items --tag errata-smoke 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags items exited non-zero"; }
FOLDER_KEY=$(echo "$TAG_ITEMS" | jq -r '.folders[] | select(.path == "errata") | .key')
[ -n "$FOLDER_KEY" ] || fail "tags items should list the tagged folder: $TAG_ITEMS"
FOLDER_ITEMS=$("$CLI" tags folder-items --tag errata-smoke --resource-type book --folder "$FOLDER_KEY" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags folder-items exited non-zero"; }
echo "$FOLDER_ITEMS" | jq -e '.items[] | select(.title == "DSA5 Errata")' >/dev/null \
  || fail "the folder tag should reach the book below it: $FOLDER_ITEMS"
ok "a folder tag reaches the book below its path"

# deleting the book folder's tags expecting a deleted status
DEL_JSON=$("$CLI" systems book-folders delete --id "$DSA" --path "$FOLDER_PATH" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders delete exited non-zero"; }
[ "$(echo "$DEL_JSON" | jq -r .status)" = "deleted" ] \
  || fail "delete should report the deletion: $DEL_JSON"
ok "systems book-folders delete removes the folder"

# listing book folders again expecting none remain after delete
FOLDERS_JSON=$("$CLI" systems book-folders list --id "$DSA" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "book-folders list exited non-zero after delete"; }
[ "$(echo "$FOLDERS_JSON" | jq '.folders | length')" -eq 0 ] \
  || fail "the folder should be gone after delete: $FOLDERS_JSON"
ok "the deleted folder is no longer listed"

# --- system covers --------------------------------------------------------
# A different system from Shadowrun 4 DE on purpose: that one already carries
# the description write above and the metadata diff assertions below, and a
# cover write would couple a third assertion to the same fixture.
# listing systems to find Fixture Explicit RPG's id for the cover tests
syslist --include-children
COVER_SYS=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Fixture Explicit RPG") | .id')
[ -n "$COVER_SYS" ] || fail "no Fixture Explicit RPG to attach a cover to"

# 404 first: this system has neither folder art nor an upload.
# getting a cover for a system with none expecting exit 2 with a not-found hint
set +e
"$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/none.png" >/dev/null 2>"$WORK/cover404.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "cover get on a system with no cover should exit 2, got $rc: $(cat "$WORK/cover404.err")"
grep -qi "not found" "$WORK/cover404.err" \
  || fail "no not-found hint: $(cat "$WORK/cover404.err")"
ok "systems cover get 404s when the system has no cover"

# uploading a png cover expecting cover_image to end with .png
UPLOAD_JSON=$("$CLI" systems cover upload --id "$COVER_SYS" --file docker/fixture-cover.png 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover upload exited non-zero"; }
echo "$UPLOAD_JSON" | jq -e '.cover_image | endswith(".png")' >/dev/null \
  || fail "upload should report a .png cover_image: $UPLOAD_JSON"
ok "systems cover upload stores a png"

# getting the cover expecting the receipt's byte count to match the file
GET_JSON=$("$CLI" systems cover get --id "$COVER_SYS" --output "$WORK/cover.png" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover get exited non-zero"; }
[ "$(echo "$GET_JSON" | jq -r .bytes)" -eq "$(wc -c < "$WORK/cover.png")" ] \
  || fail "the receipt's byte count should match the file: $GET_JSON"
ok "systems cover get writes the file and reports its size"

# getting the cover with --output - expecting identical bytes to the file
"$CLI" systems cover get --id "$COVER_SYS" --output - > "$WORK/cover-dash.png" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems cover get --output - exited non-zero"; }
cmp -s "$WORK/cover.png" "$WORK/cover-dash.png" \
  || fail "--output - and --output <file> should produce identical bytes"
ok "systems cover get --output - streams the same bytes to stdout"

# deleting the cover expecting an ok status
DEL_JSON=$("$CLI" systems cover delete --id "$COVER_SYS" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover delete exited non-zero"; }
[ "$(echo "$DEL_JSON" | jq -r .status)" = "ok" ] || fail "delete should answer ok: $DEL_JSON"
# getting the cover again after delete expecting exit 2 with a not-found hint
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

# listing books expecting a total and books array matching the seeded count
booklist
[ "$(echo "$LIST_JSON" | jq -r .total)" -eq "$EXPECTED_BOOKS" ] \
  || fail "expected $EXPECTED_BOOKS books, got $(echo "$LIST_JSON" | jq -r .total)"
[ "$(echo "$LIST_JSON" | jq '.books | length')" -eq "$EXPECTED_BOOKS" ] \
  || fail "the default limit should return every book, got $(echo "$LIST_JSON" | jq '.books | length')"
ok "books list returns a total and a books array"

# listing Shadowrun 4 DE's books with --limit 2 expecting total 3, page of 2
booklist --system-id "$SR4" --limit 2
[ "$(echo "$LIST_JSON" | jq -r .total)" -eq 3 ] \
  || fail "Shadowrun 4 DE should report 3 books regardless of --limit, got $(echo "$LIST_JSON" | jq -r .total)"
[ "$(echo "$LIST_JSON" | jq '.books | length')" -eq 2 ] \
  || fail "--limit 2 should return 2 books, got $(echo "$LIST_JSON" | jq '.books | length')"
FIRST_PAGE_ID=$(echo "$LIST_JSON" | jq -r '.books[0].id')
ok "--limit narrows the page while total stays the system's full count"

# listing with --offset 2 --limit 2 expecting 1 book and a different lead id
booklist --system-id "$SR4" --offset 2 --limit 2
[ "$(echo "$LIST_JSON" | jq '.books | length')" -eq 1 ] \
  || fail "offset 2 of 3 should leave exactly 1 book, got $(echo "$LIST_JSON" | jq '.books | length')"
[ "$(echo "$LIST_JSON" | jq -r '.books[0].id')" != "$FIRST_PAGE_ID" ] \
  || fail "--offset 2 should have skipped past the first page's lead book"
ok "--offset advances to a different first id"

# listing books with --category core expecting only core-category books
booklist --category core
[ "$(echo "$LIST_JSON" | jq '.books | length')" -gt 0 ] || fail "--category core matched nothing"
echo "$LIST_JSON" | jq -e '.books | all(.category == "core")' >/dev/null \
  || fail "--category core returned a non-core book: $(echo "$LIST_JSON" | jq -c '.books | map(.category)')"
# listing books with --category 'Core' capitalised expecting zero matches
booklist --category Core
[ "$(echo "$LIST_JSON" | jq -r .total)" -eq 0 ] \
  || fail "'Core' should match nothing, got $(echo "$LIST_JSON" | jq -r .total)"
ok "--category is case-sensitive"

# listing Shadowrun 4 DE's core books with --limit 1 to pick one book id
booklist --system-id "$SR4" --category core --limit 1
SR4_BOOK=$(echo "$LIST_JSON" | jq -r '.books[0].id')
[ -n "$SR4_BOOK" ] && [ "$SR4_BOOK" != null ] || fail "no core book under Shadowrun 4 DE"

# getting the book expecting game_system populated with the parent system id
bookget --id "$SR4_BOOK"
[ "$(echo "$GET_JSON" | jq -r '.game_system.id')" = "$SR4" ] \
  || fail "books get should populate game_system: $(echo "$GET_JSON" | jq -c .game_system)"
ok "books get returns the detail shape with game_system populated"

# Whether a fixture PDF gets a scan-generated thumbnail is the server's call,
# not the CLI's — assert on has_thumbnail first and only download when true.
# downloading a book thumbnail when has_thumbnail is true, expecting bytes > 0
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
# updating the book description via stdin expecting status ok
echo '{"description":"smoke fixture book description"}' \
  | "$CLI" books update --id "$SR4_BOOK" --stdin >"$WORK/bupd.out" 2>"$WORK/bupd.err" \
  || { cat "$WORK/bupd.err" >&2; fail "books update exited non-zero"; }
jq -e '.status == "ok"' "$WORK/bupd.out" >/dev/null \
  || fail "update should answer {\"status\":\"ok\"}: $(cat "$WORK/bupd.out")"
# getting the book back expecting the new description to read back
bookget --id "$SR4_BOOK"
[ "$(echo "$GET_JSON" | jq -r .description)" = "smoke fixture book description" ] \
  || fail "the written description did not read back: $(echo "$GET_JSON" | jq -r .description)"
ok "books update writes a field and books get reads it back"

# batch-tag is additive: the second call must not displace the first tag.
# batch-tagging the book with a first tag
echo "{\"ids\":[\"$SR4_BOOK\"],\"tags\":[\"smoke-book-alpha\"]}" \
  | "$CLI" books batch-tag --stdin >"$WORK/btag1.out" 2>"$WORK/btag1.err" \
  || { cat "$WORK/btag1.err" >&2; fail "batch-tag exited non-zero"; }
# batch-tagging the book again with a second tag, expecting both to remain
echo "{\"ids\":[\"$SR4_BOOK\"],\"tags\":[\"smoke-book-beta\"]}" \
  | "$CLI" books batch-tag --stdin >"$WORK/btag2.out" 2>"$WORK/btag2.err" \
  || { cat "$WORK/btag2.err" >&2; fail "the second batch-tag exited non-zero"; }
jq -e --arg id "$SR4_BOOK" '.tags[$id] | index("smoke-book-alpha") != null and index("smoke-book-beta") != null' \
  "$WORK/btag2.out" >/dev/null \
  || fail "batch-tag should have merged both tags: $(cat "$WORK/btag2.out")"
ok "batch-tag adds a tag and leaves the existing one in place"

# --- maps ----------------------------------------------------------------
# Requires docker/seed.sh to have run — three fixture maps, two directly under
# maps/battlemaps and one under maps/battlemaps/caves.
# listing maps expecting at least the three seeded fixture maps
"$CLI" maps list >"$WORK/maps.out" 2>"$WORK/maps.err" \
  || { cat "$WORK/maps.err" >&2; fail "maps list exited non-zero"; }
jq -e '.total >= 3 and (.maps | length) >= 3' "$WORK/maps.out" >/dev/null \
  || fail "maps list should report the seeded maps: $(cat "$WORK/maps.out")"
ok "maps list returns the seeded maps"

# listing maps with --limit 1 expecting exactly one result
"$CLI" maps list --limit 1 >"$WORK/maps-limit.out" 2>&1 \
  || fail "maps list --limit exited non-zero"
jq -e '(.maps | length) == 1' "$WORK/maps-limit.out" >/dev/null \
  || fail "--limit 1 should return one row: $(cat "$WORK/maps-limit.out")"
ok "maps list --limit bounds the page"

# The two orders are distinguishable on the fixtures: by path the caves map comes
# last (maps/battlemaps/* before maps/battlemaps/caves/*), by filename it comes
# second. Asserting both is what proves the flag reaches the query string — a
# --sort that sent nothing would return the path order and fail the first check.
# listing maps sorted by name expecting filename order, not path order
"$CLI" maps list --sort name >"$WORK/maps-sort.out" 2>&1 \
  || fail "maps list --sort exited non-zero"
jq -e '[.maps[].filename] == ["Crossroads.png", "Deep Cave.png", "Tavern.png"]' \
  "$WORK/maps-sort.out" >/dev/null \
  || fail "--sort name should order by filename: $(cat "$WORK/maps-sort.out")"
jq -e '[.maps[].filename] == ["Crossroads.png", "Tavern.png", "Deep Cave.png"]' \
  "$WORK/maps.out" >/dev/null \
  || fail "the default should order by path: $(cat "$WORK/maps.out")"
ok "maps list --sort name orders by filename, not by path"

# The exact-match rule: --folder takes the folder part of relative_path, which
# excludes the maps/ root — folder_path on a map under maps/battlemaps reads
# "battlemaps", not "maps/battlemaps" (verified via maps get below). The child
# folder's map must not appear.
# listing maps filtered by folder expecting only the two direct children
"$CLI" maps list --folder "battlemaps" >"$WORK/maps-folder.out" 2>&1 \
  || fail "maps list --folder exited non-zero"
# all() over an empty array is vacuously true, so length is asserted first —
# a regression that returned zero maps must not pass as "no subfolder leaked".
jq -e '(.maps | length) == 2' "$WORK/maps-folder.out" >/dev/null \
  || fail "--folder should return the 2 maps directly under it: $(cat "$WORK/maps-folder.out")"
jq -e '[.maps[].relative_path] | all(startswith("maps/battlemaps/caves") | not)' \
  "$WORK/maps-folder.out" >/dev/null \
  || fail "--folder must not reach a subfolder: $(cat "$WORK/maps-folder.out")"
ok "maps list --folder is an exact folder, not a subtree"

# extracting the Tavern map's id from the list output for later steps
MAP_ID=$(jq -r '.maps[] | select(.filename == "Tavern.png") | .id' "$WORK/maps.out")
[ -n "$MAP_ID" ] || fail "no fixture map id found: $(cat "$WORK/maps.out")"

# getting the map expecting grid and folder context, folder_path excluding maps/
"$CLI" maps get --id "$MAP_ID" >"$WORK/mapget.out" 2>&1 \
  || fail "maps get exited non-zero"
jq -e 'has("grid") and has("folder_path") and has("folder_tags")' "$WORK/mapget.out" >/dev/null \
  || fail "maps get should carry grid and folder context: $(cat "$WORK/mapget.out")"
jq -e '.folder_path == "battlemaps"' "$WORK/mapget.out" >/dev/null \
  || fail "folder_path should exclude the maps/ root: $(cat "$WORK/mapget.out")"
ok "maps get returns grid and folder context"

# downloading the map's rendered thumbnail expecting a non-empty file
"$CLI" maps thumbnail --id "$MAP_ID" --output "$WORK/mapthumb.webp" >/dev/null 2>&1 \
  || fail "maps thumbnail exited non-zero"
[ -s "$WORK/mapthumb.webp" ] || fail "maps thumbnail wrote no bytes"
ok "maps thumbnail downloads the rendered image"

# setting the map's grid override to 70
echo '{"grid_px":70}' | "$CLI" maps update --id "$MAP_ID" --stdin >"$WORK/mapupd.out" 2>&1 \
  || fail "maps update exited non-zero"
# getting the map back expecting grid_px to read back as 70
"$CLI" maps get --id "$MAP_ID" >"$WORK/mapget2.out" 2>&1
jq -e '.grid_px == 70' "$WORK/mapget2.out" >/dev/null \
  || fail "the grid override should read back: $(cat "$WORK/mapget2.out")"
ok "maps update sets a grid override"

# The documented clear. This is also what makes the block idempotent.
# clearing the grid override by setting grid_px to 0
echo '{"grid_px":0}' | "$CLI" maps update --id "$MAP_ID" --stdin >"$WORK/mapclr.out" 2>&1 \
  || fail "maps update --stdin with 0 exited non-zero"
# getting the map back expecting grid_px to read back as null
"$CLI" maps get --id "$MAP_ID" >"$WORK/mapget3.out" 2>&1
jq -e '.grid_px == null' "$WORK/mapget3.out" >/dev/null \
  || fail "0 should clear the grid override: $(cat "$WORK/mapget3.out")"
ok "maps update clears a grid override with 0"

# The symptom the whole group exists to fix: tagging a map without duplicates.
# batch-tagging the map with smoke-map
echo "{\"ids\":[\"$MAP_ID\"],\"tags\":[\"smoke-map\"]}" \
  | "$CLI" maps batch-tag --stdin >"$WORK/maptag.out" 2>&1 \
  || fail "maps batch-tag exited non-zero"
# looking up items by tag expecting the tagged map to be found
"$CLI" tags items --tag smoke-map --resource-type map >"$WORK/maptagitems.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$MAP_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/maptagitems.out" >/dev/null \
  || fail "the tagged map should be findable: $(cat "$WORK/maptagitems.out")"
ok "maps batch-tag tags a map without duplicates merge-metadata"

# setting a display-cased tag on the battlemaps folder
echo '{"path":"battlemaps","tags":["Smoke Folder"]}' \
  | "$CLI" maps folders set --stdin >"$WORK/mapfset.out" 2>&1 \
  || fail "maps folders set exited non-zero"
# listing map folders expecting the tag to list in display casing
"$CLI" maps folders list >"$WORK/mapflist.out" 2>&1 \
  || fail "maps folders list exited non-zero"
jq -e '[.folders[] | select(.path == "battlemaps") | .tags[]] | any(. == "Smoke Folder")' \
  "$WORK/mapflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/mapflist.out")"
ok "maps folders set writes a tag that lists in display casing"

# An unknown field is refused client-side: exit 1, and no request is made. The
# server ignores an extra key, so only the CLI can catch a misspelled field.
# updating a map with an unknown field expecting exit 1 before any request
set +e
echo '{"grid_pixels":70}' | "$CLI" maps update --id "$MAP_ID" --stdin \
  >/dev/null 2>"$WORK/maptypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown map field should exit 1, got $rc: $(cat "$WORK/maptypo.err")"
grep -q "grid_pixels" "$WORK/maptypo.err" || fail "no offending field named: $(cat "$WORK/maptypo.err")"
ok "maps update refuses an unknown field before any request"

# A declared field with the wrong type passes client validation untouched, so
# this is the server's 422 reaching the caller as a non-zero exit.
# updating a map's grid with a wrong-typed value expecting a server rejection
echo '{"grid_px":"seventy"}' | "$CLI" maps update --id "$MAP_ID" --stdin >/dev/null 2>&1 \
  && fail "a wrong-typed grid should not exit 0"
ok "maps update surfaces the server's rejection of a wrong-typed grid"

# ---- models -----------------------------------------------------------------
# Requires docker/seed.sh to have run — three fixture models: one under
# Goblins/Presupported, one under Goblins/Unsupported, and one under
# Goblins/Loose that neither regex matches (is_supported starts unknown).
# listing models expecting at least the three seeded fixture models
"$CLI" models list >"$WORK/models.out" 2>"$WORK/models.err" \
  || { cat "$WORK/models.err" >&2; fail "models list exited non-zero"; }
jq -e '.total >= 3 and (.models | length) >= 3' "$WORK/models.out" >/dev/null \
  || fail "models list should report the seeded models: $(cat "$WORK/models.out")"
ok "models list returns the seeded models"

# listing models with --limit 1 expecting exactly one result
"$CLI" models list --limit 1 >"$WORK/models-limit.out" 2>&1 \
  || fail "models list --limit exited non-zero"
jq -e '(.models | length) == 1' "$WORK/models-limit.out" >/dev/null \
  || fail "--limit 1 should return one row: $(cat "$WORK/models-limit.out")"
ok "models list --limit bounds the page"

# The path inference: each assertion names the fixture it means, so it fails
# if that exact file stops being classified. Matching on the flag alone would
# stop discriminating from run 2, when the Loose fixture the write below
# lands on is already presupported and would satisfy the check on its own.
# Neither named fixture is ever written — nothing can restore unknown, so a
# write here would permanently erase the pair for every future run.
# checking the presupported and unsupported fixtures classify correctly
jq -e '.models[] | select(.filename == "Goblin Archer.stl") | .is_presupported == true' \
  "$WORK/models.out" >/dev/null \
  || fail "the Presupported fixture should read presupported: $(cat "$WORK/models.out")"
jq -e '.models[] | select(.filename == "Goblin Shaman.stl") | .is_unsupported == true' \
  "$WORK/models.out" >/dev/null \
  || fail "the Unsupported fixture should read unsupported: $(cat "$WORK/models.out")"
ok "the derived support pair reflects the fixture folders"

# extracting the unsupported (Goblin Shaman) fixture's model id
MODEL_ID=$(jq -r '.models[] | select(.filename == "Goblin Shaman.stl") | .id' "$WORK/models.out")
[ -n "$MODEL_ID" ] || fail "no unsupported fixture id: $(cat "$WORK/models.out")"

# getting the model expecting folder context and the derived support pair
"$CLI" models get --id "$MODEL_ID" >"$WORK/modelget.out" 2>&1 \
  || fail "models get exited non-zero"
jq -e 'has("folder_path") and has("folder_tags") and has("is_presupported")' \
  "$WORK/modelget.out" >/dev/null \
  || fail "models get should carry folder context and the pair: $(cat "$WORK/modelget.out")"
ok "models get returns folder context and the derived pair"

# The is_supported write lands on the Loose fixture, not on $MODEL_ID:
# Presupported/Unsupported must stay untouched for the derived-pair check
# above to hold on every future run.
#
# true and false are both fully reversible — update_model applies either like
# any other field. Only null is one-way: it is dropped (exclude_none=True), so
# a model can leave unknown but never return to it. Because true/false are
# reversible, asserting only "ends true" would be a real transition the first
# time this fixture is ever touched, but a no-op on every run after that (it
# is already true), so a broken update path would pass silently from the
# second run onward. Toggling false-then-true forces a genuine transition on
# every single run.
# extracting the Loose fixture (Goblin Whelp) model id, still unknown support
WRITE_ID=$(jq -r '.models[] | select(.filename == "Goblin Whelp.stl") | .id' "$WORK/models.out")
[ -n "$WRITE_ID" ] || fail "no Loose fixture id: $(cat "$WORK/models.out")"

# setting is_supported false on the Loose fixture
echo '{"is_supported":false}' | "$CLI" models update --id "$WRITE_ID" --stdin >/dev/null 2>&1 \
  || fail "models update exited non-zero"
# getting the model back expecting it to read as unsupported
"$CLI" models get --id "$WRITE_ID" >"$WORK/modelget2a.out" 2>&1
jq -e '.is_unsupported == true' "$WORK/modelget2a.out" >/dev/null \
  || fail "is_supported false should read back as unsupported: $(cat "$WORK/modelget2a.out")"

# setting is_supported true on the same fixture
echo '{"is_supported":true}' | "$CLI" models update --id "$WRITE_ID" --stdin >/dev/null 2>&1 \
  || fail "models update exited non-zero"
# getting the model back expecting it to read as presupported
"$CLI" models get --id "$WRITE_ID" >"$WORK/modelget2.out" 2>&1
jq -e '.is_presupported == true' "$WORK/modelget2.out" >/dev/null \
  || fail "is_supported true should read back as presupported: $(cat "$WORK/modelget2.out")"
ok "models update writes is_supported and the derived pair follows"

# The one-way rule: a null is dropped, the write still answers ok, and the
# value does not return to unknown. This is what the help text claims. This
# check always follows a same-run true write above, so a regression that let
# null clear the field back to unknown would flip is_presupported to false
# right here — it does not rely on state left over from a previous run.
# setting is_supported to null expecting the field to be dropped, not cleared
echo '{"is_supported":null}' | "$CLI" models update --id "$WRITE_ID" --stdin >"$WORK/modelnull.out" 2>&1 \
  || fail "models update with a null exited non-zero"
# getting the model back expecting is_presupported to remain true
"$CLI" models get --id "$WRITE_ID" >"$WORK/modelget3.out" 2>&1
jq -e '.is_presupported == true' "$WORK/modelget3.out" >/dev/null \
  || fail "a null should have changed nothing: $(cat "$WORK/modelget3.out")"
ok "models update cannot return is_supported to unknown"

# The symptom the whole group exists to fix: tagging a model directly.
# batch-tagging the model with smoke-model
echo "{\"ids\":[\"$MODEL_ID\"],\"tags\":[\"smoke-model\"]}" \
  | "$CLI" models batch-tag --stdin >/dev/null 2>&1 \
  || fail "models batch-tag exited non-zero"
# looking up items by tag expecting the tagged model to be found
"$CLI" tags items --tag smoke-model --resource-type model >"$WORK/modeltag.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$MODEL_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/modeltag.out" >/dev/null \
  || fail "the tagged model should be findable: $(cat "$WORK/modeltag.out")"
ok "models batch-tag tags a model without duplicates merge-metadata"

# setting a display-cased tag on the Goblins folder
echo '{"path":"Goblins","tags":["Smoke Models"]}' \
  | "$CLI" models folders set --stdin >/dev/null 2>&1 \
  || fail "models folders set exited non-zero"
# listing model folders expecting the tag to list in display casing
"$CLI" models folders list >"$WORK/modelflist.out" 2>&1 \
  || fail "models folders list exited non-zero"
jq -e '[.folders[] | select(.path == "Goblins") | .tags[]] | any(. == "Smoke Models")' \
  "$WORK/modelflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/modelflist.out")"
ok "models folders set writes a tag that lists in display casing"

# downloading the model's rendered thumbnail expecting a non-empty file
"$CLI" models thumbnail --id "$MODEL_ID" --output "$WORK/mini.webp" >/dev/null 2>&1 \
  || fail "models thumbnail exited non-zero"
[ -s "$WORK/mini.webp" ] || fail "models thumbnail wrote no bytes"
ok "models thumbnail downloads the rendered image"

# An unknown field is refused client-side: exit 1, and no request is made.
# updating a model with an unknown field expecting exit 1
set +e
echo '{"is_suported":true}' | "$CLI" models update --id "$MODEL_ID" --stdin \
  >/dev/null 2>"$WORK/modeltypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown model field should exit 1, got $rc: $(cat "$WORK/modeltypo.err")"
grep -q "is_suported" "$WORK/modeltypo.err" || fail "no offending field named: $(cat "$WORK/modeltypo.err")"
ok "models update refuses an unknown field before any request"

# ---- tokens -----------------------------------------------------------------
# Requires docker/seed.sh to have run — three fixture tokens: Goblin.png directly
# under Monsters, and Ghoul.png and Skeleton.png under Monsters/Undead.
# listing tokens expecting at least the two seeded fixture tokens
"$CLI" tokens list >"$WORK/tokens.out" 2>"$WORK/tokens.err" \
  || { cat "$WORK/tokens.err" >&2; fail "tokens list exited non-zero"; }
jq -e '.total >= 2 and (.tokens | length) >= 2' "$WORK/tokens.out" >/dev/null \
  || fail "tokens list should report the seeded tokens: $(cat "$WORK/tokens.out")"
ok "tokens list returns the seeded tokens"

# listing tokens with --limit 1 expecting exactly one result
"$CLI" tokens list --limit 1 >"$WORK/tokens-limit.out" 2>&1 \
  || fail "tokens list --limit exited non-zero"
jq -e '(.tokens | length) == 1' "$WORK/tokens-limit.out" >/dev/null \
  || fail "--limit 1 should return one row: $(cat "$WORK/tokens-limit.out")"
ok "tokens list --limit bounds the page"

# Goblin sits in Monsters/, Ghoul and Skeleton in Monsters/Undead/, so path order
# and filename order disagree — see the maps check above.
# listing tokens sorted by name expecting filename order, not path order
"$CLI" tokens list --sort name >"$WORK/tokens-sort.out" 2>&1 \
  || fail "tokens list --sort exited non-zero"
jq -e '[.tokens[].filename] == ["Ghoul.png", "Goblin.png", "Skeleton.png"]' \
  "$WORK/tokens-sort.out" >/dev/null \
  || fail "--sort name should order by filename: $(cat "$WORK/tokens-sort.out")"
jq -e '[.tokens[].filename] == ["Goblin.png", "Ghoul.png", "Skeleton.png"]' \
  "$WORK/tokens.out" >/dev/null \
  || fail "the default should order by path: $(cat "$WORK/tokens.out")"
ok "tokens list --sort name orders by filename, not by path"

# extracting the Goblin.png fixture token id
TOKEN_ID=$(jq -r '.tokens[] | select(.filename == "Goblin.png") | .id' "$WORK/tokens.out")
[ -n "$TOKEN_ID" ] || fail "no Goblin.png fixture id: $(cat "$WORK/tokens.out")"

# getting the token expecting folder context fields to be present
"$CLI" tokens get --id "$TOKEN_ID" >"$WORK/tokenget.out" 2>&1 \
  || fail "tokens get exited non-zero"
jq -e 'has("folder_path") and has("folder_tags") and has("is_explicit")' \
  "$WORK/tokenget.out" >/dev/null \
  || fail "tokens get should carry folder context: $(cat "$WORK/tokenget.out")"
ok "tokens get returns folder context"

# downloading the token's rendered thumbnail expecting a non-empty file
"$CLI" tokens thumbnail --id "$TOKEN_ID" --output "$WORK/goblin.webp" >/dev/null 2>&1 \
  || fail "tokens thumbnail exited non-zero"
[ -s "$WORK/goblin.webp" ] || fail "tokens thumbnail wrote no bytes"
ok "tokens thumbnail downloads the rendered image"

# batch-tagging the token with smoke-token
echo "{\"ids\":[\"$TOKEN_ID\"],\"tags\":[\"smoke-token\"]}" \
  | "$CLI" tokens batch-tag --stdin >/dev/null 2>&1 \
  || fail "tokens batch-tag exited non-zero"
# looking up items by tag expecting the tagged token to be found
"$CLI" tags items --tag smoke-token --resource-type token >"$WORK/tokentag.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$TOKEN_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/tokentag.out" >/dev/null \
  || fail "the tagged token should be findable: $(cat "$WORK/tokentag.out")"
ok "tokens batch-tag tags a token without duplicates merge-metadata"

# setting a display-cased tag on the Monsters folder
echo '{"path":"Monsters","tags":["Smoke Tokens"]}' \
  | "$CLI" tokens folders set --stdin >/dev/null 2>&1 \
  || fail "tokens folders set exited non-zero"
# listing token folders expecting the tag to list in display casing
"$CLI" tokens folders list >"$WORK/tokenflist.out" 2>&1 \
  || fail "tokens folders list exited non-zero"
jq -e '[.folders[] | select(.path == "Monsters") | .tags[]] | any(. == "Smoke Tokens")' \
  "$WORK/tokenflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/tokenflist.out")"
ok "tokens folders set writes a tag that lists in display casing"

# An unknown field is refused client-side: exit 1, distinct from a server 422's 2.
# updating a token with an unknown field expecting exit 1
set +e
echo '{"is_explict":true}' | "$CLI" tokens update --id "$TOKEN_ID" --stdin \
  >/dev/null 2>"$WORK/tokentypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "an unknown token field should exit 1, got $rc: $(cat "$WORK/tokentypo.err")"
grep -q "is_explict" "$WORK/tokentypo.err" || fail "no offending field named: $(cat "$WORK/tokentypo.err")"
ok "tokens update refuses an unknown field before any request"

# ---- audio ------------------------------------------------------------------
# Requires docker/seed.sh to have run — two fixture tracks: Tavern.wav directly
# under Ambience, and Drums.wav under Ambience/Battle.
# listing audio expecting at least the two seeded fixture tracks
"$CLI" audio list >"$WORK/audio.out" 2>"$WORK/audio.err" \
  || { cat "$WORK/audio.err" >&2; fail "audio list exited non-zero"; }
jq -e '.total >= 2 and (.audio | length) >= 2' "$WORK/audio.out" >/dev/null \
  || fail "audio list should report the seeded tracks: $(cat "$WORK/audio.out")"
ok "audio list returns the seeded tracks"

# extracting the Tavern.wav fixture audio id
AUDIO_ID=$(jq -r '.audio[] | select(.filename == "Tavern.wav") | .id' "$WORK/audio.out")
[ -n "$AUDIO_ID" ] || fail "no Tavern.wav fixture id: $(cat "$WORK/audio.out")"

# The tag metadata audio update cannot write: duration is read from the file,
# title/artist/album are empty because the fixture carries no tags.
# checking the fixture track has a scan-derived duration and no tag title
jq -e '.audio[] | select(.filename == "Tavern.wav") | .duration > 0 and .title == ""' \
  "$WORK/audio.out" >/dev/null \
  || fail "the fixture should have a duration and no title: $(cat "$WORK/audio.out")"
ok "audio list reports scan-derived duration with empty tag metadata"

# getting the audio track expecting folder context fields to be present
"$CLI" audio get --id "$AUDIO_ID" >"$WORK/audioget.out" 2>&1 \
  || fail "audio get exited non-zero"
jq -e 'has("folder_path") and has("folder_tags") and has("has_artwork")' \
  "$WORK/audioget.out" >/dev/null \
  || fail "audio get should carry folder context: $(cat "$WORK/audioget.out")"
ok "audio get returns folder context"

# Tavern.wav carries no embedded art, but seed.sh drops a cover.png beside it
# in Ambience/ — _find_folder_artwork (indexer/metadata.py) claims a same-folder
# cover.*/folder.* image as folder art, which serve_audio_artwork falls back to
# before embedded art. This proves route, --id handling and the download path
# together, not just that some 4xx/5xx came back.
# downloading the audio track's artwork expecting a non-empty file
"$CLI" audio artwork --id "$AUDIO_ID" --output "$WORK/artwork.png" >/dev/null 2>&1 \
  || fail "audio artwork exited non-zero"
[ -s "$WORK/artwork.png" ] || fail "audio artwork wrote no bytes"
ok "audio artwork downloads the folder cover image"

# batch-tagging the audio track with smoke-audio
echo "{\"ids\":[\"$AUDIO_ID\"],\"tags\":[\"smoke-audio\"]}" \
  | "$CLI" audio batch-tag --stdin >/dev/null 2>&1 \
  || fail "audio batch-tag exited non-zero"
# looking up items by tag expecting the tagged track to be found
"$CLI" tags items --tag smoke-audio --resource-type audio >"$WORK/audiotag.out" 2>&1 \
  || fail "tags items exited non-zero"
jq -e --arg id "$AUDIO_ID" '[.. | .item_id? // empty] | any(. == $id)' "$WORK/audiotag.out" >/dev/null \
  || fail "the tagged track should be findable: $(cat "$WORK/audiotag.out")"
ok "audio batch-tag tags a track without duplicates merge-metadata"

# setting a display-cased tag on the Ambience folder
echo '{"path":"Ambience","tags":["Smoke Audio"]}' \
  | "$CLI" audio folders set --stdin >/dev/null 2>&1 \
  || fail "audio folders set exited non-zero"
# listing audio folders expecting the tag to list in display casing
"$CLI" audio folders list >"$WORK/audioflist.out" 2>&1 \
  || fail "audio folders list exited non-zero"
jq -e '[.folders[] | select(.path == "Ambience") | .tags[]] | any(. == "Smoke Audio")' \
  "$WORK/audioflist.out" >/dev/null \
  || fail "the folder tag should list in display casing: $(cat "$WORK/audioflist.out")"
ok "audio folders set writes a tag that lists in display casing"

# artist is scan-derived and AudioUpdate does not declare it, so this is refused
# client-side at exit 1 rather than reaching the server.
# updating audio with the scan-derived artist field expecting exit 1
set +e
echo '{"artist":"nope"}' | "$CLI" audio update --id "$AUDIO_ID" --stdin \
  >/dev/null 2>"$WORK/audiotypo.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "writing artist should exit 1, got $rc: $(cat "$WORK/audiotypo.err")"
grep -q "artist" "$WORK/audiotypo.err" || fail "no offending field named: $(cat "$WORK/audiotypo.err")"
ok "audio update refuses the scan-derived artist field before any request"

# --- discovery ---------------------------------------------------------------
# Read-only throughout: nothing here writes, so a re-run converges trivially.
# The fixture's indexed pages all read "grimoire-cli fixture · page N", which is
# what makes an exact page-text count assertable.
# searching page text with --limit 3 expecting exactly three results
SEARCH_JSON=$("$CLI" search --query "text:fixture" --limit 3 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq '.results | length')" -eq 3 ] \
  || fail "--limit should bound the page-text results: $SEARCH_JSON"
ok "search bounds page-text results with --limit"

# The rule the field syntax exists for: a metadata filter switches off the
# page-text search rather than narrowing it.
# searching with a title: filter expecting page-text results suppressed
SEARCH_JSON=$("$CLI" search --query "title:dsa" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search with a filter exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq '.results | length')" -eq 0 ] \
  || fail "a metadata filter should suppress page-text results: $SEARCH_JSON"
[ "$(echo "$SEARCH_JSON" | jq '.book_matches | length')" -gt 0 ] \
  || fail "title:dsa should match the DSA fixture books: $SEARCH_JSON"
echo "$SEARCH_JSON" | jq -e '.fields | index("title") != null' >/dev/null \
  || fail "the response should echo the filter it read: $SEARCH_JSON"
ok "a metadata filter suppresses page-text search and is echoed in fields"

# A typo'd prefix is deliberately not an error, so the only signal is an empty
# fields — this is the case the help text warns about.
# searching with an unrecognised field prefix expecting a literal search
SEARCH_JSON=$("$CLI" search --query "titel:dsa" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search with an unknown prefix exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq '.fields | length')" -eq 0 ] \
  || fail "an unrecognised prefix should not be read as a filter: $SEARCH_JSON"
ok "an unrecognised field prefix is searched literally"

# LIMIT -1 is unlimited in SQLite and the server has no lower bound, so the
# guard has to be here.
# searching with --limit -1 expecting the client to reject it
"$CLI" search --query "fixture" --limit -1 >/dev/null 2>&1 \
  && fail "--limit -1 should be rejected before the request"
ok "search rejects a limit the server would read as unlimited"

# listing filterable search fields expecting title to be included
FIELDS_JSON=$("$CLI" search fields 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "search fields exited non-zero"; }
echo "$FIELDS_JSON" | jq -e '.fields[] | select(.field == "title")' >/dev/null \
  || fail "search fields should list title: $FIELDS_JSON"
ok "search fields lists the filterable fields"

# listing tags expecting the tag written earlier in this run
TAGS_JSON=$("$CLI" tags list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags list exited non-zero"; }
echo "$TAGS_JSON" | jq -e '.tags[] | select(.internal == "smoke-book-alpha")' >/dev/null \
  || fail "tags list should include the tag just written: $TAGS_JSON"
ok "tags list includes a tag written earlier in this run"

# listing tags filtered to books expecting only book tags to remain
TAGS_JSON=$("$CLI" tags list --in-use-by book 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags list --in-use-by exited non-zero"; }
echo "$TAGS_JSON" | jq -e '.tags[] | select(.internal == "smoke-book-alpha")' >/dev/null \
  || fail "--in-use-by book should keep a book tag: $TAGS_JSON"
echo "$TAGS_JSON" | jq -e 'any(.tags[]; .internal == "smoke-alpha") | not' >/dev/null \
  || fail "--in-use-by book should drop a system-only tag: $TAGS_JSON"
ok "tags list narrows to one resource type"

# looking up items for an unknown tag expecting a non-zero exit
"$CLI" tags items --tag no-such-tag-smoke >/dev/null 2>&1 \
  && fail "tags items should exit non-zero for an unknown tag"
ok "tags items reports an unknown tag"

# batch-update: one good id and one bogus id must exit 3, applying the good
# one. license, not description: nothing above filters on a book's license,
# so this stays idempotent across re-runs.
# writing a batch-update input file with one good id and one bogus id
cat >"$WORK/bbatch.json" <<JSON
{"items":[{"id":"$SR4_BOOK","license":"Smoke Fixture Book License"},
          {"id":"no-such-id","license":"x"}]}
JSON
# running batch-update expecting exit 3 with the good id applied
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
# reindexing a text-layer fixture book expecting a 400 rejection
set +e
"$CLI" books reindex --id "$SR4_BOOK" >/dev/null 2>"$WORK/reindex.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "reindex on a text-layer fixture should have failed"
grep -qi "bad request" "$WORK/reindex.err" \
  || fail "reindex should have reported a 400: $(cat "$WORK/reindex.err")"
ok "reindex rejects a fixture book with a 400"

# rescanning the fixture book expecting it to be queued
"$CLI" books rescan --id "$SR4_BOOK" >"$WORK/brescan.out" 2>"$WORK/brescan.err" \
  || { cat "$WORK/brescan.err" >&2; fail "books rescan exited non-zero"; }
jq -e '.status == "rescan_queued"' "$WORK/brescan.out" >/dev/null \
  || fail "books rescan should answer rescan_queued: $(cat "$WORK/brescan.out")"
ok "books rescan queues a re-read"

# A single-book rescan sets the same running flag a full library scan uses
# (rescan_single_book in temp/grimoire's backend/routers/library/_helpers.py),
# so the library rescan below must wait for it to clear — otherwise it would
# see running=true and answer already_running instead of scan_started.
# polling scan-status until the book rescan finishes running
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
# rescanning the real Shadowrun 4 DE path expecting scan_started
"$CLI" library rescan --scope "books/Shadowrun/4 DE" \
  >"$WORK/librescan.out" 2>"$WORK/librescan.err" \
  || { cat "$WORK/librescan.err" >&2; fail "library rescan exited non-zero"; }
jq -e '.status == "scan_started"' "$WORK/librescan.out" >/dev/null \
  || fail "library rescan should answer scan_started: $(cat "$WORK/librescan.out")"
ok "library rescan starts a scoped scan"

# Wait for the scoped scan to settle before reading its counters, or a
# still-in-flight snapshot would read as a smaller, unstable number.
# polling scan-status until the scoped rescan finishes
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

# cancelling a scan when none is running expecting not_running
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
# running cleanup-missing expecting a numeric removed count per resource
CLEANUP_JSON=$("$CLI" library cleanup-missing 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cleanup-missing exited non-zero"; }
for key in books maps tokens audio systems; do
  echo "$CLEANUP_JSON" | jq -e --arg k "$key" '.removed[$k] | type == "number"' >/dev/null \
    || fail "removed.$key should be a number: $CLEANUP_JSON"
done
ok "library cleanup-missing reports a count for every resource"

# running cleanup-missing again expecting nothing left to remove
CLEANUP_JSON=$("$CLI" library cleanup-missing 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library cleanup-missing exited non-zero on the second call"; }
echo "$CLEANUP_JSON" | jq -e '[.removed[]] | add == 0' >/dev/null \
  || fail "a second cleanup should find nothing left to remove: $CLEANUP_JSON"
ok "a second library cleanup-missing removes nothing"

# ---- sidecars ---------------------------------------------------------------
# Enabling a format is what switches on export, the refresh-on-edit hook and the
# export-on-scan hook together. It cannot be switched off again from the CLI, so
# this section leaves the fixture library with .opf files; the documented reset
# removes them along with the library tree.
# getting sidecar export settings expecting the three config fields
"$CLI" sidecars settings get >"$WORK/sidecars-settings.out" 2>"$WORK/sidecars-settings.err" \
  || { cat "$WORK/sidecars-settings.err" >&2; fail "sidecars settings get exited non-zero"; }
jq -e 'has("formats") and has("covers") and has("overwrite_foreign")' \
  "$WORK/sidecars-settings.out" >/dev/null \
  || fail "sidecars settings get should carry the three fields: $(cat "$WORK/sidecars-settings.out")"
ok "sidecars settings get reads the export configuration"

# enabling the opf sidecar format expecting it echoed back
"$CLI" sidecars settings set --formats opf >"$WORK/sidecars-set.out" 2>&1 \
  || fail "sidecars settings set exited non-zero"
jq -e '.formats == ["opf"]' "$WORK/sidecars-set.out" >/dev/null \
  || fail "settings set should echo the stored formats: $(cat "$WORK/sidecars-set.out")"
ok "sidecars settings set enables a format"

# --formats is required precisely so this cannot send an empty list and switch
# the whole feature off.
# setting sidecars without --formats expecting exit 1
set +e
"$CLI" sidecars settings set --covers >/dev/null 2>"$WORK/sidecars-noformats.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "settings set without --formats should exit 1, got $rc"
ok "sidecars settings set refuses to run without --formats"

# The fixture library carries one hand-maintained sidecar (docker/seed.sh,
# Honey Heist.opf) that Grimoire correctly declines to overwrite: it lands in
# skipped_foreign and in errors, with failed staying 0. That must exit 0, not
# 3 — a foreign skip is a report, not a failure (metadata/export.py:220-230).
# Asserted on shape, failed and the exit code, never on written or an exact
# skipped_foreign count: the backfill is additive, so a second run of this
# script writes nothing new, but the foreign sidecar stays foreign forever.
# exporting sidecars expecting exit 0 with the foreign sidecar only skipped
set +e
"$CLI" sidecars export >"$WORK/sidecars-export.out" 2>"$WORK/sidecars-export.err"; rc=$?
set -e
[ "$rc" -eq 0 ] \
  || { cat "$WORK/sidecars-export.err" >&2; fail "sidecars export should exit 0 when it only skips a foreign sidecar, got $rc: $(cat "$WORK/sidecars-export.out")"; }
jq -e 'has("written") and has("skipped_missing") and has("covers") and has("read_only")
       and (.failed == 0) and (.skipped_foreign > 0)' \
  "$WORK/sidecars-export.out" >/dev/null \
  || fail "sidecars export should report a clean run with a foreign sidecar skipped: $(cat "$WORK/sidecars-export.out")"
ok "sidecars export exits 0 when it only skips a foreign sidecar"

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
# pointing the addon index at the local fixture server
FIXTURE_INDEX="http://addon-index/index.json"

# setting the addons index-url to the fixture expecting it echoed back
SETTINGS_JSON=$("$CLI" addons settings --index-url "$FIXTURE_INDEX" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons settings --index-url exited non-zero"; }
[ "$(echo "$SETTINGS_JSON" | jq -r .index_url)" = "$FIXTURE_INDEX" ] \
  || fail "addons settings did not echo back the fixture index URL: $SETTINGS_JSON"
ok "addons settings points the stack at the fixture index"

# refreshing addons expecting exactly one add-on from the fixture index
REFRESH_JSON=$("$CLI" addons refresh 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons refresh exited non-zero"; }
[ "$(echo "$REFRESH_JSON" | jq -r .count)" -eq 1 ] \
  || fail "addons refresh should report count 1, got: $REFRESH_JSON"
ok "addons refresh reaches the fixture index and reports 1 add-on"

# installing the fixture add-on expecting it enabled and runnable
INSTALL_JSON=$("$CLI" addons install --id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons install exited non-zero (digest mismatch? re-run docker/seed.sh after editing docker/addon-index/fixture-source.yml — it regenerates index.json via make-addon-index.py)"; }
[ "$(echo "$INSTALL_JSON" | jq -r .id)" = "fixture-source" ] \
  || fail "addons install returned the wrong id: $INSTALL_JSON"
[ "$(echo "$INSTALL_JSON" | jq -r .enabled)" = "true" ] \
  || fail "a freshly installed add-on should be enabled: $INSTALL_JSON"
[ "$(echo "$INSTALL_JSON" | jq -r .runnable)" = "true" ] \
  || fail "a freshly installed, script-free add-on should be runnable: $INSTALL_JSON"
ok "addons install installs the fixture add-on, enabled and runnable"

# listing addons expecting the fixture shown under installed and available
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
# listing metadata sources for the system expecting the fixture with paste support
SOURCES_JSON=$("$CLI" systems metadata-sources --id "$SR4" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-sources exited non-zero"; }
echo "$SOURCES_JSON" | jq -e '.sources[] | select(.id == "fixture-source" and .supports_paste == true)' >/dev/null \
  || fail "fixture-source should offer itself with supports_paste true: $SOURCES_JSON"
ok "systems metadata-sources lists the installed fixture add-on"

# The fixture targets game-system, so an empty list here is target filtering
# working rather than the endpoint returning nothing.
# listing metadata sources for a book expecting the game-system add-on excluded
BOOKSOURCES_JSON=$("$CLI" books metadata-sources --id "$SR4_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books metadata-sources exited non-zero"; }
[ "$(echo "$BOOKSOURCES_JSON" | jq '.sources | length')" -eq 0 ] \
  || fail "a game-system add-on must not appear as a book source: $BOOKSOURCES_JSON"
ok "books metadata-sources excludes a game-system add-on"

# searching system metadata expecting the query to default to the system name
SEARCH_JSON=$("$CLI" systems metadata-search --id "$SR4" --source-id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-search exited non-zero"; }
[ "$(echo "$SEARCH_JSON" | jq -r .query)" = "Shadowrun 4 DE" ] \
  || fail "an omitted --query should echo back the system's name: $SEARCH_JSON"
[ "$(echo "$SEARCH_JSON" | jq -r '.results[0].identity')" = "shadowrun-4-de" ] \
  || fail "the fixture record should rank first: $SEARCH_JSON"
ok "systems metadata-search defaults its query to the system name"

# fetching system metadata by identity expecting one row of each field status
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

# fetching system metadata via --paste expecting it to resolve the same identity
PASTE_JSON=$("$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --paste "https://fixture.test/systems/shadowrun-4-de" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems metadata-fetch --paste exited non-zero"; }
[ "$(echo "$PASTE_JSON" | jq -r .identity)" = "shadowrun-4-de" ] \
  || fail "--paste should resolve to the same identity the search returned: $PASTE_JSON"
ok "systems metadata-fetch --paste resolves a source URL to an identity"

# Fetching is a read. The field it offered must still be empty afterwards —
# and the family filter assertion earlier depends on it.
# getting the system back expecting system_family to remain unwritten
sysget --id "$SR4"
[ -z "$(echo "$GET_JSON" | jq -r '.system_family // ""')" ] \
  || fail "metadata-fetch must not have written system_family: $(echo "$GET_JSON" | jq -r .system_family)"
ok "metadata-fetch left the system unchanged"

# fetching metadata with neither --identity nor --paste expecting exit 1
set +e
"$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source >/dev/null 2>"$WORK/fetchargs.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "metadata-fetch with neither --identity nor --paste should exit 1, got $rc"
grep -q -- "--identity" "$WORK/fetchargs.err" \
  || fail "no mention of --identity: $(cat "$WORK/fetchargs.err")"
# fetching metadata with both --identity and --paste expecting exit 1
set +e
"$CLI" systems metadata-fetch --id "$SR4" --source-id fixture-source \
  --identity shadowrun-4-de --paste "https://fixture.test/systems/shadowrun-4-de" \
  >/dev/null 2>"$WORK/fetchboth.err"; rc=$?
set -e
[ "$rc" -eq 1 ] || fail "metadata-fetch with both --identity and --paste should exit 1, got $rc"
grep -q -- "--identity" "$WORK/fetchboth.err" \
  || fail "no mention of --identity: $(cat "$WORK/fetchboth.err")"
ok "metadata-fetch requires exactly one of --identity and --paste"

# disabling the fixture add-on expecting enabled to read back false
UPDATE_JSON=$("$CLI" addons update --id fixture-source --enabled false 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons update exited non-zero"; }
[ "$(echo "$UPDATE_JSON" | jq -r .enabled)" = "false" ] \
  || fail "addons update --enabled false did not disable the add-on: $UPDATE_JSON"
ok "addons update disables the fixture add-on"

# With a single fixture at a single version there is nothing to upgrade, so
# this only exercises the plumbing (a refresh, then an empty pass) — it is NOT
# coverage of upgrade-all's skip-and-continue behaviour, which needs an add-on
# that actually fails to upgrade. Asserted honestly as the empty case.
# running upgrade-all expecting nothing to update or fail
UPGRADE_JSON=$("$CLI" addons upgrade-all 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons upgrade-all exited non-zero"; }
[ "$(echo "$UPGRADE_JSON" | jq '.updated | length')" -eq 0 ] \
  || fail "nothing should have needed upgrading: $UPGRADE_JSON"
[ "$(echo "$UPGRADE_JSON" | jq '.failed | length')" -eq 0 ] \
  || fail "nothing should have failed to upgrade: $UPGRADE_JSON"
ok "addons upgrade-all exits 0 with nothing to upgrade"

# uninstalling the fixture add-on expecting status ok
UNINSTALL_JSON=$("$CLI" addons uninstall --id fixture-source 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons uninstall exited non-zero"; }
[ "$(echo "$UNINSTALL_JSON" | jq -r .status)" = "ok" ] \
  || fail "addons uninstall should answer {\"status\":\"ok\"}: $UNINSTALL_JSON"
# listing addons again expecting the fixture no longer installed
ADDONLIST_JSON=$("$CLI" addons list 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons list exited non-zero"; }
echo "$ADDONLIST_JSON" | jq -e '.installed[] | select(.id == "fixture-source")' >/dev/null \
  && fail "fixture-source should no longer be installed: $(echo "$ADDONLIST_JSON" | jq -c .installed)"
ok "addons uninstall removes the fixture add-on"

# Restore the published index so the run leaves no state behind, taken from
# default_index_url above rather than hard-coded.
# restoring the default addon index-url expecting it echoed back
RESTORE_JSON=$("$CLI" addons settings --index-url "$DEFAULT_INDEX_URL" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons settings restore exited non-zero"; }
[ "$(echo "$RESTORE_JSON" | jq -r .index_url)" = "$DEFAULT_INDEX_URL" ] \
  || fail "addons settings did not restore the default index URL: $RESTORE_JSON"
ok "addons settings restores the default index URL"

# setting addons with no flags expecting exit 1 naming the required flags
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
# passing --token to systems list, expecting it rejected as an unknown option
set +e
"$CLI" systems list --token whatever >"$WORK/notoken.out" 2>"$WORK/notoken.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "--token should be rejected as an unknown option, got $rc"
ok "--token is not an option"

# A config file that is not valid JSON must not take the CLI down with it, and
# logging in again must be enough to recover — no hand-editing, no rm. This runs
# last because it ends by restoring the config the earlier checks depend on.
# writing an invalid JSON config file
printf '{not json' > "$CONFIG"
# running systems list against the corrupt config, expecting a readable failure
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

# logging in again to repair the corrupt config, expecting the CLI to work again
printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
  >/dev/null 2>"$WORK/relogin.err" \
  || { cat "$WORK/relogin.err" >&2; fail "login should recover from a corrupt config"; }
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "login did not repair the config: $(cat "$CONFIG")"
syslist
[ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "the CLI should work again after re-login"
ok "login repairs a corrupt config"

# login is the one command that still takes --server, and now the one that also
# reads GRIMOIRE_SERVER. Without the variable an unattended login has no way in:
# the prompt's ReadLine returns null and the command exits 1.
# The stored server is broken first so the check below proves login wrote what
# the environment named: the earlier login already stored $SERVER, so without
# this the assertion would hold whether or not the variable was read at all.
# sabotaging the stored server so the next check proves login rewrote it
jq '.server = "http://127.0.0.1:1"' "$CONFIG" >"$WORK/config.sabotaged" \
  && mv "$WORK/config.sabotaged" "$CONFIG"
# logging in via GRIMOIRE_SERVER only, expecting it stored the right server
printf 'admin' | GRIMOIRE_SERVER="$SERVER" "$CLI" login --username admin --password-stdin \
  >/dev/null 2>"$WORK/envlogin.err" \
  || { cat "$WORK/envlogin.err" >&2; fail "login should take its server from GRIMOIRE_SERVER"; }
jq -e --arg s "$SERVER" '.server == $s' "$CONFIG" >/dev/null \
  || fail "login did not store the server from GRIMOIRE_SERVER: $(cat "$CONFIG")"
ok "login takes its server from GRIMOIRE_SERVER"

# The config is replaced, not rewritten in place: no temporary survives, and the
# file holding the session's tokens is readable only by its owner.
# checking no temp config file remains and the config stays mode 600
[ -z "$(find "$(dirname "$CONFIG")" -name '*.tmp' -print -quit)" ] \
  || fail "a temporary config file was left behind: $(ls "$(dirname "$CONFIG")")"
[ "$(stat -c '%a' "$CONFIG")" = "600" ] \
  || fail "the config should be owner-only, got $(stat -c '%a' "$CONFIG")"
ok "config writes leave no temporary file and stay owner-only"

# A server that is simply down is the commonest failure there is, and until
# recently the only one that escaped as a raw .NET trace: the exception left the
# command action, and System.CommandLine's own pipeline printed it and returned 1
# before Program's handler could run. Exit 2 is asserted alongside the message —
# 1 means a local/config problem in this CLI, which a dead server is not.
#
# The dead address goes in through GRIMOIRE_SERVER rather than the config file:
# CI exports that variable for the whole job, and the env tier outranks the file,
# so editing the file here would be overridden and the command would succeed.
# pointing at an unreachable server, expecting exit 2 with no stack trace
set +e
GRIMOIRE_SERVER=http://127.0.0.1:1 "$CLI" systems list >"$WORK/down.out" 2>"$WORK/down.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "an unreachable server should exit 2, got $rc: $(cat "$WORK/down.err")"
grep -qi "cannot reach" "$WORK/down.err" \
  || fail "no readable message for an unreachable server: $(cat "$WORK/down.err")"
grep -qi "at System\.\|Unhandled exception" "$WORK/down.err" \
  && fail "an unreachable server leaked a stack trace: $(cat "$WORK/down.err")"
[ ! -s "$WORK/down.out" ] || fail "stdout should stay empty when the server is unreachable"
ok "an unreachable server fails readably with no stack trace"


# Grimoire does not merely refuse a refresh token it has already rotated away:
# it reads the replay as theft and revokes the session. Reaching that state on
# purpose is the only way to check the failure path without waiting out a
# 30-minute access token.
# reading the dev SECRET_KEY, skipping this case if not available
DEV_SECRET=$(docker inspect docker-grimoire-1 \
  --format '{{range .Config.Env}}{{println .}}{{end}}' 2>/dev/null \
  | sed -n 's/^SECRET_KEY=//p')
if [ "$DEV_SECRET" != "dev-only-not-a-real-secret" ]; then
  echo "  skip: the retired-session case needs the dev SECRET_KEY" >&2
else
  # reading the stored refresh token out of the config
  STORED=$(jq -r '.refreshToken // empty' "$CONFIG")
  [ -n "$STORED" ] || fail "login stored no refresh token: $(cat "$CONFIG")"
  # rotating the refresh token directly, retiring the CLI's cookie
  curl -sf -X POST "$SERVER/api/auth/refresh" \
    -H "Cookie: grimoire_refresh=$STORED" -o /dev/null \
    || fail "could not rotate the refresh token out from under the CLI"
  # An expired but correctly signed token: TokenHelper reads its exp, finds it
  # spent, and the CLI refreshes before sending — with the cookie just retired.
  # building an expired but correctly signed jwt to trigger a refresh
  STALE_JWT=$(python3 -c "
import base64,hmac,hashlib,json,time
def b64(b): return base64.urlsafe_b64encode(b).rstrip(b'=')
h=b64(json.dumps({'alg':'HS256','typ':'JWT'},separators=(',',':')).encode())
n=int(time.time())
p=b64(json.dumps({'sub':'x','username':'admin','role':'admin','iat':n-3600,'jti':'p','exp':n-60,'sid':'p'},separators=(',',':')).encode())
print((h+b'.'+p+b'.'+b64(hmac.new(b'$DEV_SECRET',h+b'.'+p,hashlib.sha256).digest())).decode())")
  # writing the stale signed token into the config
  jq --arg t "$STALE_JWT" '.accessToken = $t' "$CONFIG" >"$WORK/retired.json"
  mv "$WORK/retired.json" "$CONFIG"
  # running systems list with the retired token, expecting exit 2
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
  # logging in again to restore a working session
  printf 'admin' | "$CLI" login --server "$SERVER" --username admin --password-stdin \
    >/dev/null 2>"$WORK/relogin2.err" \
    || { cat "$WORK/relogin2.err" >&2; fail "login should recover a revoked session"; }
  syslist
  [ "$COUNT" -eq "$EXPECTED_SYSTEMS" ] || fail "the CLI should work again after re-login"
  ok "login recovers a revoked session"
fi

# --- duplicates -------------------------------------------------------------
# Read-only except for a dismiss/undismiss round trip and a link/unlink round
# trip on two fixture books, both of which return the fixture to its prior
# state, so a re-run converges. Three commands are deliberately not exercised:
#   delete   - irreversible; no fixture item's loss could be undone by a re-run.
#   scan     - starts a real background detection pass whose completion is
#              nondeterministic, and would invalidate the cancel-scan
#              "not_running" and groups assertions in the same run.
#   promote  - needs a family to promote within, and unwinding a swapped
#              parent/child adds two more writes for a path the link/unlink
#              round trip already covers.
# Extends the files-block trap (line 162) rather than replacing it: trap bodies
# are single-quoted, so $DUP_CHILD/$DUP_DISMISSAL are expanded when the trap
# fires, not now — harmless while still unset, and a real cleanup once the
# dismiss/link calls set them.
# registering cleanup for the dismissal, link and uploaded fixture files
trap '"$CLI" duplicates undismiss --id "${DUP_DISMISSAL:-}" >/dev/null 2>&1 || true; "$CLI" duplicates unlink --resource-type book --ids "${DUP_CHILD:-}" >/dev/null 2>&1 || true; "$CLI" files delete --path "books/$SMOKE_DIR" --confirm-name "$SMOKE_DIR" --delete-files >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

# checking scan status, expecting a running field
DUP_JSON=$("$CLI" duplicates scan-status 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates scan-status exited non-zero"; }
echo "$DUP_JSON" | jq -e 'has("running")' >/dev/null \
  || fail "scan-status should report running: $DUP_JSON"
ok "duplicates scan-status reports the detection state"

# cancelling the scan, expecting not_running on an idle stack
DUP_JSON=$("$CLI" duplicates cancel-scan 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates cancel-scan exited non-zero"; }
[ "$(echo "$DUP_JSON" | jq -r .status)" = "not_running" ] \
  || fail "cancel-scan should report not_running on an idle stack: $DUP_JSON"
ok "duplicates cancel-scan reports an idle scanner"

# listing duplicate groups, expecting a groups listing
DUP_JSON=$("$CLI" duplicates groups 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates groups exited non-zero"; }
echo "$DUP_JSON" | jq -e 'has("groups")' >/dev/null \
  || fail "groups should return a listing: $DUP_JSON"
ok "duplicates groups lists candidate groups"

# The server clamps nothing here: le=200 guards the ceiling, so the floor is
# ours to refuse.
# requesting groups with limit 0 and -1, expecting both refused
"$CLI" duplicates groups --limit 0 >/dev/null 2>&1 \
  && fail "--limit 0 should be rejected before the request"
"$CLI" duplicates groups --limit -1 >/dev/null 2>&1 \
  && fail "--limit -1 should be rejected before the request"
ok "duplicates groups refuses a limit the server would not"

# listing dismissals, expecting a dismissals listing
DUP_JSON=$("$CLI" duplicates dismissals 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates dismissals exited non-zero"; }
echo "$DUP_JSON" | jq -e 'has("dismissals")' >/dev/null \
  || fail "dismissals should return a listing: $DUP_JSON"
ok "duplicates dismissals lists dismissed groups"

# Two fixture books, chosen by title so the pair is stable across runs.
# listing books to find the DSA5 parent/child pair by title
booklist
DUP_PARENT=$(echo "$LIST_JSON" | jq -r '.books[] | select(.title == "DSA5 Regelwerk") | .id')
DUP_CHILD=$(echo "$LIST_JSON" | jq -r '.books[] | select(.title == "DSA5 Errata") | .id')
[ -n "$DUP_PARENT" ] && [ -n "$DUP_CHILD" ] \
  || fail "the DSA5 fixture books are needed for the variant round trip"

# comparing the parent and child, expecting two items back
DUP_JSON=$("$CLI" duplicates compare --resource-type book --ids "$DUP_PARENT" "$DUP_CHILD" \
  2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates compare exited non-zero"; }
[ "$(echo "$DUP_JSON" | jq '.items | length')" -eq 2 ] \
  || fail "compare should return both items: $DUP_JSON"
ok "duplicates compare returns two copies side by side"

# comparing with a single id, expecting it refused
"$CLI" duplicates compare --resource-type book --ids "$DUP_PARENT" >/dev/null 2>&1 \
  && fail "compare should refuse a single id"
ok "duplicates compare refuses fewer than two items"

# dismiss/dismissals/undismiss round trip. Runs before the link/unlink pair
# below: while $DUP_CHILD is a variant it is hidden from listings.
# dismissing the parent/child pair, expecting an id back
DUP_JSON=$("$CLI" duplicates dismiss --resource-type book --member-ids "$DUP_PARENT" "$DUP_CHILD" \
  --note "smoke test" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates dismiss exited non-zero"; }
DUP_DISMISSAL=$(echo "$DUP_JSON" | jq -r .id)
[ -n "$DUP_DISMISSAL" ] || fail "dismiss should return an id: $DUP_JSON"
ok "duplicates dismiss marks a group as not duplicates"

# listing dismissals, expecting the new dismissal to appear
DUP_JSON=$("$CLI" duplicates dismissals 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates dismissals exited non-zero"; }
echo "$DUP_JSON" | jq -e --arg id "$DUP_DISMISSAL" '.dismissals | any(.id == $id)' >/dev/null \
  || fail "dismissals should list the new dismissal: $DUP_JSON"
ok "duplicates dismissals lists the new dismissal"

# undismissing the group, expecting it removed from the dismissals list
"$CLI" duplicates undismiss --id "$DUP_DISMISSAL" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "duplicates undismiss exited non-zero"; }
DUP_JSON=$("$CLI" duplicates dismissals 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates dismissals exited non-zero"; }
echo "$DUP_JSON" | jq -e --arg id "$DUP_DISMISSAL" '.dismissals | any(.id == $id) | not' >/dev/null \
  || fail "undismiss should remove the dismissal: $DUP_JSON"
ok "duplicates undismiss makes the group findable again"

# title is already set on both books, so without --overwrite the server
# copies nothing (commit is guarded by `if updated:`) — read-only in effect.
# merging metadata with title already set and no --overwrite, expecting no writes
DUP_JSON=$("$CLI" duplicates merge-metadata --resource-type book --source-id "$DUP_PARENT" \
  --target-id "$DUP_CHILD" --fields title 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates merge-metadata exited non-zero"; }
[ "$(echo "$DUP_JSON" | jq '.updated | length')" -eq 0 ] \
  || fail "merge-metadata without --overwrite should write nothing: $DUP_JSON"
echo "$DUP_JSON" | jq -e '.skipped | index("title") != null' >/dev/null \
  || fail "merge-metadata should skip the already-set title: $DUP_JSON"
ok "duplicates merge-metadata skips a field already set on the target"

# A bogus child is reported per-child, so the request succeeds and exits 3.
# writing a link request naming a bogus child id
printf '{"resource_type":"book","parent_id":"%s","children":[{"id":"no-such-id","kind":"other","label":""}]}' \
  "$DUP_PARENT" >"$WORK/dup-bad.json"
# linking with a bogus child id, expecting exit 3 and one reported error
set +e
"$CLI" duplicates link --input "$WORK/dup-bad.json" >"$WORK/dup-bad.out" 2>"$WORK/cli.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "a rejected child should exit 3, got $rc: $(cat "$WORK/cli.err")"
jq -e '.errors | length == 1 and .[0].id == "no-such-id"' "$WORK/dup-bad.out" >/dev/null \
  || fail "the bogus child should be the only error: $(cat "$WORK/dup-bad.out")"
ok "duplicates link reports a rejected child and exits 3"

# writing a link request naming the parent and a valid child variant
printf '{"resource_type":"book","parent_id":"%s","children":[{"id":"%s","kind":"version","label":"smoke variant"}]}' \
  "$DUP_PARENT" "$DUP_CHILD" >"$WORK/dup-link.json"
# linking the child under the parent as a variant, expecting no errors
DUP_JSON=$("$CLI" duplicates link --input "$WORK/dup-link.json" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates link exited non-zero"; }
echo "$DUP_JSON" | jq -e --arg id "$DUP_CHILD" '.linked | index($id) != null' >/dev/null \
  || fail "link should name the child it linked: $DUP_JSON"
[ "$(echo "$DUP_JSON" | jq '.errors | length')" -eq 0 ] \
  || fail "link should report no errors: $DUP_JSON"
ok "duplicates link files a book under a parent as its variant"

# unlinking the child, expecting it freed back to standalone
DUP_JSON=$("$CLI" duplicates unlink --resource-type book --ids "$DUP_CHILD" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "duplicates unlink exited non-zero"; }
echo "$DUP_JSON" | jq -e --arg id "$DUP_CHILD" '.unlinked | index($id) != null' >/dev/null \
  || fail "unlink should free the child again: $DUP_JSON"
ok "duplicates unlink promotes the variant back to standalone"

# The refusal is the CLI's: with neither flag the server answers 200 {"unlinked":[]}.
# unlinking with neither --ids nor --parent-id, expecting it refused
"$CLI" duplicates unlink --resource-type book >/dev/null 2>&1 \
  && fail "unlink with neither --ids nor --parent-id should be refused"
ok "duplicates unlink refuses a call the server would answer as a no-op"

# logs: the cursor contract is the whole point of the command, so the idle poll
# is asserted rather than just a non-empty page. max_seq is buffer-wide, so a
# filter that matches nothing still advances it — which is what lets a caller
# poll on --level error without losing its place.
# listing 5 log entries, expecting entries and a positive cursor
LOGS_JSON=$("$CLI" logs --limit 5 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs exited non-zero"; }
echo "$LOGS_JSON" | jq -e '.entries | length > 0' >/dev/null \
  || fail "logs should return entries on a seeded stack: $LOGS_JSON"
echo "$LOGS_JSON" | jq -e '.max_seq > 0' >/dev/null \
  || fail "logs should report a cursor: $LOGS_JSON"
ok "logs returns a page and a cursor"

# A page is emitted oldest-first, so seq ascends within it.
# checking the same page's entries are ordered by ascending seq
echo "$LOGS_JSON" | jq -e '.entries | length > 1' >/dev/null \
  || fail "need more than one entry to prove ordering: $LOGS_JSON"
echo "$LOGS_JSON" | jq -e '[.entries[].seq] == ([.entries[].seq] | sort)' >/dev/null \
  || fail "a logs page should be ordered oldest-first: $LOGS_JSON"
ok "logs orders a page oldest-first"

# debug is the whole buffer and info a strict subset of it, so a server that
# ignored --level would answer both with the same total. Strictly greater is
# what distinguishes a working filter from an ignored parameter.
# listing debug then info logs, expecting debug's total strictly higher
DEBUG_JSON=$("$CLI" logs --level debug --limit 1 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs --level debug exited non-zero"; }
INFO_JSON=$("$CLI" logs --level info --limit 1 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs --level info exited non-zero"; }
DEBUG_TOTAL=$(echo "$DEBUG_JSON" | jq -r .total)
INFO_TOTAL=$(echo "$INFO_JSON" | jq -r .total)
[ "$DEBUG_TOTAL" -gt "$INFO_TOTAL" ] \
  || fail "--level debug should total strictly more than info: $DEBUG_TOTAL vs $INFO_TOTAL"
ok "logs --level narrows the total"

# total alone cannot show the filter reached the entries, so check the page
# carries nothing below the level asked for. info rather than warning: the
# smoke run's own requests guarantee INFO entries, and an empty page would
# make the set-difference assertion vacuously true.
# listing info-level logs, expecting no entry below info level
INFO_PAGE=$("$CLI" logs --level info --limit 50 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs --level info exited non-zero"; }
echo "$INFO_PAGE" | jq -e '.entries | length > 0' >/dev/null \
  || fail "expected at least one info entry to check filtering: $INFO_PAGE"
echo "$INFO_PAGE" | jq -e '[.entries[].level] - ["INFO","WARNING","ERROR","CRITICAL"] == []' >/dev/null \
  || fail "--level info should return no DEBUG entry: $INFO_PAGE"
ok "logs --level filters the entries, not just the total"

# The idle poll: nothing is newer than max_seq, and the cursor does not move.
# polling logs after the current max_seq, expecting an empty page
MAX_SEQ=$(echo "$LOGS_JSON" | jq -r .max_seq)
POLL_JSON=$("$CLI" logs --after-seq "$MAX_SEQ" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "logs --after-seq exited non-zero"; }
echo "$POLL_JSON" | jq -e '.entries == []' >/dev/null \
  || fail "--after-seq max_seq should return no entries: $POLL_JSON"
ok "logs --after-seq at the cursor returns an empty page"

# requesting an unknown log level, expecting it refused
"$CLI" logs --level trace >/dev/null 2>&1 \
  && fail "logs should refuse a level the server does not declare"
ok "logs refuses an unknown level"

# --- tag writes --------------------------------------------------------------
# Fixed names, invented by this script and deleted at the end, so a re-run
# converges. Nothing here touches a fixture's own tags.
# creating a tag, expecting a lowercased internal key and the entered display
CREATE_JSON=$("$CLI" tags create --value "Smoke Alpha Tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags create exited non-zero"; }
[ "$(echo "$CREATE_JSON" | jq -r .internal)" = "smoke alpha tag" ] \
  || fail "create should lowercase the internal key: $CREATE_JSON"
[ "$(echo "$CREATE_JSON" | jq -r .display)" = "Smoke Alpha Tag" ] \
  || fail "create should keep the entered casing: $CREATE_JSON"
ok "tags create returns the new tag's key and display"

# creating the same tag again by internal key, expecting the display unchanged
AGAIN_JSON=$("$CLI" tags create --value "smoke alpha tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags create is not idempotent"; }
[ "$(echo "$AGAIN_JSON" | jq -r .display)" = "Smoke Alpha Tag" ] \
  || fail "a second create should not rewrite the display: $AGAIN_JSON"
ok "tags create is idempotent by internal key"

# The rename that re-keys: the issue this was built from claimed it could not.
# renaming the tag, expecting its internal key to move with the display
RENAME_JSON=$("$CLI" tags rename --tag "smoke alpha tag" --display "Smoke Renamed Tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags rename exited non-zero"; }
[ "$(echo "$RENAME_JSON" | jq -r .internal)" = "smoke renamed tag" ] \
  || fail "rename should re-key the tag: $RENAME_JSON"
ok "tags rename moves the internal key with the display"

# creating a second tag as the merge source
"$CLI" tags create --value "Smoke Beta Tag" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "tags create exited non-zero for the merge source"; }
# merging the beta tag into the renamed tag, expecting beta gone from listing
MERGE_JSON=$("$CLI" tags merge --tag "smoke beta tag" --into "smoke renamed tag" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "tags merge exited non-zero"; }
[ "$(echo "$MERGE_JSON" | jq -r .internal)" = "smoke renamed tag" ] \
  || fail "merge should return the survivor: $MERGE_JSON"
"$CLI" tags list 2>"$WORK/cli.err" | jq -e '[.tags[].internal] | index("smoke beta tag") == null' >/dev/null \
  || fail "the merged-away tag should be gone from the listing"
ok "tags merge folds the source into the target"

# merging a tag into itself, expecting it refused
"$CLI" tags merge --tag "smoke renamed tag" --into "smoke renamed tag" >/dev/null 2>&1 \
  && fail "merging a tag into itself should fail"
ok "tags merge refuses a self-merge"

# deleting the renamed tag, expecting it gone with no response body
"$CLI" tags delete --tag "smoke renamed tag" >"$WORK/tagdel.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "tags delete exited non-zero"; }
[ ! -s "$WORK/tagdel.out" ] || [ "$(tr -d '[:space:]' <"$WORK/tagdel.out")" = "" ] \
  || fail "204 should print nothing: $(cat "$WORK/tagdel.out")"
"$CLI" tags list 2>"$WORK/cli.err" | jq -e '[.tags[].internal] | index("smoke renamed tag") == null' >/dev/null \
  || fail "the deleted tag should be gone from the listing"
ok "tags delete removes the tag and prints no body"

# deleting a tag that does not exist, expecting it refused
"$CLI" tags delete --tag "no-such-smoke-tag" >/dev/null 2>&1 \
  && fail "deleting a tag that does not exist should fail"
ok "tags delete refuses an unknown tag"

# --- vocabulary writes -------------------------------------------------------
# Fixed names, invented here and deleted before the block ends, so a re-run
# converges. Nothing here touches a built-in entry or a fixture's own metadata.
# looping over each vocabulary type to round-trip create and delete
for VOCAB in genres licenses parent-systems system-families; do
  case "$VOCAB" in
    genres) LISTKEY=genres ;;
    licenses) LISTKEY=licenses ;;
    parent-systems) LISTKEY=parent_systems ;;
    system-families) LISTKEY=families ;;
  esac
  # creating a smoke entry, expecting the name echoed and marked non-default
  CREATED=$("$CLI" "$VOCAB" create --name "Smoke Vocab" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "$VOCAB create exited non-zero"; }
  [ "$(echo "$CREATED" | jq -r .name)" = "Smoke Vocab" ] \
    || fail "$VOCAB create should echo the name: $CREATED"
  [ "$(echo "$CREATED" | jq -r .is_default)" = "false" ] \
    || fail "$VOCAB create should mark the entry custom: $CREATED"
  VID=$(echo "$CREATED" | jq -r .id)

  # creating a case-insensitive duplicate name, expecting it refused
  "$CLI" "$VOCAB" create --name "smoke vocab" >/dev/null 2>&1 \
    && fail "$VOCAB create should refuse a case-insensitive duplicate"

  # deleting the created entry, expecting removed_usage 0 since unused
  DELETED=$("$CLI" "$VOCAB" delete --id "$VID" 2>"$WORK/cli.err") \
    || { cat "$WORK/cli.err" >&2; fail "$VOCAB delete exited non-zero"; }
  [ "$(echo "$DELETED" | jq -r .removed_usage)" = "0" ] \
    || fail "$VOCAB delete of an unused entry should report no usage: $DELETED"
  # listing the vocabulary, expecting the deleted entry gone
  "$CLI" "$VOCAB" list 2>"$WORK/cli.err" \
    | jq -e --arg k "$LISTKEY" '[.[$k][].name] | index("Smoke Vocab") == null' >/dev/null \
    || fail "$VOCAB should no longer list the deleted entry"
  ok "$VOCAB create and delete round-trip"
done

# dice-materials carries an extra field, so it is checked on its own rather than
# in the loop above.
# creating a dice material with --group, expecting the group kept and clean delete
DICE=$("$CLI" dice-materials create --name "Smoke Vocab" --group "Smoke" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "dice-materials create exited non-zero"; }
[ "$(echo "$DICE" | jq -r .group)" = "Smoke" ] \
  || fail "dice-materials create should keep the group it was given: $DICE"
DICE_ID=$(echo "$DICE" | jq -r .id)
"$CLI" dice-materials delete --id "$DICE_ID" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "dice-materials delete exited non-zero"; }
ok "dice-materials create keeps its group and deletes cleanly"

# creating a dice material without --group, expecting the server's Custom default
DICE=$("$CLI" dice-materials create --name "Smoke Vocab" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "dice-materials create exited non-zero"; }
[ "$(echo "$DICE" | jq -r .group)" = "Custom" ] \
  || fail "an omitted --group should leave the server's Custom default: $DICE"
"$CLI" dice-materials delete --id "$(echo "$DICE" | jq -r .id)" >/dev/null 2>&1 \
  || fail "dice-materials delete exited non-zero"
ok "an omitted --group leaves the server's default"

# The genre cascade: a child goes with its parent, and no other vocabulary has
# this behaviour to check.
# creating a parent genre for the cascade check
PARENT=$("$CLI" genres create --name "Smoke Parent" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "genres create exited non-zero"; }
PARENT_ID=$(echo "$PARENT" | jq -r .id)
# creating a child genre under the parent
CHILD=$("$CLI" genres create --name "Smoke Child" --parent-id "$PARENT_ID" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "genres create --parent-id exited non-zero"; }
[ "$(echo "$CHILD" | jq -r .parent_id)" = "$PARENT_ID" ] \
  || fail "the child should carry its parent's id: $CHILD"
# deleting the parent genre, expecting the child genre to cascade away
"$CLI" genres delete --id "$PARENT_ID" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "genres delete exited non-zero"; }
"$CLI" genres list 2>"$WORK/cli.err" \
  | jq -e '[.genres[].name] | index("Smoke Child") == null' >/dev/null \
  || fail "deleting a parent genre should take its children with it"
ok "deleting a parent genre cascades to its children"

# creating a genre with an unknown parent id, expecting it refused
"$CLI" genres create --name "Smoke Orphan" --parent-id "no-such-genre" >/dev/null 2>&1 \
  && fail "genres create should refuse an unknown --parent-id"
ok "genres create refuses an unknown --parent-id"

# deleting a license id that does not exist, expecting it refused
"$CLI" licenses delete --id "no-such-license" >/dev/null 2>&1 \
  && fail "deleting an entry that does not exist should fail"
ok "vocabulary delete refuses an unknown id"

# --- small completions -------------------------------------------------------
# Downloads land in $WORK, a fresh mktemp dir per run, so nothing needs cleaning
# up and a re-run starts from the same place.
# reading library stats, expecting book/map counts and consistent sizes
STATS_JSON=$("$CLI" library stats 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "library stats exited non-zero"; }
echo "$STATS_JSON" | jq -e 'has("game_systems") and has("books") and has("total_size_mb") and has("library_size_mb")' >/dev/null \
  || fail "library stats is missing a documented key: $STATS_JSON"
echo "$STATS_JSON" | jq -e '.total_size_mb <= .library_size_mb' >/dev/null \
  || fail "total_size_mb is books only and cannot exceed library_size_mb: $STATS_JSON"
echo "$STATS_JSON" | jq -e '.books > 0 and .maps > 0' >/dev/null \
  || fail "the seeded fixtures should show books and maps: $STATS_JSON"
ok "library stats reports both size fields"

# The four asset downloads, one per collection.
# fetching one fixture id each from maps, tokens, models and audio
SC_MAP=$("$CLI" maps list 2>/dev/null | jq -r '.maps[0].id')
SC_TOKEN=$("$CLI" tokens list 2>/dev/null | jq -r '.tokens[0].id')
SC_MODEL=$("$CLI" models list 2>/dev/null | jq -r '.models[0].id')
SC_AUDIO=$("$CLI" audio list 2>/dev/null | jq -r '.audio[0].id')
for pair in "maps:$SC_MAP" "tokens:$SC_TOKEN" "models:$SC_MODEL" "audio:$SC_AUDIO"; do
  GROUP=${pair%%:*}; ITEM=${pair#*:}
  [ -n "$ITEM" ] && [ "$ITEM" != "null" ] || fail "no $GROUP fixture to download"
  # downloading the asset, expecting a non-empty file and a byte count
  "$CLI" "$GROUP" file --id "$ITEM" --output "$WORK/$GROUP.bin" >"$WORK/dl.out" 2>"$WORK/cli.err" \
    || { cat "$WORK/cli.err" >&2; fail "$GROUP file exited non-zero"; }
  [ -s "$WORK/$GROUP.bin" ] || fail "$GROUP file wrote an empty file"
  [ "$(jq -r .bytes "$WORK/dl.out")" -gt 0 ] \
    || fail "$GROUP file should report a byte count: $(cat "$WORK/dl.out")"
  ok "$GROUP file downloads the asset and reports its size"
done

# rendering page 1 of the map, expecting a non-empty image
"$CLI" maps page --id "$SC_MAP" --page 1 --output "$WORK/page1.webp" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "maps page exited non-zero"; }
[ -s "$WORK/page1.webp" ] || fail "maps page wrote an empty file"
ok "maps page renders page 1"

# Every fixture map is a raster, so the export is the success path and the two
# /vtt/ getters are the refusal path.
# exporting the map to a Universal VTT file, expecting an image and resolution
"$CLI" maps vtt export --id "$SC_MAP" --output "$WORK/map.uvtt" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "maps vtt export exited non-zero"; }
jq -e 'has("image") and has("resolution")' "$WORK/map.uvtt" >/dev/null \
  || fail "a .uvtt export should carry an image and a resolution: $(head -c 200 "$WORK/map.uvtt")"
ok "maps vtt export builds a Universal VTT file from a raster map"

# fetching a non-VTT map's vtt data, expecting it refused
"$CLI" maps vtt data --id "$SC_MAP" >/dev/null 2>&1 \
  && fail "maps vtt data should refuse a map that is not a Universal VTT"
ok "maps vtt data refuses a non-VTT map"

# fetching a non-VTT map's vtt image, expecting it refused
"$CLI" maps vtt image --id "$SC_MAP" --output - >/dev/null 2>&1 \
  && fail "maps vtt image should refuse a map that is not a Universal VTT"
ok "maps vtt image refuses a non-VTT map"

# systems cover from-source. Das Schwarze Auge is a container system that
# carries no uploaded cover, so an unconditional delete afterwards restores
# the exact prior state and a re-run converges — but only as long as that
# stays true, so the precondition is checked here rather than just asserted
# in a comment: a cover_image that were ever non-empty would mean this write
# clobbers a real cover and the delete below would then destroy it instead of
# restoring it.
# finding Das Schwarze Auge, checking it has no cover_image set yet
syslist
COVER_SRC_SYS=$(echo "$LIST_JSON" | jq -r '.[] | select(.name == "Das Schwarze Auge") | .id')
[ -n "$COVER_SRC_SYS" ] || fail "no Das Schwarze Auge fixture for cover from-source"
sysget --id "$COVER_SRC_SYS"
[ "$(echo "$GET_JSON" | jq -r '.cover_image // ""')" = "" ] \
  || fail "Das Schwarze Auge already has an uploaded cover_image — the smoke fixture has drifted, pick a different system for cover from-source: $GET_JSON"
# listing books to pick one as the cover source
booklist
COVER_SRC_BOOK=$(echo "$LIST_JSON" | jq -r '.books[0].id')

# setting the system's cover from the book, expecting a .webp cover_image
FROMSRC_JSON=$("$CLI" systems cover from-source --id "$COVER_SRC_SYS" --source-type book --source-id "$COVER_SRC_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "systems cover from-source exited non-zero"; }
echo "$FROMSRC_JSON" | jq -e '.cover_image | endswith(".webp")' >/dev/null \
  || fail "from-source should report a .webp cover_image: $FROMSRC_JSON"
ok "systems cover from-source copies a book's image onto the system"

# campaign_file is excluded from SystemCoverSourceIn.known_source, so the
# server rejects it with a 422 validation error before the route body runs —
# not the 400 a downstream campaign lookup would suggest.
# setting the cover from a campaign_file source, expecting it refused
"$CLI" systems cover from-source --id "$COVER_SRC_SYS" --source-type campaign_file --source-id "$COVER_SRC_BOOK" \
  >/dev/null 2>&1 \
  && fail "systems cover from-source should refuse source-type campaign_file"
ok "systems cover from-source refuses source-type campaign_file"

# deleting the system's cover, expecting cover_image to go back to empty
"$CLI" systems cover delete --id "$COVER_SRC_SYS" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "systems cover delete exited non-zero"; }
sysget --id "$COVER_SRC_SYS"
[ "$(echo "$GET_JSON" | jq -r '.cover_image // ""')" = "" ] \
  || fail "cover delete should restore the empty cover_image: $GET_JSON"
ok "systems cover delete restores the empty cover_image, so the run converges"

# --- audio covers and verify-index -------------------------------------------
# Audio tracks have no cover_image field at all (unlike systems) — has_cover
# is the only signal for a deliberately-set cover, checked clean here rather
# than assumed, and cleared again at the end so a re-run converges.
# checking the audio fixture's has_cover is false before the cover checks
SC_TRACK=$("$CLI" audio list 2>"$WORK/cli.err" | jq -r '.audio[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "audio list exited non-zero"; }
[ -n "$SC_TRACK" ] && [ "$SC_TRACK" != "null" ] || fail "no audio fixture for the cover checks"
"$CLI" audio get --id "$SC_TRACK" >"$WORK/track.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "audio get exited non-zero"; }
[ "$(jq -r '.has_cover' "$WORK/track.out")" = "false" ] \
  || fail "the audio fixture already has a set cover — the smoke fixture has drifted, pick another track: $(cat "$WORK/track.out")"

# fetching the track's cover before one is set, expecting a 404
"$CLI" audio cover get --id "$SC_TRACK" --output "$WORK/nocover.bin" >/dev/null 2>&1 \
  && fail "audio cover get should 404 on a track with no set cover"
ok "audio cover get refuses a track with no set cover"

# copying a book's cover onto the track, expecting a .webp cover_image
SC_BOOK=$("$CLI" books list 2>/dev/null | jq -r '.books[0].id')
[ -n "$SC_BOOK" ] && [ "$SC_BOOK" != "null" ] || fail "no book fixture to copy a cover from"
SET_JSON=$("$CLI" audio cover from-source --id "$SC_TRACK" --source-type book --source-id "$SC_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "audio cover from-source exited non-zero"; }
echo "$SET_JSON" | jq -e '.cover_image | endswith(".webp")' >/dev/null \
  || fail "from-source should report a .webp cover_image: $SET_JSON"
ok "audio cover from-source copies a book's image onto the track"

# fetching the track's cover once set, expecting a non-empty file
"$CLI" audio cover get --id "$SC_TRACK" --output "$WORK/cover.bin" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "audio cover get exited non-zero after a cover was set"; }
[ -s "$WORK/cover.bin" ] || fail "audio cover get wrote an empty file"
ok "audio cover get serves the cover once one is set"

# campaign_file is not in the server's source_type enum for a track (a track
# has no campaign to resolve it against), so this 422s before any lookup.
# setting the track's cover from a campaign_file source, expecting it refused
"$CLI" audio cover from-source --id "$SC_TRACK" --source-type campaign_file --source-id "$SC_BOOK" >/dev/null 2>&1 \
  && fail "audio cover from-source should refuse source-type campaign_file"
ok "audio cover from-source refuses source-type campaign_file"

# deleting the track's cover, expecting has_cover to clear again
"$CLI" audio cover delete --id "$SC_TRACK" >"$WORK/coverdel.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "audio cover delete exited non-zero"; }
"$CLI" audio get --id "$SC_TRACK" >"$WORK/track.out" 2>&1
[ "$(jq -r '.has_cover' "$WORK/track.out")" = "false" ] \
  || fail "the track's has_cover should clear again, so the run converges: $(cat "$WORK/track.out")"
ok "audio cover delete clears has_cover, so the run converges"

# Both sides are normalized server-side, so a client-side comparison against
# trusted_index_urls is exactly what this endpoint exists to replace.
# verifying a trusted index url, expecting verified true
VI_TRUSTED=$("$CLI" addons list 2>/dev/null | jq -r '.trusted_index_urls[0] // ""')
[ -n "$VI_TRUSTED" ] || fail "no trusted_index_urls entry to verify against"
VI_JSON=$("$CLI" addons verify-index --url "$VI_TRUSTED" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons verify-index exited non-zero"; }
[ "$(echo "$VI_JSON" | jq -r .verified)" = "true" ] \
  || fail "a trusted index URL should verify: $VI_JSON"
ok "addons verify-index accepts a trusted index URL"

# verifying an untrusted index url, expecting verified false
VI_JSON=$("$CLI" addons verify-index --url "https://example.invalid/not-an-index.json" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "addons verify-index exited non-zero"; }
[ "$(echo "$VI_JSON" | jq -r .verified)" = "false" ] \
  || fail "an untrusted URL should not verify: $VI_JSON"
ok "addons verify-index rejects an untrusted index URL"

# --- book reading ------------------------------------------------------------
# Reads only: nothing to restore, nothing to converge.
# finding a book fixture for the reading checks
BR_BOOK=$("$CLI" books list 2>"$WORK/cli.err" | jq -r '.books[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "books list exited non-zero"; }
[ -n "$BR_BOOK" ] && [ "$BR_BOOK" != "null" ] || fail "no book fixture for the reading checks"

# reading the book's table of contents, expecting a toc array
TOC_JSON=$("$CLI" books toc --id "$BR_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books toc exited non-zero"; }
echo "$TOC_JSON" | jq -e 'has("toc") and (.toc | type == "array")' >/dev/null \
  || fail "books toc should answer a toc array: $TOC_JSON"
ok "books toc answers a toc array"

# reading page 1's text, expecting non-empty text
TEXT_JSON=$("$CLI" books page-text --id "$BR_BOOK" --page 1 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books page-text exited non-zero"; }
[ -n "$(echo "$TEXT_JSON" | jq -r '.text // ""')" ] \
  || fail "page 1 of the fixture book should carry text: $TEXT_JSON"
ok "books page-text reads a page"

# reading page 1's boxed words, expecting a page size and word boxes
WORDS_JSON=$("$CLI" books page-words --id "$BR_BOOK" --page 1 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books page-words exited non-zero"; }
echo "$WORDS_JSON" | jq -e '.width > 0 and (.words | length) > 0' >/dev/null \
  || fail "page 1 should report a page size and some words: $WORDS_JSON"
echo "$WORDS_JSON" | jq -e '.words[0] | has("x0") and has("text")' >/dev/null \
  || fail "each word should carry a box and its text: $WORDS_JSON"
ok "books page-words reports boxed words"

# The page bound is the server's, not the CLI's — it answers 400 with the real
# count rather than silently clamping.
# reading text on a page past the end, expecting it refused
"$CLI" books page-text --id "$BR_BOOK" --page 99 >/dev/null 2>&1 \
  && fail "books page-text should refuse a page past the end"
ok "books page-text refuses a page past the end"

# reading words on a page past the end, expecting it refused
"$CLI" books page-words --id "$BR_BOOK" --page 99 >/dev/null 2>&1 \
  && fail "books page-words should refuse a page past the end"
ok "books page-words refuses a page past the end"

# --- binary endpoints --------------------------------------------------------
# Reads only; downloads land in $WORK, which is fresh each run.
# finding a book fixture for the binary endpoint checks
BE_BOOK=$("$CLI" books list 2>"$WORK/cli.err" | jq -r '.books[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "books list exited non-zero"; }
[ -n "$BE_BOOK" ] && [ "$BE_BOOK" != "null" ] || fail "no book fixture for the binary checks"

# downloading the book file, expecting a non-empty file and a byte count
"$CLI" books file --id "$BE_BOOK" --output "$WORK/book.bin" >"$WORK/bookdl.out" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books file exited non-zero"; }
[ -s "$WORK/book.bin" ] || fail "books file wrote an empty file"
[ "$(jq -r .bytes "$WORK/bookdl.out")" -gt 0 ] \
  || fail "books file should report a byte count: $(cat "$WORK/bookdl.out")"
ok "books file downloads the book and reports its size"

# rendering page 1 of the book, expecting a non-empty image
"$CLI" books page --id "$BE_BOOK" --page 1 --output "$WORK/bookpage.webp" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books page exited non-zero"; }
[ -s "$WORK/bookpage.webp" ] || fail "books page wrote an empty file"
ok "books page renders page 1"

# --width re-renders rather than resizing client-side — a fixture page comes
# back a different size, not just a smaller copy of the same bytes.
# rendering page 1 at width 400, expecting different bytes than the default
"$CLI" books page --id "$BE_BOOK" --page 1 --width 400 --output "$WORK/bookpage-w.webp" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "books page --width exited non-zero"; }
[ -s "$WORK/bookpage-w.webp" ] || fail "books page --width wrote an empty file"
[ "$(wc -c <"$WORK/bookpage.webp")" -ne "$(wc -c <"$WORK/bookpage-w.webp")" ] \
  || fail "--width should change the rendered byte count"
ok "books page --width changes the rendered bytes"

# rendering a book page past the end, expecting it refused
"$CLI" books page --id "$BE_BOOK" --page 99 --output "$WORK/nope.webp" >/dev/null 2>&1 \
  && fail "books page should refuse a page past the end"
ok "books page refuses a page past the end"

# The archive is the one endpoint that exports a whole slice in a call.
# $SR4 (Shadowrun 4 DE), not the first id off `systems list` — that can land on
# a container system with no books of its own, which 404s (see below) rather
# than archiving.
# archiving the Shadowrun 4 system, expecting a valid zip with its file listed
"$CLI" downloads archive --type system --id "$SR4" --output "$WORK/sys.zip" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "downloads archive exited non-zero"; }
[ -s "$WORK/sys.zip" ] || fail "downloads archive wrote an empty file"
[ "$(head -c 2 "$WORK/sys.zip")" = "PK" ] \
  || fail "a zip archive should start with the PK magic: $(head -c 16 "$WORK/sys.zip" | od -c | head -1)"
unzip -l "$WORK/sys.zip" >"$WORK/syszip.list" 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "unzip could not list downloads archive's output"; }
grep -q "1 file\|files$" "$WORK/syszip.list" \
  || fail "unzip -l should report the archive's members: $(cat "$WORK/syszip.list")"
ok "downloads archive exports a system as a zip"

# A tag with usage is created earlier in this run (books "batch-tag adds a tag
# and leaves the existing one in place", above) and never removed, so it is
# still attached by the time this block runs.
# archiving a tag scope, expecting a valid zip
"$CLI" downloads archive --type tag --tag "smoke-book-alpha" --output "$WORK/tag.zip" >/dev/null 2>"$WORK/cli.err" \
  || { cat "$WORK/cli.err" >&2; fail "downloads archive --type tag exited non-zero"; }
[ -s "$WORK/tag.zip" ] || fail "downloads archive --type tag wrote an empty file"
[ "$(head -c 2 "$WORK/tag.zip")" = "PK" ] \
  || fail "the tag archive should start with the PK magic: $(head -c 16 "$WORK/tag.zip" | od -c | head -1)"
ok "downloads archive exports a tag scope as a zip"

# archiving an unrecognized system id, expecting it refused
"$CLI" downloads archive --type system --id "no-such-system" --output "$WORK/x.zip" >/dev/null 2>&1 \
  && fail "an archive scope with an unrecognized system id should fail"
ok "downloads archive 404s an unrecognized system id"

# archiving with an unknown format, expecting it refused
"$CLI" downloads archive --type system --id "$SR4" --fmt sausage --output "$WORK/x.zip" >/dev/null 2>&1 \
  && fail "downloads archive should refuse an unknown format"
ok "downloads archive refuses an unknown format"

# archiving with an unknown scope type, expecting it refused
"$CLI" downloads archive --type sausage --output "$WORK/x.zip" >/dev/null 2>&1 \
  && fail "downloads archive should refuse an unknown scope type"
ok "downloads archive refuses an unknown scope type"

# #48: a container system holding no books of its own 404s rather than
# archiving its children. Das Schwarze Auge and Dungeons & Dragons are such
# containers in the fixtures; assert on whichever book_count == 0 container
# systems list --include-children still reports, rather than a hardcoded id.
# finding a container system with no books, expecting archive to 404 on it
BE_CONTAINER=$("$CLI" systems list --include-children 2>/dev/null | jq -r '[.[] | select(.book_count == 0)][0].id')
if [ -n "$BE_CONTAINER" ] && [ "$BE_CONTAINER" != "null" ]; then
  "$CLI" downloads archive --type system --id "$BE_CONTAINER" --output "$WORK/x.zip" >/dev/null 2>&1 \
    && fail "a container system with no books of its own should 404, not archive"
  ok "downloads archive 404s a container system with no books of its own"
else
  ok "downloads archive container-404 check skipped — no book_count==0 container in this stack"
fi

# ---- campaigns --------------------------------------------------------------
# Runs as the admin session, which owns what it creates. Fixed names and values
# only: the campaign, category and book link are found or made, and the upload
# is unlinked again, so a re-run converges.
# listing campaigns, creating the smoke campaign if missing
CAMPAIGN=$("$CLI" campaigns list 2>"$WORK/cli.err" | jq -r '[.[] | select(.name == "Smoke Campaign")][0].id') \
  || { cat "$WORK/cli.err" >&2; fail "campaigns list exited non-zero"; }
if [ -z "$CAMPAIGN" ] || [ "$CAMPAIGN" = "null" ]; then
  CAMPAIGN=$(echo '{"name":"Smoke Campaign"}' | "$CLI" campaigns create --stdin 2>"$WORK/cli.err" | jq -r .id) \
    || { cat "$WORK/cli.err" >&2; fail "campaigns create exited non-zero"; }
fi
[ -n "$CAMPAIGN" ] && [ "$CAMPAIGN" != "null" ] || fail "campaigns create should return an id"
ok "campaigns list/create find or make the smoke campaign"

# listing campaign categories, creating the smoke handouts category if missing
CATEGORY=$("$CLI" campaigns categories list --id "$CAMPAIGN" 2>"$WORK/cli.err" \
  | jq -r '[.[] | select(.name == "Smoke Handouts")][0].id') \
  || { cat "$WORK/cli.err" >&2; fail "campaigns categories list exited non-zero"; }
if [ -z "$CATEGORY" ] || [ "$CATEGORY" = "null" ]; then
  CATEGORY=$("$CLI" campaigns categories create --id "$CAMPAIGN" --name "Smoke Handouts" 2>"$WORK/cli.err" | jq -r .id) \
    || { cat "$WORK/cli.err" >&2; fail "campaigns categories create exited non-zero"; }
fi
[ -n "$CATEGORY" ] && [ "$CATEGORY" != "null" ] || fail "categories create should return an id"
ok "campaigns categories list/create find or make the smoke category"

# finding a book fixture to link to the campaign
CR_BOOK=$("$CLI" books list 2>"$WORK/cli.err" | jq -r '.books[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "books list exited non-zero"; }
[ -n "$CR_BOOK" ] && [ "$CR_BOOK" != "null" ] || fail "no book fixture to link"
# The first run links the book; every later run finds it linked, which the
# client reports as exit 2 with the server's 409 on stderr.
# linking the book to the campaign, expecting success or a 409 if already linked
set +e
"$CLI" campaigns resources add --id "$CAMPAIGN" --resource-type book --resource-id "$CR_BOOK" \
  >"$WORK/cr-add.out" 2>"$WORK/cr-add.err"; rc=$?
set -e
[ "$rc" -eq 0 ] || { [ "$rc" -eq 2 ] && grep -q "409" "$WORK/cr-add.err"; } \
  || fail "resources add should link or 409, got $rc: $(cat "$WORK/cr-add.err")"
# listing campaign resources, expecting the linked book to appear
LINK=$("$CLI" campaigns resources list --id "$CAMPAIGN" 2>"$WORK/cli.err" \
  | jq -r --arg b "$CR_BOOK" '[.[] | select(.resource_id == $b)][0].id') \
  || { cat "$WORK/cli.err" >&2; fail "campaigns resources list exited non-zero"; }
[ -n "$LINK" ] && [ "$LINK" != "null" ] || fail "resources list should show the linked book"
# updating the link's visibility and category, expecting both to stick
"$CLI" campaigns resources update --id "$CAMPAIGN" --link-id "$LINK" --visibility public --category-id "$CATEGORY" \
  >"$WORK/cr-update.out" 2>"$WORK/cr-update.err" \
  || { cat "$WORK/cr-update.err" >&2; fail "campaigns resources update exited non-zero"; }
jq -e --arg c "$CATEGORY" '.visibility == "public" and .category_id == $c' "$WORK/cr-update.out" >/dev/null \
  || fail "resources update should set visibility and category: $(cat "$WORK/cr-update.out")"
ok "campaigns resources add/list/update link and file a book"

# bulk-linking the already-linked book, expecting exit 3 and no new links
set +e
echo "{\"resources\":[{\"resource_type\":\"book\",\"resource_id\":\"$CR_BOOK\"}]}" \
  | "$CLI" campaigns resources bulk --id "$CAMPAIGN" --stdin >"$WORK/cr-bulk.out" 2>"$WORK/cr-bulk.err"; rc=$?
set -e
[ "$rc" -eq 3 ] || fail "resources bulk of an already-linked book should exit 3, got $rc: $(cat "$WORK/cr-bulk.err")"
jq -e '. == []' "$WORK/cr-bulk.out" >/dev/null || fail "bulk should create nothing: $(cat "$WORK/cr-bulk.out")"
ok "campaigns resources bulk exits 3 when it skips"

# setting group-order with an extra type:model key, expecting it dropped
"$CLI" campaigns categories group-order --id "$CAMPAIGN" --ordered-keys "cat:$CATEGORY" type:book type:model \
  >"$WORK/cr-group.out" 2>"$WORK/cr-group.err" \
  || { cat "$WORK/cr-group.err" >&2; fail "campaigns categories group-order exited non-zero"; }
jq -e --arg c "cat:$CATEGORY" '.resource_group_order == [$c, "type:book"]' "$WORK/cr-group.out" >/dev/null \
  || fail "group-order should keep the category and type:book and drop type:model: $(cat "$WORK/cr-group.out")"
ok "campaigns categories group-order drops type:model"

# writing a smoke handout file to upload
printf 'smoke handout\n' >"$WORK/smoke-handout.txt"
# uploading a file to the campaign, expecting a linked gm file in the category
"$CLI" campaigns files upload --id "$CAMPAIGN" --file "$WORK/smoke-handout.txt" --category-id "$CATEGORY" \
  >"$WORK/cr-upload.out" 2>"$WORK/cr-upload.err" \
  || { cat "$WORK/cr-upload.err" >&2; fail "campaigns files upload exited non-zero"; }
jq -e --arg c "$CATEGORY" '.resource_type == "file" and .visibility == "gm" and .category_id == $c' \
  "$WORK/cr-upload.out" >/dev/null \
  || fail "files upload should link a gm file in the category: $(cat "$WORK/cr-upload.out")"
UPLOAD_LINK=$(jq -r .id "$WORK/cr-upload.out")
# unlinking the uploaded file, expecting it gone from resources list
"$CLI" campaigns resources remove --id "$CAMPAIGN" --link-id "$UPLOAD_LINK" >/dev/null 2>"$WORK/cr-remove.err" \
  || { cat "$WORK/cr-remove.err" >&2; fail "resources remove should unlink the upload"; }
"$CLI" campaigns resources list --id "$CAMPAIGN" 2>/dev/null \
  | jq -e --arg l "$UPLOAD_LINK" 'all(.[]; .id != $l)' >/dev/null \
  || fail "the removed upload link should be gone"
ok "campaigns files upload links a file, and remove takes it away again"

# ---- api keys ----
# Key-mode calls use their own config file, so the admin session above is untouched.
KEYCFG="$WORK/key-config.json"
# logging in as admin with curl for a bearer token to manage keys with
ADMIN_TOKEN=$(curl -sf -X POST "$SERVER/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq -r .token)
[ -n "$ADMIN_TOKEN" ] && [ "$ADMIN_TOKEN" != "null" ] || fail "admin curl login returned no token"
# deleting any smoke keys a previous run left behind, so re-runs converge
delete_smoke_keys() {
  curl -sf "$SERVER/api/api-keys" -H "Authorization: Bearer $ADMIN_TOKEN" \
    | jq -r '.[] | select(.name == "smoke-full" or .name == "smoke-books-read") | .id' \
    | while read -r id; do
        curl -sf -X DELETE "$SERVER/api/api-keys/$id" -H "Authorization: Bearer $ADMIN_TOKEN" >/dev/null \
          || fail "could not delete api key $id"
      done
}
delete_smoke_keys
# creating a key with the given permissions and printing its secret
create_key() {
  curl -sf -X POST "$SERVER/api/api-keys" -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H 'Content-Type: application/json' -d "{\"name\":\"$1\",\"permissions\":$2}" | jq -r .key
}
FULL_KEY=$(create_key smoke-full '{"*":"write"}')
READ_KEY=$(create_key smoke-books-read '{"books":"read"}')
[ -n "$FULL_KEY" ] && [ "$FULL_KEY" != "null" ] || fail "no secret for smoke-full"
[ -n "$READ_KEY" ] && [ "$READ_KEY" != "null" ] || fail "no secret for smoke-books-read"
ok "api keys created"

# logging in with the full key, expecting the key saved and the version recorded
printf '%s' "$FULL_KEY" | GRIMOIRE_CONFIG="$KEYCFG" "$CLI" login --server "$SERVER" --api-key-stdin \
  >"$WORK/key-login.out" 2>"$WORK/key-login.err" \
  || { cat "$WORK/key-login.err" >&2; fail "api key login exited non-zero"; }
grep -q "with an API key" "$WORK/key-login.err" || fail "key login should say so: $(cat "$WORK/key-login.err")"
GRIMOIRE_CONFIG="$KEYCFG" "$CLI" config get | jq -e '.auth == "api-key"' >/dev/null \
  || fail "config get should show auth api-key"
jq -e --arg v "$EXPECTED_VERSION" '.lastServerVersion == $v' "$KEYCFG" >/dev/null \
  || fail "key login should record server version $EXPECTED_VERSION: $(jq -c 'del(.apiKey)' "$KEYCFG")"
# listing systems with the full key, expecting JSON
GRIMOIRE_CONFIG="$KEYCFG" "$CLI" systems list 2>"$WORK/cli.err" | jq -e 'type == "array"' >/dev/null \
  || { cat "$WORK/cli.err" >&2; fail "systems list under an api key failed"; }
ok "api key login with a full key, then systems list"

# logging in with the books-read key, expecting the cannot-read-library warning
printf '%s' "$READ_KEY" | GRIMOIRE_CONFIG="$KEYCFG" "$CLI" login --server "$SERVER" --api-key-stdin \
  >"$WORK/key-login.out" 2>"$WORK/key-login.err" \
  || { cat "$WORK/key-login.err" >&2; fail "books-read key login exited non-zero"; }
grep -q "cannot read library" "$WORK/key-login.err" \
  || fail "books-read key login should warn it cannot read library: $(cat "$WORK/key-login.err")"
# updating a book with the read-only key, expecting exit 2 naming books and write
set +e
echo '{"description":"smoke fixture book description"}' \
  | GRIMOIRE_CONFIG="$KEYCFG" "$CLI" books update --id "$SR4_BOOK" --stdin >/dev/null 2>"$WORK/key-upd.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "books update under a read key should exit 2, got $rc: $(cat "$WORK/key-upd.err")"
grep -q "'books'" "$WORK/key-upd.err" && grep -q "write" "$WORK/key-upd.err" \
  && grep -q "Key permissions per command:" "$WORK/key-upd.err" \
  || fail "the 403 should name books, write and the permissions list: $(cat "$WORK/key-upd.err")"
ok "a books-read key logs in with a warning and is refused books update"

# logging in with a garbage key, expecting exit 2 and the saved key left alone
cp "$KEYCFG" "$WORK/key-config.before"
set +e
printf 'grim_not-a-real-key' | GRIMOIRE_CONFIG="$KEYCFG" "$CLI" login --server "$SERVER" --api-key-stdin \
  >/dev/null 2>"$WORK/key-bad.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a garbage key should exit 2, got $rc: $(cat "$WORK/key-bad.err")"
cmp -s "$KEYCFG" "$WORK/key-config.before" || fail "a rejected key should leave the config file unchanged"
ok "a rejected key exits 2 and overwrites nothing"

# deleting the smoke keys again
delete_smoke_keys
ok "api keys deleted"

echo "smoke: all checks passed" >&2
