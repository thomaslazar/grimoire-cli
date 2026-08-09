# Releasing

**No release has been cut yet.** This file documents the process to follow,
modelled on `abs-cli`'s, and is explicit about which pieces exist here and which
do not — so a first release is a checklist rather than an improvisation.

## What exists today

- `.github/workflows/build.yml` triggers on `release: types: [created]` and, for
  each of the six RIDs, publishes the AOT binary, runs `self-test` against it,
  attaches it to the release, and builds a deb for the two Linux RIDs.
- An `update-homebrew` job refreshes a tap formula from
  `.github/homebrew/grimoire-cli.rb.template`.
- `install.sh` and `install.ps1` fetch a named or latest release.
- `CHANGELOG.md` exists with an `## [Unreleased]` section only.

## What a first release still needs

| Prerequisite | Why |
|---|---|
| A `thomaslazar/homebrew-grimoire-cli` tap repository | `update-homebrew` clones and pushes to it |
| A `HOMEBREW_TAP_TOKEN` repository secret | that job authenticates with it; without it the job fails after the binaries are already attached |
| The repo to be public, or a paid plan | `main` is unprotected because GitHub Free offers neither branch protection nor rulesets on private repos — see [roadmap.md](roadmap.md) |

`install.sh` and `install.ps1` will not work until the first tag exists, since
they resolve GitHub release assets.

## Process

1. **Branch.** `release/v{version}` off `main`. `CHANGELOG.md` is owned by this
   branch and is never edited from a feature branch — that rule is in
   `CLAUDE.md`.
2. **Set the version.** `<Version>` in `src/GrimoireCli/GrimoireCli.csproj`.
   Release builds pass no `BuildId`, so `--version` prints it bare while PR
   builds carry a `+pr-<n>.<sha7>` suffix — see [build.md](build.md).
3. **Reconcile the supported server range.** `MinSupportedVersion` and
   `MaxTestedVersion` in `src/GrimoireCli/Api/GrimoireApiClient.cs` gate the
   login-time warning and must agree with the matrix in
   [grimoire-compatibility.md](grimoire-compatibility.md). If this release adds
   support for a newer Grimoire, both move together.
4. **Write the changelog entry.** Move `## [Unreleased]` items under the new
   version with a date. Describe behaviour, not commits.
5. **Verify before tagging**, per `CLAUDE.md`'s pre-PR gate plus the published
   binary:
   ```bash
   dotnet format GrimoireCli.sln --verify-no-changes
   dotnet build GrimoireCli.sln
   dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
   bash docker/seed.sh && bash docker/smoke-test.sh
   dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 \
     --self-contained true -p:PublishAot=true -o ./publish
   CLI=./publish/grimoire-cli bash docker/smoke-test.sh
   ```
   The last line matters most: it is the only check that exercises the AOT
   binary, where a missing `[JsonSerializable]` registration surfaces.
6. **Merge and tag.** PR the release branch into `main`, then create a GitHub
   release with tag `v{version}`. Creation — not the tag alone — is what triggers
   the workflow.
7. **Watch the release run to a terminal state.** It attaches six binaries, two
   debs, and updates the tap. A failure after the binaries are attached leaves a
   partial release; fix forward rather than deleting assets.
8. **Verify one artefact end to end.** Download a binary, run `--version` and
   confirm it prints bare, then `self-test`.

## Versioning

Semantic versioning on the CLI's own surface — commands, flags, output shape —
not on the Grimoire version it targets. Supporting a new server release is a
minor bump when nothing about the CLI's surface changes, and the compatibility
matrix records which server versions each CLI version was tested against.
