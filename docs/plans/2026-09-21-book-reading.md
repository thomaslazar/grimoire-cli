# book reading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `books toc`, `books page-text` and `books page-words` — the three reads that make a catalogued book readable ([#46](https://github.com/thomaslazar/grimoire-cli/issues/46)).

**Architecture:** Three plain JSON pass-throughs on the existing `books` group. No new command file, no new output convention. The only non-mechanical decision is which of the three gets a not-found hint, and that is settled per command below.

**Tech Stack:** C# / .NET, System.CommandLine, Kiota-generated client, xUnit, bash smoke test.

**Spec:** [docs/specs/2026-09-21-book-reading-design.md](../specs/2026-09-21-book-reading-design.md)

## Global Constraints

- Branch: `feat/book-reading`, cut from `main`. Never commit to `main`.
- **Commit messages carry NO attribution.** No `Co-Authored-By:` line of any kind, no "Generated with Claude Code" line, no naming of any model or tool — in commit messages, in the PR body, or in any file this change touches. This applies to every commit including fix-ups. Task 5 greps the branch to prove it before the PR opens.
- Conventional Commits, imperative, lowercase, no period, ~72 chars.
- The spec and this plan are committed **on the feature branch**, in Task 1's commit.
- **None of the three commands carries a role tag or a `permissionHint`.** All three routes are guarded by `get_current_user` (`temp/grimoire/backend/routers/books/pages.py`), and a reflexive `AddRoleRequired` here is a defect.
- Not-found hints differ per command — `toc` and `page-text` get one, `page-words` gets none. Task 1 Step 4 explains why; do not "make them consistent".
- Run `dotnet format GrimoireCli.sln` after touching any C# file.
- Help text is terse: nothing restating a flag's own description or a field visible in the rendered response sample.
- Never touch `CHANGELOG.md` or `docs/roadmap.md`.
- Writes go to the local Docker stack only — though this block only reads.

---

### Task 1: The three service sends

**Files:**
- Modify: `src/GrimoireCli/Services/BooksService.cs`
- Create: `tests/GrimoireCli.Tests/Services/BooksServiceTests.cs`
- Commit alongside: `docs/specs/2026-09-21-book-reading-design.md`, `docs/plans/2026-09-21-book-reading.md`

**Interfaces:**
- Consumes: `GrimoireApiClient.SendAsync(RequestInformation, string? permissionHint, string? notFoundHint)`, already used throughout this file.
- Produces: `BooksService.TocAsync(string id)`, `PageTextAsync(string id, int page)`, `PageWordsAsync(string id, int page)` — all `Task<string>`. Task 2 calls exactly these names.

- [ ] **Step 1: Cut the branch**

```bash
git checkout -b feat/book-reading
```

- [ ] **Step 2: Write the failing tests**

`tests/GrimoireCli.Tests/Services/BooksServiceTests.cs` does not exist. Create it, modelling the client and URI helpers on `tests/GrimoireCli.Tests/Services/TagsServiceTests.cs` — read that file first and match its shape.

```csharp
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins the book reading routes to the paths their generated builders produce.
/// The page number is a path segment rather than a query parameter, which is
/// the part a client regeneration could move without any test noticing.
/// </summary>
public class BooksServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void EachReadingRouteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Books["b1"];
        Assert.Equal("http://example.test/api/books/b1/toc", Uri(api.Toc.ToGetRequestInformation()));
        Assert.Contains("/api/books/b1/page/7/text", Uri(api.Page[7].Text.ToGetRequestInformation()));
        Assert.Contains("/api/books/b1/page/7/words", Uri(api.Page[7].Words.ToGetRequestInformation()));
    }

    // The page number belongs in the path; a regeneration that moved it to a
    // query parameter would read page 1 of every book instead of the one asked
    // for, and every other assertion here would still pass.
    [Fact]
    public void ThePageNumberIsAPathSegmentNotAQueryParameter()
    {
        var uri = Uri(Client().Api.Api.Books["b1"].Page[7].Text.ToGetRequestInformation());
        Assert.DoesNotContain("?", uri);
        Assert.Contains("/page/7/", uri);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksServiceTests`
Expected: the file compiles and the assertions pass or fail purely on the generated builders — these pin the client, not the new service methods. If they pass immediately, that is correct and expected; they are regression pins. Confirm they run at all, then continue.

- [ ] **Step 4: Add the three sends to `BooksService.cs`**

Place them after `ThumbnailAsync`. The existing book-not-found hint string appears twice in this file already; reuse the same wording where a hint is used.

```csharp
    /// <summary>
    /// GET /api/books/{id}/toc. Works for EPUB as well as PDF — PyMuPDF exposes
    /// an EPUB's nav document through the same API as a PDF outline, and the
    /// handler gates on is_fitz_mime (routers/books/pages.py:64-77).
    /// </summary>
    // A hint here, unusually, improves on the server: this route's only 404 is
    // bare (pages.py:73), so the caller would otherwise see {"detail":"Not
    // Found"}. The hint names both causes the bare 404 hides.
    public async Task<string> TocAsync(string id)
    {
        var info = _client.Api.Api.Books[id].Toc.ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            notFoundHint: "No book with that ID, or its format cannot be opened. Check mime_type with: grimoire-cli books get --id <id>");
    }

    /// <summary>
    /// GET /api/books/{id}/page/{n}/text. Reads the book_search row for that
    /// page and falls back to live extraction when there is none
    /// (routers/books/pages.py:221-245), so a result depends on the book being
    /// indexed. An out-of-range page is 400 with the real page count.
    /// </summary>
    // Two of this route's three 404s are bare (pages.py:219,241); the third
    // carries "File not found on disk" (:232). The hint names the two bare
    // causes, at the cost of masking that rarer one, which the help mentions.
    public async Task<string> PageTextAsync(string id, int page)
    {
        var info = _client.Api.Api.Books[id].Page[page].Text.ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            notFoundHint: "No book with that ID, or its format carries no readable text. Check mime_type with: grimoire-cli books get --id <id>");
    }

    /// <summary>
    /// GET /api/books/{id}/page/{n}/words. Answers 200 with an empty overlay
    /// for any book outside the fitz format family (routers/books/pages.py:
    /// 256-259). Only for the comic family does page-text 404 on the same
    /// input — the text family is can_index, so page-text succeeds there
    /// while this still returns the empty overlay.
    /// </summary>
    // No notFoundHint: both of this route's 404s carry a useful detail —
    // "Book not found" (pages.py:60) and "File not found on disk" (:263) —
    // which strengthens rather than weakens the case for leaving the
    // server's own message alone.
    public async Task<string> PageWordsAsync(string id, int page)
    {
        var info = _client.Api.Api.Books[id].Page[page].Words.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }
```

Check the generated builder property names against `src/GrimoireCli/Generated/Api/Books/Item/` before writing — `Toc`, `Page[n].Text` and `Page[n].Words` are expected, and the page indexer may be `int` or `string`. Correct the snippet if it differs.

- [ ] **Step 5: Format, build, run the tests**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: build clean, everything passes.

- [ ] **Step 6: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add docs/specs/2026-09-21-book-reading-design.md docs/plans/2026-09-21-book-reading.md \
        src/GrimoireCli/Services/BooksService.cs tests/GrimoireCli.Tests/Services/BooksServiceTests.cs
git commit -m "feat: add the book toc and page reading sends"
```

---

### Task 2: The three commands

**Files:**
- Modify: `src/GrimoireCli/Commands/BooksCommand.cs`
- Test: `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`

**Interfaces:**
- Consumes: `BooksService.TocAsync(string id)`, `PageTextAsync(string id, int page)`, `PageWordsAsync(string id, int page)` from Task 1, plus `ConsoleOutput.WriteRawJson`, `CommandHelper.BuildClient`, `AddHelpSection`, `AddExamples`, `AddResponseExample<T>`.
- Produces: `toc`, `page-text` and `page-words` subcommands on the `books` group.

- [ ] **Step 1: Write the failing tests**

Append to `tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs`, using whatever help-render helper that file already defines:

```csharp
    [Theory]
    [InlineData("toc")]
    [InlineData("page-text")]
    [InlineData("page-words")]
    public void TheGroupHostsTheReadingCommands(string leaf)
    {
        Assert.Contains(leaf, BooksCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void OnlyThePageCommandsTakeAPage()
    {
        var books = BooksCommand.Create();
        Assert.Empty(books.Parse(["toc", "--id", "b1"]).Errors);
        Assert.NotEmpty(books.Parse(["toc", "--id", "b1", "--page", "1"]).Errors);
        Assert.NotEmpty(books.Parse(["page-text", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["page-text", "--id", "b1", "--page", "1"]).Errors);
        Assert.NotEmpty(books.Parse(["page-words", "--id", "b1"]).Errors);
        Assert.Empty(books.Parse(["page-words", "--id", "b1", "--page", "1"]).Errors);
    }

    [Theory]
    [InlineData("toc")]
    [InlineData("page-text")]
    [InlineData("page-words")]
    public void NoReadingCommandDeclaresARole(string leaf)
    {
        Assert.DoesNotContain("Role required:",
            HelpRenderer.Render(BooksCommand.Create(), ["books", leaf], full: true));
    }

    [Theory]
    [InlineData("toc")]
    [InlineData("page-text")]
    [InlineData("page-words")]
    public void EveryReadingCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:",
            HelpRenderer.Render(BooksCommand.Create(), ["books", leaf], full: true));
    }

    // The asymmetry a caller cannot infer: an unopenable comic 404s on
    // page-text and answers 200 with an empty overlay here.
    [Fact]
    public void PageWordsWarnsAboutItsEmptyOverlay()
    {
        var help = HelpRenderer.Render(BooksCommand.Create(), ["books", "page-words"], full: false);
        Assert.Contains("width", help);
        Assert.Contains("page-text", help);
    }

    // Page text comes from the search index when there is one, so whether the
    // book is indexed changes the answer.
    [Fact]
    public void PageTextPointsAtTheIndexedFlag()
    {
        Assert.Contains("indexed",
            HelpRenderer.Render(BooksCommand.Create(), ["books", "page-text"], full: false));
    }

    // EPUB support is the non-obvious half of toc.
    [Fact]
    public void TocSaysItCoversEpub()
    {
        Assert.Contains("EPUB",
            HelpRenderer.Render(BooksCommand.Create(), ["books", "toc"], full: false));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj --filter BooksCommandTests`
Expected: the new tests fail; nothing else does.

- [ ] **Step 3: Register and implement the three commands**

Add three `command.Subcommands.Add(...)` lines in `BooksCommand.Create()`, placed after the existing `thumbnail` registration so the reading commands sit together. Then:

```csharp
    private static Command CreateTocCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var command = new Command("toc", "The book's table of contents")
        {
            idOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "PDF outlines and EPUB nav documents both work; 404 for a format",
            "that cannot be opened, which is indistinguishable from an unknown",
            "id.",
            "",
            "Entries nest through children, and page is where the entry points.");
        command.AddExamples("grimoire-cli books toc --id <book-id>");
        command.AddResponseExample<Generated.Models.TocResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.TocAsync(parseResult.GetValue(idOption)!);
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreatePageTextCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var pageOption = new Option<int>("--page") { Description = "1-based page number", Required = true };
        var command = new Command("page-text", "The text of one page")
        {
            idOption, pageOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Served from the search index when the page has a row, else",
            "extracted live — so a scan yields what OCR found, and nothing",
            "until indexed is true in books get.",
            "",
            "Plain-text and Markdown books are readable too, not just PDF.",
            "",
            "A page outside the book is 400 with the real count; books get",
            "reports page_count. 404 also covers a file missing from disk.");
        command.AddExamples(
            "grimoire-cli books page-text --id <book-id> --page 241",
            "grimoire-cli search --query \"grappling\" | jq '.results[0].page_number'");
        command.AddResponseExample<Generated.Models.PageTextResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.PageTextAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(pageOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }

    private static Command CreatePageWordsCommand()
    {
        var idOption = new Option<string>("--id") { Description = "Book ID", Required = true };
        var pageOption = new Option<int>("--page") { Description = "1-based page number", Required = true };
        var command = new Command("page-words", "Word bounding boxes for one page")
        {
            idOption, pageOption
        };
        command.AddHelpSection("Notes", HelpSectionPosition.Top,
            "Boxes are in PDF points against the width and height reported",
            "alongside them — for locating text on a rendered page, not for",
            "reading it. Use books page-text to read.",
            "",
            "Only PDF, EPUB and DjVu carry word boxes. Anything else — a text",
            "book, a comic — answers 200 with width 0 and no words, so an empty",
            "result does not mean the page is blank.");
        command.AddExamples("grimoire-cli books page-words --id <book-id> --page 241");
        command.AddResponseExample<Generated.Models.PageWordsResponse>();
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (client, _) = CommandHelper.BuildClient();
            var service = new BooksService(client);
            var result = await service.PageWordsAsync(
                parseResult.GetValue(idOption)!,
                parseResult.GetValue(pageOption));
            ConsoleOutput.WriteRawJson(result);
            return 0;
        });
        return command;
    }
```

The second `page-text` example cross-references `search`, which is the command that produces a page number worth reading. Confirm `search --query` and the `page_number` field are really what that command emits before shipping that line — run `grimoire-cli search --help` and check. If the flag or field differs, correct the example; if `search` does not expose a page number at all, drop the second example rather than shipping a broken pipeline.

- [ ] **Step 4: Format, build, run the full suite**

```bash
dotnet format GrimoireCli.sln
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
```

Expected: all green.

- [ ] **Step 5: Read the rendered help, not the source**

```bash
for c in toc page-text page-words; do
  echo "### books $c"; dotnet run --project src/GrimoireCli -- books $c --help
done
```

Check: none shows a Role required section; each Notes block is terse and says nothing already visible from the flags or the response sample. Trim anything that fails, except a line a Step 1 assertion depends on.

- [ ] **Step 6: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add src/GrimoireCli/Commands/BooksCommand.cs tests/GrimoireCli.Tests/Commands/BooksCommandTests.cs
git commit -m "feat: add books toc, page-text and page-words"
```

---

### Task 3: Smoke coverage and the live checks

**Files:**
- Modify: `docker/smoke-test.sh` (append a `--- book reading ---` block before the final `echo "smoke: all checks passed"`)

**Constraint:** this block only reads. Nothing needs restoring and nothing can drift — which makes it the easy one, so the effort goes into asserting things that would actually catch a regression rather than things that are true of any JSON.

- [ ] **Step 1: Bring up the stack if it is not already up**

```bash
docker compose -f docker/docker-compose.yml ps
# only if down:
mkdir -p docker/data && cp -n docker/users.json.example docker/data/users.json
docker compose -f docker/docker-compose.yml up -d --wait
bash docker/seed.sh
```

- [ ] **Step 2: Run the live checks by hand and record the answers**

```bash
dotnet build GrimoireCli.sln
bash docker/smoke-test.sh >/dev/null   # logs in as admin
CLI=src/GrimoireCli/bin/Debug/net10.0/grimoire-cli

BOOK=$($CLI books list | jq -r '.books[0].id')
$CLI books get --id "$BOOK" | jq '{title, mime_type, indexed, page_count}'
$CLI books toc --id "$BOOK"
$CLI books page-text --id "$BOOK" --page 1
$CLI books page-words --id "$BOOK" --page 1 | jq '{width, height, word_count: (.words|length)}'
$CLI books page-text --id "$BOOK" --page 99; echo "text page 99 exit=$?"
$CLI books page-words --id "$BOOK" --page 99; echo "words page 99 exit=$?"

# Is there a non-PDF book among the fixtures? If so, it is the one that shows
# the empty-overlay branch, which is the behaviour most worth pinning.
$CLI books list | jq -r '.books[] | "\(.mime_type)\t\(.id)\t\(.title)"'
```

Record every exit code and body. Two findings decide what the smoke block can assert, and your report must state both:

- whether any fixture book is a format PyMuPDF cannot open — if one exists, add an assertion that `page-words` answers 200 with `width == 0` on it while `page-text` fails, which is the asymmetry this block most wants to catch;
- what page 99 actually returns on each command, since the spec predicts 400 with the page count and that is unverified.

- [ ] **Step 3: Append the smoke block**

Insert before the final `echo "smoke: all checks passed" >&2`. Adjust every `jq` path to what the responses actually carry.

```bash
# --- book reading ------------------------------------------------------------
# Reads only: nothing to restore, nothing to converge.
BR_BOOK=$("$CLI" books list 2>"$WORK/cli.err" | jq -r '.books[0].id') \
  || { cat "$WORK/cli.err" >&2; fail "books list exited non-zero"; }
[ -n "$BR_BOOK" ] && [ "$BR_BOOK" != "null" ] || fail "no book fixture for the reading checks"

TOC_JSON=$("$CLI" books toc --id "$BR_BOOK" 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books toc exited non-zero"; }
echo "$TOC_JSON" | jq -e 'has("toc") and (.toc | type == "array")' >/dev/null \
  || fail "books toc should answer a toc array: $TOC_JSON"
ok "books toc answers a toc array"

TEXT_JSON=$("$CLI" books page-text --id "$BR_BOOK" --page 1 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books page-text exited non-zero"; }
[ -n "$(echo "$TEXT_JSON" | jq -r '.text // ""')" ] \
  || fail "page 1 of the fixture book should carry text: $TEXT_JSON"
ok "books page-text reads a page"

WORDS_JSON=$("$CLI" books page-words --id "$BR_BOOK" --page 1 2>"$WORK/cli.err") \
  || { cat "$WORK/cli.err" >&2; fail "books page-words exited non-zero"; }
echo "$WORDS_JSON" | jq -e '.width > 0 and (.words | length) > 0' >/dev/null \
  || fail "page 1 should report a page size and some words: $WORDS_JSON"
echo "$WORDS_JSON" | jq -e '.words[0] | has("x0") and has("text")' >/dev/null \
  || fail "each word should carry a box and its text: $WORDS_JSON"
ok "books page-words reports boxed words"

# The page bound is the server's, not the CLI's — it answers 400 with the real
# count rather than silently clamping.
"$CLI" books page-text --id "$BR_BOOK" --page 99 >/dev/null 2>&1 \
  && fail "books page-text should refuse a page past the end"
ok "books page-text refuses a page past the end"

"$CLI" books page-words --id "$BR_BOOK" --page 99 >/dev/null 2>&1 \
  && fail "books page-words should refuse a page past the end"
ok "books page-words refuses a page past the end"
```

If Step 2 found a non-PDF fixture book, add the empty-overlay assertion for it and say so in your report. If it found none, leave it out and record that the branch ships without live coverage — do not invent a fixture.

- [ ] **Step 4: Run the smoke test twice**

```bash
bash docker/smoke-test.sh && bash docker/smoke-test.sh
```

Expected: `smoke: all checks passed` both times. Run them strictly one after another, never concurrently — the script rewrites `$HOME/.grimoire-cli/config.json`.

- [ ] **Step 5: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add docker/smoke-test.sh
git commit -m "test: cover the book reading commands in the smoke test"
```

---

### Task 4: Docs

**Files:**
- Modify: `README.md`, `tools/generate-api-coverage.py`, `docs/grimoire-api-coverage.md`, `docs/grimoire-api-notes.md`

`docs/roadmap.md` is not touched.

- [ ] **Step 1: Add the README rows**

With the books group, after the `books thumbnail` row:

```markdown
| `books toc --id <id>` | The book's table of contents (PDF or EPUB) |
| `books page-text --id <id> --page <n>` | The text of one page |
| `books page-words --id <id> --page <n>` | Word bounding boxes for one page |
```

- [ ] **Step 2: Add the coverage entries**

```python
    "GET /api/books/{book_id}/toc": "`books toc` ✅",
    "GET /api/books/{book_id}/page/{page_num}/text": "`books page-text` ✅",
    "GET /api/books/{book_id}/page/{page_num}/words": "`books page-words` ✅",
```

Check the path-parameter names against the existing rows in `docs/grimoire-api-coverage.md`.

- [ ] **Step 3: Regenerate the coverage table**

```bash
tools/generate-api-coverage.py
git diff --stat docs/grimoire-api-coverage.md
```

Expected: exactly the three rows change, plus the derived `books` and total lines. Any other row moving means the pin or the clone moved — stop and report rather than committing the drift.

- [ ] **Step 4: Record the verified behaviour**

Add to `docs/grimoire-api-notes.md`, matching the file's existing heading style and placement:

```markdown
### Book reading

- `GET /api/books/{id}/page/{n}/text` reads the `book_search` FTS row for that
  page and only extracts live when there is none
  (`routers/books/pages.py:221-245`, v1.7.1), so an answer depends on the book
  being indexed and, for a scan, on OCR. Its format gate is `can_index`, not an
  `application/` MIME prefix, so `text/plain` and `text/markdown` books are
  readable too (`:215-219`).
- `GET /api/books/{id}/page/{n}/words` answers **200 with an empty overlay**
  (`{"width": 0, "height": 0, "words": []}`) for any book outside the `fitz`
  format family (`pages.py:256-259`). Its `page-text` sibling only 404s for
  the comic family — the text family is `can_index`, so `page-text` succeeds
  there while `page-words` still returns the empty overlay. An empty result
  therefore does not distinguish "not a renderable document" from "no words on
  this page"; `width` does.
- A page outside the book is **400 with the real page count** in the message,
  not 404 (`pages.py:237-238, 243-244, 270`).
- `GET /api/books/{id}/toc` covers EPUB as well as PDF: the handler gates on
  `is_fitz_mime`, and PyMuPDF exposes an EPUB's nav document through the same
  API as a PDF outline (`pages.py:64-77`). Its 404 is bare, so an unknown id
  and an unopenable format are indistinguishable from the response.
```

Replace any line with what Task 3 actually observed if the stack disagrees, and say so in the PR body.

- [ ] **Step 5: Commit**

The message is exactly the line below — no trailer, no attribution, no co-author line.

```bash
git add README.md tools/generate-api-coverage.py docs/grimoire-api-coverage.md docs/grimoire-api-notes.md
git commit -m "docs: record the book reading commands"
```

---

### Task 5: Pre-PR verification and the PR

- [ ] **Step 1: Prove the branch carries no attribution**

```bash
git log --format='%B' $(git merge-base main HEAD)..HEAD | grep -niE '^Co-Authored-By:|Generated with \[Claude|🤖|noreply@anthropic'
```

Expected: **no output.** If anything matches, stop and report — the fix is a history rewrite on the branch before it is pushed, which is cheap now and expensive after the merge.

- [ ] **Step 2: Run all four checks**

```bash
dotnet format GrimoireCli.sln --verify-no-changes
dotnet build GrimoireCli.sln
dotnet test tests/GrimoireCli.Tests/GrimoireCli.Tests.csproj
bash docker/smoke-test.sh
```

All four must pass. Report the actual output; do not claim a pass that was not run.

- [ ] **Step 3: Open the PR**

```bash
git push -u origin feat/book-reading
gh pr create --title "feat: book reading" --body "…"
```

The body names the three commands, explains what they complete (`search` finds a term on page 241 and nothing could read page 241), states the `page-words` empty-overlay asymmetry, says what shipped without live coverage and why, and records the verification that was run. **The body carries no attribution line of any kind** — no "Generated with Claude Code", no robot emoji, nothing naming a model or tool.

- [ ] **Step 4: Watch CI to a terminal state**

```bash
gh pr checks --watch
```

Report the result without being asked, and present the PR URL as a clickable link.
