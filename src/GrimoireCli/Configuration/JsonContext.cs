using System.Text.Json.Serialization;
using GrimoireCli.Configuration;

// Namespace deliberately kept as GrimoireCli.Models despite living in
// Configuration/ — keeps every existing `using GrimoireCli.Models;` and
// `AppJsonContext.Default.*` call site working with no churn.
namespace GrimoireCli.Models;

// Source-generated serialization: required under Native AOT, where reflection-based
// System.Text.Json is trimmed away. Every type that crosses the JSON boundary must
// be registered here or it fails at runtime, not at build time.
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(SavedFile))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AppJsonContext : JsonSerializerContext;
