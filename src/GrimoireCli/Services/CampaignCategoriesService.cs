using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// A campaign's resource categories (routers/campaigns/categories.py). Only the
/// resource kind is handled: note categories are legacy wiki data and creating
/// one is a 400 (categories.py:70-71).
/// </summary>
public class CampaignCategoriesService
{
    private const string NotFound = "No such campaign or category. List categories with: grimoire-cli campaigns categories list --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignCategoriesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/categories?kind=resource.</summary>
    public async Task<string> ListAsync(string campaignId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories.ToGetRequestInformation(c => c.QueryParameters.Kind = "resource"),
            notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/categories.</summary>
    public async Task<string> CreateAsync(string campaignId, string name, string? icon, string? iconColor)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories.ToPostRequestInformation(BuildCreateBody(name, icon, iconColor)),
            notFoundHint: NotFound);

    internal static Generated.Models.CategoryCreate BuildCreateBody(string name, string? icon, string? iconColor)
    {
        var body = new Generated.Models.CategoryCreate { Name = name, Kind = "resource" };
        if (icon is not null)
            body.Icon = new Generated.Models.CategoryCreate.CategoryCreate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.CategoryCreate.CategoryCreate_icon_color { String = iconColor };
        return body;
    }

    /// <summary>PATCH /api/campaigns/{id}/categories/{categoryId}.</summary>
    public async Task<string> UpdateAsync(string campaignId, string categoryId, string? name, string? icon, string? iconColor)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories[categoryId].ToPatchRequestInformation(BuildUpdateBody(name, icon, iconColor)),
            notFoundHint: NotFound);

    /// <summary>Only the flags given reach the body. Internal so a test can pin that.</summary>
    internal static Generated.Models.CategoryUpdate BuildUpdateBody(string? name, string? icon, string? iconColor)
    {
        var body = new Generated.Models.CategoryUpdate();
        if (name is not null)
            body.Name = new Generated.Models.CategoryUpdate.CategoryUpdate_name { String = name };
        if (icon is not null)
            body.Icon = new Generated.Models.CategoryUpdate.CategoryUpdate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.CategoryUpdate.CategoryUpdate_icon_color { String = iconColor };
        return body;
    }

    /// <summary>DELETE /api/campaigns/{id}/categories/{categoryId}. 204, including for an unknown id.</summary>
    public async Task<string> DeleteAsync(string campaignId, string categoryId, string? mode)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories[categoryId].ToDeleteRequestInformation(c => c.QueryParameters.Mode = mode),
            notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/categories/reorder. Unknown ids are skipped.</summary>
    public async Task<string> ReorderAsync(string campaignId, string[] orderedIds)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Categories.Reorder.ToPutRequestInformation(
                new Generated.Models.CategoryReorder { OrderedIds = [.. orderedIds] }),
            notFoundHint: NotFound);

    /// <summary>
    /// PUT /api/campaigns/{id}/resource-group-order. Keeps only known type keys and
    /// this campaign's resource categories, and echoes what it kept
    /// (categories.py:163-183).
    /// </summary>
    public async Task<string> GroupOrderAsync(string campaignId, string[] orderedKeys)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].ResourceGroupOrder.ToPutRequestInformation(
                new Generated.Models.ResourceGroupOrder { OrderedKeys = [.. orderedKeys] }),
            notFoundHint: NotFound);
}
