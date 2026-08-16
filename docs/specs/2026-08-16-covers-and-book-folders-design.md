# System covers, book folders, and binary output — design

Date: 2026-08-16
Status: draft, awaiting review

## Goal

Six commands closing the remaining `systems` endpoints, plus the first read of a
binary body:

```
systems cover get | upload | delete
systems book-folders list | set
books thumbnail
```

This is roadmap item 1 ("the remaining systems endpoints") and, because
`cover get` and `books thumbnail` return bytes, it also settles the convention
roadmap item 4 was holding — see [Binary output](#binary-output-the-convention).

Out of scope: map, token and audio thumbnails, book files and page images. They
are the same convention applied to resources this CLI has no other commands for;
adding the convention here is what makes them cheap later.

## Grounding

Verified against Grimoire v1.5.6 by reading `temp/grimoire`:
`backend/routers/systems/covers.py`, `backend/routers/systems/core.py` and
`backend/services/tag_service.py`.

### Covers exist on systems, not books

`GET`, `POST` and `DELETE` all live on one path, `/api/systems/{id}/cover`.
Books have only `GET /api/books/{id}/thumbnail`, derived from the file during a
scan — there is no book cover to upload or delete.

**Three sources of system cover art, in precedence order** (`systems/__init__.py:96`):

1. a `cover.*` / `folder.*` image in the system's library folder — library-managed,
   not reachable through the API;
2. an uploaded cover, stored under `SYSTEM_COVER_DIR` and recorded in
   `system.cover_image`;
3. neither, in which case `GET` 404s and clients fall back to `cover_book_id` —
   a book's thumbnail, served by the *book* endpoint.

So an upload can succeed and change nothing about what `GET` returns, and a
delete never touches folder art. Both facts belong in help text.

`POST` is `multipart/form-data` with the part named `file`
(`covers.py:122-153`). It rejects on `file.content_type` against
`image/png`, `image/jpeg`, `image/webp`, `image/gif`; caps at 10 MB (413);
rejects empty bodies; and runs `PIL.Image.verify()` so a disguised file is a 400
even when the type is right. It replaces any existing upload and answers
`{"cover_image": "<system-id><ext>"}`. `DELETE` answers `{"status": "ok"}`.

### Book folders are a second, invisible tagging layer

A book folder is a subcategory folder *inside* a system —
`{system_id}/{category}/{subfolder…}` — and the model
(`models/library.py:167`) has three columns: `id`, `path`, `tags`. Tagging one
covers every book at or below that path, computed on read by
`_book_folder_ancestor_paths` (`tag_service.py:63`). A book sitting directly in
the category dir belongs to no folder.

**The inheritance never reaches a book's own `tags`.** `tags_for_resource`
(`tag_service.py:379`) reads only the `ResourceTag` join table; folder
inheritance is resolved separately in `folder_tags_in_use` (`:509`), which
serves the tag catalogue. So `books get` and `systems get` do not show inherited
tags, and `book-folders list` is the only way to see that the layer exists.

Three behaviours to surface, all in `systems/core.py:261-288`:

- **`PATCH` replaces the tag list**, where `books batch-tag` and
  `systems batch-tag` are additive. An empty `tags` clears the folder.
- **`{system_id}` in the URL is ignored by the write.** `update_book_folder`
  takes it as a path parameter and never reads it; `data.path` alone decides
  which row is written, and nothing validates that the path belongs to that
  system or exists on disk. The `GET` *does* filter by
  `path.like(f"{system_id}/%")`, so read and write disagree about what the URL
  means.
- **Read and write return tags differently.** `GET` resolves stored internal
  keys to display casing (`folder_display_tags`); `PATCH` echoes the internal
  keys from `upsert_folder_tags`. A round trip need not match.

## Commands

`--server` and `--token` are declared per subcommand on all six.

| Command | Endpoint | Role | Output |
|---|---|---|---|
| `systems cover get --id --output` | `GET /systems/{id}/cover` | — | bytes or receipt |
| `systems cover upload --id --file` | `POST /systems/{id}/cover` | gm or admin | `{"cover_image": …}` |
| `systems cover delete --id` | `DELETE /systems/{id}/cover` | gm or admin | `{"status": "ok"}` |
| `systems book-folders list --id` | `GET /systems/{id}/book-folders` | — | `{"folders": […]}` |
| `systems book-folders set --id {--input\|--stdin}` | `PATCH /systems/{id}/book-folders` | gm or admin | `{"path", "tags"}` |
| `books thumbnail --id --output` | `GET /books/{id}/thumbnail` | — | bytes or receipt |

### Nested groups, and why the metadata trio stays flat

`cover` and `book-folders` become subgroups; `books thumbnail` stays a leaf.

The rule, harvested from what abs-cli actually does rather than from its prose:
**several HTTP methods on one path become a nested subgroup; distinct sibling
paths stay flat, with leaf names mirroring the path segment.** Every nested group
there is one path with several methods — `/api/items/:id/cover` is GET/POST/PATCH/
DELETE behind `items cover get|set|remove`, `/api/me/progress/:id` is
GET/PATCH/DELETE behind `items progress get|set|remove` — while
`/api/me/progress/batch/update`, a different path under the same noun, is the flat
`items batch-update-progress`.

Applied here: `/systems/{id}/cover` has three methods → nested.
`/systems/{id}/book-folders` has two → nested. `/books/{id}/thumbnail` has one,
with no verb set to host → flat, as abs-cli never creates a one-verb group.

**This corrects a rule, not a decision.** The metadata spec justified
`systems metadata-sources` with "every command in this CLI is two levels deep",
which was the wrong generalisation. Those three *are* sibling paths, so they are
flat for the same reason `items batch-update-progress` is, and they stay as
shipped. `docs/cli-design.md` gets the corrected rule so the next resource does
not re-derive it.

### Binary output, the convention

Ported from abs-cli, which settled it across `items cover get`,
`items file download`, `authors image get` and `backup download`:

- **`--output` is required**, described as
  `"Output file path, or '-' for binary to stdout"`.
- **`-`** copies the bytes to stdout and prints nothing else.
- **A path** writes the file, then prints a JSON receipt — a new `SavedFile`
  DTO of `{path, bytes}`, registered with `AddResponseExample<SavedFile>()`.

That keeps "stdout is valid JSON" true in the default case and makes the
exception explicit rather than ambient. The two commands share one helper,
`ConsoleOutput.WriteStreamAsync(Stream, string output)`, rather than repeating
the branch — abs-cli inlines it four times, which is the thing to avoid at two.

`GrimoireApiClient` gains `SendStreamAsync(RequestInformation, …)`, the existing
`SendAsync` with `ReadAsStreamAsync` in place of `ReadAsStringAsync`, so the
preflight version check, permission hints and error handling are unchanged.

The convention is recorded in `docs/cli-design.md` and `docs/input-output.md`,
because the remaining binary endpoints — book files, page images, map/token
thumbnails — inherit it without further debate.

### Multipart upload

`cover upload` is the CLI's first request body that is not JSON.

Kiota generated `ToPostRequestInformation(MultipartBody body, …)`, but
`MultipartBody` needs its `RequestAdapter` wired before it serialises. The repo
already has a cleaner precedent: `BooksService.UpdateAsync` uses the generated
builder for the URL, method and path parameter and replaces the content with
`info.SetStreamContent(...)`. The same shape applies here, with
`MultipartFormDataContent` from the BCL building the body:

```csharp
using var content = new MultipartFormDataContent();
var part = new ByteArrayContent(File.ReadAllBytes(path));
part.Headers.ContentType = new MediaTypeHeaderValue(MimeFor(path));
content.Add(part, "file", Path.GetFileName(path));
info.SetStreamContent(await content.ReadAsStreamAsync(), content.Headers.ContentType!.ToString());
```

The part name must be `file` — FastAPI binds `file: UploadFile = File(...)`.

**The content type is derived from the extension**, not sniffed: `.png`, `.jpg`,
`.jpeg`, `.webp`, `.gif`. Anything else sends `application/octet-stream` and the
server refuses with its own message. Deciding client-side which types are
acceptable would be mirroring server policy; picking *a* content type is merely
constructing the request, and the server validates the bytes regardless.

Size, emptiness and disguised files stay server-side — 413 and 400, surfacing as
exit 2 with the server's message.

## Response DTOs

New, registered on `AppJsonContext`:

- **`SavedFile`** — `path`, `bytes`. Local, not a server response; the receipt
  for a binary download.
- **`CoverUploadResult`** — `cover_image`.
- **`BookFolder`** — `path`, `tags`.
- **`BookFolderList`** — `folders`.
- **`BookFolderUpdated`** — `path`, `tags`. Distinct from `BookFolder` only in
  that its `tags` are internal keys rather than display strings; documented
  rather than merged, because merging would imply a round trip that does not
  hold.

`cover delete` answers `{"status": "ok"}` and registers no shape, naming the
value in Notes as `addons uninstall` does.

## Help text

Notes, verbatim.

**`systems cover get`**

```
Serves folder cover art if the system's library folder has a cover.* or
folder.* image, otherwise the uploaded cover. 404 when it has neither —
fall back to cover_book_id from systems get and books thumbnail.

--output - writes the image to stdout; a path writes the file and prints
{path, bytes}.
```

**`systems cover upload`**

```
PNG, JPEG, WebP or GIF, max 10 MB; the content type is taken from the
file extension. Replaces any existing upload.

Folder cover art still wins, so cover get may keep returning the library
image. 400 if the bytes are not a decodable image of the declared type.
```

**`systems cover delete`**

```
Removes the uploaded cover only; folder cover art is library-managed and
survives. Exits 0 whether or not one was uploaded.

Responds {"status": "ok"}.
```

**`systems book-folders list`**

```
Subcategory folder paths under this system and their tags. A folder's
tags are inherited by every book at or below its path, but never appear
in a book's own tags — this is the only place they are visible.

Books sitting directly in a category directory belong to no folder.
```

**`systems book-folders set`**

```
Replaces the folder's tag list; batch-tag adds. An empty tags array
clears it. Creates the folder record if the path has none.

path is {system-id}/{category}/{subfolder}, from book-folders list. The
server ignores the --id in the URL and writes whatever path the body
names, without checking that it belongs to this system or exists.

Tags echo back as internal keys; book-folders list shows display casing.
```

**`books thumbnail`**

```
The cover thumbnail generated from the file during a scan, not an
uploaded image. 404 when has_thumbnail is false in books list.

--output - writes the image to stdout; a path writes the file and prints
{path, bytes}.
```

## Exit codes

Nothing new. 0 on success, 2 on any HTTP error including the 404s and the
upload's 400/413, 1 on a missing required flag. `--output -` prints no JSON but
still exits 0.

## Testing

### Unit

- `Models/` — the five DTOs, including that `BookFolderList` survives a folder
  with an empty tag list.
- `Commands/` — presence and role tags for all six, the response-shape blocks,
  `--output` and `--file` being required, and that the Notes carry the four
  caveats that are not visible from the flags: folder art beating an upload,
  the replace-not-add semantics, the ignored `{system_id}`, and thumbnails being
  scan-derived.
- `Output/` — `WriteStreamAsync` writing bytes to a path and returning the byte
  count, and routing to stdout for `-`. This is the first test in that area.

### Smoke test

A fixture PNG is generated by `docker/make-fixtures.py` with PyMuPDF, already a
dependency there; Pillow is not installed in the devcontainer and is not needed
— a 1×1 pixmap saved as PNG satisfies both the content-type check and
`Image.verify()` server-side.

Sequence, appended after the systems section and before `--- books ---`:

1. `systems cover get` on a system with no cover exits 2 (the 404 path).
2. `systems cover upload` returns `cover_image` ending `.png`.
3. `systems cover get --output <file>` writes it and reports a matching byte
   count; `--output -` piped to a file produces identical bytes.
4. `systems cover delete` answers `{"status":"ok"}`, and `get` 404s again.
5. `systems book-folders set` on a fixed path with fixed tags, then
   `book-folders list` shows it.
6. `books thumbnail --output` on a book whose `has_thumbnail` is true.

Idempotent by construction: the cover sequence ends deleted, and the folder set
writes fixed values. Step 6 depends on the server having generated a thumbnail
for the fixture PDFs — if it has not, the assertion is on `has_thumbnail` first
and the download is skipped with a loud `ok:` line saying so, rather than
failing on a fixture property the CLI does not control.

**`Shadowrun 4 DE` is not used for the cover work.** The systems section already
writes its `description`, and the metadata section asserts three diff statuses
against it; adding cover writes to the same system couples another assertion to
that fixture. A different system keeps the two independent.

## Docs

- README Commands table — six rows.
- `IMPLEMENTED` in `tools/generate-api-coverage.py`, then regenerate; `systems`
  goes to fully covered except nothing, and `books` gains one.
- `docs/cli-design.md` — the nesting rule, and the binary-output convention.
- `docs/input-output.md` — `--output` and the `-` escape hatch, beside the
  existing stdout/stderr contract.
- `docs/grimoire-api-notes.md` — verified behaviour: the cover precedence chain,
  the ignored `{system_id}` on the folder PATCH, and the display-vs-internal tag
  asymmetry.
- `docs/roadmap.md` — item 1 drops; item 4 loses the binary endpoints this
  branch covers and keeps the rest.

## Risks

**The ignored `{system_id}` is a server bug we are documenting rather than
working around.** A caller can write another system's folder through any
system's URL. Validating client-side would be mirroring policy the server does
not have; the help text names it instead. Worth an upstream issue separately.

**`books thumbnail` depends on a fixture property.** Whether the seeded PDFs get
thumbnails is the server's business, and the smoke test asserts what it finds
rather than forcing it.

**The multipart path has no second user yet.** If a later endpoint needs a
different part name or several parts, the helper generalises then — not now.
