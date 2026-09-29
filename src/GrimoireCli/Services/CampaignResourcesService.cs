using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// A campaign's resource links (routers/campaigns/resources.py). The path's
/// resource id is the link's own id, not the linked library item's.
/// </summary>
public class CampaignResourcesService
{
    private const string NotFound = "No such campaign or link. List links with: grimoire-cli campaigns resources list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignResourcesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/resources, filtered to what the caller may see.</summary>
    public async Task<string> ListAsync(string campaignId)
        => await _client.SendAsync(_client.Api.Api.Campaigns[campaignId].Resources.ToGetRequestInformation(), notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/resources. 409 when the item is already linked.</summary>
    public async Task<string> AddAsync(string campaignId, string resourceType, string resourceId, string? visibility, string? categoryId)
    {
        var body = new Generated.Models.ResourceAdd { ResourceType = resourceType, ResourceId = resourceId };
        if (visibility is not null)
            body.Visibility = visibility;
        if (categoryId is not null)
            body.CategoryId = new Generated.Models.ResourceAdd.ResourceAdd_category_id { String = categoryId };
        return await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources.ToPostRequestInformation(body), notFoundHint: NotFound);
    }

    /// <summary>
    /// POST /api/campaigns/{id}/resources/bulk. Skips duplicates and unknown types
    /// silently and returns only the rows it created (resources.py:189-235).
    /// </summary>
    public async Task<string> BulkAsync(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].Resources.Bulk.ToPostRequestInformation(new Generated.Models.ResourceBulkAdd());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, notFoundHint: NotFound);
    }

    /// <summary>PATCH /api/campaigns/{id}/resources/{linkId}.</summary>
    public async Task<string> UpdateAsync(string campaignId, string linkId, string? visibility, string? categoryId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources[linkId].ToPatchRequestInformation(BuildUpdateBody(visibility, categoryId)),
            notFoundHint: NotFound);

    /// <summary>
    /// Only the flags given reach the body, so an omitted one is left alone.
    /// Internal so a test can pin that.
    /// </summary>
    internal static Generated.Models.ResourceUpdate BuildUpdateBody(string? visibility, string? categoryId)
    {
        var body = new Generated.Models.ResourceUpdate();
        if (visibility is not null)
            body.Visibility = new Generated.Models.ResourceUpdate.ResourceUpdate_visibility { String = visibility };
        if (categoryId is not null)
            body.CategoryId = new Generated.Models.ResourceUpdate.ResourceUpdate_category_id { String = categoryId };
        return body;
    }

    /// <summary>DELETE /api/campaigns/{id}/resources/{linkId}. 204; a `file` link's upload goes with it.</summary>
    public async Task<string> RemoveAsync(string campaignId, string linkId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources[linkId].ToDeleteRequestInformation(), notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/resources/reorder. Unknown ids are skipped.</summary>
    public async Task<string> ReorderAsync(string campaignId, string[] orderedIds)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Resources.Reorder.ToPutRequestInformation(
                new Generated.Models.ResourceReorder { OrderedIds = [.. orderedIds] }),
            notFoundHint: NotFound);
}
