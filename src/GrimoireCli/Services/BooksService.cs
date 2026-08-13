using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class BooksService
{
    private readonly GrimoireApiClient _client;

    public BooksService(GrimoireApiClient client) => _client = client;

    public async Task<BookListResponse> ListAsync(string? systemId, string? category, int limit, int? offset)
    {
        var info = _client.Api.Api.Books.ToGetRequestInformation(c =>
        {
            c.QueryParameters.SystemId = systemId;
            c.QueryParameters.Category = category;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info, AppJsonContext.Default.BookListResponse);
    }

    public async Task<BookDetail> GetAsync(string id)
    {
        var info = _client.Api.Api.Books[id].ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BookDetail,
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
    public async Task<BulkUpdateResult> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Books.Bulk.ToPostRequestInformation(
            new Generated.Models.BookBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BulkUpdateResult,
            permissionHint: "the gm or admin role");
    }

    /// <summary>POST /api/books/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<BulkTagResult> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Books.Bulk.Tags.ToPostRequestInformation(
            new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BulkTagResult,
            permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// POST /api/books/{id}/reindex. Queues background OCR and returns immediately;
    /// the raw response is {"reindex_queued": ...}.
    /// </summary>
    public async Task<string> ReindexAsync(string id, int? ocrDpi)
    {
        var info = _client.Api.Api.Books[id].Reindex.ToPostRequestInformation(c => c.QueryParameters.OcrDpi = ocrDpi);
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No book with that ID. List them with: grimoire-cli books list");
    }

    /// <summary>
    /// POST /api/books/{id}/rescan. Queues a background re-read and returns
    /// immediately; the raw response is {"rescan_queued": ...} whether it started
    /// a new scan or was absorbed into one already running.
    /// </summary>
    public async Task<string> RescanAsync(string id)
    {
        var info = _client.Api.Api.Books[id].Rescan.ToPostRequestInformation();
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No book with that ID. List them with: grimoire-cli books list");
    }
}
