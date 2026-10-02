using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LoginApiKeyTests
{
    [Theory]
    [InlineData(200, KeyLoginOutcome.Valid)]
    [InlineData(403, KeyLoginOutcome.ValidWithoutLibrary)]
    [InlineData(401, KeyLoginOutcome.Rejected)]
    [InlineData(429, KeyLoginOutcome.Rejected)]
    [InlineData(500, KeyLoginOutcome.Rejected)]
    [InlineData(null, KeyLoginOutcome.Rejected)]
    public void ClassifiesTheProbeStatus(int? status, KeyLoginOutcome expected)
    {
        Assert.Equal(expected, LoginCommand.ClassifyKeyProbe(status));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void RejectsCredentialFlagsBesideTheKey(bool username, bool password, bool passwordStdin)
    {
        Assert.Equal("Use --api-key-stdin on its own, without --username or a password.",
            LoginCommand.KeyFlagConflict(username, password, passwordStdin));
    }

    [Fact]
    public void AcceptsTheKeyOnItsOwn()
    {
        Assert.Null(LoginCommand.KeyFlagConflict(false, false, false));
    }

    [Fact]
    public void HelpDocumentsTheKeyFlag()
    {
        var output = HelpRenderer.Render(LoginCommand.Create(), ["login"], full: false);
        Assert.Contains("--api-key-stdin", output);
        Assert.Contains("authentication.md#api-keys", output);
    }
}
