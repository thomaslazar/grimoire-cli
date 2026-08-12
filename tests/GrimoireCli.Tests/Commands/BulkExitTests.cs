using GrimoireCli.Commands;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Commands;

public class BulkExitTests
{
    [Fact]
    public void NoErrorsIsZero() => Assert.Equal(0, BulkExit.CodeFor([]));

    [Fact]
    public void NullErrorsIsZero() => Assert.Equal(0, BulkExit.CodeFor(null));

    // 3, not 2: the request succeeded and some items did not apply. Conflating it
    // with an API error is exactly what an unattended caller cannot afford.
    [Fact]
    public void AnyErrorIsThree()
        => Assert.Equal(3, BulkExit.CodeFor([new BulkError { Id = "x", Detail = "Not found" }]));
}
