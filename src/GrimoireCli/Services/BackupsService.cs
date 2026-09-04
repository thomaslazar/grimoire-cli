using GrimoireCli.Api;

namespace GrimoireCli.Services;

/// <summary>
/// The six backup endpoints, every one require_admin. Backups are written to the
/// data directory rather than the library, so none of this depends on the
/// library mount being writable.
///
/// There is no restore endpoint and no upload: the archive can be taken and
/// fetched, and putting one back is out of band.
/// </summary>
public class BackupsService
{
    private const string AdminHint = "the admin role";
    private const string NotFoundHint =
        "No backup with that ID. List them with: grimoire-cli backups list";

    private readonly GrimoireApiClient _client;

    public BackupsService(GrimoireApiClient client) => _client = client;

    /// <summary>GET /api/backups.</summary>
    public async Task<string> ListAsync()
        => await _client.SendAsync(
            _client.Api.Api.Backups.ToGetRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>
    /// POST /api/backups. Snapshots the database under a read lock, so it can
    /// run longer than a typical request, and answers 409 when a backup is
    /// already in flight.
    /// </summary>
    public async Task<string> CreateAsync()
        => await _client.SendAsync(
            _client.Api.Api.Backups.ToPostRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>DELETE /api/backups/{id}. Answers 204, so the body is empty.</summary>
    public async Task<string> DeleteAsync(string id)
        => await _client.SendAsync(
            _client.Api.Api.Backups[id].ToDeleteRequestInformation(),
            permissionHint: AdminHint,
            notFoundHint: NotFoundHint);

    /// <summary>GET /api/backups/{id}/download. Serves application/zip.</summary>
    public async Task<Stream> DownloadAsync(string id)
        => await _client.SendStreamAsync(
            _client.Api.Api.Backups[id].Download.ToGetRequestInformation(),
            permissionHint: AdminHint,
            notFoundHint: NotFoundHint);

    /// <summary>GET /api/backups/settings.</summary>
    public async Task<string> SettingsAsync()
        => await _client.SendAsync(
            _client.Api.Api.Backups.Settings.ToGetRequestInformation(),
            permissionHint: AdminHint);

    /// <summary>
    /// PUT /api/backups/settings. A partial patch despite the method: omitted
    /// fields are left alone. Returns the full effective settings.
    /// </summary>
    public async Task<string> UpdateSettingsAsync(
        string? schedule, int? hour, int? minute, int? weekday,
        int? retentionCount, int? retentionGb, string? dir)
        => await _client.SendAsync(
            _client.Api.Api.Backups.Settings.ToPutRequestInformation(
                BuildSettingsBody(schedule, hour, minute, weekday, retentionCount, retentionGb, dir)),
            permissionHint: AdminHint);

    /// <summary>
    /// Every field is a composed-type wrapper, because each is Optional
    /// upstream. Assigning through the wrapper only when the flag was given
    /// leaves an omitted one absent from the body, which is what makes the PUT
    /// behave as the partial patch the server implements. Internal (not private)
    /// so a test can pin that a client regeneration cannot silently change it.
    /// </summary>
    internal static Generated.Models.BackupSettingsPatch BuildSettingsBody(
        string? schedule, int? hour, int? minute, int? weekday,
        int? retentionCount, int? retentionGb, string? dir)
    {
        var body = new Generated.Models.BackupSettingsPatch();
        if (schedule is not null)
            body.BackupSchedule = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule { String = schedule };
        if (hour is not null)
            body.BackupScheduleHour = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule_hour { Integer = hour.Value };
        if (minute is not null)
            body.BackupScheduleMinute = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule_minute { Integer = minute.Value };
        if (weekday is not null)
            body.BackupScheduleWeekday = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_schedule_weekday { Integer = weekday.Value };
        if (retentionCount is not null)
            body.BackupRetentionCount = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_retention_count { Integer = retentionCount.Value };
        if (retentionGb is not null)
            body.BackupRetentionGb = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_retention_gb { Integer = retentionGb.Value };
        if (dir is not null)
            body.BackupDir = new Generated.Models.BackupSettingsPatch.BackupSettingsPatch_backup_dir { String = dir };
        return body;
    }
}
