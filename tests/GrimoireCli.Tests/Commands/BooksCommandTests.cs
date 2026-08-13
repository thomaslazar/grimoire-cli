using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BooksCommandTests
{
    private static string RenderHelp(string[] path, bool full) => HelpRenderer.Render(BooksCommand.Create(), path, full);

    [Fact]
    public void ListDocumentsThePagingDefaultAndCap()
    {
        var output = RenderHelp(["books", "list"], full: false);
        Assert.Contains("--limit", output);
        Assert.Contains("default 100, max 500", output);
        Assert.Contains("--offset", output);
    }

    [Fact]
    public void ListShowsTheEnvelopeNotABareArray()
    {
        var output = RenderHelp(["books", "list"], full: true);
        Assert.Contains("Response shape:", output);
        Assert.Contains("\"total\":", output);
        Assert.Contains("\"books\":", output);
    }

    [Fact]
    public void GetShowsTheDetailShapeWithItsNestedSystem()
    {
        var output = RenderHelp(["books", "get"], full: true);
        Assert.Contains("\"game_system\":", output);
        Assert.Contains("\"authors\":", output);
    }

    // Both reads are guarded by require_not_guest or nothing at all, which per
    // CLAUDE.md is the default and carries no tag.
    [Fact]
    public void ReadsCarryNoRoleTag()
    {
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "list"], full: false));
        Assert.DoesNotContain("Role required:", RenderHelp(["books", "get"], full: false));
    }
}
