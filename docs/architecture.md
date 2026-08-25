# Architecture

`grimoire-cli` is a thin, single-purpose HTTP client: one command maps to one
Grimoire API endpoint, stdout carries that endpoint's JSON, and everything else
goes to stderr. There is no local state beyond a config file and no caching.

## Layers

```
Program.cs                 root command, global options, log setup, exit codes
  Commands/                System.CommandLine definitions — flags, help, examples
    CommandHelper          resolves config into a client, exits if unconfigured
    HelpExtensions         Notes / Examples / Response shape sections, --help-full
    JsonExamples.g.cs       generated request/response samples (tools/GenerateJsonExamples)
  Services/                one class per resource; builds the request, returns the raw body
  Api/                     GrimoireApiClient, TokenHelper, DebugHttpHandler
  Generated/               Kiota client — paths, path/query params, request bodies
  Configuration/           AppConfig, ConfigManager (flags > env > file), AppJsonContext (source-generated JSON)
  Output/                  ConsoleOutput (stdout JSON), LogSetup (stderr, NLog), SavedFile
```

A command does four things and nothing else: declare its flags, read them,
call a service, hand the result to `ConsoleOutput`. Anything that looks like
orchestration belongs in the caller, not here — see
[cli-design.md](cli-design.md).

## Request path

`systems list --family Shadowrun` walks:

1. `SystemsCommand` reads the parsed flags.
2. `CommandHelper.BuildClient()` resolves server and token, exiting 1 if either
   is missing.
3. `SystemsService.ListAsync(...)` builds a `RequestInformation` via the
   generated `Api.Api.Systems` builder, which omits unset query parameters and
   URL-encodes the rest — filter values are real system names like
   `Dungeons & Dragons`.
4. `GrimoireApiClient.SendAsync` converts it to a native request, sends it with
   the bearer token, warns on stderr if the token is near expiry, and on a
   non-2xx logs a mapped message and exits 2. `EnsureJson` then checks the
   body parses as JSON (or is empty) — Grimoire's SPA answers an unroutable
   request with an HTML 200, and this is where that gets caught instead of
   reaching stdout — and exits 2 if it doesn't.
5. `ConsoleOutput.WriteRawJson` writes that body to stdout unmodified, or
   re-indented under `--pretty`. Nothing deserializes it; there is no DTO on
   this path.

## Native AOT constrains the design

The binary is published with `PublishAot=true`, which trims reflection-based
`System.Text.Json`. Every type crossing the JSON boundary must be registered on
`AppJsonContext` with `[JsonSerializable]`. A missing registration **compiles and
passes in Debug** and fails only in the published binary, so `self-test` exists
to exercise those paths offline and runs against all six published RIDs in CI.
See [testing.md](testing.md) and [build.md](build.md).

## Why responses are a byte passthrough

A response is not deserialized into a typed model and re-serialized: the
server's bytes reach stdout unmodified (re-indented only under `--pretty`),
so undeclared fields, explicit `null`s and key order are all the server's.
There is therefore no DTO to keep in step with a version bump, and no
`[JsonExtensionData]` gap to drop an unmodelled field into.

`GrimoireApiClient.EnsureJson` is what a hand-written DTO used to provide as
a side effect of deserializing: Grimoire's SPA answers an unroutable request
with an HTML 200 rather than a JSON error, and without this check that page
would reach stdout as though it were the API's answer. It runs inside the
string-returning `SendAsync`, so every response goes through it once,
regardless of command.

The generated request/response models (`GrimoireCli.Generated.Models`) still
exist and are used for one thing: `--help-full` response and request samples,
via `Commands/JsonExamples.g.cs` — see [cli-design.md](cli-design.md).

## Generated artefacts

These are generated and committed, each guarded by a test or a script:

| File | Generator | Guard |
|---|---|---|
| `src/GrimoireCli/Commands/JsonExamples.g.cs` | `tools/GenerateJsonExamples` | `JsonExamplesDriftTest` regenerates and diffs |
| `docs/grimoire-api-coverage.md` | `tools/generate-api-coverage.py` | roles cross-checked against the spec's own descriptions |
| `src/GrimoireCli/Generated/` | `tools/generate-api-client.sh` | reviewed by the regeneration diff on a version bump, not a CI gate |

## What lives outside the CLI

Workflows spanning several endpoints are the caller's job. The reference
material used to ground API decisions lives in `temp/` (gitignored): the upstream
source pinned at the deployed release tag. No spec snapshot is kept on disk —
the generator and `tools/generate-api-client.sh` always read the spec fresh
from a running instance, so it cannot go stale. See `CLAUDE.md` for how to
populate `temp/`.
