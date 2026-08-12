using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class AuthService
{
    private readonly GrimoireApiClient _client;

    public AuthService(GrimoireApiClient client) => _client = client;

    public async Task<MeResponse> MeAsync()
    {
        var info = _client.Api.Api.Auth.Me.ToGetRequestInformation();
        return await _client.SendAsync(info, AppJsonContext.Default.MeResponse);
    }
}
