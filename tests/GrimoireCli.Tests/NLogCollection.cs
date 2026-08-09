namespace GrimoireCli.Tests;

// LogSetup.Configure and DebugHttpHandler both mutate global NLog state
// (LogManager.Configuration). Tests that touch either must not run in
// parallel with each other or they race on that shared state — hence this
// collection fixture, which xunit uses to serialize any test class tagged
// [Collection("NLog")].
[CollectionDefinition("NLog")]
public class NLogCollection
{
    // Empty marker — disables parallel execution of tests in this collection.
}
