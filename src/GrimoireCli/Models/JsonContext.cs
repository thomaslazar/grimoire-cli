using System.Text.Json.Serialization;
using GrimoireCli.Configuration;

namespace GrimoireCli.Models;

// Source-generated serialization: required under Native AOT, where reflection-based
// System.Text.Json is trimmed away. Every type that crosses the JSON boundary must
// be registered here or it fails at runtime, not at build time.
[JsonSerializable(typeof(GameSystemSummary))]
[JsonSerializable(typeof(GameSystemDetail))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(List<GameSystemSummary>))]
[JsonSerializable(typeof(Book))]
[JsonSerializable(typeof(PublisherEntry))]
[JsonSerializable(typeof(LinkEntry))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AppJsonContext : JsonSerializerContext;
