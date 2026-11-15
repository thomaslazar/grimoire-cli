using GrimoireCli.Api;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Services;

/// <summary>Admin rules restricting every book beneath a books/ or armory/ folder.</summary>
public class FolderAccessService
{
    private const string AdminHint = "the admin role";

    private readonly GrimoireApiClient _client;

    public FolderAccessService(GrimoireApiClient client) => _client = client;

    public async Task<string> ListAsync()
        => await _client.SendAsync(ListRequest(), permissionHint: AdminHint);

    public async Task<string> SetAsync(string path, string level)
        => await _client.SendAsync(SetRequest(path, level), permissionHint: AdminHint);

    public async Task<string> RemoveAsync(string path)
        => await _client.SendAsync(RemoveRequest(path), permissionHint: AdminHint);

    internal RequestInformation ListRequest()
        => _client.Api.Api.Settings.FolderAccess.ToGetRequestInformation();

    internal RequestInformation SetRequest(string path, string level)
        => _client.Api.Api.Settings.FolderAccess.ToPutRequestInformation(new Generated.Models.FolderAccessRuleIn
        {
            Path = path,
            Level = Enum.Parse<Generated.Models.FolderAccessRuleIn_level>(level, true),
        });

    internal RequestInformation RemoveRequest(string path)
        => _client.Api.Api.Settings.FolderAccess.ToDeleteRequestInformation(c => c.QueryParameters.Path = path);
}
