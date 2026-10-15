using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Campaign banner (routers/campaigns/uploads.py). Writes need ownership, get needs membership; no call carries a permission hint.</summary>
public class CampaignBannerService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";

    private readonly GrimoireApiClient _client;

    public CampaignBannerService(GrimoireApiClient client) => _client = client;

    internal RequestInformation GetRequest(string id, bool full)
        => _client.Api.Api.Campaigns[id].Banner.ToGetRequestInformation(c =>
            // Sent only when full: the server default is small.
            c.QueryParameters.Size = full ? "full" : null);

    // No notFoundHint: the 404 is either the campaign or "No banner", and only
    // the server's detail tells them apart.
    public async Task<Stream> GetAsync(string id, bool full)
        => await _client.SendStreamAsync(GetRequest(id, full));

    internal RequestInformation UploadRequest(string id, byte[] bytes, string filePath)
    {
        var body = new MultipartBody();
        body.AddOrReplacePart("file", SystemsService.MimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        return _client.Api.Api.Campaigns[id].Banner.ToPostRequestInformation(body);
    }

    /// <summary>POST /api/campaigns/{id}/banner. One multipart part named "file".</summary>
    public async Task<string> UploadAsync(string id, string filePath)
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
        return await _client.SendAsync(UploadRequest(id, bytes, filePath), notFoundHint: NotFound);
    }

    internal RequestInformation DeleteRequest(string id) => _client.Api.Api.Campaigns[id].Banner.ToDeleteRequestInformation();

    public async Task<string> DeleteAsync(string id) => await _client.SendAsync(DeleteRequest(id), notFoundHint: NotFound);

    internal RequestInformation FromSourceRequest(string id, string sourceType, string sourceId)
        => _client.Api.Api.Campaigns[id].Banner.FromSource.ToPostRequestInformation(
            new Generated.Models.BannerSourceIn { SourceType = sourceType, SourceId = sourceId });

    // No notFoundHint: several independent 404 sources (campaign, source
    // item, source has no image); the server's body tells them apart.
    public async Task<string> FromSourceAsync(string id, string sourceType, string sourceId)
        => await _client.SendAsync(FromSourceRequest(id, sourceType, sourceId));

    internal RequestInformation FocusRequest(string id, int focusY)
        => _client.Api.Api.Campaigns[id].Banner.Focus.ToPutRequestInformation(new Generated.Models.BannerFocusIn { FocusY = focusY });

    public async Task<string> FocusAsync(string id, int focusY)
        => await _client.SendAsync(FocusRequest(id, focusY), notFoundHint: NotFound);
}
