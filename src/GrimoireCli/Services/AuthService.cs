using GrimoireCli.Api;

namespace GrimoireCli.Services;

public class AuthService
{
    private readonly GrimoireApiClient _client;

    public AuthService(GrimoireApiClient client) => _client = client;

    public async Task<string> MeAsync()
    {
        var info = _client.Api.Api.Auth.Me.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }
}
