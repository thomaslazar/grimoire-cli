using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The nine endpoints behind `audio` and `audio folders`. The folder verbs live
/// here rather than in their own service because they are one collection's
/// endpoints, the way ModelsService carries `models folders`.
/// </summary>
public class AudioService
{
    private const string GmHint = "the gm or admin role";
    private const string NotFoundHint = "No audio track with that ID. List them with: grimoire-cli audio list";

    private readonly GrimoireApiClient _client;

    public AudioService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/audio. Variants are excluded server-side; only family mains are listed.</summary>
    public async Task<string> ListAsync(int? limit, int? offset)
    {
        var info = _client.Api.Api.Audio.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
        });
        return await _client.SendAsync(info);
    }

    /// <summary>GET /api/audio/{id}. Adds folder tags and the variant family to the list row.</summary>
    public async Task<string> GetAsync(string id)
    {
        var info = _client.Api.Api.Audio[id].ToGetRequestInformation();
        return await _client.SendAsync(info, notFoundHint: NotFoundHint);
    }

    /// <summary>
    /// GET /api/audio/{id}/artwork. Resolves a deliberately-set cover first, then
    /// folder art, then embedded album art; 404 when the track has none of them.
    /// </summary>
    public async Task<Stream> ArtworkAsync(string id)
    {
        var info = _client.Api.Api.Audio[id].Artwork.ToGetRequestInformation();
        return await _client.SendStreamAsync(info);
    }

    /// <summary>GET /api/audio/{id}/file. Streams the audio file.</summary>
    public async Task<Stream> FileAsync(string id)
        => await _client.SendStreamAsync(_client.Api.Api.Audio[id].File.ToGetRequestInformation());

    /// <summary>
    /// PATCH /api/audio/{id}. The generated builder supplies the URL, method and
    /// path parameter only; the validated raw body replaces the content so it
    /// reaches the server byte-for-byte.
    /// </summary>
    public async Task<string> UpdateAsync(string id, string rawBody)
    {
        var info = _client.Api.Api.Audio[id].ToPatchRequestInformation(new Generated.Models.AudioUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint, notFoundHint: NotFoundHint);
    }

    /// <summary>POST /api/audio/bulk. One transaction; only an unresolved id reaches errors.</summary>
    public async Task<string> BatchUpdateAsync(string rawBody)
    {
        var info = _client.Api.Api.Audio.Bulk.ToPostRequestInformation(new Generated.Models.AudioBulkUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/audio/bulk/tags. Additive: it never removes a tag.</summary>
    public async Task<string> BatchTagAsync(string rawBody)
    {
        var info = _client.Api.Api.Audio.Bulk.Tags.ToPostRequestInformation(new Generated.Models.BulkAddTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>GET /api/audio-folders. Resolves stored keys to display casing.</summary>
    public async Task<string> FoldersListAsync()
    {
        var info = _client.Api.Api.AudioFolders.ToGetRequestInformation();
        return await _client.SendAsync(info);
    }

    /// <summary>PATCH /api/audio-folders. Keyed by the path in the body; echoes internal keys.</summary>
    public async Task<string> FoldersSetAsync(string rawBody)
    {
        var info = _client.Api.Api.AudioFolders.ToPatchRequestInformation(new Generated.Models.FolderTagsUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>POST /api/audio-folders/bulk. One transaction; no per-item error list.</summary>
    public async Task<string> FoldersBatchSetAsync(string rawBody)
    {
        var info = _client.Api.Api.AudioFolders.Bulk.ToPostRequestInformation(new Generated.Models.BulkFolderTags());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(rawBody)), "application/json");
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
