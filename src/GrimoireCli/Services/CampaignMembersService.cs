using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Campaign membership (routers/campaigns/members.py). Access is checked by the server; no call carries a permission hint.</summary>
public class CampaignMembersService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignMembersService(GrimoireApiClient client) => _client = client;

    internal RequestInformation EligibleRequest(string id) => _client.Api.Api.Campaigns[id].EligibleMembers.ToGetRequestInformation();

    internal RequestInformation InviteRequest(string id, string userId)
        => _client.Api.Api.Campaigns[id].Invite.ToPostRequestInformation(new Generated.Models.InvitePayload { UserId = userId });

    // Each field is set only when given, so an absent flag sends no key; "" is
    // sent as-is because it clears the value.
    internal RequestInformation UpdateRequest(string id, string userId, string? status, string? characterName, string? characterSheetUrl)
    {
        var body = new Generated.Models.MemberStatusUpdate();
        if (status is not null)
        {
            body.Status = new Generated.Models.MemberStatusUpdate.MemberStatusUpdate_status { String = status };
        }
        if (characterName is not null)
        {
            body.CharacterName = new Generated.Models.MemberStatusUpdate.MemberStatusUpdate_character_name { String = characterName };
        }
        if (characterSheetUrl is not null)
        {
            body.CharacterSheetUrl = new Generated.Models.MemberStatusUpdate.MemberStatusUpdate_character_sheet_url { String = characterSheetUrl };
        }
        return _client.Api.Api.Campaigns[id].Members[userId].ToPatchRequestInformation(body);
    }

    internal RequestInformation RemoveRequest(string id, string userId) => _client.Api.Api.Campaigns[id].Members[userId].ToDeleteRequestInformation();

    /// <summary>GET /api/campaigns/{id}/eligible-members.</summary>
    public async Task<string> EligibleAsync(string id) => await _client.SendAsync(EligibleRequest(id), notFoundHint: NotFound);

    /// <summary>POST /api/campaigns/{id}/invite.</summary>
    public async Task<string> InviteAsync(string id, string userId) => await _client.SendAsync(InviteRequest(id, userId), notFoundHint: NotFound);

    /// <summary>PATCH /api/campaigns/{id}/members/{user_id}.</summary>
    public async Task<string> UpdateAsync(string id, string userId, string? status, string? characterName, string? characterSheetUrl)
        => await _client.SendAsync(UpdateRequest(id, userId, status, characterName, characterSheetUrl));

    /// <summary>DELETE /api/campaigns/{id}/members/{user_id}. 204.</summary>
    public async Task<string> RemoveAsync(string id, string userId) => await _client.SendAsync(RemoveRequest(id, userId));
}
