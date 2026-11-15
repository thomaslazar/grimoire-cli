using System.Text.Json;
using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>League membership (routers/leagues/__init__.py, core.py). Access is checked by the server; no call carries a permission hint.</summary>
public class LeagueMembersService
{
    private const string LeagueNotFound = "No league with that ID. List yours with: grimoire-cli leagues list";

    private const string UserNotFound = "No such league or user. Candidates are in: grimoire-cli leagues eligible --id <league-id>";
    private const string MemberNotFound = "No such league, or that user is not a member. Members are in: grimoire-cli leagues get --id <league-id>";

    private readonly GrimoireApiClient _client;

    public LeagueMembersService(GrimoireApiClient client) => _client = client;

    /// <summary>POST /api/leagues/{id}/members.</summary>
    public async Task<string> AddAsync(string id, string userId) => await _client.SendAsync(AddRequest(id, userId), notFoundHint: UserNotFound);

    /// <summary>DELETE /api/leagues/{id}/members/{user_id}. 204.</summary>
    public async Task<string> RemoveAsync(string id, string userId) => await _client.SendAsync(RemoveRequest(id, userId), notFoundHint: LeagueNotFound);

    /// <summary>PUT /api/leagues/{id}/members/{user_id}/army. A null army clears it.</summary>
    public async Task<string> ArmyAsync(string id, string userId, string? armyId)
        => await _client.SendAsync(ArmyRequest(id, userId, armyId), notFoundHint: MemberNotFound);

    internal RequestInformation AddRequest(string id, string userId)
        => _client.Api.Api.Leagues[id].Members.ToPostRequestInformation(new Generated.Models.MemberAdd { UserId = userId });

    internal RequestInformation RemoveRequest(string id, string userId) => _client.Api.Api.Leagues[id].Members[userId].ToDeleteRequestInformation();

    // The generated wrapper cannot serialise null, so the body is written out; the
    // key is always sent, and a null value clears the army.
    internal RequestInformation ArmyRequest(string id, string userId, string? armyId)
    {
        var info = _client.Api.Api.Leagues[id].Members[userId].Army.ToPutRequestInformation(new Generated.Models.MemberArmy());
        info.SetStreamContent(new MemoryStream(ArmyBody(armyId)), "application/json");
        return info;
    }

    private static byte[] ArmyBody(string? armyId)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("army_id", armyId);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
