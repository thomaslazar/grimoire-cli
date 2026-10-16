using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Member character sheets (routers/campaigns/uploads.py). Writes need the member or the owner, reads need membership; only sources carries a hint.</summary>
public class CampaignMemberSheetService
{
    private const string NotFound = "No campaign with that ID. List them with: grimoire-cli campaigns list";
    private readonly GrimoireApiClient _client;

    public CampaignMemberSheetService(GrimoireApiClient client) => _client = client;

    internal RequestInformation GetRequest(string id, string memberId)
        => _client.Api.Api.Campaigns[id].Members[memberId].Sheet.ToGetRequestInformation();

    // No notFoundHint: the 404 is the campaign, the member or an empty slot,
    // and only the server's detail tells them apart.
    public async Task<Stream> GetAsync(string id, string memberId)
        => await _client.SendStreamAsync(GetRequest(id, memberId));

    internal static string SheetMimeForExtension(string path)
        => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : SystemsService.MimeForExtension(path);

    internal RequestInformation UploadRequest(string id, string memberId, byte[] bytes, string filePath)
    {
        var body = new MultipartBody();
        body.AddOrReplacePart("file", SheetMimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        return _client.Api.Api.Campaigns[id].Members[memberId].Sheet.ToPostRequestInformation(body);
    }

    /// <summary>POST /api/campaigns/{id}/members/{member_id}/sheet. One multipart part named "file".</summary>
    public async Task<string> UploadAsync(string id, string memberId, string filePath)
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
        return await _client.SendAsync(UploadRequest(id, memberId, bytes, filePath));
    }

    internal RequestInformation DeleteRequest(string id, string memberId)
        => _client.Api.Api.Campaigns[id].Members[memberId].Sheet.ToDeleteRequestInformation();

    public async Task<string> DeleteAsync(string id, string memberId) => await _client.SendAsync(DeleteRequest(id, memberId));

    internal RequestInformation FromSourceRequest(string id, string memberId, string sourceType, string sourceId)
        => _client.Api.Api.Campaigns[id].Members[memberId].Sheet.Duplicate.ToPostRequestInformation(
            new Generated.Models.Body_duplicate_member_sheet_api_campaigns__campaign_id__members__member_id__sheet_duplicate_post { SourceType = sourceType, SourceId = sourceId });

    // No notFoundHint: campaign, member and source are independent 404 sources.
    public async Task<string> FromSourceAsync(string id, string memberId, string sourceType, string sourceId)
        => await _client.SendAsync(FromSourceRequest(id, memberId, sourceType, sourceId));

    internal RequestInformation SourcesRequest(string id) => _client.Api.Api.Campaigns[id].SheetSources.ToGetRequestInformation();

    public async Task<string> SourcesAsync(string id) => await _client.SendAsync(SourcesRequest(id), notFoundHint: NotFound);
}
