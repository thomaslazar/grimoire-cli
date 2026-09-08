using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class DuplicatesScanCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(DuplicatesCommand.Create(), path, full);

    [Fact]
    public void TheGroupHostsAllThirteenLeaves()
    {
        Assert.Equal(
            ["link", "promote", "unlink", "merge-metadata", "delete", "compare",
             "scan", "scan-status", "cancel-scan", "groups", "dismiss", "dismissals", "undismiss"],
            DuplicatesCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan-status")]
    [InlineData("cancel-scan")]
    [InlineData("groups")]
    [InlineData("dismiss")]
    [InlineData("dismissals")]
    [InlineData("undismiss")]
    public void EveryDetectionCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["duplicates", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan-status")]
    [InlineData("cancel-scan")]
    [InlineData("groups")]
    [InlineData("dismiss")]
    [InlineData("dismissals")]
    [InlineData("undismiss")]
    public void EveryDetectionCommandCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["duplicates", leaf], full: true));
    }

    [Fact]
    public void ScanAndStatusAndCancelParseBare()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(["scan"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["scan-status"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["cancel-scan"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["groups"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["dismissals"]).Errors);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    public void ScanAcceptsEveryAccuracy(string accuracy)
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(["scan", "--accuracy", accuracy]).Errors);
    }

    [Fact]
    public void ScanRejectsAnUnknownAccuracy()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["scan", "--accuracy", "perfect"]).Errors);
    }

    [Fact]
    public void ScanTakesRepeatableResourceTypes()
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["scan", "--resource-types", "book", "map"]).Errors);
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(
            ["scan", "--resource-types", "spellbook"]).Errors);
    }

    // The server declares limit as Query(50, le=200): the ceiling is guarded,
    // the floor is not, and a negative slices to an empty page at HTTP 200.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("201")]
    public void GroupsRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["groups", "--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("200")]
    public void GroupsAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(DuplicatesCommand.Create().Parse(["groups", "--limit", limit]).Errors);
    }

    [Fact]
    public void GroupsReportsANonNumericLimitAsAParseError()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["groups", "--limit", "abc"]).Errors);
    }

    [Fact]
    public void DismissRequiresResourceTypeAndMemberIds()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["dismiss", "--resource-type", "book"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(
            ["dismiss", "--resource-type", "book", "--member-ids", "a", "b"]).Errors);
    }

    [Fact]
    public void UndismissRequiresAnId()
    {
        Assert.NotEmpty(DuplicatesCommand.Create().Parse(["undismiss"]).Errors);
        Assert.Empty(DuplicatesCommand.Create().Parse(["undismiss", "--id", "d1"]).Errors);
    }

    // A duplicate scan already in flight is a 200 the caller must not read as
    // "started", and a library scan running is a hard refusal instead.
    [Fact]
    public void ScanDocumentsBothConflictPaths()
    {
        var output = Help(["duplicates", "scan"]);
        Assert.Contains("already_running", output);
        Assert.Contains("409", output);
        Assert.Contains("exit 3", output);
    }
}
