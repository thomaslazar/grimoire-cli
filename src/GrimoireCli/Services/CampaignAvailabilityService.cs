using System.Text.Json;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Session availability (routers/campaigns/schedule.py). Access is checked by the server; no call carries a permission hint.</summary>
public class CampaignAvailabilityService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignAvailabilityService(GrimoireApiClient client) => _client = client;

    internal RequestInformation GetRequest(string id) => _client.Api.Api.Campaigns[id].Availability.ToGetRequestInformation();

    // The generated user_id is int-or-null, which cannot carry a string id, so
    // the body is written raw and the server decides what to do with it.
    internal RequestInformation SetRequest(string id, string date, string status, bool? isCancelled, string? userId)
    {
        var info = _client.Api.Api.Campaigns[id].Availability[date].ToPutRequestInformation(new Generated.Models.AvailabilityUpdate());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("status", status);
            if (isCancelled is { } c)
            {
                writer.WriteBoolean("is_cancelled", c);
            }
            if (userId is not null)
            {
                writer.WriteString("user_id", userId);
            }
            writer.WriteEndObject();
        }
        info.SetStreamContent(new MemoryStream(stream.ToArray()), "application/json");
        return info;
    }

    internal RequestInformation ToggleCancelRequest(string id, string date)
        => _client.Api.Api.Campaigns[id].Availability[date].Cancel.ToPutRequestInformation();

    /// <summary>GET /api/campaigns/{id}/availability.</summary>
    public async Task<string> GetAsync(string id) => await _client.SendAsync(GetRequest(id), notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/availability/{date}.</summary>
    public async Task<string> SetAsync(string id, string date, string status, bool? isCancelled, string? userId)
        => await _client.SendAsync(SetRequest(id, date, status, isCancelled, userId), notFoundHint: NotFound);

    /// <summary>PUT /api/campaigns/{id}/availability/{date}/cancel.</summary>
    public async Task<string> ToggleCancelAsync(string id, string date)
        => await _client.SendAsync(ToggleCancelRequest(id, date), notFoundHint: NotFound);
}
