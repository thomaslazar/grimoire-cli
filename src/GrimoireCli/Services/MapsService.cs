using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The eight JSON endpoints behind `maps` and `maps folders`. The folder verbs
/// live here rather than in their own service because they are one collection's
/// endpoints, the way SystemsService carries `systems book-folders`.
/// </summary>
public class MapsService
{
    private const string GmHint = "the gm or admin role";
    private const string NotFoundHint = "No map with that ID. List them with: grimoire-cli maps list";

    private readonly GrimoireApiClient _client;

    public MapsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/maps. Variants are excluded server-side; only family mains are listed.</summary>
    public async Task<string> ListAsync(string? mapType, string? folder, int? limit, int? offset)
    {
        var info = _client.Api.Api.Maps.ToGetRequestInformation(c =>
        {
            c.QueryParameters.MapType = mapType;
            c.QueryParameters.Folder = folder;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/maps/{id}. Carries both the detected grid and the stored override.</summary>
    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Maps[id].ToGetRequestInformation();
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// PATCH /api/maps/{id}. The generated builder supplies the URL, method and
    /// path parameter only; the validated raw body replaces the content so it
    /// reaches the server byte-for-byte. That is what lets a literal 0 through,
    /// which is how a grid override is cleared.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Maps[id].ToPatchRequestInformation(new Generated.Models.MapUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/maps/bulk. One transaction, skip-and-continue via errors.</summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Maps.Bulk.ToPostRequestInformation(new Generated.Models.MapBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/maps/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Maps.Bulk.Tags.ToPostRequestInformation(new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>GET /api/map-folders. Resolves stored keys to display casing.</summary>
    public async Task<string> FoldersListAsync()
    {
        var info = _client.Api.Api.MapFolders.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/map-folders. Keyed by the path in the body; echoes internal keys.</summary>
    public async Task<string> FoldersSetAsync(string rawBody)
    {
        var info = _client.Api.Api.MapFolders.ToPatchRequestInformation(new Generated.Models.FolderTagsUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/map-folders/bulk. One transaction; no per-item error list.</summary>
    public async Task<string> FoldersBatchSetAsync(string rawBody)
    {
        var info = _client.Api.Api.MapFolders.Bulk.ToPostRequestInformation(new Generated.Models.BulkFolderTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
