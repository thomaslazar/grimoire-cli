using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// A league's shared books (routers/leagues/books.py). The path's resource id is the
/// league-resource id from list, not the library book's. Organiser checks are the
/// server's; no call carries a permission hint.
/// </summary>
public class LeagueBooksService
{
    private const string LeagueNotFound = "No league with that ID that you're in. List yours with: grimoire-cli leagues list";

    private const string AddNotFound = "No such league or book. List leagues with: grimoire-cli leagues list; find book IDs with: grimoire-cli leagues books search --id <league-id> --query <text>";

    private const string ResourceNotFound = "No such league, shared book, or library book. List them with: grimoire-cli leagues books list --id <league-id> (use its id, not book_id)";

    private readonly GrimoireApiClient _client;

    public LeagueBooksService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/leagues/{id}/books.</summary>
    public async Task<string> ListAsync(string id) => await _client.SendAsync(ListRequest(id), notFoundHint: LeagueNotFound);

    /// <summary>GET /api/leagues/{id}/books/search?q=.</summary>
    public async Task<string> SearchAsync(string id, string query) => await _client.SendAsync(SearchRequest(id, query), notFoundHint: LeagueNotFound);

    /// <summary>POST /api/leagues/{id}/books. 409 when already shared.</summary>
    public async Task<string> AddAsync(string id, string bookId, string? visibility, string[]? sharedUserIds)
        => await _client.SendAsync(AddRequest(id, bookId, visibility, sharedUserIds), notFoundHint: AddNotFound);

    /// <summary>PATCH /api/leagues/{id}/books/{resource_id}.</summary>
    public async Task<string> UpdateAsync(string id, string resourceId, string? visibility, string[]? sharedUserIds)
        => await _client.SendAsync(UpdateRequest(id, resourceId, visibility, sharedUserIds), notFoundHint: ResourceNotFound);

    /// <summary>DELETE /api/leagues/{id}/books/{resource_id}. 204.</summary>
    public async Task<string> RemoveAsync(string id, string resourceId) => await _client.SendAsync(RemoveRequest(id, resourceId), notFoundHint: ResourceNotFound);

    /// <summary>PUT /api/leagues/{id}/books/order. 204; unknown ids are skipped.</summary>
    public async Task<string> ReorderAsync(string id, string[] orderedIds) => await _client.SendAsync(ReorderRequest(id, orderedIds), notFoundHint: LeagueNotFound);

    internal RequestInformation ListRequest(string id) => _client.Api.Api.Leagues[id].Books.ToGetRequestInformation();

    internal RequestInformation SearchRequest(string id, string query)
        => _client.Api.Api.Leagues[id].Books.Search.ToGetRequestInformation(c => c.QueryParameters.Q = query);

    internal RequestInformation AddRequest(string id, string bookId, string? visibility, string[]? sharedUserIds)
        => _client.Api.Api.Leagues[id].Books.ToPostRequestInformation(BuildAddBody(bookId, visibility, sharedUserIds));

    internal RequestInformation UpdateRequest(string id, string resourceId, string? visibility, string[]? sharedUserIds)
        => _client.Api.Api.Leagues[id].Books[resourceId].ToPatchRequestInformation(BuildUpdateBody(visibility, sharedUserIds));

    internal RequestInformation RemoveRequest(string id, string resourceId) => _client.Api.Api.Leagues[id].Books[resourceId].ToDeleteRequestInformation();

    internal RequestInformation ReorderRequest(string id, string[] orderedIds)
        => _client.Api.Api.Leagues[id].Books.Order.ToPutRequestInformation(new Generated.Models.BookReorder { OrderedIds = [.. orderedIds] });

    // The generated model defaults visibility to organizer; clearing it sends no key.
    internal static Generated.Models.BookAdd BuildAddBody(string bookId, string? visibility, string[]? sharedUserIds)
    {
        var body = new Generated.Models.BookAdd
        {
            BookId = bookId,
            Visibility = visibility is null ? null : Enum.Parse<Generated.Models.BookAdd_visibility>(visibility, ignoreCase: true),
        };
        if (sharedUserIds is not null)
        {
            body.SharedUserIds = [.. sharedUserIds];
        }
        return body;
    }

    // Only the flags given reach the body, so an omitted one is left alone.
    internal static Generated.Models.Backend__routers__leagues___schemas__BookUpdate BuildUpdateBody(string? visibility, string[]? sharedUserIds)
    {
        var body = new Generated.Models.Backend__routers__leagues___schemas__BookUpdate();
        if (visibility is not null)
        {
            body.Visibility = new Generated.Models.Backend__routers__leagues___schemas__BookUpdate.Backend__routers__leagues___schemas__BookUpdate_visibility { String = visibility };
        }
        if (sharedUserIds is not null)
        {
            body.SharedUserIds = [.. sharedUserIds];
        }
        return body;
    }
}
