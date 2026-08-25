using GrimoireCli.Api;

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
    public async Task<string> RescanAsync(string? scope, string? metadataMode)
    {
        var body = BuildBody(scope, metadataMode);
        var info = _client.Api.Api.Rescan.ToPostRequestInformation(body);
        return await _client.SendAsync(info, permissionHint: "the admin role");
    }

    /// <summary>
    /// Scope has no constructor default, so assigning through the composed-type
    /// wrapper only when --scope is given already leaves it absent otherwise.
    /// MetadataMode is a plain nullable enum that the generated constructor sets
    /// to New unconditionally; null it out when --metadata-mode is omitted so the
    /// CLI forwards the server's own default implicitly instead of pinning it —
    /// thin pass-through, not a client-side mirror of server policy. Internal (not
    /// private) so a test can pin that the constructor default cannot creep back
    /// on a client regeneration.
    /// </summary>
    internal static Generated.Models.RescanRequest BuildBody(string? scope, string? metadataMode)
    {
        var body = new Generated.Models.RescanRequest();
        if (scope is not null)
            body.Scope = new Generated.Models.RescanRequest.RescanRequest_scope { String = scope };
        body.MetadataMode = metadataMode is not null ? ParseMetadataMode(metadataMode) : null;
        return body;
    }

    public async Task<string> ScanStatusAsync()
    {
        var info = _client.Api.Api.ScanStatus.ToGetRequestInformation();
        return await _client.SendAsync(info, permissionHint: "the admin role");
    }

    public async Task<string> CancelScanAsync()
    {
        var info = _client.Api.Api.CancelScan.ToPostRequestInformation();
        return await _client.SendAsync(info, permissionHint: "the admin role");
    }

    /// <summary>
    /// POST /api/maintenance/cleanup-missing. Deletes DB rows whose files are gone,
    /// committing per row, so a failure part-way leaves earlier removals applied.
    /// 409 while a scan runs; the server's message names that state, so no hint
    /// replaces it.
    /// </summary>
    public async Task<string> CleanupMissingAsync()
    {
        var info = _client.Api.Api.Maintenance.CleanupMissing.ToPostRequestInformation();
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
