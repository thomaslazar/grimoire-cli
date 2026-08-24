using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The three add-on metadata endpoints, which systems and books share.  Upstream
/// serves both from one implementation (routers/_metadata_lookup.py); the two
/// differ here only in the generated builder each path reaches for, so the
/// resource is a constructor argument rather than a second class.
///
/// All three are reads.  Applying a fetched value is the caller's own
/// systems update / books update.
/// </summary>
public class MetadataService
{
    private const string Systems = "systems";
    private const string Books = "books";

    private readonly GrimoireApiClient _client;
    private readonly string _resource;

    public MetadataService(GrimoireApiClient client, string resource)
    {
        if (resource is not (Systems or Books))
            throw new ArgumentException($"Unsupported metadata resource '{resource}'.", nameof(resource));
        _client = client;
        _resource = resource;
    }

    private string NotFoundHint => _resource == Systems
        ? "No system with that ID. List them with: grimoire-cli systems list"
        : "No book with that ID. List them with: grimoire-cli books list";

    public async Task<string> SourcesAsync(string id)
    {
        var info = _resource == Systems
            ? _client.Api.Api.Systems[id].MetadataSources.ToGetRequestInformation()
            : _client.Api.Api.Books[id].MetadataSources.ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: NotFoundHint);
    }

    public async Task<string> SearchAsync(string id, string sourceId, string? query)
    {
        var info = _resource == Systems
            ? _client.Api.Api.Systems[id].MetadataSearch.ToPostRequestInformation(
                new Generated.Models.Backend__routers__systems___schemas__MetadataSearch
                {
                    SourceId = sourceId,
                    Query = query,
                })
            : _client.Api.Api.Books[id].MetadataSearch.ToPostRequestInformation(
                new Generated.Models.Backend__routers__books___schemas__MetadataSearch
                {
                    SourceId = sourceId,
                    Query = query,
                });
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: NotFoundHint);
    }

    public async Task<string> FetchAsync(
        string id, string sourceId, string? identity, string? query, string? paste)
    {
        var body = BuildFetchBody(sourceId, identity, query, paste);
        var info = _resource == Systems
            ? _client.Api.Api.Systems[id].MetadataFetch.ToPostRequestInformation(body)
            : _client.Api.Api.Books[id].MetadataFetch.ToPostRequestInformation(body);
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// The generated model sets none of its properties in its constructor, so an
    /// omitted flag stays absent from the body and the server applies its own
    /// default. Internal (not private) so a test can pin that a client
    /// regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.MetadataFetch BuildFetchBody(
        string sourceId, string? identity, string? query, string? paste)
        => new()
        {
            SourceId = sourceId,
            Identity = identity,
            Query = query,
            Paste = paste,
        };
}
