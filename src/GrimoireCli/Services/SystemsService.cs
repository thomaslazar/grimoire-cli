using System.Text.Json;
using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class SystemsService
{
    private readonly GrimoireApiClient _client;

    public SystemsService(GrimoireApiClient client) => _client = client;

    public async Task<List<GameSystemSummary>> ListAsync(
        string? sort, bool desc, string? genre, string? family,
        string? parentSystem, string? edition, string? license, bool? isExplicit)
    {
        var query = QueryBuilder.Build(
            ("sort", sort),
            ("order", desc ? "desc" : null),
            ("genre", genre),
            ("family", family),
            ("parent_system", parentSystem),
            ("edition", edition),
            ("license", license),
            ("explicit", isExplicit?.ToString().ToLowerInvariant()));
        var json = await _client.GetAsync(ApiEndpoints.Systems + query);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListGameSystemSummary) ?? new();
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
        var json = await _client.GetAsync(
            ApiEndpoints.System(id) + query,
            notFoundHint: "No system with that ID. List them with: grimoire-cli systems list");
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.GameSystemDetail)!;
    }
}
