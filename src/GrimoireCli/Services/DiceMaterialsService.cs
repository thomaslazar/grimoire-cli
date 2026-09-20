using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The `dice-materials` vocabulary: list, create and delete. The read is a
/// parameterless GET guarded only by get_current_user (routers/lookups/core.py),
/// so it names no hints. Create is require_admin and takes no id, so it names
/// only a permissionHint; delete is require_admin and carries the id, so it
/// names a notFoundHint too.
/// </summary>
public class DiceMaterialsService
{
    private readonly GrimoireApiClient _client;

    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No dice/material with that ID. List them with: grimoire-cli dice-materials list";

    public DiceMaterialsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/dice-materials.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>
    /// Internal (not private) so a test can pin this vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation ListRequest() => _client.Api.Api.DiceMaterials.ToGetRequestInformation();

    /// <summary>POST /api/dice-materials. 409s on a case-insensitive duplicate name.</summary>
    public async Task<string> CreateAsync(string name, string? group)
        => await _client.SendAsync(CreateRequest(name, group), permissionHint: AdminHint);

    internal RequestInformation CreateRequest(string name, string? group)
        => _client.Api.Api.DiceMaterials.ToPostRequestInformation(BuildCreateBody(name, group));

    /// <summary>
    /// Group is a composed-type wrapper whose constructor sets nothing, so
    /// assigning through it only when the flag was given leaves an omitted one
    /// absent from the body and lets the server apply its own "Custom" default.
    /// Internal so a test can pin that a client regeneration cannot change it.
    /// </summary>
    internal static Generated.Models.DiceMaterialCreate BuildCreateBody(string name, string? group)
    {
        var body = new Generated.Models.DiceMaterialCreate { Name = name };
        if (group is not null)
            body.Group = new Generated.Models.DiceMaterialCreate.DiceMaterialCreate_group { String = group };
        return body;
    }

    /// <summary>DELETE /api/dice-materials/{id}. 409s while the name is in use unless force.</summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.DiceMaterials[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
}
