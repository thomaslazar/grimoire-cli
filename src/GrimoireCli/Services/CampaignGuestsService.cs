using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Campaign guests (routers/campaigns/guests.py). Access is checked by the server; no call carries a permission hint.</summary>
public class CampaignGuestsService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignGuestsService(GrimoireApiClient client) => _client = client;

    internal RequestInformation ListRequest(string id) => _client.Api.Api.Campaigns[id].Guests.ToGetRequestInformation();

    // Nickname is set only when given, so an absent flag sends no key.
    internal RequestInformation CreateRequest(string id, string? nickname)
    {
        var body = new Generated.Models.GuestCreate();
        if (nickname is not null)
        {
            body.Nickname = nickname;
        }
        return _client.Api.Api.Campaigns[id].Guests.ToPostRequestInformation(body);
    }

    internal RequestInformation RegenerateRequest(string id, string memberId) => _client.Api.Api.Campaigns[id].Guests[memberId].Regenerate.ToPostRequestInformation();

    internal RequestInformation RemoveRequest(string id, string memberId) => _client.Api.Api.Campaigns[id].Guests[memberId].ToDeleteRequestInformation();

    internal RequestInformation ShareRequest(string id, string memberId) => _client.Api.Api.Campaigns[id].Guests[memberId].ShareTemplate.ToGetRequestInformation();

    /// <summary>GET /api/campaigns/{id}/guests.</summary>
    public async Task<string> ListAsync(string id) => await _client.SendAsync(ListRequest(id), notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/guests.</summary>
    public async Task<string> CreateAsync(string id, string? nickname) => await _client.SendAsync(CreateRequest(id, nickname), notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/guests/{member_id}/regenerate.</summary>
    public async Task<string> RegenerateAsync(string id, string memberId) => await _client.SendAsync(RegenerateRequest(id, memberId));

    /// <summary>DELETE /api/campaigns/{id}/guests/{member_id}. 204.</summary>
    public async Task<string> RemoveAsync(string id, string memberId) => await _client.SendAsync(RemoveRequest(id, memberId));

    /// <summary>GET /api/campaigns/{id}/guests/{member_id}/share-template.</summary>
    public async Task<string> ShareAsync(string id, string memberId) => await _client.SendAsync(ShareRequest(id, memberId));
}
