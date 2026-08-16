using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class SystemsService
{
    private readonly GrimoireApiClient _client;

    public SystemsService(GrimoireApiClient client) => _client = client;

    public async Task<List<GameSystemSummary>> ListAsync(
        string? sort, bool desc, string? genre, string? family,
        string? parentSystem, string? edition, string? license, bool? isExplicit,
        string? parentId, bool includeChildren)
    {
        var info = _client.Api.Api.Systems.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Sort = sort;
            c.QueryParameters.Order = desc ? "desc" : null;
            c.QueryParameters.Genre = genre;
            c.QueryParameters.Family = family;
            c.QueryParameters.ParentSystem = parentSystem;
            c.QueryParameters.Edition = edition;
            c.QueryParameters.License = license;
            c.QueryParameters.Explicit = isExplicit;
            c.QueryParameters.ParentId = parentId;
            // Sent only when true: the server default is false, so an omitted
            // parameter and an explicit false mean the same thing.
            c.QueryParameters.IncludeChildren = includeChildren ? true : null;
        });
        return await _client.SendAsync(info, AppJsonContext.Default.ListGameSystemSummary);
    }

    public async Task<GameSystemDetail> GetAsync(
        string id, string? bookSort, bool bookDesc, string? genre, string? category, bool? isExplicit)
    {
        var info = _client.Api.Api.Systems[id].ToGetRequestInformation(c =>
        {
            c.QueryParameters.BookSort = bookSort;
            c.QueryParameters.BookOrder = bookDesc ? "desc" : null;
            c.QueryParameters.Genre = genre;
            c.QueryParameters.Category = category;
            c.QueryParameters.Explicit = isExplicit;
        });
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.GameSystemDetail,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }

    /// <summary>
    /// PATCH /api/systems/{id}. The generated builder is used for the URL, method and
    /// path parameter only; its request model would transmit unknown keys
    /// (IAdditionalDataHolder), so the validated raw body replaces the content and
    /// reaches the server byte-for-byte. Returns the raw response — {"status":"ok"},
    /// which confirms nothing about what changed.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Systems[id].ToPatchRequestInformation(
            new Generated.Models.GameSystemUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }

    /// <summary>
    /// POST /api/systems/bulk. One transaction, skip-and-continue: an unresolved id
    /// or a rejected item goes to errors and the rest still apply. Tag creation is
    /// serialised here, which per-item concurrent PATCHes could not do.
    /// </summary>
    public async Task<BulkUpdateResult> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Systems.Bulk.ToPostRequestInformation(
            new Generated.Models.GameSystemBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BulkUpdateResult,
            permissionHint: "the gm or admin role");
    }

    /// <summary>POST /api/systems/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<BulkTagResult> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Systems.Bulk.Tags.ToPostRequestInformation(
            new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BulkTagResult,
            permissionHint: "the gm or admin role");
    }

    /// <summary>GET /api/systems/{id}/cover. Bytes: folder art if the library has
    /// any, otherwise the uploaded cover; 404 when it has neither.</summary>
    public async Task<Stream> CoverAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].Cover.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>
    /// POST /api/systems/{id}/cover. The CLI's only multipart body: a
    /// <see cref="Microsoft.Kiota.Abstractions.MultipartBody"/> with one part named
    /// "file" — the name FastAPI binds — built with the generated builder's own
    /// <c>ToPostRequestInformation</c>. An empty <c>MultipartBody</c> throws ("No
    /// parts to serialize"), so the part must be added before that call.
    /// </summary>
    public async Task<CoverUploadResult> UploadCoverAsync(string id, string filePath)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyInputException($"Could not read {filePath}: {ex.Message}");
        }
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("file", MimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        var info = _client.Api.Api.Systems[id].Cover.ToPostRequestInformation(body);
        return await _client.SendAsync(info, AppJsonContext.Default.CoverUploadResult, permissionHint: "the gm or admin role");
    }

    /// <summary>
    /// The content type the server checks `file.content_type` against. Unknown
    /// extensions send octet-stream and let the server refuse — which types are
    /// acceptable is its policy, not ours. Internal so a test can pin the map.
    /// </summary>
    internal static string MimeForExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>DELETE /api/systems/{id}/cover. Removes the upload only; folder
    /// art is library-managed. Raw {"status":"ok"}.</summary>
    public async Task<string> DeleteCoverAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].Cover.ToDeleteRequestInformation();
        return await _client.SendAsync(info, permissionHint: "the gm or admin role");
    }

    /// <summary>GET /api/systems/{id}/book-folders. Tags come back in display casing.</summary>
    public async Task<BookFolderList> BookFoldersAsync(string id)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToGetRequestInformation();
        return await _client.SendAsync(info, AppJsonContext.Default.BookFolderList,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }

    /// <summary>
    /// PATCH /api/systems/{id}/book-folders. Replaces the folder's tags. The
    /// server ignores the id in the URL and writes whatever path the body names;
    /// the validated raw body reaches it byte-for-byte, as the update commands do.
    /// </summary>
    public async Task<BookFolderUpdated> SetBookFolderAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Systems[id].BookFolders.ToPatchRequestInformation(
            new Generated.Models.BookFolderUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, AppJsonContext.Default.BookFolderUpdated,
            permissionHint: "the gm or admin role");
    }
}
