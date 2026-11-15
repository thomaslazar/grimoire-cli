using System.Text;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The panel routes behind `campaigns gm-screen panels` (routers/campaigns/gm_screen.py).
/// The owner check is made inside the server's handlers, so no permission hint is passed.
/// </summary>
public class CampaignGmPanelsService
{
    private const string CampaignNotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";
    private const string PanelNotFound = "No such campaign or panel. List panels with: grimoire-cli campaigns gm-screen get --id <campaign-id>";

    private readonly GrimoireApiClient _client;

    public CampaignGmPanelsService(GrimoireApiClient client) => _client = client;

    /// <summary>POST /api/campaigns/{id}/gm-screen/panels. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string campaignId, string rawBody)
        => await _client.SendAsync(CreateRequest(campaignId, rawBody), notFoundHint: CampaignNotFound);

    /// <summary>PATCH /api/campaigns/{id}/gm-screen/panels/{panel}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string campaignId, string panelId, string rawBody)
        => await _client.SendAsync(UpdateRequest(campaignId, panelId, rawBody), notFoundHint: PanelNotFound);

    /// <summary>DELETE /api/campaigns/{id}/gm-screen/panels/{panel}.</summary>
    public async Task<string> DeleteAsync(string campaignId, string panelId)
        => await _client.SendAsync(DeleteRequest(campaignId, panelId), notFoundHint: PanelNotFound);

    internal RequestInformation CreateRequest(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].GmScreen.Panels.ToPostRequestInformation(new Generated.Models.GmPanelCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string campaignId, string panelId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].GmScreen.Panels[panelId].ToPatchRequestInformation(new Generated.Models.GmPanelUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string campaignId, string panelId)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Panels[panelId].ToDeleteRequestInformation();
}
