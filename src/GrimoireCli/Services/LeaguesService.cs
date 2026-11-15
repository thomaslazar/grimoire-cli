using System.Text;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The routes behind `leagues` (routers/leagues/). Each request is built by an
/// internal method so a test can pin its route and method. Only create is
/// role-gated; organiser and member checks are made inside the handlers.
/// </summary>
public class LeaguesService
{
    private const string LeagueNotFound = "No league with that ID that you're in. List yours with: grimoire-cli leagues list";

    private readonly GrimoireApiClient _client;

    public LeaguesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/leagues.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>GET /api/leagues/{id}.</summary>
    public async Task<string> GetAsync(string id) => await _client.SendAsync(GetRequest(id), notFoundHint: LeagueNotFound);

    /// <summary>POST /api/leagues. The validated raw body is sent as-is.</summary>
    public async Task<string> CreateAsync(string rawBody) => await _client.SendAsync(CreateRequest(rawBody), permissionHint: "the gm or admin role");

    /// <summary>PATCH /api/leagues/{id}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
        => await _client.SendAsync(UpdateRequest(id, rawBody), notFoundHint: LeagueNotFound);

    /// <summary>DELETE /api/leagues/{id}.</summary>
    public async Task<string> DeleteAsync(string id) => await _client.SendAsync(DeleteRequest(id), notFoundHint: LeagueNotFound);

    /// <summary>GET /api/leagues/{id}/eligible-members.</summary>
    public async Task<string> EligibleAsync(string id) => await _client.SendAsync(EligibleRequest(id), notFoundHint: LeagueNotFound);

    internal RequestInformation ListRequest() => _client.Api.Api.Leagues.ToGetRequestInformation();

    internal RequestInformation GetRequest(string id) => _client.Api.Api.Leagues[id].ToGetRequestInformation();

    internal RequestInformation CreateRequest(string rawBody)
    {
        var info = _client.Api.Api.Leagues.ToPostRequestInformation(new Generated.Models.LeagueCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation UpdateRequest(string id, string rawBody)
    {
        var info = _client.Api.Api.Leagues[id].ToPatchRequestInformation(new Generated.Models.LeagueUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return info;
    }

    internal RequestInformation DeleteRequest(string id) => _client.Api.Api.Leagues[id].ToDeleteRequestInformation();

    internal RequestInformation EligibleRequest(string id) => _client.Api.Api.Leagues[id].EligibleMembers.ToGetRequestInformation();
}
