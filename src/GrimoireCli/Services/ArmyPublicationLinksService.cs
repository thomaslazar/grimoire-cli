using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>The routes behind `armies publication-links`. All three are gm-or-admin only.</summary>
public class ArmyPublicationLinksService
{
    private const string Hint = "the gm or admin role";

    private readonly GrimoireApiClient _client;

    public ArmyPublicationLinksService(GrimoireApiClient client) => _client = client;

    /// <summary>PUT /api/armies/publication-links.</summary>
    public async Task<string> SetAsync(string publication, string bookId, string? systemName, int pageOffset)
        => await _client.SendAsync(SetRequest(publication, bookId, systemName, pageOffset), permissionHint: Hint);

    /// <summary>DELETE /api/armies/publication-links.</summary>
    public async Task<string> RemoveAsync(string publication, string? systemName)
        => await _client.SendAsync(RemoveRequest(publication, systemName), permissionHint: Hint);

    /// <summary>GET /api/armies/publication-links/books.</summary>
    public async Task<string> BooksAsync(string query)
        => await _client.SendAsync(BooksRequest(query), permissionHint: Hint);

    internal RequestInformation SetRequest(string publication, string bookId, string? systemName, int pageOffset)
        => _client.Api.Api.Armies.PublicationLinks.ToPutRequestInformation(BuildSetBody(publication, bookId, systemName, pageOffset));

    internal static Generated.Models.PublicationLinkIn BuildSetBody(string publication, string bookId, string? systemName, int pageOffset)
        => new() { Publication = publication, BookId = bookId, SystemName = systemName ?? "", PageOffset = pageOffset };

    internal RequestInformation RemoveRequest(string publication, string? systemName)
        => _client.Api.Api.Armies.PublicationLinks.ToDeleteRequestInformation(c =>
        {
            c.QueryParameters.Publication = publication;
            // Sent only when set: the server defaults to "".
            if (systemName is not null)
                c.QueryParameters.SystemName = systemName;
        });

    internal RequestInformation BooksRequest(string query)
        => _client.Api.Api.Armies.PublicationLinks.Books.ToGetRequestInformation(c => c.QueryParameters.Q = query);
}
