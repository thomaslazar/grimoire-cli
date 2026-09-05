using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Ten near-identical sends is exactly where a copy-paste reaches the wrong
/// endpoint, so every path is pinned. The two partial-patch bodies are pinned
/// separately: a field present-but-null would stop the server leaving it alone.
/// </summary>
public class FilesServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void EachEndpointResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Files;
        Assert.Equal("http://example.test/api/files/browse", Uri(api.Browse.ToGetRequestInformation()));
        Assert.Equal("http://example.test/api/files/move", Uri(api.Move.ToPostRequestInformation(new Generated.Models.MoveRequest())));
        Assert.Equal("http://example.test/api/files/rename", Uri(api.Rename.ToPostRequestInformation(new Generated.Models.RenameRequest())));
        Assert.Equal("http://example.test/api/files/delete", Uri(api.DeletePath.ToPostRequestInformation(new Generated.Models.DeleteRequest())));
        Assert.Equal("http://example.test/api/files/folder", Uri(api.Folder.ToPostRequestInformation(new Generated.Models.CreateFolderRequest())));
        Assert.Equal("http://example.test/api/files/folder/markers", Uri(api.Folder.Markers.ToPutRequestInformation(new Generated.Models.MarkersRequest())));
        Assert.Equal("http://example.test/api/files/folder/scaffold", Uri(api.Folder.Scaffold.ToPostRequestInformation(new Generated.Models.ScaffoldRequest())));
        Assert.Equal("http://example.test/api/files/folder/contents?path=", Uri(api.Folder.Contents.ToGetRequestInformation()));
    }

    [Fact]
    public void TheUploadPathIsWhatTheBuilderProduces()
    {
        var body = new MultipartBody();
        body.AddOrReplacePart("file", "application/octet-stream", new byte[] { 1 }, "a.bin");
        Assert.Equal("http://example.test/api/files/upload",
            Uri(Client().Api.Api.Files.Upload.ToPostRequestInformation(body)));
    }

    [Fact]
    public void BrowseSendsPathAndLimitAsQueryParameters()
    {
        var info = Client().Api.Api.Files.Browse.ToGetRequestInformation(c =>
        {
            c.QueryParameters.Path = "books/D&D";
            c.QueryParameters.Limit = 50;
        });
        var uri = Uri(info);
        Assert.Contains("limit=50", uri);
        Assert.Contains("path=books", uri);
    }

    // markers is a partial patch: an omitted field must be absent from the body,
    // not present-and-null, or the server would stop leaving it alone.
    [Fact]
    public void OmittedMarkerFieldsAreAbsentFromTheBody()
    {
        var body = FilesService.BuildMarkersBody("books/X", null, null);
        Assert.Equal("books/X", body.Path);
        Assert.Null(body.ContainerKind);
        Assert.Null(body.Nsfw);
    }

    [Fact]
    public void GivenMarkerFieldsLandOnTheirWrapperBranches()
    {
        var body = FilesService.BuildMarkersBody("books/X", "parent", true);
        Assert.Equal("parent", body.ContainerKind?.String);
        Assert.True(body.Nsfw?.Boolean);
    }

    // Clearing a container kind is expressed as "", which must survive rather
    // than be treated as absent.
    [Fact]
    public void AnEmptyContainerKindSurvivesAsAnEmptyString()
    {
        var body = FilesService.BuildMarkersBody("books/X", "", null);
        Assert.NotNull(body.ContainerKind);
        Assert.Equal("", body.ContainerKind?.String);
    }

    [Fact]
    public void FalseIsSentForNsfwRatherThanTreatedAsAbsent()
    {
        var body = FilesService.BuildMarkersBody("books/X", null, false);
        Assert.NotNull(body.Nsfw);
        Assert.False(body.Nsfw?.Boolean);
    }

    [Fact]
    public void AnOmittedConfirmNameIsAbsentFromTheDeleteBody()
    {
        var body = FilesService.BuildDeleteBody("books/X", null, deleteFiles: false);
        Assert.Equal("books/X", body.Path);
        Assert.Null(body.ConfirmName);
        Assert.False(body.DeleteFiles);
    }

    [Fact]
    public void AGivenConfirmNameLandsOnTheStringBranch()
    {
        var body = FilesService.BuildDeleteBody("books/X", "X", deleteFiles: true);
        Assert.Equal("X", body.ConfirmName?.String);
        Assert.True(body.DeleteFiles);
    }
}
