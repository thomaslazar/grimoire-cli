using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Every BackupSettingsPatch field is a composed-type wrapper, because each is
/// Optional upstream. These pin that an omitted flag stays absent from the body
/// — which is what makes the PUT behave as the partial patch the server
/// implements — and that each given one lands on the right wrapper branch.
/// </summary>
public class BackupsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static Generated.Models.BackupSettingsPatch Empty() =>
        BackupsService.BuildSettingsBody(null, null, null, null, null, null, null);

    [Fact]
    public void OmittedFlagsLeaveEveryFieldNull()
    {
        var body = Empty();
        Assert.Null(body.BackupSchedule);
        Assert.Null(body.BackupScheduleHour);
        Assert.Null(body.BackupScheduleMinute);
        Assert.Null(body.BackupScheduleWeekday);
        Assert.Null(body.BackupRetentionCount);
        Assert.Null(body.BackupRetentionGb);
        Assert.Null(body.BackupDir);
    }

    [Fact]
    public void ScheduleLandsOnTheStringBranch()
    {
        var body = BackupsService.BuildSettingsBody("daily", null, null, null, null, null, null);
        Assert.Equal("daily", body.BackupSchedule?.String);
        Assert.Null(body.BackupScheduleHour);
    }

    [Fact]
    public void TheNumericFieldsLandOnTheIntegerBranch()
    {
        var body = BackupsService.BuildSettingsBody(null, 3, 30, 6, 10, 25, null);
        Assert.Equal(3, body.BackupScheduleHour?.Integer);
        Assert.Equal(30, body.BackupScheduleMinute?.Integer);
        Assert.Equal(6, body.BackupScheduleWeekday?.Integer);
        Assert.Equal(10, body.BackupRetentionCount?.Integer);
        Assert.Equal(25, body.BackupRetentionGb?.Integer);
    }

    [Fact]
    public void DirLandsOnTheStringBranch()
    {
        var body = BackupsService.BuildSettingsBody(null, null, null, null, null, null, "/data/backups");
        Assert.Equal("/data/backups", body.BackupDir?.String);
    }

    // "" is meaningful: it resets backup_dir to DATA_PATH/backups. It must reach
    // the body as an empty string rather than be treated as absent.
    [Fact]
    public void AnEmptyDirSurvivesAsAnEmptyString()
    {
        var body = BackupsService.BuildSettingsBody(null, null, null, null, null, null, "");
        Assert.NotNull(body.BackupDir);
        Assert.Equal("", body.BackupDir?.String);
    }

    // Zero is meaningful for both retentions: it means "no limit of this kind".
    [Fact]
    public void ZeroRetentionIsSentRatherThanTreatedAsAbsent()
    {
        var body = BackupsService.BuildSettingsBody(null, null, null, null, 0, 0, null);
        Assert.Equal(0, body.BackupRetentionCount?.Integer);
        Assert.Equal(0, body.BackupRetentionGb?.Integer);
    }

    [Theory]
    [InlineData("list", "/api/backups")]
    [InlineData("settings", "/api/backups/settings")]
    public void TheCollectionPathsAreWhatTheBuildersProduce(string which, string expected)
    {
        var client = Client();
        var info = which == "list"
            ? client.Api.Api.Backups.ToGetRequestInformation()
            : client.Api.Api.Backups.Settings.ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test" + expected, info.URI.AbsoluteUri);
    }

    [Fact]
    public void TheDownloadPathIncludesTheIdAndTheDownloadSegment()
    {
        var info = Client().Api.Api.Backups["abc123"].Download.ToGetRequestInformation();
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal("http://example.test/api/backups/abc123/download", info.URI.AbsoluteUri);
    }
}
