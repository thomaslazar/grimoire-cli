using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class BookLinksServiceTests
{
    private static BookLinksService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ListResolvesToTheBooksSystemsRoute()
    {
        var info = Service().ListRequest("b1");
        Assert.Equal(Method.GET, info.HttpMethod);
        Assert.Equal("http://example.test/api/books/b1/systems", Uri(info));
    }

    [Fact]
    public void AddPostsTheSystemId()
    {
        var info = Service().AddRequest("b1", "s1");
        Assert.Equal(Method.POST, info.HttpMethod);
        Assert.Equal("http://example.test/api/books/b1/systems", Uri(info));
        using var reader = new StreamReader(info.Content);
        Assert.Equal("{\"system_id\":\"s1\"}", reader.ReadToEnd());
    }

    [Fact]
    public void RemoveDeletesTheOneLink()
    {
        var info = Service().RemoveRequest("b1", "s1");
        Assert.Equal(Method.DELETE, info.HttpMethod);
        Assert.Equal("http://example.test/api/books/b1/systems/s1", Uri(info));
    }
}
