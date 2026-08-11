using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class SystemsService
{
    private readonly GrimoireApiClient _client;

    public SystemsService(GrimoireApiClient client) => _client = client;

    public async Task<List<GameSystemSummary>> ListAsync(
        string? sort, bool desc, string? genre, string? family,
        string? parentSystem, string? edition, string? license, bool? isExplicit,
        string? parentId, bool includeChildren)
    {
        var info = _client.Api.Api.Systems.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Sort = sort;
            c.QueryParameters.Order = desc ? "desc" : null;
            c.QueryParameters.Genre = genre;
            c.QueryParameters.Family = family;
            c.QueryParameters.ParentSystem = parentSystem;
            c.QueryParameters.Edition = edition;
            c.QueryParameters.License = license;
            c.QueryParameters.Explicit = isExplicit;
            c.QueryParameters.ParentId = parentId;
            // Sent only when true: the server default is false, so an omitted
            // parameter and an explicit false mean the same thing.
            c.QueryParameters.IncludeChildren = includeChildren ? true : null;
        });
        return await _client.SendAsync(info, AppJsonContext.Default.ListGameSystemSummary);
    }

    public async Task<GameSystemDetail> GetAsync(
        string id, string? bookSort, bool bookDesc, string? genre, string? category, bool? isExplicit)
    {
        var info = _client.Api.Api.Systems[id].ToGetRequestInformation(c =>
        {
            c.QueryParameters.BookSort = bookSort;
            c.QueryParameters.BookOrder = bookDesc ? "desc" : null;
            c.QueryParameters.Genre = genre;
            c.QueryParameters.Category = category;
            c.QueryParameters.Explicit = isExplicit;
        });
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.GameSystemDetail,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }

    /// <summary>
    /// PATCH /api/systems/{id}. The generated builder is used for the URL, method and
    /// path parameter only; its request model would transmit unknown keys
    /// (IAdditionalDataHolder), so the validated raw body replaces the content and
    /// reaches the server byte-for-byte. Returns the raw response — {"status":"ok"},
    /// which confirms nothing about what changed.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Systems[id].ToPatchRequestInformation(
            new Generated.Models.GameSystemUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(
            info,
            permissionHint: "the gm or admin role",
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }
}
