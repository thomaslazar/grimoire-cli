namespace GrimoireCli.Tests;

// Console.SetOut and ConsoleOutput.Pretty are both process-global. Tests that
// touch either must not run in parallel with each other or they race on that
// shared state — hence this collection fixture, which xunit uses to serialize
// any test class tagged [Collection("Console")].
[CollectionDefinition("Console")]
public class ConsoleCollection
{
    // Empty marker — disables parallel execution of tests in this collection.
}
