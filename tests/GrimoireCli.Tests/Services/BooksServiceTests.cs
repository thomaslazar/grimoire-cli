using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Pins the book reading routes to the paths their generated builders produce.
/// The page number is a path segment rather than a query parameter, which is
/// the part a client regeneration could move without any test noticing.
/// </summary>
public class BooksServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void EachReadingRouteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Books["b1"];
        Assert.Equal("http://example.test/api/books/b1/toc", Uri(api.Toc.ToGetRequestInformation()));
        Assert.Contains("/api/books/b1/page/7/text", Uri(api.Page[7].Text.ToGetRequestInformation()));
        Assert.Contains("/api/books/b1/page/7/words", Uri(api.Page[7].Words.ToGetRequestInformation()));
    }

    // The page number belongs in the path; a regeneration that moved it to a
    // query parameter would read page 1 of every book instead of the one asked
    // for, and every other assertion here would still pass.
    [Fact]
    public void ThePageNumberIsAPathSegmentNotAQueryParameter()
    {
        var uri = Uri(Client().Api.Api.Books["b1"].Page[7].Text.ToGetRequestInformation());
        Assert.DoesNotContain("?", uri);
        Assert.Contains("/page/7/", uri);
    }

    [Fact]
    public void TheBinaryGettersResolveToTheirOwnPaths()
    {
        var api = Client().Api.Api.Books["b1"];
        Assert.Contains("/api/books/b1/file", Uri(api.File.ToGetRequestInformation()));
        Assert.Contains("/api/books/b1/page/7", Uri(api.Page[7].ToGetRequestInformation()));
    }

    // width is the only query parameter books page takes; a rename would render
    // at the server's default while the caller believes otherwise.
    [Fact]
    public void BooksPageSendsWidthAsAQueryParameter()
    {
        var info = Client().Api.Api.Books["b1"].Page[7].ToGetRequestInformation(
            c => c.QueryParameters.Width = 900);
        Assert.Contains("width=900", Uri(info));
    }

    [Fact]
    public void OmittedWidthSendsNoQueryString()
    {
        Assert.DoesNotContain("width=", Uri(Client().Api.Api.Books["b1"].Page[7].ToGetRequestInformation()));
    }

    [Fact]
    public void EachCoverRouteResolvesToItsOwnPath()
    {
        var api = Client().Api.Api.Books["b1"];
        Assert.Contains("/api/books/b1/cover", Uri(api.Cover.ToDeleteRequestInformation()));
        Assert.Contains("/api/books/b1/cover", Uri(api.Cover.ToPostRequestInformation(
            GrimoireCli.Services.BooksService.BuildCoverUploadBody(new byte[] { 1 }, "cover.png"))));
        Assert.Contains("/api/books/b1/cover/from-source", Uri(api.Cover.FromSource.ToPostRequestInformation(
            new Generated.Models.BookCoverSourceIn { SourceType = "map", SourceId = "m1" })));
    }

    // The server reads the part by name; a rename would upload nothing and the
    // failure would look like a validation error rather than a client bug.
    [Fact]
    public void TheUploadPartIsNamedFile()
    {
        var body = GrimoireCli.Services.BooksService.BuildCoverUploadBody(new byte[] { 1, 2, 3 }, "cover.png");
        Assert.Equal(new byte[] { 1, 2, 3 }, body.GetPartValue<byte[]>("file", "cover.png"));
    }
}
