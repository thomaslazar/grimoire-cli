# Starting prompt for the first session inside the container

Paste this into a fresh Claude Code session in `/workspaces/grimoire-cli`.

---

Read `HANDOVER.md` and `CLAUDE.md` first — they carry the state of this repo and
the Grimoire API facts already established, so don't re-derive them.

Context: this is `grimoire-cli`, a CLI for a self-hosted Grimoire TTRPG library
manager, built to the same stack and conventions as my `abs-cli` (.NET 10,
Native AOT, System.CommandLine, thin pass-through, JSON in / JSON out). The
environment is set up and builds; the command surface is **not designed yet**.
What is in `src/` is scaffolding — `login`, `config`, `systems list|get`,
`self-test` — not an approved design.

Done already: `login` works against a disposable local Grimoire
(`docker/docker-compose.yml`, seeded via `/data/users.json`, logins
`admin/admin` / `gm/gm` / `player/player`), and `docker/smoke-test.sh` covers
login, token persistence and JSON output, wired into CI.

The job this CLI exists for, and the open work:

1. **Design and build the metadata command surface.** Read and fix metadata on
   existing entries: `PATCH /api/systems/{id}` and `PATCH /api/books/{id}`,
   plus `POST /api/rescan` to reapply OPF sidecars. The live instance has one
   system, `Shadowrun 6 DE` with 227 books, whose `parent_system` / `edition` /
   `system_family` were left empty on purpose as a test fixture for exactly
   this.
2. Fixture generation for the local stack (`docker/seed.sh` and library
   content under `docker/library/`) — still unwritten; login needed no
   content, so it was deferred.
3. Later: a `grimoire-management` skills repo, counterpart to `abs-management`.

Reference material is in `temp/` (gitignored): the upstream source at
`temp/grimoire/`, the live OpenAPI spec at `temp/grimoire-openapi.json`, and the
deployment design records at `temp/deployment-docs/`. The upstream source is
authoritative — the spec leaves nearly every response untyped, and the published
docs have been wrong before.

Start with step 1 — the command surface is the main open work. Brainstorm the
design with me before writing code, and don't publish this repo — it will be
public later, and `HANDOVER.md` lists what has to be scrubbed first.

---

## Notes for whoever pastes it

- Swap the opening line if you want to start somewhere other than design — e.g. "build `docker/seed.sh` first" — but keep the "read HANDOVER.md and CLAUDE.md first" instruction; it saves a lot of rediscovery.
- The live server URL is in `temp/deployment-docs/2026-08-05-zimaboard-deployment-plan.md`. Set `GRIMOIRE_SERVER` in the session, or `grimoire-cli config set server <url>`, rather than pasting it into committed files.
