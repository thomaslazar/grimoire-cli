# Changelog

All notable changes to grimoire-cli are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## v0.1.0 — 2026-08-14

First release. A single self-contained binary that drives a Grimoire TTRPG
library server over its HTTP API — JSON in, JSON out, one command per endpoint —
tested against Grimoire v1.5.6.

### Highlights

- **34 commands over 31 of Grimoire's 220 API operations.** Systems, books, the
  library scanner, add-ons and metadata lookup are covered; binary endpoints
  (book files, thumbnails, page images, covers), book text extraction and the
  remaining systems endpoints are not. `docs/grimoire-api-coverage.md` is
  generated from the server's own OpenAPI spec, so the map of what is and is not
  implemented cannot drift from the API.
- **Metadata lookup through Grimoire's add-on system, on both systems and
  books.** `metadata-sources` reports which installed add-ons can answer,
  `metadata-search` returns ranked candidates, and `metadata-fetch` returns a
  per-field diff of what a source offers against what the resource already has,
  each row marked `only_incoming`, `differs` or `same`. Fetching writes nothing —
  applying is your own `update` call — so a value can be reviewed, edited or
  discarded first, and `current` ships beside `incoming` so nothing is
  overwritten blind.
- **Add-on management, because a stock instance has no sources at all.** Grimoire
  ships no bundled add-ons and does not fetch its index until asked, so on a
  fresh server the metadata commands have nothing to talk to. `addons refresh`,
  `install`, `update`, `uninstall`, `upgrade-all` and `settings` close that gap;
  script-backed add-ons need explicit per-install approval.
- **Built to be driven by agents.** `--help-full` prints each command's request
  and response shape, generated from the types the CLI parses and deserialises
  with rather than described in prose, and the notes carry the API quirks a thin
  client leaks: folder-derived fields a PATCH silently ignores, container
  children hidden before filters apply, and which failures mean the metadata
  source broke rather than the request.
- **Exit codes distinguish "failed" from "did nothing".** Exit 2 is an HTTP
  error; exit 3 is HTTP 200 that did not do what was asked — a bulk operation
  with a non-empty failure list, or a rescan that found a scan already running.
  An agent that only checks for zero would otherwise treat both as success.
- **The server version check runs daily, not only at login.** Self-hosted
  instances change version when the image is pulled, while a token stays valid
  for a month, so a login-only check would almost never fire. It probes
  `GET /api/about` before the first request of any command, throttled to once
  every 24 hours and forced fresh at login, and warns when the server moves
  outside the tested range.

### Features

- feat: add add-on response dtos
- feat: add addons install, update and uninstall
- feat: add addons list and addons refresh
- feat: add addons upgrade-all and addons settings
- feat: add book and scan-status response dtos
- feat: add books list and books get
- feat: add books reindex and books rescan
- feat: add books write commands
- feat: add bulk request and response types
- feat: add cleanup-missing response models
- feat: add library cleanup-missing command
- feat: add me command
- feat: add metadata lookup commands on systems and books
- feat: add metadata lookup response models
- feat: add metadata lookup service
- feat: add --parent-id and --include-children to systems list
- feat: add query string builder with url encoding
- feat: add strict request DTO for system updates
- feat: add systems batch-update and batch-tag commands
- feat: add systems service over the typed dtos
- feat: add systems update command
- feat: add the library scan commands
- feat: add the version staleness rule and warning text
- feat: add typed dtos for the systems responses
- feat: check the server version daily, not only at login
- feat: expose every systems query parameter as a flag
- feat: generate response shape samples for help-full
- feat: generate the api client from the openapi spec
- feat: generate typed request samples from the kiota models
- feat: persist when the server version was last checked
- feat: print typed request shapes in --help-full
- feat: reach the flag tier of config precedence on systems commands
- feat: read and validate JSON request bodies
- feat: seed a local admin and bind /data for the dev stack
- feat: send requests built by the generated client
- feat: stamp ci builds with a version suffix
- feat: surface the 1.5.5 system container fields
- feat: target grimoire 1.5.5
- feat: target grimoire 1.5.6

### Fixes

- fix: check the root's fields before parsing any value
- fix: correct license copyright to grimoire-cli
- fix: correct metadata-fetch query help and related caveats
- fix: correct rescan help text and stop masking two-cause 404s
- fix: cover enum placeholder and correct its ordering comment
- fix: don't pin the server's rescan metadata_mode default
- fix: don't report login failure for a failed version probe
- fix: drop duplicate rename/clear caveat from batch-update help
- fix: drop the stale one-page-rpgs example from --category help
- fix: fail loudly if the native request conversion returns null
- fix: harden --input/--stdin against bad paths and encoding
- fix: keep a corrupt config, restrict its mode, report write failures
- fix: keep the root path and word the partial-check warning honestly
- fix: log the response body when json parsing fails
- fix: model nullable book flags as bool?
- fix: move the explicit-marker fixture off a real publisher's system
- fix: name the query string in parse-failure messages
- fix: normalize the spec so kiota generates nested models
- fix: pin the dev stack to grimoire 1.5.4 and fix first-run setup
- fix: render enum placeholders as a value set, guard union unwrap
- fix: restore choice-option comment and pin its rendered set
- fix: scope body-validation messages to where the error occurred
- fix: scope the smoke test's rescan to a real path, not a miss
- fix: state batch-update's id rule and align shape ordering
- fix: surface addons update/install 404 and approval facts correctly
- fix: survive a corrupt config and write it atomically
- fix: url-encode the system id in the request path
- fix: use dash-form msbuild properties so windows bash builds work
- fix: warn at login when the version probe fails
- fix: widen file_size and mirror book bool coercion split

### Refactors

- refactor: add typed API client overloads, drop manual deserialize
- refactor: build login and about requests from the generated client
- refactor: build systems requests from the generated client
- refactor: build the login request from the generated body type
- refactor: drop comments that explain what was not done
- refactor: drop the hand-written request surface
- refactor: drop unreachable catch around login version check
- refactor: share the option, body-source and help-render helpers
- refactor: validate request bodies against the generated models

### Tests

- test: add pymupdf fixture generator for the local stack
- test: add smoke test covering login, token persistence and json output
- test: add token, debug-http and log-layout coverage
- test: assert a container's children in the AOT smoke test
- test: assert container behaviour in the smoke test
- test: assert every systems filter and sort in the smoke test
- test: cover books and library commands in the smoke test
- test: cover help rendering and role tagging
- test: cover library cleanup-missing
- test: cover metadata lookup against a fixture source
- test: cover non-ascii query encoding
- test: cover response DTOs in self-test AOT check
- test: cover the add-on commands against a local fixture index
- test: cover token extraction, version compare, stdin password and config resolution
- test: exercise the login body self-test actually sends
- test: fail the smoke test when the cli exits non-zero
- test: group tests by area like abs-cli
- test: harden the smoke test against a stale config
- test: prove bad systems ids exit 2 with no stack trace
- test: restructure the fixture library onto system containers
- test: seed a fixture library for the local stack
- test: tighten role-tag, model-drift and cancel-scan assertions

### Chores

- chore: give the dev stack its own docker network
- chore: rename docker/.env.example to docker/env.example
- chore: scaffold grimoire-cli development environment
- chore: scrub internal references and retire the bootstrap docs
- chore: untrack the devcontainer feature lock
- ci: add release packaging and install scripts
- ci: pin the Kiota generator to the committed lock version
- ci: port the abs-cli release skill
- ci: run the smoke test against a seeded local grimoire

### Docs

- docs: accept the generated client's binary cost
- docs: add api coverage and compatibility references
- docs: add architecture, build, container, testing and release refs
- docs: add auth, config, io and cli-design references
- docs: add enum rendering rule to typed request shapes design
- docs: add login and smoke-test design and plan
- docs: add start-here prompt, fix library bind path under docker-outside-of-docker
- docs: add systems commands design and plan
- docs: add the 1.5.5 and release-skill design and plan
- docs: add the systems write commands design, deferred
- docs: clean up rescan-conflict prose and sync spec with shipped help
- docs: correct bulk and rename claims for v1.5.5
- docs: correct scanner fixture assumptions in spec and plan
- docs: correct stale claims about release plumbing in CLAUDE.md
- docs: correct systems get --id wrinkle, one crash not two
- docs: correct the 422 operation count
- docs: correct the book dto field types in the plan
- docs: correct the category value set and book-desc caveat
- docs: correct the pruning caveat and tighten review notes
- docs: correct the response-shape docstring after generation
- docs: correct the scanner citation to v1.5.6 line numbers
- docs: correct the --server/--token rule for login
- docs: correct the smoke-test steps and the one-page aliases
- docs: correct the stale server/token precedence claim
- docs: correct the v1.5.4 rename and clear-field facts
- docs: describe the version check as daily, not login-time
- docs: design books and library commands
- docs: design library cleanup-missing
- docs: design the add-on commands
- docs: design the metadata lookup commands
- docs: design typed request shapes for --help-full
- docs: distinguish role dependencies from plain authentication
- docs: drop status reporting from the readme
- docs: drop the server-side container news from the roadmap
- docs: extract shared helpers ahead of the books commands
- docs: fix fixture manifest example and other addons doc drift
- docs: fix section docs, model count, and generator checklist
- docs: fix stale citations and comments in the write-commands work
- docs: fix stale references and miscounts from the client migration
- docs: fix the incomplete local-stack reset procedure
- docs: fix truncated string literal in the plan help text
- docs: generate the api client from the openapi spec
- docs: generate the coverage doc in abs-cli's shape
- docs: leave the changelog to the release process
- docs: link the compatibility guide and fix stale references
- docs: make abs-cli the stated reference and record the gaps
- docs: make the roadmap a list of intended work only
- docs: move request fields into --help-full and trim the notes
- docs: move the cross-cutting addon tests to task 4
- docs: pin upstream reference to v1.5.4 and record patch semantics
- docs: plan systems write commands and me
- docs: plan the server version check cadence
- docs: plan typed request shapes ahead of books
- docs: reconcile branch-created documentation inconsistencies
- docs: record case-sensitive category filter in the plan
- docs: record generated-model validation and the kiota workaround
- docs: record library cleanup-missing
- docs: record login and smoke-test completion in handover
- docs: record that rescan never clears a stale is_explicit flag
- docs: record the 1.5.5 container mechanics and coverage
- docs: record the add-on commands
- docs: record the books and library commands
- docs: record the empty --id crash as a known wrinkle
- docs: record the generated api client
- docs: record the main and tag rulesets
- docs: record the metadata lookup commands
- docs: record the override-flag and role-tag conventions
- docs: record the request-shape generator
- docs: record the version check cadence
- docs: record verified write semantics and roadmap state
- docs: refresh readme, handover and start-here for the seeded stack
- docs: remove references to private companion repos
- docs: remove references to the private management repos
- docs: reorder the roadmap around the first release
- docs: restore abs-cli's command implementation conventions
- docs: restore the batch-update rename/clear caveat
- docs: restore the full help-text conventions from abs-cli
- docs: restructure claude.md after abs-cli and move api notes to docs
- docs: rewrite readme after the abs-cli model
- docs: sharpen the role-section test step in the plan
- docs: split resolved wrinkles out and document the tap token
- docs: state server capability, not library organisation
- docs: state why abs-cli is followed, not just that it is
- docs: sync spec's books list notes with finding 3's shipped wording
- docs: tighten systems help text to abs-cli's density
