using System.Text;
using System.Text.Json.Nodes;
using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The board routes behind `campaigns gm-screen` (routers/campaigns/gm_screen.py).
/// Each request is built by an internal method so a test can pin its route and
/// method. The owner check is made inside the server's handlers, so no
/// permission hint is passed.
/// </summary>
public class CampaignGmScreenService
{
    private const string CampaignNotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignGmScreenService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns/{id}/gm-screen.</summary>
    public async Task<string> GetAsync(string campaignId)
        => await _client.SendAsync(GetRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/gm-screen/party.</summary>
    public async Task<string> PartyAsync(string campaignId)
        => await _client.SendAsync(PartyRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/gm-screen/export.</summary>
    public async Task<string> ExportAsync(string campaignId)
        => await _client.SendAsync(ExportRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/gm-screen/sources.</summary>
    public async Task<string> SourcesAsync(string campaignId)
        => await _client.SendAsync(SourcesRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>GET /api/campaigns/{id}/gm-screen/presets.</summary>
    public async Task<string> PresetsAsync(string campaignId)
        => await _client.SendAsync(PresetsRequest(campaignId), notFoundHint: CampaignNotFound);

    /// <summary>POST /api/campaigns/{id}/gm-screen/reset.</summary>
    public async Task<string> ResetAsync(string campaignId, string scope)
        => await _client.SendAsync(ResetRequest(campaignId, scope), notFoundHint: CampaignNotFound);

    /// <summary>PUT /api/campaigns/{id}/gm-screen/layout. The validated raw body is sent as-is.</summary>
    public async Task<string> LayoutAsync(string campaignId, string rawBody)
        => await _client.SendAsync(LayoutRequest(campaignId, rawBody), notFoundHint: CampaignNotFound);

    /// <summary>POST /api/campaigns/{id}/gm-screen/import.</summary>
    public async Task<string> ImportAsync(string campaignId, string documentJson, bool append)
        => await _client.SendAsync(ImportRequest(campaignId, documentJson, append), notFoundHint: CampaignNotFound);

    /// <summary>POST /api/campaigns/{id}/gm-screen/copy.</summary>
    public async Task<string> CopyAsync(string campaignId, string sourceCampaignId, bool append)
        => await _client.SendAsync(CopyRequest(campaignId, sourceCampaignId, append), notFoundHint: CampaignNotFound);

    /// <summary>POST /api/campaigns/{id}/gm-screen/presets/apply.</summary>
    public async Task<string> ApplyPresetAsync(string campaignId, string presetId, string? indexUrl, bool append)
        => await _client.SendAsync(ApplyPresetRequest(campaignId, presetId, indexUrl, append), notFoundHint: CampaignNotFound);

    internal RequestInformation GetRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.ToGetRequestInformation();

    internal RequestInformation PartyRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Party.ToGetRequestInformation();

    internal RequestInformation ExportRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Export.ToGetRequestInformation();

    internal RequestInformation SourcesRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Sources.ToGetRequestInformation();

    internal RequestInformation PresetsRequest(string campaignId)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Presets.ToGetRequestInformation();

    internal RequestInformation ResetRequest(string campaignId, string scope)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Reset.ToPostRequestInformation(
            new Generated.Models.GmResetRequest { Scope = Enum.Parse<Generated.Models.GmResetRequest_scope>(scope, ignoreCase: true) });

    internal RequestInformation LayoutRequest(string campaignId, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[campaignId].GmScreen.Layout.ToPutRequestInformation(new Generated.Models.GmLayoutUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation ImportRequest(string campaignId, string documentJson, bool append)
    {
        var info = _client.Api.Api.Campaigns[campaignId].GmScreen.Import.ToPostRequestInformation(new Generated.Models.GmImportRequest());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(BuildImportBody(documentJson, append))), "application/json");
        return info;
    }

    /// <summary>Wraps the exported document; --append sends replace false.</summary>
    internal static string BuildImportBody(string documentJson, bool append)
    {
        JsonObject node;
        try
        {
            node = JsonNode.Parse(documentJson) as JsonObject
                ?? throw new BodyInputException("The board document must be a JSON object, as gm-screen export writes it.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new BodyInputException($"The board document is not valid JSON: {ex.Message}");
        }
        return new JsonObject { ["document"] = node, ["replace"] = !append }.ToJsonString();
    }

    internal RequestInformation CopyRequest(string campaignId, string sourceCampaignId, bool append)
        => _client.Api.Api.Campaigns[campaignId].GmScreen.Copy.ToPostRequestInformation(
            new Generated.Models.GmCopyRequest { SourceCampaignId = sourceCampaignId, Replace = !append });

    internal RequestInformation ApplyPresetRequest(string campaignId, string presetId, string? indexUrl, bool append)
    {
        var body = new Generated.Models.GmApplyPresetRequest { PresetId = presetId, Replace = !append };
        if (indexUrl is not null)
            body.IndexUrl = new Generated.Models.GmApplyPresetRequest.GmApplyPresetRequest_index_url { String = indexUrl };
        return _client.Api.Api.Campaigns[campaignId].GmScreen.Presets.Apply.ToPostRequestInformation(body);
    }
}
