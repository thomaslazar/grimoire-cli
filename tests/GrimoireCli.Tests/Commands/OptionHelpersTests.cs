using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// Range rejects at parse time what the server would silently clamp
/// (routers/backups/core.py stores max(0, min(23, hour)) and answers 200), so
/// these pin the boundaries rather than the message.
/// </summary>
public class OptionHelpersTests
{
    private static ParseResult Parse(Option<int?> option, params string[] args)
    {
        var command = new Command("demo") { option };
        return command.Parse(args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(7)]
    public void RangeAcceptsValuesInsideItsBounds(int value)
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        Assert.Empty(Parse(option, "--hour", value.ToString()).Errors);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(99)]
    public void RangeRejectsValuesOutsideItsBounds(int value)
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        Assert.NotEmpty(Parse(option, "--hour", value.ToString()).Errors);
    }

    [Fact]
    public void RangeErrorNamesTheOptionAndTheBounds()
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        var error = Assert.Single(Parse(option, "--hour", "99").Errors);
        Assert.Contains("--hour", error.Message);
        Assert.Contains("0", error.Message);
        Assert.Contains("23", error.Message);
    }

    // The two retention fields have a floor and no ceiling: the server applies
    // max(0, value) and nothing else.
    [Theory]
    [InlineData(0)]
    [InlineData(500000)]
    public void RangeWithoutAMaxAcceptsAnyValueAtOrAboveTheFloor(int value)
    {
        var option = OptionHelpers.Range("--retention-count", "Count", 0);
        Assert.Empty(Parse(option, "--retention-count", value.ToString()).Errors);
    }

    [Fact]
    public void RangeWithoutAMaxStillRejectsBelowTheFloor()
    {
        var option = OptionHelpers.Range("--retention-count", "Count", 0);
        Assert.NotEmpty(Parse(option, "--retention-count", "-1").Errors);
    }

    [Fact]
    public void AnOmittedRangeOptionIsNotAnError()
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        var parsed = Parse(option);
        Assert.Empty(parsed.Errors);
        Assert.Null(parsed.GetValue(option));
    }

    // An unconvertible token must reach the framework's own parse error rather
    // than throwing out of the validator, which Program.cs does not catch.
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("3.5")]
    [InlineData("2147483648")]
    public void RangeReportsRatherThanThrowsOnANonNumericValue(string value)
    {
        var option = OptionHelpers.Range("--hour", "Hour", 0, 23);
        var command = new Command("demo") { option };
        var parsed = command.Parse(["--hour", value]);
        Assert.NotEmpty(parsed.Errors);
    }
}
