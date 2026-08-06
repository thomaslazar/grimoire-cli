using System.Text.Json.Serialization;

namespace GrimoireCli.Configuration;

public class AppConfig
{
    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }
}
