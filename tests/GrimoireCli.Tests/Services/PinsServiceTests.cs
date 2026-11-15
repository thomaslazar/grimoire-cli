using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class PinsServiceTests
{
    private static PinsService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    [Fact]
    public void ListIsAGet() => Assert.Equal(("GET", "http://example.test/api/pins"), Of(Service().ListRequest(null)));

    [Fact]
    public void ListFiltersByCollection() => Assert.Equal(("GET", "http://example.test/api/pins?collection=maps"), Of(Service().ListRequest("maps")));

    [Fact]
    public void AddIsAPost() => Assert.Equal(("POST", "http://example.test/api/pins"), Of(Service().AddRequest("maps", "x")));

    [Fact]
    public void RemoveIsADelete() => Assert.Equal(("DELETE", "http://example.test/api/pins/k-1"), Of(Service().RemoveRequest("k-1")));
}
