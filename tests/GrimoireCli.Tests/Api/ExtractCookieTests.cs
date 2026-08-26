using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

// Grimoire delivers the refresh token only as a Set-Cookie header, so this
// parser is the sole way the CLI ever obtains one.
public class ExtractCookieTests
{
    private const string Session = "grimoire_session=jwt; HttpOnly; Max-Age=2592000; Path=/; SameSite=lax";
    private const string Refresh = "grimoire_refresh=abc123; HttpOnly; Max-Age=2592000; Path=/api/auth; SameSite=strict";

    [Fact]
    public void FindsTheNamedCookieAmongOthers()
        => Assert.Equal("abc123",
            GrimoireApiClient.ExtractCookie(new[] { Session, Refresh }, "grimoire_refresh"));

    [Fact]
    public void ReturnsNullWhenTheCookieIsAbsent()
        => Assert.Null(GrimoireApiClient.ExtractCookie(new[] { Session }, "grimoire_refresh"));

    [Fact]
    public void ReturnsNullForNoHeadersAtAll()
        => Assert.Null(GrimoireApiClient.ExtractCookie(Array.Empty<string>(), "grimoire_refresh"));

    [Fact]
    public void HandlesAValueWithNoTrailingAttributes()
        => Assert.Equal("bare",
            GrimoireApiClient.ExtractCookie(new[] { "grimoire_refresh=bare" }, "grimoire_refresh"));

    // A longer name must not match on its prefix.
    [Fact]
    public void DoesNotMatchANameThatMerelyStartsTheSame()
        => Assert.Null(GrimoireApiClient.ExtractCookie(
            new[] { "grimoire_refresh_other=nope; Path=/" }, "grimoire_refresh"));

    // The server clears a dead cookie by sending it empty. Callers test with
    // string.IsNullOrEmpty, so "" and null are equivalent to them.
    [Fact]
    public void ReturnsAnEmptyStringWhenTheServerClearsTheCookie()
        => Assert.Equal("",
            GrimoireApiClient.ExtractCookie(new[] { "grimoire_refresh=; Path=/api/auth" }, "grimoire_refresh"));
}
