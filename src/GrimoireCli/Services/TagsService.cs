using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The two tag reads and the four writes. The reads are guarded by
/// get_current_user (routers/tags/core.py), which carries no role; all four
/// writes are require_gm_or_admin, so each names a permissionHint. Every path
/// carrying a tag key 404s on one that matches nothing, so those sends name a
/// notFoundHint — including merge, whose 404 is about the source tag.
/// </summary>
public class TagsService
{
    private const string NotFoundHint =
        "No such tag. List the tags with: grimoire-cli tags list";

    private const string GmOrAdminHint = "the gm or admin role";

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

    /// <summary>
    /// POST /api/tags. Answers 201. Idempotent by internal key: creating a tag
    /// that already exists returns the existing row rather than failing.
    /// </summary>
    public async Task<string> CreateAsync(string value, string? display)
    {
        var info = _client.Api.Api.Tags.ToPostRequestInformation(BuildCreateBody(value, display));
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint);
    }

    /// <summary>
    /// TagCreate.Display is a composed-type wrapper whose constructor sets
    /// nothing, so assigning through it only when --display was given leaves an
    /// omitted one absent from the body. Internal so a test can pin that a
    /// client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.TagCreate BuildCreateBody(string value, string? display)
    {
        var body = new Generated.Models.TagCreate { Value = value };
        if (display is not null)
            body.Display = new Generated.Models.TagCreate.TagCreate_display { String = display };
        return body;
    }

    /// <summary>
    /// PATCH /api/tags/{internal}. The internal key follows the new display
    /// when its normalized form changes, and the tag is merged into whatever
    /// already owns that key (services/tag_service/_admin.py:68).
    /// </summary>
    public async Task<string> RenameAsync(string tag, string display)
    {
        var body = new Generated.Models.TagDisplayUpdate { Display = display };
        var info = _client.Api.Api.Tags[tag].ToPatchRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// POST /api/tags/{internal}/merge. Creates the target if it does not
    /// exist; 400s on a self-merge.
    /// </summary>
    public async Task<string> MergeAsync(string tag, string into)
    {
        var body = new Generated.Models.TagMerge { Into = into };
        var info = _client.Api.Api.Tags[tag].Merge.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint, notFoundHint: NotFoundHint);
    }

    /// <summary>DELETE /api/tags/{internal}. Answers 204, so the body is empty.</summary>
    public async Task<string> DeleteAsync(string tag)
    {
        var info = _client.Api.Api.Tags[tag].ToDeleteRequestInformation();
        return await _client.SendAsync(info, permissionHint: GmOrAdminHint, notFoundHint: NotFoundHint);
    }
}
