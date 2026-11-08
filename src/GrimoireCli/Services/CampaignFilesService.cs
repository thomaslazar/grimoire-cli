using GrimoireCli.Api;
using GrimoireCli.Commands;

namespace GrimoireCli.Services;

/// <summary>Campaign file uploads (routers/campaigns/uploads.py:534-617).</summary>
public class CampaignFilesService
{
    private readonly GrimoireApiClient _client;

    public CampaignFilesService(GrimoireApiClient client) => _client = client;

    internal Microsoft.Kiota.Abstractions.RequestInformation GetRequest(string campaignId, string fileId)
        => _client.Api.Api.Campaigns[campaignId].Files[fileId].ToGetRequestInformation();

    /// <summary>GET /api/campaigns/{id}/files/{file_id}. Raw bytes.</summary>
    public async Task<Stream> GetAsync(string campaignId, string fileId)
        => await _client.SendStreamAsync(GetRequest(campaignId, fileId));

    /// <summary>
    /// POST /api/campaigns/{id}/files. Stores the file in the campaign, not the
    /// library, and links it as a `file` resource in the same call. Form fields
    /// are named as FastAPI binds them.
    /// </summary>
    public async Task<string> UploadAsync(string campaignId, string filePath, string? categoryId, string? newCategoryName)
        => await _client.SendUploadAsync(
            FileUpload.Request(
                b => _client.Api.Api.Campaigns[campaignId].Files.ToPostRequestInformation(b),
                Body(FileUpload.Open(filePath), filePath, categoryId, newCategoryName)),
            notFoundHint: "No campaign with that ID. List them with: grimoire-cli campaigns list");

    /// <summary>POST /api/campaigns/{id}/images. As <see cref="UploadAsync"/>, image types only.</summary>
    public async Task<string> UploadImageAsync(string campaignId, string filePath, string? categoryId, string? newCategoryName)
        => await _client.SendUploadAsync(
            UploadImageRequest(campaignId, FileUpload.Open(filePath), filePath, categoryId, newCategoryName),
            notFoundHint: "No campaign with that ID. List them with: grimoire-cli campaigns list");

    internal Microsoft.Kiota.Abstractions.RequestInformation UploadImageRequest(string campaignId, Stream file, string filePath, string? categoryId, string? newCategoryName)
        => FileUpload.Request(
            b => _client.Api.Api.Campaigns[campaignId].Images.ToPostRequestInformation(b),
            Body(file, filePath, categoryId, newCategoryName));

    private static StreamedMultipartBody Body(Stream file, string filePath, string? categoryId, string? newCategoryName)
    {
        var fields = new List<(string, string)>();
        if (categoryId is not null)
            fields.Add(("category_id", categoryId));
        if (newCategoryName is not null)
            fields.Add(("new_category_name", newCategoryName));
        return new StreamedMultipartBody(fields, Path.GetFileName(filePath), MimeForExtension(filePath), file);
    }

    /// <summary>
    /// The content type the server stores and derives is_image from. Unknown
    /// extensions send octet-stream. Internal so a test can pin the map.
    /// </summary>
    internal static string MimeForExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}
