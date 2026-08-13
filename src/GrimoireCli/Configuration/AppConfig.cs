using System.Text.Json.Serialization;

namespace GrimoireCli.Configuration;

public class AppConfig
{
    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    // Written by the CLI, not by the operator: when the server version was last
    // checked, and what it was. config set does not accept either.
    [JsonPropertyName("lastVersionCheck")]
    public DateTimeOffset? LastVersionCheck { get; set; }

    [JsonPropertyName("lastServerVersion")]
    public string? LastServerVersion { get; set; }
}
