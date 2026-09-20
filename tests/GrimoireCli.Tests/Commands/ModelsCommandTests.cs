using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ModelsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(ModelsCommand.Create(), path, full);

    [Fact]
    public void ListParsesWithNoArguments()
    {
        Assert.Empty(ModelsCommand.Create().Parse(["list"]).Errors);
    }

    // All three reads are require_not_guest or weaker, which carries no tag.
    [Theory]
    [InlineData("list")]
    [InlineData("get")]
    [InlineData("thumbnail")]
    public void ReadsDeclareNoRole(string sub)
    {
        Assert.DoesNotContain("Role required:", Help(["models", sub]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ListRejectsALimitBelowOne(string limit)
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["list", "--limit", limit]).Errors);
    }

    // The server declares no ceiling, so the CLI must not invent one.
    [Fact]
    public void ListAcceptsALimitAboveAnyServerPageSize()
    {
        Assert.Empty(ModelsCommand.Create().Parse(["list", "--limit", "100000"]).Errors);
    }

    [Fact]
    public void ListRejectsANegativeOffset()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["list", "--offset", "-1"]).Errors);
    }

    // The default has to render natively, not live in a description string.
    [Fact]
    public void ListRendersItsLimitDefault()
    {
        Assert.Contains("[default: 100]", Help(["models", "list"]));
    }

    // The read side exposes a derived pair; the write side takes one tri-state
    // field. Without this a caller reads is_presupported and tries to write it.
    [Fact]
    public void GetExplainsTheDerivedSupportPair()
    {
        var help = Help(["models", "get"]);
        Assert.Contains("is_presupported", help);
        Assert.Contains("unknown", help);
    }

    [Fact]
    public void GetRequiresAnId()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["get"]).Errors);
    }

    [Fact]
    public void ThumbnailRequiresAnIdAndAnOutput()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["thumbnail", "--id", "x"]).Errors);
        Assert.NotEmpty(ModelsCommand.Create().Parse(["thumbnail", "--output", "x.webp"]).Errors);
        Assert.Empty(ModelsCommand.Create().Parse(["thumbnail", "--id", "x", "--output", "x.webp"]).Errors);
    }

    [Fact]
    public void ListRendersItsResponseShape()
    {
        Assert.Contains("\"models\"", Help(["models", "list"], full: true));
    }

    [Fact]
    public void GetRendersItsResponseShape()
    {
        Assert.Contains("\"folder_tags\"", Help(["models", "get"], full: true));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("batch-update")]
    [InlineData("batch-tag")]
    public void WritesDeclareTheGmOrAdminRole(string sub)
    {
        Assert.Contains("Role required:\n  gm or admin\n", Help(["models", sub]));
    }

    [Fact]
    public void UpdateRequiresExactlyOneBodySource()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["update", "--id", "x"]).Errors);
        Assert.NotEmpty(ModelsCommand.Create()
            .Parse(["update", "--id", "x", "--stdin", "--input", "f.json"]).Errors);
    }

    // The sharp one: only unknown is one-way. true and false both write in
    // either direction, so help that calls the field itself one-way is wrong.
    [Fact]
    public void UpdateSaysOnlyUnknownIsOneWay()
    {
        var help = Help(["models", "update"]);
        Assert.Contains("only unknown is", help);
        Assert.Contains("true and false in either direction", help);
        Assert.DoesNotContain("is_supported is one-way", help);
    }

    // Only an unresolved id is per-item here; models passes no validate hook.
    [Fact]
    public void BatchUpdateSaysOnlyAnUnresolvedIdIsPerItem()
    {
        var help = Help(["models", "batch-update"]);
        Assert.Contains("unresolved id", help);
        Assert.Contains("422", help);
    }

    [Fact]
    public void BatchesDocumentTheThousandItemCap()
    {
        Assert.Contains("1000", Help(["models", "batch-update"]));
        Assert.Contains("1000", Help(["models", "batch-tag"]));
    }

    [Fact]
    public void UpdateRejectsAFieldModel3DUpdateDoesNotDeclare()
    {
        Assert.Throws<BodyInputException>(() =>
            JsonBodyInput.Validate("{\"is_suported\":true}",
                GrimoireCli.Generated.Models.Model3DUpdate.CreateFromDiscriminatorValue,
                "pass it with --id"));
    }

    [Fact]
    public void UpdateRendersTheRequestShape()
    {
        Assert.Contains("\"is_supported\"", Help(["models", "update"], full: true));
    }

    [Fact]
    public void TheGroupHostsFile()
    {
        Assert.Contains("file", ModelsCommand.Create().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void FileRequiresAnOutput()
    {
        Assert.NotEmpty(ModelsCommand.Create().Parse(["file", "--id", "md1"]).Errors);
        Assert.Empty(ModelsCommand.Create().Parse(["file", "--id", "md1", "--output", "-"]).Errors);
    }
}
