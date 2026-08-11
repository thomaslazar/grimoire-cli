using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SystemsCommandTests
{
    private static Command Root()
    {
        var root = new RootCommand("test");
        root.Subcommands.Add(SystemsCommand.Create());
        return root;
    }

    [Theory]
    [InlineData("systems list --sort name")]
    [InlineData("systems list --sort book_count")]
    [InlineData("systems list --sort page_count")]
    [InlineData("systems list --sort year")]
    public void AcceptsEverySupportedSortKey(string input)
    {
        Assert.Empty(Root().Parse(input).Errors);
    }

    [Fact]
    public void RejectsAnUnknownSortKeyBeforeAnyRequestIsMade()
    {
        var result = Root().Parse("systems list --sort tite");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Must be one of: name, book_count, page_count, year", result.Errors[0].Message);
    }

    [Fact]
    public void RejectsAnUnknownBookSortKey()
    {
        var result = Root().Parse("systems get --id x --book-sort pages");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Must be one of: category, title, page_count, year", result.Errors[0].Message);
    }

    // category is the server's own default for book_sort even though its whitelist
    // omits it, so the CLI must not be stricter than the server here.
    [Fact]
    public void AcceptsCategoryAsABookSortKey()
    {
        Assert.Empty(Root().Parse("systems get --id x --book-sort category").Errors);
    }

    [Fact]
    public void RequiresAnIdOnGet()
    {
        Assert.NotEmpty(Root().Parse("systems get").Errors);
    }

    [Fact]
    public void LeavesFilterValuesUnvalidated()
    {
        Assert.Empty(Root().Parse("systems list --genre Cyberpunk --family Shadowrun --edition 6").Errors);
        Assert.Empty(Root().Parse("systems list --genre \"a genre that does not exist\"").Errors);
    }

    [Fact]
    public void ListAcceptsParentIdAndIncludeChildren()
    {
        var command = SystemsCommand.Create();
        var result = command.Parse("list --parent-id abc123 --include-children");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void UpdateRequiresAnId()
    {
        Assert.NotEmpty(Root().Parse("systems update --stdin").Errors);
    }

    [Fact]
    public void UpdateAcceptsEitherInputSource()
    {
        Assert.Empty(Root().Parse("systems update --id x --stdin").Errors);
        Assert.Empty(Root().Parse("systems update --id x --input body.json").Errors);
    }

    [Fact]
    public void UpdateRejectsBothInputSourcesAtParseTime()
    {
        var result = Root().Parse("systems update --id x --stdin --input body.json");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("not both", result.Errors[0].Message);
    }

    [Fact]
    public void UpdateRejectsNeitherInputSourceAtParseTime()
    {
        var result = Root().Parse("systems update --id x");
        Assert.NotEmpty(result.Errors);
        Assert.Contains("--input", result.Errors[0].Message);
    }

    [Theory]
    [InlineData("systems batch-update --stdin")]
    [InlineData("systems batch-update --input items.json")]
    [InlineData("systems batch-tag --stdin")]
    [InlineData("systems batch-tag --input tags.json")]
    public void BatchCommandsAcceptEitherInputSource(string input)
    {
        Assert.Empty(Root().Parse(input).Errors);
    }

    [Theory]
    [InlineData("systems batch-update")]
    [InlineData("systems batch-tag")]
    [InlineData("systems batch-update --stdin --input items.json")]
    [InlineData("systems batch-tag --stdin --input tags.json")]
    public void BatchCommandsRequireExactlyOneInputSource(string input)
    {
        Assert.NotEmpty(Root().Parse(input).Errors);
    }

    // Neither takes --id: the ids are in the body.
    [Theory]
    [InlineData("systems batch-update --stdin --id x")]
    [InlineData("systems batch-tag --stdin --id x")]
    public void BatchCommandsTakeNoIdFlag(string input)
    {
        Assert.NotEmpty(Root().Parse(input).Errors);
    }
}
