using System.Text;
using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Api;

/// <summary>
/// The generated builder supplies the URL, method and path parameter; the body is
/// the caller's own bytes. These pin that the throwaway generated model used to
/// build the request never reaches the wire.
/// </summary>
public class RawBodyRequestTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static RequestInformation UpdateInfo(string id, string body)
    {
        var info = Client().Api.Api.Systems[id].ToPatchRequestInformation(
            new GrimoireCli.Generated.Models.GameSystemUpdate());
        info.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/json");
        return info;
    }

    [Fact]
    public void UsesPatchOnTheSystemPath()
    {
        var info = UpdateInfo("sys-1", "{}");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(Method.PATCH, info.HttpMethod);
        Assert.Equal("/api/systems/sys-1", info.URI.AbsolutePath);
    }

    [Fact]
    public void SendsTheCallersBytesVerbatim()
    {
        const string body = """{"system_family":"Shadowrun","description":"a \"quoted\" word"}""";
        var info = UpdateInfo("sys-1", body);
        using var reader = new StreamReader(info.Content!);
        Assert.Equal(body, reader.ReadToEnd());
    }

    [Fact]
    public void SendsJsonContentType()
    {
        var info = UpdateInfo("sys-1", "{}");
        Assert.Contains("application/json", info.Headers["Content-Type"]);
    }

    [Fact]
    public void APathParameterCannotEscapeItsSegment()
    {
        var info = UpdateInfo("../about", "{}");
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Contains("..%2Fabout", info.URI.AbsoluteUri);
    }

    [Fact]
    public void BatchUpdateUsesPostOnTheBulkPath()
    {
        var info = Client().Api.Api.Systems.Bulk.ToPostRequestInformation(
            new GrimoireCli.Generated.Models.GameSystemBulkUpdate());
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(Method.POST, info.HttpMethod);
        Assert.Equal("/api/systems/bulk", info.URI.AbsolutePath);
    }

    [Fact]
    public void BatchTagUsesPostOnTheBulkTagsPath()
    {
        var info = Client().Api.Api.Systems.Bulk.Tags.ToPostRequestInformation(
            new GrimoireCli.Generated.Models.BulkAddTags());
        info.PathParameters["baseurl"] = "http://example.test";
        Assert.Equal(Method.POST, info.HttpMethod);
        Assert.Equal("/api/systems/bulk/tags", info.URI.AbsolutePath);
    }
}
