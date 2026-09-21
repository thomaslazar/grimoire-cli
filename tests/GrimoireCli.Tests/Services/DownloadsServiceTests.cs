using GrimoireCli.Api;
using GrimoireCli.Configuration;
using GrimoireCli.Services;
using Microsoft.Kiota.Abstractions;

namespace GrimoireCli.Tests.Services;

/// <summary>
/// The archive endpoint selects among eleven scopes through seven query
/// parameters. A regeneration that renamed one would send a parameter the
/// server ignores — and for a scope flag that means silently exporting a wider
/// slice of the library than the caller asked for, which no response shape
/// would reveal.
/// </summary>
public class DownloadsServiceTests
{
    private static GrimoireApiClient Client() =>
        new(new AppConfig { Server = "http://example.test", AccessToken = "t" });

    private static string Uri(RequestInformation info)
    {
        info.PathParameters["baseurl"] = "http://example.test";
        return info.URI.AbsoluteUri;
    }

    [Fact]
    public void ArchiveResolvesToItsOwnPath()
    {
        var info = new DownloadsService(Client()).ArchiveRequest(
            "system", null, null, null, null, null, null);
        Assert.Contains("/api/downloads/archive", Uri(info));
    }

    [Fact]
    public void EveryScopeParameterKeepsItsWireName()
    {
        var info = new DownloadsService(Client()).ArchiveRequest(
            "tag_folder", "tar.gz", "s1", "core", "session-prep", "book", "errata");
        var uri = Uri(info);
        Assert.Contains("type=tag_folder", uri);
        Assert.Contains("fmt=tar.gz", uri);
        Assert.Contains("id=s1", uri);
        Assert.Contains("category=core", uri);
        Assert.Contains("tag=session-prep", uri);
        Assert.Contains("resource_type=book", uri);
        Assert.Contains("folder=errata", uri);
    }

    // An omitted scope flag must not appear at all: the server picks its own
    // default for fmt, and a stray empty parameter could change the scope.
    [Fact]
    public void OmittedScopeParametersAreAbsent()
    {
        var uri = Uri(new DownloadsService(Client()).ArchiveRequest(
            "system", null, "s1", null, null, null, null));
        Assert.Contains("type=system", uri);
        Assert.Contains("id=s1", uri);
        Assert.DoesNotContain("fmt=", uri);
        Assert.DoesNotContain("category=", uri);
        Assert.DoesNotContain("tag=", uri);
        Assert.DoesNotContain("resource_type=", uri);
        Assert.DoesNotContain("folder=", uri);
    }
}
