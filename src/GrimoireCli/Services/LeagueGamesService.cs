using System.Text;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>League games (routers/leagues/core.py). Access is checked by the server; no call carries a permission hint.</summary>
public class LeagueGamesService
{
    private const string LeagueNotFound = "No league with that ID that you're in. List yours with: grimoire-cli leagues list";

    private const string GameNotFound = "No such league or game. Game IDs are in: grimoire-cli leagues get --id <league-id>";

    private readonly GrimoireApiClient _client;

    public LeagueGamesService(GrimoireApiClient client) => _client = client;

    /// <summary>POST /api/leagues/{id}/games. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string id, string rawBody) => await _client.SendAsync(CreateRequest(id, rawBody), notFoundHint: LeagueNotFound);

    /// <summary>PUT /api/leagues/{id}/games/{game_id}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string id, string gameId, string rawBody) => await _client.SendAsync(UpdateRequest(id, gameId, rawBody), notFoundHint: GameNotFound);

    /// <summary>DELETE /api/leagues/{id}/games/{game_id}. 204.</summary>
    public async Task<string> DeleteAsync(string id, string gameId) => await _client.SendAsync(DeleteRequest(id, gameId), notFoundHint: GameNotFound);

    internal RequestInformation CreateRequest(string id, string rawBody)
    {
        var info = _client.Api.Api.Leagues[id].Games.ToPostRequestInformation(new Generated.Models.GameIn());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string id, string gameId, string rawBody)
    {
        var info = _client.Api.Api.Leagues[id].Games[gameId].ToPutRequestInformation(new Generated.Models.GameIn());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string id, string gameId) => _client.Api.Api.Leagues[id].Games[gameId].ToDeleteRequestInformation();
}
