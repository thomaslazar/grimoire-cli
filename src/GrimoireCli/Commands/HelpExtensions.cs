using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace GrimoireCli.Commands;

public enum HelpSectionPosition { Top, Bottom }

/// <summary>
/// Adds custom sections (Notes, Examples, response shapes) to help output.
/// Top-positioned sections render before the default layout; Bottom-positioned
/// sections render after Options in registration order.
/// </summary>
public static class HelpExtensions
{
    private record Section(string Title, string[] Lines, HelpSectionPosition Position, bool IsShape = false);

    // ConcurrentDictionary so parallel xUnit test classes building independent
    // command trees can mutate the outer map without corrupting it. Each Command
    // instance is touched by exactly one thread, so the per-command List does
    // not need its own synchronization.
    private static readonly ConcurrentDictionary<Command, List<Section>> CommandSections = new();

    public static void AddHelpSection(this Command command, string title, params string[] lines)
        => command.AddHelpSection(title, HelpSectionPosition.Bottom, lines);

    public static void AddRoleRequired(this Command command, string role)
        => command.AddHelpSection("Role required", HelpSectionPosition.Top, role);

    public static void AddHelpSection(this Command command, string title, HelpSectionPosition position, params string[] lines)
    {
        var sections = CommandSections.GetOrAdd(command, _ => new List<Section>());
        sections.Add(new Section(title, lines, position));
    }

    /// <summary>
    /// Registers a response-shape sample, hidden behind --help-full. Grimoire's
    /// OpenAPI spec types every response as an empty schema, so these samples are
    /// written by hand from observed responses rather than generated.
    /// </summary>
    public static void AddShapeSection(this Command command, string title, params string[] lines)
    {
        var sections = CommandSections.GetOrAdd(command, _ => new List<Section>());
        sections.Add(new Section(title, lines, HelpSectionPosition.Bottom, IsShape: true));
    }

    public static void AddExamples(this Command command, params string[] examples)
        => command.AddHelpSection("Examples", HelpSectionPosition.Bottom, examples);

    public static int GetExampleCount(this Command command)
    {
        if (!CommandSections.TryGetValue(command, out var sections))
            return 0;
        return sections
            .Where(s => s.Title == "Examples")
            .SelectMany(s => s.Lines)
            .Count();
    }

    /// <summary>
    /// Replaces the default <see cref="HelpAction"/> on the root command's
    /// <see cref="HelpOption"/> with a wrapper that writes Top-positioned
    /// sections before the default layout and Bottom-positioned sections
    /// after it. The HelpOption is recursive by default, so the wrapper also
    /// fires for every subcommand <c>--help</c>.
    /// </summary>
    public static void UseCustomHelpSections(this RootCommand root)
    {
        var helpOption = root.Options.OfType<HelpOption>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "RootCommand has no HelpOption — cannot install CustomHelpAction.");
        if (helpOption.Action is not HelpAction defaultAction)
            throw new InvalidOperationException(
                $"HelpOption.Action is {helpOption.Action?.GetType().Name ?? "null"}, expected HelpAction.");
        helpOption.Action = new CustomHelpAction(defaultAction, includeShapes: false);
        var fullHelp = new Option<bool>("--help-full")
        {
            Description = "Show full help including response-shape blocks.",
            Recursive = true,
            Action = new CustomHelpAction(defaultAction, includeShapes: true),
        };
        root.Options.Add(fullHelp);
    }

    private sealed class CustomHelpAction : SynchronousCommandLineAction
    {
        private readonly HelpAction _inner;
        private readonly bool _includeShapes;
        public CustomHelpAction(HelpAction inner, bool includeShapes)
        {
            _inner = inner;
            _includeShapes = includeShapes;
        }

        public override int Invoke(ParseResult parseResult)
        {
            var command = parseResult.CommandResult.Command;
            var output = parseResult.InvocationConfiguration.Output;
            WriteSections(command, output, HelpSectionPosition.Top, _includeShapes);
            var rc = _inner.Invoke(parseResult);
            WriteSections(command, output, HelpSectionPosition.Bottom, _includeShapes);
            if (!_includeShapes) WriteShapeHint(command, output);
            return rc;
        }
    }

    private static void WriteShapeHint(Command command, TextWriter output)
    {
        if (!CommandSections.TryGetValue(command, out var sections)) return;
        if (!sections.Any(s => s.IsShape)) return;
        output.WriteLine("Run --help-full to see response shape(s).");
        output.WriteLine();
    }

    private static void WriteSections(Command command, TextWriter output, HelpSectionPosition position, bool includeShapes)
    {
        if (!CommandSections.TryGetValue(command, out var sections)) return;
        foreach (var section in sections.Where(s => s.Position == position))
        {
            if (section.IsShape && !includeShapes) continue;
            output.WriteLine($"{section.Title}:");
            foreach (var line in section.Lines)
                output.WriteLine($"  {line}");
            output.WriteLine();
        }
    }
}
