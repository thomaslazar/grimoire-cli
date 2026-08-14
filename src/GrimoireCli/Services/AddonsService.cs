using GrimoireCli.Api;
using GrimoireCli.Models;

namespace GrimoireCli.Services;

public class AddonsService
{
    private readonly GrimoireApiClient _client;

    public AddonsService(GrimoireApiClient client) => _client = client;

    public async Task<AddonListResponse> ListAsync()
    {
        var info = _client.Api.Api.Addons.ToGetRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.AddonListResponse, permissionHint: "the admin role");
    }

    public async Task<RefreshResult> RefreshAsync()
    {
        var info = _client.Api.Api.Addons.Refresh.ToPostRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.RefreshResult, permissionHint: "the admin role");
    }

    public async Task<AddonInstalled> InstallAsync(string id, bool approveScript)
    {
        var body = new Generated.Models.AddonInstall { ApproveScript = approveScript };
        var info = _client.Api.Api.Addons[id].Install.ToPostRequestInformation(body);
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.AddonInstalled,
            permissionHint: "the admin role",
            notFoundHint: "No add-on with that ID. List them with: grimoire-cli addons list");
    }

    public async Task<AddonInstalled> UpdateAsync(string id, bool? enabled, bool? scriptApproved)
    {
        var body = BuildUpdateBody(enabled, scriptApproved);
        var info = _client.Api.Api.Addons[id].ToPatchRequestInformation(body);
        return await _client.SendAsync(
            info,
            AppJsonContext.Default.AddonInstalled,
            permissionHint: "the admin role",
            notFoundHint: "No add-on with that ID. List them with: grimoire-cli addons list");
    }

    /// <summary>
    /// Enabled and ScriptApproved are composed-type wrappers whose constructor
    /// sets neither, so assigning through the wrapper only when the flag was
    /// given leaves an omitted one absent from the body. Internal (not private)
    /// so a test can pin that a client regeneration cannot silently change that.
    /// </summary>
    internal static Generated.Models.AddonUpdate BuildUpdateBody(bool? enabled, bool? scriptApproved)
    {
        var body = new Generated.Models.AddonUpdate();
        if (enabled is not null)
            body.Enabled = new Generated.Models.AddonUpdate.AddonUpdate_enabled { Boolean = enabled.Value };
        if (scriptApproved is not null)
            body.ScriptApproved = new Generated.Models.AddonUpdate.AddonUpdate_script_approved { Boolean = scriptApproved.Value };
        return body;
    }

    public async Task<string> UninstallAsync(string id)
    {
        var info = _client.Api.Api.Addons[id].ToDeleteRequestInformation();
        return await _client.SendAsync(
            info,
            permissionHint: "the admin role",
            notFoundHint: "No add-on with that ID. List them with: grimoire-cli addons list");
    }
}
