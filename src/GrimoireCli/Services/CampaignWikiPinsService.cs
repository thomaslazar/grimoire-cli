using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The pins on a campaign map page (routers/campaigns/wiki_pins.py, v1.11.0).
/// Pins are read through the page itself: GET /api/campaigns/{id}/wiki/{page_id}
/// returns them under <c>pins</c>.
/// </summary>
public class CampaignWikiPinsService
{
    private const string PageNotFound = "No such campaign or page, or not one you can see. List pages with: grimoire-cli campaigns wiki list --id <campaign-id>";
    private const string PinNotFound = "No such campaign, page or pin, or not one you can see. Pin ids are under pins in: grimoire-cli campaigns wiki get --id <campaign-id> --page-id <page-id>";

    private readonly GrimoireApiClient _client;

    public CampaignWikiPinsService(GrimoireApiClient client) => _client = client;

    /// <summary>POST /api/campaigns/{id}/wiki/{page_id}/pins.</summary>
    public async Task<string> CreateAsync(string campaignId, string pageId, Generated.Models.WikiMapPinCreate body)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Pins.ToPostRequestInformation(body),
            notFoundHint: PageNotFound);

    internal static Generated.Models.WikiMapPinCreate BuildCreateBody(
        double x, double y, string? label, string? targetPageId, string? targetTitle,
        string? icon, string? iconColor, string? visibility)
    {
        var body = new Generated.Models.WikiMapPinCreate { X = x, Y = y, Label = label, Visibility = visibility };
        if (targetPageId is not null)
            body.TargetPageId = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_target_page_id { String = targetPageId };
        if (targetTitle is not null)
            body.TargetTitle = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_target_title { String = targetTitle };
        if (icon is not null)
            body.Icon = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.WikiMapPinCreate.WikiMapPinCreate_icon_color { String = iconColor };
        return body;
    }

    /// <summary>PATCH /api/campaigns/{id}/wiki/{page_id}/pins/{pin_id}.</summary>
    public async Task<string> UpdateAsync(string campaignId, string pageId, string pinId, Generated.Models.WikiMapPinUpdate body)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Pins[pinId].ToPatchRequestInformation(body),
            notFoundHint: PinNotFound);

    /// <summary>Only the flags given reach the body. Internal so a test can pin that.</summary>
    internal static Generated.Models.WikiMapPinUpdate BuildUpdateBody(
        double? x, double? y, string? label, string? targetPageId, string? targetTitle,
        string? icon, string? iconColor, string? visibility)
    {
        var body = new Generated.Models.WikiMapPinUpdate();
        if (x is not null)
            body.X = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_x { Double = x };
        if (y is not null)
            body.Y = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_y { Double = y };
        if (label is not null)
            body.Label = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_label { String = label };
        if (targetPageId is not null)
            body.TargetPageId = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_target_page_id { String = targetPageId };
        if (targetTitle is not null)
            body.TargetTitle = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_target_title { String = targetTitle };
        if (icon is not null)
            body.Icon = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_icon { String = icon };
        if (iconColor is not null)
            body.IconColor = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_icon_color { String = iconColor };
        if (visibility is not null)
            body.Visibility = new Generated.Models.WikiMapPinUpdate.WikiMapPinUpdate_visibility { String = visibility };
        return body;
    }

    /// <summary>DELETE /api/campaigns/{id}/wiki/{page_id}/pins/{pin_id}. 204.</summary>
    public async Task<string> DeleteAsync(string campaignId, string pageId, string pinId)
        => await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Wiki[pageId].Pins[pinId].ToDeleteRequestInformation(),
            notFoundHint: PinNotFound);
}
