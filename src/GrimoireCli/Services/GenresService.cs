using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The `genres` vocabulary: list, create and delete. The read is a
/// parameterless GET guarded only by get_current_user (routers/lookups/core.py),
/// so it names no permissionHint and no notFoundHint; create and delete are
/// admin-only and carry an id, so they name both.
/// </summary>
public class GenresService
{
    private readonly GrimoireApiClient _client;

    private const string AdminHint = "the admin role";

    private const string NotFoundHint =
        "No genre with that ID. List them with: grimoire-cli genres list";

    public GenresService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/genres.</summary>
    public async Task<string> ListAsync() => await _client.SendAsync(ListRequest());

    /// <summary>
    /// Internal (not private) so a test can pin this vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation ListRequest() => _client.Api.Api.Genres.ToGetRequestInformation();

    /// <summary>
    /// POST /api/genres. 409s on a case-insensitive duplicate name. Its only
    /// 404 names an unknown parent, so the hint can say so.
    /// </summary>
    public async Task<string> CreateAsync(string name, string? parentId)
        => await _client.SendAsync(
            CreateRequest(name, parentId),
            permissionHint: AdminHint,
            notFoundHint: "No genre with that --parent-id. List them with: grimoire-cli genres list");

    internal RequestInformation CreateRequest(string name, string? parentId)
        => _client.Api.Api.Genres.ToPostRequestInformation(BuildCreateBody(name, parentId));

    /// <summary>
    /// ParentId is a composed-type wrapper whose constructor sets nothing, so
    /// assigning through it only when the flag was given leaves an omitted one
    /// absent from the body — which is what makes a top-level genre. Internal so
    /// a test can pin that a client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.GenreCreate BuildCreateBody(string name, string? parentId)
    {
        var body = new Generated.Models.GenreCreate { Name = name };
        if (parentId is not null)
            body.ParentId = new Generated.Models.GenreCreate.GenreCreate_parent_id { String = parentId };
        return body;
    }

    /// <summary>
    /// DELETE /api/genres/{id}. 409s while the name is in use unless force; a
    /// forced delete removes the row only and cascades to child genres.
    /// </summary>
    public async Task<string> DeleteAsync(string id, bool force)
        => await _client.SendAsync(DeleteRequest(id, force), permissionHint: AdminHint, notFoundHint: NotFoundHint);

    internal RequestInformation DeleteRequest(string id, bool force)
        => _client.Api.Api.Genres[id].ToDeleteRequestInformation(c => c.QueryParameters.Force = force);
}
