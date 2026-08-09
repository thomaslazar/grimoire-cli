using System.Text.Json;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class ResponseExamplesJsonValidTest
{
    [Fact]
    public void EverySampleParsesAsJson()
    {
        Assert.NotEmpty(ResponseExamples.All);
        foreach (var (type, sample) in ResponseExamples.All)
        {
            var ex = Record.Exception(() => JsonDocument.Parse(sample));
            Assert.True(ex is null, $"Sample for {type.Name} is not valid JSON: {ex?.Message}\n{sample}");
        }
    }
}
