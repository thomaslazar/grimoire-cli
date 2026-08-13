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
            var line = Assert.Single(target.Logs);
            Assert.StartsWith("WARN ", line);
            Assert.Contains(path, line);
            Assert.Contains("not valid JSON", line);
            Assert.Contains("grimoire-cli login", line);
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
            Assert.Empty(target.Logs);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
