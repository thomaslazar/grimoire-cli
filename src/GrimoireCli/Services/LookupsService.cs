using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The five controlled-vocabulary reads. Every one is a parameterless GET guarded
/// only by get_current_user (routers/lookups/core.py), so there is no
/// permissionHint to name, and no id appears in any path, so there is no
/// notFoundHint either.
/// </summary>
public class LookupsService
{
    private readonly GrimoireApiClient _client;

    public LookupsService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync(string vocabulary)
        => await _client.SendAsync(RequestFor(vocabulary));

    /// <summary>
    /// Internal (not private) so a test can pin each vocabulary to the path its
    /// generated builder produces, which is what a client regeneration could
    /// silently move.
    /// </summary>
    internal RequestInformation RequestFor(string vocabulary) => vocabulary switch
    {
        "genres" => _client.Api.Api.Genres.ToGetRequestInformation(),
        "licenses" => _client.Api.Api.Licenses.ToGetRequestInformation(),
        "parent-systems" => _client.Api.Api.ParentSystems.ToGetRequestInformation(),
        "system-families" => _client.Api.Api.SystemFamilies.ToGetRequestInformation(),
        "dice-materials" => _client.Api.Api.DiceMaterials.ToGetRequestInformation(),
        _ => throw new ArgumentException($"Unknown vocabulary '{vocabulary}'.", nameof(vocabulary)),
    };
}
