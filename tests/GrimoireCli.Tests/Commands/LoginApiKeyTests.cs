using GrimoireCli.Api;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LoginApiKeyTests
{
    [Theory]
    [InlineData(200, "{}", KeyLoginOutcome.Valid)]
    [InlineData(403, "{\"detail\":\"This API key needs 'read' access to 'library' (it has 'none')\"}", KeyLoginOutcome.ValidWithoutLibrary)]
    [InlineData(403, "{\"detail\":\"API keys are disabled on this server\"}", KeyLoginOutcome.Rejected)]
    [InlineData(403, "{\"detail\":\"API keys are not enabled for your account\"}", KeyLoginOutcome.Rejected)]
    [InlineData(403, "{\"detail\":\"API keys cannot be used for this endpoint\"}", KeyLoginOutcome.Rejected)]
    [InlineData(401, "{}", KeyLoginOutcome.Rejected)]
    [InlineData(429, "{}", KeyLoginOutcome.Rejected)]
    [InlineData(500, "{}", KeyLoginOutcome.Rejected)]
    [InlineData(null, "", KeyLoginOutcome.Rejected)]
    public void ClassifiesTheProbeStatus(int? status, string body, KeyLoginOutcome expected)
    {
        Assert.Equal(expected, LoginCommand.ClassifyKeyProbe(status, body));
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
        Assert.Contains("https://github.com/thomaslazar/grimoire-cli/blob/main/docs/authentication.md#api-keys", output);
        Assert.Contains(GrimoireApiClient.KeyPermissionsUrl, output);
    }
}
