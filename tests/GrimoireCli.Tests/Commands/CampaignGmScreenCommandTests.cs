using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class CampaignGmScreenCommandTests
{
    private static string RenderHelp(string verb) =>
        HelpRenderer.Render(CampaignsCommand.Create(), ["campaigns", "gm-screen", verb], full: false);

    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CampaignsCommand.Create().Parse(["gm-screen", .. args]);

    [Fact]
    public void ResetRejectsAnUnknownScope()
        => Assert.NotEmpty(Parse("reset", "--id", "c", "--scope", "battle").Errors);

    [Fact]
    public void ResetAcceptsAScope()
        => Assert.Empty(Parse("reset", "--id", "c", "--scope", "rest").Errors);

    [Fact]
    public void CopyNeedsASource()
        => Assert.NotEmpty(Parse("copy", "--id", "c").Errors);

    [Fact]
    public void ApplyPresetNeedsAPresetId()
        => Assert.NotEmpty(Parse("apply-preset", "--id", "c").Errors);

    [Fact]
    public void ImportNeedsABodySource()
        => Assert.NotEmpty(Parse("import", "--id", "c").Errors);

    [Fact]
    public void NoVerbCarriesARoleTag()
    {
        foreach (var verb in new[] { "get", "party", "export", "sources", "presets", "reset", "layout", "import", "copy", "apply-preset" })
            Assert.DoesNotContain("Role required:", RenderHelp(verb));
    }

    [Fact]
    public void GetStatesTheOwnerRule() => Assert.Contains("Only the campaign owner", RenderHelp("get"));

    [Fact]
    public void PresetsWarnsAboutExternalInstalls() => Assert.Contains("external installs", RenderHelp("presets"));
}
