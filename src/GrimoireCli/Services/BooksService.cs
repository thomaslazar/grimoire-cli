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
}
