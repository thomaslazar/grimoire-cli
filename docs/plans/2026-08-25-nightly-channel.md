# Nightly Channel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the local stack from the `edge` channel to `nightly`, where the 1.6.0 RC now lands — and stop the version check from calling an unparseable version "older than the minimum supported".

**Architecture:** Two independent pieces. First, `VersionWarning` gains an "unknown version" branch so a server reporting a non-semver string is treated as un-comparable rather than ancient; `CompareVersions` is untouched, keeping its never-throw contract. Second, `docker/docker-compose.yml` repins to the nightly digest and the client is regenerated from it. The order matters: without the first, every command against nightly prints a false warning.

**Tech Stack:** .NET 10, Docker Compose, Kiota 1.34.1.

## Global Constraints

- **Branch:** `feat/nightly-channel`, off `main`. PR targets `main`.
- **Pin the digest, never the tag.** `nightly` floats exactly as `edge` did, and a floating tag makes a red CI run indistinguishable from upstream moving.
- **Never hand-edit `src/GrimoireCli/Generated/`** — `bash tools/generate-api-client.sh` is the only supported path, and `kiota update` is forbidden (it refetches the raw spec and skips `tools/normalize-spec.py`).
- **Never hand-edit a `*.g.cs`** — regenerate by running its tool.
- **Kiota must be exactly 1.34.1**, matching `.kiotaVersion` in `src/GrimoireCli/Generated/kiota-lock.json`.
- **`bash docker/smoke-test.sh` against the newly pinned stack is the gate.**
- **`CHANGELOG.md`, `docs/roadmap.md` and `docs/grimoire-api-coverage.md` are not touched.**
- **Run `dotnet format GrimoireCli.sln`** after modifying any C# file; CI fails on `--verify-no-changes`.
- **Conventional Commits**, imperative, lowercase, no trailing period, no `Co-Authored-By`, no tool-attribution lines.
- **`MinSupportedVersion` / `MaxTestedVersion` stay at `1.5.6`.** Moving them is workstream C and waits for a released 1.6.0 tag. This plan must not touch them.

## Measured facts this plan is built on

Probed on 2026-08-25 against `hunterreadca/grimoire:nightly`, digest
`sha256:90e2380acad59eef798b5f216751e3b22778fa5c1e5ca4767dcd9e38df0ed042`, built
07:36 UTC, versus the currently pinned `edge` digest `sha256:f274522b…`:

| | edge (pinned) | nightly |
|---|---|---|
| `/api/about` version | `1.5.6-tk8i6j` | **`nightly`** |
| `/api/about` commit_hash | `dev` | `7f5937071f51dfc65bc09f5e5e49d33c431f0a5d` |
| Component schemas | 342 | 342 |
| Operations | 282 | 282 |
| Operations added / removed | — | **0 / 0** |
| Schemas added / removed | — | **0 / 0** |
| Shared schema definitions that changed | — | **0** |

The API surface is identical. Three paths differ, none structurally:

- `/api/downloads/archive` — descriptions only, documenting a new `library_folder`
  scope ("admin-only; any folder as it sits on disk, indexed or not"). `type` is a
  free string, not an enum, so no schema or parameter changed.
- `/api/campaigns/calendar/{token}/all.ics` and `…/{campaign_id}.ics` —
  `operationId` suffix flipped `_get` → `_head`. Kiota derives method names from
  path and verb, so this is expected to produce no diff; Task 2 verifies that.

**So this switch buys the RC channel, not new surface.** Expect a near-empty
regeneration diff. A large one means something was missed — investigate rather
than committing it.

---

### Task 1: Treat an unparseable server version as unknown

`/api/about` on nightly reports the literal string `nightly`. `ParseVersion` maps
each non-numeric segment to `0`, so `nightly` compares as `0.0.0` — below
`MinSupportedVersion` — and `VersionWarning` returns *"Grimoire server version
nightly is older than the minimum supported version (1.5.6). Some features may not
work."* on every command. That is not merely noisy, it is false: an unparseable
version is unknown, not ancient.

This is a latent bug independent of nightly. Any prerelease-only string —
`nightly`, `edge`, `dev` — trips it.

**Files:**
- Modify: `src/GrimoireCli/Api/GrimoireApiClient.cs`
- Test: `tests/GrimoireCli.Tests/Api/CompareVersionsTests.cs` — the `IsComparableVersion` cases
- Test: `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs` — the `VersionWarning` cases, where its existing five already live

**Interfaces:**
- Produces: `GrimoireApiClient.IsComparableVersion(string? version)` → `bool`, and a `VersionWarning` that returns `null` for an un-comparable version.

- [ ] **Step 1: Do NOT change `CompareVersions`**

Read `tests/GrimoireCli.Tests/Api/CompareVersionsTests.cs` first. It contains:

```csharp
    // An unparseable version must not throw — it would take down a working command.
    [Fact]
    public void TreatsUnparseableSegmentsAsZero()
    {
        Assert.Equal(0, GrimoireApiClient.CompareVersions("dev", "0.0.0"));
    }
```

That behaviour is deliberate and stays. `CompareVersions` must never throw, because
it runs on the path of every command. The fix belongs one level up, in
`VersionWarning`, which decides whether a comparison is meaningful at all. Do not
edit `CompareVersions` or `ParseVersion`, and do not delete or weaken that test.

- [ ] **Step 2: Write the failing tests, in the file each belongs to**

Two files, split by what they cover — `IsComparableVersion` is about reading a
version string, `VersionWarning` is about what the check says, and that file already
holds its other five cases.

Append to `tests/GrimoireCli.Tests/Api/CompareVersionsTests.cs`:

```csharp
    // A version with no numeric component tells us nothing, so there is nothing to
    // compare it against — the nightly channel reports the literal "nightly".
    [Theory]
    [InlineData("1.5.6", true)]
    [InlineData("1.5.6-tk8i6j", true)]
    [InlineData("v1.6.0", true)]
    [InlineData("2", true)]
    [InlineData("nightly", false)]
    [InlineData("edge", false)]
    [InlineData("dev", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsComparableVersion_RequiresANumericComponent(string? version, bool expected)
        => Assert.Equal(expected, GrimoireApiClient.IsComparableVersion(version));
```

Append to `tests/GrimoireCli.Tests/Api/VersionCheckCadenceTests.cs`, beside
`AnInRangeVersionWarnsAboutNothing` and the rest:

```csharp
    // The bug this fixes: "nightly" parsed as 0.0.0 and so read as older than the
    // minimum supported version, which is a claim the string does not support.
    [Theory]
    [InlineData("nightly")]
    [InlineData("edge")]
    [InlineData("dev")]
    public void AnUncomparableVersionWarnsAboutNothing(string observed)
        => Assert.Null(GrimoireApiClient.VersionWarning(observed, previous: null));

    // The "moved" prefix is about provenance, not comparability, so a move onto an
    // uncomparable version is still worth saying.
    [Fact]
    public void AMoveOntoAnUncomparableVersionStillSaysItMoved()
    {
        var warning = GrimoireApiClient.VersionWarning("nightly", previous: "1.5.6");
        Assert.NotNull(warning);
        Assert.Contains("moved", warning);
        Assert.DoesNotContain("older than the minimum", warning);
    }
```

**Do not add a test that a real version below the floor still warns** —
`VersionCheckCadenceTests.AnOlderServerWarnsAboutTheFloor` already covers exactly
that with `1.4.0`, and duplicating it buys nothing. That file's five
`VersionWarning` tests all use parseable versions or `null`, so they must keep
passing untouched; if any of them breaks, the change reached further than intended
and that is a finding, not a test to update.

The "moved" test encodes a judgement worth stating: switching a server from `1.5.6` to
`nightly` is exactly the kind of change the "moved" notice exists to surface, so
silence there would lose real information. If you disagree after reading
`VersionWarning`, say so in your report rather than quietly implementing the other
behaviour.

- [ ] **Step 3: Run them and confirm they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "IsComparableVersion|Uncomparable"`
Expected: compile failure — `IsComparableVersion` does not exist.

- [ ] **Step 4: Implement**

In `src/GrimoireCli/Api/GrimoireApiClient.cs`, beside `CompareVersions`:

```csharp
    /// <summary>
    /// Whether a version string carries anything to compare. The nightly and edge
    /// channels report their channel name rather than a version, and
    /// <see cref="ParseVersion"/> reads a non-numeric segment as 0 — so comparing
    /// one would report "older than the minimum" about a string that says nothing
    /// of the sort.
    /// </summary>
    internal static bool IsComparableVersion(string? version)
        => !string.IsNullOrWhiteSpace(version) && ParseVersion(version).Any(p => p != 0);
```

Then in `VersionWarning`, after the existing `moved` assignment and before the
`CompareVersions` comparisons:

```csharp
        if (!IsComparableVersion(observed))
            return string.IsNullOrEmpty(moved) ? null : moved.TrimEnd();
```

Note the subtlety in `IsComparableVersion`: `ParseVersion("0.0.0")` yields all
zeros, so a literal `0.0.0` counts as un-comparable. That is acceptable — no
Grimoire release is 0.0.0 — but say so in the summary if you keep it, or use a
digit-scan on the first segment instead if you prefer. Either is fine; an
unexplained choice is not.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "IsComparableVersion|Uncomparable"`
Expected: PASS, and `TreatsUnparseableSegmentsAsZero` still passing.

- [ ] **Step 6: Make the debug line honest too**

`RecordServerVersion` logs, in the no-warning case:

```csharp
        else _logger.Debug($"server version {observed} (in tested range {MinSupportedVersion}-{MaxTestedVersion})");
```

For `nightly` that now claims it is in the tested range, which is the same false
statement in a quieter voice. Split it:

```csharp
        else if (IsComparableVersion(observed))
            _logger.Debug($"server version {observed} (in tested range {MinSupportedVersion}-{MaxTestedVersion})");
        else
            _logger.Debug($"server version {observed} carries no version number to compare against the tested range {MinSupportedVersion}-{MaxTestedVersion}");
```

- [ ] **Step 7: Format, build, test**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: 0 warnings, 0 errors, all tests pass. Report the count.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "fix: treat a version with no number as unknown, not ancient

The nightly and edge channels report their channel name where a version
belongs, and ParseVersion reads a non-numeric segment as 0 — so the
check announced that a server called nightly was older than the minimum
supported version, which the string says nothing about. CompareVersions
keeps its never-throw contract; VersionWarning decides whether there is
anything worth comparing."
```

---

### Task 2: Repin to the nightly digest and regenerate

**Files:**
- Modify: `docker/docker-compose.yml` — the image pin and its comment
- Modify: `src/GrimoireCli/Generated/` (generator only)
- Modify: `src/GrimoireCli/Commands/JsonExamples.g.cs` (generator only)
- Modify: `CLAUDE.md`, `docs/grimoire-compatibility.md` — they name `edge`

**Interfaces:**
- Consumes: Task 1's fix, without which every command against the new stack prints a false warning.

- [ ] **Step 1: Confirm the digest before pinning it**

```bash
docker pull hunterreadca/grimoire:nightly
docker inspect hunterreadca/grimoire:nightly --format '{{index .RepoDigests 0}}'
```

Expected: `hunterreadca/grimoire@sha256:90e2380acad59eef798b5f216751e3b22778fa5c1e5ca4767dcd9e38df0ed042`.

**If the digest differs, `nightly` has moved since this plan was written.** Do not
pin the plan's stale digest. Pin what you just pulled, and re-measure the spec
diff in Step 6 against the pinned `edge` build rather than trusting this plan's
"identical surface" claim — that claim was measured against `90e2380a`.

- [ ] **Step 2: Repin the compose image**

In `docker/docker-compose.yml`, replace the image line and rewrite its comment,
which currently describes the `edge` channel and the 2026-08-23 build:

```yaml
    # Pinned to the build this CLI targets. That target is 1.6.0, which is
    # unreleased, so the pin is a DIGEST rather than a tag: the channel moves under
    # you and a floating tag would make a red CI run indistinguishable from
    # upstream changing. A digest is as reproducible as a release tag.
    #
    # This is `nightly` built 2026-08-25, where the 1.6.0 RC lands. It reports its
    # version as the literal "nightly" — see IsComparableVersion in
    # GrimoireApiClient. Move it deliberately and regenerate the client in the same
    # commit, because the spec is read from whatever this pin resolves to. Support
    # for released 1.5.6 lives on support/grimoire-1.5.6, where this file still
    # pins the 1.5.6 tag.
    image: hunterreadca/grimoire@sha256:90e2380acad59eef798b5f216751e3b22778fa5c1e5ca4767dcd9e38df0ed042
```

- [ ] **Step 3: Bring the stack up on the new pin, from clean state**

A database created by one server build must not be reused by another, and the boot
scan indexes whatever library tree is on disk — so reset both, per CLAUDE.md.

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data docker/library/books docker/addon-index/index.json
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
```

Copying the fixture before the first boot is required — skip it and the only
symptom is a 401. Then seed:

```bash
bash docker/seed.sh
```

- [ ] **Step 4: Confirm the stack is the intended build**

```bash
curl -s http://host.docker.internal:9481/api/openapi.json | jq -r '.info.version'
TOKEN=$(curl -sf -X POST http://host.docker.internal:9481/api/auth/login \
  -H 'Content-Type: application/json' -d '{"username":"admin","password":"admin"}' | jq -r .token)
curl -s http://host.docker.internal:9481/api/about -H "Authorization: Bearer $TOKEN" | jq '.'
```

Expected: `nightly`, and a `commit_hash` that is a real 40-character SHA rather
than `dev`. Record the SHA in your report — it is the only precise provenance
marker this channel gives.

- [ ] **Step 5: Regenerate the client**

```bash
bash tools/generate-api-client.sh
```

It defaults to `http://host.docker.internal:9481`, which is the pinned stack, so it
needs no override. Then confirm `descriptionLocation` is the stable hostname and
not a container IP:

```bash
jq -r '.descriptionLocation' src/GrimoireCli/Generated/kiota-lock.json
```

- [ ] **Step 6: Check the regeneration diff is as small as predicted**

```bash
git diff --stat src/GrimoireCli/Generated/
```

Expected: only `kiota-lock.json`'s `descriptionHash` — the API surface was measured
identical, and the `operationId` flips on the two `.ics` paths should not reach
generated code because Kiota names methods from path and verb.

**A larger diff is a finding, not a nuisance.** Report what changed and why before
committing it; do not assume the measurement was wrong.

- [ ] **Step 7: Regenerate the examples file**

The example generator walks the generated models, and its drift test shells out and
compares, so it must be regenerated whenever the models change:

```bash
dotnet run --project tools/GenerateJsonExamples -- src/GrimoireCli/Commands/JsonExamples.g.cs
git diff --stat src/GrimoireCli/Commands/JsonExamples.g.cs
```

Expected: no change, for the same reason as Step 6. Report it either way.

- [ ] **Step 8: Update the two docs that name `edge`**

- **`CLAUDE.md`**, the digest-pin bullet: it says `edge` moves and gives
  `docker pull hunterreadca/grimoire:edge` plus a `docker inspect` as the
  staleness check. Point both at `nightly` and say why the channel changed — the
  1.6.0 RC lands there. Keep the bullet's shape: a digest is a temporary exception
  that workstream C retires by repinning to `hunterreadca/grimoire:1.6.0`.
- **`docs/grimoire-compatibility.md`**: says `main` pins an "`edge` digest rather
  than a release". Say `nightly`. Its "Runtime check" section lists the check's
  outcomes — below minimum, above tested, inside range — and now needs the fourth:
  a version with no number to compare is reported only under `--debug`.
- **`docs/grimoire-1.6.0-migration.md`** is the living working reference for this
  migration, so its statement of the *current* pin must be true: the sequence
  section says `docker/docker-compose.yml` pins an `edge` digest. Say `nightly`,
  and note that the RC channel is why. Leave its historical measurement notes
  ("re-measured against `edge` built 2026-08-17", the three-build progression)
  alone — those are records of when numbers were taken, and nightly's surface is
  identical, so the numbers still hold.

- [ ] **Step 9: Full verification**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -o /tmp/gp-nightly
/tmp/gp-nightly/grimoire-cli self-test
CLI=/tmp/gp-nightly/grimoire-cli bash docker/smoke-test.sh
```

Build must be 0 warnings and 0 errors. `self-test` must exit 0. The smoke test is
the gate. If a `jq` assertion fails, read it before touching it — report a genuine
behaviour difference rather than adjusting the assertion to match.

- [ ] **Step 10: Confirm the false warning is actually gone**

This is the point of Task 1, against a real server rather than a unit test:

```bash
rm -f ~/.grimoire-cli/config.json
/tmp/gp-nightly/grimoire-cli --debug login --server http://host.docker.internal:9481 \
  --username admin --password admin 2>&1 | grep -iE "version|older|tested"
```

Expected: a debug line saying `nightly` carries no version number to compare, and
**no** warning about being older than the minimum supported version. Paste the
output into your report.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "ci: pin the dev stack to the nightly digest

The 1.6.0 RC lands on nightly rather than edge, so that is the build to
test against. The API surface is identical to the edge digest it
replaces — zero operations, schemas or schema definitions changed — so
this buys the channel, not new surface, and the regeneration diff is
expected to be empty beyond the spec hash."
```

---

## Notes for the implementer

- **`nightly` floats.** If a step's observed digest disagrees with this plan's, the channel moved; pin what you pulled and re-measure rather than reconciling to the plan.
- **`MinSupportedVersion` / `MaxTestedVersion` stay at `1.5.6`.** They move in workstream C, when 1.6.0 tags. Touching them here would make the CLI claim support for a version nobody can install.
- **Writes go to the local stack, never the live instance.** The smoke test writes only to `Shadowrun 4 DE`; `seed.sh` deliberately leaves it unpatched as a metadata fixture, so do not spend it.
- **A database-only reset leaves stale rows.** The boot scan indexes whatever library tree is on disk, so wiping `docker/data` without also clearing `docker/library/books` leaves systems that survive as `is_missing` and still count toward `book_count`.
