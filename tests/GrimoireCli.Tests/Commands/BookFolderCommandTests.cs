using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BookFolderCommandTests
{
    // Matches how the other command tests build a root — see MeCommandTests.
    private static RootCommand Root() => new() { SystemsCommand.Create() };

    [Fact]
    public void ListAcceptsAnId()
        => Assert.Empty(Root().Parse("systems book-folders list --id sys-1").Errors);

    [Fact]
    public void ListRequiresAnId()
        => Assert.NotEmpty(Root().Parse("systems book-folders list").Errors);

    [Fact]
    public void SetAcceptsAnIdAndStdin()
        => Assert.Empty(Root().Parse("systems book-folders set --id sys-1 --stdin").Errors);

    [Fact]
    public void SetAcceptsAnIdAndInput()
        => Assert.Empty(Root().Parse("systems book-folders set --id sys-1 --input body.json").Errors);

    // Exactly one body source: RequireExactlyOneSource rejects both and neither.
    [Fact]
    public void SetRejectsBothBodySources()
        => Assert.NotEmpty(Root().Parse("systems book-folders set --id sys-1 --stdin --input body.json").Errors);

    [Fact]
    public void SetRejectsNoBodySource()
        => Assert.NotEmpty(Root().Parse("systems book-folders set --id sys-1").Errors);

    [Fact]
    public void DeleteAcceptsAnIdAndPath()
        => Assert.Empty(Root().Parse("systems book-folders delete --id sys-1 --path sys-1/core/errata").Errors);

    // The path is the only thing that identifies the row, so it cannot default.
    [Fact]
    public void DeleteRequiresAPath()
        => Assert.NotEmpty(Root().Parse("systems book-folders delete --id sys-1").Errors);

    [Fact]
    public void DeleteRequiresAnId()
        => Assert.NotEmpty(Root().Parse("systems book-folders delete --path sys-1/core/errata").Errors);

    // There is no --token tier anywhere in this CLI any more.
    [Fact]
    public void NoTokenOverride()
        => Assert.NotEmpty(Root().Parse("systems book-folders list --id sys-1 --token t").Errors);
}
