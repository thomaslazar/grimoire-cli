using GrimoireCli.Api;
using GrimoireCli.Commands;

namespace GrimoireCli.Services;

/// <summary>Campaign file uploads (routers/campaigns/uploads.py:534-617).</summary>
public class CampaignFilesService
{
    private readonly GrimoireApiClient _client;

    public CampaignFilesService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// POST /api/campaigns/{id}/files. Stores the file in the campaign, not the
    /// library, and links it as a `file` resource in the same call. Form fields
    /// are named as FastAPI binds them.
    /// </summary>
    public async Task<string> UploadAsync(string campaignId, string filePath, string? categoryId, string? newCategoryName)
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
        var body = new Microsoft.Kiota.Abstractions.MultipartBody();
        body.AddOrReplacePart("file", MimeForExtension(filePath), bytes, Path.GetFileName(filePath));
        if (categoryId is not null)
            body.AddOrReplacePart("category_id", "text/plain", categoryId);
        if (newCategoryName is not null)
            body.AddOrReplacePart("new_category_name", "text/plain", newCategoryName);
        return await _client.SendAsync(
            _client.Api.Api.Campaigns[campaignId].Files.ToPostRequestInformation(body),
            notFoundHint: "No campaign with that ID. List them with: grimoire-cli campaigns list");
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
