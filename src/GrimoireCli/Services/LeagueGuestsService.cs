using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>League guests (routers/leagues/guests.py). Access is checked by the server; no call carries a permission hint.</summary>
public class LeagueGuestsService
{
    private const string LeagueNotFound = "No league with that ID. List yours with: grimoire-cli leagues list";

    private readonly GrimoireApiClient _client;

    public LeagueGuestsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/leagues/{id}/guests.</summary>
    public async Task<string> ListAsync(string id) => await _client.SendAsync(ListRequest(id), notFoundHint: LeagueNotFound);

    /// <summary>POST /api/leagues/{id}/guests.</summary>
    public async Task<string> CreateAsync(string id, string? nickname) => await _client.SendAsync(CreateRequest(id, nickname), notFoundHint: LeagueNotFound);

    /// <summary>POST /api/leagues/{id}/guests/{user_id}/regenerate.</summary>
    public async Task<string> RegenerateAsync(string id, string userId) => await _client.SendAsync(RegenerateRequest(id, userId), notFoundHint: LeagueNotFound);

    /// <summary>GET /api/leagues/{id}/guests/{user_id}/share.</summary>
    public async Task<string> ShareAsync(string id, string userId) => await _client.SendAsync(ShareRequest(id, userId), notFoundHint: LeagueNotFound);

    /// <summary>DELETE /api/leagues/{id}/guests/{user_id}. 204.</summary>
    public async Task<string> RemoveAsync(string id, string userId) => await _client.SendAsync(RemoveRequest(id, userId), notFoundHint: LeagueNotFound);

    internal RequestInformation ListRequest(string id) => _client.Api.Api.Leagues[id].Guests.ToGetRequestInformation();

    // Nickname is set only when given, so an absent flag sends no key.
    internal RequestInformation CreateRequest(string id, string? nickname)
    {
        var body = new Generated.Models.Backend__routers__leagues___schemas__GuestCreate();
        if (nickname is not null)
        {
            body.Nickname = nickname;
        }
        return _client.Api.Api.Leagues[id].Guests.ToPostRequestInformation(body);
    }

    internal RequestInformation RegenerateRequest(string id, string userId) => _client.Api.Api.Leagues[id].Guests[userId].Regenerate.ToPostRequestInformation();

    internal RequestInformation ShareRequest(string id, string userId) => _client.Api.Api.Leagues[id].Guests[userId].Share.ToGetRequestInformation();

    internal RequestInformation RemoveRequest(string id, string userId) => _client.Api.Api.Leagues[id].Guests[userId].ToDeleteRequestInformation();
}
