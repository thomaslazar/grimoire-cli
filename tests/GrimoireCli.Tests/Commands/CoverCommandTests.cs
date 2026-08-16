using System.CommandLine;
using GrimoireCli.Commands;
using GrimoireCli.Services;

namespace GrimoireCli.Tests.Commands;

public class CoverCommandTests
{
    private static string Help(string leaf, bool full) =>
        HelpRenderer.Render(SystemsCommand.Create(), ["systems", "cover", leaf], full);

    [Theory]
    [InlineData("get")]
    [InlineData("upload")]
    [InlineData("delete")]
    public void EveryCoverVerbExists(string leaf) => Assert.Contains(leaf, Help(leaf, full: false));

    [Theory]
    [InlineData("upload")]
    [InlineData("delete")]
    public void WritesAreGmOrAdmin(string leaf) => Assert.Contains("gm or admin", Help(leaf, full: false));

    [Fact]
    public void ReadCarriesNoRoleTag() =>
        Assert.DoesNotContain("Role required:", Help("get", full: false));

    // Folder art beating an upload is the caveat that makes an apparently
    // successful upload look like it did nothing.
    [Fact]
    public void UploadWarnsThatFolderArtWins()
    {
        Assert.Contains("Folder cover art still wins", Help("upload", full: false));
    }

    [Fact]
    public void GetExplainsThe404Fallback()
    {
        var output = Help("get", full: false);
        Assert.Contains("cover_book_id", output);
        Assert.Contains("books thumbnail", output);
    }

    [Fact]
    public void DeleteSaysFolderArtSurvives()
    {
        Assert.Contains("library-managed", Help("delete", full: false));
    }

    [Fact]
    public void GetRequiresAnOutputAndUploadRequiresAFile()
    {
        Assert.NotEmpty(SystemsCommand.Create().Parse(["cover", "get", "--id", "1"]).Errors);
        Assert.NotEmpty(SystemsCommand.Create().Parse(["cover", "upload", "--id", "1"]).Errors);
    }

    // The server rejects on content type, so the CLI must send one. Unknown
    // extensions fall through to octet-stream rather than being refused here —
    // deciding which types are acceptable is the server's job.
    [Theory]
    [InlineData("art.png", "image/png")]
    [InlineData("art.jpg", "image/jpeg")]
    [InlineData("art.JPEG", "image/jpeg")]
    [InlineData("art.webp", "image/webp")]
    [InlineData("art.gif", "image/gif")]
    [InlineData("art.bmp", "application/octet-stream")]
    [InlineData("art", "application/octet-stream")]
    public void MimeComesFromTheExtension(string file, string expected) =>
        Assert.Equal(expected, SystemsService.MimeForExtension(file));

    // Reproduces the unguarded File.ReadAllBytesAsync: a missing --file must
    // report BodyInputException the same way JsonBodyInput.Read does, not throw
    // raw and crash the process before any client call is made.
    [Fact]
    public async Task UploadReportsAMissingFileInsteadOfThrowingRaw()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"cover-missing-{Guid.NewGuid():N}.png");
        var service = new SystemsService(null!);
        var ex = await Assert.ThrowsAsync<BodyInputException>(() => service.UploadCoverAsync("sys", missing));
        Assert.Contains("Could not read", ex.Message);
        Assert.Contains(missing, ex.Message);
    }
}
