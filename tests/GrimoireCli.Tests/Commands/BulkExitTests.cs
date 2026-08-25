using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BulkExitTests
{
    // 3, not 2: the request succeeded and some items did not apply. Conflating it
    // with an API error is exactly what an unattended caller cannot afford.
    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 0)]
    public void CodeForReportsThreeOnlyWhenThereAreFailures(bool hasFailures, int expected)
        => Assert.Equal(expected, BulkExit.CodeFor(hasFailures));
}
