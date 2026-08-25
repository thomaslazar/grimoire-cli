using GrimoireCli.Output;

namespace GrimoireCli.Tests.Output;

public class ConsoleOutputTests
{
    [Fact]
    public void WriteRawJson_PassesBytesThroughUnchanged_ByDefault()
    {
        var previous = ConsoleOutput.Pretty;
        var stdout = new StringWriter();
        var original = Console.Out;
        try
        {
            ConsoleOutput.Pretty = false;
            Console.SetOut(stdout);
            ConsoleOutput.WriteRawJson("{\"b\":1,\"a\":null}");
        }
        finally
        {
            Console.SetOut(original);
            ConsoleOutput.Pretty = previous;
        }
        // Key order, explicit null and compactness are the server's, not ours.
        Assert.Equal("{\"b\":1,\"a\":null}", stdout.ToString().Trim());
    }

    [Fact]
    public void WriteRawJson_ReindentsWhenPretty()
    {
        var previous = ConsoleOutput.Pretty;
        var stdout = new StringWriter();
        var original = Console.Out;
        try
        {
            ConsoleOutput.Pretty = true;
            Console.SetOut(stdout);
            ConsoleOutput.WriteRawJson("{\"b\":1,\"a\":null}");
        }
        finally
        {
            Console.SetOut(original);
            ConsoleOutput.Pretty = previous;
        }
        var output = stdout.ToString();
        Assert.Contains("\n", output);
        Assert.Contains("\"b\": 1", output);
    }

    [Fact]
    public void WriteRawJson_PassesUnparseableBodyThrough_WhenPretty()
    {
        var previous = ConsoleOutput.Pretty;
        var stdout = new StringWriter();
        var original = Console.Out;
        try
        {
            ConsoleOutput.Pretty = true;
            Console.SetOut(stdout);
            ConsoleOutput.WriteRawJson("<html>");
        }
        finally
        {
            Console.SetOut(original);
            ConsoleOutput.Pretty = previous;
        }
        Assert.Equal("<html>", stdout.ToString().Trim());
    }

    [Fact]
    public void WriteRawJson_LeavesNonAsciiUnescaped_WhenPretty()
    {
        var previous = ConsoleOutput.Pretty;
        var stdout = new StringWriter();
        var original = Console.Out;
        try
        {
            ConsoleOutput.Pretty = true;
            Console.SetOut(stdout);
            ConsoleOutput.WriteRawJson("{\"name\":\"Anf\\u00e4ngerbox <x> & y\"}");
        }
        finally
        {
            Console.SetOut(original);
            ConsoleOutput.Pretty = previous;
        }
        Assert.Contains("Anfängerbox <x> & y", stdout.ToString());
    }
}
