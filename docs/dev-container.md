# Dev container

The repo ships a dev container so the toolchain, the AOT prerequisites and the
disposable Grimoire stack all come up the same way on any machine. Open the
folder in VS Code and reopen in container.

## What it provides

From `.devcontainer/Dockerfile` (base `mcr.microsoft.com/devcontainers/dotnet:10.0`):

| Package | Why |
|---|---|
| `clang`, `zlib1g-dev` | Native AOT compilation |
| `python3`, `python3-pip` | tooling scripts |
| `python3-fitz` | MuPDF bindings — the same library Grimoire reads PDFs with, used by `docker/seed.sh` to generate fixture PDFs |
| `jq` | reading JSON responses and editing settings files |

From `devcontainer.json`: the **docker-outside-of-docker** feature and the GitHub
CLI. `post-create.sh` links the Claude Code session directory, installs plugins,
and registers the status line.

**Changing anything under `.devcontainer/` needs a container rebuild** — *Dev
Containers: Rebuild Container*. Without it the new tooling is simply absent, and
`seed.sh` fails with a message telling you so.

## Docker-outside-of-Docker, and why paths get confusing

The container talks to the **host's** Docker daemon. Two consequences that cause
most of the friction here:

1. **Bind-mount paths are resolved by the daemon, so they are host paths.**
   `./library` inside the container is meaningless to it. Set `GRIMOIRE_LIBRARY`
   and `GRIMOIRE_DATA` to the paths *as the host sees them* and keep them in
   `docker/.env` (gitignored) so you do not retype them. `docker/.env.example`
   documents the shape.
   Note the separate `GRIMOIRE_LIBRARY_LOCAL`, which is the path `seed.sh`
   **writes** to from inside the container — the same directory, addressed from
   the other side.
2. **Published ports land on the host, not on localhost here.** Reach the stack
   at `http://host.docker.internal:9481`. On a CI runner the daemon is local, so
   there it is `http://localhost:9481` — which is why every script takes
   `GRIMOIRE_SERVER` rather than hard-coding either.

## Bringing up the local stack

```bash
mkdir -p docker/data && cp docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
bash docker/smoke-test.sh
```

The fixture copy is required **before the first boot**: Grimoire seeds its users
from `/data/users.json` at startup and renames the file afterwards. Skip it and
the stack comes up with no users, whose only symptom is a 401. Logins are
`admin/admin`, `gm/gm`, `player/player` — throwaway credentials for a throwaway
stack with a fixed dev `SECRET_KEY`.

Reset:

```bash
docker compose -f docker/docker-compose.yml down && rm -rf docker/data
```

A reset — not just a re-seed — is needed after renaming or re-marking a fixture
folder, because a rescan never clears a stale `is_explicit`. See
[grimoire-api-notes.md](grimoire-api-notes.md).

## Reference material

`temp/` holds the upstream source pinned at the deployed release tag and a spec
snapshot pulled from a running instance. It is gitignored, lives in the
bind-mounted workspace so it survives rebuilds, and is populated by hand —
`post-create.sh` deliberately does not fetch it, because a fetch-on-create is a
no-op after the first run while quietly deciding which upstream ref you read.
`CLAUDE.md` has the commands.
