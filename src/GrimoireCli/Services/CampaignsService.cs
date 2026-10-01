using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// Campaign records. Writes are gated by ownership, not role
/// (routers/campaigns/_helpers.py:304-317), so no call carries a permission hint.
/// </summary>
public class CampaignsService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/campaigns. Owned and joined; archived only when asked.</summary>
    public async Task<string> ListAsync(bool includeArchived)
    {
        var info = _client.Api.Api.Campaigns.ToGetRequestInformation(c =>
            // Sent only when true: the server default is false.
            c.QueryParameters.IncludeArchived = includeArchived ? true : null);
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/campaigns/{id}.</summary>
    public async Task<string> GetAsync(string id)
        => await _client.SendAsync(_client.Api.Api.Campaigns[id].ToGetRequestInformation(), notFoundHint: NotFound);

    /// <summary>
    /// POST /api/campaigns. The validated raw body replaces the generated model's
    /// content so it reaches the server byte-for-byte.
    /// </summary>
    public async Task<string> CreateAsync(string rawBody)
    {
        var info = _client.Api.Api.Campaigns.ToPostRequestInformation(new Generated.Models.CampaignCreate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/campaigns/{id}. Raw body, as <see cref="CreateAsync"/>.</summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Campaigns[id].ToPatchRequestInformation(new Generated.Models.CampaignUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, notFoundHint: NotFound);
    }
}
