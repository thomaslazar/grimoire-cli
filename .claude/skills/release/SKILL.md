---
name: release
description: Create a new grimoire-cli release with human review gates. Creates release branch, generates changelog, opens PR for CI validation, then tags and publishes after merge.
disable-model-invocation: true
allowed-tools:
  - Bash
  - Read
  - Write
  - Glob
  - Grep
  - Edit
  - AskUserQuestion
---

# Release grimoire-cli

Multi-step release workflow with human gates. You drive each step, pause at
gates for human approval before proceeding. Never skip a gate.

## Step 1: Preflight

Verify prerequisites:

```bash
BRANCH=$(git branch --show-current)
[ "$BRANCH" = "main" ] || { echo "ERROR: must be on main, currently on $BRANCH"; exit 1; }
git diff --quiet && git diff --cached --quiet || { echo "ERROR: working tree not clean"; git status --short; exit 1; }
git pull
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
./publish/grimoire-cli self-test
```

Run the full smoke test (Docker is required). The fixture copy is **required
before the first boot**; skip it and the stack comes up with no users, whose
only symptom is a 401:

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
docker compose -f docker/docker-compose.yml down
rm -rf publish/
```

> The library must go too, not just the database: the boot scan indexes
> whatever is on disk, so a database-only reset leaves the old tree to be
> re-indexed as stale rows that still count toward `book_count`.
>
> Under docker-outside-of-docker the daemon runs on the host: set
> `GRIMOIRE_LIBRARY` and `GRIMOIRE_DATA` to host paths, reach the stack at
> `http://host.docker.internal:9481`, not `localhost`, and set
> `GRIMOIRE_LIBRARY_LOCAL` if the library lives outside the repo. See `CLAUDE.md`
> for the full set.

If any check fails, stop and report the issue. Do not proceed.

Determine the version number:
- Get the last tag: `git describe --tags --abbrev=0 2>/dev/null || echo "none"`
- Read commits since last tag
- Propose a version based on conventional commits:
  - Any `feat:` commits → bump MINOR
  - Only `fix:`, `docs:`, `test:`, `ci:`, `chore:` → bump PATCH
  - Grimoire compatibility bumps with no CLI changes → PATCH
  - See `docs/releasing.md` for versioning rules

**GATE: Ask the human to confirm the version number.** Show them the proposed
version and the commit summary. Wait for their response.

## Step 2: Create Release Branch and Bump Version

```bash
VERSION="v{version}"  # from step 1, e.g. "v0.1.0"
VERSION_NUM="${VERSION#v}"  # strip leading "v" — csproj wants "0.1.0", not "v0.1.0"
git checkout -b "release/${VERSION}"
```

Bump `<Version>` in `src/GrimoireCli/GrimoireCli.csproj` to `${VERSION_NUM}`. This is
what `grimoire-cli --version` reports and what the binary sends in its HTTP
`User-Agent` header. Forgetting this leaves the binary self-reporting the
previous version.

Use `Edit` to change the line `<Version>OLD</Version>` to `<Version>NEW</Version>`
in `src/GrimoireCli/GrimoireCli.csproj`. Verify with grep:

```bash
grep "<Version>" src/GrimoireCli/GrimoireCli.csproj
# Should print:   <Version>{VERSION_NUM}</Version>
```

Rebuild the AOT binary and confirm `--version` reports the new number:

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishAot=true -o ./publish
./publish/grimoire-cli --version
# Must print exactly: {VERSION_NUM} — with no "+pr-<n>.<sha7>" suffix.
# Release builds pass no BuildId; a suffix here means the build picked up PR
# metadata and the artifact would self-report a PR version. See docs/build.md.
rm -rf publish/
```

If the printed version does not match, stop — something is wrong with
the csproj or the build.

Commit the bump:

```bash
git add src/GrimoireCli/GrimoireCli.csproj
git commit -m "chore: bump version to ${VERSION_NUM}"
```

## Step 3: Reconcile the Supported Server Range

`MinSupportedVersion` and `MaxTestedVersion` in
`src/GrimoireCli/Api/GrimoireApiClient.cs` gate the daily version-check warning
(forced fresh at login). They must agree with the matrix in
`docs/grimoire-compatibility.md` and the compatibility line in `README.md`
before a tag is cut.

```bash
grep -n "MinSupportedVersion\|MaxTestedVersion" src/GrimoireCli/Api/GrimoireApiClient.cs
grep -n "Tested against Grimoire" README.md
sed -n '/## Matrix/,/^$/p' docs/grimoire-compatibility.md
```

All three must name the same Grimoire version. If this release adds support for
a newer Grimoire, they move together, and `docs/grimoire-compatibility.md`
gains a matrix row.

**GATE: Show the human all three values and confirm they agree.**

## Step 4: Generate Release Notes

Generate release notes into `temp/release-notes.md`. The full convention — and the
reasons behind it — is in [docs/releasing.md](../../../docs/releasing.md#release-notes);
the summary is:

An opening paragraph naming the kind of release and its theme, then:

**Highlights** — three to six bullets, each a **bold lead-in** followed by prose
that says *why*, not only what. The only section allowed to explain itself.

**Changes** — every conventional commit since the last tag, one bullet each with
its prefix kept, grouped by type in this order and sorted alphabetically within
each group: `Features` (`feat:`), `Fixes` (`fix:`), `Refactors` (`refactor:`),
`Tests` (`test:`), `Chores` (`chore:`, `ci:`), `Docs` (`docs:`). Omit empty
groups. Collapse to a single flat list only when a release has so few commits
that the headings would outweigh the entries.

Do not consolidate or curate the list — it is the record of what changed:

```bash
LAST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
if [ -n "$LAST_TAG" ]; then
    RANGE="${LAST_TAG}..HEAD"
else
    RANGE="HEAD"
fi
git log --oneline $RANGE --pretty="- %s" | grep -E "^- (feat|fix|refactor|docs|test|ci|chore):" | sort
```

Write `temp/release-notes.md` in this format:

```markdown
## v{version} — YYYY-MM-DD

{Kind of release, and its theme, in two or three lines.}

### Highlights

- **Bold lead-in.** Why it matters, not only what changed.

### Features

- feat: ...

### Fixes

- fix: ...

### Refactors

- refactor: ...

### Tests

- test: ...

### Chores

- chore: ...
- ci: ...

### Docs

- docs: ...
```

**GATE: Open `temp/release-notes.md` in the editor** (e.g. `code temp/release-notes.md`)
and ask the human to review and approve. If they want edits, make them and
show again.

Then prepend the release notes to `CHANGELOG.md` (create the file if it
doesn't exist). Keep a header at the top:

```markdown
# Changelog

All notable changes to grimoire-cli are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/).

{contents of temp/release-notes.md}

{previous entries...}
```

This is the only place `CHANGELOG.md` is written — feature branches never touch it.

Commit the changelog:

```bash
git add CHANGELOG.md
git commit -m "docs: add v{version} changelog entry"
```

## Step 5: Open PR for CI Validation

Push the release branch and open a PR:

```bash
git push -u origin "release/${VERSION}"
gh pr create --title "release: ${VERSION}" --body "Release ${VERSION}. See CHANGELOG.md for details." --base main
```

Wait for CI to complete:

```bash
# Run may not exist immediately — retry until it appears
for i in $(seq 1 10); do
    RUN_ID=$(gh run list --branch "release/${VERSION}" --limit 1 --json databaseId -q '.[0].databaseId')
    [ -n "$RUN_ID" ] && break
    sleep 3
done
# Watch in background to avoid flooding context with streaming output
gh run watch "$RUN_ID" --exit-status
# Then get structured results
gh run view "$RUN_ID" --json jobs --jq '.jobs[] | "\(.name)\t\(.conclusion)"'
```

If CI fails:
- Show failure details: `gh run view "$RUN_ID" --log-failed`
- Stop and report. Fix issues on the release branch, push, and re-check.

Report CI results (all jobs, times). Use `gh run view --json` for the
summary — do not paste streaming `gh run watch` output into chat.

**GATE: Tell the human CI passed. Ask them to review and merge the PR.**
Show the PR URL. Wait for them to confirm the merge is done.

## Step 6: Tag and Create GitHub Release

After the PR is merged, switch back to main and create the release.
The `temp/release-notes.md` from step 4 is still available (gitignored, not committed).

```bash
git checkout main
git pull
gh release create "${VERSION}" --title "${VERSION}" --notes-file temp/release-notes.md
```

Clean up after the release is created:
```bash
rm temp/release-notes.md
```

Show the release URL.

**GATE: Confirm the release was created.** Show the URL.

## Step 7: Wait for Release CI

The release triggers CI which builds all 6 platforms, **automatically
attaches binaries and deb packages** to the GitHub Release, and updates
the Homebrew tap (`thomaslazar/homebrew-grimoire-cli`). Monitor it:

```bash
# Wait for the run to appear
for i in $(seq 1 10); do
    RUN_ID=$(gh run list --limit 5 --json databaseId,event -q '[.[] | select(.event=="release")] | .[0].databaseId')
    [ -n "$RUN_ID" ] && break
    sleep 3
done
gh run watch "$RUN_ID" --exit-status
```

If CI fails, show failure details and stop.

Report CI results (all jobs, times).

## Step 8: Verify

Download and test one binary:

```bash
gh release download "${VERSION}" --pattern "grimoire-cli-linux-x64" --dir /tmp/release-verify
chmod +x /tmp/release-verify/grimoire-cli-linux-x64
/tmp/release-verify/grimoire-cli-linux-x64 self-test
rm -rf /tmp/release-verify
```

Verify the Homebrew tap was updated:

```bash
gh api repos/thomaslazar/homebrew-grimoire-cli/commits --jq '.[0].commit.message'
# Should contain the new version
```

**GATE: Ask the human to check the GitHub Release page.** They should verify:
- All 6 platform binaries are attached
- Both deb packages are attached (amd64 + arm64)
- Homebrew tap was updated (confirmed above)
- Release notes render correctly
- Everything looks right

## Step 9: Done

Report:
- Release URL
- Version number
- Number of release artifacts (should be 8: 6 binaries + 2 deb packages)
- Self-test result
- Changelog committed to repo

## Rules

- NEVER skip a human gate
- NEVER proceed past a failed check
- If anything unexpected happens, stop and ask
- Clean up temporary files at the end
- The CHANGELOG.md entry is the source of truth — GitHub Release notes mirror it
- This skill may commit without asking — the commit steps are part of the defined workflow (overrides the ask-before-commit rule in CLAUDE.md)
