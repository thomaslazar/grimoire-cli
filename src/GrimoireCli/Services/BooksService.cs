using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

public class BooksService
{
    private readonly GrimoireApiClient _client;

    public BooksService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync(string? systemId, string? category, int limit, int? offset)
    {
        var info = _client.Api.Api.Books.ToGetRequestInformation(c =>
        {
            c.QueryParameters.SystemId = systemId;
            c.QueryParameters.Category = category;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Books[id].ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            notFoundHint: "No book with that ID. List them with: grimoire-cli books list");
    }

    /// <summary>
    /// PATCH /api/books/{id}. The generated builder is used for the URL, method and
    /// path parameter only; its request model would transmit unknown keys
    /// (IAdditionalDataHolder), so the validated raw body replaces the content and
    /// reaches the server byte-for-byte. Returns the raw response — {"status":"ok"},
    /// which confirms nothing about what changed.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Books[id].ToPatchRequestInformation(
            new Generated.Models.BookUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No book with that ID. List them with: grimoire-cli books list");
    }

    /// <summary>
    /// POST /api/books/bulk. One transaction, skip-and-continue: an unresolved id
    /// or a rejected item goes to errors and the rest still apply.
    /// </summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Books.Bulk.ToPostRequestInformation(
            new Generated.Models.BookBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>POST /api/books/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Books.Bulk.Tags.ToPostRequestInformation(
            new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// POST /api/books/{id}/reindex. Queues background OCR and returns immediately;
    /// the raw response is {"reindex_queued": ...}. No notFoundHint: the endpoint
    /// raises two distinct 404s ("Book not found" and "File not found on disk"),
    /// and a hint would replace the server's detail with a message that cannot
    /// tell them apart.
    /// </summary>
    public async Task<string> ReindexAsync(string id, int? ocrDpi)
    {
        var info = _client.Api.Api.Books[id].Reindex.ToPostRequestInformation(c => c.QueryParameters.OcrDpi = ocrDpi);
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// POST /api/books/{id}/rescan. Queues a background re-read and returns
    /// immediately; the raw response is {"rescan_queued": ...} whether it started
    /// a new scan or no-oped under one already running. No notFoundHint: the
    /// endpoint raises two distinct 404s ("Book not found" and "File not found on
    /// disk"), and a hint would replace the server's detail with a message that
    /// cannot tell them apart.
    /// </summary>
    public async Task<string> RescanAsync(string id)
    {
        var info = _client.Api.Api.Books[id].Rescan.ToPostRequestInformation();
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// GET /api/books/{id}/thumbnail. Bytes, not JSON: the thumbnail generated
    /// from the file during a scan. 404 when the book has none.
    /// </summary>
    public async Task<Stream> ThumbnailAsync(string id)
    {
        var info = _client.Api.Api.Books[id].Thumbnail.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>
    /// GET /api/books/{id}/file. Serves the book as stored. Both 404s carry a
    /// detail ("Book not found", "File not found on disk"), so no notFoundHint.
    /// A missing file also flips the book's is_missing to true before the 404
    /// (routers/books/core.py:388-392).
    /// </summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Books[id].File.ToGetRequestInformation());

    /// <summary>
    /// GET /api/books/{id}/page/{n}. Renders PDF, EPUB and DjVu to WebP; serves
    /// a comic archive's page as the image member it already is, and a
    /// single-page image book as stored (routers/books/pages.py:126-146). A
    /// missing file flips the book's is_missing to true before the 404, at
    /// each of the three serving paths (pages.py:130-131, 140-141, 179-180).
    /// width defaults to 1200 server-side, not the 1600 maps uses, and is
    /// left unset when the flag is omitted.
    /// </summary>
    public async Task<Stream> PageAsync(string id, int page, int? width)
        => await _client.SendStreamAsync(
            _client.Api.Api.Books[id].Page[page].ToGetRequestInformation(c => c.QueryParameters.Width = width));

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
    /// (routers/books/pages.py:221-245) — a scan's result depends on the book
    /// being indexed, while a born-digital book extracts live with no index
    /// row at all. An out-of-range page is 400 with the real page count.
    /// </summary>
    // Four 404s in total: pages.py:60 ("Book not found") and :232 ("File not
    // found on disk") carry detail; :219 and :241 are bare. The hint names
    // the two bare causes — its own first clause already covers "Book not
    // found", so nothing informative is lost there, and masking the rarer
    // disk case is the accepted cost.
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
}
