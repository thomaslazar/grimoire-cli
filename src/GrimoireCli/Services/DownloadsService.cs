using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>
/// The archive download. Guarded by get_current_user
/// (routers/downloads/core.py), so the send names no permissionHint — the
/// library_folder scope's admin check happens inside the handler and surfaces
/// as a 403 with the server's own message. Its 404 ("No files found for the
/// requested scope") is informative, so there is no notFoundHint either.
/// </summary>
public class DownloadsService
{
    private readonly GrimoireApiClient _client;

    public DownloadsService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// GET /api/downloads/archive. Which scope flags are required depends on
    /// type; the server validates the combination and answers 400 naming the
    /// missing one.
    /// </summary>
    public async Task<Stream> ArchiveAsync(
        string type, string? fmt, string? id, string? category, string? tag, string? resourceType, string? folder)
        => await _client.SendStreamAsync(
            ArchiveRequest(type, fmt, id, category, tag, resourceType, folder));

    /// <summary>
    /// Internal so a test can pin all seven query-parameter wire names. A
    /// renamed scope parameter would be dropped by the server and silently
    /// widen the exported slice, which no response shape would reveal.
    /// </summary>
    internal RequestInformation ArchiveRequest(
        string type, string? fmt, string? id, string? category, string? tag, string? resourceType, string? folder)
        => _client.Api.Api.Downloads.Archive.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Type = type;
            c.QueryParameters.Fmt = fmt;
            c.QueryParameters.Id = id;
            c.QueryParameters.Category = category;
            c.QueryParameters.Tag = tag;
            c.QueryParameters.ResourceType = resourceType;
            c.QueryParameters.Folder = folder;
        });
}
