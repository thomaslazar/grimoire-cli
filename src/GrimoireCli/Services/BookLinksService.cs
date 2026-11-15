using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>A book's links onto other systems' shelves. Every call answers with the book's link list.</summary>
public class BookLinksService
{
    private const string WriteHint = "the gm or admin role";
    private const string NotFoundHint = "No book with that ID. List them with: grimoire-cli books list";

    private readonly GrimoireApiClient _client;

    public BookLinksService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync(string id)
        => await _client.SendAsync(ListRequest(id), notFoundHint: NotFoundHint);

    public async Task<string> AddAsync(string id, string systemId)
        => await _client.SendAsync(AddRequest(id, systemId), permissionHint: WriteHint);

    public async Task<string> RemoveAsync(string id, string systemId)
        => await _client.SendAsync(RemoveRequest(id, systemId), permissionHint: WriteHint);

    internal RequestInformation ListRequest(string id)
        => _client.Api.Api.Books[id].Systems.ToGetRequestInformation();

    internal RequestInformation AddRequest(string id, string systemId)
        => _client.Api.Api.Books[id].Systems.ToPostRequestInformation(
            new Generated.Models.BookSystemLinkCreate { SystemId = systemId });

    internal RequestInformation RemoveRequest(string id, string systemId)
        => _client.Api.Api.Books[id].Systems[systemId].ToDeleteRequestInformation();
}
