using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Campaign calendar (routers/campaigns/calendar.py). Access is checked by the server; no call carries a permission hint.</summary>
public class CampaignCalendarService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignCalendarService(GrimoireApiClient client) => _client = client;

    internal RequestInformation DownloadRequest(string id) => _client.Api.Api.Campaigns[id].CalendarIcs.ToGetRequestInformation();

    internal RequestInformation SubscriptionRequest(string? campaignId)
        => _client.Api.Api.Campaigns.Calendar.Subscription.ToGetRequestInformation(c => c.QueryParameters.CampaignId = campaignId);

    internal RequestInformation CreateSubscriptionRequest(string? campaignId)
        => _client.Api.Api.Campaigns.Calendar.Subscription.ToPostRequestInformation(c => c.QueryParameters.CampaignId = campaignId);

    internal RequestInformation RevokeSubscriptionRequest() => _client.Api.Api.Campaigns.Calendar.Subscription.ToDeleteRequestInformation();

    /// <summary>GET /api/campaigns/{id}/calendar.ics.</summary>
    public async Task<Stream> DownloadAsync(string id) => await _client.SendStreamAsync(DownloadRequest(id), notFoundHint: NotFound);

    /// <summary>GET /api/campaigns/calendar/subscription. campaign_id is sent only when given.</summary>
    public async Task<string> SubscriptionAsync(string? campaignId) => await _client.SendAsync(SubscriptionRequest(campaignId));

    /// <summary>POST /api/campaigns/calendar/subscription. campaign_id is sent only when given.</summary>
    public async Task<string> CreateSubscriptionAsync(string? campaignId) => await _client.SendAsync(CreateSubscriptionRequest(campaignId));

    /// <summary>DELETE /api/campaigns/calendar/subscription.</summary>
    public async Task<string> RevokeSubscriptionAsync() => await _client.SendAsync(RevokeSubscriptionRequest());
}
