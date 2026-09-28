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
}
