using System.Text;
using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

public class TokenHelperTests
{
    // JWT with exp = 1775928439 (2026-04-11T17:07:19Z) — already expired relative to "now".
    private const string ExpiredToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiJ0ZXN0IiwidHlwZSI6ImFjY2VzcyIsImlhdCI6MTc3NTkyNDgzOSwiZXhwIjoxNzc1OTI4NDM5fQ.fakesig";

    // JWT without exp field (legacy token).
    private const string NoExpToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiJ0ZXN0IiwiaWF0IjoxNzc1OTI0ODAyfQ.fakesig";

    // Payload JSON is {"exp":9999999999,"note":">"} — the trailing '>' lands on a
    // 3-byte boundary that base64-encodes to '+', which becomes '-' in base64url.
    // Our TokenHelper maps '-' back to '+' before decoding; abs-cli's does not,
    // which is the bug this test guards against regressing here.
    private const string DashInPayloadToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjk5OTk5OTk5OTksIm5vdGUiOiI-In0.fakesig";

    // Payload JSON is {"exp":9999999999,"note":"?"} — same alignment trick, but the
    // '?' base64-encodes to '/', which becomes '_' in base64url.
    private const string UnderscoreInPayloadToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjk5OTk5OTk5OTksIm5vdGUiOiI_In0.fakesig";

    private static string BuildToken(long expUnix)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes($"{{\"exp\":{expUnix}}}"));
        return $"{header}.{payload}.fakesig";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    [Fact]
    public void GetExpiration_ReturnsExpTime_WhenPresent()
    {
        var exp = TokenHelper.GetExpiration(ExpiredToken);

        Assert.NotNull(exp);
        Assert.Equal(1775928439, exp.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void GetExpiration_ReturnsNull_WhenNoExp()
    {
        var exp = TokenHelper.GetExpiration(NoExpToken);

        Assert.Null(exp);
    }

    [Fact]
    public void GetExpiration_ReturnsNull_ForGarbageInput()
    {
        var exp = TokenHelper.GetExpiration("not-a-jwt");

        Assert.Null(exp);
    }

    [Theory]
    [InlineData("only.two")]
    [InlineData("a.b.c.d")]
    [InlineData("")]
    public void GetExpiration_ReturnsNull_ForNonThreePartString(string token)
    {
        Assert.Null(TokenHelper.GetExpiration(token));
    }

    [Fact]
    public void GetExpiration_ReturnsNull_ForMalformedPayload()
    {
        // Three parts, but the payload segment isn't valid base64/JSON.
        var exp = TokenHelper.GetExpiration("aGVhZGVy.not-valid-base64-json!!!.sig");

        Assert.Null(exp);
    }

    [Fact]
    public void GetExpiration_DecodesPayload_WhenBase64UrlContainsDash()
    {
        var exp = TokenHelper.GetExpiration(DashInPayloadToken);

        Assert.NotNull(exp);
        Assert.Equal(9999999999, exp.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void GetExpiration_DecodesPayload_WhenBase64UrlContainsUnderscore()
    {
        var exp = TokenHelper.GetExpiration(UnderscoreInPayloadToken);

        Assert.NotNull(exp);
        Assert.Equal(9999999999, exp.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void IsExpiringSoon_ReturnsTrue_WhenAlreadyExpired()
    {
        Assert.True(TokenHelper.IsExpiringSoon(ExpiredToken, thresholdSeconds: 60));
    }

    [Fact]
    public void IsExpiringSoon_ReturnsFalse_WhenNoExp()
    {
        Assert.False(TokenHelper.IsExpiringSoon(NoExpToken, thresholdSeconds: 60));
    }

    [Fact]
    public void IsExpiringSoon_ReturnsFalse_WhenFarBeyondThreshold()
    {
        // exp is centuries away; a 60s threshold should not trip.
        Assert.False(TokenHelper.IsExpiringSoon(DashInPayloadToken, thresholdSeconds: 60));
    }

    [Fact]
    public void IsExpiringSoon_ReturnsTrue_WhenRemainingTimeIsBelowThreshold()
    {
        var token = BuildToken(DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds());

        Assert.True(TokenHelper.IsExpiringSoon(token, thresholdSeconds: 120));
    }

    [Fact]
    public void IsExpiringSoon_ReturnsFalse_WhenRemainingTimeExceedsThreshold()
    {
        var token = BuildToken(DateTimeOffset.UtcNow.AddSeconds(300).ToUnixTimeSeconds());

        Assert.False(TokenHelper.IsExpiringSoon(token, thresholdSeconds: 60));
    }

    [Fact]
    public void SecondsUntilExpiry_ReturnsNull_WhenNoExp()
    {
        Assert.Null(TokenHelper.SecondsUntilExpiry(NoExpToken));
    }

    [Fact]
    public void SecondsUntilExpiry_ReturnsNegative_ForExpiredToken()
    {
        var seconds = TokenHelper.SecondsUntilExpiry(ExpiredToken);

        Assert.NotNull(seconds);
        Assert.True(seconds < 0);
    }

    [Fact]
    public void SecondsUntilExpiry_ReturnsApproximateRemainingTime_ForFutureToken()
    {
        var token = BuildToken(DateTimeOffset.UtcNow.AddSeconds(100).ToUnixTimeSeconds());

        var seconds = TokenHelper.SecondsUntilExpiry(token);

        Assert.NotNull(seconds);
        Assert.InRange(seconds.Value, 90, 100);
    }
}
