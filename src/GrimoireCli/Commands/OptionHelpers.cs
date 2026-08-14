using System.CommandLine;

namespace GrimoireCli.Commands;

/// <summary>Option factories shared across command groups.</summary>
public static class OptionHelpers
{
    /// <summary>
    /// A string option restricted to a fixed value set, rejected at parse time and
    /// offered as shell completions. A server that silently falls back to a default
    /// when given an unknown value would otherwise return differently-shaped data
    /// with exit 0, so an unrecognised value is rejected here instead. The rendered
    /// help lists the values itself, so the description must not repeat them.
    /// </summary>
    public static Option<string?> Choice(string name, string description, string[] allowed)
    {
        var option = new Option<string?>(name) { Description = description };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !allowed.Contains(value))
                result.AddError($"'{value}' is not a valid value for {name}. Must be one of: {string.Join(", ", allowed)}");
        });
        option.CompletionSources.Add(allowed);
        return option;
    }
}
