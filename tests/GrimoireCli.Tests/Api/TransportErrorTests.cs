using GrimoireCli.Api;

namespace GrimoireCli.Tests.Api;

/// <summary>
/// The message half of transport-failure handling, split out so it is testable:
/// the caller logs it and calls Environment.Exit, which would take the test host
/// with it. Same split as VersionWarning.
/// </summary>
public class TransportErrorTests
{
    private const string Server = "http://grimoire.example.com";

    // A server that is down is the commonest failure there is, and the address is
    // the one thing the caller needs — a stale config and a dead server are
    // otherwise indistinguishable.
    [Fact]
    public void AnUnreachableServerNamesTheAddressAndTheCause()
    {
        var message = GrimoireApiClient.TransportErrorMessage(
            new HttpRequestException("Connection refused (127.0.0.1:1)"), Server);

        Assert.Contains(Server, message);
        Assert.Contains("Connection refused", message);
        Assert.DoesNotContain("   at ", message);
    }

    // A timeout and an unreachable host need different remedies, so they must not
    // read the same. Raising a command's budget fixes one and nothing fixes the
    // other.
    [Fact]
    public void ATimeoutSaysSoRatherThanClaimingTheServerIsUnreachable()
    {
        var message = GrimoireApiClient.TransportErrorMessage(new TaskCanceledException(), Server);

        Assert.Contains(Server, message);
        Assert.Contains("timed out", message);
        Assert.DoesNotContain("Cannot reach", message);
    }

    // The message is what a human or an agent reads on stderr; a trailing frame
    // or an empty tail would mean the exception text leaked in raw.
    [Fact]
    public void TheMessageIsASingleReadableLine()
    {
        var message = GrimoireApiClient.TransportErrorMessage(
            new HttpRequestException("Connection refused"), Server);

        Assert.DoesNotContain("\n", message);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }
}
