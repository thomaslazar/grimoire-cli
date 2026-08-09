# Changelog

All notable changes to grimoire-cli are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

No releases yet. What exists on `main` so far:

### Added

- `login [--server <url>] [--username <u>] [--password <pw> | --password-stdin]` — authenticate against a Grimoire server and store the JWT (falls back to interactive prompts).
- `config <key> <value>` — read/write local configuration (server URL).
- `systems list [--sort ... ] [--desc] [--genre] [--family] [--parent-system] [--edition] [--license] [--explicit]` — list game systems.
- `systems get --id <id> [--book-sort ...] [--book-desc] [--genre] [--category] [--explicit]` — get a single game system with its books.
- `self-test` — verify binary integrity (AOT validation, no network required).
- `--debug` / `GRIMOIRE_DEBUG=1` and `--log-json` root options for HTTP call tracing and structured stderr logs.
- A seeded local Docker stack (`docker/docker-compose.yml`, `docker/seed.sh`) and `docker/smoke-test.sh` exercising the AOT binary against a running Grimoire instance.
