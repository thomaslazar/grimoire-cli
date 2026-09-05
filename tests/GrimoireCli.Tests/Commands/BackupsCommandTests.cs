using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BackupsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(BackupsCommand.Create(), path, full);

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("download")]
    public void EveryCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["backups", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheFourVerbs()
    {
        var names = BackupsCommand.Create().Subcommands.Select(c => c.Name).ToArray();
        Assert.Contains("list", names);
        Assert.Contains("create", names);
        Assert.Contains("delete", names);
        Assert.Contains("download", names);
    }

    [Fact]
    public void DeleteRequiresAnId()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["delete"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["delete", "--id", "abc"]).Errors);
    }

    [Fact]
    public void DownloadRequiresBothIdAndOutput()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["download", "--id", "abc"]).Errors);
        Assert.NotEmpty(BackupsCommand.Create().Parse(["download", "--output", "-"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["download", "--id", "abc", "--output", "-"]).Errors);
    }

    [Fact]
    public void CreateDocumentsTheReadLockAndTheConflict()
    {
        var output = Help(["backups", "create"]);
        Assert.Contains("lock", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("409", output);
    }

    [Fact]
    public void DeleteDocumentsThatItIsIrreversibleAndAnswersNoBody()
    {
        var output = Help(["backups", "delete"]);
        Assert.Contains("no body", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be undone", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("download")]
    public void EveryCommandWithABodyCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["backups", leaf], full: true));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public void TheSettingsPairDeclaresTheAdminRole(string leaf)
    {
        var output = HelpRenderer.Render(BackupsCommand.Create(), ["backups", "settings", leaf], full: false);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheSettingsSubgroup()
    {
        var settings = BackupsCommand.Create().Subcommands.Single(c => c.Name == "settings");
        Assert.Equal(["get", "set"], settings.Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void SettingsSetErrorsWithNoFlags()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set"]).Errors);
    }

    // The unconvertible token must reach the framework's own parse error rather
    // than throwing out of the "at least one flag" validator, which is what
    // actually crashed the real CLI.
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("3.5")]
    [InlineData("2147483648")]
    public void SettingsSetReportsRatherThanThrowsOnANonNumericValue(string value)
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set", "--hour", value]).Errors);
    }

    [Theory]
    [InlineData("--schedule", "daily")]
    [InlineData("--hour", "3")]
    [InlineData("--minute", "30")]
    [InlineData("--weekday", "6")]
    [InlineData("--retention-count", "7")]
    [InlineData("--retention-gb", "20")]
    [InlineData("--dir", "")]
    public void SettingsSetAcceptsAnySingleFlagOnItsOwn(string flag, string value)
    {
        Assert.Empty(BackupsCommand.Create().Parse(["settings", "set", flag, value]).Errors);
    }

    [Fact]
    public void SettingsSetRejectsAnUnknownSchedule()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set", "--schedule", "fortnightly"]).Errors);
    }

    // The server clamps rather than refusing, so the CLI is the only thing that
    // can tell the caller their value was not stored as given.
    [Theory]
    [InlineData("--hour", "24")]
    [InlineData("--minute", "60")]
    [InlineData("--weekday", "7")]
    [InlineData("--retention-count", "-1")]
    [InlineData("--retention-gb", "-1")]
    public void SettingsSetRejectsOutOfRangeNumbers(string flag, string value)
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["settings", "set", flag, value]).Errors);
    }

    // The rejection cases above cannot catch a bound that is too tight: --minute 60
    // is out of range under both 0-59 and a mistaken 0-23. These pin the ceilings.
    [Theory]
    [InlineData("--hour", "23")]
    [InlineData("--minute", "59")]
    [InlineData("--weekday", "6")]
    public void SettingsSetAcceptsTheTopOfEachRange(string flag, string value)
    {
        Assert.Empty(BackupsCommand.Create().Parse(["settings", "set", flag, value]).Errors);
    }

    [Fact]
    public void SettingsSetDocumentsThePatchSemanticsAndTheLocks()
    {
        var output = HelpRenderer.Render(BackupsCommand.Create(), ["backups", "settings", "set"], full: false);
        Assert.Contains("left alone", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0=Mon", output);
        Assert.Contains("400", output);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public void TheSettingsPairCarriesAResponseShape(string leaf)
    {
        var output = HelpRenderer.Render(BackupsCommand.Create(), ["backups", "settings", leaf], full: true);
        Assert.Contains("Response shape:", output);
    }
}
