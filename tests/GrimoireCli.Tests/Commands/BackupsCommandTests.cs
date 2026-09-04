using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class BackupsCommandTests
{
    private static string Help(string[] path, bool full = false) =>
        HelpRenderer.Render(BackupsCommand.Create(), path, full);

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("download")]
    public void EveryCommandDeclaresTheAdminRole(string leaf)
    {
        var output = Help(["backups", leaf]);
        Assert.Contains("Role required:", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void TheGroupHostsTheFourVerbs()
    {
        var names = BackupsCommand.Create().Subcommands.Select(c => c.Name).ToArray();
        Assert.Contains("list", names);
        Assert.Contains("create", names);
        Assert.Contains("delete", names);
        Assert.Contains("download", names);
    }

    [Fact]
    public void DeleteRequiresAnId()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["delete"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["delete", "--id", "abc"]).Errors);
    }

    [Fact]
    public void DownloadRequiresBothIdAndOutput()
    {
        Assert.NotEmpty(BackupsCommand.Create().Parse(["download", "--id", "abc"]).Errors);
        Assert.NotEmpty(BackupsCommand.Create().Parse(["download", "--output", "-"]).Errors);
        Assert.Empty(BackupsCommand.Create().Parse(["download", "--id", "abc", "--output", "-"]).Errors);
    }

    // The archive is the whole recovery path, because the API has no restore
    // endpoint. An agent must not be left to infer a round trip that is absent.
    [Fact]
    public void DownloadWarnsThereIsNoRestoreEndpoint()
    {
        var output = Help(["backups", "download"]);
        Assert.Contains("no restore", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDocumentsTheReadLockAndTheConflict()
    {
        var output = Help(["backups", "create"]);
        Assert.Contains("lock", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("409", output);
    }

    [Fact]
    public void DeleteDocumentsThatItIsIrreversibleAndAnswersNoBody()
    {
        var output = Help(["backups", "delete"]);
        Assert.Contains("no body", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be undone", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("download")]
    public void EveryCommandWithABodyCarriesAResponseShape(string leaf)
    {
        Assert.Contains("Response shape:", Help(["backups", leaf], full: true));
    }
}
