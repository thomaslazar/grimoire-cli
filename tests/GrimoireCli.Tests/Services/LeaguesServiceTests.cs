using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>Pins each leagues request to its route and method.</summary>
public class LeaguesServiceTests
{
    private const string Base = "http://example.test/api/leagues";

    private static LeaguesService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "list", "GET", Base },
        { "get", "GET", Base + "/l-1" },
        { "create", "POST", Base },
        { "update", "PATCH", Base + "/l-1" },
        { "delete", "DELETE", Base + "/l-1" },
        { "eligible", "GET", Base + "/l-1/eligible-members" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "list" => s.ListRequest(),
            "get" => s.GetRequest("l-1"),
            "create" => s.CreateRequest("{\"name\":\"L\"}"),
            "update" => s.UpdateRequest("l-1", "{\"name\":\"L\"}"),
            "delete" => s.DeleteRequest("l-1"),
            "eligible" => s.EligibleRequest("l-1"),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void CreateSendsTheRawBodyUnchanged()
    {
        using var reader = new StreamReader(Service().CreateRequest("{\"name\":\"L\"}").Content);
        Assert.Equal("{\"name\":\"L\"}", reader.ReadToEnd());
    }
}
