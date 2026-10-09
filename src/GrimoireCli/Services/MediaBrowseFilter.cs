namespace GrimoireCli.Services;

/// <summary>The query parameters every media list shares (<c>routers/_browse.py</c>).</summary>
public sealed record MediaBrowseFilter(
    string? Query,
    string? Tags,
    bool? Favorites,
    string? AddedSince,
    string? Folder,
    string? Sort,
    string? Order);
