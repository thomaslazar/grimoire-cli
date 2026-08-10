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
        var query = QueryBuilder.Build(
            ("sort", sort),
            ("order", desc ? "desc" : null),
            ("genre", genre),
            ("family", family),
            ("parent_system", parentSystem),
            ("edition", edition),
            ("license", license),
            ("explicit", isExplicit?.ToString().ToLowerInvariant()),
            ("parent_id", parentId),
            ("include_children", includeChildren ? "true" : null));
        return await _client.GetAsync(ApiEndpoints.Systems + query, AppJsonContext.Default.ListGameSystemSummary);
    }

    public async Task<GameSystemDetail> GetAsync(
        string id, string? bookSort, bool bookDesc, string? genre, string? category, bool? isExplicit)
    {
        var query = QueryBuilder.Build(
            ("book_sort", bookSort),
            ("book_order", bookDesc ? "desc" : null),
            ("genre", genre),
            ("category", category),
            ("explicit", isExplicit?.ToString().ToLowerInvariant()));
        return await _client.GetAsync(
            ApiEndpoints.System(id) + query,
            AppJsonContext.Default.GameSystemDetail,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
    }
}
