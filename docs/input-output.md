# Input/Output

## Output

- All commands write JSON to stdout via `ConsoleOutput.WriteJson` (`src/GrimoireCli/Output/ConsoleOutput.cs`).
- Every log line and human-facing message goes to stderr — prompts (`login`),
  `Set <key> = <value>` confirmations (`config set`), warnings, and errors.
- **Not a raw passthrough.** Responses deserialize into typed DTOs
  (`GameSystemSummary`, `GameSystemDetail`, `Book`, …, registered on
  `AppJsonContext` for Native AOT source-generated serialization) and are
  re-serialized on the way out. `stdout` is therefore the DTO's shape, not
  necessarily the server's raw JSON byte-for-byte.
- **No `[JsonExtensionData]`.** An unmodelled field the server adds is
  silently dropped rather than passed through. This is deliberate: a dropped
  field is meant to be *noticed* — `tests/GrimoireCli.Tests/Commands/ResponseExamplesDriftTest.cs`
  and `ResponseExamplesJsonValidTest.cs` guard the generated examples on the
  response side, `RequestExamplesDriftTest.cs` on the request side, and a
  real server response gaining a field the DTOs don't know about is a signal
  to update the DTOs per the version-bump procedure in
  [grimoire-compatibility.md](grimoire-compatibility.md), not something to
  paper over with an extension-data bag.
- `config get` and the built-in help/self-test paths are the exception:
  they write a hand-built `Dictionary<string,string>`, not a server response.
- List endpoints that return a bare JSON array (e.g. `GET /api/systems`) are
  re-serialized as a bare array, not wrapped in an envelope — there's no
  pagination envelope in Grimoire's `systems` responses today.

## Exit Codes

- `0` — success
- `1` — usage or configuration errors (missing server/token, unknown config
  key, missing required argument)
- `2` — API failures (HTTP error status from Grimoire, or an unhandled
  exception reaching `Program.cs`'s top-level catch)
- `3` — the request succeeded (HTTP 200) but did not do what was asked: a bulk
  call with a non-empty `errors` list (a partial write), `library rescan`
  reporting `already_running`, where a scan was already in flight and the
  requested one never started, or `addons upgrade-all` with a non-empty
  `failed` list. stdout carries the full response either way.

See `GrimoireApiClient.EnsureSuccessAsync` for the status-to-message mapping
(401/403/400/404/422 get specific text; anything else falls back to
`API request failed: {status} {reason}` with the body appended).

## Logging

- `--debug` (root option) or `GRIMOIRE_DEBUG=1` raises the stderr log level
  from `Warn` to `Debug` — HTTP requests (via `DebugHttpHandler`), token
  expiry checks, and server-version comparisons all log at `Debug`.
- `--log-json` (root option) switches the stderr layout from
  `{timestamp} {LEVEL} {message}` to single-line JSON
  (`{"timestamp":...,"level":...,"message":...}`) — see `LogSetup.cs`.
- **Both are root options and must precede the subcommand**:
  `grimoire-cli --debug systems list`, not `grimoire-cli systems list --debug`.
  System.CommandLine parses root options before subcommand tokens, and
  neither is `Recursive` on the subcommands.
- The bearer token is never logged, at any verbosity — `DebugHttpHandler`
  logs only method, URL, status, and (for 4xx/5xx) a truncated response
  body; it never dumps request headers, so the `Authorization` header is
  out of scope by construction, not by omission.

## Input for Updates

No write/update commands exist yet — `systems list` and `systems get` are
the only two commands, both reads (`src/GrimoireCli/Commands/SystemsCommand.cs`).
There is currently no `--input` / `--stdin` convention to document, unlike
abs-cli's `items update` / `batch-update`. When a `PATCH` command lands
(`systems update`, following `GameSystemUpdate` in the Grimoire spec), this
section is where its file/stdin input convention belongs.

## Pipeline Support

Reads compose today:

```bash
grimoire-cli systems list | jq '.[] | select(.parent_system == null)'
grimoire-cli systems get --id <system-id> | jq '.books[] | select(.category == "core")'
```
