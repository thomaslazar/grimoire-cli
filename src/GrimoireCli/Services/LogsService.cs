using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The single log read. Guarded by require_admin (routers/logs/core.py), so the
/// send names a permissionHint. The route takes no path segment, so there is no
/// notFoundHint to give.
/// </summary>
public class LogsService
{
    private const string AdminHint = "the admin role";

    private readonly GrimoireApiClient _client;

    public LogsService(GrimoireApiClient client) => _client = client;

    /// <summary>
    /// GET /api/logs. The level is sent through the generated enum rather than
    /// the string property beside it, which Kiota marks obsolete.
    /// </summary>
    public async Task<string> ReadAsync(string? level, int? limit, int? offset, int? afterSeq)
    {
        var info = _client.Api.Api.Logs.ToGetRequestInformation(c =>
        {
            if (level is not null)
                c.QueryParameters.LevelAsGetLevelQueryParameterType =
                    Enum.Parse<Generated.Api.Logs.GetLevelQueryParameterType>(level, true);
            c.QueryParameters.Limit = limit;
            c.QueryParameters.Offset = offset;
            c.QueryParameters.AfterSeq = afterSeq;
        });
        return await _client.SendAsync(info, permissionHint: AdminHint);
    }
}
