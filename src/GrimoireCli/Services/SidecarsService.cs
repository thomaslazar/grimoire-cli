using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The three metadata-sidecar endpoints, every one require_admin. Sidecars are
/// written into the library tree beside each book, so unlike backups these do
/// depend on the library mount being writable.
/// </summary>
public class SidecarsService
{
    private const string AdminHint = "the admin role";

    private readonly GrimoireApiClient _client;

    public SidecarsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/maintenance/sidecars/settings.</summary>
    public async Task<string> SettingsAsync()
        => await _client.SendAsync(
            _client.Api.Api.Maintenance.Sidecars.Settings.ToGetRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>
    /// PUT /api/maintenance/sidecars/settings. A whole-object replace: the model
    /// declares concrete defaults rather than Optional, so every field the body
    /// omits is stored as false or empty.
    /// </summary>
    public async Task<string> SettingsSetAsync(string[] formats, bool covers, bool overwriteForeign)
    {
        var body = new Generated.Models.SidecarSettings
        {
            Formats = [.. formats],
            Covers = covers,
            OverwriteForeign = overwriteForeign,
        };
        return await _client.SendAsync(
            _client.Api.Api.Maintenance.Sidecars.Settings.ToPutRequestInformation(body),
            permissionHint: AdminHint);
    }

    /// <summary>
    /// POST /api/maintenance/sidecars/export. Runs inline rather than in the
    /// background, so the response carries the per-item outcome instead of a
    /// status to poll. 400 when no format is enabled, 409 while a scan runs.
    /// </summary>
    public async Task<string> ExportAsync()
        => await _client.SendAsync(
            _client.Api.Api.Maintenance.Sidecars.Export.ToPostRequestInformation(),
            permissionHint: AdminHint);
}
