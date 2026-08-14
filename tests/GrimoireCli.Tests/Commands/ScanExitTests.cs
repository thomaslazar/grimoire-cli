using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Commands;

public class ScanExitTests
{
    [Theory]
    [InlineData("already_running", 3)]
    [InlineData("scan_started", 0)]
    [InlineData(null, 0)]
    public void RescanExitsThreeOnlyWhenTheScanDidNotStart(string? status, int expected)
        => Assert.Equal(expected, ScanExit.CodeFor(new ScanTriggerResult { Status = status }));
}
