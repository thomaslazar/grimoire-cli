using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The `parent-systems` vocabulary. Its read is a parameterless GET guarded only by
/// get_current_user (routers/lookups/core.py), so the send names no
/// permissionHint, and the path carries no id, so it names no notFoundHint.
/// </summary>
public class ParentSystemsService
{
    private readonly GrimoireApiClient _client;

    public ParentSystemsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/parent-systems.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>
    /// Internal (not private) so a test can pin this vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation ListRequest() => _client.Api.Api.ParentSystems.ToGetRequestInformation();
}
