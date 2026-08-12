using System.Text.Json;
using GrimoireCli.Models;

namespace GrimoireCli.Tests.Models;

public class MeResponseTests
{
    [Fact]
    public void DeserializesEveryFieldTheServerSends()
    {
        const string json = """
        {
          "id": "3f1c8e5a-0000-4000-8000-000000000001",
          "username": "admin",
          "display_name": "Admin",
          "email": "admin@example.test",
          "role": "admin",
          "allow_explicit": true,
          "campaign_access": true,
          "oidc_linked": false
        }
        """;
        var me = JsonSerializer.Deserialize(json, AppJsonContext.Default.MeResponse)!;
        Assert.Equal("3f1c8e5a-0000-4000-8000-000000000001", me.Id);
        Assert.Equal("admin", me.Username);
        Assert.Equal("Admin", me.DisplayName);
        Assert.Equal("admin@example.test", me.Email);
        Assert.Equal("admin", me.Role);
        Assert.True(me.AllowExplicit);
        Assert.True(me.CampaignAccess);
        Assert.False(me.OidcLinked);
    }

    // display_name and email are nullable columns; a bare account sends nulls.
    [Fact]
    public void ToleratesNullDisplayNameAndEmail()
    {
        const string json = """
        {"id":"x","username":"gm","display_name":null,"email":null,"role":"gm",
         "allow_explicit":false,"campaign_access":false,"oidc_linked":true}
        """;
        var me = JsonSerializer.Deserialize(json, AppJsonContext.Default.MeResponse)!;
        Assert.Null(me.DisplayName);
        Assert.Null(me.Email);
        Assert.Equal("gm", me.Role);
    }

    // A field a newer Grimoire adds must be ignored, not throw — reads stay lenient.
    [Fact]
    public void IgnoresAnUnknownField()
    {
        const string json = """{"username":"admin","role":"admin","future_field":"x"}""";
        var me = JsonSerializer.Deserialize(json, AppJsonContext.Default.MeResponse)!;
        Assert.Equal("admin", me.Username);
    }
}
