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

    private static string RenderHelp(string[] path, bool full) =>
        HelpRenderer.Render(SystemsCommand.Create(), path, full);

    // The block is rendered from the generated model, so this is also the guard on
    // that: a regeneration that dropped a model's properties (microsoft/kiota#2338)
    // would empty the help output as well as the validation.
    [Fact]
    public void UpdateShowsItsRequestShapeFromTheGeneratedModel()
    {
        var output = RenderHelp(["systems", "update"], full: true);
        var expected = new GrimoireCli.Generated.Models.GameSystemUpdate()
            .GetFieldDeserializers().Keys;
        Assert.Equal(17, expected.Count);
        Assert.Contains("Request shape:", output);
        foreach (var field in expected)
            Assert.Contains($"\"{field}\":", output);
    }

    // Types are the point of the shape: a name list left "publishers is a list of
    // {name, url}" and "year is a number" to be learned by being refused once.
    [Fact]
    public void UpdateShowsFieldTypesNotJustNames()
    {
        var output = RenderHelp(["systems", "update"], full: true);
        Assert.Contains("\"year\": 0", output);
        Assert.Contains("\"is_explicit\": false", output);
        Assert.Contains("\"name\": \"<string>\"", output);
    }

    // The bulk body is an envelope, and the sample is the model
    // JsonBodyInput.Validate parses against — item fields nest inside items.
    [Fact]
    public void BatchUpdateShowsTheItemsEnvelope()
    {
        var output = RenderHelp(["systems", "batch-update"], full: true);
        Assert.Contains("Request shape:", output);
        Assert.Contains("\"items\":", output);
        Assert.Contains("\"id\":", output);
        Assert.Contains("\"system_family\":", output);
    }

    // Shapes cost tokens on every invocation, so they stay behind --help-full
    // with the response shapes rather than loading the default help.
    [Fact]
    public void RequestShapeStaysBehindHelpFull()
    {
        var output = RenderHelp(["systems", "update"], full: false);
        Assert.DoesNotContain("Request shape:", output);
        Assert.Contains("Run --help-full", output);
    }
}
