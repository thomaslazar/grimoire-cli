# Per-install config resolution

**Status:** approved
**Issue:** none — arises from the campaigns investigation (#51)
**Verified against:** this repo at `49b0522`; harness conventions read from
`thomaslazar/grimoire-management` at its current head

## Problem

The config path is a single hardcoded location:
`ConfigManager.DefaultConfigPath()` returns `~/.grimoire-cli/config.json` and
nothing overrides it. One config means one Grimoire account per machine per OS
user.

That becomes a wall as soon as more than one harness drives the CLI. The
`grimoire-management` harness installs its own binary per project directory —
`./bin/grimoire-cli`, with a per-machine pin at `bin/.installed.json` — and its
skills invoke it as `./bin/grimoire-cli` from the project root. So the *binary*
is already per-directory while the *config* is global: two harness directories,
two binaries, one shared account.

The immediate motivation is campaign work. Campaign writes require the
authenticated account to **be the campaign owner** — there is no admin override
(`routers/campaigns/_helpers.py:304` at `v1.7.2`) — so a harness that manages
campaigns needs to act as a dedicated GM user while the library-management
harness stays on its own account.

## Why not the obvious alternatives

**A token environment variable.** `abs-cli` solves multi-account with
`ABS_TOKEN`, and `grimoire-management`'s own `CLAUDE.md` currently claims
`GRIMOIRE_TOKEN` exists here. It does not — `GRIMOIRE_SERVER` and
`GRIMOIRE_DEBUG` are the only environment variables this CLI reads — and it must
not be added. Grimoire's access token lives 30 minutes and the CLI renews it by
writing the new tokens back to the config file
([authentication.md](../authentication.md)). A token supplied through the
environment has nowhere to be renewed into, so the session would die at the first
expiry with no recovery. abs-cli's answer does not port because its tokens are
long-lived.

**Working-directory discovery.** Walking up from the current directory for a
`.grimoire-cli/` is the git/npm shape, and it was rejected. The operator expects
two or three harness directories at most and may never use the feature at all,
so the rule would be machinery built for a maybe — and worse, it changes the
meaning of every existing invocation: a stray `.grimoire-cli/` anywhere above
the working directory would silently switch accounts.

**An environment variable alone.** Rejected as the *only* mechanism, for the
failure mode rather than the ergonomics: an agent that forgets to export it does
not fail, it silently acts as the default account. A wrong-account write that
reports success is exactly the class of failure this CLI works to eliminate. It
survives as an override, where forgetting it means "the normal thing happened".

## Design

### Resolution

`DefaultConfigPath()` resolves in this order and returns a path:

1. `GRIMOIRE_CONFIG`, if set and non-empty — the config file's full path.
2. `grimoire-cli.json` in the directory holding the running executable, **if
   that file exists**.
3. `~/.grimoire-cli/config.json` — unchanged.

Tier 2 is the one that cannot be forgotten: it is tied to the install, not to
the invocation, so `./bin/grimoire-cli` finds `./bin/grimoire-cli.json`
regardless of the working directory or the environment. Tier 1 stays for Docker,
CI and one-off runs, where moving files is the wrong lever.

**Tier 2 applies only when the file already exists.** Otherwise every install
would implicitly claim a config beside itself, and the default path would change
for everyone.

**Resolution produces a path, and every existing behaviour then applies
unchanged at it**: the atomic temp-file-and-replace write, the `0600` mode, the
corrupt-file rename to `<name>.json.corrupt` beside it, and the token renewals.
Nothing downstream of the path changes, so this is a change to one function. All
five `new ConfigManager()` call sites — `CommandHelper`, `LoginCommand`,
`ConfigCommand` (twice) and `GrimoireApiClient`'s fallback — pick it up without
edit.

The executable's directory comes from `Environment.ProcessPath`. That returns
null in exotic hosting cases, which falls through to tier 3 rather than throwing.

### Bootstrapping an install's own account

The CLI never creates a tier-2 file implicitly. Whoever sets the directory up
creates it once, and `login` then writes into it because it is what resolved:

```bash
echo '{}' > bin/grimoire-cli.json
./bin/grimoire-cli login --server https://grimoire.example.com
```

A `login --local` flag was considered and rejected: a flag that matters for one
moment in a directory's life costs more than the line of documentation it saves.

### `config get` reports the truth

`config get` already prints a `configPath` field, but it prints
`ConfigManager.DefaultConfigPath()` as a static call rather than the path in
use — correct today by accident, and a lie the moment the path is resolvable. It
becomes the resolved path, alongside a field naming which tier produced it, because
"why is this the wrong account?" is the question this feature creates and one
line of output ends it.

### Help text

The detail goes on `config get`, which is where someone debugging a wrong account
looks:

```
configPath is the file in use, resolved in order: GRIMOIRE_CONFIG;
grimoire-cli.json beside the binary, if it exists;
~/.grimoire-cli/config.json.

To give an install its own account, create the sibling file before logging
in — echo '{}' > bin/grimoire-cli.json — then login writes into it.

A symlinked binary resolves to the real file's directory.
```

`login` carries one line, since it is the command that writes credentials
somewhere, with a one-way consumer→producer cross-reference:

```
Writes the resolved config file; config get reports which one.
```

The symlink line is worth its words: on Linux the executable path resolves
through `/proc/self/exe`, which follows symlinks, so a symlinked binary looks
beside the real file rather than beside the link. Verified empirically on this
platform. It does not affect the harness, whose installer copies a real binary
into `./bin/`.

## Testing

### Unit

Resolution is a pure function of (environment, executable directory, home), so
it is testable without touching the real environment if the lookups are
injectable the way `ConfigManager.Resolve` already takes an `envLookup`:

- `GRIMOIRE_CONFIG` set → that path, whether or not the file exists.
- `GRIMOIRE_CONFIG` set to empty → ignored, falls through.
- sibling file present, no env → the sibling path.
- sibling file absent, no env → the home default.
- both present → the environment wins.
- executable path unavailable → the home default.

Plus: `config get` prints the resolved path and the tier, not the static default.

### AOT

`Environment.ProcessPath` must be confirmed against the **published AOT binary**,
not `dotnet run`, which reports the host rather than the app. This repo already
treats the AOT binary as the only check that catches AOT-specific breakage
([releasing.md](../releasing.md)). If it does not report the binary's own path
under AOT, the design's tier 2 does not work and the implementation stops rather
than shipping a tier that silently never fires.

### Smoke

One check: `GRIMOIRE_CONFIG` pointed at a scratch file, `config get` reporting
that path. The existing config-file smoke assertions already use the environment
tier, so this fits beside them. A tier-2 check is not added — it would require
writing a file next to the binary under test, which the smoke test has no
business doing.

## Documentation

`docs/configuration.md` gains the precedence table and the bootstrap recipe;
the README's Configuration section gains the same table in brief. Both currently
state the single hardcoded location as fact.

## Not in scope

- **`GRIMOIRE_TOKEN`**, for the renewal reason above. Recorded here so it is not
  added later as an obvious-looking convenience.
- **Working-directory discovery.**
- **A `login --local` flag.**
- **Any change to what the config file contains**, or to how it is read, written,
  permissioned or repaired.
