using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class BooksService
{
    private readonly GrimoireApiClient _client;

    public BooksService(GrimoireApiClient client) => _client = client;

    public async Task<BookListResponse> ListAsync(string? systemId, string? category, int limit, int? offset)
    {
        var info = _client.Api.Api.Books.ToGetRequestInformation(c =>
        {
            c.QueryParameters.SystemId = systemId;
            c.QueryParameters.Category = category;
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info, AppJsonContext.Default.BookListResponse);
    }

    public async Task<BookDetail> GetAsync(string id)
    {
        var info = _client.Api.Api.Books[id].ToGetRequestInformation();
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.BookDetail,
            notFoundHint: "No book with that ID. List them with: grimoire-cli books list");
    }
}
