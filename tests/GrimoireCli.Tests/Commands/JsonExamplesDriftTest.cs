using System.Diagnostics;

namespace GrimoireCli.Tests.Commands;

public class JsonExamplesDriftTest
{
    [Fact]
    public void CheckedInFileMatchesFreshGeneration()
    {
        var repoRoot = RepoRoot();
        var checkedInPath = Path.Combine(repoRoot, "src", "GrimoireCli", "Commands", "JsonExamples.g.cs");
        Assert.True(File.Exists(checkedInPath), $"Missing generated file: {checkedInPath}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"json-examples-{Guid.NewGuid():N}.g.cs");
        try
        {
            var toolProject = Path.Combine(repoRoot, "tools", "GenerateJsonExamples", "GenerateJsonExamples.csproj");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "run", "--project", toolProject, "--", tempPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi)!;
            proc.WaitForExit();
            Assert.True(proc.ExitCode == 0,
                $"Generator exited {proc.ExitCode}\nstdout: {proc.StandardOutput.ReadToEnd()}\nstderr: {proc.StandardError.ReadToEnd()}");
            var expected = File.ReadAllText(checkedInPath).Replace("\r\n", "\n");
            var actual = File.ReadAllText(tempPath).Replace("\r\n", "\n");
            Assert.True(expected == actual,
                "JsonExamples.g.cs is stale. Regenerate with: " +
                "dotnet run --project tools/GenerateJsonExamples -- src/GrimoireCli/Commands/JsonExamples.g.cs");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "GrimoireCli.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
