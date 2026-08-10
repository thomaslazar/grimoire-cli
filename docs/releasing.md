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

## What a first release still needs

| Prerequisite | State | Why |
|---|---|---|
| A `thomaslazar/homebrew-grimoire-cli` tap repository | **done** — public, initialised with a commit on `main` | `update-homebrew` clones and pushes to it; an empty repository has no HEAD to clone |
| A `HOMEBREW_TAP_TOKEN` repository secret | **outstanding** | that job authenticates with it; without it the job fails *after* the binaries are already attached, leaving a partial release |
| The repo to be public, or a paid plan | outstanding, not blocking | `main` is unprotected because GitHub Free offers neither branch protection nor rulesets on private repos — see [roadmap.md](roadmap.md) |

`install.sh` and `install.ps1` will not work until the first tag exists, since
they resolve GitHub release assets.

### Creating the tap token

A fine-grained token scoped to the tap alone, rather than a classic `repo` token
that would grant write to every repository on the account:

1. <https://github.com/settings/personal-access-tokens/new>
2. Resource owner `thomaslazar`; repository access *Only select repositories* →
   `homebrew-grimoire-cli`.
3. Repository permissions → **Contents: Read and write**. That is the only
   permission needed — the job clones the tap and pushes one commit. Metadata:
   read is added automatically.
4. Store it without putting the value in a shell history or a transcript:
   ```bash
   gh secret set HOMEBREW_TAP_TOKEN --repo thomaslazar/grimoire-cli   # prompts, no echo
   gh secret list --repo thomaslazar/grimoire-cli                     # confirms, value never readable
   ```

The workflow authenticates as
`https://x-access-token:${HOMEBREW_TAP_TOKEN}@github.com/…`, which is the form
GitHub expects for token auth over HTTPS.

**Token expiry is a real failure mode.** When it lapses, `update-homebrew` starts
failing after a release has already published its binaries. A calendar reminder
is worth more than a distant expiry date.

## Process

1. **Branch.** `release/v{version}` off `main`. **`CHANGELOG.md` does not exist in
   the repository and is not created by feature work** — it is written on this
   branch, by this process, and only here. That rule is in `CLAUDE.md`; a
   changelog assembled ahead of a release is a changelog nobody trusts.
2. **Set the version.** `<Version>` in `src/GrimoireCli/GrimoireCli.csproj`.
   Release builds pass no `BuildId`, so `--version` prints it bare while PR
   builds carry a `+pr-<n>.<sha7>` suffix — see [build.md](build.md).
3. **Reconcile the supported server range.** `MinSupportedVersion` and
   `MaxTestedVersion` in `src/GrimoireCli/Api/GrimoireApiClient.cs` gate the
   login-time warning and must agree with the matrix in
   [grimoire-compatibility.md](grimoire-compatibility.md). If this release adds
   support for a newer Grimoire, both move together.
4. **Write the changelog.** On the first release this means creating
   `CHANGELOG.md`; afterwards, adding a section for the new version above the
   previous one. Either way it is written here, from `git log` since the last tag,
   and describes behaviour rather than commits — what a user can now do, or what
   changed under them.
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
