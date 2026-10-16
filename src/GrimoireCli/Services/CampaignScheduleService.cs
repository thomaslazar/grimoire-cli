using System.Text;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>A group campaign's recurring schedule (routers/campaigns/schedule.py). Access is checked by the server; no call carries a permission hint.</summary>
public class CampaignScheduleService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignScheduleService(GrimoireApiClient client) => _client = client;

    internal RequestInformation GetRequest(string id) => _client.Api.Api.Campaigns[id].Schedule.ToGetRequestInformation();

    internal RequestInformation SetRequest(string id, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[id].Schedule.ToPutRequestInformation(new Generated.Models.ScheduleUpsert());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string id) => _client.Api.Api.Campaigns[id].Schedule.ToDeleteRequestInformation();

    /// <summary>GET /api/campaigns/{id}/schedule.</summary>
    public async Task<string> GetAsync(string id) => await _client.SendAsync(GetRequest(id), notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/schedule. The validated raw body is sent as-is.</summary>
    public async Task<string> SetAsync(string id, string rawBody) => await _client.SendAsync(SetRequest(id, rawBody), notFoundHint: NotFound);

    /// <summary>DELETE /api/campaigns/{id}/schedule.</summary>
    public async Task<string> DeleteAsync(string id) => await _client.SendAsync(DeleteRequest(id), notFoundHint: NotFound);
}
