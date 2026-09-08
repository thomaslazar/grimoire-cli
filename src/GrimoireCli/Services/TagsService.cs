using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The two tag reads. Both are guarded by get_current_user
/// (routers/tags/core.py), which carries no role, so neither send names a
/// permissionHint. The items path carries a tag key and 404s on one that
/// matches nothing, so that send names a notFoundHint.
/// </summary>
public class TagsService
{
    private const string NotFoundHint =
        "No such tag. List the tags with: grimoire-cli tags list";

    private readonly GrimoireApiClient _client;

    public TagsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/tags. Folder-derived tags are merged into the listing server-side.</summary>
    public async Task<string> ListAsync(string? inUseBy)
    {
        var info = _client.Api.Api.Tags.ToGetRequestInformation(c =>
            c.QueryParameters.InUseBy = inUseBy);
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/tags/{internal}/items.</summary>
    public async Task<string> ItemsAsync(string tag, string? resourceType)
    {
        var info = _client.Api.Api.Tags[tag].Items.ToGetRequestInformation(c =>
            c.QueryParameters.ResourceType = resourceType);
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }
}
