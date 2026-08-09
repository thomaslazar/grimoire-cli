# Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
The dev container has it, along with the Native AOT toolchain — see
[dev-container.md](dev-container.md).

## Debug build

```bash
dotnet build GrimoireCli.sln
./src/GrimoireCli/bin/Debug/net10.0/grimoire-cli --help
```

Fast, JIT-compiled, and what the unit tests run against. It will **not** catch
AOT-only failures; see the warning below.

## Native AOT publish

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj \
  -c Release -r linux-x64 --self-contained true -p:PublishAot=true
# Binary at: src/GrimoireCli/bin/Release/net10.0/linux-x64/publish/grimoire-cli
```

Swap `-r` for any supported RID: `linux-x64`, `linux-arm64`, `osx-arm64`,
`osx-x64`, `win-x64`, `win-arm64`. The result is a single self-contained
executable of roughly 10 MB with no runtime dependency.

**Use `-p:` and not `/p:` for MSBuild properties.** Under Git Bash on Windows a
leading `/` is rewritten as a path, so `/p:PublishAot=true` reaches MSBuild as a
second project argument and fails with `MSB1008: Only one project can be
specified`. Both Windows legs of the CI matrix broke this way once.

## Why AOT changes what you must test

`PublishAot=true` trims reflection-based `System.Text.Json`. A type that crosses
the JSON boundary without a `[JsonSerializable]` registration on `AppJsonContext`
compiles cleanly, passes every Debug test, and throws only in the published
binary on a user's machine. So:

- `self-test` exercises the source-generated JSON paths, JWT parsing and version
  comparison offline, and CI runs it against **every** published RID.
- The `smoke-test` job runs the full suite against the **published** binary, not
  the Debug one, for the same reason.

Cross-compilation limits: Linux and Windows AOT require a matching host
architecture, which is why the CI matrix pairs `linux-arm64` with an ARM runner.
macOS cross-compiles arm64 ↔ x64 through the Xcode toolchain.

## Version stamping

The version lives in `src/GrimoireCli/GrimoireCli.csproj` as `<Version>`.

CI passes `-p:BuildId=pr-<number>.<sha7>` for non-release builds, which the
csproj turns into an `InformationalVersion` of `0.1.0+pr-3.a1b2c3d`. That string
is what `--version` prints and what goes out in the `User-Agent`, so an installed
build identifies itself. Releases pass no `BuildId` and stay bare.

`IncludeSourceRevisionInInformationalVersion` is deliberately `false`. Without
it, the SDK's source-link integration appends the full git SHA to *every* build,
releases included — the property is load-bearing, not leftover.

## CI

`.github/workflows/build.yml` runs on pull requests to `main` and on release
creation:

| Job | What it does |
|---|---|
| `unit-test` | `dotnet format --verify-no-changes`, then the unit tests |
| `smoke-test` | installs `python3-fitz`, publishes linux-x64, starts and seeds the stack, runs `docker/smoke-test.sh` against the published binary |
| `build` | 6-RID matrix; publishes, runs `self-test`, uploads a per-RID artifact (5-day retention), and on release attaches binaries and builds the deb |
| `update-homebrew` | on release only; refreshes the tap formula |

Per-PR artifacts are how a build gets installed and exercised against a real
server before merge.
