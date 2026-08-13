# Server version check cadence — design

**Date:** 2026-08-13
**Status:** approved, not yet implemented
**Targets:** Grimoire **v1.5.6**, the version the CLI targets.

Ported from `abs-cli`'s [PR #75](https://github.com/thomaslazar/abs-cli/pull/75)
and its `docs/specs/2026-08-12-server-version-check-cadence-design.md`, whose
"Portability to grimoire-cli" section anticipated this change and named the two
things that would not port: the endpoint and the interval. Both are settled
below. Everything else is that design, applied to a smaller client.

## Problem

`GrimoireApiClient.CheckServerVersion` is called from exactly one place —
`LoginCommand.cs:117`, reading `version` from a `GET /api/about` made just after
the token is saved. It compares against `MinSupportedVersion` /
`MaxTestedVersion` (both `1.5.6`) and warns when the server is below the floor or
above the tested ceiling.

Binding the check to login is wrong for one reason above all: **nothing about a
login correlates with the server changing.** These are self-hosted servers whose
version changes when an image is pulled — an event involving no login, which the
CLI cannot observe.

Here the gap is wider than in `abs-cli`. Grimoire issues a **30-day JWT with no
refresh endpoint** (`docs/grimoire-api-notes.md`; there is no `/auth/refresh` to
piggyback on), so a working install logs in at most monthly and can go a month
without any verdict at all. `abs-cli`'s live stack was upgraded and went
unnoticed for weeks on an hourly-refreshing token; the same blindness here lasts
longer.

Two clarifications, to keep the scope honest:

- The in-range case logs at **Debug**, invisible without `--debug` /
  `GRIMOIRE_DEBUG=1`. Between logins the CLI emits nothing about the version — so
  the defect is a **missing warning**, not a stale claim.
- The check is warn-only and stays that way. Real API drift announces itself
  concretely: a deserialization failure, a 404, a field that vanished. This
  check's job is **provenance** — "you are off the versions the maintainer
  exercised" — and it must not pretend to be protection.

The tested window here is a single point (`1.5.6` to `1.5.6`), so **every**
upstream release trips the ceiling branch. That is correct under the wording
above, not a bug to design around: upstream does not treat patch releases as
behaviour-preserving — 1.5.6 shipped a new feature and a fix for a
database-destroying bug in the same release.

## Goals

- Surface a verdict within 24 hours of the server changing, without depending on
  the operator logging in.
- Never cost a round-trip on every invocation.
- Never let the check fail, slow, or interrupt the command it precedes.
- Keep the warning actionable: name the versions and say what to do.

## Non-goals

- Blocking or refusing to run on an out-of-range server. Warn-only, unchanged.
- Checking whether a newer *CLI* exists. No calls to GitHub or any release feed.
- Making the interval configurable — see "Rejected alternatives".
- Any change to the `MinSupportedVersion` / `MaxTestedVersion` model itself.

## Design

### Trigger

`GrimoireApiClient` has **one** choke point where `abs-cli` had nine: every
request goes through `SendAsync(RequestInformation, …)`, and the typed overload
delegates to it. That method already calls `WarnIfTokenExpired()`. It gains:

```csharp
private bool _versionCheckDone;

private async Task PreflightAsync()
{
    WarnIfTokenExpired();
    await EnsureVersionCheckedAsync();
}
```

The once-per-process guard belongs to the **version check only**.
`WarnIfTokenExpired` stays per-request: it is local, costs no round-trip, and a
long-running command can cross the expiry mid-run.

`config` and `self-test` never construct a client, so they are unaffected for
free.

### Staleness decision

A pure, internal, directly tested function:

```csharp
internal static readonly TimeSpan VersionCheckInterval = TimeSpan.FromHours(24);

internal static bool ShouldCheckVersion(DateTimeOffset? lastCheck, DateTimeOffset now)
    => lastCheck is null
       || now - lastCheck.Value >= VersionCheckInterval
       || lastCheck.Value > now;   // clock moved backwards — treat as stale
```

24 hours, hardcoded, matching `abs-cli`. Worst-case staleness is a day against
the month-long blindness that motivates this. A shorter interval was defensible
given the single-point window, and was considered and declined: one verdict per
day is enough to catch an image pull, and the same constant in both tools is one
less thing to remember.

### Probe

`GET /api/about`, **authenticated**, returning `{version, commit_hash,
python_version}` (`temp/grimoire/backend/routers/library/core.py:116-130`).

This is the one place the port genuinely diverges. `abs-cli` probes ABS's
unauthenticated `/status`; Grimoire has no endpoint that is both unauthenticated
and cheap. Measured against the pinned 1.5.6 stack on 2026-08-13:

| endpoint | auth | size | version |
|---|---|---|---|
| `/api/about` | **required** — 401 without a token | 103 B, 25 ms | `version` |
| `/api/openapi.json` | none | 252 KB, 122 ms | `info.version` |
| `/api/health` | none | tiny | none |

`/api/about` is deliberately gated upstream — its own description says "Login
required — deliberately not exposed on the API-key-gated /stats endpoint so build
details aren't leaked to external integrations" — and upstream asserts the 401
(`backend/tests/test_library.py:111`).

Authentication costs little here. When the token is dead the probe 401s, the
check is skipped, and the timestamp is left alone; the real command then fails a
moment later with `Run: grimoire-cli login`, and login records the version
itself. So the unauthenticated alternative would only help in the state where the
operator is about to log in anyway — and it would cost 252 KB per day to read one
string, from the artifact the client is generated from.

Mechanics:

- Built from the generated builder: `Api.Api.About.ToGetRequestInformation()`.
- Sent through the **raw** send path, bypassing `PreflightAsync`, so the probe
  cannot re-enter the version check that triggered it.
- Its **own 3-second timeout** via a dedicated `CancellationTokenSource`, not the
  100-second client default. A hung server must not stall the real command behind
  a diagnostic.
- The version is read with the existing `ReadStringProperty(body, "version")`,
  which is what `login` already uses for this. **No new DTO**, no `AppJsonContext`
  entry, and no `self-test` round-trip — `abs-cli` needed a `ServerStatus` type
  only because it had no such helper.

### Comparison and message

`CompareVersions` is reused unchanged — already `internal` and unit-tested.
`CheckServerVersion` is replaced by a pure function returning text or null, so
the wording is testable without capturing logs:

```csharp
internal static string? VersionWarning(string? observed, string? previous);
```

Ceiling, the case that actually occurs:

```
grimoire-cli 0.1.0 was tested up to Grimoire 1.5.6; this server is 1.6.0.
Check for a newer grimoire-cli.
```

When the observed version differs from `lastServerVersion`, the message names the
change, since that is the operator's real signal:

```
This server moved from Grimoire 1.5.6 to 1.6.0 since the last check.
grimoire-cli 0.1.0 was tested up to 1.5.6. Check for a newer grimoire-cli.
```

The floor message keeps its current wording. Warnings go to stderr, as now.

Rate limiting is implicit: at most one check per 24 hours means at most one
warning per day. No separate "already warned" bookkeeping.

### Persistence

Two new `AppConfig` fields, camelCase keys matching the existing two:

```csharp
[JsonPropertyName("lastVersionCheck")]
public DateTimeOffset? LastVersionCheck { get; set; }

[JsonPropertyName("lastServerVersion")]
public string? LastServerVersion { get; set; }
```

`lastServerVersion` pays for itself twice: it lets the warning say the version
*changed* rather than merely stating it, and it answers "what was I talking to?"
after a failure.

**Both are written by a read-modify-write of the on-disk config, never by
`Save(_config)`:**

```csharp
public void UpdateVersionCheck(string? serverVersion, DateTimeOffset checkedAt)
{
    var onDisk = Load();          // file only — deliberately not Resolve()
    onDisk.LastServerVersion = serverVersion;
    onDisk.LastVersionCheck = checkedAt;
    Save(onDisk);
}
```

`Resolve()` merges `GRIMOIRE_SERVER` and `GRIMOIRE_TOKEN` from the environment
into the in-memory config (`ConfigManager.cs:53,56`), so persisting the resolved
config would write a token the operator deliberately kept out of the file. A
daily check would make that routine.

Consequence to accept: for an operator with no config file, the first check
**creates** `~/.grimoire-cli/config.json` holding only these two fields. No
secrets, and it stops a re-probe on every invocation. If the file cannot be
written, the failure is swallowed and the next invocation probes again.

### Failure handling

Any probe failure — unreachable, timeout, non-2xx, non-JSON, missing `version` —
is caught, logged at Debug, and **does not advance the timestamp**, so the next
invocation retries. If the server is genuinely down, the real command fails a
moment later with a useful error; the diagnostic must never be the thing that
reports it.

Note the probe must not route through `EnsureSuccessAsync`, which maps a non-2xx
to a message and `Environment.Exit(2)`. A failed diagnostic may not take down the
command it precedes.

### The recorder

One method owns "a version was observed", so probe and login share it exactly:

```csharp
// Warns per the rules above, then persists via ConfigManager.UpdateVersionCheck.
internal void RecordServerVersion(string? observed);
```

`EnsureVersionCheckedAsync` reads `LastVersionCheck` from the in-memory resolved
config — reading merged values is fine, only *writing* them is the hazard — calls
`ShouldCheckVersion`, probes if stale, and hands the result to
`RecordServerVersion`.

### Login

`login` currently makes its own `/api/about` call and calls `CheckServerVersion`
(`LoginCommand.cs:111-118`). Since the probe *is* that call, login instead asks
for one unconditionally:

```csharp
public async Task CheckVersionNowAsync();   // probes, records, marks done
```

A login is exactly when a fresh verdict is wanted, so it ignores the 24-hour
window. This removes the duplicated about-and-compare code, and means the token
just saved is what authenticates the probe. Its existing behaviour is preserved:
the call sits outside login's own `try`, and a failure there is a stderr warning
that still exits 0 — login genuinely succeeded.

### `config get`

`config get` prints a fixed dictionary; both new fields join it so the state is
inspectable. Neither is settable via `config set`, which stays limited to
`server`.

## Rejected alternatives

- **`GET /api/openapi.json` for an unauthenticated probe.** Matches `abs-cli`'s
  "works with a dead token" property, but costs 252 KB and 122 ms to read one
  string — 2,400× the payload — and overloads the artifact the client is
  generated from with a runtime role. The property it buys is nearly worthless
  here, per "Probe" above.
- **`GET /api/health`.** Unauthenticated and tiny, but carries no version
  (`backend/main.py:195-215`): status and dependency checks only.
- **Probe on every invocation, no persistence.** Simplest code, always current,
  but a round-trip per command for a diagnostic cuts against thin pass-through
  for no gain over a 24-hour window.
- **A shorter interval than `abs-cli`'s.** Defensible — every upstream release
  trips this CLI's single-point window — but a day already catches an image pull,
  and one constant across both tools is one less thing to remember.
- **Configurable interval.** A config key plus env var plus help text plus docs,
  to tune a constant with one sensible value. If 24 hours proves wrong, changing
  it is a patch release.
- **Blocking on an out-of-range server.** The check is provenance, not
  protection. Warn-only, unchanged.

## Testing

- **Unit:** `ShouldCheckVersion` — null → due, 23 h → not due, exactly 24 h → due
  (the boundary is `>=`), 25 h → due, future timestamp → due. `VersionWarning`
  wording for floor, ceiling, in-range (null), and the moved-since-last-check
  case. `UpdateVersionCheck` preserving unrelated on-disk fields and **not**
  persisting env-derived values.
- **Any new test that makes production code log joins `[Collection("NLog")]`.**
  NLog's configuration is process-global and the fixture already exists here
  (`tests/GrimoireCli.Tests/NLogCollection.cs`) for exactly this reason.
- **Smoke**, with `GRIMOIRE_DEBUG=1` and the config file under the test's control:
  login records a version without a second probe; a second invocation inside the
  window does not probe; a backdated `lastVersionCheck` triggers one; the
  timestamp advances afterwards.
- No `self-test` change: no new DTO crosses the JSON boundary.

## Docs

- `docs/grimoire-compatibility.md` — the "Runtime check" section describes
  login-only behaviour that this change makes false. Rewrite it.
- `docs/configuration.md` — document the two keys as CLI-managed state, not
  settable by hand.
- README Commands table — no change; no verb or flag is added or removed.

## Differences from `abs-cli`'s implementation

Recorded so a reader comparing the two does not "fix" one into the other:

- **One preflight call site, not nine.** Every request here already funnels
  through `SendAsync(RequestInformation, …)`.
- **No `ServerStatus` DTO.** `ReadStringProperty` already exists and is what
  `login` uses to read this exact field.
- **`login` shares the probe path** rather than keeping its own about-and-compare
  code, which also removes the risk of a probe re-entering preflight.
- **The probe is authenticated**, because no Grimoire endpoint is both
  unauthenticated and cheap.
