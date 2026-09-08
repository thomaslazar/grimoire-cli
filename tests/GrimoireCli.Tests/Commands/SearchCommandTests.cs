using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class SearchCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(SearchCommand.Create(), path, full);

    [Fact]
    public void SearchRequiresAQuery()
    {
        Assert.NotEmpty(SearchCommand.Create().Parse([]).Errors);
        Assert.Empty(SearchCommand.Create().Parse(["--query", "dragon"]).Errors);
    }

    [Fact]
    public void TheGroupHostsTheFieldsLeaf()
    {
        Assert.Equal(["fields"], SearchCommand.Create().Subcommands.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void FieldsParsesWithNoArguments()
    {
        Assert.Empty(SearchCommand.Create().Parse(["fields"]).Errors);
    }

    // The server declares limit as Query(50, le=200): it 422s above 200 but has
    // no lower bound, and the value reaches SQLite as a bare LIMIT, where -1
    // means unlimited. Verified live: --limit -1 returned all 126 indexed pages
    // with a 200, so the whole index comes back for a value the server accepted.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("201")]
    public void SearchRejectsALimitOutsideTheServersRange(string limit)
    {
        Assert.NotEmpty(SearchCommand.Create().Parse(["--query", "a", "--limit", limit]).Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("200")]
    public void SearchAcceptsTheBoundsOfTheServersRange(string limit)
    {
        Assert.Empty(SearchCommand.Create().Parse(["--query", "a", "--limit", limit]).Errors);
    }

    // Range reads the raw token: an unconvertible one must surface as a parse
    // error rather than throwing out of Parse.
    [Fact]
    public void SearchReportsANonNumericLimitAsAParseError()
    {
        Assert.NotEmpty(SearchCommand.Create().Parse(["--query", "a", "--limit", "abc"]).Errors);
    }

    [Theory]
    [InlineData(new object[] { new[] { "search" } })]
    [InlineData(new object[] { new[] { "search", "fields" } })]
    public void EveryCommandCarriesAResponseShape(string[] path)
    {
        Assert.Contains("Response shape:", Help(path, full: true));
    }

    // Both routes are require_not_guest, which is the CLI's no-tag default.
    [Theory]
    [InlineData(new object[] { new[] { "search" } })]
    [InlineData(new object[] { new[] { "search", "fields" } })]
    public void NoCommandDeclaresARole(string[] path)
    {
        Assert.DoesNotContain("Role required:", Help(path));
    }

    // The query language lives entirely inside --query, and `search fields`
    // returns field names and aliases only — never the syntax — so the AND/OR
    // semantics, the quoting rule and the year: operators are learnable from
    // this section or nowhere.
    [Theory]
    [InlineData("tag:forest tag:swamp")]
    [InlineData("Quote a multi-word value")]
    [InlineData("1999-2005")]
    public void SearchDocumentsItsQuerySyntax(string expected)
    {
        Assert.Contains(expected, Help(["search"]));
    }

    // The three caveats a caller cannot infer: the other result sets ignore
    // --limit, a filter switches off page-text search, and a typo'd prefix is
    // searched literally instead of refused.
    [Fact]
    public void SearchDocumentsTheCapsTheFiltersAndTheSilentTypo()
    {
        var output = Help(["search"]);
        Assert.Contains("book_matches", output);
        Assert.Contains("text:", output);
        Assert.Contains("literally", output);
    }
}
