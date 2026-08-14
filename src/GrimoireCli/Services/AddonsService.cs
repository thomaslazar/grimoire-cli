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

    // No notFoundHint: install_addon raises only 502 (download failure) and 400
    // (no such id in the index, already covered by install's own Note); there is
    // no 404 path to hint at.
    public async Task<AddonInstalled> InstallAsync(string id, bool approveScript)
    {
        var body = new Generated.Models.AddonInstall { ApproveScript = approveScript };
        var info = _client.Api.Api.Addons[id].Install.ToPostRequestInformation(body);
        return await _client.SendAsync(
            info, AppJsonContext.Default.AddonInstalled, permissionHint: "the admin role");
    }

    // No notFoundHint: update_addon's 404 has two distinct causes — no such
    // add-on installed, or --script-approved true naming a script-free one —
    // and a hint would replace the server's discriminating body with a message
    // that cannot tell them apart.
    public async Task<AddonInstalled> UpdateAsync(string id, bool? enabled, bool? scriptApproved)
    {
        var body = BuildUpdateBody(enabled, scriptApproved);
        var info = _client.Api.Api.Addons[id].ToPatchRequestInformation(body);
        return await _client.SendAsync(
            info, AppJsonContext.Default.AddonInstalled, permissionHint: "the admin role");
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

    public async Task<UpgradeAllResult> UpgradeAllAsync()
    {
        var info = _client.Api.Api.Addons.UpdateAll.ToPostRequestInformation();
        return await _client.SendAsync(
            info, AppJsonContext.Default.UpgradeAllResult, permissionHint: "the admin role");
    }

    public async Task<AddonSettings> SettingsAsync(string? indexUrl, bool? allowScripts)
    {
        var body = BuildSettingsBody(indexUrl, allowScripts);
        var info = _client.Api.Api.Addons.Settings.ToPatchRequestInformation(body);
        return await _client.SendAsync(
            info, AppJsonContext.Default.AddonSettings, permissionHint: "the admin role");
    }

    /// <summary>
    /// AllowScripts and IndexUrl are composed-type wrappers whose constructor
    /// sets neither, so assigning through the wrapper only when the flag was
    /// given leaves an omitted one absent from the body. Internal (not private)
    /// so a test can pin that a client regeneration cannot silently change that.
    /// </summary>
    internal static Generated.Models.AddonSettingsUpdate BuildSettingsBody(string? indexUrl, bool? allowScripts)
    {
        var body = new Generated.Models.AddonSettingsUpdate();
        if (indexUrl is not null)
            body.IndexUrl = new Generated.Models.AddonSettingsUpdate.AddonSettingsUpdate_index_url { String = indexUrl };
        if (allowScripts is not null)
            body.AllowScripts = new Generated.Models.AddonSettingsUpdate.AddonSettingsUpdate_allow_scripts { Boolean = allowScripts.Value };
        return body;
    }
}
