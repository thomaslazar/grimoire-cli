# grimoire-cli

A command-line interface for [Grimoire](https://github.com/hunter-read/grimoire),
the self-hosted TTRPG library manager. Built for agent-driven metadata
management — JSON in, JSON out.

Same stack and conventions as [abs-cli](https://github.com/thomaslazar/abs-cli):
.NET 10, Native AOT, `System.CommandLine`, single self-contained binary.

> **Status: early.** The development environment is set up and the toolchain is
> proven end to end. The command surface is **not designed yet** — what is in
> `src/` today is scaffolding (config, auth plumbing, `systems list`,
> `self-test`), not an approved design.

## Getting started

Open the folder in VS Code and reopen in the devcontainer. It provides .NET 10,
the AOT toolchain (`clang`, `zlib1g-dev`), `gh`, Docker-outside-of-Docker, Claude
Code, and the superpowers / ponytail / answer-first plugins.

```bash
dotnet build GrimoireCli.sln
dotnet run --project src/GrimoireCli -- --help
dotnet run --project src/GrimoireCli -- self-test     # offline integrity check
```

Against a server:

```bash
dotnet run --project src/GrimoireCli -- login --server https://grimoire.example.com
dotnet run --project src/GrimoireCli -- systems list | jq
```

A disposable local instance lives in `docker/`:

```bash
cd docker && docker compose up -d      # http://localhost:9481
```

Drop fixture content into `docker/library/` (Grimoire's layout: `books/{system}/{category}/`)
and pick it up with `POST /api/rescan`.

## Layout

| path | what |
|---|---|
| `src/GrimoireCli/` | the CLI |
| `tests/GrimoireCli.Tests/` | unit tests |
| `docker/` | disposable Grimoire stack for development |
| `docs/` | specs and plans |
| `temp/` | reference material, gitignored — see `CLAUDE.md` |

## Related

- [`deployment-repo`](https://github.com/thomaslazar/deployment-repo) — the deployed instance: compose, design records, upstream bug reports
- `management-repo` — planned skills/rules repo, the counterpart to `management-repo`
