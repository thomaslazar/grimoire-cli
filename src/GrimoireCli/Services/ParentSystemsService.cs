using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The `parent-systems` vocabulary: list, create and delete. The read is a
/// parameterless GET guarded only by get_current_user (routers/lookups/core.py),
/// so it names no permissionHint and no notFoundHint; create and delete are
/// admin-only and carry an id, so they name both.
/// </summary>
public class ParentSystemsService
{
    private readonly GrimoireApiClient _client;

    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No parent system with that ID. List them with: grimoire-cli parent-systems list";

    public ParentSystemsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/parent-systems.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>
    /// Internal (not private) so a test can pin this vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation ListRequest() => _client.Api.Api.ParentSystems.ToGetRequestInformation();

    /// <summary>POST /api/parent-systems. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name)
        => await _client.SendAsync(CreateRequest(name), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name)
        => _client.Api.Api.ParentSystems.ToPostRequestInformation(
            new Generated.Models.ParentSystemCreate { Name = name });

    /// <summary>DELETE /api/parent-systems/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.ParentSystems[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
}
