using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>The caller's pinned folders and game systems (routers/pins).</summary>
public class PinsService
{
    private const string PinNotFound = "No such pin. List yours with: grimoire-cli pins list";

    private readonly GrimoireApiClient _client;

    public PinsService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync(string? collection) => await _client.SendAsync(ListRequest(collection));

    public async Task<string> AddAsync(string collection, string target) => await _client.SendAsync(AddRequest(collection, target));

    public async Task<string> RemoveAsync(string pinId) => await _client.SendAsync(RemoveRequest(pinId), notFoundHint: PinNotFound);

    /// <summary>GET /api/pins.</summary>
    internal RequestInformation ListRequest(string? collection)
        => _client.Api.Api.Pins.ToGetRequestInformation(c => c.QueryParameters.Collection = collection);

    /// <summary>POST /api/pins.</summary>
    internal RequestInformation AddRequest(string collection, string target)
        => _client.Api.Api.Pins.ToPostRequestInformation(new Generated.Models.PinIn { Collection = collection, Target = target });

    /// <summary>DELETE /api/pins/{pinId}. 204.</summary>
    internal RequestInformation RemoveRequest(string pinId)
        => _client.Api.Api.Pins[pinId].ToDeleteRequestInformation();
}
