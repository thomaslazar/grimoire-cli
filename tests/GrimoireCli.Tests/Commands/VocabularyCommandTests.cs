using System.CommandLine;
using GrimoireCli.Commands;

namespace GrimoireCli.Tests.Commands;

/// <summary>
/// The five vocabulary groups, each built by its own command class. The
/// no-role-section assertion is the point: their route is deliberately untagged
/// (Depends(get_current_user), not require_not_guest), so a later reflexive
/// AddRoleRequired must fail here.
/// </summary>
public class VocabularyCommandTests
{
    public static TheoryData<string> Vocabularies() =>
        new("genres", "licenses", "parent-systems", "system-families", "dice-materials");

    /// <summary>
    /// Resolves a group through the same factory Program.cs calls, so a class
    /// left unregistered there is not silently covered by these tests.
    /// </summary>
    private static Command Group(string name) => name switch
    {
        "genres" => GenresCommand.Create(),
        "licenses" => LicensesCommand.Create(),
        "parent-systems" => ParentSystemsCommand.Create(),
        "system-families" => SystemFamiliesCommand.Create(),
        "dice-materials" => DiceMaterialsCommand.Create(),
        _ => throw new ArgumentException($"Unknown vocabulary '{name}'.", nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void EachGroupIsNamedForItsVocabulary(string name)
    {
        Assert.Equal(name, Group(name).Name);
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
    public void ListHelpRendersOptionsThenExamples(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        var examples = output.IndexOf("Examples:", StringComparison.Ordinal);
        var options = output.IndexOf("Options:", StringComparison.Ordinal);
        Assert.True(options >= 0, "Options section missing");
        Assert.True(examples > options, "Examples (Bottom) must render after Options");
    }

    // Only the two vocabularies with something an agent cannot read off the
    // response sample carry Notes. The other three carry none, deliberately —
    // restating a visible field is what the help-text rules forbid.
    [Theory]
    [InlineData("genres", false)]
    [InlineData("licenses", false)]
    [InlineData("system-families", false)]
    [InlineData("parent-systems", true)]
    [InlineData("dice-materials", true)]
    public void OnlyVocabulariesWithARealCaveatCarryNotes(string name, bool expected)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        var notes = output.IndexOf("Notes:", StringComparison.Ordinal);
        Assert.Equal(expected, notes >= 0);
        if (expected)
        {
            var options = output.IndexOf("Options:", StringComparison.Ordinal);
            Assert.True(options > notes, "Notes (Top) must render before Options");
        }
    }

    // How a value is submitted, and what happens when it does not match, are the
    // writer's business. Cross-references run consumer -> producer, so that advice
    // lives on systems update / books update and must not drift back here.
    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void ListHelpCarriesNoConsumerAdvice(string name)
    {
        var output = HelpRenderer.Render(Group(name), [name, "list"], full: false);
        Assert.DoesNotContain("Submit the name", output);
        Assert.DoesNotContain("Nothing validates", output);
        Assert.DoesNotContain("systems update", output);
        Assert.DoesNotContain("books update", output);
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
    public void ParentSystemsWarnTheyShipEmpty()
    {
        var output = HelpRenderer.Render(Group("parent-systems"), ["parent-systems", "list"], full: false);
        Assert.Contains("ships empty", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiceMaterialsNoteTheirDefaultGroup()
    {
        var output = HelpRenderer.Render(Group("dice-materials"), ["dice-materials", "list"], full: false);
        Assert.Contains("group is Custom when unset", output);
    }

    // Cross-references are one-way, consumer -> producer, so `update` is where the
    // pointer at the vocabularies has to live.
    [Fact]
    public void SystemsUpdateNamesTheVocabularyCommands()
    {
        var output = HelpRenderer.Render(SystemsCommand.Create(), ["systems", "update"], full: false);
        Assert.Contains("genres list", output);
        Assert.Contains("dice-materials list", output);
        Assert.Contains("Submit the name, not the id", output);
        Assert.Contains("stored as written", output);
    }

    [Fact]
    public void BooksUpdateNamesOnlyTheVocabulariesItAccepts()
    {
        var output = HelpRenderer.Render(BooksCommand.Create(), ["books", "update"], full: false);
        Assert.Contains("genres list", output);
        Assert.Contains("licenses list", output);
        Assert.Contains("Submit the name, not the id", output);
        Assert.DoesNotContain("dice-materials list", output);
    }
}
