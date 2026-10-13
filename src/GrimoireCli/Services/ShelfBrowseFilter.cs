namespace GrimoireCli.Services;

/// <summary>The query parameters a system's shelf routes share (<c>book_browse_params</c>).</summary>
public sealed record ShelfBrowseFilter(
    string? Query,
    string? Tags,
    bool? Favorites,
    string? AddedSince,
    bool? Explicit,
    string? Genre,
    string? ProductCode,
    string? Category,
    string? Folder,
    string? Sort,
    string? Order);
