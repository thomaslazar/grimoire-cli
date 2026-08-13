using GrimoireCli.Configuration;
using NLog;
using NLog.Layouts;
using NLog.Targets;

namespace GrimoireCli.Tests.Configuration;

/// <summary>
/// A corrupt config is swallowed so it cannot kill every command, which means the
/// warning is the only thing telling the operator why their token stopped being
/// used. These assert that it names the file and the remedy — separated from
/// <see cref="ConfigManagerTests"/> so only the log-asserting tests pay for the
/// serialization that process-global NLog state forces.
/// </summary>
[Collection("NLog")]
public class ConfigManagerLoggingTests
{
    private static MemoryTarget ConfigureMemoryTarget()
    {
        var config = new NLog.Config.LoggingConfiguration();
        var target = new MemoryTarget("memory")
        {
            Layout = new SimpleLayout("${level:uppercase=true} ${message}")
        };
        config.AddTarget(target);
        config.AddRule(LogLevel.Warn, LogLevel.Fatal, target);
        LogManager.Configuration = config;
        return target;
    }

    [Fact]
    public void AnUnparseableConfigWarnsWithThePathAndTheRemedy()
    {
        var target = ConfigureMemoryTarget();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        try
        {
            new ConfigManager(path).Load();
            // Scoped to this test's own path: NLog's configuration is process-global,
            // so another class's warning can land in this target even under the
            // collection, and an unscoped Single() would fail on its arrival.
            var line = Assert.Single(target.Logs, l => l.Contains(path));
            Assert.StartsWith("WARN ", line);
            Assert.Contains("not valid JSON", line);
            Assert.Contains("grimoire-cli login", line);
            Assert.Contains($"{path}.corrupt", line);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void AReadableConfigWarnsAboutNothing()
    {
        var target = ConfigureMemoryTarget();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        var manager = new ConfigManager(path);
        try
        {
            manager.Save(new AppConfig { Server = "http://example.test" });
            manager.Load();
            Assert.DoesNotContain(target.Logs, l => l.Contains(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
