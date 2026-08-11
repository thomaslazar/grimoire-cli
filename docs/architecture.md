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
    ResponseExamples.g.cs  generated samples (tools/GenerateResponseExamples)
  Services/                one class per resource; builds the request, deserializes
  Api/                     GrimoireApiClient, TokenHelper, DebugHttpHandler
  Generated/               Kiota client — paths, path/query params, request bodies
  Models/                  response DTOs + AppJsonContext (source-generated JSON)
  Configuration/           AppConfig, ConfigManager (flags > env > file)
  Output/                  ConsoleOutput (stdout JSON), LogSetup (stderr, NLog)
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
   non-2xx logs a mapped message and exits 2.
5. The body deserializes through `AppJsonContext` into DTOs, which
   `ConsoleOutput.WriteJson` re-serializes to stdout.

## Native AOT constrains the design

The binary is published with `PublishAot=true`, which trims reflection-based
`System.Text.Json`. Every type crossing the JSON boundary must be registered on
`AppJsonContext` with `[JsonSerializable]`. A missing registration **compiles and
passes in Debug** and fails only in the published binary, so `self-test` exists
to exercise those paths offline and runs against all six published RIDs in CI.
See [testing.md](testing.md) and [build.md](build.md).

## Why responses are typed

Grimoire's OpenAPI spec types nearly every response as `{}` — FastAPI without
`response_model` — so DTOs are written from the upstream serializers rather than
generated from the spec. They carry no `[JsonExtensionData]` catch-all
deliberately: the CLI's output is a contract for agent consumers, so a field the
DTOs do not model is a signal to update them under the version-bump procedure in
[grimoire-compatibility.md](grimoire-compatibility.md), not something to pass
through unmodelled.

The trade-off is explicit: faithfulness to a *known* server version over
tolerance of an unknown one.

## Generated artefacts

These are generated and committed, each guarded by a test or a script:

| File | Generator | Guard |
|---|---|---|
| `src/GrimoireCli/Commands/ResponseExamples.g.cs` | `tools/GenerateResponseExamples` | `ResponseExamplesDriftTest` regenerates and diffs |
| `docs/grimoire-api-coverage.md` | `tools/generate-api-coverage.py` | roles cross-checked against the spec's own descriptions |
| `src/GrimoireCli/Generated/` | `tools/generate-api-client.sh` | reviewed by the regeneration diff on a version bump, not a CI gate |

## What lives outside the CLI

Workflows spanning several endpoints are the caller's job. The reference
material used to ground API decisions lives in `temp/` (gitignored): the upstream
source pinned at the deployed release tag. No spec snapshot is kept on disk —
the generator and `tools/generate-api-client.sh` always read the spec fresh
from a running instance, so it cannot go stale. See `CLAUDE.md` for how to
populate `temp/`.
