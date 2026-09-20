using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class TokensCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(TokensCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(TokensCommand.Create().Parse(["list"]).Errors);
    }

    // All three reads are require_not_guest or weaker, which carries no tag.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("thumbnail")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["tokens", sub]));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["tokens", sub]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(TokensCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The default has to render natively, not live in a description string.
    [Fact]
    public void ListRendersItsLimitDefault()
    {
        Assert.Contains("[default: 100]", Help(["tokens", "list"]));
    }

    // Tokens carry is_explicit and the server filters on it per account. Audio
    // has no such field, so this claim is tokens-only and must be here.
    [Fact]
    public void ListSaysExplicitIsFilteredServerSide()
    {
        Assert.Contains("explicit permission", Help(["tokens", "list"]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(TokensCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    [Fact]
    public void BatchUpdateSaysOnlyAnUnresolvedIdIsPerItem()
    {
        var help = Help(["tokens", "batch-update"]);
        Assert.Contains("unresolved id", help);
        Assert.Contains("422", help);
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["tokens", "batch-update"]));
        Assert.Contains("1000", Help(["tokens", "batch-tag"]));
    }

    [Fact]
    public void BatchTagDocumentsTheForbiddenTagCharacters()
    {
        Assert.Contains("422 on the whole request", Help(["tokens", "batch-tag"]));
    }

    [Fact]
    public void UpdateRejectsAFieldTokenUpdateDoesNotDeclare()
    {
        Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"is_explict\":true}",
                GrimoireCli.Generated.Models.TokenUpdate.CreateFromDiscriminatorValue,
                "pass it with --id"));
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ThumbnailRequiresAnIdAndAnOutput()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["thumbnail", "--id", "x"]).Errors);
        Assert.NotEmpty(TokensCommand.Create().Parse(["thumbnail", "--output", "x.webp"]).Errors);
        Assert.Empty(TokensCommand.Create().Parse(["thumbnail", "--id", "x", "--output", "x.webp"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"tokens\"", Help(["tokens", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["tokens", "get"], full: true));
    }

    [Fact]
    public void TheGroupHostsFile()
    {
        Assert.Contains("file", TokensCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void FileRequiresAnOutput()
    {
        Assert.NotEmpty(TokensCommand.Create().Parse(["file", "--id", "t1"]).Errors);
        Assert.Empty(TokensCommand.Create().Parse(["file", "--id", "t1", "--output", "-"]).Errors);
    }
}
