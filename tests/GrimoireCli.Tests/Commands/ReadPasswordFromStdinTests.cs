using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ReadPasswordFromStdinTests
{
    [Fact]
    public void ReadsTheFirstLine()
    {
        Assert.Equal("secret", LoginCommand.ReadPasswordFromStdin(new StringReader("secret\nignored\n")));
    }

    [Fact]
    public void StripsTheTrailingNewline()
    {
        Assert.Equal("secret", LoginCommand.ReadPasswordFromStdin(new StringReader("secret\n")));
    }

    [Fact]
    public void StripsACarriageReturnNewlinePair()
    {
        Assert.Equal("secret", LoginCommand.ReadPasswordFromStdin(new StringReader("secret\r\n")));
    }

    [Fact]
    public void ReturnsEmptyForEmptyStdin()
    {
        Assert.Equal("", LoginCommand.ReadPasswordFromStdin(new StringReader("")));
    }

    [Fact]
    public void PreservesSpacesInsideThePassword()
    {
        Assert.Equal("two words", LoginCommand.ReadPasswordFromStdin(new StringReader("two words\n")));
    }
}
