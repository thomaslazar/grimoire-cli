using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LogsCommandTests
{
    private static string Help(bool full = false) =>
        HelpRenderer.Render(LogsCommand.Create(), ["logs"], full);

    [Fact]
    public void LogsParsesWithNoArguments()
    {
        Assert.Empty(LogsCommand.Create().Parse([]).Errors);
    }

    [Fact]
    public void LogsIsALeafWithNoSubcommands()
    {
        Assert.Empty(LogsCommand.Create().Subcommands);
    }

    // The route is require_admin, so the tag and the 403 message have to agree.
    [Fact]
    public void LogsDeclaresTheAdminRole()
    {
        var output = Help();
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("critical")]
    public void LogsAcceptsEveryServerLevel(string level)
    {
        Assert.Empty(LogsCommand.Create().Parse(["--level", level]).Errors);
    }

    // A level the server does not declare would 422 after a paid round-trip.
    [Fact]
    public void LogsRejectsAnUnknownLevel()
    {
        Assert.NotEmpty(LogsCommand.Create().Parse(["--level", "trace"]).Errors);
    }

    // The server declares limit as Query(200, ge=1, le=20000) and 422s outside
    // it; rejecting here saves the round-trip.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("20001")]
    public void LogsRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(LogsCommand.Create().Parse(["--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("20000")]
    public void LogsAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(LogsCommand.Create().Parse(["--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("--offset")]
    [InlineData("--after-seq")]
    public void LogsRejectsANegativeCursor(string flag)
    {
        Assert.NotEmpty(LogsCommand.Create().Parse([flag, "-1"]).Errors);
    }

    [Theory]
    [InlineData("--limit")]
    [InlineData("--offset")]
    [InlineData("--after-seq")]
    public void LogsReportsANonNumericValueAsAParseError(string flag)
    {
        Assert.NotEmpty(LogsCommand.Create().Parse([flag, "abc"]).Errors);
    }

    [Fact]
    public void LogsShowsTheResponseShapeWithTheCursorFields()
    {
        var output = Help(full: true);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"entries\":", output);
        Assert.Contains("\"max_seq\":", output);
        Assert.Contains("\"total\":", output);
    }
}
