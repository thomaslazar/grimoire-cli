using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The nine endpoints behind `models` and `models folders`. The folder verbs live
/// here rather than in their own service because they are one collection's
/// endpoints, the way MapsService carries `maps folders`.
/// </summary>
public class ModelsService
{
    private const string GmHint = "the gm or admin role";
    private const string NotFoundHint = "No model with that ID. List them with: grimoire-cli models list";

    private readonly GrimoireApiClient _client;

    public ModelsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/models. Variants and disallowed explicit rows are excluded server-side.</summary>
    public async Task<string> ListAsync(int? limit, int? offset)
    {
        var info = _client.Api.Api.Models.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/models/{id}. Carries the derived is_presupported/is_unsupported pair.</summary>
    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Models[id].ToGetRequestInformation();
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/models/{id}/thumbnail. 404 when no thumbnail was rendered.</summary>
    public async Task<Stream> ThumbnailAsync(string id)
    {
        var info = _client.Api.Api.Models[id].Thumbnail.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>
    /// PATCH /api/models/{id}. The generated builder supplies the URL, method and
    /// path parameter only; the validated raw body replaces the content so it
    /// reaches the server byte-for-byte.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Models[id].ToPatchRequestInformation(new Generated.Models.Model3DUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/models/bulk. One transaction; only an unresolved id reaches errors.</summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Models.Bulk.ToPostRequestInformation(new Generated.Models.Model3DBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/models/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Models.Bulk.Tags.ToPostRequestInformation(new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>GET /api/model-folders. Resolves stored keys to display casing.</summary>
    public async Task<string> FoldersListAsync()
    {
        var info = _client.Api.Api.ModelFolders.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/model-folders. Keyed by the path in the body; echoes internal keys.</summary>
    public async Task<string> FoldersSetAsync(string rawBody)
    {
        var info = _client.Api.Api.ModelFolders.ToPatchRequestInformation(new Generated.Models.FolderTagsUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/model-folders/bulk. One transaction; no per-item error list.</summary>
    public async Task<string> FoldersBatchSetAsync(string rawBody)
    {
        var info = _client.Api.Api.ModelFolders.Bulk.ToPostRequestInformation(new Generated.Models.BulkFolderTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
