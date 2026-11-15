using System.Text.Json.Nodes;
using GrimoireCli.Api;
using GrimoireCli.Commands;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

public class CampaignGmScreenServiceTests
{
    private const string Base = "http://example.test/api/campaigns/c-1/gm-screen";

    private static CampaignGmScreenService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    public static TheoryData<string, string, string> Routes() => new()
    {
        { "get", "GET", Base },
        { "party", "GET", Base + "/party" },
        { "export", "GET", Base + "/export" },
        { "sources", "GET", Base + "/sources" },
        { "presets", "GET", Base + "/presets" },
        { "reset", "POST", Base + "/reset" },
        { "layout", "PUT", Base + "/layout" },
        { "import", "POST", Base + "/import" },
        { "copy", "POST", Base + "/copy" },
        { "apply-preset", "POST", Base + "/presets/apply" },
    };

    private static RequestInformation Request(string verb)
    {
        var s = Service();
        return verb switch
        {
            "get" => s.GetRequest("c-1"),
            "party" => s.PartyRequest("c-1"),
            "export" => s.ExportRequest("c-1"),
            "sources" => s.SourcesRequest("c-1"),
            "presets" => s.PresetsRequest("c-1"),
            "reset" => s.ResetRequest("c-1", "scene"),
            "layout" => s.LayoutRequest("c-1", "{\"panels\":[]}"),
            "import" => s.ImportRequest("c-1", "{\"panels\":[]}", false),
            "copy" => s.CopyRequest("c-1", "c-2", false),
            "apply-preset" => s.ApplyPresetRequest("c-1", "p-1", null, false),
            _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EachRequestHitsItsRouteWithItsMethod(string verb, string method, string uri)
        => Assert.Equal((method, uri), Of(Request(verb)));

    [Fact]
    public void ImportReplacesByDefaultAndKeepsTheDocument()
    {
        var body = JsonNode.Parse(CampaignGmScreenService.BuildImportBody("{\"panels\":[]}", false))!;
        Assert.True(body["replace"]!.GetValue<bool>());
        Assert.NotNull(body["document"]!["panels"]);
    }

    [Fact]
    public void ImportAppendSendsReplaceFalse()
        => Assert.False(JsonNode.Parse(CampaignGmScreenService.BuildImportBody("{\"panels\":[]}", true))!["replace"]!.GetValue<bool>());

    [Theory]
    [InlineData("[1]")]
    [InlineData("{not json")]
    public void ImportRejectsANonObjectDocument(string document)
        => Assert.Throws<BodyInputException>(() => CampaignGmScreenService.BuildImportBody(document, false));
}
