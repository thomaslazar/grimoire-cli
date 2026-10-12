using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The three routes behind `systems books`: a system's shelf, browsed a page at
/// a time. Each 404s when the system is missing or hidden from the caller.
/// </summary>
public class SystemBooksService
{
    private const string NotFoundHint = "No system with that ID. List them with: grimoire-cli systems list";

    private readonly GrimoireApiClient _client;

    public SystemBooksService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/systems/{id}/books. A page of the shelf, full book records.</summary>
    public async Task<string> ListAsync(string systemId, int? limit, int? offset, ShelfBrowseFilter filter)
        => await _client.SendAsync(ListRequest(systemId, limit, offset, filter), notFoundHint: NotFoundHint);

    /// <summary>GET /api/systems/{id}/book-groups. Each directory holding matching books, with its direct count.</summary>
    public async Task<string> FolderCountsAsync(string systemId, ShelfBrowseFilter filter)
        => await _client.SendAsync(FolderCountsRequest(systemId, filter), notFoundHint: NotFoundHint);

    /// <summary>GET /api/systems/{id}/book-facets. The filter values the whole visible shelf offers.</summary>
    public async Task<string> FacetsAsync(string systemId)
        => await _client.SendAsync(FacetsRequest(systemId), notFoundHint: NotFoundHint);

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation ListRequest(string systemId, int? limit, int? offset, ShelfBrowseFilter filter)
        => _client.Api.Api.Systems[systemId].Books.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
            c.QueryParameters.Q = filter.Query;
            c.QueryParameters.Tags = filter.Tags;
            c.QueryParameters.Favorites = filter.Favorites;
            c.QueryParameters.AddedSince = filter.AddedSince;
            c.QueryParameters.Explicit = filter.Explicit;
            c.QueryParameters.Genre = filter.Genre;
            c.QueryParameters.ProductCode = filter.ProductCode;
            c.QueryParameters.Category = filter.Category;
            c.QueryParameters.Folder = filter.Folder;
            c.QueryParameters.Sort = filter.Sort;
            if (filter.Order is not null)
                c.QueryParameters.OrderAsGetOrderQueryParameterType =
                    Enum.Parse<Generated.Api.Systems.Item.Books.GetOrderQueryParameterType>(filter.Order, true);
        });

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation FolderCountsRequest(string systemId, ShelfBrowseFilter filter)
        => _client.Api.Api.Systems[systemId].BookGroups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Q = filter.Query;
            c.QueryParameters.Tags = filter.Tags;
            c.QueryParameters.Favorites = filter.Favorites;
            c.QueryParameters.AddedSince = filter.AddedSince;
            c.QueryParameters.Explicit = filter.Explicit;
            c.QueryParameters.Genre = filter.Genre;
            c.QueryParameters.ProductCode = filter.ProductCode;
            c.QueryParameters.Category = filter.Category;
            c.QueryParameters.Folder = filter.Folder;
            c.QueryParameters.Sort = filter.Sort;
            if (filter.Order is not null)
                c.QueryParameters.OrderAsGetOrderQueryParameterType =
                    Enum.Parse<Generated.Api.Systems.Item.BookGroups.GetOrderQueryParameterType>(filter.Order, true);
        });

    /// <summary>Internal (not private) so a test can pin it to its own builder's path.</summary>
    internal RequestInformation FacetsRequest(string systemId)
        => _client.Api.Api.Systems[systemId].BookFacets.ToGetRequestInformation();
}
