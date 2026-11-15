using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

public class LeagueMembersCommandTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        LeagueMembersCommands.Create().Parse(args);

    [Fact]
    public void AddNeedsAUserId() => Assert.NotEmpty(Parse("add", "--id", "l").Errors);

    [Fact]
    public void RemoveNeedsAUserId() => Assert.NotEmpty(Parse("remove", "--id", "l").Errors);

    [Fact]
    public void ArmyTakesNoArmyId() => Assert.Empty(Parse("army", "--id", "l", "--user-id", "u").Errors);

    [Fact]
    public void ArmyWarnsTheArmyMustBeTheirs()
        => Assert.Contains("not theirs", HelpRenderer.Render(LeagueMembersCommands.Create(), ["members", "army"], full: false));
}
