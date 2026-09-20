using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Commands;

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

    /// <summary>
    /// GET /api/audio/{id}/cover. Serves only the deliberately-set cover and
    /// 404s when there is none (routers/audio/covers.py:149-165); ArtworkAsync
    /// is the one that resolves folder and embedded art too.
    /// </summary>
    public async Task<Stream> CoverAsync(string id)
        => await _client.SendStreamAsync(
            _client.Api.Api.Audio[id].Cover.ToGetRequestInformation(),
            permissionHint: GmHint);

    /// <summary>
    /// DELETE /api/audio/{id}/cover. Clears the set cover, then recomputes
    /// has_artwork from folder and embedded art, so a track can still serve
    /// artwork afterwards (routers/audio/covers.py:128-146).
    /// </summary>
    public async Task<string> DeleteCoverAsync(string id)
        => await _client.SendAsync(
            _client.Api.Api.Audio[id].Cover.ToDeleteRequestInformation(),
            permissionHint: GmHint);

    /// <summary>POST /api/audio/{id}/cover. Multipart; the server checks the content type, then the size.</summary>
    public async Task<string> UploadCoverAsync(string id, string filePath)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyInputException($"Could not read {filePath}: {ex.Message}");
        }
        var info = _client.Api.Api.Audio[id].Cover.ToPostRequestInformation(BuildCoverUploadBody(bytes, filePath));
        return await _client.SendAsync(info, permissionHint: GmHint);
    }

    /// <summary>
    /// Internal so a test can pin the part name the server reads the file from.
    /// </summary>
    internal static Microsoft.Kiota.Abstractions.MultipartBody BuildCoverUploadBody(byte[] bytes, string filePath)
    {
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("file", MimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        return body;
    }

    /// <summary>
    /// The content type the server checks `file.content_type` against. Unknown
    /// extensions send octet-stream and let the server refuse — which types are
    /// acceptable is its policy, not ours. Internal so a test can pin the map.
    /// </summary>
    internal static string MimeForExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// POST /api/audio/{id}/cover/from-source. Same validator as the systems
    /// verb: map, token, book or audio, with campaign_file excluded by the
    /// route's own schema (routers/audio/_schemas.py:100-118), so sending it is
    /// a 422.
    /// </summary>
    // No notFoundHint: this route has two independent 404 sources — the track
    // lookup, and load_source_image's per-source messages ("Book not found",
    // "That book has no cover thumbnail", etc.) — and a hint would replace the
    // server's discriminating body with one that cannot tell them apart.
    public async Task<string> CoverFromSourceAsync(string id, string sourceType, string sourceId)
    {
        var body = new Generated.Models.AudioCoverSourceIn { SourceType = sourceType, SourceId = sourceId };
        var info = _client.Api.Api.Audio[id].Cover.FromSource.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: GmHint);
    }
}
