using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>Pins each league-books request to its route, method and body.</summary>
public class LeagueBooksServiceTests
{
    private const string Base = "http://example.test/api/leagues/l-1/books";

    private static LeagueBooksService Service() =>
        new(new GrimoireApiClient(new AppConfig { Server = "http://example.test", AccessToken = "t" }));

    private static (string Method, string Uri) Of(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return (info.HttpMethod.ToString().ToUpperInvariant(), info.URI.AbsoluteUri);
    }

    private static string BodyOf(RequestInformation info)
    {
        using var reader = new StreamReader(info.Content);
        return reader.ReadToEnd().Replace(" ", "");
    }

    [Fact]
    public void ListGetsBooks() => Assert.Equal(("GET", Base), Of(Service().ListRequest("l-1")));

    [Fact]
    public void SearchSendsQAsTheQuery() => Assert.Equal(("GET", Base + "/search?q=codex"), Of(Service().SearchRequest("l-1", "codex")));

    [Fact]
    public void AddPostsToBooks() => Assert.Equal(("POST", Base), Of(Service().AddRequest("l-1", "b-1", null, null)));

    [Fact]
    public void UpdatePatchesTheResource() => Assert.Equal(("PATCH", Base + "/r-1"), Of(Service().UpdateRequest("l-1", "r-1", "public", null)));

    [Fact]
    public void RemoveDeletesTheResource() => Assert.Equal(("DELETE", Base + "/r-1"), Of(Service().RemoveRequest("l-1", "r-1")));

    [Fact]
    public void ReorderPutsToOrder() => Assert.Equal(("PUT", Base + "/order"), Of(Service().ReorderRequest("l-1", ["r-1"])));

    [Fact]
    public void AddWithoutVisibilityLeavesItUnset()
    {
        var body = BodyOf(Service().AddRequest("l-1", "b-1", null, null));
        Assert.Contains("\"book_id\":\"b-1\"", body);
        Assert.DoesNotContain("visibility", body);
        Assert.DoesNotContain("shared_user_ids", body);
    }

    [Fact]
    public void AddSendsGivenVisibilityAndShares()
    {
        var body = BodyOf(Service().AddRequest("l-1", "b-1", "private", ["u-1"]));
        Assert.Contains("\"visibility\":\"private\"", body);
        Assert.Contains("\"shared_user_ids\":[\"u-1\"]", body);
    }

    [Fact]
    public void UpdateBodyCarriesOnlyTheFlagsGiven()
    {
        var visibilityOnly = BodyOf(Service().UpdateRequest("l-1", "r-1", "public", null));
        Assert.Contains("\"visibility\":\"public\"", visibilityOnly);
        Assert.DoesNotContain("shared_user_ids", visibilityOnly);
        var sharesOnly = BodyOf(Service().UpdateRequest("l-1", "r-1", null, ["u-1"]));
        Assert.Contains("\"shared_user_ids\":[\"u-1\"]", sharesOnly);
        Assert.DoesNotContain("visibility", sharesOnly);
    }
}
