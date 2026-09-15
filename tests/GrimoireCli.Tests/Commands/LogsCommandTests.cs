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
    // A single Contains for the whole tag would still pass if it regressed to
    // "gm or admin", since that also contains "admin".
    [Fact]
    public void LogsDeclaresTheAdminRole()
    {
        Assert.Contains("Role required:\n  admin\n", Help());
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

    // The buffer is fed at DEBUG whatever LOG_LEVEL is set to, so --level debug
    // returns detail an operator cannot see in docker logs. Issue #45 recorded
    // this backwards, which is why it is pinned.
    [Fact]
    public void LogsSaysDebugIsAvailableRegardlessOfServerLogLevel()
    {
        var output = Help();
        // Not "20000": --limit's own description carries that number too, so the
        // assertion would pass with no Notes section at all.
        Assert.Contains("Ring buffer", output);
        Assert.Contains("LOG_LEVEL", output);
    }

    // Measured: eight info entries at seq [1,2,7,9,10,100,102,103] answered
    // limit=2 with [102,103] and limit=2&offset=2 with [10,100].
    [Fact]
    public void LogsSaysHowAPageIsSelectedAndOrdered()
    {
        var output = Help();
        Assert.Contains("newest end", output);
        Assert.Contains("oldest-first", output);
    }

    // Measured: after_seq=100 and after_seq=100&offset=2 returned the same page.
    [Fact]
    public void LogsSaysOffsetIsIgnoredWithAfterSeq()
    {
        Assert.Contains("ignored when --after-seq", Help());
    }

    // Measured: level=error returned entries [] with max_seq 107. Without this,
    // a caller filtering narrowly cannot tell the cursor still advanced.
    [Fact]
    public void LogsTeachesTheCursorAndItsBufferWideScope()
    {
        var output = Help();
        Assert.Contains("max_seq", output);
        Assert.Contains("whole buffer", output);
    }

    // total is the count at that level: 107 for debug, 8 for info, 0 for error.
    [Fact]
    public void LogsSaysWhatTotalCounts()
    {
        Assert.Contains("total counts what matches --level", Help());
    }
}
