using System.CommandLine;
using System.Text.Json;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// Pins each bulk command's registered <c>AddResponseExample&lt;T&gt;()</c> to the
/// wire property its exit-code reader (<c>BulkExit</c>/<c>ScanExit</c> via
/// <c>HasItems</c>/<c>ReadStringProperty</c>) keys on. A command wired to the
/// wrong response model — the batch-tag commands used <c>TagsResponse</c>
/// instead of <c>BulkTagResult</c> — renders a "Response shape" sample that
/// omits the very field the exit code depends on; this fails whenever that
/// happens again.
/// </summary>
public class ResponseShapeExitCodeAlignmentTests
{
    [Fact]
    public void ResponseShapeIncludesTheExitCodeReadersProperty()
    {
        var cases = new (Command Command, string[] Path, string Property)[]
        {
            (SystemsCommand.Create(), ["systems", "batch-update"], "errors"),
            (SystemsCommand.Create(), ["systems", "batch-tag"], "errors"),
            (BooksCommand.Create(), ["books", "batch-update"], "errors"),
            (BooksCommand.Create(), ["books", "batch-tag"], "errors"),
            (AddonsCommand.Create(), ["addons", "upgrade-all"], "failed"),
            (LibraryCommand.Create(), ["library", "rescan"], "status"),
        };
        foreach (var (command, path, property) in cases)
        {
            var label = string.Join(' ', path);
            var output = HelpRenderer.Render(command, path, full: true);
            const string marker = "Response shape:\n";
            var start = output.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{label}: no Response shape section registered");
            start += marker.Length;
            var end = output.IndexOf("\n\n", start, StringComparison.Ordinal);
            var block = string.Join('\n', output[start..end].Split('\n').Select(line => line[2..]));
            var root = JsonDocument.Parse(block).RootElement;
            Assert.True(root.TryGetProperty(property, out _),
                $"{label}: response sample is missing '{property}'");
        }
    }
}
