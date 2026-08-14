using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class AddonsService
{
    private readonly GrimoireApiClient _client;

    public AddonsService(GrimoireApiClient client) => _client = client;

    public async Task<AddonListResponse> ListAsync()
    {
        var info = _client.Api.Api.Addons.ToGetRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.AddonListResponse, permissionHint: "the admin role");
    }

    public async Task<RefreshResult> RefreshAsync()
    {
        var info = _client.Api.Api.Addons.Refresh.ToPostRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.RefreshResult, permissionHint: "the admin role");
    }
}
