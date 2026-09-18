using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The `system-families` vocabulary: list, create and delete. The read is a
/// parameterless GET guarded only by get_current_user (routers/lookups/core.py),
/// so it names no permissionHint and no notFoundHint; create and delete are
/// admin-only and carry an id, so they name both.
/// </summary>
public class SystemFamiliesService
{
    private readonly GrimoireApiClient _client;

    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No system family with that ID. List them with: grimoire-cli system-families list";

    public SystemFamiliesService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/system-families.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>
    /// Internal (not private) so a test can pin this vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation ListRequest() => _client.Api.Api.SystemFamilies.ToGetRequestInformation();

    /// <summary>POST /api/system-families. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name)
        => await _client.SendAsync(CreateRequest(name), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name)
        => _client.Api.Api.SystemFamilies.ToPostRequestInformation(
            new Generated.Models.SystemFamilyCreate { Name = name });

    /// <summary>DELETE /api/system-families/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.SystemFamilies[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
}
