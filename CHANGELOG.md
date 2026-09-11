# Changelog

All notable changes to grimoire-cli are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/).

## v0.2.0 — 2026-09-11

The release that makes grimoire-cli cover the library rather than just its
catalogue: five new command groups, a generated API client, and a rebuilt
output contract. It targets Grimoire 1.6.2 alone, and carries three breaking
changes to the CLI's own surface — `0.x`, so they arrive in a minor bump.

### Highlights

- **The library is reachable now, not just the systems and books in it.**
  `search`, `tags`, `duplicates`, `files` and `backups` join the vocabulary
  reads, taking the CLI from 20-odd commands to 75 covered endpoints. The
  point is what an agent can now *finish* without leaving the tool: find a
  duplicate, compare the copies, file one under the other as a variant, and
  delete the loser's file — or upload a book, watch it index, and fix where it
  landed.
- **Breaking: `--token` and `GRIMOIRE_TOKEN` are gone.** The access token now
  comes from the config file alone. Grimoire 1.6.0 cut the token's life from 30
  days to 30 minutes, which turns a pasted token from a convenience into a
  thing that expires mid-script; the CLI renews its own session from a stored
  refresh token instead, transparently and before the request rather than after
  a 401.
- **Breaking: stdout is the server's bytes, unmodified.** The hand-written
  response DTOs are deleted. They had to be taught every field the server
  might return, so a field Grimoire added was a field the CLI silently
  dropped — the exact failure a thin pass-through exists to avoid. Output is
  now whatever the API sent, and `--pretty` is the only thing that reshapes it.
- **The API client is generated from Grimoire's OpenAPI spec, not
  transcribed.** Kiota reads the spec straight from the pinned container, so
  the request surface cannot drift from the server it was built against, and a
  version bump produces a reviewable diff instead of a reading exercise. The
  generated models are also what validates a request body, so there is no
  hand-maintained mirror of the API's fields left to go stale.
- **Grimoire 1.6.2 only, up from 1.5.6.** The 1.6.x line made the library
  writable — which is why the `files` commands exist at all — and 1.6.2 added
  `model` as a fifth collection, which the resource-type flags now offer.
  Whoever stays on 1.5.6 stays on `0.1.x`, maintained on
  `support/grimoire-1.5.6`.
- **Every command's `--help` is written for the agent reading it.** Closed
  value sets, destructive side effects, and the server quirks a thin
  pass-through leaks are documented at the call site — folder-derived fields
  that ignore a PATCH, children hidden before filters apply, which statuses map
  to exit 3. `--help-full` adds the request and response shapes, generated from
  the spec.

### Changes

### Features

- feat: accept the model collection wherever a resource type is named
- feat: add --pretty and a raw-JSON guard and field reader
- feat: add a range-constrained int option helper
- feat: add backups list, create, delete and download
- feat: add backups settings get and set
- feat: add binary output and books thumbnail
- feat: add files browse and upload
- feat: add files move, rename and delete
- feat: add search and search fields commands
- feat: add systems book-folders list and set
- feat: add systems book-folders list, set and delete
- feat: add systems cover get, upload and delete
- feat: add tags list and tags items commands
- feat: add the backups service
- feat: add the duplicate detection and dismissal commands
- feat: add the duplicate resolution commands
- feat: add the duplicates service
- feat: add the files folder subgroup
- feat: add the files service
- feat: add the vocabulary read service
- feat: add vocabulary list commands
- feat: capture the refresh cookie at login
- feat: cut systems book-folders commands before merge
- feat: generate the api client from the edge spec
- feat: refresh the access token proactively and on expiry
- feat: store the refresh token beside the access token

### Fixes

- fix: accept --debug and --log-json after the subcommand
- fix: address discovery branch review findings
- fix: correct duplicates help text and close two guard holes
- fix: correct lookup command help text and test claims
- fix: generate repeated-key query params for array parameters
- fix: guard filesystem access and correct binary-io docs
- fix: harden files smoke block cleanup and error reporting
- fix: keep --pretty output UTF-8 safe end to end
- fix: keep untyped-node coverage honest and restore SavedFile drift check
- fix: let folder markers clear a container kind, tighten help/tests
- fix: point batch-tag response shape at BulkTagResult
- fix: read route roles from the container serving the spec
- fix: report rather than crash on a non-numeric range value
- fix: report the refresh token in config get, masked
- fix: scope DebugHttpHandlerTests memory target to its own logger
- fix: treat a version with no number as unknown, not ancient

### Refactors

- refactor: delete the hand-written response dtos
- refactor: drop --token and GRIMOIRE_TOKEN
- refactor: drop the redundant files folder delete command
- refactor: give each vocabulary its own command file
- refactor: give each vocabulary its own service
- refactor: make each vocabulary command file self-contained
- refactor: move consumer caveats off the vocabulary commands
- refactor: pass library, addons, metadata and me responses through
- refactor: pass systems, cover and books responses through
- refactor: source help samples from the generated models
- refactor: trim the vocabulary help to its real caveats

### Tests

- test: aim self-test at the raw json path
- test: avoid xUnit2031 warning in settings subgroup test
- test: correct the excluded-command count in the smoke comment
- test: cover a retired refresh token end to end
- test: cover all seven settings flags and range ceilings
- test: cover dismiss/undismiss/merge-metadata in smoke test
- test: cover system covers, book folders and binary output
- test: cover the book-folders round trip end to end
- test: cover the discovery commands in the smoke test
- test: cover the duplicates commands in the smoke test
- test: fix leaked temp files and thin test coverage from prior tasks
- test: give the fixture a book below a category directory
- test: pin the delete-folder wrapper and the upload path
- test: serialize console-mutating tests to fix parallel flake
- test: smoke-test the backup commands
- test: smoke-test the file management commands
- test: smoke-test the vocabulary read commands

### Chores

- chore: bump the pinned Grimoire to 1.6.2
- chore: bump version to 0.2.0
- chore: drop obsolete generated types from json examples
- chore: ignore python bytecode from the tools scripts
- chore: pin to Grimoire 1.6.1 and regenerate the client
- chore: pin to the released Grimoire 1.6.0
- chore: remount the dev library read-write
- ci: drop the edge compose override
- ci: pin the dev stack to an edge digest and retire the epic
- ci: pin the dev stack to the nightly digest
- ci: ride the nightly tag unpinned until 1.6.0
- ci: run the edge image as a second local stack

### Docs

- docs: add agent use cases to the readme
- docs: add the 1.6.0 migration reference
- docs: add the untyped-node placeholder step the plan dropped
- docs: assess grimoire 1.6.0 compatibility
- docs: collapse the repeated shape registrations in the plan
- docs: correct lookup-vocabulary consumer claim in cli-design
- docs: correct stale claims about tests and spec coverage
- docs: correct the BodyInputException namespace in the plan
- docs: correct the InlineData array wrapping in the plan
- docs: correct the book-folders read role in the spec
- docs: correct the duplicates enum and wrapper handling
- docs: correct the plan's aot claim about serializing a JsonElement
- docs: correct the untyped-array counts to the raw spec
- docs: correct the version strategy to one server per cli version
- docs: correct which bulk test file covers what
- docs: cut help text that restates flags and response fields
- docs: design covers, book folders and binary output
- docs: design the backup commands
- docs: design the discovery block
- docs: design the duplicates block
- docs: design the edge client and byte-passthrough output
- docs: design the file management commands
- docs: design the return of systems book-folders
- docs: design the vocabulary read commands
- docs: design transparent token refresh for 1.6.0
- docs: document the released install paths in the readme
- docs: document the search query syntax in help
- docs: drop the 1.5.6 fallback notes and the absent-flag bullet
- docs: drop the shipped ingest roadmap item
- docs: drop the shipped safety roadmap item
- docs: drop the shipped vocabularies roadmap item
- docs: drop the stub step from the duplicates plan
- docs: explain the 0.0.0 edge case in IsComparableVersion
- docs: fix a case-sensitive assertion in the plan
- docs: fix roadmap heading and search notes house style
- docs: fold task 1's review minors into the verification task
- docs: fold the single json-example generator into the assessment
- docs: gate the cleanup sweep on approval too
- docs: gate this branch on edge, not on 1.5.6
- docs: generalize the moving-channel comment in the dev pin
- docs: hand task 5's leftovers to the self-test task
- docs: list the searchable fields in search help
- docs: make the json guard's verdict testable in the plan
- docs: make the link kind vocabulary readable
- docs: name the real writability gate, not its backstop
- docs: pin the source clone to the build the stack runs
- docs: plan the book-folders implementation
- docs: plan the byte-passthrough work
- docs: plan the discovery implementation
- docs: plan the duplicates implementation
- docs: plan the switch to the nightly channel
- docs: point the update commands at the vocabularies
- docs: put the purge fact where it is actually true
- docs: re-measure the 1.6.0 migration against edge and sequence the work
- docs: record 1.6.0 session renewal
- docs: record book folders as measured against the 1.6.0 rc
- docs: record covers, book folders and the binary convention
- docs: record the backup commands and their server behaviour
- docs: record the byte-passthrough output contract
- docs: record the discovery commands and their verified behaviour
- docs: record the duplicates commands and their verified behaviour
- docs: record the file commands and their server behaviour
- docs: record the kiota response-model trial results
- docs: record the vocabulary commands in the README and coverage
- docs: regenerate coverage and correct the search note for 1.6.1
- docs: remove orphaned duplicate-handling reference from roadmap
- docs: replace the upload deviation this branch made stale
- docs: say deletes the files, not unlinks
- docs: say plainly what browse's record_id means
- docs: scope the books pipeline roadmap
- docs: show the approval gate before metadata is applied
- docs: specify the flag descriptions the plan had left blank
- docs: surface 1.6.2's changed scan, scaffold and unindex behaviour
- docs: sync the spec with the corrected help caveat
- docs: use real book and folder names in examples

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
