using GrimoireCli.Services;

namespace GrimoireCli.Tests.Services;

public class AddonsServiceTests
{
    // An omitted flag must stay absent from the PATCH body: the server ignores
    // what is not sent, and sending a value would clear or set a field the
    // caller never mentioned.
    [Fact]
    public void OmittedFlagsLeaveTheUpdateBodyEmpty()
    {
        var body = AddonsService.BuildUpdateBody(enabled: null, scriptApproved: null);
        Assert.Null(body.Enabled);
        Assert.Null(body.ScriptApproved);
    }

    [Fact]
    public void GivenFlagsReachTheBodyThroughTheComposedWrapper()
    {
        var body = AddonsService.BuildUpdateBody(enabled: false, scriptApproved: true);
        Assert.False(body.Enabled!.Boolean);
        Assert.True(body.ScriptApproved!.Boolean);
    }
}
