using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The nine endpoints behind `tokens` and `tokens folders`. The folder verbs live
/// here rather than in their own service because they are one collection's
/// endpoints, the way ModelsService carries `models folders`.
/// </summary>
public class TokensService
{
    private const string GmHint = "the gm or admin role";
    private const string NotFoundHint = "No token with that ID. List them with: grimoire-cli tokens list";

    private readonly GrimoireApiClient _client;

    public TokensService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/tokens. Variants and disallowed explicit rows are excluded server-side.</summary>
    public async Task<string> ListAsync(int? limit, int? offset)
    {
        var info = _client.Api.Api.Tokens.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/tokens/{id}. Carries the derived is_presupported/is_unsupported pair.</summary>
    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Tokens[id].ToGetRequestInformation();
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }

    /// <summary>GET /api/tokens/{id}/thumbnail. 404 when no thumbnail was rendered.</summary>
    public async Task<Stream> ThumbnailAsync(string id)
    {
        var info = _client.Api.Api.Tokens[id].Thumbnail.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>
    /// PATCH /api/tokens/{id}. The generated builder supplies the URL, method and
    /// path parameter only; the validated raw body replaces the content so it
    /// reaches the server byte-for-byte.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Tokens[id].ToPatchRequestInformation(new Generated.Models.TokenUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/tokens/bulk. One transaction; only an unresolved id reaches errors.</summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Tokens.Bulk.ToPostRequestInformation(new Generated.Models.TokenBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/tokens/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Tokens.Bulk.Tags.ToPostRequestInformation(new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>GET /api/token-folders. Resolves stored keys to display casing.</summary>
    public async Task<string> FoldersListAsync()
    {
        var info = _client.Api.Api.TokenFolders.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/token-folders. Keyed by the path in the body; echoes internal keys.</summary>
    public async Task<string> FoldersSetAsync(string rawBody)
    {
        var info = _client.Api.Api.TokenFolders.ToPatchRequestInformation(new Generated.Models.FolderTagsUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/token-folders/bulk. One transaction; no per-item error list.</summary>
    public async Task<string> FoldersBatchSetAsync(string rawBody)
    {
        var info = _client.Api.Api.TokenFolders.Bulk.ToPostRequestInformation(new Generated.Models.BulkFolderTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
