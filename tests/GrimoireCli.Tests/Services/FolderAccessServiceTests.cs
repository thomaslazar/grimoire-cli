using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class FolderAccessServiceTests
{
    private static FolderAccessService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListResolvesToTheFolderAccessRoute()
        => Assert.Equal("http://example.test/api/settings/folder-access", Uri(Service().ListRequest()));

    [Theory]
    [InlineData("gm")]
    [InlineData("admin")]
    public void SetPutsPathAndLevel(string level)
    {
        var info = Service().SetRequest("books/Shadowrun/4 DE", level);
        Assert.Equal(Method.PUT, info.HttpMethod);
        Assert.Equal("http://example.test/api/settings/folder-access", Uri(info));
        using var reader = new StreamReader(info.Content);
        var body = reader.ReadToEnd();
        Assert.Contains("\"path\":\"books/Shadowrun/4 DE\"", body);
        Assert.Contains($"\"level\":\"{level}\"", body);
    }

    [Fact]
    public void RemoveSendsThePathAsAQuery()
    {
        var info = Service().RemoveRequest("books/x");
        Assert.Equal(Method.DELETE, info.HttpMethod);
        Assert.StartsWith("http://example.test/api/settings/folder-access?path=books", Uri(info));
    }
}
