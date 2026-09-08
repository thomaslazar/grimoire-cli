using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The two search reads. Both are guarded by require_not_guest
/// (routers/search/__init__.py), so neither send names a permissionHint, and
/// neither path carries an id, so neither names a notFoundHint.
/// </summary>
public class SearchService
{
    private readonly GrimoireApiClient _client;

    public SearchService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// GET /api/search. limit bounds the page-text results only; the book,
    /// map, token and audio result sets are capped at 50 each server-side.
    /// </summary>
    public async Task<string> SearchAsync(string query, int? limit, string? bookId, string? systemId)
    {
        var info = _client.Api.Api.Search.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Q = query;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.BookId = bookId;
            c.QueryParameters.SystemId = systemId;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/search/fields.</summary>
    public async Task<string> FieldsAsync()
    {
        var info = _client.Api.Api.Search.Fields.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }
}
