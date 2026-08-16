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
[JsonSerializable(typeof(BookSummary))]
[JsonSerializable(typeof(BookDetail))]
[JsonSerializable(typeof(BookListResponse))]
[JsonSerializable(typeof(GameSystemRef))]
[JsonSerializable(typeof(ScanStatus))]
[JsonSerializable(typeof(ScanTriggerResult))]
[JsonSerializable(typeof(PublisherEntry))]
[JsonSerializable(typeof(LinkEntry))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(BulkUpdateResult))]
[JsonSerializable(typeof(BulkTagResult))]
[JsonSerializable(typeof(BulkError))]
[JsonSerializable(typeof(AddonInstalled))]
[JsonSerializable(typeof(AddonAvailable))]
[JsonSerializable(typeof(AddonListResponse))]
[JsonSerializable(typeof(AddonSettings))]
[JsonSerializable(typeof(RefreshResult))]
[JsonSerializable(typeof(UpgradeAllResult))]
[JsonSerializable(typeof(AddonUpgrade))]
[JsonSerializable(typeof(AddonUpgradeFailure))]
[JsonSerializable(typeof(MetadataSource))]
[JsonSerializable(typeof(MetadataSourceList))]
[JsonSerializable(typeof(MetadataCandidate))]
[JsonSerializable(typeof(MetadataSearchResult))]
[JsonSerializable(typeof(MetadataFieldDiff))]
[JsonSerializable(typeof(MetadataFetchResult))]
[JsonSerializable(typeof(CleanupCounts))]
[JsonSerializable(typeof(CleanupResult))]
[JsonSerializable(typeof(SavedFile))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AppJsonContext : JsonSerializerContext;
