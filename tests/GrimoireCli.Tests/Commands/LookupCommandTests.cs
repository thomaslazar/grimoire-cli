using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// The five vocabulary groups. The no-role-section assertion is the point: these
/// are the first commands whose route is deliberately untagged
/// (Depends(get_current_user), not require_not_guest), so a later reflexive
/// AddRoleRequired must fail here.
/// </summary>
public class LookupCommandTests
{
    public static TheoryData<string> Vocabularies() =>
        new("genres", "licenses", "parent-systems", "system-families", "dice-materials");

    private static Command Group(string name) =>
        LookupCommands.Create().Single(c => c.Name == name);

    [Fact]
    public void CreateYieldsTheFiveVocabularyGroups()
    {
        Assert.Equal(
            ["genres", "licenses", "parent-systems", "system-families", "dice-materials"],
            LookupCommands.Create().Select(c => c.Name).ToArray());
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachGroupHasExactlyOneListSubcommand(string name)
    {
        var group = Group(name);
        Assert.Equal(["list"], group.Subcommands.Select(c => c.Name).ToArray());
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpRendersNotesThenExamplesThenOptions(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        var notes = output.IndexOf("Notes:", StringComparison.Ordinal);
        var examples = output.IndexOf("Examples:", StringComparison.Ordinal);
        var options = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(notes >= 0, "Notes section missing");
        Assert.True(options > notes, "Notes must render before Options");
        Assert.True(examples > options, "Examples must render after Options");
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpCarriesTheSharedCaveats(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        Assert.Contains("Submit name, not id", output);
        Assert.Contains("Nothing validates a written value", output);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpCarriesAResponseShape(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: true);
        Assert.Contains("Response shape:", output);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpHasNoRoleSection(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: true);
        Assert.DoesNotContain("Role required:", output);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListParsesAndAcceptsServer(string name)
    {
        var group = Group(name);
        Assert.Empty(group.Parse(["list"]).Errors);
        Assert.Empty(group.Parse(["list", "--server", "http://example.test"]).Errors);
    }

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void AnUnknownSubcommandErrors(string name)
    {
        Assert.NotEmpty(Group(name).Parse(["create", "--name", "x"]).Errors);
    }

    // The response shape is the only place the id/name distinction the Notes warn
    // about is visible, so it must actually show both.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ResponseShapeShowsBothIdAndName(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: true);
        var start = output.IndexOf("Response shape:", StringComparison.Ordinal);
        var block = output[start..];
        Assert.Contains("\"id\"", block);
        Assert.Contains("\"name\"", block);
    }

    [Fact]
    public void GenresNoteTheirTiering()
    {
        var output = HelpRenderer.Render(Group("genres"), ["genres", "list"], full: false);
        Assert.Contains("parent_id", output);
    }

    [Fact]
    public void ParentSystemsWarnTheyShipEmpty()
    {
        var output = HelpRenderer.Render(Group("parent-systems"), ["parent-systems", "list"], full: false);
        Assert.Contains("ships empty", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiceMaterialsNoteTheirGroupField()
    {
        var output = HelpRenderer.Render(Group("dice-materials"), ["dice-materials", "list"], full: false);
        Assert.Contains("group", output);
    }
}
