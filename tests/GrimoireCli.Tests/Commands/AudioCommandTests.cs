using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class AudioCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(AudioCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(AudioCommand.Create().Parse(["list"]).Errors);
    }

    // All three reads are require_not_guest or weaker, which carries no tag.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("artwork")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["audio", sub]));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["audio", sub]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(AudioCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The default has to render natively, not live in a description string.
    [Fact]
    public void ListRendersItsLimitDefault()
    {
        Assert.Contains("[default: 100]", Help(["audio", "list"]));
    }

    // Audio has no is_explicit anywhere — claiming a server-side explicit filter
    // here would describe a field the collection does not have.
    [Fact]
    public void ListDoesNotClaimAnExplicitFilter()
    {
        Assert.DoesNotContain("explicit", Help(["audio", "list"]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(AudioCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    [Fact]
    public void BatchUpdateSaysOnlyAnUnresolvedIdIsPerItem()
    {
        var help = Help(["audio", "batch-update"]);
        Assert.Contains("unresolved id", help);
        Assert.Contains("422", help);
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["audio", "batch-update"]));
        Assert.Contains("1000", Help(["audio", "batch-tag"]));
    }

    [Fact]
    public void BatchTagDocumentsTheForbiddenTagCharacters()
    {
        Assert.Contains("422 on the whole request", Help(["audio", "batch-tag"]));
    }

    // duration/title/artist/album are read off the file at index time and
    // AudioUpdate accepts none of them, so a caller reading artist in a response
    // will otherwise try to PATCH it.
    [Fact]
    public void UpdateSaysTheTagMetadataIsUnwritable()
    {
        var help = Help(["audio", "update"]);
        Assert.Contains("artist", help);
        Assert.Contains("cannot be set here", help);
    }

    [Fact]
    public void ArtworkExplainsItsThreeSources()
    {
        var help = Help(["audio", "artwork"]);
        Assert.Contains("folder art", help);
        Assert.Contains("embedded", help);
        Assert.Contains("has_artwork", help);
    }

    [Fact]
    public void UpdateRejectsAFieldAudioUpdateDoesNotDeclare()
    {
        Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"artist\":\"nope\"}",
                GrimoireCli.Generated.Models.AudioUpdate.CreateFromDiscriminatorValue,
                "pass it with --id"));
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ArtworkRequiresAnIdAndAnOutput()
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["artwork", "--id", "x"]).Errors);
        Assert.NotEmpty(AudioCommand.Create().Parse(["artwork", "--output", "x.jpg"]).Errors);
        Assert.Empty(AudioCommand.Create().Parse(["artwork", "--id", "x", "--output", "x.jpg"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"audio\"", Help(["audio", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["audio", "get"], full: true));
    }

    [Fact]
    public void TheGroupHostsFile()
    {
        Assert.Contains("file", AudioCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void FileRequiresAnOutput()
    {
        Assert.NotEmpty(AudioCommand.Create().Parse(["file", "--id", "a1"]).Errors);
        Assert.Empty(AudioCommand.Create().Parse(["file", "--id", "a1", "--output", "-"]).Errors);
    }

    [Fact]
    public void TheGroupHostsTheCoverSubgroup()
    {
        Assert.Contains("cover", AudioCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("upload")]
    [InlineData("delete")]
    [InlineData("from-source")]
    public void TheCoverSubgroupHostsItsFourVerbs(string leaf)
    {
        var cover = AudioCommand.Create().Subcommands.Single(c => c.Name == "cover");
        Assert.Contains(leaf, cover.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void OnlyCoverGetTakesAnOutput()
    {
        var audio = AudioCommand.Create();
        Assert.NotEmpty(audio.Parse(["cover", "get", "--id", "a1"]).Errors);
        Assert.Empty(audio.Parse(["cover", "get", "--id", "a1", "--output", "-"]).Errors);
        Assert.NotEmpty(audio.Parse(["cover", "delete", "--id", "a1", "--output", "-"]).Errors);
        Assert.NotEmpty(audio.Parse(["cover", "upload", "--id", "a1", "--file", "c.png", "--output", "-"]).Errors);
        Assert.NotEmpty(audio.Parse(
            ["cover", "from-source", "--id", "a1", "--source-type", "book", "--source-id", "b1", "--output", "-"])
            .Errors);
    }

    [Fact]
    public void CoverWritesRequireTheirFlags()
    {
        var audio = AudioCommand.Create();
        Assert.NotEmpty(audio.Parse(["cover", "upload", "--id", "a1"]).Errors);
        Assert.Empty(audio.Parse(["cover", "upload", "--id", "a1", "--file", "c.png"]).Errors);
        Assert.NotEmpty(audio.Parse(["cover", "from-source", "--id", "a1", "--source-type", "book"]).Errors);
        Assert.Empty(audio.Parse(
            ["cover", "from-source", "--id", "a1", "--source-type", "book", "--source-id", "b1"]).Errors);
        Assert.Empty(audio.Parse(["cover", "delete", "--id", "a1"]).Errors);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("upload")]
    [InlineData("delete")]
    [InlineData("from-source")]
    public void EveryCoverVerbDeclaresTheGmOrAdminRole(string leaf)
    {
        var help = HelpRenderer.Render(AudioCommand.Create(), ["audio", "cover", leaf], full: false);
        Assert.Contains("Role required:", help);
        Assert.Contains("gm or admin", help);
    }

    // The whole reason both exist: artwork resolves the precedence chain, cover
    // get serves only what was deliberately set.
    [Fact]
    public void CoverGetDistinguishesItselfFromArtwork()
    {
        var help = HelpRenderer.Render(AudioCommand.Create(), ["audio", "cover", "get"], full: false);
        Assert.Contains("audio artwork", help);
    }

    // Deleting the set cover does not necessarily leave the track without art.
    [Fact]
    public void CoverDeleteWarnsArtworkCanSurvive()
    {
        var help = HelpRenderer.Render(AudioCommand.Create(), ["audio", "cover", "delete"], full: false);
        Assert.Contains("has_artwork", help);
    }
}
