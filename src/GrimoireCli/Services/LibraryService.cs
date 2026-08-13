using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class LibraryService
{
    private readonly GrimoireApiClient _client;

    public LibraryService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// POST /api/rescan. Returns 200 with status "already_running" instead of
    /// starting a second scan, which is why the caller (ScanExit) maps that
    /// status onto exit 3 rather than treating this response as a plain success.
    /// </summary>
    public async Task<ScanTriggerResult> RescanAsync(string? scope, string? metadataMode)
    {
        var body = new Generated.Models.RescanRequest();
        // Kiota models the optional fields as composed types; assign through the
        // wrapper so an omitted flag stays absent from the body.
        if (scope is not null)
            body.Scope = new Generated.Models.RescanRequest.RescanRequest_scope { String = scope };
        if (metadataMode is not null)
            body.MetadataMode = ParseMetadataMode(metadataMode);
        var info = _client.Api.Api.Rescan.ToPostRequestInformation(body);
        return await _client.SendAsync(
            info, AppJsonContext.Default.ScanTriggerResult, permissionHint: "the admin role");
    }

    public async Task<ScanStatus> ScanStatusAsync()
    {
        var info = _client.Api.Api.ScanStatus.ToGetRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.ScanStatus, permissionHint: "the admin role");
    }

    public async Task<string> CancelScanAsync()
    {
        var info = _client.Api.Api.CancelScan.ToPostRequestInformation();
        return await _client.SendAsync(info, permissionHint: "the admin role");
    }

    private static Generated.Models.RescanRequest_metadata_mode ParseMetadataMode(string metadataMode) => metadataMode switch
    {
        "new" => Generated.Models.RescanRequest_metadata_mode.New,
        "missing" => Generated.Models.RescanRequest_metadata_mode.Missing,
        "replace" => Generated.Models.RescanRequest_metadata_mode.Replace,
        _ => throw new ArgumentOutOfRangeException(nameof(metadataMode), metadataMode, "Unrecognised metadata mode"),
    };
}
