using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>Pins each armies request to its route and method.</summary>
public class ArmiesServiceTests
{
    private const string Base = "http://example.test/api/armies";

    private static ArmiesService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "list", "GET", Base },
        { "get", "GET", Base + "/a-1" },
        { "create", "POST", Base },
        { "update", "PATCH", Base + "/a-1" },
        { "delete", "DELETE", Base + "/a-1" },
        { "import", "POST", Base + "/import" },
        { "export", "GET", Base + "/a-1/export?format=roster" },
        { "publications", "GET", Base + "/a-1/publications" },
        { "progress", "PUT", Base + "/a-1/units/u-1/progress" },
        { "games", "GET", Base + "/games" },
        { "factions", "GET", Base + "/games/s-1/factions" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "list" => s.ListRequest(),
            "get" => s.GetRequest("a-1"),
            "create" => s.CreateRequest("{\"name\":\"N\"}"),
            "update" => s.UpdateRequest("a-1", "{\"name\":\"N\"}"),
            "delete" => s.DeleteRequest("a-1"),
            "import" => s.ImportRequest(new MemoryStream(), "list.rosz", "N"),
            "export" => s.ExportRequest("a-1", "roster"),
            "publications" => s.PublicationsRequest("a-1"),
            "progress" => s.ProgressRequest("a-1", "u-1", 1, [], null),
            "games" => s.GamesRequest(),
            "factions" => s.FactionsRequest("s-1"),
            _ => throw new ArgumentException(verb),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void RequestHitsItsRoute(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void ProgressBodyAlwaysSendsEveryField()
    {
        var body = ArmiesService.BuildProgressBody(0, [], null);
        Assert.Equal(0, body.Experience);
        Assert.Empty(body.Honours!);
        Assert.Equal("", body.Notes);
    }
}
