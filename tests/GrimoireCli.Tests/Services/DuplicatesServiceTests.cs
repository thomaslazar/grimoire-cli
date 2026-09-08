using GrimoireCli.Api;
using GrimoireCli.Configuration;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// Thirteen near-identical sends is exactly where a copy-paste reaches the wrong
/// endpoint, so every path is pinned. The query names are pinned too: a client
/// regeneration that renamed one would leave the server ignoring the filter and
/// answering 200 with unfiltered data.
/// </summary>
public class DuplicatesServiceTests
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
        var api = Client().Api.Api.Duplicates;
        Assert.Equal("http://example.test/api/duplicates/link",
            Uri(api.Link.ToPostRequestInformation(new Generated.Models.LinkRequest())));
        Assert.Equal("http://example.test/api/duplicates/promote",
            Uri(api.Promote.ToPostRequestInformation(new Generated.Models.PromoteRequest())));
        Assert.Equal("http://example.test/api/duplicates/unlink",
            Uri(api.Unlink.ToPostRequestInformation(new Generated.Models.UnlinkRequest())));
        Assert.Equal("http://example.test/api/duplicates/merge-metadata",
            Uri(api.MergeMetadata.ToPostRequestInformation(new Generated.Models.MergeMetadataRequest())));
        Assert.Equal("http://example.test/api/duplicates/scan",
            Uri(api.Scan.ToPostRequestInformation(new Generated.Models.ScanRequest())));
        Assert.Equal("http://example.test/api/duplicates/cancel-scan",
            Uri(api.CancelScan.ToPostRequestInformation()));
        Assert.Equal("http://example.test/api/duplicates/scan-status",
            Uri(api.ScanStatus.ToGetRequestInformation()));
        Assert.Equal("http://example.test/api/duplicates/dismiss",
            Uri(api.Dismiss.ToPostRequestInformation(new Generated.Models.DismissRequest())));
    }

    [Fact]
    public void TheItemDeleteCarriesBothPathParameters()
    {
        var info = Client().Api.Api.Duplicates.Items["book"]["abc"]
            .ToDeleteRequestInformation(new Generated.Models.DeleteItemRequest());
        Assert.Equal("http://example.test/api/duplicates/items/book/abc", Uri(info));
    }

    [Fact]
    public void TheDismissalDeleteCarriesItsPathParameter()
    {
        var info = Client().Api.Api.Duplicates.Dismissals["d1"].ToDeleteRequestInformation();
        Assert.Equal("http://example.test/api/duplicates/dismissals/d1", Uri(info));
    }

    [Fact]
    public void CompareSendsResourceTypeAndRepeatedIds()
    {
        var info = Client().Api.Api.Duplicates.Compare.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = "book";
            c.QueryParameters.Ids = ["a", "b"];
        });
        var uri = Uri(info);
        Assert.Contains("resource_type=book", uri);
        // The compare URL template has no explode modifier on ids, so Kiota
        // joins the array with commas rather than repeating the key.
        Assert.Contains("ids=a,b", uri);
    }

    [Fact]
    public void GroupsSendsEveryQueryParameterByItsWireName()
    {
        var info = Client().Api.Api.Duplicates.Groups.ToGetRequestInformation(c =>
        {
            c.QueryParameters.ResourceType = "book";
            c.QueryParameters.MinConfidence = 0.5;
            c.QueryParameters.Limit = 10;
            c.QueryParameters.Offset = 20;
        });
        var uri = Uri(info);
        Assert.Contains("resource_type=book", uri);
        Assert.Contains("min_confidence=0.5", uri);
        Assert.Contains("limit=10", uri);
        Assert.Contains("offset=20", uri);
    }

    [Fact]
    public void DismissalsSendsResourceType()
    {
        var info = Client().Api.Api.Duplicates.Dismissals.ToGetRequestInformation(c =>
            c.QueryParameters.ResourceType = "book");
        Assert.Contains("resource_type=book", Uri(info));
    }

    // reparent_to is a composed-type wrapper because it is Optional upstream.
    // Assigning through the wrapper only when the flag was given is what keeps
    // --reparent-to "" distinguishable from the flag being absent: "" promotes
    // every variant to standalone, absent means "refuse if there are any".
    [Fact]
    public void TheDeleteBodyOmitsReparentToUnlessGiven()
    {
        Assert.Null(GrimoireCli.Services.DuplicatesService
            .BuildDeleteItemBody(true, null).ReparentTo);
        var cleared = GrimoireCli.Services.DuplicatesService
            .BuildDeleteItemBody(true, "").ReparentTo;
        Assert.NotNull(cleared);
        Assert.Equal("", cleared!.String);
    }

    [Fact]
    public void TheUnlinkBodyOmitsParentIdUnlessGiven()
    {
        Assert.Null(GrimoireCli.Services.DuplicatesService
            .BuildUnlinkBody("book", ["a"], null).ParentId);
        var parent = GrimoireCli.Services.DuplicatesService
            .BuildUnlinkBody("book", [], "p1").ParentId;
        Assert.NotNull(parent);
        Assert.Equal("p1", parent!.String);
    }
}
