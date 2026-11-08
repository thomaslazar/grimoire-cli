using GrimoireCli.Api;
using GrimoireCli.Commands;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Member character art (routers/campaigns/uploads.py). Writes need the member or the owner, get needs membership; no call carries a permission hint.</summary>
public class CampaignMemberArtService
{
    private readonly GrimoireApiClient _client;

    public CampaignMemberArtService(GrimoireApiClient client) => _client = client;

    internal RequestInformation GetRequest(string id, string memberId)
        => _client.Api.Api.Campaigns[id].Members[memberId].Art.ToGetRequestInformation();

    // No notFoundHint: the 404 is the campaign, the member or an empty slot,
    // and only the server's detail tells them apart.
    public async Task<Stream> GetAsync(string id, string memberId)
        => await _client.SendStreamAsync(GetRequest(id, memberId));

    internal RequestInformation UploadRequest(string id, string memberId, Stream file, string filePath)
        => FileUpload.Request(b => _client.Api.Api.Campaigns[id].Members[memberId].Art.ToPostRequestInformation(b), FileUpload.Body(file, filePath, SystemsService.MimeForExtension(filePath)));

    /// <summary>POST /api/campaigns/{id}/members/{member_id}/art. One multipart part named "file".</summary>
    public async Task<string> UploadAsync(string id, string memberId, string filePath)
        => await _client.SendUploadAsync(UploadRequest(id, memberId, FileUpload.Open(filePath), filePath));

    internal RequestInformation DeleteRequest(string id, string memberId)
        => _client.Api.Api.Campaigns[id].Members[memberId].Art.ToDeleteRequestInformation();

    public async Task<string> DeleteAsync(string id, string memberId) => await _client.SendAsync(DeleteRequest(id, memberId));
}
