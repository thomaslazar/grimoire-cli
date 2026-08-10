using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class ExtractTokenTests
{
    // Grimoire's login response is untyped in the spec; the key is "token",
    // but "access_token" is the FastAPI convention and is accepted too.
    [Theory]
    [InlineData("{\"token\":\"abc\"}", "abc")]
    [InlineData("{\"access_token\":\"abc\"}", "abc")]
    [InlineData("{\"accessToken\":\"abc\"}", "abc")]
    public void ReturnsTokenForEveryAcceptedSpelling(string body, string expected)
    {
        Assert.Equal(expected, GrimoireApiClient.ExtractToken(body));
    }

    [Theory]
    [InlineData("{\"nope\":1}")]
    [InlineData("{\"token\":42}")]
    [InlineData("not json")]
    [InlineData("")]
    public void ReturnsNullWhenNoStringTokenIsPresent(string body)
    {
        Assert.Null(GrimoireApiClient.ExtractToken(body));
    }

    [Fact]
    public void PrefersAccessTokenWhenBothArePresent()
    {
        Assert.Equal("first", GrimoireApiClient.ExtractToken("{\"access_token\":\"first\",\"token\":\"second\"}"));
    }
}
