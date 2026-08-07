# Systems Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `systems list` and `systems get` to the standard abs-cli established — every query parameter exposed as a flag, typed DTOs, a service layer, generated response shapes in `--help-full` — and the seeded fixture library and smoke assertions that prove it.

**Architecture:** `docker/seed.sh` writes a fixture library with PyMuPDF-generated PDFs, rescans, and PATCHes the metadata folders can't express. Commands become declarations over `SystemsService`, which builds query strings with `QueryBuilder` and deserializes into DTOs registered on `AppJsonContext`. A `tools/GenerateResponseExamples` console app reflects over those DTOs to emit `ResponseExamples.g.cs`, which `--help-full` renders.

**Tech Stack:** .NET 10, Native AOT, System.CommandLine 2.0.7, xunit.v3, bash + curl + python3 (PyMuPDF), Docker Compose, GitHub Actions.

## Global Constraints

- Target Grimoire **v1.5.4** only. The deployed instance runs commit `3466247`, identical to the `v1.5.4` tag in `temp/grimoire`.
- `OPDS_ENABLED=false` must stay set: with OPDS on, `GET /api/openapi.json` returns 500 (upstream #276).
- Run `dotnet format GrimoireCli.sln` after modifying any C# file. CI enforces `--verify-no-changes`.
- No blank lines inside method bodies between consecutive declarations, `AddCommand`/`AddOption` calls, or before a `return` that follows setup calls.
- **Comments explain what the code does or why it must be so — never what was deliberately left out.** State requirements positively.
- Conventional Commits: `type: subject`, imperative, lowercase, no trailing period, ≤72 chars. No `Co-Authored-By`, no generated-with attribution.
- stdout is API JSON only; all logs and human-facing lines go to stderr.
- Branch is `feat/systems-commands`, already created. Never commit to `main`.
- Never stage anything from `temp/`, `.superpowers/`, `docker/data/`, `docker/library/` or `docker/.env`.
- The live instance is **read-only** for this work. Never PATCH it. All writes go to the local stack.

---

### Task 1: Commit the groundwork

The working tree already carries the spec and two changes made while writing it.

**Files:**
- Create: `docs/specs/2026-08-07-systems-commands-design.md` (already written)
- Create: `docs/plans/2026-08-07-systems-commands.md` (already written)
- Modify: `.devcontainer/Dockerfile` (already edited — adds `python3-fitz`)
- Modify: `README.md` (already edited — dev-container tooling + rebuild note)

- [ ] **Step 1: Confirm the tree**

Run: `git status --short && git branch --show-current`
Expected: branch `feat/systems-commands`; modified `.devcontainer/Dockerfile` and `README.md`; untracked `docs/specs/2026-08-07-systems-commands-design.md` and `docs/plans/2026-08-07-systems-commands.md`. Nothing from `temp/` or `docker/library/`.

- [ ] **Step 2: Verify the baseline is clean**

Run: `dotnet format GrimoireCli.sln --verify-no-changes && dotnet build GrimoireCli.sln && dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: format silent, build 0 errors, 24/24 tests pass.

- [ ] **Step 3: Commit**

```bash
git add docs/specs/2026-08-07-systems-commands-design.md \
        docs/plans/2026-08-07-systems-commands.md \
        .devcontainer/Dockerfile README.md
git commit -m "docs: add systems commands design and plan"
```

---

### Task 2: Fixture generator

**Files:**
- Create: `docker/make-fixtures.py`

**Interfaces:**
- Produces: `python3 docker/make-fixtures.py <output-path> <pages>` writes a valid PDF with that many pages. Task 3 calls it.

- [ ] **Step 1: Write the script**

`docker/make-fixtures.py`:

```python
#!/usr/bin/env python3
"""Generate fixture PDFs for the local Grimoire stack.

Uses PyMuPDF — the same library Grimoire reads PDFs with — so anything written
here is parseable by the indexer. Install with: sudo apt-get install -y python3-fitz
(the devcontainer image does this; rebuild the container if the import fails).

Usage: make-fixtures.py <path> <pages>
"""
import sys

try:
    import fitz
except ImportError:
    sys.exit(
        "python3-fitz (PyMuPDF) is required to generate fixtures.\n"
        "  devcontainer: rebuild the container, or "
        "sudo apt-get install -y python3-fitz"
    )


def make_pdf(path: str, pages: int) -> None:
    doc = fitz.open()
    for i in range(pages):
        page = doc.new_page()
        page.insert_text((72, 72), f"grimoire-cli fixture — page {i + 1}")
    doc.save(path)
    doc.close()


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("Usage: make-fixtures.py <path> <pages>")
    make_pdf(sys.argv[1], int(sys.argv[2]))
```

- [ ] **Step 2: Verify it produces a PDF the reader accepts**

```bash
python3 docker/make-fixtures.py /tmp/fixture-check.pdf 4
python3 -c "
import fitz
d = fitz.open('/tmp/fixture-check.pdf')
assert d.page_count == 4, f'expected 4 pages, got {d.page_count}'
print('ok:', d.page_count, 'pages')
"
rm -f /tmp/fixture-check.pdf
```
Expected: `ok: 4 pages`. If the import fails, the container needs rebuilding — stop and report rather than working around it.

- [ ] **Step 3: Commit**

```bash
git add docker/make-fixtures.py
git commit -m "test: add pymupdf fixture generator for the local stack"
```

---

### Task 3: Seed script and fixture library

**Files:**
- Create: `docker/seed.sh`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `docker/make-fixtures.py` from Task 2.
- Produces: a seeded stack with **8 systems**. Task 10's smoke assertions depend on the exact fixture set below.

Verified API facts this task depends on:
- `POST /api/rescan` body `{"metadata_mode":"new"}` returns `{"status":"scan_started"}`.
- `GET /api/scan-status` returns `{"running": bool, "total_books": n, "scanned_books": n, ...}`. **`running` is `false` before a scan starts as well as after it ends**, so completion must be tested with `scanned_books`.
- One immediate subdirectory of `books/` = one system; `name` is the folder name.
- `(nsfw)` in a folder name sets `is_explicit` and is stripped from the name.
- Leading `!$%` characters are stripped from the name.
- Category folder names normalise to canonical singular values: `supplements/` → `supplement`, `adventures/` → `adventure`.

- [ ] **Step 1: Ignore generated fixtures**

Append to `.gitignore`:

```gitignore
# Fixture library generated by docker/seed.sh
docker/library/*
!docker/library/.gitkeep
```

- [ ] **Step 2: Write the seed script**

`docker/seed.sh`:

```bash
#!/usr/bin/env bash
# Seed the local Grimoire stack with a fixture library.
#
#   bash docker/seed.sh
#   GRIMOIRE_SERVER=http://localhost:9481 bash docker/seed.sh
#
# Writes fixture PDFs into the library directory, rescans, then PATCHes the
# metadata that folder structure cannot express (edition, family, parent system,
# genre, license, year). Grimoire mounts the library read-only, so seeding writes
# from this side and the server only reads.
#
# Re-runnable: the library is rebuilt from scratch each time. To reset the
# database as well: docker compose -f docker/docker-compose.yml down && rm -rf docker/data
set -euo pipefail

SERVER="${GRIMOIRE_SERVER:-http://host.docker.internal:9481}"
LIBRARY="${GRIMOIRE_LIBRARY_LOCAL:-docker/library}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fail() { echo "SEED FAIL: $*" >&2; exit 1; }
say() { echo "  $*" >&2; }

python3 -c "import fitz" 2>/dev/null \
  || fail "python3-fitz (PyMuPDF) missing — rebuild the devcontainer, or: sudo apt-get install -y python3-fitz"

# 1. Wait for the instance.
for i in $(seq 1 60); do
  curl -sf "$SERVER/api/health" >/dev/null 2>&1 && break
  [ "$i" -eq 60 ] && fail "no response from $SERVER/api/health after 60s"
  sleep 1
done
say "health ok"

# 2. Authenticate.
TOKEN=$(curl -sf -X POST "$SERVER/api/auth/login" \
  -H 'content-type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq -r .token)
[ -n "$TOKEN" ] && [ "$TOKEN" != null ] || fail "login failed — is the stack seeded with docker/users.json.example?"
AUTH="Authorization: Bearer $TOKEN"
say "authenticated"

# 3. Build the fixture tree. Folder names carry edition and language because the
#    scanner parses neither from the path — one folder is exactly one system.
rm -rf "${LIBRARY:?}/books"
mkdir -p "$LIBRARY/books"

book() {  # book <system-folder> <category-folder> <filename> <pages>
  local dir="$LIBRARY/books/$1/$2"
  mkdir -p "$dir"
  python3 "$HERE/make-fixtures.py" "$dir/$3.pdf" "$4"
}

book "Shadowrun 6 DE"                  core         "SR6 Grundregelwerk"      12
book "Shadowrun 6 DE"                  core         "SR6 Kreuzfeuer"           8
book "Shadowrun 6 DE"                  supplements  "SR6 Strassengrimoire"     6
book "Shadowrun 5 DE"                  core         "SR5 Grundregelwerk"      10
book "Shadowrun 5 DE"                  core         "SR5 Datenpfade"           5
book "Shadowrun 4 DE"                  core         "SR4 Grundregelwerk"       7
book "!!Dungeons & Dragons 5e EN"      core         "Players Handbook"        14
book "!!Dungeons & Dragons 5e EN"      adventures   "Lost Mine of Phandelver"  9
book "Das Schwarze Auge 5 DE"          core         "DSA5 Regelwerk"          11
book "Das Schwarze Auge 5 DE"          core         "DSA5 Aventurien"          4
book "The Dark Eye 5 EN"               core         "TDE5 Core Rules"         11
book "Vampire The Masquerade 5 EN (nsfw)" core      "V5 Corebook"             13

# one-page-rpgs is a special collection: the scanner makes it ONE system whose
# subfolder names become category labels, not one system per file. A loose PDF at
# the books root is skipped entirely (scan.py requires a directory), so none is
# seeded — it would never be indexed yet would still inflate total_books.
mkdir -p "$LIBRARY/books/one-page-rpgs"
python3 "$HERE/make-fixtures.py" "$LIBRARY/books/one-page-rpgs/Lasers and Feelings.pdf" 1
python3 "$HERE/make-fixtures.py" "$LIBRARY/books/one-page-rpgs/Honey Heist.pdf" 1

EXPECTED_BOOKS=14
say "wrote $EXPECTED_BOOKS fixture books"

# 4. Rescan, then wait for completion. `running` reads false before the scan
#    starts too, so completion is tested with scanned_books.
curl -sf -X POST "$SERVER/api/rescan" -H "$AUTH" \
  -H 'content-type: application/json' -d '{"metadata_mode":"new"}' >/dev/null \
  || fail "rescan request failed"
for i in $(seq 1 90); do
  ST=$(curl -sf "$SERVER/api/scan-status" -H "$AUTH")
  RUNNING=$(echo "$ST" | jq -r .running)
  SCANNED=$(echo "$ST" | jq -r .scanned_books)
  if [ "$RUNNING" = false ] && [ "$SCANNED" -ge "$EXPECTED_BOOKS" ]; then break; fi
  [ "$i" -eq 90 ] && fail "scan did not finish: running=$RUNNING scanned=$SCANNED expected>=$EXPECTED_BOOKS"
  sleep 1
done
say "scan complete ($SCANNED books)"

# 5. Apply the metadata folders cannot express. Shadowrun 4 DE is deliberately
#    left raw — it mirrors a fresh import and is the fixture the future metadata
#    commands will target.
patch_system() {  # patch_system <system name> <json body>
  local name="$1" body="$2" id
  id=$(curl -sf "$SERVER/api/systems" -H "$AUTH" \
       | jq -r --arg n "$name" '.[] | select(.name == $n) | .id')
  [ -n "$id" ] || fail "no system named '$name' after the scan"
  curl -sf -X PATCH "$SERVER/api/systems/$id" -H "$AUTH" \
    -H 'content-type: application/json' -d "$body" >/dev/null \
    || fail "PATCH failed for '$name'"
  say "patched $name"
}

patch_system "Shadowrun 6 DE" '{"system_family":"Shadowrun","parent_system":"Shadowrun","edition":"6","genres":["Cyberpunk"],"year":2019,"publishers":[{"name":"Pegasus Spiele","url":""}]}'
patch_system "Shadowrun 5 DE" '{"system_family":"Shadowrun","parent_system":"Shadowrun","edition":"5","genres":["Cyberpunk"],"year":2013,"publishers":[{"name":"Pegasus Spiele","url":""}]}'
patch_system "Dungeons & Dragons 5e EN" '{"system_family":"D&D","parent_system":"Dungeons & Dragons","edition":"5e","genres":["Fantasy"],"license":"OGL","year":2014,"publishers":[{"name":"Wizards of the Coast","url":""}]}'
patch_system "Das Schwarze Auge 5 DE" '{"system_family":"The Dark Eye","parent_system":"Das Schwarze Auge","edition":"5","genres":["Fantasy"],"year":2015,"publishers":[{"name":"Ulisses Spiele","url":""}]}'
patch_system "The Dark Eye 5 EN" '{"system_family":"The Dark Eye","parent_system":"The Dark Eye","edition":"5","genres":["Fantasy"],"year":2016,"publishers":[{"name":"Ulisses North America","url":""}]}'
patch_system "Vampire The Masquerade 5 EN" '{"system_family":"World of Darkness","parent_system":"Vampire: The Masquerade","edition":"5","genres":["Horror"],"year":2018,"publishers":[{"name":"Renegade Game Studios","url":""}]}'

COUNT=$(curl -sf "$SERVER/api/systems" -H "$AUTH" | jq 'length')
say "seed complete — $COUNT systems"
[ "$COUNT" -eq 8 ] || fail "expected 8 systems, got $COUNT"
```

Note the PATCH names have no `!!` prefix and no `(nsfw)` suffix: the scanner strips both, so the stored names are `Dungeons & Dragons 5e EN` and `Vampire The Masquerade 5 EN`. If `patch_system` reports "no system named …", that assumption broke and is worth reporting rather than renaming the fixture folders.

- [ ] **Step 3: Reset the stack and run it**

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data && mkdir -p docker/data
cp docker/users.json.example docker/data/users.json
GRIMOIRE_LIBRARY=/path/to/grimoire-cli/docker/library \
GRIMOIRE_DATA=/path/to/grimoire-cli/docker/data \
  docker compose -f docker/docker-compose.yml up -d --wait
chmod +x docker/seed.sh
bash docker/seed.sh
```

Substitute the host path this repo is bind-mounted from — under docker-outside-of-docker the daemon resolves bind paths on the host. Expected: ends with `seed complete — 8 systems`.

- [ ] **Step 4: Verify the fixture set matches what Task 10 will assert**

```bash
TOKEN=$(curl -sf -X POST http://host.docker.internal:9481/api/auth/login \
  -H 'content-type: application/json' -d '{"username":"admin","password":"admin"}' | jq -r .token)
curl -sf "http://host.docker.internal:9481/api/systems?genre=Cyberpunk" -H "Authorization: Bearer $TOKEN" | jq 'length'
curl -sf "http://host.docker.internal:9481/api/systems?family=Shadowrun" -H "Authorization: Bearer $TOKEN" | jq 'length'
curl -sf "http://host.docker.internal:9481/api/systems?explicit=true" -H "Authorization: Bearer $TOKEN" | jq '.[].name'
curl -sf "http://host.docker.internal:9481/api/systems?edition=5" -H "Authorization: Bearer $TOKEN" | jq 'length'
```
Expected: `2`, `2` (Shadowrun 4 DE is raw and must not match), `"Vampire The Masquerade 5 EN"`, `4` — edition `5` is carried by Shadowrun 5 DE, Das Schwarze Auge 5 DE, The Dark Eye 5 EN and Vampire The Masquerade 5 EN.

If `explicit=true` returns nothing, the `(nsfw)` folder convention did not apply — report it; do not fall back to PATCHing `is_explicit`, because the fixture exists to prove the scanner behaviour.

- [ ] **Step 5: Commit**

```bash
git add docker/seed.sh .gitignore
git commit -m "test: seed a fixture library for the local stack"
```

---

### Task 4: Response DTOs

**Files:**
- Create: `src/GrimoireCli/Models/GameSystemSummary.cs`
- Create: `src/GrimoireCli/Models/GameSystemDetail.cs`
- Create: `src/GrimoireCli/Models/Book.cs`
- Create: `src/GrimoireCli/Models/PublisherEntry.cs`
- Create: `src/GrimoireCli/Models/LinkEntry.cs`
- Modify: `src/GrimoireCli/Models/JsonContext.cs`
- Create: `tests/GrimoireCli.Tests/GameSystemDtoTests.cs`

**Interfaces:**
- Produces: `GameSystemSummary`, `GameSystemDetail : GameSystemSummary` (adds `Books`), `Book`, `PublisherEntry`, `LinkEntry`, and `AppJsonContext` entries for each plus `List<GameSystemSummary>`. Tasks 6, 7 and 9 consume these.

No `[JsonExtensionData]`. The CLI's output is a contract; an unmodelled field means the DTOs need updating under the version-bump procedure, not a passthrough bucket.

- [ ] **Step 1: Write the nested types**

`src/GrimoireCli/Models/PublisherEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class PublisherEntry
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
```

`src/GrimoireCli/Models/LinkEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class LinkEntry
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
```

- [ ] **Step 2: Write `GameSystemSummary`**

One property per field below, in this order, each with a `[JsonPropertyName]` matching the API name exactly. Source: `backend/routers/systems/_serializers.py::serialize_system_summary`.

| API field | C# property | type |
|---|---|---|
| `id` | `Id` | `string?` |
| `name` | `Name` | `string?` |
| `slug` | `Slug` | `string?` |
| `description` | `Description` | `string?` |
| `publishers` | `Publishers` | `List<PublisherEntry>?` |
| `character_builder_url` | `CharacterBuilderUrl` | `string?` |
| `character_builder_urls` | `CharacterBuilderUrls` | `List<LinkEntry>?` |
| `urls` | `Urls` | `List<LinkEntry>?` |
| `tags` | `Tags` | `List<string>?` |
| `genre` | `Genre` | `string?` |
| `genres` | `Genres` | `List<string>?` |
| `dice_materials` | `DiceMaterials` | `List<string>?` |
| `system_family` | `SystemFamily` | `string?` |
| `parent_system` | `ParentSystem` | `string?` |
| `edition` | `Edition` | `string?` |
| `license` | `License` | `string?` |
| `year` | `Year` | `int?` |
| `book_count` | `BookCount` | `int` |
| `total_page_count` | `TotalPageCount` | `int` |
| `cover_image` | `CoverImage` | `string?` |
| `cover_book_id` | `CoverBookId` | `string?` |
| `is_explicit` | `IsExplicit` | `bool` |
| `is_system_agnostic` | `IsSystemAgnostic` | `bool` |
| `is_one_page` | `IsOnePage` | `bool` |

Shape of each property:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

public class GameSystemSummary
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("book_count")]
    public int BookCount { get; set; }

    // … one property per row of the table above, same order
}
```

- [ ] **Step 3: Write `Book` and `GameSystemDetail`**

`Book` fields, from `serialize_book` in the same file — 32 in this order: `id` `Id` string?, `title` `Title` string?, `filename` `Filename` string?, `category` `Category` string?, `description` `Description` string?, `page_count` `PageCount` int?, `file_size` `FileSize` long?, `mime_type` `MimeType` string?, `authors` `Authors` List&lt;string&gt;?, `artists` `Artists` List&lt;string&gt;?, `genres` `Genres` List&lt;string&gt;?, `publisher` `Publisher` string?, `publisher_url` `PublisherUrl` string?, `urls` `Urls` List&lt;LinkEntry&gt;?, `isbn` `Isbn` string?, `version` `Version` string?, `language` `Language` string?, `license` `License` string?, `year` `Year` int?, `month` `Month` int?, `day` `Day` int?, `indexed` `Indexed` bool, `index_failed` `IndexFailed` bool, `index_error` `IndexError` string?, `ocr_indexed` `OcrIndexed` bool, `ocr_dpi` `OcrDpi` int?, `has_thumbnail` `HasThumbnail` bool, `tags` `Tags` List&lt;string&gt;?, `is_explicit` `IsExplicit` bool, `is_missing` `IsMissing` bool, `relative_path` `RelativePath` string?.

`src/GrimoireCli/Models/GameSystemDetail.cs`:

```csharp
using System.Text.Json.Serialization;

namespace GrimoireCli.Models;

/// <summary>
/// GET /api/systems/{id} — the summary shape plus the system's books. Filters on
/// that endpoint apply to the book list, and book_count / total_page_count are
/// recomputed from the filtered list.
/// </summary>
public class GameSystemDetail : GameSystemSummary
{
    [JsonPropertyName("books")]
    public List<Book>? Books { get; set; }
}
```

- [ ] **Step 4: Register everything on `AppJsonContext`**

Add above the existing attributes in `src/GrimoireCli/Models/JsonContext.cs`:

```csharp
[JsonSerializable(typeof(GameSystemSummary))]
[JsonSerializable(typeof(GameSystemDetail))]
[JsonSerializable(typeof(List<GameSystemSummary>))]
[JsonSerializable(typeof(Book))]
[JsonSerializable(typeof(PublisherEntry))]
[JsonSerializable(typeof(LinkEntry))]
```

- [ ] **Step 5: Write the tests**

`tests/GrimoireCli.Tests/GameSystemDtoTests.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests;

public class GameSystemDtoTests
{
    // A trimmed real response from the live instance: the field names must match
    // exactly or values silently deserialize to null.
    private const string SummaryJson = """
    {"id":"abc","name":"Shadowrun 6 DE","slug":"shadowrun-6-de","description":null,
     "publishers":[{"name":"Pegasus Spiele","url":""}],"character_builder_url":null,
     "character_builder_urls":[],"urls":[],"tags":[],"genre":"","genres":["Cyberpunk"],
     "dice_materials":[],"system_family":"Shadowrun","parent_system":"Shadowrun",
     "edition":"6","license":"","year":2019,"book_count":227,"total_page_count":6002,
     "cover_image":null,"cover_book_id":"xyz","is_explicit":false,
     "is_system_agnostic":false,"is_one_page":false}
    """;

    [Fact]
    public void SummaryDeserializesEveryScalarField()
    {
        var s = JsonSerializer.Deserialize(SummaryJson, AppJsonContext.Default.GameSystemSummary)!;
        Assert.Equal("abc", s.Id);
        Assert.Equal("Shadowrun 6 DE", s.Name);
        Assert.Equal("shadowrun-6-de", s.Slug);
        Assert.Equal("Shadowrun", s.SystemFamily);
        Assert.Equal("Shadowrun", s.ParentSystem);
        Assert.Equal("6", s.Edition);
        Assert.Equal(2019, s.Year);
        Assert.Equal(227, s.BookCount);
        Assert.Equal(6002, s.TotalPageCount);
        Assert.Equal("xyz", s.CoverBookId);
        Assert.False(s.IsExplicit);
        Assert.False(s.IsSystemAgnostic);
        Assert.False(s.IsOnePage);
    }

    [Fact]
    public void SummaryDeserializesNestedAndListFields()
    {
        var s = JsonSerializer.Deserialize(SummaryJson, AppJsonContext.Default.GameSystemSummary)!;
        Assert.Equal("Pegasus Spiele", Assert.Single(s.Publishers!).Name);
        Assert.Equal("Cyberpunk", Assert.Single(s.Genres!));
        Assert.Empty(s.Urls!);
        Assert.Empty(s.Tags!);
    }

    [Fact]
    public void SummaryRoundTripsToTheSameApiFieldNames()
    {
        var s = JsonSerializer.Deserialize(SummaryJson, AppJsonContext.Default.GameSystemSummary)!;
        var json = JsonSerializer.Serialize(s, AppJsonContext.Default.GameSystemSummary);
        Assert.Contains("\"book_count\":227", json);
        Assert.Contains("\"system_family\":\"Shadowrun\"", json);
        Assert.Contains("\"is_one_page\":false", json);
        Assert.DoesNotContain("BookCount", json);
    }

    [Fact]
    public void DetailCarriesBooksOnTopOfTheSummary()
    {
        const string detail = """
        {"id":"abc","name":"Shadowrun 6 DE","book_count":1,"total_page_count":12,
         "is_explicit":false,"is_system_agnostic":false,"is_one_page":false,
         "books":[{"id":"b1","title":"SR6 Grundregelwerk","category":"core",
                   "page_count":12,"language":"","indexed":true,"index_failed":false,
                   "ocr_indexed":false,"has_thumbnail":false,"is_explicit":false,
                   "is_missing":false,"relative_path":"books/Shadowrun 6 DE/core/x.pdf"}]}
        """;
        var d = JsonSerializer.Deserialize(detail, AppJsonContext.Default.GameSystemDetail)!;
        Assert.Equal("Shadowrun 6 DE", d.Name);
        var book = Assert.Single(d.Books!);
        Assert.Equal("SR6 Grundregelwerk", book.Title);
        Assert.Equal("core", book.Category);
        Assert.Equal(12, book.PageCount);
        Assert.Equal("books/Shadowrun 6 DE/core/x.pdf", book.RelativePath);
    }

    [Fact]
    public void ListOfSummariesDeserializes()
    {
        var list = JsonSerializer.Deserialize($"[{SummaryJson}]", AppJsonContext.Default.ListGameSystemSummary)!;
        Assert.Equal("Shadowrun 6 DE", Assert.Single(list).Name);
    }
}
```

If the generated context member is named something other than `ListGameSystemSummary`, use whatever the source generator produced — check IntelliSense or the build error; do not add a hand-written wrapper type.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: all pass, 24 existing + 5 new.

- [ ] **Step 7: Format and commit**

```bash
dotnet format GrimoireCli.sln
dotnet format GrimoireCli.sln --verify-no-changes
git add src/GrimoireCli/Models tests/GrimoireCli.Tests/GameSystemDtoTests.cs
git commit -m "feat: add typed dtos for the systems responses"
```

---

### Task 5: Query string builder

**Files:**
- Create: `src/GrimoireCli/Api/QueryBuilder.cs`
- Create: `tests/GrimoireCli.Tests/QueryBuilderTests.cs`

**Interfaces:**
- Produces: `QueryBuilder.Build(params (string Name, string? Value)[] parameters) → string`, returning `""` or `?a=b&c=d`. Task 6 consumes it.

- [ ] **Step 1: Write the failing tests**

`tests/GrimoireCli.Tests/QueryBuilderTests.cs`:

```csharp
using GrimoireCli.Api;

namespace GrimoireCli.Tests;

public class QueryBuilderTests
{
    [Fact]
    public void ReturnsEmptyStringWhenNothingIsSet()
    {
        Assert.Equal("", QueryBuilder.Build(("sort", null), ("genre", null)));
    }

    [Fact]
    public void SkipsNullAndEmptyValues()
    {
        Assert.Equal("?sort=name", QueryBuilder.Build(("sort", "name"), ("genre", null), ("edition", "")));
    }

    [Fact]
    public void JoinsMultipleParametersWithAmpersands()
    {
        Assert.Equal("?sort=name&order=desc", QueryBuilder.Build(("sort", "name"), ("order", "desc")));
    }

    // Filter values are real system names: "Dungeons & Dragons" would otherwise
    // terminate the parameter early and silently change the query.
    [Fact]
    public void EncodesAmpersandsInValues()
    {
        Assert.Equal("?parent_system=Dungeons%20%26%20Dragons",
            QueryBuilder.Build(("parent_system", "Dungeons & Dragons")));
    }

    [Fact]
    public void EncodesSpacesAndNonAscii()
    {
        Assert.Equal("?family=Das%20Schwarze%20Auge", QueryBuilder.Build(("family", "Das Schwarze Auge")));
        Assert.Equal("?genre=Stra%C3%9Fe", QueryBuilder.Build(("genre", "Straße")));
    }

    [Fact]
    public void EncodesTheParameterNameToo()
    {
        Assert.Equal("?odd%20name=v", QueryBuilder.Build(("odd name", "v")));
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~QueryBuilderTests"`
Expected: FAIL — `QueryBuilder` does not exist.

- [ ] **Step 3: Implement**

`src/GrimoireCli/Api/QueryBuilder.cs`:

```csharp
namespace GrimoireCli.Api;

/// <summary>
/// Builds the query string for a request. Unset parameters are omitted entirely
/// rather than sent empty, because Grimoire treats an empty filter as a filter.
/// </summary>
public static class QueryBuilder
{
    public static string Build(params (string Name, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!)}")
            .ToArray();
        return parts.Length == 0 ? "" : "?" + string.Join("&", parts);
    }
}
```

- [ ] **Step 4: Run them again**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter "FullyQualifiedName~QueryBuilderTests"`
Expected: 6/6 pass. If `EncodesAmpersandsInValues` fails on the space encoding, note that `Uri.EscapeDataString` renders a space as `%20`, not `+` — the expectation above is correct; fix the implementation, not the test.

- [ ] **Step 5: Format and commit**

```bash
dotnet format GrimoireCli.sln
git add src/GrimoireCli/Api/QueryBuilder.cs tests/GrimoireCli.Tests/QueryBuilderTests.cs
git commit -m "feat: add query string builder with url encoding"
```

---

### Task 6: Systems service

**Files:**
- Create: `src/GrimoireCli/Services/SystemsService.cs`
- Modify: `src/GrimoireCli/Api/ApiEndpoints.cs`

**Interfaces:**
- Consumes: `QueryBuilder.Build` (Task 5); `GameSystemSummary`, `GameSystemDetail`, `AppJsonContext` (Task 4); `GrimoireApiClient.GetAsync(string endpoint, string? permissionHint = null, string? notFoundHint = null, TimeSpan? timeout = null)`.
- Produces:
  - `SystemsService(GrimoireApiClient client)`
  - `Task<List<GameSystemSummary>> ListAsync(string? sort, bool desc, string? genre, string? family, string? parentSystem, string? edition, string? license, bool? isExplicit)`
  - `Task<GameSystemDetail> GetAsync(string id, string? bookSort, bool bookDesc, string? genre, string? category, bool? isExplicit)`

  Task 7 consumes both.

- [ ] **Step 1: Write the service**

`src/GrimoireCli/Services/SystemsService.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class SystemsService
{
    private readonly GrimoireApiClient _client;

    public SystemsService(GrimoireApiClient client) => _client = client;

    public async Task<List<GameSystemSummary>> ListAsync(
        string? sort, bool desc, string? genre, string? family,
        string? parentSystem, string? edition, string? license, bool? isExplicit)
    {
        var query = QueryBuilder.Build(
            ("sort", sort),
            ("order", desc ? "desc" : null),
            ("genre", genre),
            ("family", family),
            ("parent_system", parentSystem),
            ("edition", edition),
            ("license", license),
            ("explicit", isExplicit?.ToString().ToLowerInvariant()));
        var json = await _client.GetAsync(ApiEndpoints.Systems + query);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListGameSystemSummary) ?? new();
    }

    public async Task<GameSystemDetail> GetAsync(
        string id, string? bookSort, bool bookDesc, string? genre, string? category, bool? isExplicit)
    {
        var query = QueryBuilder.Build(
            ("book_sort", bookSort),
            ("book_order", bookDesc ? "desc" : null),
            ("genre", genre),
            ("category", category),
            ("explicit", isExplicit?.ToString().ToLowerInvariant()));
        var json = await _client.GetAsync(
            ApiEndpoints.System(id) + query,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.GameSystemDetail)!;
    }
}
```

`order` is omitted rather than sent as `asc`, because the server already defaults to ascending — sending it adds noise to the request for no behavioural difference. `explicit` is lowercased because Python's parser accepts `true`/`false`, not `True`/`False`.

- [ ] **Step 2: Remove the unused endpoint constants**

`src/GrimoireCli/Api/ApiEndpoints.cs` declares `Me`, `Books`, `Book`, `Rescan` and `ScanStatus`, which nothing calls. Delete those five lines; the file's own comment says to add rows as commands land. Keep `Login`, `About`, `Systems` and `System(id)`.

- [ ] **Step 3: Build**

Run: `dotnet build GrimoireCli.sln`
Expected: 0 errors. If `AppJsonContext.Default.ListGameSystemSummary` does not resolve, use the member name the source generator produced for `List<GameSystemSummary>`.

- [ ] **Step 4: Format and commit**

```bash
dotnet format GrimoireCli.sln
git add src/GrimoireCli/Services/SystemsService.cs src/GrimoireCli/Api/ApiEndpoints.cs
git commit -m "feat: add systems service over the typed dtos"
```

---

### Task 7: Systems commands

**Files:**
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs` (full rewrite)
- Create: `tests/GrimoireCli.Tests/SystemsCommandTests.cs`

**Interfaces:**
- Consumes: `SystemsService.ListAsync` / `GetAsync` (Task 6), `CommandHelper.BuildClient()` returning `(GrimoireApiClient client, AppConfig config)`, `ConsoleOutput.WriteJson<T>(T data, JsonTypeInfo<T> typeInfo)`, `HelpExtensions.AddHelpSection` / `AddExamples`.
- Produces: `systems list` and `systems get` with the flags below. Task 9 adds response-shape sections to both; Task 10 asserts their behaviour.

The value-restriction API: `AcceptOnlyFromAmong` exists on `Argument`, **not** on `Option`, in System.CommandLine 2.0.7. Options validate through `Option.Validators`. The helper below was compile- and run-verified against 2.0.7.

- [ ] **Step 1: Write the command file**

`src/GrimoireCli/Commands/SystemsCommand.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Models;
using GrimoireCli.Output;
using GrimoireCli.Services;

namespace GrimoireCli.Commands;

public static class SystemsCommand
{
    private static readonly string[] SystemSortKeys = ["name", "book_count", "page_count", "year"];
    private static readonly string[] BookSortKeys = ["category", "title", "page_count", "year"];

    public static Command Create()
    {
        var command = new Command("systems", "Game systems (the folders under books/)");
        command.Subcommands.Add(CreateListCommand());
        command.Subcommands.Add(CreateGetCommand());
        return command;
    }

    /// <summary>
    /// An option restricted to a fixed value set. The server silently falls back to
    /// its default sort when given an unknown key, so an unrecognised value is
    /// rejected here instead of returning differently-ordered data with exit 0.
    /// </summary>
    private static Option<string?> ChoiceOption(string name, string description, string[] allowed)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !allowed.Contains(value))
                result.AddError($"'{value}' is not a valid value for {name}. Must be one of: {string.Join(", ", allowed)}");
        });
        option.CompletionSources.Add(allowed);
        return option;
    }

    private static Command CreateListCommand()
    {
        var sortOption = ChoiceOption("--sort", "Sort field (name | book_count | page_count | year); default name", SystemSortKeys);
        var descOption = new Option<bool>("--desc") { Description = "Sort descending" };
        var genreOption = new Option<string?>("--genre") { Description = "Filter by genre" };
        var familyOption = new Option<string?>("--family") { Description = "Filter by system family" };
        var parentOption = new Option<string?>("--parent-system") { Description = "Filter by parent system" };
        var editionOption = new Option<string?>("--edition") { Description = "Filter by edition" };
        var licenseOption = new Option<string?>("--license") { Description = "Filter by license" };
        var explicitOption = new Option<bool?>("--explicit") { Description = "Filter by explicit flag (true | false); omit for both" };
        var command = new Command("list", "List all game systems")
        {
            sortOption, descOption, genreOption, familyOption,
            parentOption, editionOption, licenseOption, explicitOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Filters are case-insensitive exact matches, not substrings: --edition 5",
            "does not match 5e. They test stored metadata, which the scanner leaves",
            "empty — a freshly imported system matches no filter at all.");
        command.AddExamples(
            "grimoire-cli systems list",
            "grimoire-cli systems list --sort book_count --desc",
            "grimoire-cli systems list --family Shadowrun --edition 6",
            "grimoire-cli systems list --explicit false");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            var result = await service.ListAsync(
                parseResult.GetValue(sortOption),
                parseResult.GetValue(descOption),
                parseResult.GetValue(genreOption),
                parseResult.GetValue(familyOption),
                parseResult.GetValue(parentOption),
                parseResult.GetValue(editionOption),
                parseResult.GetValue(licenseOption),
                parseResult.GetValue(explicitOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.ListGameSystemSummary);
            return 0;
        });
        return command;
    }

    private static Command CreateGetCommand()
    {
        var idOption = new Option<string>("--id") { Description = "System ID", Required = true };
        var bookSortOption = ChoiceOption("--book-sort", "Sort the books (category | title | page_count | year); default category", BookSortKeys);
        var bookDescOption = new Option<bool>("--book-desc") { Description = "Sort the books descending" };
        var genreOption = new Option<string?>("--genre") { Description = "Keep only books with this genre" };
        var categoryOption = new Option<string?>("--category") { Description = "Keep only books in this category (core | supplement | adventure | character-sheet | map | handout | homebrew | starter-set)" };
        var explicitOption = new Option<bool?>("--explicit") { Description = "Keep only books with this explicit flag (true | false)" };
        var command = new Command("get", "Get one game system, with its books")
        {
            idOption, bookSortOption, bookDescOption, genreOption, categoryOption, explicitOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "--genre, --category and --explicit filter the books, not the system, and",
            "book_count / total_page_count are recomputed from the filtered list — so",
            "--category core reports counts for the core books alone.",
            "",
            "--category takes the normalised category, not the folder name:",
            "'supplement', not 'supplements'. It is also case-sensitive — 'Core'",
            "matches nothing — while --genre is case-insensitive.);
        command.AddExamples(
            "grimoire-cli systems get --id <system-id>",
            "grimoire-cli systems get --id <system-id> --category core",
            "grimoire-cli systems get --id <system-id> --book-sort page_count --book-desc");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new SystemsService(client);
            var result = await service.GetAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(bookSortOption),
                parseResult.GetValue(bookDescOption),
                parseResult.GetValue(genreOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(explicitOption));
            ConsoleOutput.WriteJson(result, AppJsonContext.Default.GameSystemDetail);
            return 0;
        });
        return command;
    }
}
```

- [ ] **Step 2: Write the tests**

`tests/GrimoireCli.Tests/SystemsCommandTests.cs`:

```csharp
using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests;

public class SystemsCommandTests
{
    private static Command Root()
    {
        var root = new RootCommand("test");
        root.Subcommands.Add(SystemsCommand.Create());
        return root;
    }

    [Theory]
    [InlineData("systems list --sort name")]
    [InlineData("systems list --sort book_count")]
    [InlineData("systems list --sort page_count")]
    [InlineData("systems list --sort year")]
    public void AcceptsEverySupportedSortKey(string input)
    {
        Assert.Empty(Root().Parse(input).Errors);
    }

    [Fact]
    public void RejectsAnUnknownSortKeyBeforeAnyRequestIsMade()
    {
        var result = Root().Parse("systems list --sort tite");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Must be one of: name, book_count, page_count, year", result.Errors[0].Message);
    }

    [Fact]
    public void RejectsAnUnknownBookSortKey()
    {
        var result = Root().Parse("systems get --id x --book-sort pages");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Must be one of: category, title, page_count, year", result.Errors[0].Message);
    }

    // category is the server's own default for book_sort even though its whitelist
    // omits it, so the CLI must not be stricter than the server here.
    [Fact]
    public void AcceptsCategoryAsABookSortKey()
    {
        Assert.Empty(Root().Parse("systems get --id x --book-sort category").Errors);
    }

    [Fact]
    public void RequiresAnIdOnGet()
    {
        Assert.NotEmpty(Root().Parse("systems get").Errors);
    }

    [Fact]
    public void LeavesFilterValuesUnvalidated()
    {
        Assert.Empty(Root().Parse("systems list --genre Cyberpunk --family Shadowrun --edition 6").Errors);
        Assert.Empty(Root().Parse("systems list --genre \"a genre that does not exist\"").Errors);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj`
Expected: all pass — 24 existing + 5 DTO + 6 QueryBuilder + 10 command tests.

- [ ] **Step 4: Exercise it against the seeded stack**

```bash
dotnet build GrimoireCli.sln
BIN=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
$BIN systems list | jq 'length'
$BIN systems list --family Shadowrun | jq '[.[].name]'
$BIN systems list --sort book_count --desc | jq '[.[].book_count]'
$BIN systems list --sort tite; echo "exit=$? (expect non-zero)"
```
Expected: `10`; the two patched Shadowrun systems and not `Shadowrun 4 DE`; book counts in descending order; the last command exits non-zero without an HTTP request.

- [ ] **Step 5: Format and commit**

```bash
dotnet format GrimoireCli.sln
git add src/GrimoireCli/Commands/SystemsCommand.cs tests/GrimoireCli.Tests/SystemsCommandTests.cs
git commit -m "feat: expose every systems query parameter as a flag"
```

---

### Task 8: Response example generator

**Files:**
- Create: `tools/GenerateResponseExamples/GenerateResponseExamples.csproj`
- Create: `tools/GenerateResponseExamples/Program.cs`
- Create: `tools/GenerateResponseExamples/SampleJsonWalker.cs`
- Create: `src/GrimoireCli/Commands/ResponseExamples.g.cs` (generated, committed)
- Modify: `src/GrimoireCli/Commands/HelpExtensions.cs`
- Modify: `src/GrimoireCli/Commands/SystemsCommand.cs`
- Modify: `GrimoireCli.sln`
- Create: `tests/GrimoireCli.Tests/ResponseExamplesDriftTest.cs`
- Create: `tests/GrimoireCli.Tests/ResponseExamplesJsonValidTest.cs`

**Interfaces:**
- Consumes: the DTOs and `AppJsonContext` from Task 4; `HelpExtensions.AddShapeSection(this Command, string title, params string[] lines)` which already exists.
- Produces: `ResponseExamples.For(Type) → string`, `ResponseExamples.All`, `command.AddResponseExample<T>()` and `command.AddResponseExampleArray<T>()`.

- [ ] **Step 1: Port the generator from abs-cli**

Fetch the two source files and adapt them:

```bash
mkdir -p tools/GenerateResponseExamples
gh api repos/thomaslazar/abs-cli/contents/tools/GenerateResponseExamples/Program.cs --jq .content | base64 -d > tools/GenerateResponseExamples/Program.cs
gh api repos/thomaslazar/abs-cli/contents/tools/GenerateResponseExamples/SampleJsonWalker.cs --jq .content | base64 -d > tools/GenerateResponseExamples/SampleJsonWalker.cs
gh api repos/thomaslazar/abs-cli/contents/tools/GenerateResponseExamples/GenerateResponseExamples.csproj --jq .content | base64 -d > tools/GenerateResponseExamples/GenerateResponseExamples.csproj
```

Required adaptations, all mechanical:

1. Namespaces `AbsCli.*` → `GrimoireCli.*`; emitted namespace `AbsCli.Commands` → `GrimoireCli.Commands`.
2. The csproj's `ProjectReference` points at `../../src/GrimoireCli/GrimoireCli.csproj`.
3. `DiscoverResponseTypes()` reads `[JsonSerializable]` attributes off `AppJsonContext`. Skip the types that are not response payloads: `LoginRequest`, `AppConfig`, `Dictionary<string, string>`, and any `List<>` (the array shape is composed at the call site by `AddResponseExampleArray<T>`).
4. Drop any abs-cli-specific property overrides that reference ABS types; keep the mechanism. Seed it with Grimoire-appropriate sample values so the output reads as real data rather than `"string"`: `name` → `"Shadowrun 6 DE"`, `slug` → `"shadowrun-6-de"`, `edition` → `"6"`, `category` → `"core"`, `book_count` → `227`, `total_page_count` → `6002`.
5. Delete anything referencing `LibraryItemMinified`, media unions or paginated envelopes — Grimoire has none of those.

- [ ] **Step 2: Add the tool to the solution and generate**

```bash
dotnet sln GrimoireCli.sln add tools/GenerateResponseExamples/GenerateResponseExamples.csproj
dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs
head -20 src/GrimoireCli/Commands/ResponseExamples.g.cs
```
Expected: a generated file with an auto-generated header and a `Dictionary<Type, string>` containing entries for `GameSystemSummary`, `GameSystemDetail`, `Book`, `PublisherEntry` and `LinkEntry`.

- [ ] **Step 3: Wire it into help**

Add to `src/GrimoireCli/Commands/HelpExtensions.cs`:

```csharp
    /// <summary>Registers the generated response-shape sample for <typeparamref name="T"/>.</summary>
    public static void AddResponseExample<T>(this Command command)
        => command.AddShapeSection("Response shape", ResponseExamples.For(typeof(T)).Split('\n'));

    /// <summary>
    /// Registers a response-shape sample for an endpoint returning a bare array of
    /// <typeparamref name="T"/>, which is what GET /api/systems does.
    /// </summary>
    public static void AddResponseExampleArray<T>(this Command command)
    {
        var element = ResponseExamples.For(typeof(T));
        var indented = string.Join('\n', element.Split('\n').Select(l => "  " + l));
        command.AddShapeSection("Response shape", $"[\n{indented}\n]".Split('\n'));
    }
```

Then in `SystemsCommand`, after each `AddExamples(...)` call:

```csharp
        command.AddResponseExampleArray<GameSystemSummary>();   // in CreateListCommand
        command.AddResponseExample<GameSystemDetail>();         // in CreateGetCommand
```

- [ ] **Step 4: Write the drift and validity tests**

`tests/GrimoireCli.Tests/ResponseExamplesDriftTest.cs`:

```csharp
using System.Diagnostics;

namespace GrimoireCli.Tests;

public class ResponseExamplesDriftTest
{
    [Fact]
    public void CheckedInFileMatchesFreshGeneration()
    {
        var repoRoot = RepoRoot();
        var checkedInPath = Path.Combine(repoRoot, "src", "GrimoireCli", "Commands", "ResponseExamples.g.cs");
        Assert.True(File.Exists(checkedInPath), $"Missing generated file: {checkedInPath}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"response-examples-{Guid.NewGuid():N}.g.cs");
        try
        {
            var toolProject = Path.Combine(repoRoot, "tools", "GenerateResponseExamples", "GenerateResponseExamples.csproj");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "run", "--project", toolProject, "--", tempPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            Assert.True(proc.ExitCode == 0,
                $"Generator exited {proc.ExitCode}\nstdout: {proc.StandardOutput.ReadToEnd()}\nstderr: {proc.StandardError.ReadToEnd()}");
            var expected = File.ReadAllText(checkedInPath).Replace("\r\n", "\n");
            var actual = File.ReadAllText(tempPath).Replace("\r\n", "\n");
            Assert.True(expected == actual,
                "ResponseExamples.g.cs is stale. Regenerate with: " +
                "dotnet run --project tools/GenerateResponseExamples -- src/GrimoireCli/Commands/ResponseExamples.g.cs");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GrimoireCli.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
```

`tests/GrimoireCli.Tests/ResponseExamplesJsonValidTest.cs`:

```csharp
using System.Text.Json;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests;

public class ResponseExamplesJsonValidTest
{
    [Fact]
    public void EverySampleParsesAsJson()
    {
        Assert.NotEmpty(ResponseExamples.All);
        foreach (var (type, sample) in ResponseExamples.All)
        {
            var ex = Record.Exception(() => JsonDocument.Parse(sample));
            Assert.True(ex is null, $"Sample for {type.Name} is not valid JSON: {ex?.Message}\n{sample}");
        }
    }
}
```

`ResponseExamples` is `internal`, so this test relies on the existing
`<InternalsVisibleTo Include="GrimoireCli.Tests" />` in `GrimoireCli.csproj`.

- [ ] **Step 5: Verify help output and tests**

```bash
dotnet build GrimoireCli.sln
BIN=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli
$BIN systems list --help | tail -5
$BIN systems list --help-full | tail -30
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```
Expected: plain `--help` ends with `Run --help-full to see response shape(s).`; `--help-full` prints a `Response shape:` block wrapped in `[` … `]`; all tests pass.

- [ ] **Step 6: Format and commit**

```bash
dotnet format GrimoireCli.sln
git add tools GrimoireCli.sln src/GrimoireCli/Commands/ResponseExamples.g.cs \
        src/GrimoireCli/Commands/HelpExtensions.cs src/GrimoireCli/Commands/SystemsCommand.cs \
        tests/GrimoireCli.Tests/ResponseExamplesDriftTest.cs \
        tests/GrimoireCli.Tests/ResponseExamplesJsonValidTest.cs
git commit -m "feat: generate response shape samples for help-full"
```

---

### Task 9: Smoke test and CI

**Files:**
- Modify: `docker/smoke-test.sh`
- Modify: `.github/workflows/build.yml`

**Interfaces:**
- Consumes: the fixture set from Task 3 and the commands from Task 7.

- [ ] **Step 1: Extend the smoke test**

Change the header comment to state that it requires a seeded stack (`bash docker/seed.sh` first), then append this section before the final `echo "smoke: all checks passed"`:

```bash
# --- seeded data -------------------------------------------------------------
# Requires docker/seed.sh to have run. Counts mirror the fixture set defined
# there; changing a fixture must change these numbers.
EXPECTED_SYSTEMS=8

count() { "$CLI" systems list "$@" 2>/dev/null | jq 'length'; }

[ "$(count)" -eq "$EXPECTED_SYSTEMS" ] \
  || fail "expected $EXPECTED_SYSTEMS systems, got $(count)"
ok "systems list returns $EXPECTED_SYSTEMS systems"

[ "$(count --genre Cyberpunk)" -eq 2 ] || fail "--genre Cyberpunk should match 2"
[ "$(count --edition 6)" -eq 1 ] || fail "--edition 6 should match 1"
[ "$(count --edition 5)" -eq 4 ] || fail "--edition 5 should match 4 across families"
[ "$(count --license OGL)" -eq 1 ] || fail "--license OGL should match 1"
[ "$(count --genre nope)" -eq 0 ] || fail "an unmatched filter should return []"
ok "filters narrow the result set"

# Shadowrun 4 DE is seeded raw, so a family filter must exclude it.
[ "$(count --family Shadowrun)" -eq 2 ] \
  || fail "--family Shadowrun should match 2, not the raw Shadowrun 4 DE"
ok "systems with empty metadata are excluded by filters"

# The (nsfw) folder marker, not a PATCH, is what sets this.
EXPLICIT=$("$CLI" systems list --explicit true | jq -r '.[].name')
[ "$EXPLICIT" = "Vampire The Masquerade 5 EN" ] \
  || fail "--explicit true returned '$EXPLICIT'"
ok "--explicit true matches the nsfw-marked system"

# Filter values with an ampersand must survive URL encoding.
[ "$(count --parent-system "Dungeons & Dragons")" -eq 1 ] \
  || fail "a filter value containing '&' did not round-trip"
ok "ampersand in a filter value round-trips"

# Descending sort must actually be descending.
COUNTS=$("$CLI" systems list --sort book_count --desc | jq '[.[].book_count]')
echo "$COUNTS" | jq -e '. == (. | sort | reverse)' >/dev/null \
  || fail "--sort book_count --desc was not descending: $COUNTS"
ok "--sort book_count --desc is ordered"

# A rejected sort key must fail before any request is made.
set +e
"$CLI" systems list --sort bogus >/dev/null 2>"$WORK/sort.err"; rc=$?
set -e
[ "$rc" -ne 0 ] || fail "--sort bogus should have failed"
grep -q "Must be one of" "$WORK/sort.err" || fail "no value-set message: $(cat "$WORK/sort.err")"
ok "--sort bogus is rejected at parse time"

# systems get: filters apply to the books and change the reported counts.
SR6=$("$CLI" systems list --edition 6 | jq -r '.[0].id')
[ "$("$CLI" systems get --id "$SR6" | jq '.books | length')" -eq 3 ] \
  || fail "Shadowrun 6 DE should have 3 books"
CORE=$("$CLI" systems get --id "$SR6" --category core)
[ "$(echo "$CORE" | jq '.books | length')" -eq 2 ] || fail "--category core should keep 2 books"
[ "$(echo "$CORE" | jq '.book_count')" -eq 2 ] \
  || fail "book_count should be recomputed from the filtered books"
ok "systems get filters books and recomputes counts"

# The canonical category, not the folder name.
[ "$("$CLI" systems get --id "$SR6" --category supplements | jq '.books | length')" -eq 0 ] \
  || fail "'supplements' is a folder name and should match nothing"
[ "$("$CLI" systems get --id "$SR6" --category supplement | jq '.books | length')" -eq 1 ] \
  || fail "'supplement' is the canonical category and should match 1"
ok "category filtering uses canonical values"

set +e
"$CLI" systems get --id no-such-id >/dev/null 2>"$WORK/nf.err"; rc=$?
set -e
[ "$rc" -eq 2 ] || fail "a missing id should exit 2, got $rc"
grep -qi "not found" "$WORK/nf.err" || fail "no not-found hint: $(cat "$WORK/nf.err")"
ok "systems get on a missing id exits 2 with a hint"
```

- [ ] **Step 2: Run it**

```bash
bash docker/seed.sh
bash docker/smoke-test.sh
```
Expected: the six original `ok:` lines plus the new ones, ending in `smoke: all checks passed`. If a count assertion fails, check the fixture set actually seeded rather than loosening the assertion.

- [ ] **Step 3: Update CI**

In `.github/workflows/build.yml`, in the `smoke-test` job: add a step installing the fixture dependency before the stack starts, and a seed step between `Start Grimoire` and the smoke run.

```yaml
      - name: Install fixture tooling
        run: sudo apt-get update && sudo apt-get install -y --no-install-recommends python3-fitz
      - name: Seed fixtures
        run: bash docker/seed.sh
        env:
          GRIMOIRE_SERVER: http://localhost:9481
```

The install step goes before `Start Grimoire`; the seed step after it. `GRIMOIRE_LIBRARY_LOCAL` is not set, so `seed.sh` writes to `docker/library`, which is what the compose default mounts on a runner.

- [ ] **Step 4: Validate the workflow**

Run: `docker exec docker-grimoire-1 python -c "import sys,yaml; d=yaml.safe_load(sys.stdin.read()); print([s.get('name') or s.get('uses') for s in d['jobs']['smoke-test']['steps']])" < .github/workflows/build.yml`
Expected: the step list in order, including `Install fixture tooling`, `Start Grimoire`, `Seed fixtures`, `Run smoke test against the AOT binary`. (The Grimoire container is used as a YAML parser because PyYAML is not installed in the devcontainer.)

- [ ] **Step 5: Commit**

```bash
git add docker/smoke-test.sh .github/workflows/build.yml
git commit -m "test: assert every systems filter and sort in the smoke test"
```

---

### Task 10: Documentation

**Files:**
- Create: `docs/grimoire-api-coverage.md`
- Create: `docs/grimoire-compatibility.md`
- Modify: `docs/grimoire-api-notes.md`
- Modify: `docs/roadmap.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Write the coverage doc**

`docs/grimoire-api-coverage.md` — a table of every endpoint group in the v1.5.4 spec (130 paths) with an implemented/not-implemented marker and the command name where one exists. Generate the endpoint list rather than transcribing it:

```bash
python3 -c "
import json
s=json.load(open('temp/grimoire-openapi.json'))
for p,ops in sorted(s['paths'].items()):
    for m in ops:
        if m in ('get','post','patch','put','delete'):
            print(f'| \`{m.upper()} {p}\` | — |')
" > /tmp/coverage-rows.md
```

Mark these four as implemented: `POST /api/auth/login` → `login`, `GET /api/about` → *(version check inside `login`)*, `GET /api/systems` → `systems list`, `GET /api/systems/{system_id}` → `systems get`. Open the file with a line stating it covers v1.5.4 and must be updated in the same PR as any endpoint change.

- [ ] **Step 2: Write the compatibility doc**

`docs/grimoire-compatibility.md` with three sections:

1. **Matrix** — one row: `grimoire-cli 0.1.x | Grimoire 1.5.4 | initial support`.
2. **Runtime check** — `MinSupportedVersion` / `MaxTestedVersion` in `src/GrimoireCli/Api/GrimoireApiClient.cs`, warning on stderr when outside the range, never refusing to run.
3. **Handling a Grimoire release** — the procedure:
   - `git -C temp/grimoire fetch --depth 1 origin tag vX.Y.Z && git -C temp/grimoire checkout vX.Y.Z`
   - Diff the two specs structurally. No baseline is committed; start each image in turn and pull its spec:
     ```bash
     for v in 1.5.4 X.Y.Z; do
       docker run -d --rm --name spec-$v -p 9500:9481 -e OPDS_ENABLED=false hunterreadca/grimoire:$v
       until curl -sf localhost:9500/api/openapi.json -o /tmp/spec-$v.json; do sleep 2; done
       docker stop spec-$v
     done
     python3 -c "
     import json
     a=json.load(open('/tmp/spec-1.5.4.json')); b=json.load(open('/tmp/spec-X.Y.Z.json'))
     print('added paths:', sorted(set(b['paths']) - set(a['paths'])))
     print('removed paths:', sorted(set(a['paths']) - set(b['paths'])))
     "
     ```
   - Diff the serializers for the untyped response shapes:
     `git -C temp/grimoire diff vOLD..vNEW -- backend/routers/*/_serializers.py backend/routers/*/core.py backend/models/`
   - Update DTOs, flags and help text; bump the compose image tag; re-run `seed.sh` + `smoke-test.sh`; update `MinSupportedVersion` / `MaxTestedVersion`, this matrix, and the README compatibility line.

- [ ] **Step 3: Extend the API notes**

Add to `docs/grimoire-api-notes.md` under Scanner behaviour:

```markdown
- **Category folder names are aliases, not values.** `CATEGORY_MAP`
  (`backend/indexer/constants.py`) normalises folder names onto canonical singular
  categories: `supplements/`, `sourcebook/`, `guide/`, `companion/` all become
  `supplement`. Canonical values are `core`, `supplement`, `adventure`,
  `character-sheet`, `map`, `handout`, `homebrew`, `starter-set`. Filtering by the
  folder name silently matches nothing.
- **`(nsfw)` in a system folder name sets `is_explicit`** and is stripped from the
  stored name, so `Vampire The Masquerade 5 EN (nsfw)/` becomes a system named
  `Vampire The Masquerade 5 EN` with `is_explicit: true`.
- **Leading `!`, `$`, `%` are stripped from system folder names**
  (`strip_sort_prefix`), so `!!Dungeons & Dragons/` is stored as
  `Dungeons & Dragons`. Only the contiguous leading run is removed.
```

**Two existing bullets in that file are wrong and must be replaced, not appended
to.** They were inherited from the original handover and are contradicted by
v1.5.4's source and by the running stack:

- The file claims a loose `foo.pdf` directly under `books/` becomes its own
  single-book system. It does not: `_scan_books` skips non-directories
  (`if not system_dir.is_dir(): continue`, `backend/indexer/scan.py:178`), so the
  file is never indexed at all — while `_count_eligible_files` still counts it in
  `total_books`, which will hang any wait loop polling `scanned_books >= total_books`.
- The file claims `one-page-rpgs/` makes each file its own system. It does not: it
  is **one** system with `is_one_page: true`, whose immediate subfolder names become
  category labels, exactly like the system-agnostic collection.

And under PATCH semantics / filtering:

```markdown
- **`GET /api/systems` filters are case-insensitive exact matches**, not
  substrings (`_has_value` in `backend/routers/systems/core.py`): `edition=5`
  matches `5` but never `5e`, and `genre=Cyber` never matches `Cyberpunk`. A list
  field matches if any element equals the value; an empty or null field never
  matches, so a freshly scanned system is excluded from every metadata filter.
  `genre=` tests the `genres` list, not the legacy `genre` string.
- **`category` on `GET /api/systems/{id}` is case-SENSITIVE**, unlike every other
  filter. `core.py:154` compares with `==` (`b.category == category`) while
  `genre` goes through `_has_value`, which lowercases both sides. So
  `category=Core` returns no books and `category=core` returns them. Verified
  against a running instance.
```

And under a new heading:

```markdown
## Systems have no language field

`GameSystemUpdate` has 17 fields and `serialize_system_summary` returns 23; neither
includes `language`. It exists only on books (`BookUpdate.language`), and
`GET /api/systems` has no `language` query parameter. A system's language can be
expressed only through its name (the `Shadowrun 6 DE` convention), a tag, or
per-book metadata.
```

- [ ] **Step 4: Update README, roadmap and CLAUDE.md**

- `README.md`: replace the two `systems` rows in the Commands table with the full flag lists from Task 7, and correct the `config set` row — the valid key list is `server` only, since `ApplyConfigSet` rejects anything else.
- `docs/roadmap.md`: move `systems list` / `systems get` out of open work; keep the metadata command surface and `seed.sh`-driven fixtures accurate (fixtures now exist).
- `CLAUDE.md`: add three rules —
  - any PR that adds, renames or removes a command, or changes a user-visible flag, updates the README Commands table in the same change;
  - any PR that touches which endpoints are called updates `docs/grimoire-api-coverage.md` in the same change;
  - commands whose endpoint needs a non-default role call `command.AddRoleRequired("<role>")`, and the string matches the `permissionHint` passed to the service call. `systems list` / `systems get` need no tag: any authenticated non-guest can read them.
- `CLAUDE.md` Pre-PR verification: the smoke test now requires `bash docker/seed.sh` first.

- [ ] **Step 5: Commit**

```bash
git add docs/grimoire-api-coverage.md docs/grimoire-compatibility.md \
        docs/grimoire-api-notes.md docs/roadmap.md README.md CLAUDE.md
git commit -m "docs: add api coverage and compatibility references"
```

---

### Task 11: Final verification

- [ ] **Step 1: Full local gate from a clean stack**

```bash
docker compose -f docker/docker-compose.yml down
rm -rf docker/data && mkdir -p docker/data
cp docker/users.json.example docker/data/users.json
GRIMOIRE_LIBRARY=/path/to/grimoire-cli/docker/library \
GRIMOIRE_DATA=/path/to/grimoire-cli/docker/data \
  docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```
Expected: all five succeed. Report the actual output; do not claim success without it.

- [ ] **Step 2: Confirm the AOT binary behaves the same**

```bash
dotnet publish src/GrimoireCli/GrimoireCli.csproj -c Release -r linux-x64 --self-contained true -p:PublishAot=true -o ./publish
CLI=./publish/grimoire-cli bash docker/smoke-test.sh
./publish/grimoire-cli systems list --help-full | tail -20
rm -rf ./publish
```
Expected: the smoke suite passes against the published binary and the response shape renders. This is the AOT check that matters — the DTOs are source-generated, and a missing `[JsonSerializable]` registration fails only here.

- [ ] **Step 3: Report and stop**

```bash
git log --oneline main..HEAD
```

Ask before pushing or opening a pull request.

---

## Notes for the implementer

- **The live instance is read-only.** Never PATCH it, never point `seed.sh` at it. Its single system's empty metadata is a deliberate fixture for the next piece of work.
- **Don't add `[JsonExtensionData]`** to the DTOs. An unmodelled field is a signal to update the DTOs under the version-bump procedure, not to pass unknown data through.
- **Don't widen the sort validation to filters.** Filter values depend on library content and must stay unvalidated.
- If a step's expected output does not match, stop and report rather than adjusting the assertion to whatever the code happens to produce.
